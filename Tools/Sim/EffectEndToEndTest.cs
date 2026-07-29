using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The half of "you may" the other suites do not reach. lifefaceup proves the cost is payable and
    /// that the prefix survives queueing, i.e. that the player gets ASKED. It stops there. Nothing
    /// asserted that pressing Use makes the card's BODY happen, which is the part the player actually
    /// notices, and a card can be perfectly promptable and still do nothing.
    ///
    /// So each case here drives the whole sequence against the real command path - play the card, press
    /// Use, answer any pick - and then asserts the payoff on the board rather than a log line or a
    /// pending-effect count.
    ///
    /// The two cards are deliberately different shapes:
    ///   OP15-114 Wyper   cost = flip a Life card    body = a sweep with a ". Then," rider on it
    ///   OP15-101 Kalgara cost = a CHOSEN discard    body = a deck-look
    /// Wyper's rider matters because ". Then," clauses have been mis-gated in this engine repeatedly,
    /// and a -2000 sweep that forgets its K.O. half looks fine right up until you count the board.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- endtoend
    /// </summary>
    public static class EffectEndToEndTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== \"You may\": press Use, and the body must actually happen ===");
            WyperPaysItsCostAndSweepsTheBoard();
            WyperSkipDoesNeitherHalf();
            KalgaraAsksForTheDiscardThenOpensTheLook();
            Console.WriteLine($"endtoend: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static CardInstance Top(PlayerState p) => p.Life[p.Life.Count - 1];

        /// <summary>Press Use on whatever the seat has pending. Returns false if there was nothing to
        /// press, which is itself a failure for these cards - it means the prompt never appeared.</summary>
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

        /// <summary>
        /// "[On Play] You may turn 1 card from the top of your Life cards face-up: Give all of your
        /// opponent's Characters -2000 power during this turn. Then, K.O. all of your opponent's
        /// Characters with 0 power or less."
        ///
        /// The two opponent bodies straddle the threshold on purpose: a 2000-power Character lands on
        /// exactly 0 and must die, a 6000 drops to 4000 and must live. Asserting only "something was
        /// K.O.'d" would pass on an engine that wiped the board.
        /// </summary>
        private static void WyperPaysItsCostAndSweepsTheBoard()
        {
            var b = new Board();
            b.Life("south", 6);
            b.Don("south", 10);
            var wyper = b.Hand("south", "OP15-114");
            var dies = b.Character("north", "OP07-099");   // vanilla 2000
            var lives = b.Character("north", "EB03-002");  // vanilla 6000

            b.Apply(new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = wyper.InstanceId, SlotIndex = 0 });
            if (b.S.CharacterArea.All(c => c == null || c.CardId != "OP15-114"))
            { Check("Wyper end-to-end", false, "fixture: Wyper never reached the board"); return; }

            bool asked = b.St.PendingEffects.Any(e => e != null && e.Seat == "south");
            if (!asked) { Check("Wyper end-to-end", false, "no prompt appeared at all - nothing to press Use on"); return; }

            Use(b);

            bool flipped = Top(b.S).FaceUp;
            bool deadIsGone = b.N.CharacterArea.All(c => c == null || c.InstanceId != dies.InstanceId);
            bool aliveSurvived = b.N.CharacterArea.Any(c => c != null && c.InstanceId == lives.InstanceId);

            Check("Wyper: the cost is paid - the TOP Life card is turned face-up", flipped,
                  "pressing Use did not flip anything");
            Check("Wyper: the '. Then,' K.O. fires - the 2000-power Character is gone", deadIsGone,
                  "-2000 was applied but the K.O. rider never ran (or was never reached)");
            Check("Wyper: the sweep is bounded - the 6000-power Character survives", aliveSurvived,
                  "the rider K.O.'d a Character that was still above 0 power");
        }

        /// <summary>Skip is the other half of a decision. If declining still flips a Life card or still
        /// sweeps the board, the prompt was cosmetic.</summary>
        private static void WyperSkipDoesNeitherHalf()
        {
            var b = new Board();
            b.Life("south", 6);
            b.Don("south", 10);
            var wyper = b.Hand("south", "OP15-114");
            var dies = b.Character("north", "OP07-099");

            b.Apply(new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = wyper.InstanceId, SlotIndex = 0 });
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("Wyper Skip", false, "fixture: nothing was queued to skip"); return; }
            b.Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });

            bool noFlip = !b.S.Life.Any(c => c.FaceUp);
            bool stillThere = b.N.CharacterArea.Any(c => c != null && c.InstanceId == dies.InstanceId);
            Check("Wyper: Skip pays nothing and sweeps nothing", noFlip && stillThere,
                  $"lifeFlipped={!noFlip} victimKod={!stillThere}");
        }

        /// <summary>
        /// "[On Play] You may trash 1 card from your hand: Look at 5 cards from the top of your deck;
        /// reveal up to a total of 2 [Mont Blanc Noland] or {Shandian Warrior} type cards and add them
        /// to your hand. Then, place the rest at the bottom of your deck in any order."
        ///
        /// Its cost is a CHOSEN discard, so pressing Use must stop and ask which card - and only after
        /// that answer should the look open. Both steps are asserted; an engine that opened the look
        /// while silently pitching a card for you would pass a "did the look open" check alone.
        /// </summary>
        private static void KalgaraAsksForTheDiscardThenOpensTheLook()
        {
            var b = new Board();
            b.Don("south", 10);
            var kalgara = b.Hand("south", "OP15-101");
            var discard = b.Hand("south", "OP07-099");    // the card we will choose to pitch
            b.Hand("south", "EB03-002");                  // a second option, so there IS a choice
            b.StackDeck("south", "OP06-102", "OP06-105", "OP07-099", "EB03-002", "OP06-111");

            b.Apply(new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = kalgara.InstanceId, SlotIndex = 0 });
            if (b.S.CharacterArea.All(c => c == null || c.CardId != "OP15-101"))
            { Check("Kalgara end-to-end", false, "fixture: Kalgara never reached the board"); return; }
            if (!b.St.PendingEffects.Any(e => e != null && e.Seat == "south"))
            { Check("Kalgara end-to-end", false, "no prompt appeared at all"); return; }

            int hand0 = b.S.Hand.Count;
            Use(b);
            bool askedForDiscard = b.S.Hand.Count == hand0
                && b.St.PendingEffects.Any(e => e != null && e.Seat == "south");
            Check("Kalgara: Use asks WHICH card to trash rather than pitching one for you", askedForDiscard,
                  $"hand {hand0}->{b.S.Hand.Count} pending={b.St.PendingEffects.Count(e => e != null && e.Seat == "south")}");

            Pick(b, discard.InstanceId);
            bool paidTheChosenCard = b.S.Trash.Any(c => c.InstanceId == discard.InstanceId);
            bool lookOpened = b.St.DeckLook != null
                || b.St.PendingEffects.Any(e => e != null && e.Seat == "south");
            Check("Kalgara: the chosen card is the one trashed", paidTheChosenCard,
                  "a different card was pitched than the one picked");
            Check("Kalgara: with the cost paid, the deck-look half of the body runs", lookOpened,
                  "the cost was paid and then nothing happened - the body never started");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "end-to-end" });
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

            /// <summary>Put known cards on top of the deck. Deck top is index 0.</summary>
            public void StackDeck(string seat, params string[] ids)
            {
                var p = seat == "south" ? S : N;
                for (int i = ids.Length - 1; i >= 0; i--)
                    p.Deck.Insert(0, Card(ids[i], seat, "deck"));
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
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-e2e-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
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
                InstanceId = $"{owner}-{id}-e2e-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
