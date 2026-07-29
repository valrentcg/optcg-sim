using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Cost shapes the suites never drove, found the same way the Life gaps were: by enumerating every
    /// "You may &lt;cost&gt;:" in the pool — 121 distinct shapes — and checking each against what was
    /// actually exercised, rather than trusting that "costs are covered".
    ///
    ///   place N cards from your trash at the bottom of your deck   15 cards
    ///   trash N cards from the top of your deck                     7
    ///   return N or more DON!! cards to your DON!! deck             6
    ///   trash N card from the top of your Life cards (as a COST)    5
    ///
    /// Each asserts the SPECIFIC zone moved by the SPECIFIC amount, then that the body ran. A cost that
    /// pays from the wrong zone, or pays the wrong number, still produces a plausible log — which is how
    /// the reveal cost went unnoticed while logging only a count.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- costshapes
    /// </summary>
    public static class CostShapeGapTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Cost shapes the other suites never drove ===");
            PlaceFromTrashAtTheBottomOfDeck();
            TrashFromTheTopOfYourDeck();
            ReturnDonToTheDonDeck();
            TrashFromTheTopOfLifeAsACost();
            AnUnpayableVersionOfEachPaysNothing();
            Console.WriteLine($"costshapes: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Queue, press Use, answer up to a few picks. Returns whether the body's draw landed.</summary>
        /// <summary>Queue the clause, press Use, and answer any picks it asks for. Supplying a
        /// null target every time is not "using" the effect: a pick-based cost simply re-prompts,
        /// which is how "place N cards from your trash" first read as doing nothing at all. The
        /// target is chosen from the zone the effect says it wants.</summary>
        private static void Use(Board b, string clause)
        {
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-009"), "main", clause);
            for (int i = 0; i < 8; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                string target = null;
                switch (pe.TargetZone)
                {
                    case EffectTargetZone.Trash: target = b.S.Trash.FirstOrDefault()?.InstanceId; break;
                    case EffectTargetZone.Hand:  target = b.S.Hand.FirstOrDefault()?.InstanceId; break;
                    // No Life zone in the enum — a Life cost is clicked on the board, so the
                    // Play/Any zones cover it and the top Life card is the candidate.
                    case EffectTargetZone.Any:   target = b.S.Life.LastOrDefault()?.InstanceId; break;
                }
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
            }
        }

        private static void PlaceFromTrashAtTheBottomOfDeck()
        {
            var b = new Board(); b.Trash(4);
            int trash0 = b.S.Trash.Count, deck0 = b.S.Deck.Count, hand0 = b.S.Hand.Count;
            string bottomWas = b.S.Deck.Count > 0 ? b.S.Deck[b.S.Deck.Count - 1].InstanceId : null;

            Use(b, "You may place 2 cards from your trash at the bottom of your deck in any order: Draw 1 card.");

            bool moved = b.S.Trash.Count == trash0 - 2 && b.S.Deck.Count == deck0 + 2 - 1;
            bool wentToBottom = bottomWas == null
                || b.S.Deck.Count < 2
                || b.S.Deck[b.S.Deck.Count - 1].InstanceId != bottomWas;   // bottom changed
            bool bodyRan = b.S.Hand.Count == hand0 + 1;
            Check("place-from-trash: trash -2, deck +2 at the BOTTOM, then the body runs",
                  moved && wentToBottom && bodyRan,
                  $"trash {trash0}->{b.S.Trash.Count} deck {deck0}->{b.S.Deck.Count} "
                  + $"bottomChanged={wentToBottom} hand {hand0}->{b.S.Hand.Count}");
        }

        private static void TrashFromTheTopOfYourDeck()
        {
            var b = new Board();
            int deck0 = b.S.Deck.Count, trash0 = b.S.Trash.Count, hand0 = b.S.Hand.Count;
            string topWas = b.S.Deck[0].InstanceId;   // deck top is index 0

            Use(b, "You may trash 2 cards from the top of your deck: Draw 1 card.");

            // The body draws, and that card comes off the deck too, so the deck loses 3: two
            // milled as the cost plus one drawn. My first version asserted -2 and read the
            // engine being right as a failure.
            bool milled = b.S.Deck.Count == deck0 - 3 && b.S.Trash.Count == trash0 + 2;
            bool tookTheTop = b.S.Trash.Any(c => c.InstanceId == topWas);
            Check("mill-as-cost: deck -2 from the TOP, trash +2, then the body runs",
                  milled && tookTheTop && b.S.Hand.Count == hand0 + 1,
                  $"deck {deck0}->{b.S.Deck.Count} trash {trash0}->{b.S.Trash.Count} "
                  + $"tookTop={tookTheTop} hand {hand0}->{b.S.Hand.Count}");
        }

        private static void ReturnDonToTheDonDeck()
        {
            var b = new Board();
            int don0 = b.S.CostArea.Count, donDeck0 = b.S.DonDeck, hand0 = b.S.Hand.Count;

            Use(b, "You may return 2 of your DON!! cards to your DON!! deck: Draw 1 card.");

            bool returned = b.S.CostArea.Count == don0 - 2 && b.S.DonDeck == donDeck0 + 2;
            Check("return-DON!!-as-cost: cost area -2, DON!! deck +2, then the body runs",
                  returned && b.S.Hand.Count == hand0 + 1,
                  $"costArea {don0}->{b.S.CostArea.Count} donDeck {donDeck0}->{b.S.DonDeck} "
                  + $"hand {hand0}->{b.S.Hand.Count}");
        }

        private static void TrashFromTheTopOfLifeAsACost()
        {
            var b = new Board(); b.Life(4);
            int life0 = b.S.Life.Count, trash0 = b.S.Trash.Count, hand0 = b.S.Hand.Count;
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;   // Life top is the LAST element

            Use(b, "You may trash 1 card from the top of your Life cards: Draw 1 card.");

            bool paid = b.S.Life.Count == life0 - 1 && b.S.Trash.Count == trash0 + 1;
            bool tookTheTop = b.S.Trash.Any(c => c.InstanceId == top);
            Check("Life-trash-as-cost: takes the TOP Life card to the trash, then the body runs",
                  paid && tookTheTop && b.S.Hand.Count == hand0 + 1,
                  $"life {life0}->{b.S.Life.Count} trash {trash0}->{b.S.Trash.Count} "
                  + $"tookTop={tookTheTop} hand {hand0}->{b.S.Hand.Count}");
        }

        /// <summary>Control. Each cost above is now made UNPAYABLE; none may pay itself, and none may
        /// run its body. Without this, an engine that paid nothing and drew anyway would pass all four.
        /// </summary>
        private static void AnUnpayableVersionOfEachPaysNothing()
        {
            var cases = new (string What, Func<Board> Setup, string Clause)[]
            {
                ("place from an empty trash", () => { var b = new Board(); b.Trash(0); return b; },
                 "You may place 2 cards from your trash at the bottom of your deck in any order: Draw 1 card."),
                ("mill from an empty deck", () => { var b = new Board(); b.S.Deck.Clear(); return b; },
                 "You may trash 2 cards from the top of your deck: Draw 1 card."),
                ("return DON!! with none in the cost area", () => { var b = new Board(); b.S.CostArea.Clear(); return b; },
                 "You may return 2 of your DON!! cards to your DON!! deck: Draw 1 card."),
                ("trash from an empty Life", () => { var b = new Board(); b.Life(0); return b; },
                 "You may trash 1 card from the top of your Life cards: Draw 1 card."),
            };

            int bad = 0;
            foreach (var c in cases)
            {
                var b = c.Setup();
                int hand0 = b.S.Hand.Count;
                Use(b, c.Clause);
                if (b.S.Hand.Count != hand0)
                {
                    bad++;
                    Console.WriteLine($"      {c.What}: body ran anyway (hand {hand0}->{b.S.Hand.Count})");
                }
            }
            Check("an unpayable version of each pays nothing and runs no body", bad == 0,
                  bad == 0 ? "" : $"{bad} of {cases.Length} paid themselves from an empty zone");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "cost-shapes" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); S.CostArea.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 4; i++) S.Life.Add(Card("ST01-005", "life"));
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-cs-don-{serial++}", Rested = false });
                S.DonDeck = 0;
            }

            public void Life(int n)
            { S.Life.Clear(); for (int i = 0; i < n; i++) S.Life.Add(Card("ST01-005", "life")); }

            public void Trash(int n)
            { S.Trash.Clear(); for (int i = 0; i < n; i++) S.Trash.Add(Card("ST01-005", "trash")); }

            public CardInstance Character(string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++ % 5] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-cs-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
