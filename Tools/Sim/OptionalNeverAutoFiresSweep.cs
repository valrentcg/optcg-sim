using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The brief in one sentence: "the user should be prompted to make a decision on if they'd like to
    /// use effect". This checks the one way that can fail silently.
    ///
    /// Queueing runs an auto-resolve gate, so an effect the gate accepts never reaches the player at
    /// all. For a MANDATORY clause that is correct and desirable - nobody wants to click through
    /// "draw 1 card". For an OPTIONAL one it is the bug the whole session started from, in its purest
    /// form: the card does its thing and no question is ever asked.
    ///
    /// Every other sweep here starts by pressing Use, so all of them are blind to this: they only see
    /// effects that survived long enough to be pressed. This one looks at the moment BEFORE that.
    ///
    /// The check is: queue an optional clause and assert something is still pending. Anything that
    /// auto-fired took the decision away.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- optionalfires
    /// </summary>
    public static class OptionalNeverAutoFiresSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Does any OPTIONAL effect fire without asking? ===");

            int tested = 0, asked = 0, threw = 0;
            var autoFired = new List<(string Id, string Name, string Clause, string Did)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    // Parenthesised text glosses a cost symbol; it is not a decision.
                    var speakable = Regex.Replace(raw, @"\([^)]*\)", " ");
                    if (!Regex.IsMatch(speakable, @"\byou may\b", RegexOptions.IgnoreCase)) continue;
                    // Reactive "When ..." abilities are dispatched separately and never queued as a
                    // prompt; forcing one through this path invents a question that never existed.
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    if (clause.Length == 0) continue;
                    if (Regex.IsMatch(clause, @"^(When|While)\b", RegexOptions.IgnoreCase)) continue;
                    tested++;

                    try
                    {
                        var b = new Board();
                        var src = b.Character("south", def.Id);
                        if (src == null) continue;
                        int logBefore = b.St.EventLog.Count;

                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);

                        // Anything the player can still answer counts as asked.
                        bool stillAskable = b.St.PendingEffects.Any(e => e != null && e.Seat == "south")
                                         || b.St.ActiveChoice != null || b.St.DeckLook != null;
                        if (stillAskable) { asked++; continue; }

                        // Nothing pending. That is only a defect if the clause actually DID something -
                        // an optional effect the engine declined to run took nothing from the player.
                        var did = b.St.EventLog.Skip(logBefore).Select(l => l.Message ?? "")
                            .Where(m => m.IndexOf("is pending", StringComparison.OrdinalIgnoreCase) < 0
                                     && m.IndexOf("not carried out", StringComparison.OrdinalIgnoreCase) < 0
                                     && m.IndexOf("effect skipped", StringComparison.OrdinalIgnoreCase) < 0
                                     && m.IndexOf("cost cannot be paid", StringComparison.OrdinalIgnoreCase) < 0
                                     && m.IndexOf("acknowledged for manual resolution", StringComparison.OrdinalIgnoreCase) < 0
                                     // Diagnostics, not the effect happening. Counting them would
                                     // inflate the number with clauses that in fact did nothing.
                                     && m.IndexOf("Unknown condition", StringComparison.OrdinalIgnoreCase) < 0
                                     && m.IndexOf("cannot pay the cost", StringComparison.OrdinalIgnoreCase) < 0)
                            .ToList();
                        if (did.Count > 0)
                            autoFired.Add((def.Id, def.Name ?? "", clause, did[0]));
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  optional clauses tested           : {tested}");
            Console.WriteLine($"  the player was asked              : {asked}");
            Console.WriteLine($"  FIRED WITHOUT ASKING              : {autoFired.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            if (autoFired.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  optional effects that took the decision away:");
                foreach (var a in autoFired.Take(25))
                {
                    Console.WriteLine($"    {a.Id,-10} {Trim(a.Name, 16),-16} {Trim(a.Clause, 88)}");
                    Console.WriteLine($"               did: {Trim(a.Did, 96)}");
                }
                if (autoFired.Count > 25) Console.WriteLine($"    ... and {autoFired.Count - 25} more");
            }

            // Unambiguous: the card says "you may", and the engine did it anyway without a prompt.
            // No board makes that correct, so this one is a gate rather than a report.
            return autoFired.Count == 0 ? 0 : 1;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "optional-fires" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 5; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                Hand("south", "ST29-004"); Hand("south", "OP15-020"); Hand("south", "ST29-009");
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                Character("south", "EB03-002");
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-of-don-{serial++}", Rested = false });
                N.CostArea.Add(new DonInstance { InstanceId = $"north-of-don-{serial++}", Rested = false });
                S.DonDeck = 2; N.DonDeck = 2;
                St.PendingEffects.Clear();
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
                InstanceId = $"{owner}-{id}-of-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
