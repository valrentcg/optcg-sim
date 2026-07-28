using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Generalises the OP15-114 Wyper bug: its ". Then, K.O. all of your opponent's Characters with 0
    /// power or less" was queued and then never run, because IsAutomatedEffectPattern did not recognise
    /// the shape. The resolver handled it perfectly — it was only the auto-resolve GATE that did not know
    /// to fire it. Nothing crashed, nothing logged, the payoff just never happened.
    ///
    /// That failure is mechanically detectable, so this looks for every other instance of it:
    ///
    ///   a clause that is NOT an opt-in ("you may"), that the recognition gate REJECTS, but which the
    ///   resolver will happily resolve when handed a null target and which visibly DOES something.
    ///
    /// Anything matching that is queued in game and sits there waiting for a click it does not need.
    /// Clauses that genuinely need a pick return WaitingForTarget instead and are not reported.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- stallsweep
    /// </summary>
    public static class SilentStallSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Silent-stall sweep: clauses that need no target but are never auto-run ===");

            var findings = new List<(string Id, string Name, string Clause, string Did)>();
            int clausesTested = 0, cardsScanned = 0, threw = 0;

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                cardsScanned++;
                foreach (var clause in Clauses(def.Effect))
                {
                    // An opt-in is SUPPOSED to wait for the player.
                    if (clause.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    // A "<cost>:" prefix is a payment step and also supposed to wait.
                    if (Regex.IsMatch(clause, @"^[^.]{0,80}:\s")) continue;
                    // Already recognised → it auto-resolves today, nothing to find.
                    if (GameEngine.AuditEffectRecognized(clause)) continue;
                    clausesTested++;

                    try
                    {
                        var b = new Board();
                        var src = b.Character("south", "ST29-010");
                        b.Populate();
                        int logBefore = b.St.EventLog.Count;
                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                        if (b.St.PendingEffects.Count == 0) continue;   // retired/handled — not a stall

                        var pe = b.St.PendingEffects[0];
                        b.Apply(new GameCommand
                        { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = null });

                        // Resolved by a null target AND it visibly did something ⇒ it never needed the
                        // click, so leaving it pending is the Wyper failure.
                        if (b.St.PendingEffects.Count != 0) continue;   // genuinely wanted a pick
                        // "acknowledged for manual resolution" is the NOT-AUTOMATED fallback: the engine
                        // gives up on the clause and clears it. That is a different problem (an
                        // unimplemented effect), not a clause that works but is never fired, so it must
                        // not count as a payoff or it drowns the real findings ~10:1.
                        var did = b.St.EventLog.Skip(logBefore).Select(l => l.Message)
                                   .Where(m => m.IndexOf("is pending", StringComparison.OrdinalIgnoreCase) < 0
                                            && m.IndexOf("acknowledged for manual resolution", StringComparison.OrdinalIgnoreCase) < 0)
                                   .ToList();
                        if (did.Count == 0) continue;                   // resolved to a no-op — not a payoff
                        findings.Add((def.Id, def.Name, Trim(clause, 96), Trim(did[did.Count - 1], 76)));
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  scanned {cardsScanned} cards, tested {clausesTested} unrecognised non-optional clauses"
                              + (threw > 0 ? $" ({threw} threw)" : ""));
            Console.WriteLine($"  clauses that resolve untargeted but are left waiting: {findings.Count}");
            foreach (var f in findings.Take(60))
                Console.WriteLine($"    {f.Id,-10} {Trim(f.Name, 20),-20} :: {f.Clause}\n{"",-34}→ {f.Did}");
            if (findings.Count > 60) Console.WriteLine($"    … and {findings.Count - 60} more");

            // Reporting tool, not a gate: it prints what it finds and always exits 0 so it can be run
            // against a dirty tree while the findings are being triaged.
            return 0;
        }

        // One clause per line, then split again at a sentence-level ". Then," / ". After that," — the
        // rider is queued as its own effect, and the rider is exactly where Wyper's stall lived.
        private static IEnumerable<string> Clauses(string effect)
        {
            foreach (var line in effect.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Only a TAGGED clause is ever queued as an effect. An untagged line is continuous or
                // reactive text — "All of your Characters … cannot be K.O.'d", "The cost of playing … is
                // reduced", "Under the rules of this game …", "When this Character is K.O.'d, …" — read
                // by aura scans and reactive dispatchers, never handed to the pending panel. Reporting
                // them as clauses that "wait for a click" is meaningless, and they were 8 of the
                // remaining findings.
                if (!Regex.IsMatch(line, @"^\s*\[[^\]]+\]")) continue;
                foreach (var part in Regex.Split(line, @"(?<=\.)\s*(?:Then|After that),\s*"))
                {
                    var c = StripTags(part).Trim();
                    // "(This card can attack on the turn in which it is played.)" and friends are keyword
                    // REMINDER text, not clauses — they are never queued in a real game.
                    if (c.StartsWith("(")) continue;
                    if (c.Length >= 12) yield return c;
                }
            }
        }

        private static string StripTags(string s) =>
            Regex.Replace(s ?? "", @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();

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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "silent-stall" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0; N.Leader.PlayedOnTurn = 0;
                S.Leader.AttachedDonIds.Clear(); N.Leader.AttachedDonIds.Clear();
                St.PendingEffects.Clear();
            }

            // A board with something of everything, so a clause is not reported as a no-op merely because
            // the fixture gave it nothing to act on.
            public void Populate()
            {
                Character("north", "ST29-009"); Character("north", "OP15-040"); Character("north", "ST29-010");
                Character("south", "ST29-009");
                Hand("south", "ST29-004"); Hand("south", "OP15-020");
                Hand("north", "ST29-004");
                Life("south", 3); Life("north", 3);
                Don("south", 8); Don("north", 4);
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public void Life(string seat, int n)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < n; i++) p.Life.Add(Card("ST01-005", seat, "life"));
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-ss-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ss-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
