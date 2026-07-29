using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// An audit of the highest-reach fix in this workstream: "up to N" is a CEILING, not a
    /// requirement. 1,601 distinct cards carry the wording, and the fix made
    /// ClauseHasNoLegalCharacterTarget treat "up to N" as needing only ONE legal target, so a
    /// clause is retired solely when there are ZERO.
    ///
    /// Rule 8-4-4-1 is more generous than that: with "up to", the player may choose **0**. So an
    /// "up to N" clause facing an empty board is not unresolvable — it resolves and does nothing.
    /// The distinction is invisible for a lone clause (same board either way) and very visible for a
    /// COMPOUND one:
    ///
    ///     "K.O. up to 1 of your opponent's Characters. Then, draw 1 card."
    ///
    /// against an empty board. Retiring the clause because its first half has no target would take
    /// the draw with it — the player loses a card they were entitled to, and the log reads as a
    /// reasonable "no legal target" message either way.
    ///
    /// The same question for a cost-prefixed compound is the one that actually costs something: pay
    /// a card, get nothing, because the first half of the body had no target.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- uptonrider
    /// </summary>
    public static class UpToNRiderTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== \"Up to N\" with nothing to hit: does the rider still run? ===");
            RiderRunsWhenTheUpToNHasNoTarget();
            RiderRunsWhenTheBoardIsFull();
            CostIsNotTakenForAnEmptyBody();
            UpToTwoWithOneCandidateStillKOsThatOne();
            Console.WriteLine($"uptonrider: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>The empty-board case: "up to 1" hits nothing, and the draw must still happen.</summary>
        private static void RiderRunsWhenTheUpToNHasNoTarget()
        {
            var b = new Board();                 // north has NO Characters
            int hand0 = b.S.Hand.Count;

            b.Resolve("K.O. up to 1 of your opponent's Characters. Then, draw 1 card.");

            Check("with no legal target, the \". Then,\" rider still runs",
                  b.S.Hand.Count == hand0 + 1,
                  $"hand {hand0}->{b.S.Hand.Count} — the whole clause was retired because its FIRST "
                  + "half had nothing to hit, taking the rider with it (rule 8-4-4-1 allows 0)");
        }

        /// <summary>The control: with a target present the rider must also run, so the case above is
        /// not passing for some unrelated reason.</summary>
        private static void RiderRunsWhenTheBoardIsFull()
        {
            var b = new Board();
            var victim = b.Character("north", "OP15-040");
            int hand0 = b.S.Hand.Count;

            b.Resolve("K.O. up to 1 of your opponent's Characters. Then, draw 1 card.", victim.InstanceId);

            bool gone = b.N.CharacterArea.All(c => c == null || c.InstanceId != victim.InstanceId);
            Check("with a legal target, the K.O. lands AND the rider runs",
                  gone && b.S.Hand.Count == hand0 + 1,
                  $"victimGone={gone} hand {hand0}->{b.S.Hand.Count}");
        }

        /// <summary>The version that costs the player something. If the body's first half has no
        /// target, the cost must not be taken for nothing — either the whole thing is declined, or
        /// the rider delivers.</summary>
        private static void CostIsNotTakenForAnEmptyBody()
        {
            var b = new Board();                 // north has NO Characters
            int hand0 = b.S.Hand.Count;

            b.Resolve("You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters. "
                      + "Then, draw 1 card.");

            int net = b.S.Hand.Count - hand0;
            // Paid 1, drew 1 => net 0. Paid 1 and got nothing => -1, which is the defect.
            Check("a cost paid against an empty board still delivers the rider",
                  net >= 0,
                  $"net hand change {net:+#;-#;0} — the cost was taken and the rider never ran");
        }

        /// <summary>The case that actually exercises the ceiling, and the reason this file was
        /// rewritten: with ZERO candidates the clause is retired whether "up to N" means a ceiling
        /// or an exact count, so the empty-board cases above pass identically with the fix REVERTED.
        /// They assert something real (the rider survives) but audit nothing.
        ///
        /// The ceiling only decides anything strictly between 1 and N-1 candidates — "up to 2" with
        /// exactly ONE legal victim, which is the OP12-038 scenario the fix was written for: the
        /// player rests 2 DON!! and, without the ceiling, gets neither the K.O. nor the DON!! back.
        /// </summary>
        private static void UpToTwoWithOneCandidateStillKOsThatOne()
        {
            var b = new Board();
            var only = b.Character("north", "OP15-040");   // exactly ONE legal victim for "up to 2"
            int hand0 = b.S.Hand.Count;

            b.Resolve("K.O. up to 2 of your opponent's Characters. Then, draw 1 card.", only.InstanceId);

            bool gone = b.N.CharacterArea.All(c => c == null || c.InstanceId != only.InstanceId);
            Check("\"up to 2\" with ONE candidate still K.O.s that one",
                  gone && b.S.Hand.Count == hand0 + 1,
                  $"victimGone={gone} hand {hand0}->{b.S.Hand.Count} — \"up to N\" is being read as "
                  + "EXACTLY N, so one legal target is treated as none");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "upto-n-rider" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ur-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                foreach (var id in new[] { "EB01-004", "EB01-005", "EB01-006" })
                    S.Hand.Add(Make(id, "south", "hand"));
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                southSlot = 1;
                St.PendingEffects.Clear();
            }

            public void Resolve(string clause, string target = null)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 8; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string t = target ?? Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = t });
                    target = null;                       // the named target is used once
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(6)) Console.WriteLine("      log: " + e.Message);
            }

            public System.Collections.Generic.IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Make(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ur-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
