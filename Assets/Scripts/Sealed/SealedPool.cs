// One Piece TCG — Sealed / Pre-Release: the player's card pool, deck, and validation.
//
// Pure C#, no UnityEngine. A pool OWNS its pack history for its whole life — nothing is ever
// flattened away, so "show me what came out of pack 4" is always answerable. That is the point of
// keeping SealedPack objects rather than merging straight into a card-count dictionary.

using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    /// <summary>Why a deck is not yet legal, in the order a builder should surface them.</summary>
    public sealed class SealedValidation
    {
        public bool Ok;
        public int MainCount;
        public int RequiredCount;
        public bool HasLeader;
        public bool LeaderLegal;
        public bool WithinPool;
        public List<string> Problems = new List<string>();
    }

    /// <summary>Everything a sealed run is: the product, the seed, the packs as opened, the deck being
    /// built, and (optionally) the event it belongs to.</summary>
    public sealed class SealedPool
    {
        public string SetCode;
        public string Seed;
        public List<SealedPack> Packs = new List<SealedPack>();

        /// <summary>Chosen Leader (a card id from the pool), or null.</summary>
        public string LeaderId;
        /// <summary>Main deck: card id -> copies.</summary>
        public Dictionary<string, int> Deck = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public SealedProduct Product() => SealedCatalog.Find(SetCode);

        public static SealedPool Generate(SealedProduct product, string seed)
        {
            seed = PackGenerator.NormalizeSeed(seed);
            return new SealedPool
            {
                SetCode = product.SetCode,
                Seed = seed,
                Packs = PackGenerator.Open(product, seed),
            };
        }

        // ---- The pool -------------------------------------------------------------------------

        /// <summary>Every card owned, card id -> copies, aggregated across all packs. Parallels stack
        /// with their normal print: they are the same card for deckbuilding.</summary>
        public Dictionary<string, int> PoolCounts()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in Packs)
                foreach (var c in pack.Cards)
                    counts[c.CardId] = counts.TryGetValue(c.CardId, out var n) ? n + 1 : 1;
            return counts;
        }

        /// <summary>Distinct card ids in the pool, for the builder grid.</summary>
        public List<string> PoolCardIds() => PoolCounts().Keys.ToList();

        /// <summary>Leaders actually opened — a sealed deck's Leader must come from the pool.</summary>
        public List<string> AvailableLeaders() =>
            PoolCounts().Keys.Where(id => CardData.GetCard(id)?.Type == "leader")
                             .OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary>Copies still available to add (pool minus what is already in the deck). The Leader
        /// does not consume a main-deck copy.</summary>
        public int Remaining(string cardId)
        {
            var pool = PoolCounts();
            int owned = pool.TryGetValue(cardId, out var n) ? n : 0;
            int used = Deck.TryGetValue(cardId, out var d) ? d : 0;
            return Math.Max(0, owned - used);
        }

        public int InDeck(string cardId) => Deck.TryGetValue(cardId, out var n) ? n : 0;
        public int InPool(string cardId) { var p = PoolCounts(); return p.TryGetValue(cardId, out var n) ? n : 0; }

        // ---- Deck editing ---------------------------------------------------------------------

        /// <summary>Add up to <paramref name="count"/> copies, bounded by what is left in the pool and
        /// the product's copy limit. Returns how many were actually added.</summary>
        public int Add(string cardId, int count = 1)
        {
            var def = CardData.GetCard(cardId);
            if (def == null || def.Type == "leader") return 0;   // Leaders are set via LeaderId
            var product = Product();
            int maxCopies = product?.MaxCopies ?? 4;
            int have = InDeck(cardId);
            int room = Math.Min(Remaining(cardId), Math.Max(0, maxCopies - have));
            int add = Math.Max(0, Math.Min(count, room));
            if (add > 0) Deck[cardId] = have + add;
            return add;
        }

        /// <summary>Remove copies; removing the last one drops the key so the deck stays clean.</summary>
        public int Remove(string cardId, int count = 1)
        {
            int have = InDeck(cardId);
            int rm = Math.Max(0, Math.Min(count, have));
            if (rm <= 0) return 0;
            if (have - rm <= 0) Deck.Remove(cardId); else Deck[cardId] = have - rm;
            return rm;
        }

        public int AddAll(string cardId) => Add(cardId, int.MaxValue);
        public int RemoveAll(string cardId) => Remove(cardId, int.MaxValue);
        public void ClearDeck() { Deck.Clear(); LeaderId = null; }

        public int DeckCount() => Deck.Values.Sum();

        // ---- Validation -----------------------------------------------------------------------

        /// <summary>Sealed validation only — deliberately NOT the constructed rules. A sealed deck is
        /// 40 cards (not 50), its Leader must come from the pool, and every copy must be one the
        /// player actually opened. Format legality/ban lists do not apply to a prerelease pool.</summary>
        public SealedValidation Validate()
        {
            var product = Product();
            int required = product?.DeckSize ?? 40;
            var v = new SealedValidation
            {
                MainCount = DeckCount(),
                RequiredCount = required,
                HasLeader = !string.IsNullOrEmpty(LeaderId),
                WithinPool = true,
                LeaderLegal = true,
            };

            if (!v.HasLeader) v.Problems.Add("Choose a Leader from your pool.");
            else if (product != null && product.LeaderMustComeFromPool && !AvailableLeaders().Contains(LeaderId))
            {
                v.LeaderLegal = false;
                v.Problems.Add("Your Leader must be one you opened.");
            }

            if (v.MainCount != required)
                v.Problems.Add(v.MainCount < required
                    ? $"Add {required - v.MainCount} more card(s) — {v.MainCount}/{required}."
                    : $"Remove {v.MainCount - required} card(s) — {v.MainCount}/{required}.");

            var pool = PoolCounts();
            foreach (var kv in Deck)
            {
                int owned = pool.TryGetValue(kv.Key, out var n) ? n : 0;
                if (kv.Value > owned)
                {
                    v.WithinPool = false;
                    v.Problems.Add($"{CardData.GetCard(kv.Key)?.Name ?? kv.Key}: {kv.Value} in deck but only {owned} opened.");
                }
            }

            v.Ok = v.Problems.Count == 0;
            return v;
        }

        // ---- Statistics (the right-hand panel) ------------------------------------------------

        public sealed class Stats
        {
            public int Total, Characters, Events, Stages;
            public double AverageCost;
            public int NoCounter, Counter1000, Counter2000;
            public int Blockers, Triggers, Rush, DoubleAttack, Banish;
            public Dictionary<string, int> Colors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<int, int> Curve = new Dictionary<int, int>();   // cost -> count (6 = "6+")
        }

        /// <summary>Live deck statistics. Counts COPIES, not distinct cards — 4 Blockers means four
        /// cards in the deck, which is what a builder needs to show.</summary>
        public Stats DeckStats()
        {
            var s = new Stats();
            long costSum = 0;
            int costed = 0;

            foreach (var kv in Deck)
            {
                var def = CardData.GetCard(kv.Key);
                if (def == null) continue;
                int n = kv.Value;
                s.Total += n;

                switch (def.Type)
                {
                    case "character": s.Characters += n; break;
                    case "event": s.Events += n; break;
                    case "stage": s.Stages += n; break;
                }

                costSum += (long)def.Cost * n;
                costed += n;
                int bucket = Math.Min(6, Math.Max(0, def.Cost));
                s.Curve[bucket] = s.Curve.TryGetValue(bucket, out var cv) ? cv + n : n;

                if (def.Counter >= 2000) s.Counter2000 += n;
                else if (def.Counter >= 1000) s.Counter1000 += n;
                else s.NoCounter += n;

                if (HasKeyword(def, "Blocker")) s.Blockers += n;
                if (HasKeyword(def, "Rush")) s.Rush += n;
                if (HasKeyword(def, "Double Attack")) s.DoubleAttack += n;
                if (HasKeyword(def, "Banish")) s.Banish += n;
                if (!string.IsNullOrWhiteSpace(def.Trigger)) s.Triggers += n;

                foreach (var col in SplitColors(def.Color))
                    s.Colors[col] = s.Colors.TryGetValue(col, out var c) ? c + n : n;
            }

            s.AverageCost = costed == 0 ? 0 : (double)costSum / costed;
            return s;
        }

        /// <summary>Keyword test that uses the PARSED keyword list first and falls back to the effect
        /// text, so it matches what the engine actually grants rather than a raw substring scan.</summary>
        public static bool HasKeyword(CardDef def, string keyword)
        {
            if (def == null) return false;
            if (def.Keywords != null && def.Keywords.Any(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase)))
                return true;
            return (def.Effect ?? "").IndexOf("[" + keyword + "]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>"Red" / "Red/Green" -> individual colours. Multicolour cards count for each.</summary>
        public static IEnumerable<string> SplitColors(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) yield break;
            foreach (var part in color.Split('/'))
            {
                var t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        public static bool IsMulticolor(CardDef def) => SplitColors(def?.Color).Count() > 1;

        // ---- Export ---------------------------------------------------------------------------

        /// <summary>Plain-text decklist, same "count id" shape the deck importer already reads.</summary>
        public string ExportText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Sealed {SetCode} — seed {Seed}");
            if (!string.IsNullOrEmpty(LeaderId)) sb.AppendLine($"1 {LeaderId}");
            foreach (var kv in Deck.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.AppendLine($"{kv.Value} {kv.Key}");
            return sb.ToString();
        }

        /// <summary>Build the engine deck definition this pool's deck represents, so a sealed deck can
        /// be handed straight to GameEngine.CreateMatch without touching the constructed DeckStore.</summary>
        public DeckDef ToDeckDef(string name = "Sealed Deck")
        {
            var list = new List<(string cardId, int qty)>();
            if (!string.IsNullOrEmpty(LeaderId)) list.Add((LeaderId, 1));
            foreach (var kv in Deck.OrderBy(k => k.Key, StringComparer.Ordinal)) list.Add((kv.Key, kv.Value));
            return new DeckDef { Id = "sealed_" + SetCode + "_" + Seed, Name = name, Leader = LeaderId, List = list };
        }
    }
}
