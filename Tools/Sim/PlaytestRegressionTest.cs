using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>Regression coverage for the 2026-09-20 recorded playtest.</summary>
    public static class PlaytestRegressionTest
    {
        private static int passed, failed, serial;

        public static int Run()
        {
            Console.WriteLine("=== 2026-09-20 playtest regressions ===");
            LinlinDrawsThenOffersChoiceAndAura();
            NewestDecisionBlocksItsParent();
            FullBoardTrashPlayOffersReplacement();
            RogerAttachesThePlacedDon();
            DeckTopRevealStaysPublicAtItsLocation();
            LokiCanStopAfterOneLegalKo();
            Console.WriteLine($"playtestregression: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void LinlinDrawsThenOffersChoiceAndAura()
        {
            var st = Board("south");
            var linlin = Card("OP17-112", "south", "character");
            var hatchan = Card("OP03-033", "south", "character");
            st.Players["south"].CharacterArea[0] = linlin;
            st.Players["south"].CharacterArea[1] = hatchan;
            int hand0 = st.Players["south"].Hand.Count;
            GameEngine.QueueClauseForTest(st, "south", linlin, "onPlay",
                "Draw 1 card and choose one of the following effects.\n" +
                "- Add up to 1 card from the top of your deck to the top of your Life cards.\n" +
                "- Add up to 1 card from the top of your opponent's Life cards to the owner's hand.");
            Check("OP17-112 draws exactly once and waits for A/B", st.Players["south"].Hand.Count == hand0 + 1
                && st.ActiveChoice != null && st.ActiveChoice.Seat == "south" && st.PendingEffects.Count == 0);
            Check("OP17-112 sets eligible 4000 Trigger Characters to 8000 on your turn",
                GameEngine.GetPower(st, hatchan) == 8000, $"power={GameEngine.GetPower(st, hatchan)}");
            st.ActiveSeat = "north";
            Check("OP17-112 aura ends on the opponent's turn", GameEngine.GetPower(st, hatchan) == 4000,
                $"power={GameEngine.GetPower(st, hatchan)}");
        }

        private static void NewestDecisionBlocksItsParent()
        {
            var st = Board("south");
            var source = st.Players["south"].Leader;
            var first = Card("ST01-005", "south", "hand");
            var second = Card("ST01-006", "south", "hand");
            st.Players["south"].Hand.Add(first); st.Players["south"].Hand.Add(second);
            GameEngine.QueueClauseForTest(st, "south", source, "main", "You may trash 1 card from your hand: Draw 1 card.");
            string parent = st.PendingEffects.Last().EffectId;
            GameEngine.QueueClauseForTest(st, "south", source, "main", "You may trash 1 card from your hand: Draw 1 card.");
            var child = st.PendingEffects.Last();
            child.ParentEffectId = parent;
            int hand0 = st.Players["south"].Hand.Count;
            Check("the nested decision is the next resolvable effect",
                GameEngine.NextPendingEffect(st, "south")?.EffectId == child.EffectId);
            child.Seat = "north";
            Check("a parent stays blocked while the other seat owns its child",
                GameEngine.NextPendingEffect(st, "south") == null
                && GameEngine.NextPendingEffect(st, "north")?.EffectId == child.EffectId);
            child.Seat = "south";
            GameEngine.ApplyCommand(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = parent, Target = first.InstanceId });
            Check("a parent effect cannot advance past a newer nested decision",
                st.Players["south"].Hand.Count == hand0 && st.Players["south"].Hand.Any(c => c.InstanceId == first.InstanceId));
        }

        private static void FullBoardTrashPlayOffersReplacement()
        {
            var st = Board("south");
            var p = st.Players["south"];
            for (int i = 0; i < 5; i++) p.CharacterArea[i] = Card("ST01-005", "south", "character");
            var recur = Card("OP03-033", "south", "trash"); p.Trash.Add(recur);
            GameEngine.QueueClauseForTest(st, "south", p.Leader, "main",
                "Play up to 1 Character card with a cost of 4 or less from your trash.");
            var pe = st.PendingEffects.Last();
            GameEngine.ApplyCommand(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = recur.InstanceId });
            Check("a full board opens the six-character replacement choice", st.PendingCharReplace != null
                && st.PendingCharReplace.Held.InstanceId == recur.InstanceId && !p.Trash.Any(c => c.InstanceId == recur.InstanceId));
            string replaced = p.CharacterArea[0].InstanceId;
            GameEngine.ApplyCommand(st, new GameCommand { Type = "charReplace", Seat = "south", Target = replaced });
            Check("replacement puts the trash card in play and the old Character in trash",
                p.CharacterArea.Any(c => c != null && c.InstanceId == recur.InstanceId)
                && p.Trash.Any(c => c.InstanceId == replaced));
        }

        private static void RogerAttachesThePlacedDon()
        {
            var st = Board("north");
            var p = st.Players["south"];
            p.Leader = Card("OP13-003", "south", "leader");
            p.CostArea.Add(new DonInstance { InstanceId = "roger-existing-don", Rested = false });
            int attached0 = p.Leader.AttachedDonIds.Count;
            GameEngine.ApplyCommand(st, new GameCommand { Type = "endTurn", Seat = "north" });
            Check("OP13-003 receives the first DON placed in its DON phase",
                p.Leader.AttachedDonIds.Count == attached0 + 1, $"attached={p.Leader.AttachedDonIds.Count}");
        }

        private static void DeckTopRevealStaysPublicAtItsLocation()
        {
            var st = Board("south");
            var top = st.Players["south"].Deck[0];
            st.ActiveReveal = new PublicRevealState();
            st.ActiveReveal.Cards.Add(new RevealedCardRef
            {
                InstanceId = top.InstanceId, CardId = top.CardId, OwnerSeat = "south", ZoneAtReveal = "deck"
            });
            Check("a revealed top-deck card is visible in its deck location", GameEngine.IsPubliclyRevealed(st, top.InstanceId));
        }

        private static void LokiCanStopAfterOneLegalKo()
        {
            var pe = new PendingEffect { SourceCardId = "OP17-119", RemainingBudget = 1 };
            Check("OP17-119 can finish after one legal K.O.", GameEngine.IsEffectSkippable(pe));
        }

        private static GameState Board(string active)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "playtest-regression" });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = active; st.TurnNumber = 6;
            st.PendingEffects.Clear(); st.ActiveChoice = null; st.DeckLook = null; st.ActiveReveal = null;
            foreach (var p in st.Players.Values)
            {
                for (int i = 0; i < p.CharacterArea.Count; i++) p.CharacterArea[i] = null;
                p.Hand.Clear(); p.Trash.Clear(); p.Life.Clear(); p.CostArea.Clear();
                p.TurnsStarted = 3; p.DonDeck = 10;
            }
            return st;
        }

        private static CardInstance Card(string id, string owner, string zone) => new CardInstance
        {
            InstanceId = $"{owner}-{id}-pt-{++serial}", CardId = id, Owner = owner, Zone = zone,
            Rested = false, PlayedOnTurn = 0,
        };

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length == 0 ? "" : " -- " + detail)); }
        }
    }
}
