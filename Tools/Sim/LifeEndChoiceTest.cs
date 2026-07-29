using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "…from the top OR BOTTOM of your Life cards" — 46 cards, and the one selective wording the
    /// `autopick` sweep deliberately skips, because it filters out "top of"/"bottom of" as positional.
    /// That exclusion is right for the 200-odd clauses naming ONE end and wrong for exactly these,
    /// so they need checking by hand rather than being quietly covered by a filter that cannot see
    /// them.
    ///
    /// It is a real decision, not a formality. Life damage comes off the TOP, so the end you take
    /// from decides whether you keep your next [Trigger] or spend it, and a face-up Life card makes
    /// the choice fully informed. Taking the top when the player wanted the bottom can hand the
    /// opponent a [Trigger] the player was holding, or throw away a face-up card they were saving.
    ///
    /// Two things have to hold, and the second is the one an engine that "works" usually fails:
    ///   1. both ends are reachable at all;
    ///   2. the player's stated end is the one taken — not merely a legal end.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifeend
    /// </summary>
    public static class LifeEndChoiceTest
    {
        private static int passed, failed;

        // The COST form ("You may <cost>: <body>") and the BODY form (a bare sentence, e.g. the
        // rider on OP08-116 / ST29-007) are separate handlers. Both are driven: one honouring the
        // named end while the other quietly takes the top is precisely the drift this engine keeps
        // producing, and testing only the cost form would not see it.
        private const string CostForm =
            "You may add 1 card from the top or bottom of your Life cards to your hand: Draw 1 card.";
        private const string BodyForm =
            "Add 1 card from the top or bottom of your Life cards to your hand.";

        private static string Clause = CostForm;

        public static int Run()
        {
            Console.WriteLine("=== \"top or bottom of your Life\": can the player actually pick the end? ===");
            foreach (var form in new[] { CostForm, BodyForm })
            {
                Clause = form;
                string label = ReferenceEquals(form, CostForm) ? "cost form" : "body form";
                Console.WriteLine($"  -- {label} --");
                TheTopEndIsReachable();
                TheBottomEndIsReachable();
                BothEndsAreDistinguishable();
            }
            Console.WriteLine($"lifeend: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Name a Life card and report which one actually reached hand.</summary>
        private static string TakeNaming(bool top, out string wanted)
        {
            var b = new Board();
            // Life top is the LAST element — the opposite of the deck, and the single most common
            // way to write this test backwards.
            var target = top ? b.S.Life[b.S.Life.Count - 1] : b.S.Life[0];
            wanted = target.InstanceId;

            GameEngine.QueueClauseForTest(b.St, "south", b.S.CharacterArea[0], "main", Clause);
            for (int i = 0; i < 6; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                int before = b.St.EventLog.Count;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target.InstanceId });
                if (b.St.EventLog.Count == before) break;
            }
            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                foreach (var e in b.St.EventLog.TakeLast(5)) Console.WriteLine("      log: " + e.Message);

            // Whatever left Life and arrived in hand. `wanted` is an out-param and cannot be
            // captured by a lambda, so copy it first.
            string want = target.InstanceId;
            var arrived = b.S.Hand.FirstOrDefault(c => c.InstanceId == want);
            return arrived?.InstanceId
                ?? b.S.Hand.Select(c => c.InstanceId).FirstOrDefault(id => b.Originals.Contains(id));
        }

        private static void TheTopEndIsReachable()
        {
            string got = TakeNaming(top: true, out string wanted);
            Check("naming the TOP Life card takes that card",
                  got == wanted,
                  got == null ? "no Life card reached hand at all" : "a different Life card was taken");
        }

        private static void TheBottomEndIsReachable()
        {
            string got = TakeNaming(top: false, out string wanted);
            Check("naming the BOTTOM Life card takes that card",
                  got == wanted,
                  got == null ? "no Life card reached hand at all"
                              : "a different Life card was taken — the bottom end is unreachable, "
                                + "so \"top or bottom\" is really \"top\"");
        }

        /// <summary>The two cases above could both pass against an engine that ignores the target and
        /// always takes the same end, IF that end happened to be the one named. Asserting they differ
        /// is what rules that out.</summary>
        private static void BothEndsAreDistinguishable()
        {
            string top = TakeNaming(top: true, out string wantTop);
            string bottom = TakeNaming(top: false, out string wantBottom);
            Check("the two ends are genuinely different cards",
                  top != null && bottom != null && wantTop != wantBottom && top != bottom,
                  $"top={top ?? "(none)"} bottom={bottom ?? "(none)"} — if these match, the end named "
                  + "is being ignored");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public readonly System.Collections.Generic.HashSet<string> Originals =
                new System.Collections.Generic.HashSet<string>();
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-end" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-le-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                // Four DISTINGUISHABLE Life cards, so "which end" is answerable by identity.
                foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005", "ST01-006" })
                {
                    var c = Make(id, "south", "life");
                    S.Life.Add(c); Originals.Add(c.InstanceId);
                }
                for (int i = 0; i < 4; i++) St.Players["north"].Life.Add(Make("ST01-005", "north", "life"));
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                St.PendingEffects.Clear();
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-le-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
