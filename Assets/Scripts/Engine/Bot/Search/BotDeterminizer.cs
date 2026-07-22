using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// FAIR-INFORMATION DETERMINIZER for the Advanced (search) bot. The AI opponent must decide from the same
    /// knowledge a PLAYER has — its own hand, everything in play, both trashes, revealed Life, and each deck's
    /// KNOWN multiset (the decklist minus the visible cards) — but NOT which specific hidden card sits in which
    /// slot, nor any deck's order. The Advanced tier's search/rollout and Trigger simulation would otherwise
    /// read the true hidden state (the opponent's hand, both deck orders, face-down Life) and best-respond it,
    /// which is cheating.
    ///
    /// <para>This clones the true state and PERMUTES the CardIds among the zones hidden from the acting seat:
    /// the opponent's hand, BOTH decks (order), and BOTH players' face-down Life — a player does not know even
    /// their own face-down Life in OPTCG (it is placed off the top of the deck, unseen until damage reveals it)
    /// — plus the life card in flight when the seat is the ATTACKER. The per-player multiset is preserved (a
    /// legal, legitimately-known quantity), only the ARRANGEMENT is randomized.</para>
    ///
    /// <para>Two properties make it correct: (1) the hidden CardIds are canonically SORTED before the shuffle,
    /// so the sampled arrangement depends only on (multiset, seed) and NOT on the true arrangement — the bot
    /// cannot read the truth through the sample (noninterference). (2) Every card the seat legitimately sees
    /// (its own hand, all public zones, its own in-progress deck look, its own revealed Life) keeps its real
    /// InstanceId AND CardId, so any command the search returns still references real cards and applies to the
    /// true state.</para>
    ///
    /// <para>Scope: this is the shipped analogue of the Tools/Sim K=1 determinizer, minus the decklist/ledger
    /// plumbing — the hidden multiset is reconstructed from the state itself (it equals decklist − visible), so
    /// no external decklist is needed and an illegal sample is impossible. It does not yet PRESERVE a legally
    /// searched top-of-deck segment across a resolved look (the engine already drops that knowledge on look
    /// confirm), so search-heavy decks are sampled a touch loosely — acceptable for a first honest cut.</para>
    /// </summary>
    public static class BotDeterminizer
    {
        /// <summary>Return a clone of <paramref name="state"/> in which every zone hidden from <paramref
        /// name="seat"/> has had its cards' identities permuted (multiset preserved, arrangement randomized
        /// under <paramref name="seed"/>). Public/own-visible cards are untouched.</summary>
        public static GameState FairView(GameState state, string seat, int seed)
        {
            if (state == null) return null;
            var world = GameClone.Clone(state);
            if (world.Players == null) return world;
            var rng = new Random(seed);

            foreach (var owner in world.Players.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
            {
                var p = world.Players[owner];
                if (p == null) continue;
                bool own = owner == seat;

                // Collect the CardInstances the seat is NOT entitled to identify for this owner.
                var hidden = new List<CardInstance>();
                if (!own && p.Hand != null) hidden.AddRange(p.Hand.Where(c => c != null));  // opponent's hand
                if (p.Deck != null) hidden.AddRange(p.Deck.Where(c => c != null));           // deck order (either player)
                if (p.Life != null) hidden.AddRange(p.Life.Where(c => c != null && !c.FaceUp)); // face-down Life (either)

                // A deck look in progress: the SEARCHER (its Seat) sees its own looked-at cards; nobody else does.
                if (world.DeckLook != null && world.DeckLook.Seat == owner && !own)
                {
                    if (world.DeckLook.Cards != null) hidden.AddRange(world.DeckLook.Cards.Where(c => c != null));
                    if (world.DeckLook.Ordered != null) hidden.AddRange(world.DeckLook.Ordered.Where(c => c != null));
                }

                // The life card in flight belongs to the DEFENDER (Battle.TargetSeat): the defender may see it
                // (they reveal it to decide their Trigger), the attacker may not.
                if (world.Battle?.RevealedLife != null && world.Battle.TargetSeat == owner && !own)
                    hidden.Add(world.Battle.RevealedLife);

                if (hidden.Count <= 1) continue;

                // Canonicalize the multiset BEFORE shuffling so the result is independent of the true
                // arrangement (noninterference), then Fisher–Yates under the derived seed, then write the
                // shuffled CardIds back onto the (structurally-ordered) hidden slots. InstanceIds untouched.
                var ids = hidden.Select(c => c.CardId).ToList();
                ids.Sort(StringComparer.Ordinal);
                for (int i = ids.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (ids[i], ids[j]) = (ids[j], ids[i]); }
                for (int i = 0; i < hidden.Count; i++) hidden[i].CardId = ids[i];
            }
            return world;
        }

        /// <summary>A per-decision seed derived from PUBLIC scalars only, so it never encodes the hidden truth
        /// the sample is meant to conceal, yet varies across decisions (a fresh honest guess each time).</summary>
        public static int Seed(GameState state, string seat)
        {
            if (state == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + state.TurnNumber;
                h = h * 31 + StableHash(state.Phase);
                h = h * 31 + StableHash(state.ActiveSeat);
                h = h * 31 + StableHash(seat);
                h = h * 31 + state.EffectSequence;
                h = h * 31 + state.BattleSequence;
                h = h * 31 + state.PendingEffects.Count;
                foreach (var kv in state.Players.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var pl = kv.Value;
                    h = h * 31 + (pl.Hand?.Count ?? 0);
                    h = h * 31 + (pl.Deck?.Count ?? 0);
                    h = h * 31 + (pl.Life?.Count ?? 0);
                    h = h * 31 + (pl.Trash?.Count ?? 0);
                    h = h * 31 + (pl.CharacterArea?.Count(c => c != null) ?? 0);
                }
                return h;
            }
        }

        // A DETERMINISTIC string hash. string.GetHashCode() is randomized per-process in .NET Core, which
        // made the sample (and thus every rollout score that reads the fair view) vary run-to-run — the bot
        // played DIFFERENT games each launch, breaking A/B pairing and surfacing an intermittent decision-loop
        // hang. FNV-1a keeps the seed a pure function of the public state.
        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }
    }
}
