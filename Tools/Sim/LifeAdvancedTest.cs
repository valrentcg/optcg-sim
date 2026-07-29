using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The Life shapes the earlier suites never reached, found by sweeping the pool for Life wording
    /// rather than by following the reports:
    ///
    ///   15  turn a Life card FACE-DOWN            (the reverse of everything tested so far)
    ///    7  add a Character to the top or BOTTOM of Life, face-up
    ///    6  look at ALL your Life cards and place them back in any order   ("re-arranging")
    ///
    /// It also covers EB03-053 Nami, which is the card actually named in the report - "Nami on-KO
    /// still not prompting me". ST29-008 Nami is a K.O. REPLACEMENT and is what lifefaceup covers;
    /// EB03-053 is a different card with an [On K.O.] trigger and a face-up cost. Testing the first
    /// one and reporting on the second is how a fixed suite sits next to a live bug.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifeadvanced
    /// </summary>
    public static class LifeAdvancedTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life: face-down, bottom placement, re-arranging, [On K.O.] ===");
            NamiOnKoOffersHerEffect();
            NamiOnKoResolvesBothHalves();
            FaceDownCostNeedsAFaceUpTopCard();
            FaceDownCostTurnsTheTopCardDown();
            LifeReArrangeKeepsEveryCard();
            AddCharacterToBottomOfLife();
            Console.WriteLine($"lifeadvanced: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Last segment of an instance id, so order mismatches print readably.</summary>
        private static string Tail(string id) => id.Substring(id.LastIndexOf('-') + 1);

        private static CardInstance Top(PlayerState p) => p.Life[p.Life.Count - 1];

        private static bool Use(Board b, string seat = "south")
        {
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == seat);
            if (pe == null) return false;
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = pe.EffectId });
            return true;
        }

        private static void Pick(Board b, string instanceId, string seat = "south")
        {
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == seat);
            if (pe == null) return;
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = seat, EffectId = pe.EffectId, Target = instanceId });
        }

        /// <summary>EB03-053 Nami: "[On K.O.] You may turn 1 card from the top of your Life cards
        /// face-up: Play up to 1 Character card with 6000 power or less from your hand." The report was
        /// that being K.O.'d never offered anything.</summary>
        private static void NamiOnKoOffersHerEffect()
        {
            var b = new Board(); b.Life("south", 4);
            var nami = b.Character("south", "EB03-053");
            b.Hand("south", "EB03-002");                 // a 6000-power Character to play with the payoff
            GameEngine.AuditKoByEffect(b.St, "south", nami.InstanceId);
            bool offered = b.St.PendingEffects.Any(e => e != null && e.Seat == "south");
            Check("Nami EB03-053: being K.O.'d offers her [On K.O.] effect",
                  offered, "nothing was queued - the player is never asked");
        }

        private static void NamiOnKoResolvesBothHalves()
        {
            var b = new Board(); b.Life("south", 4);
            var nami = b.Character("south", "EB03-053");
            var body = b.Hand("south", "EB03-002");      // vanilla 6000, so it is a legal payoff target
            GameEngine.AuditKoByEffect(b.St, "south", nami.InstanceId);
            if (!Use(b)) { Check("Nami EB03-053: Use", false, "no prompt to press Use on"); return; }

            bool flipped = Top(b.S).FaceUp;
            Check("Nami EB03-053: the cost is paid - the top Life card turns face-up", flipped,
                  "Use did not flip anything");

            // The payoff is "play up to 1 Character ... from your hand" - a pick, so answer it.
            if (b.St.PendingEffects.Any(e => e != null && e.Seat == "south")) Pick(b, body.InstanceId);
            bool inPlay = b.S.CharacterArea.Any(c => c != null && c.InstanceId == body.InstanceId);
            Check("Nami EB03-053: the payoff runs - the chosen Character is put into play", inPlay,
                  "the cost was paid and the body never played anything");
        }

        /// <summary>OP08-063 Katakuri: "You may turn 1 card from the top of your Life cards face-down".
        /// The mirror of the rule fixed for face-up, and it has to hold in both directions: you cannot
        /// turn the top card DOWN if it is already down, regardless of what is underneath.</summary>
        private static void FaceDownCostNeedsAFaceUpTopCard()
        {
            const string COST = "turn 1 card from the top of your Life cards face-down";
            var down = new Board(); down.Life("south", 3);        // all face-down already
            bool unpayable = GameEngine.AuditTryAutoPayCost(down.St, "south", null, COST) == 0;

            var up = new Board(); up.Life("south", 3);
            Top(up.S).FaceUp = true;                              // a face-up top card IS payable
            bool payable = GameEngine.AuditTryAutoPayCost(up.St, "south", null, COST) > 0;

            Check("face-DOWN cost: unpayable when the top is already face-down, payable when it is up",
                  unpayable && payable,
                  $"allDownUnpayable={unpayable} faceUpTopPayable={payable}");
        }

        private static void FaceDownCostTurnsTheTopCardDown()
        {
            var b = new Board(); b.Life("south", 3);
            Top(b.S).FaceUp = true;
            var below = b.S.Life[b.S.Life.Count - 2];
            below.FaceUp = true;                                   // second card also up, to catch off-by-one
            GameEngine.AuditTryAutoPayCost(b.St, "south", null,
                "turn 1 card from the top of your Life cards face-down");
            Check("face-DOWN cost turns the TOP card down and leaves the one below it alone",
                  !Top(b.S).FaceUp && below.FaceUp,
                  $"top={Top(b.S).FaceUp} below={below.FaceUp}");
        }

        /// <summary>OP13-105 Momonosuke: "Look at all of your Life cards and place them back in your
        /// Life area in any order." Re-arranging must not be a disguised draw or a disguised trash -
        /// the same cards must still be there afterwards, whatever order they end in.</summary>
        private static void LifeReArrangeKeepsEveryCard()
        {
            var b = new Board(); b.Life("south", 4);
            var before2 = b.S.Life.Select(c => c.InstanceId).ToList();          // original order
            var before = before2.OrderBy(x => x, StringComparer.Ordinal).ToList(); // as a set
            int hand0 = b.S.Hand.Count, trash0 = b.S.Trash.Count;

            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "OP13-105"), "onPlay",
                "Look at all of your Life cards and place them back in your Life area in any order.");
            for (int guard = 0; guard < 6 && b.St.PendingEffects.Any(e => e != null && e.Seat == "south"); guard++)
                Use(b);

            // The clause opens a LOOK holding the Life cards while the player commits an order; the
            // cards sit in that buffer until then. Reading the empty Life zone as "the effect deleted
            // my Life" would be the same mistake as calling a pending pick a failure - answer it.
            Check("Life re-arrange opens a rearrange prompt rather than resolving silently",
                  b.St.DeckLook != null, "no look was opened, so there was no order to choose");
            if (b.St.DeckLook != null)
            {
                // Submit an order that genuinely DIFFERS from the current one, or the assertion below
                // cannot tell "honoured" from "ignored". Life is stored bottom-first and the prompt
                // treats leftmost as the TOP, so handing it the bottom-first list flips the stack.
                b.Apply(new GameCommand
                { Type = "deckLookConfirmOrder", Seat = "south",
                  OrderedInstanceIds = new List<string>(before2) });
            }

            var after = b.S.Life.Select(c => c.InstanceId).OrderBy(x => x, StringComparer.Ordinal).ToList();
            // The point of re-arranging is the ORDER. Restoring the right four cards in the order they
            // started would satisfy every count-based check above and still mean the submitted order
            // was thrown away, so compare it card for card. Life is stored bottom-first and the prompt
            // labels leftmost as the TOP, so the submitted list read back is Life reversed.
            var topFirst = Enumerable.Reverse(b.S.Life.Select(x => x.InstanceId).ToList()).ToList();
            var originalTopFirst = before2.AsEnumerable().Reverse().ToList();
            Check("Life re-arrange applies the ORDER the player submitted",
                  topFirst.SequenceEqual(before2) && !topFirst.SequenceEqual(originalTopFirst),
                  "submitted [" + string.Join(",", before2.Select(Tail))
                  + "] got [" + string.Join(",", topFirst.Select(Tail))
                  + "] original was [" + string.Join(",", originalTopFirst.Select(Tail)) + "]");

            Check("Life re-arrange keeps exactly the same cards in Life (no draw, no trash)",
                  before.SequenceEqual(after) && b.S.Hand.Count == hand0 && b.S.Trash.Count == trash0,
                  $"life {before.Count}->{after.Count} same={before.SequenceEqual(after)} " +
                  $"hand {hand0}->{b.S.Hand.Count} trash {trash0}->{b.S.Trash.Count}");
        }

        /// <summary>OP03-123 Katakuri: "Add up to 1 Character with a cost of 8 or less to the top or
        /// bottom of the owner's Life cards face-up." Every Life test so far has added to the TOP; this
        /// is the only shape where a card can land on the BOTTOM, and it lands face-UP.</summary>
        private static void AddCharacterToBottomOfLife()
        {
            var b = new Board(); b.Life("south", 3);
            var victim = b.Character("south", "EB03-002");        // cost 8 or less
            int life0 = b.S.Life.Count;

            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "OP03-123"), "onPlay",
                "Add up to 1 Character with a cost of 8 or less to the top or bottom of the owner's Life cards face-up.");
            if (b.St.PendingEffects.Any(e => e != null && e.Seat == "south")) Pick(b, victim.InstanceId);
            for (int guard = 0; guard < 4 && b.St.PendingEffects.Any(e => e != null && e.Seat == "south"); guard++)
                Use(b);

            bool grew = b.S.Life.Count == life0 + 1;
            bool leftField = b.S.CharacterArea.All(c => c == null || c.InstanceId != victim.InstanceId);
            bool atAnEnd = grew && (b.S.Life[0].InstanceId == victim.InstanceId
                                 || Top(b.S).InstanceId == victim.InstanceId);
            Check("a Character added to Life leaves the field and lands at an END of the stack",
                  grew && leftField && atAnEnd,
                  $"life {life0}->{b.S.Life.Count} leftField={leftField} atAnEnd={atAnEnd}");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-advanced" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-la-don-{serial++}", Rested = false });
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

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "character");
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-la-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
