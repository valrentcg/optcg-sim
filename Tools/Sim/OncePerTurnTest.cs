using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "[Once Per Turn]" is the other GATE hiding behind a timing tag — 27 "you may" clauses carry one,
    /// and like [DON!! xN] the dispatch sweeps drive the tag that follows it, so a gate that never
    /// closes looks exactly like a working card.
    ///
    /// It is the more dangerous of the two. A [DON!! xN] gate stuck open gives away one ability; a
    /// once-per-turn gate stuck open gives away *unbounded* repetitions of it — activate, resolve,
    /// activate again, for as long as the cost can be paid. That is a loop, not a leak.
    ///
    /// Three cases, because the gate has three states worth distinguishing: it must OPEN the first
    /// time, CLOSE for the rest of the turn, and REOPEN next turn. Testing only the first two would
    /// pass against an engine that closes the gate permanently, which would quietly make the card
    /// once-per-game.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- onceperturn
    /// </summary>
    public static class OncePerTurnTest
    {
        private static int passed, failed;

        // OP01-013 Sanji: "[Activate: Main] [Once Per Turn] You may add 1 card from your Life area to
        // your hand: This Character gains +2000 power during this turn."
        private const string Card = "OP01-013";

        public static int Run()
        {
            Console.WriteLine("=== [Once Per Turn]: opens once, closes, reopens next turn ===");
            FirstUseIsOffered();
            SecondUseInTheSameTurnIsRefused();
            NextTurnItIsOfferedAgain();
            Console.WriteLine($"onceperturn: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void FirstUseIsOffered()
        {
            var b = new Board();
            var c = b.Character("south", Card);
            Check("the first activation of the turn runs the ability",
                  b.ActivateAndItRan(c),
                  "a fresh [Once Per Turn] ability did nothing on its first use");
        }

        private static void SecondUseInTheSameTurnIsRefused()
        {
            var b = new Board();
            var c = b.Character("south", Card);
            bool first = b.ActivateAndItRan(c);
            bool second = b.ActivateAndItRan(c);

            Check("a second activation in the SAME turn is refused",
                  first && !second,
                  $"first={first} second={second} — an open gate here is an unbounded loop, "
                  + "not a one-off extra");
        }

        private static void NextTurnItIsOfferedAgain()
        {
            var b = new Board();
            var c = b.Character("south", Card);
            b.ActivateAndItRan(c);
            b.PassAFullTurn();                      // south -> north -> south
            bool again = b.ActivateAndItRan(c);

            Check("after a full turn cycle it is offered again",
                  again,
                  "the gate never reopened — the card is once per GAME, not once per turn");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "once-per-turn" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    p.AbilityUsedThisTurn.Clear();
                    for (int i = 0; i < 5; i++) p.Life.Add(Card2("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-op-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

            /// <summary>Activate the card and report whether the ability actually RAN.
            ///
            /// Not "was a decision offered" — my first version asked that and read all three cases as
            /// broken while the engine was right. Clicking activate IS the decision, so the clause
            /// queues and resolves inside the same ApplyCommand and PendingEffects is empty again by
            /// the time the caller looks. The honest observable is the cost moving: Sanji's ability
            /// takes the top Life card to hand, which cannot happen unless the gate opened.</summary>
            public bool ActivateAndItRan(CardInstance c)
            {
                c.Rested = false;                  // a rested source cannot activate; not what is under test
                int life0 = S.Life.Count, hand0 = S.Hand.Count, log0 = St.EventLog.Count;
                Apply(new GameCommand { Type = "activateMain", Seat = "south", Target = c.InstanceId });
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.Skip(log0)) Console.WriteLine("      log: " + e.Message);
                return S.Life.Count == life0 - 1 && S.Hand.Count == hand0 + 1;
            }

            /// <summary>South ends, north ends, and it is south's turn again — the point at which a
            /// once-per-turn flag is supposed to clear.</summary>
            public void PassAFullTurn()
            {
                Apply(new GameCommand { Type = "endTurn", Seat = "south" });
                Apply(new GameCommand { Type = "endTurn", Seat = "north" });
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Card2(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card2(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-op-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
