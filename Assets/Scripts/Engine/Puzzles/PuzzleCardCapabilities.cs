using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Read-only capability index over the complete loaded card database. Puzzle generation consumes unique
    /// card IDs, not artwork variants, and classifies mechanics from the same CardDef text the rules engine
    /// executes. The index also supplies conservative "skins": mechanically blank cards may be exchanged only
    /// for cards with identical color, type, cost, power, counter, keywords, features, effect, and Trigger.
    /// Non-blank cards are never substituted, because printed names and engine-specific ID handlers can be
    /// rules-relevant even when two texts appear identical.
    /// </summary>
    public static class PuzzleCardCapabilities
    {
        public sealed class Snapshot
        {
            public int UniqueCards;
            public int Leaders;
            public int Characters;
            public int Events;
            public int Stages;
            public int Blockers;
            public int Rush;
            public int DoubleAttack;
            public int Banish;
            public int Restand;
            public int KoRemoval;
            public int RestEffects;
            public int CounterEvents;
            public int LifeEffects;
            public int DonReactivation;
        }

        private static int _libraryVersion = -1;
        private static Snapshot _snapshot;
        private static Dictionary<string, string[]> _equivalents;

        public static Snapshot Current
        {
            get { Ensure(); return _snapshot; }
        }

        public static IReadOnlyList<string> EquivalentIds(CardDef card)
        {
            if (card == null) return Array.Empty<string>();
            Ensure();
            return _equivalents.TryGetValue(GameplaySignature(card), out var ids)
                ? ids
                : Array.Empty<string>();
        }

        /// <summary>
        /// Deterministically swaps safe role-equivalent cards after a certified recipe is built. This increases
        /// visual/card-recognition breadth without changing any solved power, cost, counter, keyword, effect,
        /// feature, or Trigger constraint.
        /// </summary>
        public static GameState ApplySafeVariety(GameState state, int seed)
        {
            if (state == null) return null;
            Ensure();
            var rng = new Rng(seed);
            foreach (var p in state.Players.Values)
            {
                Replace(p.Leader, ref rng);
                foreach (var c in p.CharacterArea) Replace(c, ref rng);
                foreach (var c in p.Hand) Replace(c, ref rng);
                foreach (var c in p.Life) Replace(c, ref rng);
                foreach (var c in p.Trash) Replace(c, ref rng);
                foreach (var c in p.Deck) Replace(c, ref rng);
                Replace(p.Stage, ref rng);
            }
            return state;
        }

        private static void Replace(CardInstance instance, ref Rng rng)
        {
            if (instance == null) return;
            var def = CardData.GetCard(instance.CardId);
            if (def == null) return;
            var ids = EquivalentIds(def);
            if (ids.Count <= 1) return;
            instance.CardId = ids[rng.Range(ids.Count)];
        }

        private static void Ensure()
        {
            // CardData is loaded once before Puzzle Mode starts. Count is an inexpensive version marker and
            // also lets headless tests rebuild the index after loading the JSON database.
            if (_snapshot != null && _libraryVersion == CardData.Library.Count) return;
            _libraryVersion = CardData.Library.Count;

            var cards = CardData.Library.Values
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
                .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .ToList();

            bool Has(CardDef c, string pattern) =>
                Regex.IsMatch((c.Effect ?? "") + "\n" + (c.Trigger ?? ""), pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            _snapshot = new Snapshot
            {
                UniqueCards = cards.Count,
                Leaders = cards.Count(c => Eq(c.Type, "leader")),
                Characters = cards.Count(c => Eq(c.Type, "character")),
                Events = cards.Count(c => Eq(c.Type, "event")),
                Stages = cards.Count(c => Eq(c.Type, "stage")),
                Blockers = cards.Count(c => Has(c, @"\[Blocker\]")),
                Rush = cards.Count(c => Has(c, @"\[Rush\]|gains \[Rush\]")),
                DoubleAttack = cards.Count(c => Has(c, @"\[Double Attack\]|gains \[Double Attack\]")),
                Banish = cards.Count(c => Has(c, @"\[Banish\]|gains \[Banish\]")),
                Restand = cards.Count(c => Has(c, @"Set (?:this|up to \d+ of your).+ as active")),
                KoRemoval = cards.Count(c => Has(c, @"K\.O\. up to")),
                RestEffects = cards.Count(c => Has(c, @"Rest up to")),
                CounterEvents = cards.Count(c => Eq(c.Type, "event") && Has(c, @"\[Counter\]")),
                LifeEffects = cards.Count(c => Has(c, @"\bLife\b")),
                DonReactivation = cards.Count(c => Has(c, @"DON!! cards? as active")),
            };

            _equivalents = cards
                .GroupBy(GameplaySignature, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
        }

        private static string GameplaySignature(CardDef c)
        {
            string effect = Normalize(c.Effect);
            string trigger = Normalize(c.Trigger);
            string keywords = Join(c.Keywords);
            string features = Join(c.Features);
            bool mechanicallyBlank = effect.Length == 0 && trigger.Length == 0 && keywords.Length == 0;
            // Only mechanically blank cards can cross IDs. Effect-bearing cards can be referenced by printed
            // name or routed through an ID-specific interpreter branch, so their own ID is part of the key.
            string identity = mechanicallyBlank ? "" : Normalize(c.Id);
            return string.Join("|",
                Normalize(c.Type), Normalize(c.Color), c.Cost, c.Power, c.Life ?? -1, c.Counter,
                keywords, effect, trigger, features, identity);
        }

        private static string Join(IEnumerable<string> values) =>
            values == null ? "" : string.Join(",", values.Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(Normalize).OrderBy(v => v, StringComparer.Ordinal));

        private static string Normalize(string value) =>
            Regex.Replace((value ?? "").Trim(), @"\s+", " ").ToLowerInvariant();

        private static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private struct Rng
        {
            private ulong _state;
            public Rng(int seed)
            {
                _state = ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) ^ 0xD6E8FEB86659FD93UL;
                if (_state == 0) _state = 1;
            }
            private uint Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 7;
                _state ^= _state << 17;
                return (uint)(_state ^ (_state >> 32));
            }
            public int Range(int count) => count <= 1 ? 0 : (int)(Next() % (uint)count);
        }
    }
}
