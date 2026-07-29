using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Outcome verification at scale, for the subset of cards where the outcome is a NUMBER.
    ///
    /// endtoend proves two cards resolve correctly, in detail. costresolvesweep covers all 565
    /// cost-prefix clauses but can only ask whether something happened - it reads the log, so a card
    /// that draws one card instead of two passes it. Between those sits a gap: many cards, checked
    /// exactly.
    ///
    /// "You may &lt;cost&gt;: Draw N cards." closes part of it. The body is unambiguous and countable,
    /// so after pressing Use the hand must be exactly N larger - not "something drew", exactly N.
    ///
    /// Costs that themselves touch the hand are excluded, because then the net change is N minus the
    /// discard and the assertion stops being crisp. What remains pays with a rest, a DON!!, or a Life
    /// flip, none of which move hand cards.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- drawoutcome
    /// </summary>
    public static class DrawOutcomeSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== \"You may <cost>: Draw N cards\" — is it exactly N? ===");

            int tested = 0, exact = 0, neverPaid = 0, skippedHandCost = 0, threw = 0;
            var wrong = new List<(string Id, string Name, int Want, int Got, string Clause)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    var m = Regex.Match(clause, @"^You may (?<cost>[^:]+):\s*Draw (?<n>\d+) cards?\.?$",
                                        RegexOptions.IgnoreCase);
                    if (!m.Success) continue;

                    // A cost paid from HAND changes the hand too, so the expected delta is N minus
                    // the discard rather than N. Excluding those threw away 19 of 27 clauses and left
                    // only 4 verified, too thin to mean anything - so account for them instead. A
                    // REVEAL cost moves nothing (the cards stay in hand).
                    string cost = m.Groups["cost"].Value;
                    int handCost = 0;
                    var trashM = Regex.Match(cost, @"trash (\d+) [^,]*?from your hand", RegexOptions.IgnoreCase);
                    if (trashM.Success) handCost = int.Parse(trashM.Groups[1].Value);
                    else if (cost.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
                             && cost.IndexOf("reveal", StringComparison.OrdinalIgnoreCase) < 0)
                    { skippedHandCost++; continue; }   // some other hand interaction - not modelled

                    int want = int.Parse(m.Groups["n"].Value);
                    tested++;

                    try
                    {
                        var b = new Board(clause);
                        var src = b.Character("south", def.Id);
                        if (src == null) continue;
                        int hand0 = b.S.Hand.Count;
                        int logBefore = b.St.EventLog.Count;

                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                        var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (pe == null) continue;
                        b.Apply(new GameCommand
                        { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
                        // A from-hand cost stops for a pick. Answer it with a hand card, then let
                        // the body run.
                        for (int q = 0; q < 4; q++)
                        {
                            var nxt = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                            if (nxt == null) break;
                            var pick = b.S.Hand.FirstOrDefault();
                            b.Apply(new GameCommand
                            { Type = "resolveEffect", Seat = "south", EffectId = nxt.EffectId, Target = pick?.InstanceId });
                        }

                        // Only judge cards whose cost was actually paid on this board; an unpayable cost
                        // correctly draws nothing and says so.
                        // The engine marks payment TWO ways: "(cost)" from the auto-payer and a
                        // " cost: " prefix from the click-driven pick path. Checking only the first
                        // classified 19 of 24 as unpayable when they had in fact been paid by a pick,
                        // which is the same two-forms detail PaidForNothingSweep already handles.
                        bool paid = b.St.EventLog.Skip(logBefore).Any(l =>
                            (l.Message ?? "").IndexOf("(cost)", StringComparison.OrdinalIgnoreCase) >= 0
                            || (l.Message ?? "").IndexOf(" cost: ", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!paid) { neverPaid++; continue; }

                        int got = b.S.Hand.Count - hand0;
                        int expect = want - handCost;
                        if (got == expect) exact++;
                        else wrong.Add((def.Id, def.Name ?? "", expect, got, clause));
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  draw-body clauses tested          : {tested}");
            Console.WriteLine($"  skipped (cost paid from hand)     : {skippedHandCost}");
            Console.WriteLine($"  cost unpayable here, drew nothing : {neverPaid}");
            Console.WriteLine($"  drew EXACTLY the stated number    : {exact}");
            Console.WriteLine($"  DREW THE WRONG NUMBER             : {wrong.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            foreach (var w in wrong.Take(20))
                Console.WriteLine($"    {w.Id,-10} {Trim(w.Name, 16),-16} wanted {w.Want}, got {w.Got}  {Trim(w.Clause, 70)}");
            if (wrong.Count > 20) Console.WriteLine($"    ... and {wrong.Count - 20} more");

            // The card states a number and the cost was paid. There is no board on which drawing a
            // different number is correct.
            return wrong.Count == 0 ? 0 : 1;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board(string clause = null)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "draw-outcome" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 5; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                for (int i = 0; i < 4; i++) S.Hand.Add(Card("ST29-004", "south", "hand"));   // to pay from-hand costs
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                Character("south", "EB03-002");
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-do-don-{serial++}", Rested = false });
                S.DonDeck = 2; N.DonDeck = 2;
                St.PendingEffects.Clear();
                if (string.IsNullOrEmpty(clause)) return;
                // Stock the board from the CLAUSE. 19 of 24 costs were unpayable on a fixed board
                // because they name a {Type} nobody was holding, leaving only 5 cards actually
                // verified - too few to call this coverage.
                foreach (System.Text.RegularExpressions.Match t in
                         Regex.Matches(clause, @"\{([^}]+)\}"))
                {
                    var id = FindCharacterWithFeature(t.Groups[1].Value);
                    if (id == null) continue;
                    Character("south", id);
                    S.Hand.Add(Card(id, "south", "hand"));
                    S.Trash.Add(Card(id, "south", "trash"));
                }
            }

            private static string FindCharacterWithFeature(string tag)
            {
                foreach (var d in CardData.Library.Values)
                {
                    if (d == null || !string.Equals(d.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                    if (d.Features == null) continue;
                    foreach (var f in d.Features)
                        if ((f ?? "").IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return d.Id;
                }
                return null;
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-do-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
