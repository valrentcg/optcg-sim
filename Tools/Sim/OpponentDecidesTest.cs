using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// A handful of cards hand the decision to the OTHER player — "Your opponent chooses one:",
    /// "Your opponent may ...", "Your opponent chooses 1 card from your hand". Seven cards in the
    /// pool, and every one of them is a way for the controller to make their opponent's choice for
    /// them if the prompt is routed to the wrong seat. In PvP both clients run the engine and the
    /// engine is the only referee, so this is the same failure shape as the no-id seat hole fixed
    /// earlier in this workstream.
    ///
    /// There is a second trap underneath the first, and it is the interesting one. The option texts
    /// are written from the CONTROLLER's point of view, so "your opponent's Life cards" means the
    /// chooser's OWN Life. Resolving an option relative to whoever clicked would invert the whole
    /// card: option A would eat the controller's Life instead of the opponent's, and the log would
    /// look perfectly reasonable either way. The engine keeps Seat (who chooses) and ControllerSeat
    /// (who it resolves for) apart for exactly this reason; nothing checked that it holds.
    ///
    /// ST07-010: "[On Play] Your opponent chooses one:
    ///              - Trash 1 card from the top of your opponent's Life cards.
    ///              - Add 1 card from the top of your deck to the top of your Life cards."
    /// Both branches are Life operations, so this doubles as a Life-mechanics case: whichever the
    /// opponent picks, the cards must move in the controller's frame of reference.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- opponentdecides
    /// </summary>
    public static class OpponentDecidesTest
    {
        private static int passed, failed;

        private const string ChooseOne = "ST07-010";

        public static int Run()
        {
            Console.WriteLine("=== \"Your opponent chooses\": who is asked, and whose cards move ===");
            TheOpponentIsTheOneAsked();
            TheControllerCannotAnswerItThemselves();
            OptionA_TrashesTheCHOOSERsLife();
            OptionB_FeedsTheCONTROLLERsLife();
            Console.WriteLine($"opponentdecides: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void TheOpponentIsTheOneAsked()
        {
            var b = new Board();
            b.Play(ChooseOne);
            var ch = b.St.ActiveChoice;
            Check("south plays it, NORTH is asked (and south stays the controller)",
                  ch != null && ch.Seat == "north" && (ch.ControllerSeat ?? ch.Seat) == "south",
                  ch == null ? "no choice was raised at all"
                             : $"Seat={ch.Seat} ControllerSeat={ch.ControllerSeat}");
        }

        /// <summary>The whole point of the card is that the controller does not get to pick.</summary>
        private static void TheControllerCannotAnswerItThemselves()
        {
            var b = new Board();
            b.Play(ChooseOne);
            if (b.St.ActiveChoice == null) { Check("the controller cannot answer it", false, "no choice was raised"); return; }

            b.Apply(new GameCommand { Type = "resolveChoice", Seat = "south", Target = "A" });

            Check("south cannot answer a choice that belongs to north",
                  b.St.ActiveChoice != null,
                  "the controller answered their opponent's decision — in PvP that is one player "
                  + "choosing for the other");
        }

        /// <summary>"Trash 1 card from the top of your OPPONENT'S Life cards" — written from the
        /// controller's side, so the Life that shrinks belongs to north, the player who clicked.</summary>
        private static void OptionA_TrashesTheCHOOSERsLife()
        {
            var b = new Board();
            b.Play(ChooseOne);
            if (b.St.ActiveChoice == null) { Check("option A", false, "no choice was raised"); return; }
            int nLife0 = b.N.Life.Count, sLife0 = b.S.Life.Count;

            b.Apply(new GameCommand { Type = "resolveChoice", Seat = "north", Target = "A" });

            Check("option A takes a Life card from NORTH (\"your opponent\" is the controller's opponent)",
                  b.N.Life.Count == nLife0 - 1 && b.S.Life.Count == sLife0,
                  $"north Life {nLife0}->{b.N.Life.Count}, south Life {sLife0}->{b.S.Life.Count}"
                  + (b.S.Life.Count < sLife0 ? " — resolved in the CHOOSER's frame, which inverts the card" : ""));
        }

        /// <summary>"Add 1 card from the top of your deck to the top of your Life cards" — "your" is
        /// still the controller, so south's deck feeds south's Life.</summary>
        private static void OptionB_FeedsTheCONTROLLERsLife()
        {
            var b = new Board();
            b.Play(ChooseOne);
            if (b.St.ActiveChoice == null) { Check("option B", false, "no choice was raised"); return; }
            int sLife0 = b.S.Life.Count, sDeck0 = b.S.Deck.Count, nLife0 = b.N.Life.Count;

            b.Apply(new GameCommand { Type = "resolveChoice", Seat = "north", Target = "B" });

            Check("option B moves SOUTH's deck card onto SOUTH's Life",
                  b.S.Life.Count == sLife0 + 1 && b.S.Deck.Count == sDeck0 - 1 && b.N.Life.Count == nLife0,
                  $"south Life {sLife0}->{b.S.Life.Count}, south deck {sDeck0}->{b.S.Deck.Count}, "
                  + $"north Life {nLife0}->{b.N.Life.Count}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "opponent-decides" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-od-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
                St.ActiveChoice = null;
            }

            /// <summary>Play the card from south's hand for real, so the [On Play] fires the way it
            /// would in a game rather than being queued straight in.</summary>
            public void Play(string cardId)
            {
                var c = Card(cardId, "south", "hand");
                S.Hand.Add(c);
                int slot = 0;
                for (int i = 0; i < 5; i++) if (S.CharacterArea[i] == null) { slot = i; break; }
                Apply(new GameCommand
                { Type = "playCard", Seat = "south", InstanceId = c.InstanceId, SlotIndex = slot });
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-od-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
