using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Life shapes the other four Life suites never touched, found by enumerating every Life-bearing
    /// sentence in the pool and checking each against what was actually exercised — an audit of my own
    /// documented claim that Life was "covered", which it was not.
    ///
    ///   the OPPONENT's Life        every earlier test operated on your own; trashing from and adding
    ///                              from the opponent's Life are separate paths
    ///   turn ALL Life face-down    all four Life suites flip exactly one card
    ///   trash UNTIL you have N     a loop, not a count — the failure mode is off-by-one or no bound
    ///   "if you have N or less     a life-count CONDITION rather than a life mutation
    ///    Life cards"
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifeshapes
    /// </summary>
    public static class LifeShapeGapTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life shapes the other suites missed ===");
            TrashFromTheOpponentsLife();
            AddFromTheOpponentsLifeToTheirHand();
            TurnAllOfYourLifeFaceDown();
            TrashUntilYouHaveNLifeCards();
            LifeCountConditionIsEvaluated();
            LifeCountConditionIsFalseWhenItShouldBe();
            Console.WriteLine($"lifeshapes: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Resolve a clause, answering any picks with <paramref name="pick"/>.</summary>
        private static void Resolve(Board b, string clause, string pick = null)
        {
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "ST29-009"), "main", clause);
            for (int i = 0; i < 5; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = pick });
            }
        }

        /// <summary>EB03-057. Every earlier Life test worked on the player's own Life; the opponent's
        /// is a different zone and a different owner.</summary>
        private static void TrashFromTheOpponentsLife()
        {
            var b = new Board(); b.Life("south", 3); b.Life("north", 3);
            int mine0 = b.S.Life.Count, theirs0 = b.N.Life.Count, theirTrash0 = b.N.Trash.Count;
            string theirTop = b.N.Life[b.N.Life.Count - 1].InstanceId;

            Resolve(b, "Trash up to 1 card from the top of your opponent's Life cards.", theirTop);

            bool tookTheirs = b.N.Life.Count == theirs0 - 1;
            bool leftMineAlone = b.S.Life.Count == mine0;
            Check("trashing from the OPPONENT's Life takes theirs, not yours",
                  tookTheirs && leftMineAlone,
                  $"theirs {theirs0}->{b.N.Life.Count} mine {mine0}->{b.S.Life.Count} "
                  + $"theirTrash {theirTrash0}->{b.N.Trash.Count}");
        }

        /// <summary>EB04-054: "Add up to 1 card from the top of your opponent's Life cards to the owner's
        /// hand" — it goes to the OWNER's hand, which is the opponent's, not yours. Getting the owner
        /// wrong hands you a free card and is the sort of thing that reads as correct in a log.</summary>
        private static void AddFromTheOpponentsLifeToTheirHand()
        {
            var b = new Board(); b.Life("south", 3); b.Life("north", 3);
            int myHand0 = b.S.Hand.Count, theirHand0 = b.N.Hand.Count, theirs0 = b.N.Life.Count;
            string theirTop = b.N.Life[b.N.Life.Count - 1].InstanceId;

            Resolve(b, "Add up to 1 card from the top of your opponent's Life cards to the owner's hand.", theirTop);

            bool leftTheirLife = b.N.Life.Count == theirs0 - 1;
            bool wentToOwner = b.N.Hand.Count == theirHand0 + 1 && b.S.Hand.Count == myHand0;
            Check("a card taken from the opponent's Life goes to THEIR hand, not yours",
                  leftTheirLife && wentToOwner,
                  $"theirLife {theirs0}->{b.N.Life.Count} theirHand {theirHand0}->{b.N.Hand.Count} "
                  + $"myHand {myHand0}->{b.S.Hand.Count}");
        }

        /// <summary>EB03-051. Every other suite flips exactly one card; "all" is a different code path
        /// and the one that matters after a Wyper-style flip has already turned some face-up.</summary>
        private static void TurnAllOfYourLifeFaceDown()
        {
            var b = new Board(); b.Life("south", 4);
            foreach (var c in b.S.Life) c.FaceUp = true;      // all up, as an earlier effect would leave them

            Resolve(b, "Turn all of your Life cards face-down.");

            Check("\"turn ALL of your Life cards face-down\" turns every one of them",
                  b.S.Life.Count == 4 && b.S.Life.All(c => !c.FaceUp),
                  $"faceUp still = {b.S.Life.Count(c => c.FaceUp)} of {b.S.Life.Count}");
        }

        /// <summary>EB01-059: "trash cards from the top of your Life cards until you have N Life cards".
        /// A loop rather than a count — the failure modes are stopping one short, one long, or not
        /// stopping at all.</summary>
        private static void TrashUntilYouHaveNLifeCards()
        {
            var b = new Board(); b.Life("south", 5);
            int trash0 = b.S.Trash.Count;

            Resolve(b, "Trash cards from the top of your Life cards until you have 2 Life cards.");

            Check("\"trash until you have 2 Life cards\" stops at exactly 2",
                  b.S.Life.Count == 2 && b.S.Trash.Count == trash0 + 3,
                  $"life 5->{b.S.Life.Count} (want 2), trash {trash0}->{b.S.Trash.Count} (want +3)");
        }

        /// <summary>"If you have 2 or less Life cards, draw 1 card." — a condition ON the Life count.
        /// Both directions, because a condition that is always true and one that is always false are
        /// equally broken and only differ in which half you test.</summary>
        private static void LifeCountConditionIsEvaluated()
        {
            var b = new Board(); b.Life("south", 2);          // 2 <= 2, so it holds
            int hand0 = b.S.Hand.Count;
            Resolve(b, "If you have 2 or less Life cards, draw 1 card.");
            Check("a life-count condition FIRES when it is met (2 <= 2)",
                  b.S.Hand.Count == hand0 + 1,
                  $"hand {hand0}->{b.S.Hand.Count} with 2 Life");
        }

        private static void LifeCountConditionIsFalseWhenItShouldBe()
        {
            var b = new Board(); b.Life("south", 5);          // 5 > 2, so it does not
            int hand0 = b.S.Hand.Count;
            Resolve(b, "If you have 2 or less Life cards, draw 1 card.");
            Check("the same condition does NOT fire when unmet (5 > 2)",
                  b.S.Hand.Count == hand0,
                  $"hand {hand0}->{b.S.Hand.Count} with 5 Life — it drew anyway");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-shapes" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ls-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

            public void Life(string seat, int n)
            {
                var p = St.Players[seat];
                p.Life.Clear();
                for (int i = 0; i < n; i++) p.Life.Add(Card("ST01-005", seat, "life"));
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return p.CharacterArea.FirstOrDefault(x => x != null);
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ls-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
