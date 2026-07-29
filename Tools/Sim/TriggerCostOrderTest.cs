using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// An audit of my own fix, not of the engine as I found it.
    ///
    /// Making the [Trigger] discard a real choice meant deciding WHEN it happens. I play the card
    /// first and queue the discard after, because the alternative — parking the battle's trigger
    /// step on a pick that decides whether the card arrives — is how the fragile path deadlocks.
    /// Payability is checked up front, so the outcome is identical.
    ///
    /// "Identical outcome" is a claim, and rule 8-4-1-3 says costs are determined and paid BEFORE
    /// the effect activates (8-4-1-4) and resolves (8-4-1-5). My ordering inverts that. It is only
    /// harmless if nothing the body does can change what the cost is paid FROM — and there is a
    /// card where it plainly could:
    ///
    ///   OP08-104 Charlotte Poire: "[Trigger] You may trash 1 card from your hand: Play this card.
    ///                              Then, draw 1 card."
    ///
    /// Under the rules the discard is paid before the draw, so the drawn card cannot be the one
    /// discarded. Under my ordering the draw may land first, which would let a player see a fresh
    /// card and pay with it — strictly better than the rules allow, and invisible in any log.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- triggerorder
    /// </summary>
    public static class TriggerCostOrderTest
    {
        private static int passed, failed;

        private const string DrawTrig = "OP08-104";   // play this card, THEN draw 1

        public static int Run()
        {
            Console.WriteLine("=== Is the [Trigger] cost paid out of the PRE-draw hand? ===");
            TheDrawnCardIsNotPayableAsTheCost();
            Console.WriteLine($"triggerorder: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void TheDrawnCardIsNotPayableAsTheCost()
        {
            var b = new Board();
            b.DealDamage();
            if (!b.AtTriggerStep) { Check("the drawn card is not payable", false, "fixture: never reached the trigger step"); return; }

            var handBefore = b.S.Hand.Select(x => x.InstanceId).ToHashSet();
            b.UseTrigger();

            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("the drawn card is not payable", false, "no cost prompt was raised"); return; }

            // Everything the glow filter would let the player click, right now.
            var clickable = b.S.Hand
                .Where(x => GameEngine.IsValidEffectTarget(b.St, pe, x))
                .ToList();
            var fresh = clickable.Where(x => !handBefore.Contains(x.InstanceId)).ToList();

            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
            {
                Console.WriteLine($"      handBefore={handBefore.Count} clickable={clickable.Count} fresh={fresh.Count}");
                foreach (var e in b.St.EventLog.TakeLast(6)) Console.WriteLine("      log: " + e.Message);
            }

            Check("a card drawn by the body cannot be used to pay the cost",
                  fresh.Count == 0,
                  $"{fresh.Count} freshly drawn card(s) are clickable as payment — the cost is being "
                  + "paid out of a hand the body already improved (rule 8-4-1-3 pays costs first)");

            // The glow filter refusing it is only half the guarantee. A client that sends the
            // command anyway must be refused by the RESOLVER too — glow and resolver disagreeing is
            // this engine's signature failure, and in PvP the engine is the only referee.
            var drawn = b.S.Hand.FirstOrDefault(x => !handBefore.Contains(x.InstanceId));
            if (drawn == null) { Check("the resolver refuses it too", false, "fixture: nothing was drawn"); return; }
            int handAtAttempt = b.S.Hand.Count;
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = drawn.InstanceId });

            Check("the RESOLVER also refuses a drawn card sent directly",
                  b.S.Hand.Any(x => x.InstanceId == drawn.InstanceId) && b.S.Hand.Count == handAtAttempt,
                  $"the drawn card was accepted as payment anyway (hand {handAtAttempt}->{b.S.Hand.Count})");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private CardInstance attacker;
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "trigger-order" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "north"; St.TurnNumber = 9;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-to-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                }
                S.Life.Add(Make("ST01-005", "south", "life"));
                S.Life.Add(Make("ST01-005", "south", "life"));
                S.Life.Add(Make(DrawTrig, "south", "life"));
                for (int i = 0; i < 4; i++) N.Life.Add(Make("ST01-005", "north", "life"));
                foreach (var id in new[] { "EB01-004", "EB01-005" })
                    S.Hand.Add(Make(id, "south", "hand"));

                attacker = Make("EB03-002", "north", "character");
                N.CharacterArea[0] = attacker;
                St.PendingEffects.Clear();
            }

            public bool AtTriggerStep => St.Battle?.Step == "trigger";

            public void DealDamage()
            {
                attacker.Rested = false; attacker.PlayedOnTurn = 0;
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = attacker.InstanceId, Target = S.Leader?.InstanceId });
                if (St.Battle?.Step == "block") Apply(new GameCommand { Type = "passBlock", Seat = "south" });
                if (St.Battle?.Step == "counter") Apply(new GameCommand { Type = "passCounter", Seat = "south" });
                if (St.Battle?.Step == "damage") Apply(new GameCommand { Type = "resolveAttack", Seat = "south" });
            }

            public void UseTrigger() => Apply(new GameCommand { Type = "useTrigger", Seat = "south" });

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-to-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
