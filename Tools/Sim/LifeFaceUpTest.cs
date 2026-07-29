using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Reported from playtest as three symptoms of what turned out to be one rule:
    ///   - OP15-114 Wyper "isn't letting me flip the top card up"
    ///   - ST29-008 Nami's protection never fires
    ///   - "if a card is already face-up on top of my Life these effects can't be used again;
    ///      it'd have to have a face-down card on top"
    ///
    /// The third is the rule, and it is stricter than the engine was. "Turn 1 card from the TOP of
    /// your Life cards face-up" names the top card specifically: a face-up top card makes the effect
    /// unavailable, even with face-down cards beneath it. The engine counted flippable cards anywhere
    /// in the stack and then scanned PAST the top to find one, so these effects could pay themselves
    /// from positions the rules do not allow.
    ///
    /// Both shapes are covered because they live in separate code paths that made the same mistake:
    /// the COST form ("you may [cost]: [effect]", Wyper) and the REPLACEMENT form
    /// ("...you may turn 1 card ... face-up instead", Nami).
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifefaceup
    /// </summary>
    public static class LifeFaceUpTest
    {
        private static int passed, failed;
        private const string COST = "turn 1 card from the top of your Life cards face-up";

        public static int Run()
        {
            Console.WriteLine("=== Life face-up: top-of-stack semantics ===");
            CostPayableWhenTopIsFaceDown();
            CostNotPayableWhenTopIsFaceUp();
            CostFlipsTheTopCardSpecifically();
            ReplacementUnavailableWhenTopIsFaceUp();
            ReplacementFiresWhenTopIsFaceDown();
            WyperQueuesWithItsCostPrefixIntact();
            RealPlayKeepsTheCostPrefix("OP15-114", "Wyper");
            RealPlayKeepsTheCostPrefix("OP15-101", "Kalgara");
            CostIsPaidEvenWhenAТargetIsSupplied();
            NamiOnKoCostIsPaidWithATargetSupplied();
            Console.WriteLine($"lifefaceup: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Life is stored bottom-first, so the TOP card is the last element.</summary>
        private static CardInstance Top(PlayerState p) => p.Life[p.Life.Count - 1];

        private static void CostPayableWhenTopIsFaceDown()
        {
            var b = new Board(); b.Life("south", 3);
            Check("all Life face-down: the cost is payable",
                  GameEngine.AuditTryAutoPayCost(b.St, "south", null, COST) > 0,
                  "Wyper could not pay even with every Life card face-down");
        }

        private static void CostNotPayableWhenTopIsFaceUp()
        {
            var b = new Board(); b.Life("south", 3);
            Top(b.S).FaceUp = true;                     // face-up top, two face-down beneath it
            Check("top already face-up: the cost is NOT payable despite face-down cards below",
                  GameEngine.AuditTryAutoPayCost(b.St, "south", null, COST) == 0,
                  "it reached past the top card and spent one lower in the stack");
        }

        private static void CostFlipsTheTopCardSpecifically()
        {
            var b = new Board(); b.Life("south", 3);
            var top = Top(b.S);
            var below = b.S.Life[b.S.Life.Count - 2];
            GameEngine.AuditTryAutoPayCost(b.St, "south", null, COST);
            Check("the card turned face-up is the TOP one",
                  top.FaceUp && !below.FaceUp,
                  $"top={top.FaceUp} below={below.FaceUp}");
        }

        private static void ReplacementUnavailableWhenTopIsFaceUp()
        {
            var b = new Board(); b.Life("south", 1);
            Top(b.S).FaceUp = true;                     // the only Life card is already face-up
            b.Character("south", "ST29-008");           // Nami, offering the protection
            var victim = b.Character("south", "ST29-009");
            GameEngine.AuditKoByEffect(b.St, "south", victim.InstanceId);
            bool gone = !b.S.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId);
            Check("Nami cannot protect when the top Life card is already face-up", gone,
                  "the Character survived, so the protection paid itself from an illegal position");
        }

        private static void ReplacementFiresWhenTopIsFaceDown()
        {
            var b = new Board(); b.Life("south", 2);    // both face-down
            b.Character("south", "ST29-008");
            var victim = b.Character("south", "ST29-009");
            GameEngine.AuditKoByEffect(b.St, "south", victim.InstanceId);
            bool alive = b.S.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId);
            Check("Nami DOES protect when the top Life card is face-down",
                  alive && Top(b.S).FaceUp,
                  alive ? "protected but no Life card was turned face-up" : "the Character was K.O.'d anyway");
        }

        /// <summary>The UI decides between "Use Effect" and a board pick by looking for an unpaid
        /// "You may &lt;cost&gt;:" prefix on the pending effect's text. If the prefix is not preserved
        /// when the effect is queued, that check silently never fires and Wyper shows Skip alone.</summary>
        private static void WyperQueuesWithItsCostPrefixIntact()
        {
            var b = new Board(); b.Life("south", 6);
            var wyper = b.Character("south", "OP15-114");
            b.Character("north", "OP15-040");
            GameEngine.QueueClauseForTest(b.St, "south", wyper, "onPlay",
                "You may turn 1 card from the top of your Life cards face-up: " +
                "Give all of your opponent's Characters -2000 power during this turn.");
            var pe = b.St.PendingEffects.FirstOrDefault();
            bool prefixed = pe != null && System.Text.RegularExpressions.Regex.IsMatch(
                pe.Text ?? "", @"^(?:\[[^\]]+\]\s*/?\s*)*You may [^:]+:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Check("Wyper's queued effect keeps its 'You may <cost>:' prefix", prefixed,
                  pe == null ? "nothing was queued at all" : "text was: " + pe.Text);
        }

        /// <summary>The previous check hand-fed the clause text and then asserted the text survived,
        /// which tests nothing. This plays the card for real and reads whatever the engine actually
        /// queued - the only thing that tells us the UI check will fire in game.</summary>
        private static void RealPlayKeepsTheCostPrefix(string cardId, string label)
        {
            var b = new Board(); b.Life("south", 6);
            var hand = b.Hand("south", cardId);
            b.Don("south", 10);
            b.Character("north", "OP15-040");
            // playCard reads InstanceId, not Target. Passing Target silently played nothing and
            // the test read that as "the engine queues no effect" - a fixture fault, not a finding.
            b.Apply(new GameCommand { Type = "playCard", Seat = "south", InstanceId = hand.InstanceId, SlotIndex = 0 });
            if (b.S.CharacterArea.All(x => x == null || x.CardId != cardId))
            { Check($"{label} ({cardId}) real play", false, "fixture: the card never reached the board"); return; }
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            bool prefixed = pe != null && System.Text.RegularExpressions.Regex.IsMatch(
                pe.Text ?? "", @"^(?:\[[^\]]+\]\s*/?\s*)*You may [^:]+:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Check($"{label} ({cardId}) queues with its cost prefix on a REAL play", prefixed,
                  pe == null ? "no pending effect was queued at all" : "queued text: " + pe.Text);
        }

        /// <summary>A resolveEffect that carries a TARGET must still pay an unpaid "You may &lt;cost&gt;:"
        /// prefix first. Paying the cost IS the first interaction — that is fix #1, and the UI
        /// follows it by sending no target for these effects.
        ///
        /// The bots do supply one, and measurement showed the consequence: over 2,880 games, 72 of
        /// these effects were resolved, ALL of them with a target, and the cost-prefix block was
        /// reached zero times. Every existing suite missed it because every suite resolves with
        /// null, exactly as the UI does — so none could produce the state that breaks.</summary>
        private static void CostIsPaidEvenWhenAТargetIsSupplied()
        {
            var b = new Board(); b.Life("south", 6);
            var wyper = b.Character("south", "OP15-114");
            var oppo = b.Character("north", "OP15-040");
            GameEngine.QueueClauseForTest(b.St, "south", wyper, "onPlay",
                "You may turn 1 card from the top of your Life cards face-up: "
                + "Give all of your opponent's Characters -2000 power during this turn.");
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("cost paid with a target supplied", false, "nothing queued"); return; }

            // Resolve WITH a target, the way the bot does.
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = oppo.InstanceId });

            Check("an unpaid cost is still paid when a target is supplied",
                  Top(b.S).FaceUp,
                  "the top Life card was not turned face-up — a supplied target skipped the cost");
        }

        /// <summary>The same question for EB03-053 Nami's [On K.O.], which is the OTHER clause whose
        /// text matches "from the top of your Life cards face-" and is far commoner in the meta decks
        /// measured (five of six contain her, one contains Wyper).</summary>
        private static void NamiOnKoCostIsPaidWithATargetSupplied()
        {
            var b = new Board(); b.Life("south", 4);
            var nami = b.Character("south", "EB03-053");
            var body = b.Hand("south", "EB03-002");
            GameEngine.AuditKoByEffect(b.St, "south", nami.InstanceId);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("Nami [On K.O.] cost with a target", false, "nothing queued"); return; }

            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = body.InstanceId });

            Check("Nami [On K.O.]: the cost is paid when a target is supplied",
                  Top(b.S).FaceUp,
                  "no Life card turned face-up — the supplied target skipped the cost");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-faceup" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                St.PendingEffects.Clear();
            }

            public void Life(string seat, int n)
            {
                var p = seat == "south" ? S : N;
                p.Life.Clear();
                for (int i = 0; i < n; i++) p.Life.Add(Card("ST01-005", seat, "life"));
            }

            public CardInstance Hand(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "hand");
                p.Hand.Add(c);
                return c;
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-lf-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "character");
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-lf-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
