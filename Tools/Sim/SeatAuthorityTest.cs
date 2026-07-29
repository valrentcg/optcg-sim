using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// `opponentdecides` proves the WRONG SEAT cannot answer an ActiveChoice. Nothing makes the same
    /// demand of the other three decision surfaces, and those are precisely the ones this workstream
    /// touched:
    ///
    ///   resolveEffect            the "Use" half of every "You may &lt;cost&gt;:" decision
    ///   passEffect               the "Skip" half
    ///   deckLookConfirmOrder     the Life re-arrange / look panel
    ///
    /// Why it matters more here than it looks. In PvP both clients run the engine, so a command that
    /// the engine accepts from either seat is a command EITHER PLAYER CAN SEND. If south can resolve
    /// an effect owned by north, one player answers the other's decision — spending their Life, their
    /// hand, their DON!! — and the two clients then disagree about what happened. My own notes
    /// already record the UI half of this bug (panels gated on isNetworked alone hand the human live
    /// buttons that act AS THE BOT, because the engine checks the seat, not who clicked). That note
    /// assumes the ENGINE is the backstop. This file is the test that the backstop exists.
    ///
    /// Each case asserts BOTH directions. "The wrong seat was refused" is worth nothing on its own —
    /// a command that is broken for everyone also refuses the wrong seat. The right seat must then
    /// succeed on the very same board.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- seatauthority
    /// </summary>
    public static class SeatAuthorityTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Only the owning seat may answer its own decision ===");
            ResolveEffectRejectsTheWrongSeat();
            PassEffectRejectsTheWrongSeat();
            DeckLookConfirmRejectsTheWrongSeat();
            Console.WriteLine($"seatauthority: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private const string Optional =
            "You may trash 1 card from the top of your Life cards: This Character gains +1000 power during this turn.";

        private static void ResolveEffectRejectsTheWrongSeat()
        {
            var b = new Board();
            b.QueueFor("north", Optional);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "north");
            if (pe == null) { Check("resolveEffect: wrong seat refused", false, "no pending effect was raised for north"); return; }

            int northLife0 = b.N.Life.Count;
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            bool refused = b.N.Life.Count == northLife0
                        && b.St.PendingEffects.Any(e => e != null && e.EffectId == pe.EffectId);

            // ...and the OWNER can still do it, on the same board.
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "north", EffectId = pe.EffectId });
            bool ownerWorked = b.N.Life.Count == northLife0 - 1;

            Check("resolveEffect: south cannot USE an effect owned by north; north can",
                  refused && ownerWorked,
                  $"refusedWrongSeat={refused} ownerSucceeded={ownerWorked} "
                  + $"(north Life {northLife0} -> {b.N.Life.Count})");
        }

        private static void PassEffectRejectsTheWrongSeat()
        {
            var b = new Board();
            b.QueueFor("north", Optional);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "north");
            if (pe == null) { Check("passEffect: wrong seat refused", false, "no pending effect was raised for north"); return; }

            b.Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
            bool stillPending = b.St.PendingEffects.Any(e => e != null && e.EffectId == pe.EffectId);

            b.Apply(new GameCommand { Type = "passEffect", Seat = "north", EffectId = pe.EffectId });
            bool ownerCleared = !b.St.PendingEffects.Any(e => e != null && e.EffectId == pe.EffectId);

            Check("passEffect: south cannot SKIP a decision owned by north; north can",
                  stillPending && ownerCleared,
                  $"survivedWrongSeat={stillPending} ownerCleared={ownerCleared} — skipping for someone "
                  + "else silently discards THEIR option");
        }

        /// <summary>The Life re-arrange panel. A look belongs to one seat; confirming an order from
        /// the other would let a player write the opponent's Life stack into whatever order they
        /// chose — and Life order decides which card the next damage takes.</summary>
        private static void DeckLookConfirmRejectsTheWrongSeat()
        {
            var b = new Board();
            b.QueueFor("north", "Look at all of your Life cards and place them back in your Life area in any order.");
            if (b.St.DeckLook == null) { Check("deckLookConfirmOrder: wrong seat refused", false, "no look was opened for north"); return; }

            var order = b.St.DeckLook.Cards.Select(x => x.InstanceId).ToList();
            b.Apply(new GameCommand
            { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order });
            bool stillOpen = b.St.DeckLook != null;

            b.Apply(new GameCommand
            { Type = "deckLookConfirmOrder", Seat = "north", OrderedInstanceIds = order });
            bool ownerClosed = b.St.DeckLook == null;

            Check("deckLookConfirmOrder: south cannot confirm north's Life look; north can",
                  stillOpen && ownerClosed,
                  $"survivedWrongSeat={stillOpen} ownerClosed={ownerClosed} — Life ORDER decides which "
                  + "card the next damage takes, so writing it for the opponent is not cosmetic");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "seat-authority" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009" }) p.Hand.Add(Make(id, p.Seat, "hand"));
                    foreach (var id in new[] { "ST01-005", "ST01-006", "EB01-004", "EB01-005" })
                        p.Life.Add(Make(id, p.Seat, "life"));
                    foreach (var c in p.Life) c.FaceUp = false;
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-sa-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.CharacterArea[0] = Make("ST29-010", p.Seat, "character");
                    p.AbilityUsedThisTurn.Clear();
                }
                St.PendingEffects.Clear();
            }

            public void QueueFor(string seat, string clause) =>
                GameEngine.QueueClauseForTest(St, seat, St.Players[seat].CharacterArea[0], "main", clause);

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-sa-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
