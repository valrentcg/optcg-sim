using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// A population none of the pool-wide sweeps has ever driven, found by following up the fixture
    /// mistake in `namedcards`: [Trigger] text lives in its OWN data field, not in `effect`, and all
    /// five sweeps here (optionalfires, timingsweep, autopick, costresolvesweep, paidfornothing)
    /// enumerate `def.Effect` and nothing else. Checked, not assumed — none of them mentions
    /// `.Trigger` anywhere.
    ///
    /// 485 cards carry a [Trigger]. 42 of those triggers contain a "you may" cost decision:
    ///
    ///   33  [Trigger] You may trash 1 card from your hand: Play this card.
    ///   11  [Trigger] You may trash N cards from your hand: Add up to 1 card from the top of your
    ///       deck to the top of your Life cards.
    ///    3  [Trigger] You may add 1 card from the top or bottom of your Life cards to your hand: …
    ///
    /// This is where the two halves of the brief meet. A [Trigger] fires only when a Life card is
    /// dealt as damage, so every one of these is a "you may" decision that happens DURING the life
    /// mechanics — and the payoff is usually "play this card", turning a card the player was about
    /// to lose into a body on the board. Silently skipping the cost would hand it over free;
    /// silently failing the cost would eat the card.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- triggercost
    /// </summary>
    public static class TriggerCostTest
    {
        private static int passed, failed;

        // OP08-104 Charlotte Poire: "[Trigger] You may trash 1 card from your hand: Play this card."
        // Chosen over ST29-004 Sanji deliberately — Sanji's own [On Play] opens a deck look, which
        // the engine (correctly) blocks effect resolution behind, so the cost pick could not be
        // answered and the test was measuring the LOOK, not the cost. Poire has no printed effect.
        private const string TrigCard = "OP08-104";

        public static int Run()
        {
            Console.WriteLine("=== [Trigger] with a cost: prompted during Life damage, and paid? ===");
            ActivatingOffersTheCost();
            PayingPlaysTheCard();
            PassingTheTriggerPlaysNothing();
            SkippingThePickStillCosts();
            PayingCostsExactlyOneHandCard();
            TheCardNAMEDIsTheOneTrashed();
            Console.WriteLine($"triggercost: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void ActivatingOffersTheCost()
        {
            var b = new Board();
            b.DamageSouth();
            b.ActivateTrigger();
            Check("activating the [Trigger] raises the cost decision",
                  b.Mine() != null,
                  "no prompt — a \"you may trash 1 card\" cost was decided by the engine");
        }

        private static void PayingPlaysTheCard()
        {
            var b = new Board();
            b.DamageSouth();
            b.ActivateTrigger();
            if (b.Mine() == null) { Check("paying plays the card", false, "never prompted"); return; }
            b.PayTheCost();

            bool onBoard = b.S.CharacterArea.Any(c => c != null && c.CardId == TrigCard);
            Check("paying the cost PLAYS the card onto the board",
                  onBoard,
                  "the cost was taken and the card never arrived — the payoff for a card the player "
                  + "was about to lose anyway");
        }

        /// <summary>Passing the [Trigger] outright is the "no" answer — nothing is played and
        /// nothing is paid, and the Life card goes to hand as ordinary damage. This is the control
        /// for "paying plays the card": without it, an engine that plays the card no matter what
        /// passes that case perfectly.</summary>
        private static void PassingTheTriggerPlaysNothing()
        {
            var b = new Board();
            b.DamageSouth();
            int hand0 = b.S.Hand.Count;
            b.PassTheTrigger();

            bool onBoard = b.S.CharacterArea.Any(c => c != null && c.CardId == TrigCard);
            Check("passing the [Trigger] plays NOTHING and costs nothing",
                  !onBoard && b.S.Hand.Count == hand0 + 1,
                  $"onBoard={onBoard} hand {hand0}->{b.S.Hand.Count} "
                  + "(want +1: the Life card becomes ordinary damage and goes to hand)");
        }

        /// <summary>Once the Trigger is taken, the discard is MANDATORY — the player chose to pay by
        /// pressing it, so skipping the pick must still cost a card. Otherwise making it a prompt
        /// would be strictly worse than the auto-pick it replaced.</summary>
        private static void SkippingThePickStillCosts()
        {
            var b = new Board();
            b.DamageSouth();
            b.ActivateTrigger();
            if (b.Mine() == null) { Check("skipping the pick still costs", false, "never prompted"); return; }
            int hand0 = b.S.Hand.Count;
            b.SkipTheCost();

            Check("skipping the pick still costs exactly 1 card",
                  b.S.Hand.Count == hand0 - 1,
                  $"hand {hand0}->{b.S.Hand.Count} — the choice is WHICH, never WHETHER");
        }

        /// <summary>Exactly one card, not zero and not the whole hand. A cost that takes nothing is a
        /// free play; one that takes two is the engine over-charging for a card the player already
        /// owned.</summary>
        private static void PayingCostsExactlyOneHandCard()
        {
            var b = new Board();
            b.DamageSouth();
            b.ActivateTrigger();
            if (b.Mine() == null) { Check("the cost is exactly 1 card", false, "never prompted"); return; }
            int hand0 = b.S.Hand.Count;
            b.PayTheCost();

            Check("paying costs EXACTLY 1 card from hand",
                  b.S.Hand.Count == hand0 - 1,
                  $"hand {hand0}->{b.S.Hand.Count} (want -1)");
        }

        /// <summary>The point of the whole change: the card the PLAYER names is the one that goes.
        /// The old behaviour took Hand[0], so this deliberately names a different card — asserting
        /// only the count would pass against the auto-pick this replaced.</summary>
        private static void TheCardNAMEDIsTheOneTrashed()
        {
            var b = new Board();
            b.DamageSouth();
            b.ActivateTrigger();
            if (b.Mine() == null) { Check("the named card is the one trashed", false, "never prompted"); return; }

            var want = b.S.Hand[1];          // NOT Hand[0], which is what the engine used to take
            var keep = b.S.Hand[0];
            b.PayNaming(want.InstanceId);

            bool wentAway = b.S.Hand.All(x => x.InstanceId != want.InstanceId);
            bool keptTheOther = b.S.Hand.Any(x => x.InstanceId == keep.InstanceId);
            Check("the card the player NAMES is the one trashed",
                  wentAway && keptTheOther && b.S.Trash.Any(x => x.InstanceId == want.InstanceId),
                  $"named={want.CardId} gone={wentAway} keptHand0={keptTheOther}");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "trigger-cost" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "north"; St.TurnNumber = 9;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-tc-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                // South's Life: the TOP card (last element) is the one dealt as damage.
                S.Life.Add(Make("ST01-005", "south", "life"));
                S.Life.Add(Make("ST01-005", "south", "life"));
                S.Life.Add(Make(TrigCard, "south", "life"));
                for (int i = 0; i < 4; i++) N.Life.Add(Make("ST01-005", "north", "life"));
                // Spare cards to pay the "trash 1 card from your hand" cost with.
                foreach (var id in new[] { "EB01-004", "EB01-005", "EB01-006" })
                    S.Hand.Add(Make(id, "south", "hand"));

                // 6000 power, so the attack actually BEATS south's 5000 Leader. A 2000 attacker
                // simply bounced off and no Life card was ever dealt — the test was measuring an
                // attack that never connected.
                attacker = Make("EB03-002", "north", "character");
                N.CharacterArea[0] = attacker;
                St.PendingEffects.Clear();
            }

            public PendingEffect Mine() =>
                St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");

            /// <summary>North attacks south's Leader and the hit is carried through, so the top Life
            /// card is actually dealt as damage — the only way a [Trigger] ever fires.</summary>
            public void DamageSouth()
            {
                attacker.Rested = false; attacker.PlayedOnTurn = 0;
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = attacker.InstanceId, Target = S.Leader?.InstanceId });
                // The battle has explicit STEPS (block -> counter -> damage -> trigger) and each has
                // its own command; spamming resolveAttack parks it on the damage step forever, which
                // is why nothing was ever offered. The DEFENDER owns all of them, so they come from
                // south even though north is attacking.
                if (St.Battle?.Step == "block") Apply(new GameCommand { Type = "passBlock", Seat = "south" });
                if (St.Battle?.Step == "counter") Apply(new GameCommand { Type = "passCounter", Seat = "south" });
                if (St.Battle?.Step == "damage") Apply(new GameCommand { Type = "resolveAttack", Seat = "south" });
                Diag("after damage");
            }

            /// <summary>True once the battle is parked on the [Trigger] step, i.e. the defender is
            /// actually being asked whether to use it.</summary>
            public bool AtTriggerStep => St.Battle?.Step == "trigger";

            /// <summary>Answer the "activate this [Trigger]?" question with YES. It is its own
            /// command (useTrigger), not a pending effect — the pending effect is what the trigger
            /// RAISES once accepted.</summary>
            public void ActivateTrigger()
            {
                if (AtTriggerStep) Apply(new GameCommand { Type = "useTrigger", Seat = "south" });
                Diag("after activate");
            }

            public void PayTheCost()
            {
                for (int i = 0; i < 6; i++)
                {
                    var pe = Mine();
                    if (pe == null) break;
                    string target = S.Hand.FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
                }
                Diag("after pay");
            }

            /// <summary>Answer the "activate this [Trigger]?" question with NO.</summary>
            public void PassTheTrigger()
            {
                if (AtTriggerStep) Apply(new GameCommand { Type = "passTrigger", Seat = "south" });
                Diag("after pass");
            }

            /// <summary>Pay by naming a specific card, as a click would.</summary>
            public void PayNaming(string instanceId)
            {
                var pe = Mine();
                if (pe == null) return;
                Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = instanceId });
                Diag("after pay-naming");
            }

            public void SkipTheCost()
            {
                var pe = Mine();
                if (pe != null) Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
                Diag("after skip");
            }

            private void Diag(string where)
            {
                if (Environment.GetEnvironmentVariable("OPT_DIAG") != "1") return;
                Console.WriteLine($"      [{where}] pending={St.PendingEffects.Count} "
                                  + $"life={S.Life.Count} hand={S.Hand.Count} "
                                  + $"board={S.CharacterArea.Count(c => c != null)}");
                foreach (var pe in St.PendingEffects.Where(x => x != null))
                    Console.WriteLine($"        pe seat={pe.Seat} zone={pe.TargetZone} :: {pe.Text}");
                Console.WriteLine($"        battle={(St.Battle != null)} choice={(St.ActiveChoice != null)} look={(St.DeckLook != null)} trash={S.Trash.Count}");
                foreach (var e in St.EventLog.Skip(Math.Max(0, St.EventLog.Count - 6)))
                    Console.WriteLine("        log: " + e.Message);
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-tc-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
