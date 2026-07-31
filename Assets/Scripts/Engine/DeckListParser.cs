using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine
{
    /// <summary>
    /// Parses a pasted decklist into (leader, card counts). Deliberately tolerant: people paste from
    /// EGMan, OPTCGSim, OnePieceTopDecks, tournament sheets, Discord messages and their own notes, and
    /// a decklist that imports with the WRONG NUMBERS is worse than one that refuses outright — the
    /// player gets a deck that looks right and is not.
    ///
    /// The previous implementation ran one whole-text regex that required the quantity to sit
    /// IMMEDIATELY before the code (`4xOP01-016`, `4 OP01-016`). Any line that put the name in between —
    /// `4x Nami (OP01-016)`, the shape several builders and every hand-written list use — matched
    /// nothing. When no line matched, it fell back to counting each bare code once, so a 50-card list
    /// imported as "one of everything". That is the reported symptom: numbers not matching up.
    ///
    /// This version works LINE BY LINE and separates the two questions: find the card code (unambiguous,
    /// because a code must contain a dash and a name never does), then look for a quantity in what is
    /// left of the line. A line with no code at all — "Deck (50 cards)", "Leader:", "// notes" — is
    /// skipped entirely, which also kills the old bug where a header's number became the next card's
    /// quantity.
    ///
    /// Engine-side and UnityEngine-free so the harness can gate it against real-world samples.
    /// </summary>
    /// <summary>
    /// Text normalisation for card search. People type fast and loose — "youre" for "You're",
    /// "monkey d luffy" for "Monkey.D.Luffy", "strawhat" for "Straw Hat" — and a plain
    /// <c>Contains</c> over the raw name finds none of those, so the card "isn't there" as far as the
    /// player is concerned. Folding punctuation, apostrophes, accents and spaces out of BOTH sides
    /// makes all of those hit.
    /// </summary>
    public static class SearchText
    {
        /// <summary>Lowercase, strip accents, and drop everything that is not a letter or digit.
        /// "Monkey.D.Luffy" → "monkeydluffy"; "You're" → "youre"; "Straw Hat Crew" → "strawhatcrew".</summary>
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string norm = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(norm.Length);
            foreach (char ch in norm)
            {
                // Drop combining marks so "Zorō"/"Zoro" and "Poké"/"Poke" behave the same.
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                    == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>True when every whitespace-separated term in <paramref name="query"/> appears in at
        /// least one of the folded haystacks. Multi-term is AND so "luffy blocker" narrows rather than
        /// widens, and terms may match different fields (name + effect).</summary>
        public static bool Matches(string query, params string[] haystacks)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var folded = haystacks.Select(Fold).ToArray();
            foreach (var term in query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = Fold(term);
                if (t.Length == 0) continue;
                if (!folded.Any(h => h.Contains(t))) return false;
            }
            return true;
        }
    }

    public static class DeckListParser
    {
        /// A card code: letters, optional set digits, a dash, the number, optional alt-art suffix.
        /// The mandatory dash is what makes a code impossible to confuse with a card NAME.
        private static readonly Regex CodeRegex = new Regex(
            @"\b[A-Za-z]{1,4}\d{0,3}-\d{1,4}(?:-\d{1,3})?\b", RegexOptions.Compiled);

        /// Quantity written BEFORE the code: "4x", "4 x", "4", "x4". Anchored to the END of the
        /// preceding text so "OP01" style noise earlier in the line cannot be read as a count.
        private static readonly Regex QtyBeforeRegex = new Regex(
            @"(?:^|[^\d])(\d{1,2})\s*[xX×]?\s*[^\d]*$", RegexOptions.Compiled);

        /// Quantity written AFTER the code: "x4", "×4", "(4)". Anchored to the START of the trailing text.
        private static readonly Regex QtyAfterRegex = new Regex(
            @"^[^\d]*?[xX×(]\s*(\d{1,2})\b", RegexOptions.Compiled);

        /// Splits a quantity from a code it is GLUED to: "1xOP01-001" → "1 OP01-001".
        /// CodeRegex needs a word boundary before the letters, and in the glued form the boundary
        /// between the separator 'x' and the code does not exist — so the most common native format
        /// (OPTCGSim's own "NxCODE") would find no code at all. Only fires when the character after the
        /// separator is a LETTER, so a trailing quantity ("OP01-016 x4") is left for QtyAfterRegex.
        private static readonly Regex GluedQtyRegex = new Regex(
            @"(\d)\s*[xX×](?=\s*[A-Za-z])", RegexOptions.Compiled);

        public sealed class Result
        {
            public string LeaderId;
            public readonly List<KeyValuePair<string, int>> Cards = new List<KeyValuePair<string, int>>();
            public readonly List<string> Unknown = new List<string>();
            /// <summary>Total main-deck copies parsed — what the caller should compare against 50.</summary>
            public int Total => Cards.Sum(kv => kv.Value);
        }

        /// <summary>Resolve a code to a real card id, retrying without a trailing alt-art suffix
        /// ("OP09-004-1" → "OP09-004"). Returns null when nothing matches.
        ///
        /// Uses Library.ContainsKey, NOT GetCard: GetCard never returns null — it hands back a
        /// placeholder CardDef(id, id, "unknown", ...) for any id it does not know. A `GetCard(x) != null`
        /// existence check therefore always passes, which would import every typo'd or not-yet-shipped
        /// code (an OP17 card, say) as N phantom copies instead of reporting it — another way for the
        /// numbers to silently not match up.</summary>
        public static string ResolveCode(string rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode)) return null;
            string code = rawCode.Trim().ToUpperInvariant();
            if (CardData.Library.ContainsKey(code)) return code;
            int lastDash = code.LastIndexOf('-'), firstDash = code.IndexOf('-');
            if (lastDash > firstDash && firstDash > 0)
            {
                string stripped = code.Substring(0, lastDash);
                if (CardData.Library.ContainsKey(stripped)) return stripped;
            }
            return null;
        }

        public static Result Parse(string raw)
        {
            var result = new Result();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();

            // A JSON-array export lists every copy as its own element, so quantities are implicit and
            // any digits present are indices/ids rather than counts. Count occurrences instead.
            bool jsonArray = raw.TrimStart().StartsWith("[");

            foreach (var rawLine in raw.Split('\n'))
            {
                string line = GluedQtyRegex.Replace(rawLine.Trim(), "$1 ");
                if (line.Length == 0) continue;

                var codes = CodeRegex.Matches(line);
                if (codes.Count == 0) continue;   // header / note / total line — never a quantity source

                if (jsonArray || codes.Count > 1)
                {
                    // Several codes on one line (or a JSON array): each occurrence is one copy. Trying to
                    // attach a single quantity to several codes is how a list silently multiplies.
                    foreach (Match m in codes) Add(m.Value, 1);
                    continue;
                }

                var codeMatch = codes[0];
                string before = line.Substring(0, codeMatch.Index);
                string after = line.Substring(codeMatch.Index + codeMatch.Length);

                int qty = 1;
                var mb = QtyBeforeRegex.Match(before);
                if (mb.Success) qty = int.Parse(mb.Groups[1].Value);
                else
                {
                    var ma = QtyAfterRegex.Match(after);
                    if (ma.Success) qty = int.Parse(ma.Groups[1].Value);
                }
                Add(codeMatch.Value, qty);
            }

            foreach (var id in order)
                result.Cards.Add(new KeyValuePair<string, int>(id, counts[id]));
            return result;

            void Add(string rawCode, int qty)
            {
                string id = ResolveCode(rawCode);
                if (id == null)
                {
                    string u = rawCode.ToUpperInvariant();
                    if (!result.Unknown.Contains(u)) result.Unknown.Add(u);
                    return;
                }
                var def = CardData.GetCard(id);
                // The Leader is identified by TYPE, never by position or by being the "1x" line —
                // plenty of lists put it last, or omit the quantity entirely.
                if (string.Equals(def?.Type, "leader", StringComparison.OrdinalIgnoreCase))
                {
                    result.LeaderId = id;   // last leader wins, matching the old behaviour
                    return;
                }
                if (qty < 1) qty = 1;
                if (!counts.ContainsKey(id)) { counts[id] = 0; order.Add(id); }
                counts[id] += qty;
            }
        }
    }
}
