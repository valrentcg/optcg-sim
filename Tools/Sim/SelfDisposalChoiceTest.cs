using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The mirror of `opponentpicks`, and much the larger half: 25 cards whose effect makes the
    /// OPPONENT dispose of a card from their OWN hand.
    ///
    ///   "Your opponent trashes 1 card from their hand."                              17 cards
    ///   "Your opponent places 1 card from their hand at the bottom of their deck."     8 cards
    ///
    /// Who chooses is not stated on the cards, but it cannot be the controller: rule 3-4-3 says
    /// players cannot view the contents of the other player's hand, so the only player who can
    /// perform this action is the one holding the cards. The engine was choosing anyway — taking
    /// Hand[Count-1] at five separate sites — which is the same defect as OP01-038's auto-pick and
    /// the protection discard before it, in the class where it costs the most: a discard is only
    /// ever as bad as the card you give up, and having that decided for you is the difference
    /// between losing a spare and losing your answer.
    ///
    /// Note this is MANDATORY. The opponent chooses WHICH, never WHETHER, so a skip must still cost
    /// them the card — otherwise turning it into a prompt would be strictly worse than the auto-pick.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- selfdisposal
    /// </summary>
    public static class SelfDisposalChoiceTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== The opponent disposes of their OWN card — so they choose which ===");
            Case("trash", "Your opponent trashes 1 card from their hand.",
                 (b, id) => b.N.Trash.Any(c => c.InstanceId == id));
            Case("place at deck bottom", "Your opponent places 1 card from their hand at the bottom of their deck.",
                 (b, id) => b.N.Deck.Any(c => c.InstanceId == id));
            SkippingStillCostsThem("Your opponent trashes 1 card from their hand.", "trash");
            SkippingStillCostsThem("Your opponent places 1 card from their hand at the bottom of their deck.", "place");
            Console.WriteLine($"selfdisposal: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Three assertions per wording: north is asked, the card leaves NORTH's hand, and
        /// the card north names is the one that goes. The third is what separates a real choice from
        /// an engine pick that happens to move the right number of cards.</summary>
        private static void Case(string label, string clause, Func<Board, string, bool> landed)
        {
            var b = new Board();
            b.Queue(clause);
            bool asked = b.Mine("north") != null;
            if (!asked)
            {
                Check($"{label}: north is asked to choose", false,
                      b.Mine("south") != null ? "SOUTH was asked" : "nobody was asked — the engine chose");
                Check($"{label}: the card north names is the one that goes", false, "north was never asked");
                return;
            }
            Check($"{label}: north is asked to choose", true);

            // Name the FIRST card: the engine's fallbacks all take from the END of the hand, so a
            // pick that is not honoured shows up here and nowhere else.
            var want = b.N.Hand[0];
            int n0 = b.N.Hand.Count;
            b.Answer("north", want.InstanceId);

            bool gone = b.N.Hand.All(c => c.InstanceId != want.InstanceId);
            Check($"{label}: the card north names is the one that goes",
                  gone && landed(b, want.InstanceId) && b.N.Hand.Count == n0 - 1,
                  $"named={want.CardId} stillInHand={!gone} arrived={landed(b, want.InstanceId)} "
                  + $"hand {n0}->{b.N.Hand.Count}");
        }

        private static void SkippingStillCostsThem(string clause, string label)
        {
            var b = new Board();
            b.Queue(clause);
            var pe = b.Mine("north");
            if (pe == null) { Check($"{label}: skipping still costs them the card", false, "north was never asked"); return; }
            int n0 = b.N.Hand.Count;

            b.Apply(new GameCommand { Type = "passEffect", Seat = "north", EffectId = pe.EffectId });

            Check($"{label}: north skipping still costs north a card",
                  b.N.Hand.Count == n0 - 1,
                  $"north hand {n0}->{b.N.Hand.Count} — they choose WHICH, never WHETHER");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "self-disposal" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-sd-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" }) Hand("north", id);
                St.PendingEffects.Clear();
            }

            public PendingEffect Mine(string seat) =>
                St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == seat);

            public void Queue(string clause)
            {
                var src = Card("ST29-010", "south", "character");
                S.CharacterArea[0] = src;
                GameEngine.QueueClauseForTest(St, "south", src, "main", clause);
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                {
                    foreach (var e in St.EventLog.TakeLast(5)) Console.WriteLine("      log: " + e.Message);
                    foreach (var pe in St.PendingEffects.Where(x => x != null))
                        Console.WriteLine($"      [pending] seat={pe.Seat} :: {pe.Text}");
                }
            }

            public void Answer(string seat, string target)
            {
                var pe = Mine(seat);
                if (pe == null) return;
                Apply(new GameCommand
                { Type = "resolveEffect", Seat = seat, EffectId = pe.EffectId, Target = target });
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-sd-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
