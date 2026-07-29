using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Life operations at their BOUNDARIES — empty deck, empty Life, a single card to re-arrange.
    ///
    /// Every Life suite so far runs on a comfortable board: several Life cards, a full deck. Those are
    /// the states the engine is written for. The states that break software are the ones nobody pictures
    /// while writing it, and in a real game they arrive late — decking out, healing on an empty deck,
    /// re-arranging your last Life card while at 1.
    ///
    /// The failure mode here is different from the rest of this session too. A wrong number is a bad
    /// game; an unhandled exception mid-battle is a dead client, and it lands on the player who was
    /// already losing.
    ///
    /// Every case asserts the operation is SURVIVED and the zone is left coherent, not that it does
    /// something particular — at a boundary, doing nothing gracefully is usually the correct answer.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifeboundary
    /// </summary>
    public static class LifeBoundaryTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life at the boundaries: empty deck, empty Life, single card ===");
            HealFromAnEmptyDeck();
            TakeFromEmptyLife();
            TrashFromEmptyLife();
            RearrangeASingleLifeCard();
            RearrangeAnEmptyLifeArea();
            FlipFaceUpWithNoLifeAtAll();
            Console.WriteLine($"lifeboundary: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Run a clause and report the exception if it throws. A boundary that crashes is the
        /// thing being hunted, so the exception type matters more than any state assertion.</summary>
        private static bool Survives(Board b, string clause, out string how)
        {
            how = null;
            try
            {
                GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-009"), "main", clause);
                for (int i = 0; i < 4; i++)
                {
                    var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    b.Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
                }
                if (b.St.DeckLook != null)
                    b.Apply(new GameCommand
                    {
                        Type = "deckLookConfirmOrder", Seat = "south",
                        OrderedInstanceIds = b.S.Life.Select(x => x.InstanceId).ToList(),
                    });
                return true;
            }
            catch (Exception ex) { how = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        private static void HealFromAnEmptyDeck()
        {
            var b = new Board(); b.Life(2); b.S.Deck.Clear();
            int life0 = b.S.Life.Count;
            bool ok = Survives(b, "Add up to 1 card from the top of your deck to the top of your Life cards.", out string how);
            Check("heal with an EMPTY deck survives and adds nothing",
                  ok && b.S.Life.Count == life0,
                  ok ? $"life {life0}->{b.S.Life.Count} (nothing to heal from)" : how);
        }

        private static void TakeFromEmptyLife()
        {
            var b = new Board(); b.Life(0);
            int hand0 = b.S.Hand.Count;
            bool ok = Survives(b, "Add 1 card from the top of your Life cards to your hand.", out string how);
            Check("taking from EMPTY Life survives and adds nothing to hand",
                  ok && b.S.Hand.Count == hand0 && b.S.Life.Count == 0,
                  ok ? $"hand {hand0}->{b.S.Hand.Count} life={b.S.Life.Count}" : how);
        }

        private static void TrashFromEmptyLife()
        {
            var b = new Board(); b.Life(0);
            int trash0 = b.S.Trash.Count;
            bool ok = Survives(b, "Trash 1 card from the top of your Life cards.", out string how);
            Check("trashing from EMPTY Life survives and trashes nothing",
                  ok && b.S.Trash.Count == trash0,
                  ok ? $"trash {trash0}->{b.S.Trash.Count}" : how);
        }

        private static void RearrangeASingleLifeCard()
        {
            var b = new Board(); b.Life(1);
            string only = b.S.Life[0].InstanceId;
            bool ok = Survives(b, "Look at all of your Life cards and place them back in your Life area in any order.", out string how);
            Check("re-arranging ONE Life card survives and keeps it",
                  ok && b.S.Life.Count == 1 && b.S.Life[0].InstanceId == only,
                  ok ? $"life={b.S.Life.Count}" : how);
        }

        private static void RearrangeAnEmptyLifeArea()
        {
            var b = new Board(); b.Life(0);
            bool ok = Survives(b, "Look at all of your Life cards and place them back in your Life area in any order.", out string how);
            Check("re-arranging an EMPTY Life area survives",
                  ok && b.S.Life.Count == 0,
                  ok ? $"life={b.S.Life.Count}" : how);
        }

        private static void FlipFaceUpWithNoLifeAtAll()
        {
            // The cost that started this session, at the boundary: no Life to turn over. It must be
            // unpayable rather than throwing, and must not invent a card.
            var b = new Board(); b.Life(0);
            bool threw = false;
            int paid = 0;
            try { paid = GameEngine.AuditTryAutoPayCost(b.St, "south", null, "turn 1 card from the top of your Life cards face-up"); }
            catch (Exception) { threw = true; }
            Check("turning a Life card face-up with NO Life is unpayable, not a crash",
                  !threw && paid == 0 && b.S.Life.Count == 0,
                  threw ? "it threw" : $"paid={paid} life={b.S.Life.Count}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-boundary" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); S.CostArea.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-lb-don-{serial++}", Rested = false });
                S.DonDeck = 0;
            }

            public void Life(int n)
            {
                S.Life.Clear();
                for (int i = 0; i < n; i++) S.Life.Add(Card("ST01-005", "life"));
            }

            public CardInstance Character(string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++ % 5] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-lb-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
