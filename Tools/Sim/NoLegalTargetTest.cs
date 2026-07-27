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
            HalfPaidCostIsRefundedButAFullyPaidOneIsNot();
            RedAceLeaderTargetsItsDottedNameFilter();
            OldRedAceLeaderScalesWithEveryCardTrashed();
            OldRedAceAlsoTriggersOnDefence();
            MandatoryHandDiscardWithNoHandDoesNotFreeze();
            OtherThanExclusionIsEnforcedByTheResolver();
            NamedCostCardMustActuallyBeThatCard();
            OtherThanIsNotReadAsARequirement();
            ACostOfferingAChoiceCanBePaidEitherWay();
            GiveAllOpponentCharactersHitsEveryoneAndNeverWaits();

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

            // Compare by id: ApplyCommand mutates the state in place and hands the same object back,
            // but the instances inside it move between zone lists, so a captured reference is not a
            // reliable way to ask "where is this card now".
            bool offBoard = !b.S.CharacterArea.Any(c => c != null && c.InstanceId == paidId);
            var inLife = b.S.Life.FirstOrDefault(c => c.InstanceId == paidId);
            Check("add-to-Life cost moves the Character to the top of Life, face-up",
                offBoard && inLife != null && inLife == b.S.Life.Last() && inLife.FaceUp,
                $"offBoard={offBoard} inLife={inLife != null} onTop={inLife != null && inLife == b.S.Life.Last()} faceUp={inLife?.FaceUp} lifeBefore={lifeBefore} lifeNow={b.S.Life.Count}");
        }

        // A multi-item optional cost ("You may trash 2 cards from your hand: <benefit>") is clicked one
        // card at a time and stays skippable the whole way through, so bailing out after the first
        // click used to LOSE that card for nothing. Payment is now all-or-nothing. 24 cards can reach
        // this (20 hand-trash, 4 board-rest). Both directions are locked: the refund must happen when
        // the cost is abandoned, and must NOT happen once it has been paid in full.
        private static void HalfPaidCostIsRefundedButAFullyPaidOneIsNot()
        {
            const string Clause = "You may trash 2 cards from your hand: Draw 2 cards.";

            // (a) pay 1 of 2, then skip → the trashed card comes back.
            var b = new Fixture();
            var c1 = b.Hand("south", "ST01-005");
            b.Hand("south", "ST01-005");
            GameEngine.QueueClauseForTest(b.St, "south", b.Hand("south", "OP03-076"), "main", Clause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("half-paid cost is refunded on skip", false, "no pending effect"); return; }
            string eid = pe.EffectId;
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = eid, Target = c1.InstanceId });
            bool wentToTrash = b.S.Trash.Any(c => c.InstanceId == c1.InstanceId);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "passEffect", Seat = "south", EffectId = eid });
            Check("half-paid cost is refunded on skip",
                wentToTrash && b.S.Hand.Any(c => c.InstanceId == c1.InstanceId)
                    && !b.S.Trash.Any(c => c.InstanceId == c1.InstanceId),
                $"paid={wentToTrash} backInHand={b.S.Hand.Any(c => c.InstanceId == c1.InstanceId)}");

            // (b) pay both, then skip whatever the BODY queues → the cost stays paid.
            var f = new Fixture();
            var d1 = f.Hand("south", "ST01-005");
            var d2 = f.Hand("south", "ST01-005");
            GameEngine.QueueClauseForTest(f.St, "south", f.Hand("south", "OP03-076"), "main", Clause);
            var pe2 = f.St.PendingEffects.FirstOrDefault();
            if (pe2 == null) { Check("a fully paid cost is never refunded", false, "no pending effect"); return; }
            string eid2 = pe2.EffectId;
            foreach (var pick in new[] { d1.InstanceId, d2.InstanceId })
                f.St = GameEngine.ApplyCommand(f.St, new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = eid2, Target = pick });
            foreach (var rest in f.St.PendingEffects.ToList())
                f.St = GameEngine.ApplyCommand(f.St, new GameCommand
                { Type = "passEffect", Seat = "south", EffectId = rest.EffectId });
            Check("a fully paid cost is never refunded",
                f.S.Trash.Any(c => c.InstanceId == d1.InstanceId) && f.S.Trash.Any(c => c.InstanceId == d2.InstanceId)
                    && !f.S.Hand.Any(c => c.InstanceId == d1.InstanceId || c.InstanceId == d2.InstanceId),
                $"d1InTrash={f.S.Trash.Any(c => c.InstanceId == d1.InstanceId)} d2InTrash={f.S.Trash.Any(c => c.InstanceId == d2.InstanceId)}");
        }

        // OP16-001 Portgas.D.Ace (red Leader) — a live check on the dotted-name fix from a card that
        // was NOT one of the ones repaired. Its [Activate: Main] reads "Up to 1 of your
        // [Monkey.D.Luffy] Characters or up to 1 of your Characters with a type including
        // "Whitebeard Pirates", with 8000 power or more, gains [Rush] during this turn."
        // Card names carry periods, and a [^.] -bounded filter stops at the first one, so a name
        // filter that is not dot-safe silently matches NOTHING and the Leader's whole ability is dead
        // with no error. Both branches of the OR are exercised, plus a Character that matches neither.
        private static void RedAceLeaderTargetsItsDottedNameFilter()
        {
            const string Clause = "Up to 1 of your [Monkey.D.Luffy] Characters or up to 1 of your " +
                "Characters with a type including \"Whitebeard Pirates\", with 8000 power or more, " +
                "gains [Rush] during this turn.";

            Check("red Ace grants Rush to a dotted-name [Monkey.D.Luffy] Character",
                AceGrantsRush(Clause, "OP04-014"), "OP04-014 Monkey.D.Luffy 9000");
            Check("red Ace grants Rush to a {Whitebeard Pirates} Character",
                AceGrantsRush(Clause, "OP02-007"), "OP02-007 Thatch 8000");
            Check("red Ace does NOT grant Rush to a Character matching neither branch",
                !AceGrantsRush(Clause, "EB01-023"), "EB01-023 Edward Weevil 8000, no matching name or type");

            // The trailing ", with 8000 power or more," is read as qualifying BOTH alternatives: the
            // pool's normal way to hang two filters on ONE description is "…and 8000 power or more"
            // with no commas (ST13-001 Sabo), so the comma-delimited form is doing something else.
            // Not confirmed against the official Q&A — that page renders its entries via script and
            // could not be read.
            Check("red Ace does NOT grant Rush to an under-power [Monkey.D.Luffy]",
                !AceGrantsRush(Clause, "OP01-024"), "OP01-024 Monkey.D.Luffy 3000 — below the 8000 gate");

            // The glow filter decides what is CLICKABLE, so a target the resolver accepts but the glow
            // rejects is unreachable in the real game — the bug would look identical to the player.
            Check("both branches glow as clickable targets",
                AceGlows(Clause, "OP04-014") && AceGlows(Clause, "OP02-007"),
                $"luffy={AceGlows(Clause, "OP04-014")} whitebeard={AceGlows(Clause, "OP02-007")}");
        }

        private static bool AceGlows(string clause, string targetCardId)
        {
            var b = new Fixture();
            var target = b.Character("south", targetCardId);
            GameEngine.QueueClauseForTest(b.St, "south", b.Hand("south", "OP16-001"), "activateMain", clause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            return pe != null && GameEngine.IsValidEffectTarget(b.St, pe, target);
        }

        private static bool AceGrantsRush(string clause, string targetCardId)
        {
            var b = new Fixture();
            var target = b.Character("south", targetCardId);
            string tid = target.InstanceId;
            GameEngine.QueueClauseForTest(b.St, "south", b.Hand("south", "OP16-001"), "activateMain", clause);

            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) return false;
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = tid });

            var after = b.S.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == tid);
            return after != null && GameEngine.HasRush(b.St, after);
        }

        // OP03-001 Portgas.D.Ace, the ORIGINAL red Ace Leader: "When this Leader attacks or is attacked,
        // you may trash any number of Event or Stage cards from your hand. This Leader gains +1000 power
        // during this battle for every card trashed."
        //
        // Three ways this shape goes wrong, all silent: the buff does not SCALE (a generic "+N power"
        // handler reads the first number and grants a flat +1000 while trashing nothing — the player gets
        // the buff for free), the "Event or Stage" type filter is not enforced (any hand card pays), and
        // the unbounded pick stops after one card. All three are checked here, on a real battle.
        private static void OldRedAceLeaderScalesWithEveryCardTrashed()
        {
            var b = new Fixture();
            b.S.Leader.CardId = "OP03-001";              // 5000-power red Ace Leader
            b.S.Leader.Rested = false; b.S.Leader.PlayedOnTurn = 0;
            b.N.Leader.Rested = false; b.N.Leader.PlayedOnTurn = 0;

            var ev1 = b.Hand("south", "EB01-009");       // Event
            var ev2 = b.Hand("south", "EB01-010");       // Event
            var stage = b.Hand("south", "EB01-011");     // Stage — the "or Stage" half of the filter
            var chr = b.Hand("south", "EB01-002");       // Character — must NOT be payable
            string leaderId = b.S.Leader.InstanceId;

            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "declareAttack", Seat = "south", Attacker = leaderId, Target = b.N.Leader.InstanceId });

            var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP03-001");
            Check("old red Ace offers its trash-for-power on attack", pe != null,
                "no pending effect after the Leader attacked");
            if (pe == null) return;
            string eid = pe.EffectId;

            int Bonus() => b.St.Battle != null && b.St.Battle.BattlePowerBonus.TryGetValue(leaderId, out var v) ? v : 0;
            void Pick(string id) => b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = eid, Target = id });

            // A Character in hand does not match "Event or Stage" — it must be refused, and refusing it
            // must not consume the pick or trash the card.
            Pick(chr.InstanceId);
            Check("old red Ace refuses a Character as the trash cost",
                Bonus() == 0 && b.S.Hand.Any(c => c.InstanceId == chr.InstanceId),
                $"bonus={Bonus()} stillInHand={b.S.Hand.Any(c => c.InstanceId == chr.InstanceId)}");

            Pick(ev1.InstanceId);
            int afterOne = Bonus();
            Pick(ev2.InstanceId);
            int afterTwo = Bonus();
            Pick(stage.InstanceId);
            int afterThree = Bonus();

            Check("old red Ace scales +1000 for EVERY card trashed",
                afterOne == 1000 && afterTwo == 2000 && afterThree == 3000,
                $"1 card={afterOne}, 2 cards={afterTwo}, 3 cards={afterThree} (want 1000/2000/3000)");
            Check("old red Ace's Leader power reflects the trashes mid-battle",
                GameEngine.GetPower(b.St, b.S.Leader) == 5000 + 3000,
                $"power={GameEngine.GetPower(b.St, b.S.Leader)} want 8000");
            Check("old red Ace actually trashed the cards it was paid",
                b.S.Trash.Count(c => c.InstanceId == ev1.InstanceId || c.InstanceId == ev2.InstanceId
                                  || c.InstanceId == stage.InstanceId) == 3,
                $"in trash={b.S.Trash.Count(c => c.InstanceId == ev1.InstanceId || c.InstanceId == ev2.InstanceId || c.InstanceId == stage.InstanceId)}");
        }

        // The other half of the same ability: "attacks OR IS ATTACKED". The defensive side is a separate
        // trigger path and is the one a player leans on to survive a swing, so it is checked on its own —
        // an ability that only works on offence would look like the card was half-implemented.
        private static void OldRedAceAlsoTriggersOnDefence()
        {
            var b = new Fixture();
            b.St.ActiveSeat = "north";                   // the opponent is attacking us
            b.S.Leader.CardId = "OP03-001";
            b.S.Leader.Rested = false; b.S.Leader.PlayedOnTurn = 0;
            b.N.Leader.Rested = false; b.N.Leader.PlayedOnTurn = 0;
            var ev = b.Hand("south", "EB01-009");
            string leaderId = b.S.Leader.InstanceId;

            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "declareAttack", Seat = "north", Attacker = b.N.Leader.InstanceId, Target = leaderId });

            var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP03-001");
            if (pe == null)
            {
                Check("old red Ace also triggers when it IS attacked", false, "no pending effect on defence");
                return;
            }
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = ev.InstanceId });

            int bonus = b.St.Battle != null && b.St.Battle.BattlePowerBonus.TryGetValue(leaderId, out var v) ? v : 0;
            Check("old red Ace also triggers when it IS attacked", bonus == 1000,
                $"defensive bonus={bonus} want 1000");
        }

        // A mandatory "Trash N cards from your hand" you cannot pay used to be a hard freeze: nothing is
        // clickable, the pending panel disables Skip because the clause is mandatory, and its "Use Effect"
        // button re-enters the same wait — every control on screen a no-op. Reached by simply playing your
        // LAST card when its [On Play] demands a discard. 5 cards carry this wording (EB03-028 Yu,
        // OP11-083 Caribou, OP11-086 Coribou, OP12-046 Zephyr(Navy), ST27-004 Sanjuan.Wolf).
        private static void MandatoryHandDiscardWithNoHandDoesNotFreeze()
        {
            var b = new Fixture();
            var src = b.Hand("south", "OP11-083");
            b.S.Hand.Remove(src);                       // the source is the last card — hand is now empty
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", "Trash 2 cards from your hand.");
            Check("an unpayable mandatory hand discard retires instead of freezing",
                b.St.PendingEffects.Count == 0,
                $"pending={b.St.PendingEffects.Count}");

            // …but a discard the hand CAN cover is still a cost that must be paid, not a free decline.
            var f = new Fixture();
            var keep1 = f.Hand("south", "ST01-005");
            var keep2 = f.Hand("south", "ST01-005");
            var src2 = f.Hand("south", "OP11-083"); f.S.Hand.Remove(src2);
            GameEngine.QueueClauseForTest(f.St, "south", src2, "main", "Trash 2 cards from your hand.");
            Check("a payable mandatory hand discard is NOT retired",
                f.St.PendingEffects.Count == 1 || f.S.Trash.Count == 2,
                $"pending={f.St.PendingEffects.Count} trash={f.S.Trash.Count} hand={f.S.Hand.Count} " +
                $"(cards {keep1.InstanceId != null} {keep2.InstanceId != null})");
        }

        // "other than [Name]" was enforced only by the GLOW, so the resolver happily accepted the one
        // card the text rules out — OP06-107 Kouzuki Momonosuke could add HIMSELF to Life on a clause
        // reading "Add up to 1 of your {Land of Wano} type Characters other than [Kouzuki Momonosuke]".
        // The exclusion now lives in one helper both sides call.
        private static void OtherThanExclusionIsEnforcedByTheResolver()
        {
            const string Clause = "Add up to 1 of your {Land of Wano} type Characters other than " +
                "[Kouzuki Momonosuke] to the top or bottom of the owner's Life cards face-up.";

            var b = new Fixture();
            var momo = b.Character("south", "OP06-107");          // the excluded card, on the board
            int lifeBefore = b.S.Life.Count;
            GameEngine.QueueClauseForTest(b.St, "south", momo, "main", Clause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("\"other than [Name]\" is enforced when resolving", false, "no pending effect"); return; }

            GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = momo.InstanceId });

            bool stillOnBoard = b.S.CharacterArea.Any(c => c != null && c.InstanceId == momo.InstanceId);
            Check("\"other than [Name]\" is enforced when resolving, not just when glowing",
                stillOnBoard && b.S.Life.Count == lifeBefore,
                $"onBoard={stillOnBoard} life {lifeBefore}->{b.S.Life.Count}");
            Check("the excluded card does not glow either",
                !GameEngine.IsValidEffectTarget(b.St, pe, momo));
        }

        // "You may trash 1 [Ice Oni] from your hand …: Play 1 [Ice Oni] from your trash." (OP04-055 Plague
        // Rounds). The [Name] on a from-hand cost was never checked, so ANY card paid it — and that then
        // FROZE the game, because paying the cost correctly is what puts an [Ice Oni] in the trash for the
        // mandatory body to play. Paying with the wrong card left the body with no legal target and no way
        // to dismiss it. One unchecked filter, two bugs.
        private static void NamedCostCardMustActuallyBeThatCard()
        {
            const string Clause = "You may trash 1 [Ice Oni] from your hand: Play 1 [Ice Oni] from your trash.";

            var b = new Fixture();
            var wrong = b.Hand("south", "ST01-005");            // Jinbe — not an [Ice Oni]
            var src = b.Hand("south", "OP04-055");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", Clause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("a named cost refuses the wrong card", false, "no pending effect"); return; }

            GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = wrong.InstanceId });

            Check("a named cost refuses the wrong card",
                b.S.Hand.Any(c => c.InstanceId == wrong.InstanceId)
                    && !b.S.Trash.Any(c => c.InstanceId == wrong.InstanceId),
                $"stillInHand={b.S.Hand.Any(c => c.InstanceId == wrong.InstanceId)} inTrash={b.S.Trash.Any(c => c.InstanceId == wrong.InstanceId)}");
            Check("the wrong card does not glow for a named cost",
                !GameEngine.IsValidEffectTarget(b.St, pe, wrong));
        }

        // "other than [Name]" read as a POSITIVE name filter inverts the card completely: P-029
        // Bartolomeo ("Set up to 1 of your {FILM} type Characters other than [Bartolomeo] as active")
        // could set only Bartolomeo active — the one Character the text rules out.
        private static void OtherThanIsNotReadAsARequirement()
        {
            const string Clause = "Set up to 1 of your {FILM} type Characters other than [Bartolomeo] as active.";
            var b = new Fixture();
            var barto = b.Character("south", "P-029", rested: true);
            GameEngine.QueueClauseForTest(b.St, "south", barto, "main", Clause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("\"other than\" is not read as a requirement", false, "no pending effect"); return; }
            GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = barto.InstanceId });
            var after = b.S.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == barto.InstanceId);
            Check("\"other than [Name]\" excludes that card instead of requiring it",
                after != null && after.Rested, $"rested={after?.Rested} (should stay rested — it is excluded)");
        }

        // A cost may offer a CHOICE of cards to pay with. Checked as one string the alternatives AND
        // together and nothing can pay it (OP06-033 Vander Decken IX). The "or" inside "8000 power or
        // more" must not be mistaken for one of those choices.
        private static void ACostOfferingAChoiceCanBePaidEitherWay()
        {
            const string Cost = "trash 1 {Fish-Man} type card from your hand or 1 [The Ark Noah] from your hand";
            var fishman = CardData.Library.Values.FirstOrDefault(d =>
                d != null && d.Features != null && d.Features.Any(f => f != null && f.Contains("Fish-Man")));
            var ark = CardData.GetCard("OP06-041");           // The Ark Noah
            Check("a choice-of-cards cost accepts EITHER alternative",
                fishman != null && ark != null
                    && GameEngine.AuditCostCardMatches(Cost, fishman.Id)
                    && GameEngine.AuditCostCardMatches(Cost, ark.Id),
                $"fishman={fishman?.Id}:{(fishman != null && GameEngine.AuditCostCardMatches(Cost, fishman.Id))} " +
                $"ark={(ark != null && GameEngine.AuditCostCardMatches(Cost, ark.Id))}");
            Check("a power range's \"or\" is not mistaken for a choice",
                !GameEngine.AuditCostCardMatches("trash 1 Character card with 9000 power or more from your hand", "ST01-005"));
        }

        // "Give ALL of your opponent's Characters −3000 power" was resolved as a single-target PICK, so
        // only one Character was ever weakened — and with an empty opposing board it waited for a click
        // that could never come. EB04-051 Emet is a [Trigger], which fires exactly when you are taking
        // damage, so facing an empty board there is ordinary. Both halves are checked.
        private static void GiveAllOpponentCharactersHitsEveryoneAndNeverWaits()
        {
            const string Clause = "Give all of your opponent's Characters −3000 power during this turn.";

            var b = new Fixture();
            var a1 = b.Character("north", "ST01-005");
            var a2 = b.Character("north", "ST01-006");
            int p1 = GameEngine.GetPower(b.St, a1), p2 = GameEngine.GetPower(b.St, a2);
            GameEngine.QueueClauseForTest(b.St, "south", b.Hand("south", "EB04-051"), "trigger", Clause);
            Check("\"give ALL\" weakens every opposing Character, not just one",
                GameEngine.GetPower(b.St, a1) == p1 - 3000 && GameEngine.GetPower(b.St, a2) == p2 - 3000
                    && b.St.PendingEffects.Count == 0,
                $"{p1}->{GameEngine.GetPower(b.St, a1)}, {p2}->{GameEngine.GetPower(b.St, a2)}, pending={b.St.PendingEffects.Count}");

            var f = new Fixture();     // opponent has nothing: must resolve, not wait
            GameEngine.QueueClauseForTest(f.St, "south", f.Hand("south", "EB04-051"), "trigger", Clause);
            Check("\"give ALL\" against an empty board resolves instead of freezing",
                f.St.PendingEffects.Count == 0, $"pending={f.St.PendingEffects.Count}");
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
