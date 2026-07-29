using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Taking damage - the most common Life interaction in a real game, and the last one with no
    /// coverage. lifefaceup/lifemechanics/lifeadvanced all reach Life through card EFFECTS; this
    /// reaches it through a battle, which is a different path entirely.
    ///
    /// It is also a "you may" in its own right: a Life card with a [Trigger] stops and asks whether to
    /// activate it, so the same prompt-then-resolve contract applies. Both answers are asserted, and
    /// the no-trigger case is the control that stops "it always asks" from passing.
    ///
    /// Life is stored bottom-first, so damage must take the LAST element. An engine taking index 0
    /// would still produce the right COUNTS, which is why these name the instance.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifedamage
    /// </summary>
    public static class LifeDamageTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life damage: top card, [Trigger] decision, face-up rule ===");
            DamageTakesTheTopLifeCard();
            PlainLifeCardAsksNothing();
            TriggerCardStopsAndAsks();
            PassingTheTriggerStillGivesYouTheCard();
            UsingTheTriggerResolvesIt();
            LosingTheLastLifeEndsTheGame();
            Console.WriteLine($"lifedamage: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void DamageTakesTheTopLifeCard()
        {
            var b = new Board();
            b.Life(3, topCardId: "ST01-005");
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            string bottom = b.S.Life[0].InstanceId;
            int life0 = b.S.Life.Count;

            b.AttackLeaderAndLetItThrough();

            bool tookOne = b.S.Life.Count == life0 - 1;
            bool tookTheTop = b.S.Hand.Any(c => c.InstanceId == top);
            bool bottomStillThere = b.S.Life.Any(c => c.InstanceId == bottom);
            Check("damage takes the TOP Life card to hand, not the bottom",
                  tookOne && tookTheTop && bottomStillThere,
                  $"life {life0}->{b.S.Life.Count} tookTop={tookTheTop} bottomIntact={bottomStillThere}");
        }

        private static void PlainLifeCardAsksNothing()
        {
            // Control: a Life card with no [Trigger] must NOT stop the game to ask. Without this, an
            // engine that prompted on every single damage would pass the trigger tests below.
            var b = new Board();
            b.Life(3, topCardId: "ST01-005");            // no [Trigger] text
            b.AttackLeaderAndLetItThrough();
            Check("a Life card with no [Trigger] goes straight to hand, no question asked",
                  b.St.Battle == null && b.S.Hand.Count == 1,
                  $"battle={(b.St.Battle == null ? "over" : b.St.Battle.Step)} hand={b.S.Hand.Count}");
        }

        private static void TriggerCardStopsAndAsks()
        {
            var b = new Board();
            b.Life(3, topCardId: "OP01-009");            // Carrot: "[Trigger] Play this card."
            b.AttackLeaderAndLetItThrough();
            Check("a [Trigger] Life card stops at the trigger step and asks",
                  b.St.Battle != null && b.St.Battle.Step == "trigger",
                  b.St.Battle == null ? "the battle ended without offering the trigger"
                                      : $"step={b.St.Battle.Step}");
        }

        private static void PassingTheTriggerStillGivesYouTheCard()
        {
            // Declining the trigger is not declining the damage - the card still comes to hand.
            var b = new Board();
            b.Life(3, topCardId: "OP01-009");
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            b.AttackLeaderAndLetItThrough();
            if (b.St.Battle?.Step != "trigger") { Check("passing the trigger", false, "never reached the trigger step"); return; }
            b.Apply(new GameCommand { Type = "passTrigger", Seat = "south" });

            bool inHand = b.S.Hand.Any(c => c.InstanceId == top);
            bool notInPlay = b.S.CharacterArea.All(c => c == null || c.InstanceId != top);
            Check("Skip the trigger: the card still reaches hand and is NOT played",
                  inHand && notInPlay,
                  $"inHand={inHand} inPlay={!notInPlay}");
        }

        private static void UsingTheTriggerResolvesIt()
        {
            var b = new Board();
            b.Life(3, topCardId: "OP01-009");            // "[Trigger] Play this card."
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            b.AttackLeaderAndLetItThrough();
            if (b.St.Battle?.Step != "trigger") { Check("using the trigger", false, "never reached the trigger step"); return; }
            b.Apply(new GameCommand { Type = "useTrigger", Seat = "south" });
            for (int i = 0; i < 4 && b.St.PendingEffects.Any(e => e != null && e.Seat == "south"); i++)
            {
                var pe = b.St.PendingEffects.First(e => e != null && e.Seat == "south");
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            }

            bool onField = b.S.CharacterArea.Any(c => c != null && c.InstanceId == top);
            bool notStillInHand = b.S.Hand.All(c => c.InstanceId != top);
            Check("Use the trigger: \"Play this card\" actually puts it on the field",
                  onField && notStillInHand,
                  $"onField={onField} stillInHand={!notStillInHand}");
        }

        private static void LosingTheLastLifeEndsTheGame()
        {
            var b = new Board();
            b.Life(1, topCardId: "ST01-005");
            b.AttackLeaderAndLetItThrough();             // takes the last card
            int life0 = b.S.Life.Count;
            b.AttackLeaderAndLetItThrough();             // now there is nothing left to take

            Check("running out of Life ends the match",
                  b.St.Status == "finished",
                  $"life={life0}->{b.S.Life.Count} status={b.St.Status} (want finished)");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-damage" });
                St.Status = "active"; St.Phase = "main"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-ld-don-{serial++}", Rested = false });
                S.DonDeck = 0; N.DonDeck = 0;
                St.PendingEffects.Clear();
            }

            /// <summary>Life is stored bottom-first; the named card is placed on TOP (last).</summary>
            public void Life(int n, string topCardId)
            {
                S.Life.Clear();
                for (int i = 0; i < n - 1; i++) S.Life.Add(Card("ST01-005", "south", "life"));
                S.Life.Add(Card(topCardId, "south", "life"));
            }

            /// <summary>North swings at our Leader; we decline to block and decline to counter, so the
            /// hit lands and the damage step runs.</summary>
            public void AttackLeaderAndLetItThrough()
            {
                var atk = Card("EB03-002", "north", "character");   // vanilla 6000, beats a 5000 Leader
                for (int i = 0; i < 5; i++)
                    if (N.CharacterArea[i] == null) { N.CharacterArea[i] = atk; break; }
                atk.Rested = false; atk.PlayedOnTurn = 0;
                St.ActiveSeat = "north";
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = atk.InstanceId, Target = S.Leader?.InstanceId });
                if (St.Battle?.Step == "block") Apply(new GameCommand { Type = "passBlock", Seat = "south" });
                if (St.Battle?.Step == "counter") Apply(new GameCommand { Type = "passCounter", Seat = "south" });
                // The DEFENDER owns every decision after the declaration, the final resolve
                // included - a resolveAttack from the attacker's seat is silently ignored, which
                // left the battle parked on "damage" and failed all six checks at once.
                if (St.Battle?.Step == "damage") Apply(new GameCommand { Type = "resolveAttack", Seat = "south" });
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ld-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
