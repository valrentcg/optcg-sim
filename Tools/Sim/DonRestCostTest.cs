using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "You may rest N of your DON!! cards: &lt;body&gt;" - the one cost shape the timing sweep kept
    /// flagging (OP14-049 Jinbe, OP16-006 Shanks, OP10-019 Divine Departure, OP12-038, OP13-019...).
    ///
    /// It is worth isolating because the body is often unconditional ("Draw 2 cards"), so unlike the
    /// {Fish-Man}/[Kaido] misses there is no precondition to hide behind: if the DON!! are there and
    /// the cost is payable, the player must be asked, and pressing Use must both rest the DON!! and
    /// run the body.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- donrest
    /// </summary>
    public static class DonRestCostTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== \"You may rest N of your DON!! cards: <body>\" ===");
            OffersWhenTheDonAreThere();
            NotOfferedWhenTooFewActiveDon();
            UseRestsExactlyNDonAndRunsTheBody();
            SkipRestsNothing();
            RealPlayOfJinbeOffersIt();
            Console.WriteLine($"donrest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private const string CLAUSE = "You may rest 2 of your DON!! cards: Draw 2 cards.";

        private static void OffersWhenTheDonAreThere()
        {
            var b = new Board(); b.Don("south", 6, restedCount: 0);
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "ST29-009"), "onPlay", CLAUSE);
            Check("with 6 active DON!!, the effect is offered",
                  b.St.PendingEffects.Any(e => e != null && e.Seat == "south"),
                  "nothing queued - the player is never asked");
        }

        private static void NotOfferedWhenTooFewActiveDon()
        {
            // Negative control: only 1 active DON!! cannot pay a cost of 2. Offering it anyway would
            // be an unpayable prompt, which is its own bug.
            var b = new Board(); b.Don("south", 4, restedCount: 3);
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "ST29-009"), "onPlay", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe != null)
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            // Whether it queues or not, the invariant is that an unpayable cost never gets PAID:
            // exactly the 3 pre-rested DON!! should still be the only rested ones.
            Check("with only 1 active DON!!, a rest-2 cost is never actually paid",
                  b.S.CostArea.Count(d => d.Rested) == 3,
                  $"rested={b.S.CostArea.Count(d => d.Rested)} (want 3 - the pre-rested ones only)");
        }

        private static void UseRestsExactlyNDonAndRunsTheBody()
        {
            var b = new Board(); b.Don("south", 6, restedCount: 0);
            int hand0 = b.S.Hand.Count;
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "ST29-009"), "onPlay", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("Use rests 2 DON!! and draws 2", false, "nothing was queued to press Use on"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

            int rested = b.S.CostArea.Count(d => d.Rested);
            int drew = b.S.Hand.Count - hand0;
            Check("Use rests exactly 2 DON!! and draws exactly 2 cards",
                  rested == 2 && drew == 2,
                  $"rested={rested} (want 2) drew={drew} (want 2)");
        }

        private static void SkipRestsNothing()
        {
            var b = new Board(); b.Don("south", 6, restedCount: 0);
            int hand0 = b.S.Hand.Count;
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "ST29-009"), "onPlay", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("Skip rests nothing", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
            Check("Skip rests no DON!! and draws no cards",
                  b.S.CostArea.Count(d => d.Rested) == 0 && b.S.Hand.Count == hand0,
                  $"rested={b.S.CostArea.Count(d => d.Rested)} drew={b.S.Hand.Count - hand0}");
        }

        /// <summary>The whole point of the timing sweep: queueing the clause by hand proves the
        /// resolver, not the dispatch. Jinbe costs 8, so a full cost area leaves exactly 2 active -
        /// enough to pay, which is what makes him a clean test rather than a fixture accident.</summary>
        private static void RealPlayOfJinbeOffersIt()
        {
            var b = new Board(); b.Don("south", 10, restedCount: 0);
            // His body is "Draw 2 cards AND return up to 1 Character with a cost of 7 or less".
            // Give the tail clause a legal target so a miss cannot be blamed on an empty board.
            b.Opp("OP15-040");
            var jinbe = b.Hand("south", "OP14-049");
            b.Apply(new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = jinbe.InstanceId, SlotIndex = 0 });
            if (b.S.CharacterArea.All(c => c == null || c.CardId != "OP14-049"))
            { Check("Jinbe real play", false, "fixture: Jinbe never reached the board"); return; }
            int active = b.S.CostArea.Count(d => !d.Rested);
            Check("OP14-049 Jinbe: playing him offers the rest-2-DON!! effect",
                  b.St.PendingEffects.Any(e => e != null && e.Seat == "south"),
                  $"{active} active DON!! remain after his cost, so a rest-2 cost is payable, yet nothing was offered");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "don-rest" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.CostArea.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 4; i++) S.Life.Add(Card("ST01-005", "life"));
            }

            public void Don(string seat, int count, int restedCount)
            {
                S.CostArea.Clear();
                for (int i = 0; i < count; i++)
                    S.CostArea.Add(new DonInstance
                    { InstanceId = $"south-dr-don-{serial++}", Rested = i < restedCount });
                S.DonDeck = 0;
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, "hand"); S.Hand.Add(c); return c; }

            /// <summary>An opponent Character, so "return up to 1 Character" has something to name.</summary>
            public CardInstance Opp(string id)
            {
                var n = St.Players["north"];
                var c = new CardInstance
                {
                    InstanceId = $"north-{id}-dr-{serial++}",
                    CardId = id, Owner = "north", Zone = "character", Rested = false, PlayedOnTurn = 0,
                };
                for (int i = 0; i < 5; i++) if (n.CharacterArea[i] == null) { n.CharacterArea[i] = c; break; }
                return c;
            }

            public CardInstance Character(string seat, string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-dr-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
