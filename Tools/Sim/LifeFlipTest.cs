using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "flip life mechanics" — the brief's first Life keyword, and the one with the largest gap
    /// between what is checked and what is claimed.
    ///
    /// `lifefacing` already asks the NEGATIVE question: does any clause leave a Life card face-up
    /// that the text never asked to expose? That is a leak check, and it passes trivially when the
    /// mechanic is UNIMPLEMENTED — nothing face-up reads as perfectly clean. Asserting "unchanged"
    /// is what a dead effect produces, which is the same vacuous shape that made the opponent-facing
    /// rearrange case pass while doing nothing at all.
    ///
    /// So this file asks the POSITIVE question, on the family that turns facing into a resource:
    ///
    ///   COST        "You may turn 1 card from the top of your Life cards face-up: <effect>"
    ///               — 4 distinct wordings; paying it must actually flip the TOP Life card
    ///   CONDITION   "If you have a face-up Life card, <effect>" (EB03-051)
    ///               — must read the flag the cost just set. Two implementations of one fact is the
    ///               recurring bug in this engine, and here they live in different files entirely
    ///   CLEAR       "Turn all of your Life cards face-down." (EB01-052) — must clear it again
    ///   COUNT       a flip is not a heal and not a loss; the stack size never moves
    ///
    /// The pairing is the point. A cost that flips nothing and a condition that reads nothing agree
    /// with each other perfectly, and every count in the game stays right, while the whole mechanic
    /// does nothing.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifeflip
    /// </summary>
    public static class LifeFlipTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Flip Life: paying with facing, and reading it back ===");
            PayingTheFaceUpCostActuallyFlipsTheTopLifeCard();
            AFlipIsNotAHealAndNotALoss();
            TheFaceUpConditionReadsWhatTheCostSet();
            TurnAllFaceDownClearsIt();
            Console.WriteLine($"lifeflip: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private const string FlipCost =
            "You may turn 1 card from the top of your Life cards face-up: This Character gains +1000 power during this turn.";

        /// <summary>Life top is the LAST element. Flipping the bottom would be just as wrong as
        /// flipping nothing, and a count-only check cannot tell the two apart.</summary>
        private static void PayingTheFaceUpCostActuallyFlipsTheTopLifeCard()
        {
            var b = new Board();
            var top = b.S.Life.Last();

            b.Drive(FlipCost);

            bool topIsUp = b.S.Life.Contains(top) && top.FaceUp;
            int othersUp = b.S.Life.Count(c => c.FaceUp && c != top);
            Check("paying the face-up cost flips the TOP Life card, and only it",
                  topIsUp && othersUp == 0,
                  $"topFaceUp={topIsUp} othersFaceUp={othersUp} lifeCount={b.S.Life.Count} — "
                  + "a cost that flips nothing is invisible to a facing LEAK check");
        }

        private static void AFlipIsNotAHealAndNotALoss()
        {
            var b = new Board();
            int n0 = b.S.Life.Count;
            var ids = b.S.Life.Select(x => x.InstanceId).OrderBy(x => x).ToList();

            b.Drive(FlipCost);

            Check("flipping a Life card does not change the stack",
                  b.S.Life.Count == n0
                  && b.S.Life.Select(x => x.InstanceId).OrderBy(x => x).SequenceEqual(ids),
                  $"life {n0} -> {b.S.Life.Count}; turning a card face-up must not remove it");
        }

        /// <summary>EB03-051's gate must read the flag the cost sets. Driven both ways from the same
        /// fixture, so a condition that is simply always-true cannot pass.</summary>
        private static void TheFaceUpConditionReadsWhatTheCostSet()
        {
            const string Gated = "If you have a face-up Life card, draw 1 card.";

            var withNone = new Board();
            foreach (var c in withNone.S.Life) c.FaceUp = false;
            int h0 = withNone.S.Hand.Count;
            withNone.Drive(Gated);
            int drewWithout = withNone.S.Hand.Count - h0;

            var withOne = new Board();
            foreach (var c in withOne.S.Life) c.FaceUp = false;
            withOne.S.Life.Last().FaceUp = true;
            int h1 = withOne.S.Hand.Count;
            withOne.Drive(Gated);
            int drewWith = withOne.S.Hand.Count - h1;

            Check("\"if you have a face-up Life card\" reads the facing flag",
                  drewWith == 1 && drewWithout == 0,
                  $"face-up present -> drew {drewWith} (want 1); none -> drew {drewWithout} (want 0). "
                  + "Equal counts mean the condition ignores facing entirely");
        }

        private static void TurnAllFaceDownClearsIt()
        {
            var b = new Board();
            foreach (var c in b.S.Life) c.FaceUp = true;
            int n0 = b.S.Life.Count;

            b.Drive("Turn all of your Life cards face-down.");

            int stillUp = b.S.Life.Count(c => c.FaceUp);
            Check("\"turn all of your Life cards face-down\" clears every one",
                  stillUp == 0 && b.S.Life.Count == n0,
                  $"{stillUp} of {b.S.Life.Count} still face-up (life was {n0})");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-flip" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009" }) p.Hand.Add(Make(id, p.Seat, "hand"));
                    // Distinguishable, so "which card was flipped" is answerable, not just "how many".
                    foreach (var id in new[] { "ST01-005", "ST01-006", "EB01-004", "EB01-005" })
                        p.Life.Add(Make(id, p.Seat, "life"));
                    foreach (var c in p.Life) c.FaceUp = false;
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-lf-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.AbilityUsedThisTurn.Clear();
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 10; i++)
                {
                    if (St.DeckLook != null)
                    {
                        var order = St.DeckLook.Cards.Select(x => x.InstanceId).ToList();
                        int b0 = St.EventLog.Count;
                        St = GameEngine.ApplyCommand(St, new GameCommand
                        { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order });
                        if (St.EventLog.Count == b0) break;
                        continue;
                    }
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
                }
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(6)) Console.WriteLine("      log: " + e.Message);
            }

            public System.Collections.Generic.IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-lf-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
