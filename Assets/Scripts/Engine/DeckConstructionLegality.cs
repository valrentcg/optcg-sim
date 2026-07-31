using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine
{
    /// <summary>
    /// Official OPTCG deck-CONSTRUCTION rules: exactly 1 Leader, exactly 50 main-deck cards, at most 4
    /// copies of a card number (unless the card itself lifts the cap), no Leader cards in the main deck,
    /// every main-deck card shares a colour with the Leader, plus the two leader-specific deck-building
    /// restrictions (a cost ceiling, and a single-{type} lock).
    ///
    /// This is a DIFFERENT axis from <see cref="FormatLegality"/>, which answers "is this card in the
    /// Standard/Extra pool and not banned". A deck can be perfectly Standard-legal and still be an
    /// illegal deck (40 cards, 8 copies, off-colour), so both checks are needed.
    ///
    /// Lives in the engine, with no UnityEngine dependency, for two reasons: the rules were previously
    /// implemented only inside DeckBuilderManager where nothing but the UI could reach them (so the
    /// result was used to tint a badge and never to refuse anything), and a headless harness needs to be
    /// able to gate them. DeckBuilderManager now delegates here so there is exactly ONE copy of the rule
    /// set — two copies of a rule is how this codebase produces defects.
    /// </summary>
    public static class DeckConstructionLegality
    {
        public const int MainDeckSize = 50;
        public const int DefaultMaxCopies = 4;

        // "you may have any number of this card in your deck" lifts the 4-copy cap for that card.
        private const string AnyNumberPhrase = "you may have any number of this card in your deck";

        private static readonly Regex CostCeilingRegex =
            new Regex(@"cannot include (\w+) with a cost of (\d+) or more in your deck", RegexOptions.Compiled);
        private static readonly Regex FeatureRestrictionRegex =
            new Regex(@"you can only include \{([^}]+)\} type cards in your deck", RegexOptions.Compiled);

        /// <summary>Max copies of this card number allowed in one deck.</summary>
        public static int MaxCopiesFor(string cardId)
        {
            var def = CardData.GetCard(cardId);
            if (def != null && (def.Effect ?? "").Contains(AnyNumberPhrase)) return MainDeckSize;
            return DefaultMaxCopies;
        }

        /// <summary>Leader's cost-ceiling restriction: the affected card type (null = every type) and the
        /// lowest DISALLOWED cost. Null when this leader has no such restriction.</summary>
        public static (string restrictedType, int ceiling)? CostCeilingFor(string leaderId)
        {
            var def = CardData.GetCard(leaderId);
            if (def == null) return null;
            var m = CostCeilingRegex.Match(def.Effect ?? "");
            if (!m.Success) return null;
            string word = m.Groups[1].Value.ToLowerInvariant();
            string restrictedType = word == "cards" ? null : word.TrimEnd('s');   // "Events" -> "event"
            return (restrictedType, int.Parse(m.Groups[2].Value));
        }

        /// <summary>Leader's single-{type} deck lock (e.g. P-117 Nami: {East Blue} only). Null otherwise.</summary>
        public static string FeatureRestrictionFor(string leaderId)
        {
            var def = CardData.GetCard(leaderId);
            if (def == null) return null;
            var m = FeatureRestrictionRegex.Match(def.Effect ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string[] ColorsOf(CardDef def) =>
            string.IsNullOrEmpty(def?.Color)
                ? new string[0]
                : def.Color.Split('/').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();

        /// <summary>Every construction problem with this deck, newest-reader-friendly text. Empty = legal.
        /// <paramref name="entries"/> is the MAIN deck only (id + copy count); the Leader is separate and
        /// the DON!! deck is fixed and not part of deck building.</summary>
        public static List<string> Problems(string leaderId, IEnumerable<(string id, int count)> entries)
        {
            var msgs = new List<string>();
            var lead = CardData.GetCard(leaderId);
            var list = entries?.ToList() ?? new List<(string id, int count)>();

            if (string.IsNullOrEmpty(leaderId) || lead == null) msgs.Add("No leader selected");
            else if (!string.Equals(lead.Type, "leader", System.StringComparison.OrdinalIgnoreCase))
                msgs.Add($"{lead.Name} is not a leader card");

            int total = list.Sum(e => e.count);
            if (total != MainDeckSize) msgs.Add($"Main deck is {total}/{MainDeckSize}");

            string[] leadColors = ColorsOf(lead);
            var ceiling = leaderId != null ? CostCeilingFor(leaderId) : null;
            string featureLock = leaderId != null ? FeatureRestrictionFor(leaderId) : null;

            // Duplicate ids in the entry list would otherwise let 3+3 slip past a per-entry cap.
            foreach (var group in list.Where(e => !string.IsNullOrEmpty(e.id)).GroupBy(e => e.id))
            {
                string id = group.Key;
                int count = group.Sum(g => g.count);
                var def = CardData.GetCard(id);
                if (def == null) { msgs.Add($"Unknown card {id}"); continue; }

                int maxCopies = MaxCopiesFor(id);
                if (count > maxCopies) msgs.Add($"{def.Name}: {count} copies (max {maxCopies})");
                if (string.Equals(def.Type, "leader", System.StringComparison.OrdinalIgnoreCase))
                    msgs.Add($"{def.Name} is a leader, not a deck card");
                if (lead != null && leadColors.Length > 0 && !ColorsOf(def).Any(c => leadColors.Contains(c)))
                    msgs.Add($"{def.Name} ({def.Color}) is off-colour");
                if (ceiling.HasValue)
                {
                    var (restrictedType, cap) = ceiling.Value;
                    string ct = (def.Type ?? "").ToLowerInvariant();
                    if ((restrictedType == null || ct == restrictedType) && def.Cost >= cap)
                    {
                        string scope = restrictedType != null ? $" for {restrictedType}s" : "";
                        msgs.Add($"{def.Name}: cost {def.Cost} exceeds {lead.Name}'s deck-building limit (max {cap - 1}{scope})");
                    }
                }
                if (!string.IsNullOrEmpty(featureLock) && !def.HasFeature(featureLock))
                    msgs.Add($"{def.Name}: {lead.Name} can only include {{{featureLock}}} type cards");
            }
            return msgs;
        }

        public static bool IsLegal(string leaderId, IEnumerable<(string id, int count)> entries)
            => Problems(leaderId, entries).Count == 0;
    }
}
