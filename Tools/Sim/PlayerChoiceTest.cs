using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Where the CARD leaves the choice open, the PLAYER makes it — the engine must not pick for them.
    ///
    /// "Trash 1 card from your hand" names a count, not a card. Auto-trashing the last card in hand is
    /// not a shortcut, it is the engine playing the game: which card you pitch is frequently the whole
    /// decision. Same for "the top or bottom of your Life cards", where the two ends are different
    /// cards and only the player knows which one they want.
    ///
    /// The distinction being tested is choice vs. no choice, not effect vs. effect:
    ///   - one legal option  -> resolving straight through is correct, there is nothing to ask
    ///   - two or more       -> the engine must stop and wait for a pick
    /// A test that only ever ran the second case would pass against an engine that prompts for
    /// everything, including "trash your only card", which would be its own bug.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- playerchoice
    /// </summary>
    public static class PlayerChoiceTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Player choice: the engine must not pick for you ===");
            TrashFromHandWaitsForAPick();
            TrashFromHandWithOneCardNeedsNoPick();
            TopOrBottomOfLifeWaitsForAPick();
            TopOrBottomOfLifeHonoursTheEndPicked();
            DiscardChoiceIsNotSilentlyTheLastCard();
            CostFormTrashFromHandWaitsForAPick();
            CostFormTrashTakesThePickedCard();
            ProtectionDiscardIsThePlayersPick();
            ProtectionDiscardIsForcedWhenOnlyOneCardQualifies();
            Console.WriteLine($"playerchoice: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Queue a clause and press Use with no target. Returns true if the engine is still
        /// WAITING - which is the correct answer whenever the clause leaves a choice open.</summary>
        private static bool UseAndSeeIfItWaits(Board b, CardInstance src, string clause)
        {
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId });
            return b.St.PendingEffects.Any(x => x != null && x.Seat == "south")
                || b.St.ActiveChoice != null || b.St.DeckLook != null;
        }

        private static bool Pick(Board b, string instanceId)
        {
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId, Target = instanceId });
            return b.St.PendingEffects.Any(x => x != null && x.Seat == "south");
        }

        private static void TrashFromHandWaitsForAPick()
        {
            var b = new Board();
            b.Hand("ST29-004"); b.Hand("OP15-020"); b.Hand("ST29-009");
            int hand0 = b.S.Hand.Count;
            bool waits = UseAndSeeIfItWaits(b, b.Character("ST29-010"), "Trash 1 card from your hand.");
            Check("trash-from-hand with 3 cards waits for the player to choose",
                  waits && b.S.Hand.Count == hand0,
                  waits ? "" : $"it resolved on its own; hand {hand0}->{b.S.Hand.Count} (the engine chose)");
        }

        private static void TrashFromHandWithOneCardNeedsNoPick()
        {
            // Negative control. With a single card there IS no choice, so stopping to ask would be
            // its own defect - this is what stops the test from passing on a prompt-for-everything engine.
            var b = new Board();
            var only = b.Hand("ST29-004");
            UseAndSeeIfItWaits(b, b.Character("ST29-010"), "Trash 1 card from your hand.");
            // Either it resolved outright, or it asked and the single legal answer settles it.
            if (b.S.Hand.Count == 1) Pick(b, only.InstanceId);
            Check("trash-from-hand with exactly 1 card ends with that card trashed",
                  b.S.Hand.Count == 0 && b.S.Trash.Any(c => c.InstanceId == only.InstanceId),
                  $"hand={b.S.Hand.Count} trashed={b.S.Trash.Any(c => c.InstanceId == only.InstanceId)}");
        }

        private static void TopOrBottomOfLifeWaitsForAPick()
        {
            var b = new Board(); b.Life(4);
            int life0 = b.S.Life.Count;
            bool waits = UseAndSeeIfItWaits(b, b.Character("ST29-009"),
                "Add 1 card from the top or bottom of your Life cards to your hand.");
            Check("top-or-bottom of Life waits for the player to pick an END",
                  waits && b.S.Life.Count == life0,
                  waits ? "" : $"it resolved on its own; life {life0}->{b.S.Life.Count} (the engine chose the end)");
        }

        private static void TopOrBottomOfLifeHonoursTheEndPicked()
        {
            // Life is stored bottom-first: index 0 is the BOTTOM, the last element is the TOP. Picking
            // the bottom must take the bottom - an engine that always takes the top would still pass a
            // count-only assertion, which is why this names the instance.
            var b = new Board(); b.Life(4);
            string bottom = b.S.Life[0].InstanceId;
            string top = b.S.Life[b.S.Life.Count - 1].InstanceId;
            bool waits = UseAndSeeIfItWaits(b, b.Character("ST29-009"),
                "Add 1 card from the top or bottom of your Life cards to your hand.");
            if (waits) Pick(b, bottom);
            bool gotBottom = b.S.Hand.Any(c => c.InstanceId == bottom);
            bool gotTop = b.S.Hand.Any(c => c.InstanceId == top);
            Check("picking the BOTTOM of Life takes the bottom card, not the top",
                  gotBottom && !gotTop,
                  $"bottomInHand={gotBottom} topInHand={gotTop}");
        }

        private static void DiscardChoiceIsNotSilentlyTheLastCard()
        {
            // The specific failure shape: the engine reaches for Hand[Hand.Count - 1]. If the player
            // picks a DIFFERENT card, that exact card must be the one that goes.
            var b = new Board();
            var want = b.Hand("ST29-004");      // index 0 - deliberately not the last
            b.Hand("OP15-020"); b.Hand("ST29-009");
            bool waits = UseAndSeeIfItWaits(b, b.Character("ST29-010"), "Trash 1 card from your hand.");
            if (waits) Pick(b, want.InstanceId);
            Check("the trashed card is the one the player picked, not the last in hand",
                  b.S.Trash.Any(c => c.InstanceId == want.InstanceId)
                  && b.S.Hand.All(c => c.InstanceId != want.InstanceId),
                  $"pickedWasTrashed={b.S.Trash.Any(c => c.InstanceId == want.InstanceId)} handLeft={b.S.Hand.Count}");
        }

        /// <summary>The COST form of the same wording. "You may trash 1 card from your hand: &lt;body&gt;"
        /// runs through the cost payer rather than the body resolver, so it is a separate code path
        /// that can auto-pick even when the body path asks properly - which is why the plain-body
        /// cases above passing proves nothing about this one.</summary>
        private static void CostFormTrashFromHandWaitsForAPick()
        {
            var b = new Board();
            b.Hand("ST29-004"); b.Hand("OP15-020"); b.Hand("ST29-009");
            int hand0 = b.S.Hand.Count;
            bool waits = UseAndSeeIfItWaits(b, b.Character("ST29-010"),
                "You may trash 1 card from your hand: Draw 1 card.");
            Check("COST form: pressing Use waits for the player to choose the card to trash",
                  waits && b.S.Hand.Count == hand0,
                  waits ? "" : $"it paid itself; hand {hand0}->{b.S.Hand.Count} (the engine chose the discard)");
        }

        private static void CostFormTrashTakesThePickedCard()
        {
            var b = new Board();
            var want = b.Hand("ST29-004");      // index 0 - deliberately not the last
            b.Hand("OP15-020"); b.Hand("ST29-009");
            bool waits = UseAndSeeIfItWaits(b, b.Character("ST29-010"),
                "You may trash 1 card from your hand: Draw 1 card.");
            if (waits) Pick(b, want.InstanceId);
            Check("COST form: the card trashed is the one the player picked",
                  b.S.Trash.Any(c => c.InstanceId == want.InstanceId)
                  && b.S.Hand.All(c => c.InstanceId != want.InstanceId),
                  $"pickedWasTrashed={b.S.Trash.Any(c => c.InstanceId == want.InstanceId)}");
        }

        /// <summary>EB03-001 Nefeltari Vivi (Leader): "If your Character with a base cost of 4 or more
        /// would be K.O.'d, you may trash 1 card from your hand instead." Paying that protection used
        /// to take Hand[Count-1] - the engine picking your discard at the moment the choice matters
        /// most. The victim must survive AND the discard must still be the player's to choose.</summary>
        private static void ProtectionDiscardIsThePlayersPick()
        {
            var b = new Board(); b.Leader("EB03-001");
            var want = b.Hand("ST29-004");          // index 0 - deliberately not the last
            b.Hand("ST29-009"); b.Hand("ST29-010");
            var victim = b.Character("ST29-009");   // base cost 4, so the protection applies
            int hand0 = b.S.Hand.Count;

            GameEngine.AuditKoByEffect(b.St, "south", victim.InstanceId);
            // The protection is offered as an optional pending effect; take it.
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId });

            bool alive = b.S.CharacterArea.Any(ch => ch != null && ch.InstanceId == victim.InstanceId);
            bool handUntouched = b.S.Hand.Count == hand0;
            bool asking = b.St.PendingEffects.Any(x => x != null && x.Seat == "south");
            Check("protection: the guard survives and the discard is still being ASKED",
                  alive && handUntouched && asking,
                  $"alive={alive} handUntouched={handUntouched} (hand {hand0}->{b.S.Hand.Count}) asking={asking}");

            if (asking) Pick(b, want.InstanceId);
            Check("protection: the card trashed is the one the player picked",
                  b.S.Trash.Any(x => x.InstanceId == want.InstanceId)
                  && b.S.Hand.All(x => x.InstanceId != want.InstanceId),
                  $"pickedWasTrashed={b.S.Trash.Any(x => x.InstanceId == want.InstanceId)} hand={b.S.Hand.Count}");
        }

        private static void ProtectionDiscardIsForcedWhenOnlyOneCardQualifies()
        {
            // Negative control for the same branch: a single card in hand is the only legal payment,
            // so queueing a pick would be an empty question. It must just pay and protect.
            var b = new Board(); b.Leader("EB03-001");
            var only = b.Hand("ST29-004");
            var victim = b.Character("ST29-009");
            GameEngine.AuditKoByEffect(b.St, "south", victim.InstanceId);
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId });
            bool alive = b.S.CharacterArea.Any(ch => ch != null && ch.InstanceId == victim.InstanceId);
            Check("protection with exactly 1 legal discard pays it outright, no empty question",
                  alive && b.S.Hand.Count == 0 && b.S.Trash.Any(x => x.InstanceId == only.InstanceId),
                  $"alive={alive} hand={b.S.Hand.Count} trashed={b.S.Trash.Any(x => x.InstanceId == only.InstanceId)}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "player-choice" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); St.PendingEffects.Clear();
            }

            public void Leader(string id)
            { var c = Card(id, "leader"); S.Leader = c; }

            public void Life(int n)
            { S.Life.Clear(); for (int i = 0; i < n; i++) S.Life.Add(Card("ST01-005", "life")); }

            public CardInstance Hand(string id)
            { var c = Card(id, "hand"); S.Hand.Add(c); return c; }

            public CardInstance Character(string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-pc-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
