using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// What happens when you say NO to a "[Once Per Turn] You may ..."?
    ///
    /// The two rules meet here and the wrong answer is punishing: if declining burns the use, a
    /// player who clicks Skip once has silently lost that ability for the entire turn, with nothing
    /// in the log to say why the prompt stopped coming. It is the exact shape of bug a player
    /// reports as "the card just stopped working".
    ///
    /// The engine intends the right rule — the deferred triggered path stamps an OnceKey on the
    /// PendingEffect and commits it only on EffectResolution.Resolved, with a comment saying
    /// "never on a skip". A comment is not the rule holding, and this is a SECOND implementation of
    /// once-per-turn alongside the immediate one in ActivateMain (which onceperturn covers); two
    /// implementations of one rule drifting apart is the recurring bug class here.
    ///
    /// OP07-112: "[When Attacking] [Once Per Turn] You may add 1 card from the top or bottom of your
    /// Life cards to your hand: You may rest up to 1 of your opponent's Characters ...". Its cost
    /// moves a Life card, so "was it used" is observable rather than inferred, and it does not set
    /// itself active, so the test controls when it can attack again.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- optionalonce
    /// </summary>
    public static class OptionalOnceSkipTest
    {
        private static int passed, failed;

        private const string Card = "OP07-112";

        public static int Run()
        {
            Console.WriteLine("=== Declining a [Once Per Turn] \"you may\" must not burn the use ===");
            TheFirstAttackAsks();
            DecliningLeavesItAvailable();
            UsingItDoesConsumeIt();
            Console.WriteLine($"optionalonce: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void TheFirstAttackAsks()
        {
            var b = new Board();
            var atk = b.Character("south", Card);
            b.Character("north", "OP15-040");
            Check("the first attack of the turn offers the decision",
                  b.AttackAndAsks(atk),
                  "an unused [Once Per Turn] \"you may\" never prompted");
        }

        /// <summary>Skip it, then attack again. The prompt must come back.</summary>
        private static void DecliningLeavesItAvailable()
        {
            var b = new Board();
            var atk = b.Character("south", Card);
            b.Character("north", "OP15-040");

            bool asked1 = b.AttackAndAsks(atk);
            b.Skip();
            b.EndBattle();
            bool asked2 = b.AttackAndAsks(atk);

            Check("after DECLINING, a later attack asks again",
                  asked1 && asked2,
                  $"asked1={asked1} asked2={asked2} — saying no once must not cost the turn's use");
        }

        /// <summary>The control for the case above. Without it, an engine that never records the use
        /// at all would pass "it asks again" perfectly.</summary>
        private static void UsingItDoesConsumeIt()
        {
            var b = new Board();
            var atk = b.Character("south", Card);
            b.Character("north", "OP15-040");

            bool asked1 = b.AttackAndAsks(atk);
            bool paid = b.UseIt();          // pays a Life card into hand
            b.EndBattle();
            bool asked2 = b.AttackAndAsks(atk);

            Check("after USING it, a later attack does not ask again",
                  asked1 && paid && !asked2,
                  $"asked1={asked1} costPaid={paid} askedAgain={asked2}"
                  + (asked2 ? " — the gate never closed, so the skip case proves nothing" : ""));
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "optional-once" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    p.AbilityUsedThisTurn.Clear();
                    // Enough Life on both sides that repeated attacks never end the game mid-test.
                    for (int i = 0; i < 5; i++) p.Life.Add(Card2("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-oo-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

            private PendingEffect Mine() =>
                St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");

            /// <summary>Ready the attacker, declare, and report whether south was asked anything.</summary>
            public bool AttackAndAsks(CardInstance atk)
            {
                atk.Rested = false; atk.PlayedOnTurn = 0;
                St.ActiveSeat = "south"; St.Phase = "main";
                int log0 = St.EventLog.Count;
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = N.Leader?.InstanceId });
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                {
                    Console.WriteLine($"      [diag] battle={(St.Battle != null)} pending={St.PendingEffects.Count}");
                    foreach (var e in St.EventLog.Skip(log0)) Console.WriteLine("      log: " + e.Message);
                }
                return Mine() != null;
            }

            public void Skip()
            {
                var pe = Mine();
                if (pe != null) Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
            }

            /// <summary>Answer the prompt for real, and report whether the Life cost actually moved —
            /// "the prompt went away" is not the same as "the ability was used".</summary>
            public bool UseIt()
            {
                int life0 = S.Life.Count, hand0 = S.Hand.Count;
                for (int i = 0; i < 6; i++)
                {
                    var pe = Mine();
                    if (pe == null) break;
                    // The cost wants a Life card (top or bottom); the body wants an opponent
                    // Character. Ask the engine which of the cards on the table it will accept
                    // rather than guessing the zone.
                    string target = Candidates()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
                return S.Life.Count == life0 - 1 && S.Hand.Count == hand0 + 1;
            }

            private System.Collections.Generic.IEnumerable<CardInstance> Candidates()
            {
                foreach (var x in S.Life.AsEnumerable().Reverse()) yield return x;   // Life top is LAST
                foreach (var x in N.CharacterArea.Where(y => y != null)) yield return x;
                foreach (var x in S.Hand) yield return x;
            }

            /// <summary>Close the battle so the next declareAttack is legal. The DEFENDER owns every
            /// decision after the declaration, so this is sent from north.</summary>
            public void EndBattle()
            {
                for (int i = 0; i < 4 && St.Battle != null; i++)
                {
                    int before = St.EventLog.Count;
                    Apply(new GameCommand { Type = "resolveAttack", Seat = "north" });
                    if (St.EventLog.Count == before) break;
                }
                // A [Trigger] or on-damage decision may be waiting on north; answer it so play resumes.
                for (int i = 0; i < 4; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "north");
                    if (pe == null) break;
                    Apply(new GameCommand { Type = "passEffect", Seat = "north", EffectId = pe.EffectId });
                }
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Card2(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card2(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-oo-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
