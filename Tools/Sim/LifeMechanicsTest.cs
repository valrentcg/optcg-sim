using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Life-zone mechanics beyond the face-up flip covered by LifeFaceUpTest: healing (deck -> Life
    /// and hand -> Life), taking cards back off Life, and the top/bottom re-arranging choices.
    ///
    /// Shapes and their frequency in the pool, from a sweep of every card's text:
    ///   33  add N card from the top OR BOTTOM of your Life cards to your hand
    ///   23  add up to N card from the top of your DECK to the top of your Life cards   (heal)
    ///   21  add N card from the top of your Life cards to your hand
    ///   14  reveal N card from the top of your Life cards
    ///   11  trash N card from the top of your Life cards
    ///    7  add up to N card from your HAND to the top of your Life cards               (heal)
    ///    6  add up to N Character ... to the top or bottom of the owner's Life cards
    ///
    /// Each case asserts the resulting STATE - counts and which end of the stack moved - rather than
    /// that the engine recognised a string. Life is stored bottom-first, so the TOP is the last
    /// element; getting that backwards is the easiest way for one of these to look correct and be
    /// silently inverted.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifemechanics
    /// </summary>
    public static class LifeMechanicsTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life mechanics: heal, take, re-arrange ===");
            HealFromDeckGoesOnTop();
            HealFromHandGoesOnTop();
            TakeFromTopOfLifeGoesToHand();
            TrashFromTopOfLife();
            RevealFromTopOfLifeKeepsItThere();
            TopOrBottomIsAChoiceNotAnAssumption();
            Console.WriteLine($"lifemechanics: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Resolve with no target. Returns true if the engine left something PENDING,
        /// which means it wants a pick - not that it failed to understand the clause. Conflating
        /// those two is how a fixture fault gets reported as an engine bug.</summary>
        private static bool Resolve(Board b, CardInstance src, string clause)
        {
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId });
            return b.St.PendingEffects.Any(x => x != null && x.Seat == "south")
                || b.St.ActiveChoice != null || b.St.DeckLook != null;
        }

        /// <summary>Answer an outstanding pick with a specific card, then report whether anything
        /// is still waiting.</summary>
        private static bool Pick(Board b, string instanceId)
        {
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId, Target = instanceId });
            return b.St.PendingEffects.Any(x => x != null && x.Seat == "south");
        }

        private static void HealFromDeckGoesOnTop()
        {
            var b = new Board(); b.Life("south", 3);
            int life0 = b.S.Life.Count, deck0 = b.S.Deck.Count;
            // Deck top is index 0, Life top is the LAST element - opposite conventions.
            string expectTop = b.S.Deck.Count > 0 ? b.S.Deck[0].InstanceId : null;
            Resolve(b, b.Character("south", "ST29-009"),
                    "Add up to 1 card from the top of your deck to the top of your Life cards.");
            bool grew = b.S.Life.Count == life0 + 1 && b.S.Deck.Count == deck0 - 1;
            bool onTop = grew && expectTop != null && b.S.Life[b.S.Life.Count - 1].InstanceId == expectTop;
            Check("heal from deck: Life +1, deck -1, and it lands on TOP", grew && onTop,
                  $"life {life0}->{b.S.Life.Count} deck {deck0}->{b.S.Deck.Count} onTop={onTop}");
        }

        private static void HealFromHandGoesOnTop()
        {
            var b = new Board(); b.Life("south", 2);
            var card = b.Hand("south", "ST29-009");
            int life0 = b.S.Life.Count, hand0 = b.S.Hand.Count;
            // "up to 1 card from your hand" needs to know WHICH card, so resolving with a null
            // target is declining, not failing. Supply the pick.
            bool waiting = Resolve(b, b.Character("south", "ST29-010"),
                    "Add up to 1 card from your hand to the top of your Life cards.");
            if (waiting) waiting = Pick(b, card.InstanceId);
            bool moved = b.S.Life.Count == life0 + 1 && b.S.Hand.Count == hand0 - 1;
            bool onTop = moved && b.S.Life[b.S.Life.Count - 1].CardId == card.CardId;
            Check("heal from hand: Life +1, hand -1, and it lands on TOP", moved && onTop,
                  $"life {life0}->{b.S.Life.Count} hand {hand0}->{b.S.Hand.Count} onTop={onTop} waitingForPick={waiting}");
        }

        private static void TakeFromTopOfLifeGoesToHand()
        {
            var b = new Board(); b.Life("south", 3);
            int life0 = b.S.Life.Count, hand0 = b.S.Hand.Count;
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            Resolve(b, b.Character("south", "ST29-009"),
                    "Add 1 card from the top of your Life cards to your hand.");
            bool moved = b.S.Life.Count == life0 - 1 && b.S.Hand.Count == hand0 + 1;
            bool tookTop = moved && b.S.Hand.Any(c => c.InstanceId == top);
            Check("take from Life: the TOP card is the one that moves to hand", moved && tookTop,
                  $"life {life0}->{b.S.Life.Count} hand {hand0}->{b.S.Hand.Count} tookTop={tookTop}");
        }

        private static void TrashFromTopOfLife()
        {
            var b = new Board(); b.Life("south", 3);
            int life0 = b.S.Life.Count, trash0 = b.S.Trash.Count;
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            Resolve(b, b.Character("south", "ST29-009"),
                    "Trash 1 card from the top of your Life cards.");
            bool moved = b.S.Life.Count == life0 - 1 && b.S.Trash.Count == trash0 + 1;
            Check("trash from Life: the TOP card goes to the trash",
                  moved && b.S.Trash.Any(c => c.InstanceId == top),
                  $"life {life0}->{b.S.Life.Count} trash {trash0}->{b.S.Trash.Count}");
        }

        private static void RevealFromTopOfLifeKeepsItThere()
        {
            var b = new Board(); b.Life("south", 3);
            int life0 = b.S.Life.Count;
            Resolve(b, b.Character("south", "ST29-009"),
                    "Reveal 1 card from the top of your Life cards.");
            Check("reveal from Life does not remove the card", b.S.Life.Count == life0,
                  $"life {life0}->{b.S.Life.Count} — a reveal must not consume the card");
        }

        private static void TopOrBottomIsAChoiceNotAnAssumption()
        {
            // "from the top OR BOTTOM" is the single most common Life shape in the pool (33 cards).
            // Either end is legal, but the card must come from ONE of them - never the middle.
            var b = new Board(); b.Life("south", 4);
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            string bottom = b.S.Life[0].InstanceId;
            int life0 = b.S.Life.Count, hand0 = b.S.Hand.Count;
            // "top or bottom" needs to know WHICH end. Choose the top explicitly.
            bool waiting2 = Resolve(b, b.Character("south", "ST29-009"),
                    "Add 1 card from the top or bottom of your Life cards to your hand.");
            if (waiting2) waiting2 = Pick(b, top);
            bool moved = b.S.Life.Count == life0 - 1 && b.S.Hand.Count == hand0 + 1;
            bool fromAnEnd = moved && b.S.Hand.Any(c => c.InstanceId == top || c.InstanceId == bottom);
            Check("top-or-bottom takes from an END of the stack, never the middle", moved && fromAnEnd,
                  $"life {life0}->{b.S.Life.Count} hand {hand0}->{b.S.Hand.Count} fromEnd={fromAnEnd} waitingForPick={waiting2}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-mech" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); St.PendingEffects.Clear();
            }

            public void Life(string seat, int n)
            {
                S.Life.Clear();
                for (int i = 0; i < n; i++) S.Life.Add(Card("ST01-005", "life"));
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, "hand"); S.Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-lm-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
