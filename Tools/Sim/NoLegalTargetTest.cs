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

            // ── the class sweep turned up: MANDATORY selections of your OWN board ──
            OwnBoardSelectionDoesNotStallWhenEmpty();
            OwnBoardSelectionDoesNotStallWhenTypeGateMatchesNothing();
            OwnBoardSelectionStillQueuesWhenTheTypeMatches();
            TallyClauseIsNotEaten();
            RiderDoesNotDecideTheTargetZone();
            AddToLifeCostActuallyMovesTheCharacter();

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

        // ---- own-board selections (OP04-079 Orlumbus, OP06-006 Saga, OP14-001 Law) -----------

        private static void OwnBoardSelectionDoesNotStallWhenEmpty()
        {
            var b = new Fixture();                       // south has NO Characters
            QueueClause(b, "OP04-079", "south", "K.O. 1 of your {Dressrosa} type Characters.");
            Check("own-board K.O. does not stall with no Characters at all",
                b.St.PendingEffects.Count == 0, $"{b.St.PendingEffects.Count} pending");
        }

        // The board is populated, but nothing matches the type gate — the case a plain
        // "is the area empty" check would miss entirely.
        private static void OwnBoardSelectionDoesNotStallWhenTypeGateMatchesNothing()
        {
            var b = new Fixture();
            b.Character("south", "ST01-005");             // not a {Dressrosa} Character
            QueueClause(b, "OP04-079", "south", "K.O. 1 of your {Dressrosa} type Characters.");
            Check("own-board K.O. does not stall when no Character matches the type gate",
                b.St.PendingEffects.Count == 0, $"{b.St.PendingEffects.Count} pending");
        }

        private static void OwnBoardSelectionStillQueuesWhenTheTypeMatches()
        {
            var b = new Fixture();
            b.Character("south", "ST01-005");
            QueueClause(b, "OP04-079", "south", "K.O. 1 of your Characters.");   // no gate
            Check("own-board K.O. DOES queue when a legal Character exists",
                b.St.PendingEffects.Count == 1, $"{b.St.PendingEffects.Count} pending");
        }

        // "gains +1000 power for every 3 of your … Characters" is a TALLY, not a selection.
        // The counted-selection regex must not read it as one (EB01-014 Sanji).
        private static void TallyClauseIsNotEaten()
        {
            const string clause =
                "This Character gains +1000 power for every 3 of your {Germa 66} type Characters.";
            var empty = new Fixture();
            QueueClause(empty, "EB01-014", "south", clause);
            var populated = new Fixture();
            populated.Character("south", "ST01-005");
            QueueClause(populated, "EB01-014", "south", clause);
            Check("\"for every N of your …\" tally behaves identically regardless of board",
                empty.St.PendingEffects.Count == populated.St.PendingEffects.Count,
                $"empty={empty.St.PendingEffects.Count} vs populated={populated.St.PendingEffects.Count}");
        }

        // A ". Then, …" rider must not decide what the player is asked for FIRST. OP15-020 Fire
        // Fist ends in "trash 2 cards from your hand", which made the whole effect target the
        // HAND — so the UI demanded a discard before the board effect that comes first.
        private static void RiderDoesNotDecideTheTargetZone()
        {
            var b = new Fixture();
            b.Character("north", "ST01-005");
            QueueClause(b, "OP15-020", "south",
                "Your Leader gains +3000 power during this turn and give up to 1 of your opponent's " +
                "Characters -8000 power until the end of your opponent's next End Phase. " +
                "Then, you may trash 2 cards from your hand. If you do, K.O. up to 1 of your " +
                "opponent's Characters with 0 power or less.");
            var pe = b.St.PendingEffects.FirstOrDefault();
            Check("a \". Then, … from your hand\" rider does not retarget the effect to the hand",
                pe == null || pe.TargetZone != EffectTargetZone.Hand,
                pe == null ? "no pending effect" : $"TargetZone={pe.TargetZone}");
        }

        // ST13-001 Sabo: "You may add 1 of your Characters with a cost of 3 or more and 7000 power
        // or more to the top of your Life cards face-up: <benefit>". "add" was not a recognised cost
        // verb, so the whole effect was NotAutomated. Checks the cost is really PAID, not just parsed.
        private static void AddToLifeCostActuallyMovesTheCharacter()
        {
            var b = new Fixture();
            var big = b.Character("south", "ST01-005");        // a real Character (not a Leader) to pay with
            int lifeBefore = b.S.Life.Count;

            GameEngine.QueueClauseForTest(b.St, "south", b.Hand("south", "ST13-001"), "activateMain",
                "You may add 1 of your Characters to the top of your Life cards face-up: " +
                "Up to 1 of your Characters gains +2000 power until the start of your next turn.");

            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("add-to-Life cost queues a selection", false, "no pending effect"); return; }
            string paidId = big.InstanceId;
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            {
                Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = paidId,
            });

            // ApplyCommand returns a fresh state, so compare by id rather than by reference.
            bool offBoard = !b.S.CharacterArea.Any(c => c != null && c.InstanceId == paidId);
            var inLife = b.S.Life.FirstOrDefault(c => c.InstanceId == paidId);
            Check("add-to-Life cost moves the Character to the top of Life, face-up",
                offBoard && inLife != null && inLife == b.S.Life.Last() && inLife.FaceUp,
                $"offBoard={offBoard} inLife={inLife != null} onTop={inLife != null && inLife == b.S.Life.Last()} faceUp={inLife?.FaceUp} lifeBefore={lifeBefore} lifeNow={b.S.Life.Count}");
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
