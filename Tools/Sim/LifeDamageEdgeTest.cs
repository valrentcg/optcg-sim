using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Life damage beyond the ordinary one-card hit that lifedamage covers. These are the cases a real
    /// game reaches often and no suite touched:
    ///
    ///   [Double Attack]  one hit, TWO Life cards, and a subtle rule attached to it
    ///   [Banish]         the card is trashed instead of taken, and no [Trigger] step happens
    ///
    /// The Double Attack rule is the interesting one. Running out of Life does not lose the game on the
    /// SECOND damage point of a single hit - the engine passes canDefeat: false for it - so a player on
    /// 1 Life who eats a Double Attack ends at 0 Life and is still playing. Getting that backwards ends
    /// matches that should have continued, which is about as bad as a bug gets and would be reported as
    /// "it just said I lost".
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifedamageedge
    /// </summary>
    public static class LifeDamageEdgeTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life damage: Double Attack and Banish ===");
            DoubleAttackTakesTwoLifeCards();
            DoubleAttackOnOneLifeSurvivesAtZero();
            NoLifeAtAllLosesTheGame();
            BanishTrashesTheLifeCardWithNoTrigger();
            Console.WriteLine($"lifedamageedge: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void DoubleAttackTakesTwoLifeCards()
        {
            var b = new Board();
            b.Life("south", 3);
            int life0 = b.S.Life.Count;
            b.NorthAttacksLeader(doubleAttack: true);
            b.AnswerAnyTriggers();

            Check("[Double Attack] takes TWO Life cards from one hit",
                  b.S.Life.Count == life0 - 2,
                  $"life {life0}->{b.S.Life.Count} (want -2)");
        }

        /// <summary>The rule with teeth: the second damage point of one hit cannot defeat you, so 1 Life
        /// against a Double Attack leaves you at 0 and still playing.</summary>
        private static void DoubleAttackOnOneLifeSurvivesAtZero()
        {
            var b = new Board();
            b.Life("south", 1);
            b.NorthAttacksLeader(doubleAttack: true);
            b.AnswerAnyTriggers();

            Check("[Double Attack] into 1 Life ends at 0 Life, game still running",
                  b.S.Life.Count == 0 && b.St.Status != "finished",
                  $"life={b.S.Life.Count} status={b.St.Status} — the 2nd damage point of a single hit "
                  + "must not defeat, so this match should still be live");
        }

        /// <summary>Control for the case above: with NO Life at all, an ordinary hit does end it.
        /// Without this, an engine that never defeats anyone would pass the survival test.</summary>
        private static void NoLifeAtAllLosesTheGame()
        {
            var b = new Board();
            b.Life("south", 0);
            b.NorthAttacksLeader(doubleAttack: false);
            b.AnswerAnyTriggers();

            Check("no Life left: the hit finishes the match",
                  b.St.Status == "finished",
                  $"status={b.St.Status} (want finished)");
        }

        /// <summary>[Banish]: "the target card is trashed without activating its [Trigger]". So the card
        /// must reach the TRASH, not the hand, and no trigger step may open.</summary>
        private static void BanishTrashesTheLifeCardWithNoTrigger()
        {
            var b = new Board();
            b.Life("south", 3, topCardId: "OP01-009");   // Carrot: "[Trigger] Play this card."
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            int trash0 = b.S.Trash.Count, hand0 = b.S.Hand.Count;

            b.NorthAttacksLeader(doubleAttack: false, attackerId: "OP01-067");   // Crocodile, [Banish]

            bool trashed = b.S.Trash.Any(c => c.InstanceId == top);
            bool notInHand = b.S.Hand.All(c => c.InstanceId != top);
            bool noTriggerStep = b.St.Battle == null || b.St.Battle.Step != "trigger";
            Check("[Banish] trashes the Life card and skips the [Trigger] step",
                  trashed && notInHand && noTriggerStep,
                  $"trashed={trashed} inHand={!notInHand} step={(b.St.Battle?.Step ?? "no battle")} "
                  + $"(trash {trash0}->{b.S.Trash.Count}, hand {hand0}->{b.S.Hand.Count})");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-edge" });
                St.Status = "active"; St.Phase = "main"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                St.PendingEffects.Clear();
            }

            public void Life(string seat, int n, string topCardId = null)
            {
                var p = seat == "south" ? S : N;
                p.Life.Clear();
                for (int i = 0; i < n; i++)
                    p.Life.Add(Card(i == n - 1 && topCardId != null ? topCardId : "ST01-005", seat, "life"));
            }

            /// <summary>North swings at our Leader with a body big enough to win the clash; we decline to
            /// block and to counter, so the damage step runs.</summary>
            public void NorthAttacksLeader(bool doubleAttack, string attackerId = null)
            {
                // Use a card that genuinely HAS [Double Attack] (OP13-046 Vista, 8000, keyword on
                // its own line) rather than setting Battle.PendingLifeDamage by hand. Faking the
                // flag tested my simulation of the keyword, not the keyword - and it took only one
                // Life card, because the engine populates that field itself at declaration time.
                attackerId = attackerId ?? (doubleAttack ? "OP13-046" : "EB03-002");
                var atk = Card(attackerId, "north", "character");
                N.CharacterArea[0] = atk;
                atk.Rested = false; atk.PlayedOnTurn = 0;
                St.ActiveSeat = "north";
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = atk.InstanceId, Target = S.Leader?.InstanceId });
                if (St.Battle?.Step == "block") Apply(new GameCommand { Type = "passBlock", Seat = "south" });
                if (St.Battle?.Step == "counter") Apply(new GameCommand { Type = "passCounter", Seat = "south" });
                // The DEFENDER owns every decision after the declaration, the resolve included.
                if (St.Battle?.Step == "damage") Apply(new GameCommand { Type = "resolveAttack", Seat = "south" });
            }

            /// <summary>Clear any [Trigger] prompts so the damage chain finishes.</summary>
            public void AnswerAnyTriggers()
            {
                for (int i = 0; i < 6 && St.Battle?.Step == "trigger"; i++)
                    Apply(new GameCommand { Type = "passTrigger", Seat = "south" });
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-le-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
