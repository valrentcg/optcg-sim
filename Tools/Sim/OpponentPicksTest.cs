using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The last of the seven opponent-decision cards, and the one where getting the frame of
    /// reference backwards is most expensive.
    ///
    /// OP01-038: "[On K.O.] Your opponent chooses 1 card from your hand; trash that card."
    ///
    /// Two players, two roles, and they are NOT the same seat: the OPPONENT picks, but the card
    /// leaves the CONTROLLER's hand. Resolve it relative to whoever clicked and the card punishes
    /// the wrong player — it would let a Character trade itself for a card out of the opponent's
    /// hand instead of costing its own controller one, which is the card doing the opposite of
    /// what it says. Both halves need asserting separately: that north is the one asked, and that
    /// south is the one who pays.
    ///
    /// Also note this is a MANDATORY opponent decision, unlike OP05-099's "your opponent may" —
    /// north does not get to decline it, they only get to choose which card. A "skip" here must
    /// not let the controller keep their whole hand.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- opponentpicks
    /// </summary>
    public static class OpponentPicksTest
    {
        private static int passed, failed;

        private const string Card = "OP01-038";

        public static int Run()
        {
            Console.WriteLine("=== \"Your opponent chooses 1 card from your hand\": they pick, you pay ===");
            NorthIsTheOneAsked();
            TheCardLeavesSOUTHsHand();
            NorthPicksWHICHCard();
            SouthCannotAnswerIt();
            NorthCannotDeclineToDecide();
            Console.WriteLine($"opponentpicks: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void NorthIsTheOneAsked()
        {
            var b = new Board();
            b.KoTheReactor();
            Check("north is the seat asked to choose",
                  b.Mine("north") != null,
                  b.Mine("south") != null
                      ? "SOUTH was asked to choose which of their own cards to lose"
                      : "nobody was asked at all");
        }

        private static void TheCardLeavesSOUTHsHand()
        {
            var b = new Board();
            int s0 = b.S.Hand.Count, n0 = b.N.Hand.Count;
            b.KoTheReactor();
            b.AnswerAs("north");

            Check("the trashed card comes out of SOUTH's hand, not north's",
                  b.S.Hand.Count == s0 - 1 && b.N.Hand.Count == n0,
                  $"south hand {s0}->{b.S.Hand.Count}, north hand {n0}->{b.N.Hand.Count}"
                  + (b.N.Hand.Count < n0 ? " — resolved in the CHOOSER's frame, which inverts the card" : ""));
        }

        /// <summary>The choice has to be real: the card north names is the card that goes. Without
        /// this, an engine that always trashes (say) the last card would pass the count check.</summary>
        private static void NorthPicksWHICHCard()
        {
            var b = new Board();
            b.KoTheReactor();
            if (b.Mine("north") == null) { Check("north picks which card", false, "north was never asked"); return; }

            // Name the FIRST hand card, which is the one an auto-picker is least likely to take
            // (the engine's fallbacks trash from the end of the hand).
            var want = b.S.Hand[0];
            b.AnswerAs("north", want.InstanceId);

            bool gone = b.S.Hand.All(c => c.InstanceId != want.InstanceId);
            bool inTrash = b.S.Trash.Any(c => c.InstanceId == want.InstanceId);
            Check("the card NORTH names is the one trashed",
                  gone && inTrash,
                  $"named={want.CardId} stillInHand={!gone} inSouthTrash={inTrash}");
        }

        private static void SouthCannotAnswerIt()
        {
            var b = new Board();
            b.KoTheReactor();
            if (b.Mine("north") == null) { Check("south cannot answer it", false, "north was never asked"); return; }
            int s0 = b.S.Hand.Count;

            b.AnswerAs("south", b.S.Hand[0].InstanceId);

            Check("south cannot answer a decision that belongs to north",
                  b.S.Hand.Count == s0 && b.Mine("north") != null,
                  $"south hand {s0}->{b.S.Hand.Count} — the controller chose which card they would lose");
        }

        /// <summary>North chooses WHICH card, never WHETHER. Declining used to be impossible because
        /// the engine picked for them; now that it is a real prompt, skipping it must not hand south
        /// their card back — that would turn a fixed downside into an opt-out the opponent controls.
        /// </summary>
        private static void NorthCannotDeclineToDecide()
        {
            var b = new Board();
            int s0 = b.S.Hand.Count;
            b.KoTheReactor();
            if (b.Mine("north") == null) { Check("north cannot decline", false, "north was never asked"); return; }

            var pe = b.Mine("north");
            b.Apply(new GameCommand { Type = "passEffect", Seat = "north", EffectId = pe.EffectId });

            Check("north skipping still costs SOUTH a card",
                  b.S.Hand.Count == s0 - 1 && b.Mine("north") == null,
                  $"south hand {s0}->{b.S.Hand.Count} — declining must not be an escape hatch");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private CardInstance reactor;
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "opponent-picks" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "north"; St.TurnNumber = 9;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-op-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                // Distinguishable hands on both sides, so "which card" and "whose hand" are separable.
                foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" }) Hand("south", id);
                foreach (var id in new[] { "ST29-004", "ST29-009" }) Hand("north", id);
                // Qualified: Board has its own Card(...) helper, which shadows the constant.
                reactor = Character("south", OpponentPicksTest.Card);
                St.PendingEffects.Clear();
                St.ActiveChoice = null;
            }

            public PendingEffect Mine(string seat) =>
                St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == seat);

            /// <summary>K.O. south's reactor from north's side, so [On K.O.] fires the way it would
            /// in a game rather than being queued straight in.</summary>
            public void KoTheReactor()
            {
                GameEngine.QueueClauseForTest(St, "north", Character("north", "ST29-009"), "main",
                    "K.O. up to 1 of your opponent's Characters.");
                for (int i = 0; i < 4; i++)
                {
                    var pe = Mine("north");
                    if (pe == null) break;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "north", EffectId = pe.EffectId, Target = reactor.InstanceId });
                    if (St.EventLog.Count == before) break;
                    if (S.CharacterArea.All(c => c == null || c.InstanceId != reactor.InstanceId)) break;
                }
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                {
                    foreach (var e in St.EventLog.TakeLast(6)) Console.WriteLine("      log: " + e.Message);
                    foreach (var pe in St.PendingEffects.Where(x => x != null))
                        Console.WriteLine($"      [pending] seat={pe.Seat} zone={pe.TargetZone} :: {pe.Text}");
                }
            }

            public void AnswerAs(string seat, string target = null)
            {
                for (int i = 0; i < 4; i++)
                {
                    var pe = Mine(seat);
                    if (pe == null) break;
                    string t = target ?? S.Hand.FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId
                               ?? S.Hand.LastOrDefault()?.InstanceId;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = seat, EffectId = pe.EffectId, Target = t });
                    if (St.EventLog.Count == before) break;
                    if (target != null) break;
                }
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-op-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
