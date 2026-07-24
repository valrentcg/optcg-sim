using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Focused regressions for the keyword semantics in Comprehensive Rules v1.2.0,
    /// section 10, plus the official General Rules Q&A keyword clarifications.
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- keywordrulescheck
    /// </summary>
    public static class KeywordRulesTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Official keyword rules validation (v1.2.0) ===");
            RushWaivesPlayedTurnRestriction();
            PrintedRushCharacterOnlyAttacksRestedCharacters();
            GrantedRushCharacterIsNotRushOrActiveTargeting();
            DoubleAttackDealsTwoButDoesNotOverkill();
            BanishTrashesLifeAndSuppressesTrigger();
            BlockerRedirectsExactlyOneOtherTarget();
            TriggerDeclineAddsToHand();
            ActivatedTriggerTrashesCard();
            TriggerResolvesBetweenDoubleAttackDamage();
            PlayTriggerResolvesBeforeDoubleAttackContinues();
            PrintedUnblockablePreventsBlocker();
            CounterCanBuffAnotherOwnCard();
            OncePerTurnResetsOnFieldReentry();
            OfficialEndPhaseTagsAreRecognized();
            TrashingForSixthCharacterIsNotKo();
            BattleKoActivatesOnKo();

            Console.WriteLine($"keywordrulescheck: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void RushWaivesPlayedTurnRestriction()
        {
            var b = new Board();
            var rush = b.Character("south", "OP01-025", playedThisTurn: true);
            b.Attack("south", rush, b.N.Leader);
            Check("[Rush] can attack on the turn played",
                b.St.Battle != null && b.St.Battle.AttackerId == rush.InstanceId);
        }

        private static void PrintedRushCharacterOnlyAttacksRestedCharacters()
        {
            var b = new Board();
            var rushCharacter = b.Character("south", "EB04-011", playedThisTurn: true);
            var active = b.Character("north", "ST01-002", rested: false);
            var rested = b.Character("north", "ST01-003", rested: true);

            b.Attack("south", rushCharacter, b.N.Leader);
            bool leaderRejected = b.St.Battle == null && !rushCharacter.Rested;
            b.Attack("south", rushCharacter, active);
            bool activeRejected = b.St.Battle == null && !rushCharacter.Rested;
            b.Attack("south", rushCharacter, rested);
            bool restedAccepted = b.St.Battle != null && b.St.Battle.TargetId == rested.InstanceId;
            Check("[Rush: Character] permits only ordinary legal Character targets on play turn",
                leaderRejected && activeRejected && restedAccepted);
        }

        private static void GrantedRushCharacterIsNotRushOrActiveTargeting()
        {
            var b = new Board();
            var zoro = b.Character("south", "EB04-007", playedThisTurn: true);
            var active = b.Character("north", "EB04-023", rested: false); // 9000 power satisfies Zoro's condition
            var rested = b.Character("north", "ST01-002", rested: true);
            b.St = GameEngine.ApplyCommand(b.St,
                new GameCommand { Type = "activateMain", Seat = "south", Target = zoro.InstanceId });

            bool notFullRush = !GameEngine.HasRush(b.St, zoro)
                && GameEngine.CanAttackCharactersOnPlayTurn(b.St, "south", zoro);
            b.Attack("south", zoro, b.N.Leader);
            bool leaderRejected = b.St.Battle == null && !zoro.Rested;
            b.Attack("south", zoro, active);
            bool activeRejected = b.St.Battle == null && !zoro.Rested;
            b.Attack("south", zoro, rested);
            bool restedAccepted = b.St.Battle != null && b.St.Battle.TargetId == rested.InstanceId;
            Check("effect-granted [Rush: Character] is neither full [Rush] nor active-Character targeting",
                notFullRush && leaderRejected && activeRejected && restedAccepted);
        }

        private static void DoubleAttackDealsTwoButDoesNotOverkill()
        {
            var b = new Board();
            var attacker = b.Character("south", "OP02-087");
            b.Life("north", "ST01-002", "ST01-003");
            b.ResolveLeaderHit(attacker);
            Check("[Double Attack] removes 2 Life and does not defeat a Leader that started at 2 Life",
                b.N.Life.Count == 0 && b.St.Status == "active" && b.St.Battle == null);
        }

        private static void BanishTrashesLifeAndSuppressesTrigger()
        {
            var b = new Board();
            var attacker = b.Character("south", "OP01-067");
            b.Life("north", "EB02-030");
            int hand = b.N.Hand.Count;
            b.ResolveLeaderHit(attacker);
            Check("[Banish] trashes Life and never opens [Trigger]",
                b.N.Life.Count == 0 && b.N.Hand.Count == hand
                && b.N.Trash.Any(c => c.CardId == "EB02-030") && b.St.Battle == null);
        }

        private static void BlockerRedirectsExactlyOneOtherTarget()
        {
            var b = new Board();
            var attacker = b.Character("south", "ST01-012");
            var blocker = b.Character("north", "ST01-006");
            b.Attack("south", attacker, b.N.Leader);
            b.St = GameEngine.ApplyCommand(b.St,
                new GameCommand { Type = "blockAttack", Seat = "north", Blocker = blocker.InstanceId });
            Check("[Blocker] rests and replaces the attacked card",
                blocker.Rested && b.St.Battle != null && b.St.Battle.TargetId == blocker.InstanceId
                && b.St.Battle.Step == "counter");
        }

        private static void TriggerDeclineAddsToHand()
        {
            var b = new Board();
            var attacker = b.Character("south", "ST01-012");
            b.Life("north", "EB02-030");
            b.OpenTrigger(attacker);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "passTrigger", Seat = "north" });
            Check("declining [Trigger] adds the Life card to hand",
                b.N.Hand.Any(c => c.CardId == "EB02-030") && !b.N.Trash.Any(c => c.CardId == "EB02-030"));
        }

        private static void ActivatedTriggerTrashesCard()
        {
            var b = new Board();
            var attacker = b.Character("south", "ST01-012");
            b.Life("north", "EB02-030");
            int hand = b.N.Hand.Count;
            b.OpenTrigger(attacker);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "useTrigger", Seat = "north" });
            Check("an activated [Trigger] is trashed after resolving",
                b.N.Trash.Any(c => c.CardId == "EB02-030")
                && !b.N.Hand.Any(c => c.CardId == "EB02-030")
                && b.N.Hand.Count == hand + 1);
        }

        private static void TriggerResolvesBetweenDoubleAttackDamage()
        {
            var b = new Board();
            var attacker = b.Character("south", "OP02-087");
            // Last item is top Life in the engine, so the Trigger is checked first.
            b.Life("north", "ST01-003", "EB02-030");
            b.OpenTrigger(attacker);
            bool firstDamagePaused = b.St.Battle?.Step == "trigger" && b.N.Life.Count == 1;
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "useTrigger", Seat = "north" });
            if (!(firstDamagePaused && b.N.Life.Count == 0 && b.St.Status == "active"
                && b.St.Battle == null && b.N.Trash.Any(c => c.CardId == "EB02-030")))
                Console.WriteLine($"        detail paused={firstDamagePaused} life={b.N.Life.Count} status={b.St.Status} " +
                    $"battle={b.St.Battle?.Step ?? "-"} trashTrigger={b.N.Trash.Any(c => c.CardId == "EB02-030")}");
            Check("[Trigger] resolves between [Double Attack]'s first and second damage",
                firstDamagePaused && b.N.Life.Count == 0 && b.St.Status == "active"
                && b.St.Battle == null && b.N.Trash.Any(c => c.CardId == "EB02-030"));
        }

        private static void PlayTriggerResolvesBeforeDoubleAttackContinues()
        {
            var b = new Board();
            var attacker = b.Character("south", "OP02-087");
            b.Life("north", "ST01-003", "ST01-002"); // ST01-002 Trigger: Play this card
            b.OpenTrigger(attacker);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "useTrigger", Seat = "north" });
            Check("a play-this-card [Trigger] finishes before [Double Attack] continues",
                b.N.CharacterArea.Any(c => c?.CardId == "ST01-002")
                && b.N.Life.Count == 0 && b.St.Status == "active" && b.St.Battle == null);
        }

        private static void PrintedUnblockablePreventsBlocker()
        {
            var b = new Board();
            var attacker = b.Character("south", "OP16-032");
            var blocker = b.Character("north", "ST01-006");
            b.Attack("south", attacker, b.N.Leader);
            b.St = GameEngine.ApplyCommand(b.St,
                new GameCommand { Type = "blockAttack", Seat = "north", Blocker = blocker.InstanceId });
            Check("printed [Unblockable] is recognized even without an imported keyword-array entry",
                GameEngine.IsUnblockable(b.St, attacker) && !blocker.Rested
                && b.St.Battle != null && b.St.Battle.TargetId == b.N.Leader.InstanceId);
        }

        private static void CounterCanBuffAnotherOwnCard()
        {
            var b = new Board();
            var attacker = b.Character("south", "ST01-012");
            var other = b.Character("north", "ST01-012");
            var counter = b.Hand("north", "ST01-002");
            b.Attack("south", attacker, b.N.Leader);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "passBlock", Seat = "north" });
            int before = GameEngine.GetPower(b.St, other);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            {
                Type = "counterWithCard", Seat = "north", InstanceId = counter.InstanceId, Target = other.InstanceId,
            });
            Check("a Counter may increase an own card that is not being attacked",
                b.St.Battle != null && b.St.Battle.CounterPower == 0
                && GameEngine.GetPower(b.St, other) == before + 1000);
        }

        private static void OncePerTurnResetsOnFieldReentry()
        {
            var b = new Board();
            var card = b.Hand("south", "ST01-003");
            b.S.AbilityUsedThisTurn.Add(card.InstanceId);
            b.S.AbilityUsedThisTurn.Add(card.InstanceId + ":whenAttacking");
            b.Don("south", 1);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            {
                Type = "playCard", Seat = "south", InstanceId = card.InstanceId, SlotIndex = 0,
            });
            Check("[Once Per Turn] usage resets when the same physical card re-enters the field",
                b.S.CharacterArea[0]?.InstanceId == card.InstanceId
                && !b.S.AbilityUsedThisTurn.Any(k => k == card.InstanceId || k.StartsWith(card.InstanceId + ":")));
        }

        private static void OfficialEndPhaseTagsAreRecognized()
        {
            CardData.UpsertCard("TEST-END-YOURS", "End Yours Fixture", "character", "Red", 1, 1000,
                effect: "[End of Your Turn] Draw 1 card.");
            CardData.UpsertCard("TEST-END-OPP", "End Opponent Fixture", "character", "Red", 1, 1000,
                effect: "[End of Your Opponent's Turn] Draw 1 card.");
            var b = new Board();
            b.Character("south", "TEST-END-YOURS");
            b.Character("north", "TEST-END-OPP");
            int southHand = b.S.Hand.Count;
            int northHand = b.N.Hand.Count;
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "endTurn", Seat = "south" });
            // North also takes its ordinary start-of-turn draw; the extra card proves the exact
            // official End-of-Opponent tag fired rather than merely being refreshed.
            Check("[End of Your Turn] and [End of Your Opponent's Turn] use the official timings",
                b.S.Hand.Count == southHand + 1 && b.N.Hand.Count == northHand + 2);
        }

        private static void TrashingForSixthCharacterIsNotKo()
        {
            var b = new Board();
            b.Character("south", "OP04-049"); // [On K.O.] Draw 1
            b.Character("south", "ST01-002");
            b.Character("south", "ST01-003");
            b.Character("south", "ST01-004");
            b.Character("south", "ST01-005");
            var replacement = b.Hand("south", "ST01-002");
            b.Don("south", 2);
            int handBefore = b.S.Hand.Count;
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            {
                Type = "playCard", Seat = "south", InstanceId = replacement.InstanceId, SlotIndex = 0,
            });
            Check("trashing a Character to make room for a sixth Character is not K.O.",
                b.S.Hand.Count == handBefore - 1 && b.S.Trash.Any(c => c.CardId == "OP04-049"));
        }

        private static void BattleKoActivatesOnKo()
        {
            var b = new Board();
            var attacker = b.Character("south", "ST01-012"); // 5000
            var victim = b.Character("north", "OP04-049", rested: true); // 3000, [On K.O.] Draw 1
            int hand = b.N.Hand.Count;
            b.Attack("south", attacker, victim);
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "passBlock", Seat = "north" });
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "passCounter", Seat = "north" });
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "resolveAttack", Seat = "north" });
            Check("[On K.O.] activates when its own Character is K.O.'d in battle",
                b.N.Trash.Any(c => c.InstanceId == victim.InstanceId) && b.N.Hand.Count == hand + 1);
        }

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
            private int southSlot;
            private int northSlot;
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeck = "st01", NorthDeck = "st01", Seed = "keyword-rules",
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
                S.Life.Clear();
                N.Life.Clear();
                S.CostArea.Clear();
                N.CostArea.Clear();
                S.Leader.Rested = false;
                N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0;
                N.Leader.PlayedOnTurn = 0;
            }

            public CardInstance Character(string seat, string id, bool rested = false, bool playedThisTurn = false)
            {
                var c = Card(id, seat, "character");
                c.Rested = rested;
                c.PlayedOnTurn = playedThisTurn ? St.TurnNumber : 0;
                var p = seat == "south" ? S : N;
                int slot = seat == "south" ? southSlot++ : northSlot++;
                p.CharacterArea[slot] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public void Life(string seat, params string[] ids)
            {
                var p = seat == "south" ? S : N;
                p.Life.Clear();
                foreach (string id in ids) p.Life.Add(Card(id, seat, "life"));
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-keyword-don-{serial++}", Rested = false });
            }

            public void Attack(string seat, CardInstance attacker, CardInstance target)
            {
                St = GameEngine.ApplyCommand(St, new GameCommand
                {
                    Type = "declareAttack", Seat = seat, Attacker = attacker.InstanceId, Target = target.InstanceId,
                });
            }

            public void OpenTrigger(CardInstance attacker)
            {
                Attack("south", attacker, N.Leader);
                St = GameEngine.ApplyCommand(St, new GameCommand { Type = "passBlock", Seat = "north" });
                St = GameEngine.ApplyCommand(St, new GameCommand { Type = "passCounter", Seat = "north" });
                St = GameEngine.ApplyCommand(St, new GameCommand { Type = "resolveAttack", Seat = "north" });
            }

            public void ResolveLeaderHit(CardInstance attacker)
            {
                OpenTrigger(attacker);
                int guard = 0;
                while (St.Battle?.Step == "trigger" && guard++ < 4)
                    St = GameEngine.ApplyCommand(St, new GameCommand { Type = "passTrigger", Seat = "north" });
            }

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-kw-{serial++}",
                CardId = id,
                Owner = owner,
                Zone = zone,
                Rested = false,
                PlayedOnTurn = 0,
            };
        }
    }
}
