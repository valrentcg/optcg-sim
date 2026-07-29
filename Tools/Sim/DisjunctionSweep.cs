using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Generalises the reveal-cost bug: "{A} or {B} type" read as "{A}".
    ///
    /// The engine has two tag parsers - ParseCurlyBraceTag (first tag only) and ParseCurlyBraceTagsOr
    /// (both). There are 25 call sites of the first and 4 of the second, so the question is not whether
    /// the disjunction is handled but WHERE it is not, and eyeballing 25 sites cannot answer that.
    ///
    /// So this measures the consequence instead of auditing the code. For every clause carrying a
    /// "{A} ... or ... {B}" disjunction, it builds a board holding one card with ONLY tag A and one with
    /// ONLY tag B, then asks the glow filter about each. Both are legal by the card text. If A lights
    /// and B does not, that clause is reading one tag - the player is holding a legal card that cannot
    /// be clicked, which is the exact symptom the reveal cost produced.
    ///
    /// WHAT THIS DOES NOT COVER, measured rather than assumed. It asks the GLOW filter, which
    /// reaches CardPassesFeatureFilter - and that iterates EVERY {tag} in the text, so it is
    /// disjunction-safe by construction. Crippling ParseCurlyBraceTagsOr to drop the second tag does
    /// NOT make this sweep report an asymmetry; it moves one clause from "both" to "neither". So the
    /// sweep cannot see drift in the 25 single-tag call sites that sit on the RESOLVER and COST paths,
    /// which is where the reveal bug actually lived. It confirms the glow path is symmetric over 24
    /// clauses and nothing more. Treated as a report, never a gate: an assertion that survives its own
    /// negative control is not evidence.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- disjunctionsweep
    /// </summary>
    public static class DisjunctionSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== \"{A} or {B} type\": is the SECOND tag honoured? ===");

            int clauses = 0, bothLit = 0, neitherLit = 0, threw = 0;
            var asymmetric = new List<(string Id, string Name, string Clause, string Lit, string Dark)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    var m = Regex.Match(raw, @"\{([^}]+)\}[^{}]{0,24}?\bor\b[^{}]{0,24}?\{([^}]+)\}");
                    if (!m.Success) continue;
                    string tagA = m.Groups[1].Value.Trim(), tagB = m.Groups[2].Value.Trim();
                    if (string.Equals(tagA, tagB, StringComparison.OrdinalIgnoreCase)) continue;
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    if (clause.Length == 0) continue;

                    // Need a card carrying ONE tag and not the other, in both directions, or the test
                    // cannot tell the tags apart.
                    // Honour the clause's OWN cost cap when picking the two probe cards. Ignoring it
                    // made P-091 Shirahoshi ("{Neptunian} or {Fish-Man Island} ... with a cost of 5 or
                    // less") look asymmetric: the {Neptunian} pick was cost 7 and correctly rejected,
                    // which is the filter working, not a dropped tag.
                    var capM = Regex.Match(clause, @"cost of (\d+) or less", RegexOptions.IgnoreCase);
                    int cap = capM.Success ? int.Parse(capM.Groups[1].Value) : int.MaxValue;
                    string onlyA = FindCharacterWith(tagA, without: tagB, maxCost: cap);
                    string onlyB = FindCharacterWith(tagB, without: tagA, maxCost: cap);
                    if (onlyA == null || onlyB == null) continue;
                    clauses++;

                    try
                    {
                        var b = new Board();
                        var src = b.Character("south", def.Id);
                        if (src == null) continue;
                        var cardA = b.Hand("south", onlyA);
                        var cardB = b.Hand("south", onlyB);
                        var boardA = b.Character("south", onlyA);
                        var boardB = b.Character("south", onlyB);

                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                        var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (pe == null) continue;

                        bool litA = Lit(b, pe, cardA) || Lit(b, pe, boardA);
                        bool litB = Lit(b, pe, cardB) || Lit(b, pe, boardB);

                        if (litA && litB) bothLit++;
                        else if (!litA && !litB) neitherLit++;
                        else asymmetric.Add((def.Id, def.Name ?? "", clause,
                                             litA ? tagA : tagB, litA ? tagB : tagA));
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  disjunction clauses testable      : {clauses}");
            Console.WriteLine($"  both tags accepted                : {bothLit}");
            Console.WriteLine($"  neither accepted (gated elsewhere): {neitherLit}");
            Console.WriteLine($"  ONE tag accepted, the other not   : {asymmetric.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            if (asymmetric.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  asymmetric - a legal card that cannot be clicked:");
                foreach (var a in asymmetric.Take(20))
                    Console.WriteLine($"    {a.Id,-10} {Trim(a.Name, 15),-15} lit={{{Trim(a.Lit, 18)}}} dark={{{Trim(a.Dark, 18)}}}  {Trim(a.Clause, 66)}");
                if (asymmetric.Count > 20) Console.WriteLine($"    ... and {asymmetric.Count - 20} more");
            }

            // Reporting only. Asymmetry WOULD be unambiguous, but the control above shows this
            // instrument cannot reliably produce it, so a 0 here is not proof of anything beyond the
            // glow path. Always exits 0; read the numbers.
            return 0;
        }

        private static bool Lit(Board b, PendingEffect pe, CardInstance c) =>
            c != null && GameEngine.IsValidEffectTarget(b.St, pe, c);

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        /// <summary>A Character carrying <paramref name="tag"/> but NOT <paramref name="without"/>, so
        /// the two tags can be told apart.</summary>
        private static string FindCharacterWith(string tag, string without, int maxCost = int.MaxValue)
        {
            foreach (var d in CardData.Library.Values)
            {
                if (d == null || !string.Equals(d.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                var fs = d.Features;
                if (fs == null) continue;
                bool has = fs.Any(f => (f ?? "").IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasOther = fs.Any(f => (f ?? "").IndexOf(without, StringComparison.OrdinalIgnoreCase) >= 0);
                if (has && !hasOther && d.Cost <= maxCost) return d.Id;
            }
            return null;
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "disjunction" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; St.Players["north"].TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; St.Players["north"].CharacterArea[i] = null; }
                S.Hand.Clear(); S.Life.Clear(); S.CostArea.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 4; i++) S.Life.Add(Card("ST01-005", "south", "life"));
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-dj-don-{serial++}", Rested = false });
                S.DonDeck = 0;
                var nc = Character("north", "OP15-040"); if (nc != null) nc.Rested = true;
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-dj-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
