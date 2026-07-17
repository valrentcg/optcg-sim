using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// THE K=1 DETERMINIZER (honest root world sampling). Runner-side — it takes the referee state because it
    /// needs the full RUNNABLE structure (board, DON, modifiers, battle, pending internals) to build a world
    /// the engine can search. But it NEVER keeps the referee's true hidden assignment: every zone hidden from
    /// the acting seat (the opponent's hand, both decks, both players' face-down Life, and the life card in
    /// flight when the seat is the attacker) is OVERWRITTEN with cards sampled from the LEGAL decklist pool
    /// under a derived seed. The true hidden CardIds and instance ids are discarded. So the search runs on a
    /// legal, concrete world that is consistent with everything the seat may observe and contains none of the
    /// secrets it may not — which is exactly what turns privacy-test's LAYER 2 secrecy green.
    ///
    /// ⚠ SCOPE: this is K=1 and pre-ledger. It samples the unseen pool uniformly, so it does NOT yet respect
    /// legally-ACQUIRED knowledge (a searched/known top card is reshuffled). That is the knowledge-ledger's
    /// job (design §2); until it lands, search-heavy decks are sampled too loosely. Requires OpenList (both
    /// decklists known) — the phase-1 assumption.
    /// </summary>
    public static class RootWorldSampler
    {
        // A shared empty correspondence for callers that search the world directly (via TurnPlanner) and never
        // translate a root ObservedAction — they don't need the surrogate/pendingRef links.
        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        /// <param name="buildLinks">When false, skip the per-world <see cref="Projection.Project"/> and return
        /// empty surrogate/pendingRef maps. The SAMPLED WORLD IS IDENTICAL either way — this only omits the
        /// (potentially unused) correspondence maps. Use false on a path that searches the world's GameState
        /// directly (e.g. <see cref="Planning.HonestPlannerBot"/>), which is O(cards) cheaper per world.</param>
        public static SearchWorld Determinize(GameState referee, KnowledgeState knowledge, int seed, bool buildLinks = true)
        {
            if (referee == null) throw new ArgumentNullException(nameof(referee));
            if (knowledge == null) throw new ArgumentNullException(nameof(knowledge));
            if (knowledge.Assumption != ListAssumption.OpenList || knowledge.OwnList == null || knowledge.OpponentList == null)
                throw new InvalidOperationException("Determinize (K=1, phase-1) requires OpenList with both decklists.");

            string seat = knowledge.Seat;
            // searchMode ⇒ empty EventLog/CommandHistory. The search world does not need them, and dropping
            // them removes the private log lines / hidden-id history entries that would otherwise ride along.
            var world = GameClone.Clone(referee, searchMode: true);   // runnable copy; all GAMEPLAY state preserved
            var rng = new Random(seed);
            int fresh = 0;
            string FreshId() => "det#" + seed + "#" + (fresh++);

            foreach (var kv in world.Players.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string owner = kv.Key;
                var p = kv.Value;
                bool own = owner == seat;
                var list = own ? knowledge.OwnList : knowledge.OpponentList;

                // Partition THIS owner's non-leader cards into what the SEAT legally knows (kept) vs what it
                // does not (resampled). Every card the owner has must land in exactly one bucket, so the pool
                // (decklist − known) and the hidden slots always balance — including cards pulled into a
                // transient zone (an active deck look, the life card in flight).
                var known = new List<CardInstance>();
                var hidden = new List<CardInstance>();

                foreach (var c in p.CharacterArea) if (c != null) known.Add(c);   // public board — known to all
                if (p.Stage != null) known.Add(p.Stage);
                known.AddRange(p.Trash);
                known.AddRange(p.Life.Where(c => c.FaceUp));                        // face-up life — public
                (own ? known : hidden).AddRange(p.Hand);                            // own hand known; opp hand hidden
                hidden.AddRange(p.Deck);                                            // deck order/identity hidden from all
                hidden.AddRange(p.Life.Where(c => !c.FaceUp));                      // face-down life hidden from all (incl. owner)

                // An active deck look holds this owner's cards out of the deck: the SEARCHER (its Seat) knows
                // them; anyone else does not. Both the still-to-arrange Cards and any already-Ordered cards are
                // out of the deck and must be bucketed, or the count check would throw on a legal state (or,
                // worse, a held-out card would keep its true identity).
                if (world.DeckLook != null && world.DeckLook.Seat == owner)
                {
                    if (world.DeckLook.Cards != null) (own ? known : hidden).AddRange(world.DeckLook.Cards);
                    if (world.DeckLook.Ordered != null) (own ? known : hidden).AddRange(world.DeckLook.Ordered);
                }

                // The life card in flight belongs to the defender: the defender knows it, the attacker does not.
                if (world.Battle?.RevealedLife != null && world.Battle.TargetSeat == owner)
                    (own ? known : hidden).Add(world.Battle.RevealedLife);

                var pool = DecklistMultiset(list);
                foreach (var c in known) RemoveOne(pool, c.CardId);

                // LEDGER: legally-KNOWN top-of-deck cards (a searched/arranged segment) are PLACED, not
                // resampled — otherwise the honest bot forgets what it legitimately searched (the rayleigh
                // problem). Fix them onto the top deck slots in order, remove them from the pool, and sample
                // only the remaining hidden slots. A segment card missing from the pool means the ledger is
                // inconsistent with the decklist ⇒ RemoveOne throws (fail loud).
                var knownTop = knowledge.Segments
                    .FirstOrDefault(s => s.Seat == owner && s.Zone == "deck" && s.Anchor == "Top")?.CardIds
                    ?? (IReadOnlyList<string>)Array.Empty<string>();
                int nk = Math.Min(knownTop.Count, p.Deck.Count);
                var segSlots = p.Deck.Take(nk).ToList();                 // the top nk deck slots (index 0 = top)
                for (int i = 0; i < nk; i++) RemoveOne(pool, knownTop[i]);

                int chars = p.CharacterArea.Count(c => c != null);
                int dlCards = world.DeckLook?.Seat == owner ? world.DeckLook.Cards?.Count ?? 0 : 0;
                int dlOrdered = world.DeckLook?.Seat == owner ? world.DeckLook.Ordered?.Count ?? 0 : 0;
                int revealed = world.Battle?.TargetSeat == owner && world.Battle.RevealedLife != null ? 1 : 0;
                string inventory = $"known={known.Count} hidden={hidden.Count} top={nk}; zones deck={p.Deck.Count} " +
                    $"hand={p.Hand.Count} life={p.Life.Count} chars={chars} stage={(p.Stage == null ? 0 : 1)} " +
                    $"trash={p.Trash.Count} deckLook={dlCards}+{dlOrdered} revealedLife={revealed}; " +
                    $"status={world.Status} turn={world.TurnNumber} phase={world.Phase} " +
                    $"activeChoice={(world.ActiveChoice == null ? "none" : world.ActiveChoice.Seat)} " +
                    $"pending={world.PendingEffects.Count};";
                var sampleSlots = hidden.Where(c => !segSlots.Contains(c)).ToList();
                if (sampleSlots.Count != pool.Count)
                    throw new InvalidOperationException(
                        $"Determinize: {owner} sample slots={sampleSlots.Count} but legal pool={pool.Count}; {inventory} " +
                        "(decklist/observation/ledger inconsistent — refusing to sample an illegal world).");

                // Placed known-top: keep the seat's legal identity; fresh instance id (deck cards carry no
                // surrogate, so the id is free to rewrite and the true id is still discarded).
                for (int i = 0; i < nk; i++) { segSlots[i].CardId = knownTop[i]; segSlots[i].InstanceId = FreshId(); }
                // Sample the rest; OVERWRITE identity AND instance id so the referee's true hidden assignment is gone.
                for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
                for (int i = 0; i < sampleSlots.Count; i++) { sampleSlots[i].CardId = pool[i]; sampleSlots[i].InstanceId = FreshId(); }
            }

            // Correspondences: project the SAMPLED world for the seat. Public/own-hand cards kept their referee
            // instance ids, so the surrogates match the observation the agent was given. Skipped when the caller
            // searches the world directly and needs no root-action translation.
            if (!buildLinks)
                return SearchWorld.FromDeterminized(world, seat, Empty, Empty);
            Projection.Project(world, knowledge, out var links);
            return SearchWorld.FromDeterminized(world, seat, links.SurrogateToInstanceId, links.PendingRefToEffectId);
        }

        private static List<string> DecklistMultiset(DeckDef list)
        {
            var pool = new List<string>();
            foreach (var (cardId, qty) in list.List)
            {
                if (cardId == list.Leader) continue;
                for (int i = 0; i < qty; i++) pool.Add(cardId);
            }
            return pool;
        }

        private static void RemoveOne(List<string> pool, string cardId)
        {
            int idx = pool.IndexOf(cardId);
            if (idx < 0)
                // A known card whose CardId is not (or no longer) in the decklist pool: a >quantity copy, a
                // token, or an OpenList mismatch. Silently skipping it lets the slot-count check pass while the
                // sampled multiset is ILLEGAL (an extra copy of a real card cancels an un-subtracted slot), so
                // fail loud instead — identity legality, not just counts.
                throw new InvalidOperationException(
                    $"Determinize: known card '{cardId}' is not accounted for in the decklist pool " +
                    "(>quantity copy, token, or wrong OpenList) — refusing to sample an illegal world.");
            pool.RemoveAt(idx);
        }
    }
}
