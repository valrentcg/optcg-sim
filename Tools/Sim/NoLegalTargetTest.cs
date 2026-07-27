using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Regression for the reported hard freeze: Gum-Gum Champion Rifle (EB01-028) played as a
    /// [Counter] while the opponent had no Characters. Its second clause, "Then, your opponent
    /// returns 1 of their active Characters to the owner's hand", is MANDATORY - so it queued a
    /// selection with no candidates and no skip button, and the game stopped.
    ///
    /// Comprehensive Rules 1-3-2: "If a player is required to perform an impossible action for
    /// any reason, that action is not carried out. Likewise, if an effect requires the player to
    /// carry out multiple actions, some of which are impossible, the player performs as many of
    /// the actions as possible."
    ///
    /// So the card stays LEGAL to play - it is not a play restriction. The impossible clause is
    /// dropped and everything else still resolves. These lock that reading in both directions:
    /// the impossible clause never stalls, and a clause that IS satisfiable is never eaten.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- notargettest
    /// </summary>
    public static class NoLegalTargetTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Unsatisfiable-clause freeze regression (rule 1-3-2) ===");

            ChampionRifleDoesNotStallOnEmptyBoard();
            ChampionRifleStillQueuesWhenATargetExists();
            ChambresDoesNotStallWithOnlyOneTarget();
            ChambresStillQueuesWithTwoTargets();
            IsukaStyleConditionalIsNotEaten();
            RestAllIsNotEaten();

            Console.WriteLine($"notargettest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // ---- the reported freeze ------------------------------------------------------------

        private static void ChampionRifleDoesNotStallOnEmptyBoard()
        {
            var b = new Fixture();
            // north has NOTHING on board
            QueueClause(b, "EB01-028", "south",
                "your opponent returns 1 of their active Characters to the owner's hand.");
            Check("Champion Rifle clause does not stall when the opponent has no Characters",
                b.St.PendingEffects.Count == 0,
                $"{b.St.PendingEffects.Count} effect(s) left pending");
        }

        private static void ChampionRifleStillQueuesWhenATargetExists()
        {
            var b = new Fixture();
            b.Character("north", "ST01-005");                       // an active Character
            QueueClause(b, "EB01-028", "south",
                "your opponent returns 1 of their active Characters to the owner's hand.");
            Check("Champion Rifle clause DOES queue when the opponent has an active Character",
                b.St.PendingEffects.Count == 1,
                $"{b.St.PendingEffects.Count} pending");
        }

        // ---- the count-limited case (needs TWO) ---------------------------------------------

        private static void ChambresDoesNotStallWithOnlyOneTarget()
        {
            var b = new Fixture();
            b.Character("north", "ST01-005");                       // only ONE candidate
            QueueClause(b, "OP14-017", "south",
                "Select 2 of your opponent's Characters with 9000 base power or less. " +
                "Swap the base power of the selected Characters with each other during this turn.");
            Check("Chambres does not stall when only one of the two required targets exists",
                b.St.PendingEffects.Count == 0,
                $"{b.St.PendingEffects.Count} effect(s) left pending");
        }

        private static void ChambresStillQueuesWithTwoTargets()
        {
            var b = new Fixture();
            b.Character("north", "ST01-005");
            b.Character("north", "ST01-005");
            QueueClause(b, "OP14-017", "south",
                "Select 2 of your opponent's Characters with 9000 base power or less. " +
                "Swap the base power of the selected Characters with each other during this turn.");
            Check("Chambres DOES queue when both required targets exist",
                b.St.PendingEffects.Count == 1,
                $"{b.St.PendingEffects.Count} pending");
        }

        // ---- false positives the guard must NOT swallow --------------------------------------

        // OP02-094 Isuka names the opponent only in a CONDITION; the action is on its own card,
        // and it fires just after that Character was K.O.'d, so the board is routinely empty.
        private static void IsukaStyleConditionalIsNotEaten()
        {
            const string clause =
                "When this Character battles and K.O.'s your opponent's Character, set this Character as active.";

            // The invariant that matters is not "it stays pending" (this clause auto-resolves),
            // it is that the opponent's board must not change the outcome at all. If the guard
            // were reading the condition as a target it would diverge between these two.
            var empty = new Fixture();
            QueueClause(empty, "OP02-094", "south", clause);

            var populated = new Fixture();
            populated.Character("north", "ST01-005");
            QueueClause(populated, "OP02-094", "south", clause);

            Check("Isuka's conditional behaves identically with and without opponent Characters",
                empty.St.PendingEffects.Count == populated.St.PendingEffects.Count,
                $"empty={empty.St.PendingEffects.Count} vs populated={populated.St.PendingEffects.Count} " +
                "— the guard read a condition as a target");
        }

        // OP06-041 "Rest ALL of your opponent's Characters" needs no selection at all.
        private static void RestAllIsNotEaten()
        {
            var b = new Fixture();
            b.Character("north", "ST01-005");
            QueueClause(b, "OP06-041", "south", "Rest all of your opponent's Characters.");
            Check("\"Rest all\" is not treated as a counted selection",
                b.St.PendingEffects.Count <= 1,
                $"{b.St.PendingEffects.Count} pending");
        }

        // ---- plumbing -------------------------------------------------------------------------

        // Drives the real queue path the engine uses for a clause, via the public command surface
        // that reaches QueueAndAutoResolve.
        private static void QueueClause(Fixture b, string sourceCardId, string seat, string clause)
        {
            var src = b.Hand(seat, sourceCardId);
            GameEngine.QueueClauseForTest(b.St, seat, src, "main", clause);
        }

        private static void Check(string name, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  ok    " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
        }

        private sealed class Fixture
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Fixture()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeck = "st01", NorthDeck = "st01", Seed = "no-legal-target",
                });
                St.Status = "active";
                St.Phase = "main";
                St.ActiveSeat = "south";
                St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                St.PendingEffects.Clear();
            }

            public CardInstance Character(string seat, string id, bool rested = false)
            {
                var c = Card(id, seat, "character");
                c.Rested = rested;
                var p = seat == "south" ? S : N;
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            private CardInstance Card(string cardId, string seat, string zone) => new CardInstance
            {
                InstanceId = $"{seat}-nlt-{serial++}",
                CardId = cardId,
                Owner = seat,
                Zone = zone,
            };
        }
    }
}
