using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// OP10-088 Nami is the only card the brief names that sits inside `usevsskip`'s 75-clause
    /// "Use and Skip are indistinguishable" residual. That residual is documented as FIXTURE
    /// MISMATCH rather than defect — a cost the sweep's board cannot pay is correctly inert — but
    /// "the fixture cannot pay it" is an assumption, and on a card the user named by name it is
    /// worth converting into a measurement.
    ///
    ///     [Activate: Main] You may rest this Character and 1 of your {Dressrosa} type Leader
    ///                      or Stage cards: Draw 1 card.
    ///
    /// Three shapes stacked in one cost, and the engine has broken each of them before:
    ///
    ///   COMPOUND      two components joined by "and" — both must be paid, or neither
    ///   ALTERNATIVE   "Leader OR Stage" — a choice of WHICH permanent pays
    ///   TYPE-FILTERED {Dressrosa} — read from the card's feature, not its name
    ///
    /// The reason this needs all three directions tested: a cost that is silently treated as
    /// unpayable and a cost that is silently free look identical in a Use-vs-Skip differential run
    /// against a board that cannot pay it. Only a board that CAN pay separates them.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- altcost
    /// </summary>
    public static class AlternativeCostTest
    {
        private static int passed, failed;

        // A {Dressrosa} Leader, a {Dressrosa} Stage, and a Leader that is NOT Dressrosa.
        private const string DressrosaLeader = "OP10-042";
        private const string DressrosaStage  = "OP04-096";
        private const string PlainLeader     = "ST01-001";

        public static int Run()
        {
            Console.WriteLine("=== Compound / alternative / type-filtered cost (OP10-088 Nami) ===");
            PayableViaTheLeaderBranch();
            PayableViaTheStageBranch();
            UnpayableWithoutADressrosaPermanent();
            Console.WriteLine($"altcost: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private const string Clause =
            "You may rest this Character and 1 of your {Dressrosa} type Leader or Stage cards: Draw 1 card.";

        private static void PayableViaTheLeaderBranch()
        {
            var b = new Board(DressrosaLeader, stageId: null);
            int hand0 = b.S.Hand.Count;

            b.Drive(Clause);

            bool namiRested = b.S.CharacterArea[0]?.Rested == true;
            bool leaderRested = b.S.Leader?.Rested == true;
            int drew = b.S.Hand.Count - hand0;
            Check("payable through the LEADER branch: both components rest, and the body fires",
                  drew == 1 && namiRested && leaderRested,
                  $"drew {drew} (want 1), namiRested={namiRested}, leaderRested={leaderRested} — "
                  + "a compound cost must charge BOTH components");
        }

        private static void PayableViaTheStageBranch()
        {
            var b = new Board(PlainLeader, stageId: DressrosaStage);
            int hand0 = b.S.Hand.Count;

            b.Drive(Clause);

            bool namiRested = b.S.CharacterArea[0]?.Rested == true;
            bool stageRested = b.S.Stage?.Rested == true;
            int drew = b.S.Hand.Count - hand0;
            Check("payable through the STAGE branch when the Leader is not {Dressrosa}",
                  drew == 1 && namiRested && stageRested,
                  $"drew {drew} (want 1), namiRested={namiRested}, stageRested={stageRested} — "
                  + "the \"or\" must reach the Stage, not only the Leader");
        }

        /// <summary>The direction that separates "unpayable" from "free". Without this, a cost the
        /// engine silently ignores and a cost it silently refuses look the same.</summary>
        private static void UnpayableWithoutADressrosaPermanent()
        {
            var b = new Board(PlainLeader, stageId: null);
            int hand0 = b.S.Hand.Count;

            b.Drive(Clause);

            int drew = b.S.Hand.Count - hand0;
            bool leaderRested = b.S.Leader?.Rested == true;
            Check("NOT payable with no {Dressrosa} Leader or Stage — and the body does not fire",
                  drew == 0 && !leaderRested,
                  $"drew {drew} (want 0), leaderRested={leaderRested} — a body that fires here means "
                  + "the type filter is not read and the cost is effectively free");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int serial;

            public Board(string leaderId, string stageId)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "alt-cost" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009" }) p.Hand.Add(Make(id, p.Seat, "hand"));
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    foreach (var c in p.Life) c.FaceUp = false;
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ac-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.AbilityUsedThisTurn.Clear();
                }
                S.Leader = Make(leaderId, "south", "leader");
                S.Leader.Rested = false;
                if (stageId != null) { S.Stage = Make(stageId, "south", "stage"); S.Stage.Rested = false; }
                S.CharacterArea[0] = Make("OP10-088", "south", "character");
                St.PendingEffects.Clear();
            }

            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 10; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
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
                    if (p.Stage != null) yield return p.Stage;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ac-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
