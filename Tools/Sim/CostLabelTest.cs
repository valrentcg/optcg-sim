using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The Use button was asked for by name — "the use button shouldn't just say use, it should say
    /// what the you may action is" — and it shipped untested, because the derivation was private to
    /// a MonoBehaviour and nothing headless could call it. Moving it into the engine
    /// (GameEngine.DescribeCostPrefix) is what makes this suite possible at all.
    ///
    /// The label is the only thing standing between the player and an unlabelled commitment: press
    /// it and you have paid. A wrong label is worse than a generic one, so this checks the whole
    /// pool rather than a handful of cards:
    ///
    ///   * every "You may &lt;cost&gt;:" clause produces a label, and it is the COST, not the body
    ///   * a clause with no cost prefix produces none (the caller falls back to "Use Effect")
    ///   * the label never exceeds the width the bubble can show
    ///   * timing tags are stripped, so it never reads "[On Play] You may..."
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- costlabel
    /// </summary>
    public static class CostLabelTest
    {
        private static int passed, failed;
        private const int Width = 58;

        public static int Run()
        {
            Console.WriteLine("=== The Use button names the cost, pool-wide ===");
            EveryCostPrefixGetsALabel();
            TheLabelIsTheCostNotTheBody();
            NoCostPrefixMeansNoLabel();
            TagsAreStrippedAndWidthIsRespected();
            EveryCostPrefixRoutesToTheUseButton();
            APaidOrTargetingStepDoesNotRouteToUse();
            CleanedTextIsReadable();
            CircledDonCostsParse();
            DonRestCostsParseIncludingCompound();
            SlashCombinedTagsAreStripped();
            TypeWordingStaysGrammatical();
            Console.WriteLine($"costlabel: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Every cost-prefixed clause in the pool. A miss here is a card whose button falls
        /// back to "Use Effect" — the exact thing the request was about.</summary>
        private static IEnumerable<(string Id, string Clause)> CostPrefixClauses()
        {
            foreach (var def in CardData.Library.Values.Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                                                       .GroupBy(d => d.Id).Select(g => g.First()))
            {
                foreach (var raw in def.Effect.Split((char)10))
                {
                    var s = raw.Trim();
                    if (s.Length == 0) continue;
                    // The shape the button exists for: an opt-in with a colon-terminated cost.
                    var stripped = System.Text.RegularExpressions.Regex.Replace(
                        s, @"^\s*(?:\[[^\]]+\]\s*/?\s*)+", "");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(
                            stripped, @"^You (?:may|can) [^:]{2,90}:",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    yield return (def.Id, s);
                }
            }
        }

        private static void EveryCostPrefixGetsALabel()
        {
            var missing = new List<string>();
            int total = 0;
            foreach (var (id, clause) in CostPrefixClauses())
            {
                total++;
                var label = GameEngine.DescribeCostPrefix(clause, Width);
                if (string.IsNullOrWhiteSpace(label)) missing.Add($"{id} :: {Trim(clause, 70)}");
            }

            // Guard against a vacuous pass: if the enumeration finds nothing, "0 missing" is
            // meaningless. The pool carries hundreds of these.
            Check("the cost-prefix enumeration still finds clauses",
                  total >= 300,
                  $"only {total} found — the detector has gone blind, so the check below proves nothing");
            Check($"every cost-prefixed clause produces a label ({total} clauses)",
                  missing.Count == 0,
                  $"{missing.Count} fell back to \"Use Effect\": {string.Join(" | ", missing.Take(4))}");
        }

        /// <summary>The label must name what you PAY, not what you get. Labelling the body would read
        /// as a promise and hide the price.</summary>
        private static void TheLabelIsTheCostNotTheBody()
        {
            var cases = new[]
            {
                ("[On Play] You may trash 1 card from your hand: Draw 2 cards.",
                 "Trash 1 card from your hand", "Draw"),
                ("[When Attacking] You may rest this Character: K.O. up to 1 of your opponent's Characters.",
                 "Rest this Character", "K.O."),
                ("You may add 1 card from the top or bottom of your Life cards to your hand: Draw 1 card.",
                 "Add 1 card from the top or bottom of your Life cards to your hand", "Draw"),
            };

            int bad = 0;
            foreach (var (clause, wantStart, mustNotContain) in cases)
            {
                var label = GameEngine.DescribeCostPrefix(clause, 0) ?? "";
                bool startsRight = label.StartsWith(wantStart, StringComparison.Ordinal);
                bool leaksBody = label.IndexOf(mustNotContain, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!startsRight || leaksBody)
                {
                    bad++;
                    Console.WriteLine($"      got \"{label}\" (want it to start \"{wantStart}\" and omit \"{mustNotContain}\")");
                }
            }
            Check("the label is the COST and never leaks the body", bad == 0, bad == 0 ? "" : $"{bad} of {cases.Length} wrong");
        }

        private static void NoCostPrefixMeansNoLabel()
        {
            var noCost = new[]
            {
                "[On Play] Draw 1 card.",
                "[On Play] K.O. up to 1 of your opponent's Characters with a cost of 3 or less.",
                "You may draw 1 card.",                       // "you may" with NO colon: not a cost
                "",
            };
            var leaked = noCost.Where(t => GameEngine.DescribeCostPrefix(t, Width) != null).ToList();
            Check("a clause with no cost prefix produces no label",
                  leaked.Count == 0,
                  $"{leaked.Count} produced one anyway — the button would name a cost that does not exist");
        }

        private static void TagsAreStrippedAndWidthIsRespected()
        {
            int overWidth = 0, taggy = 0, total = 0;
            foreach (var (_, clause) in CostPrefixClauses())
            {
                var label = GameEngine.DescribeCostPrefix(clause, Width);
                if (label == null) continue;
                total++;
                if (label.Length > Width) overWidth++;
                if (label.StartsWith("[", StringComparison.Ordinal)) taggy++;
            }
            Check($"no label exceeds the bubble width or starts with a timing tag ({total} labels)",
                  overWidth == 0 && taggy == 0,
                  $"{overWidth} too long, {taggy} still carry a leading [tag]");
        }

        /// <summary>The routing predicate behind the FIRST defect fixed in this workstream: an unpaid
        /// "You may &lt;cost&gt;:" clause must send the panel to the Use button, not to a board prompt.
        /// Routing it the other way is what showed Wyper only a Skip button with nothing clickable,
        /// across 512 clauses.
        ///
        /// Worth asserting pool-wide precisely because the failure was SILENT: the check was inline
        /// in the UI with an anchored ^You-may that never matched a tagged clause, so it was dead
        /// code and every one of these routed the wrong way.</summary>
        private static void EveryCostPrefixRoutesToTheUseButton()
        {
            var wrong = new List<string>();
            int total = 0;
            foreach (var (id, clause) in CostPrefixClauses())
            {
                total++;
                // A freshly queued effect: no selections made yet, which is the state the panel sees
                // the moment the decision appears.
                var pe = new PendingEffect { Text = clause, SelectionsRemaining = 0 };
                if (!GameEngine.IsUnpaidCostPrefix(pe)) wrong.Add($"{id} :: {Trim(clause, 70)}");
            }
            Check($"every unpaid cost prefix routes to the Use button ({total} clauses)",
                  wrong.Count == 0,
                  $"{wrong.Count} would show a board prompt with nothing clickable: "
                  + string.Join(" | ", wrong.Take(4)));
        }

        /// <summary>The other direction, which is what stops the predicate being "return true".
        /// A clause with no cost prefix, and a cost-prefixed clause MID-PICK (selections already
        /// outstanding), must both route to the board prompt instead.</summary>
        private static void APaidOrTargetingStepDoesNotRouteToUse()
        {
            var plain = new PendingEffect
            { Text = "[On Play] K.O. up to 1 of your opponent's Characters.", SelectionsRemaining = 0 };
            var midPick = new PendingEffect
            { Text = "[On Play] You may trash 1 card from your hand: Draw 2 cards.", SelectionsRemaining = 2 };

            Check("a clause with no cost prefix does NOT route to Use",
                  !GameEngine.IsUnpaidCostPrefix(plain),
                  "a plain targeting clause was sent to the Use button, hiding its board prompt");
            Check("a cost-prefixed clause MID-PICK does NOT route to Use",
                  !GameEngine.IsUnpaidCostPrefix(midPick),
                  "an effect with selections outstanding was sent back to Use, so the pick it is "
                  + "waiting on would never be offered");
        }

        /// <summary>The cleaning behind the pending-effect progress ledger — the green/red text both
        /// players watch fill in. The cleaned string is ALSO matched back against sub-clauses to
        /// decide which characters get coloured, so a cleaning bug shows twice: garbled text, and
        /// colouring that lines up against the wrong words.
        ///
        /// It lived in the UI in two copies, one live and one dead, with a comment saying they were
        /// "kept separate". Driven over the whole pool now that there is one copy, in the engine.</summary>
        private static void CleanedTextIsReadable()
        {
            int total = 0, emptied = 0, taggy = 0, reminder = 0, doubled = 0;
            foreach (var def in CardData.Library.Values.Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                                                       .GroupBy(d => d.Id).Select(g => g.First()))
            {
                foreach (var raw in def.Effect.Split((char)10))
                {
                    var clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    total++;
                    var cleaned = GameEngine.CleanClauseText(clause);
                    // A clause that is ONLY tags legitimately cleans to nothing; anything with real
                    // words must survive.
                    bool onlyTags = System.Text.RegularExpressions.Regex.IsMatch(
                        clause, @"^\s*(\[[^\]]*\]\s*/?\s*)+$");
                    if (cleaned.Length == 0 && !onlyTags) emptied++;
                    if (cleaned.StartsWith("[", StringComparison.Ordinal)) taggy++;
                    if (cleaned.IndexOf("specified number of DON!!", StringComparison.OrdinalIgnoreCase) >= 0) reminder++;
                    if (cleaned.Contains("  ")) doubled++;
                }
            }
            Check($"cleaned clause text stays readable across {total} clauses",
                  emptied == 0 && taggy == 0 && reminder == 0 && doubled == 0,
                  $"emptied={emptied} leadingTag={taggy} reminderLeft={reminder} doubleSpace={doubled}");
        }

        /// <summary>The circled DON!! cost glyph — the cost the player is being asked to pay before
        /// an [Activate: Main] ability will fire. The UI held a character-for-character duplicate of
        /// the engine's parser; they agreed, but the same duplication between the button label and
        /// the routing predicate is what left OP01-031 unusable, so there is now one copy.
        ///
        /// Both Unicode series are asserted, since a parser that understood only one would still
        /// look correct against whichever series the pool happens to use today.</summary>
        private static void CircledDonCostsParse()
        {
            // Both series, spelled by codepoint so the file itself carries no exotic glyph.
            int seriesA = GameEngine.ParseCircledDonCost("cost " + (char)0x2461 + " here");   // circled 2
            int seriesB = GameEngine.ParseCircledDonCost("cost " + (char)0x2782 + " here");   // dingbat 3
            Check("both circled-digit series parse to the right number",
                  seriesA == 2 && seriesB == 3,
                  $"U+2461 -> {seriesA} (want 2), U+2782 -> {seriesB} (want 3)");

            int parsed = 0, zero = 0;
            foreach (var def in CardData.Library.Values.Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                                                       .GroupBy(d => d.Id).Select(g => g.First()))
            {
                foreach (var line in def.Effect.Split((char)10))
                {
                    bool hasGlyph = line.Any(ch => (ch >= (char)0x2460 && ch <= (char)0x2469)
                                                || (ch >= (char)0x2780 && ch <= (char)0x2789));
                    if (!hasGlyph) continue;
                    parsed++;
                    if (GameEngine.ParseCircledDonCost(line) <= 0) zero++;
                }
            }
            Check($"every clause carrying a circled glyph yields a cost ({parsed} clauses)",
                  parsed >= 20 && zero == 0,
                  $"found={parsed} parsedToZero={zero} — a clause showing a DON!! cost the gate reads "
                  + "as free, or a detector that has gone blind");
            Check("a clause with no glyph costs nothing",
                  GameEngine.ParseCircledDonCost("[On Play] Draw 1 card.") == 0,
                  "a DON!! cost was invented for a clause that has none");
        }

        /// <summary>The DON!!-rest cost drives a dedicated affordance: a glowing active DON!! to
        /// click, and a Use button GREYED OUT when the player cannot afford it. The UI's own parser
        /// required the colon to follow immediately, so the compound printings scored 0 — those
        /// cards kept a live Use button with too few DON!! to pay it.
        ///
        /// Both shapes are driven, because the simple one alone passes against exactly the parser
        /// that was wrong.</summary>
        private static void DonRestCostsParseIncludingCompound()
        {
            int simple = 0, compound = 0, simpleZero = 0, compoundZero = 0;
            foreach (var def in CardData.Library.Values.Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                                                       .GroupBy(d => d.Id).Select(g => g.First()))
            {
                foreach (var line in def.Effect.Split((char)10))
                {
                    var bare = System.Text.RegularExpressions.Regex.Replace(
                        line.Trim(), @"^\s*(?:\[[^\]]+\]\s*/?\s*)+", "");
                    var m = System.Text.RegularExpressions.Regex.Match(
                        bare, @"^You (?:may|can) rest (\d+) of your DON!! cards?(.*?):",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    bool isCompound = m.Groups[2].Value.Trim().Length > 0;
                    int want = int.Parse(m.Groups[1].Value);
                    int got = GameEngine.ParseDonRestCost(line);
                    if (isCompound) { compound++; if (got != want) compoundZero++; }
                    else            { simple++;   if (got != want) simpleZero++; }
                }
            }
            Check($"simple DON!!-rest costs parse ({simple} clauses)",
                  simple >= 20 && simpleZero == 0,
                  $"found={simple} wrong={simpleZero}");
            Check($"COMPOUND DON!!-rest costs parse too ({compound} clauses)",
                  compound >= 5 && compoundZero == 0,
                  $"found={compound} wrong={compoundZero} — these lose the DON!! click affordance and "
                  + "keep a Use button that cannot be paid");
        }

        /// <summary>39 cards print slash-combined timing tags — "[On Play]/[When Attacking]",
        /// OP02-036 Nami among them. The engine spelled the tag-stripping rule 28 times; 6 of those
        /// spellings could not consume the "/", so they stopped after the first tag and left
        /// "/[When Attacking] ..." behind. Every one of those sites then runs an ANCHORED match
        /// ("^If ...", "^You may ..."), which fails, and the code path silently skips the card.
        ///
        /// Asserted through the shared helper and through two public consumers, so this covers the
        /// rule rather than one call site.</summary>
        private static void SlashCombinedTagsAreStripped()
        {
            const string slashed = "[On Play]/[When Attacking] You may trash 1 card from your hand: Draw 2 cards.";
            const string plain   = "[On Play] You may trash 1 card from your hand: Draw 2 cards.";

            string strippedSlash = GameEngine.StripLeadingTimingTags(slashed);
            string strippedPlain = GameEngine.StripLeadingTimingTags(plain);
            Check("slash-combined tags are fully stripped",
                  strippedSlash == strippedPlain && !strippedSlash.StartsWith("/", StringComparison.Ordinal),
                  $"got \"{Trim(strippedSlash, 60)}\" — a leading \"/\" breaks every anchored match after it");

            // Two public consumers of the same rule, so a future divergence shows up here too.
            Check("a slash-tagged clause still yields its cost label",
                  GameEngine.DescribeCostPrefix(slashed, 58) == GameEngine.DescribeCostPrefix(plain, 58),
                  "the slash printing produced a different label from the plain one");
            Check("a slash-tagged clause still routes to the Use button",
                  GameEngine.IsUnpaidCostPrefix(new PendingEffect { Text = slashed, SelectionsRemaining = 0 }),
                  "the slash printing would show a board prompt with nothing clickable");
        }

        /// <summary>A data-shape assumption the engine silently rests on.
        ///
        /// The type matcher is spelled two ways: `type includ(?:es|ing)` at 4 sites and the narrower
        /// `type including` at 7. That looks like a drift waiting to happen — the narrow sites would
        /// miss the 41 cards printing "includes" — but the two wordings are GRAMMATICALLY distinct
        /// in the pool, not variants:
        ///
        ///   "...with a type INCLUDING X"      a filter on a card   (129 occurrences)
        ///   "...your Leader's type INCLUDES X" a condition          (70 occurrences)
        ///
        /// The narrow sites are filter sites, so they are correct today. Nothing was changed on the
        /// strength of the divergence. What is asserted is the assumption itself: the day a set
        /// prints "Character with a type includes X", those 7 sites go blind and this says so.</summary>
        private static void TypeWordingStaysGrammatical()
        {
            int filterWithIncludes = 0, conditionWithIncluding = 0, total = 0;
            foreach (var def in CardData.Library.Values.Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                                                       .GroupBy(d => d.Id).Select(g => g.First()))
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(def.Effect, "([A-Za-z']+) type includ(es|ing)"))
                {
                    total++;
                    bool possessive = m.Groups[1].Value.EndsWith("'s", StringComparison.Ordinal);
                    bool isIncludes = m.Groups[2].Value == "es";
                    // A filter ("a type including") is never possessive; a condition always is.
                    if (!possessive && isIncludes) filterWithIncludes++;
                    if (possessive && !isIncludes) conditionWithIncluding++;
                }
            }
            Check($"the type wording stays grammatical ({total} occurrences)",
                  total >= 100 && filterWithIncludes == 0 && conditionWithIncluding == 0,
                  $"found={total} filtersSaying\"includes\"={filterWithIncludes} "
                  + $"conditionsSaying\"including\"={conditionWithIncluding} — the 7 narrow "
                  + "`type including` sites only cover the filter form");
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
    }
}
