using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Regression for confirmed report 20260724-134024-044: IntermediateBot played all four
    /// Fullaleads (OP09-099) across two turns, trashing three copies for no gain — the deploy loop
    /// had a "play over the weakest Character" guard but nothing stopped a redundant Stage swap, and
    /// Stage replacement trashes the existing Stage. These lock the fix while proving a genuine Stage
    /// upgrade still happens (so replacement isn't disabled globally).
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- stagereplacetest
    /// </summary>
    public static class StageReplacementTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Stage replacement bot regression ===");
            DoesNotReplaceIdenticalNoOnPlayStage();
            KeepsStageOverAnEqualValueSidegrade();
            ReplacesWithAClearUpgrade();
            Console.WriteLine($"stagereplacetest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // The exact reported bug: Fullalead is already out and an identical copy sits in hand. Fullalead's
        // search is [Activate: Main] (no [On Play] ETB), so replacing it is pure card loss — never do it.
        private static void DoesNotReplaceIdenticalNoOnPlayStage()
        {
            var b = new Board();
            b.SetStage("south", "OP09-099");
            var dup = b.Hand("south", "OP09-099");
            b.Don("south", 3);
            var cmd = IntermediateBot.DecideOneCommand(b.St, "south", new HashSet<string>());
            Check("bot does not replace an active Stage with an identical no-[On Play] copy (Fullalead)",
                !IsPlayOf(cmd, dup));
        }

        // A different Stage of equal net value is a sidegrade: swapping just trashes a card for nothing.
        private static void KeepsStageOverAnEqualValueSidegrade()
        {
            CardData.UpsertCard("TEST-STAGE-A", "Stage A", "stage", "Black", 2, 0);
            CardData.UpsertCard("TEST-STAGE-B", "Stage B", "stage", "Black", 2, 0);
            var b = new Board();
            b.SetStage("south", "TEST-STAGE-A");
            var side = b.Hand("south", "TEST-STAGE-B");
            b.Don("south", 5);
            var cmd = IntermediateBot.DecideOneCommand(b.St, "south", new HashSet<string>());
            Check("bot keeps its Stage rather than swapping for an equal-value sidegrade",
                !IsPlayOf(cmd, side));
        }

        // A clearly higher-value Stage SHOULD replace a weak one — the guard must not disable upgrades.
        private static void ReplacesWithAClearUpgrade()
        {
            CardData.UpsertCard("TEST-STAGE-WEAK", "Weak Stage", "stage", "Black", 1, 0);
            CardData.UpsertCard("TEST-STAGE-STRONG", "Strong Stage", "stage", "Black", 4, 0,
                effect: "[Activate: Main] Look at 3 cards from the top of your deck; add 1 to your hand.");
            var b = new Board();
            b.SetStage("south", "TEST-STAGE-WEAK");
            var upgrade = b.Hand("south", "TEST-STAGE-STRONG");
            b.Don("south", 6);
            var cmd = IntermediateBot.DecideOneCommand(b.St, "south", new HashSet<string>());
            Check("bot replaces a weak Stage with a clearly higher-value one",
                IsPlayOf(cmd, upgrade));
        }

        private static bool IsPlayOf(GameCommand cmd, CardInstance card) =>
            cmd != null && cmd.Type == "playCard" && cmd.InstanceId == card.InstanceId;

        private static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeck = "st01", NorthDeck = "st01", Seed = "stage-replace",
                });
                St.Status = "active";
                St.Phase = "main";
                St.ActiveSeat = "south";
                St.TurnNumber = 8;
                S.TurnsStarted = 4;
                N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear();
                N.Hand.Clear();
                S.CostArea.Clear();
                N.CostArea.Clear();
                // Rest the Leader so no attack is available — the only candidate action is the Stage
                // decision under test, so a "no play" resolves to endTurn rather than a swing.
                S.Leader.Rested = true;
                N.Leader.Rested = true;
                S.Stage = null;
                N.Stage = null;
            }

            public CardInstance SetStage(string seat, string id)
            {
                var c = Card(id, seat, "stage");
                (seat == "south" ? S : N).Stage = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-stage-don-{serial++}", Rested = false });
            }

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-stg-{serial++}",
                CardId = id,
                Owner = owner,
                Zone = zone,
                Rested = false,
                PlayedOnTurn = 0,
            };
        }
    }
}
