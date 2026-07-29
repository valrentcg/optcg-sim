using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "K.O. up to 1 of your opponent's Characters with 0 POWER or less" — the payoff half of the
    /// reduce-then-remove archetype (OP04-008, OP11-002, OP13-013, and OP15-114 Wyper, the card
    /// this whole workstream started from).
    ///
    /// WHY THIS EXISTS, and what it did NOT find. Diffing the engine's repeated regex literals
    /// showed the power cap spelled two ways — `(\d{1,5}) power or less` and `(\d{3,5}) power or
    /// less`. Three digits minimum cannot match "0", the K.O. resolver uses the permissive spelling
    /// and the glow filter at the generic Character branch uses the strict one, so the obvious
    /// conclusion was that these cards offer a K.O. with nothing highlighted.
    ///
    /// That conclusion is WRONG, and these cases are what proved it: both the capped wording
    /// ("0 power or less") and the exact wording ("with 0 power", OP06-103) are matched fine,
    /// because those clauses reach the filter through earlier branches that never consult the
    /// strict pattern. The divergence is real in the source and not reachable from the card pool.
    /// Six call sites were NOT changed on the strength of a theory the tests refuted.
    ///
    /// What remains is worth keeping: a Character only reaches 0 power AFTER a -power effect lands,
    /// and every other fixture in this suite builds boards from printed stats. This is the only
    /// place that state exists, so it guards the reduce-then-remove archetype against a future
    /// change to either spelling.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- zeropowerko
    /// </summary>
    public static class ZeroPowerKoTest
    {
        private static int passed, failed;

        private const string Clause = "K.O. up to 1 of your opponent's Characters with 0 power or less.";

        public static int Run()
        {
            Console.WriteLine("=== \"K.O. ... with 0 power or less\": is the weakened Character clickable? ===");
            AZeroedCharacterIsAValidTarget();
            AZeroedCharacterActuallyDies();
            AHealthyCharacterIsNotATarget();
            ExactZeroPowerIsAlsoMatchable();
            Console.WriteLine($"zeropowerko: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void AZeroedCharacterIsAValidTarget()
        {
            var b = new Board();
            var weak = b.WeakenedCharacter();
            b.Queue(Clause);
            var pe = b.Mine();
            if (pe == null) { Check("a zeroed Character is clickable", false, "no prompt was raised"); return; }

            Check("a Character reduced to 0 power is a valid target for the K.O.",
                  GameEngine.IsValidEffectTarget(b.St, pe, weak),
                  $"power={GameEngine.GetPower(b.St, weak)} but the glow filter refuses it — the player "
                  + "sees \"K.O. up to 1 …\" with nothing highlighted");
        }

        /// <summary>Clickable is not the same as effective: the click must also be accepted.</summary>
        private static void AZeroedCharacterActuallyDies()
        {
            var b = new Board();
            var weak = b.WeakenedCharacter();
            b.Queue(Clause);
            var pe = b.Mine();
            if (pe == null) { Check("a zeroed Character dies", false, "no prompt was raised"); return; }

            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = weak.InstanceId });

            Check("clicking it actually K.O.s it",
                  b.N.CharacterArea.All(c => c == null || c.InstanceId != weak.InstanceId),
                  "the target survived — glow and resolver disagree in the other direction");
        }

        /// <summary>The cap has to still MEAN something: a full-power Character must not be a legal
        /// target, or the fix would be "match everything".</summary>
        private static void AHealthyCharacterIsNotATarget()
        {
            var b = new Board();
            b.WeakenedCharacter();
            var healthy = b.HealthyCharacter();
            b.Queue(Clause);
            var pe = b.Mine();
            if (pe == null) { Check("a healthy Character is not a target", false, "no prompt was raised"); return; }

            Check("a full-power Character is NOT a valid target",
                  !GameEngine.IsValidEffectTarget(b.St, pe, healthy),
                  $"power={GameEngine.GetPower(b.St, healthy)} was accepted against a \"0 power or less\" cap");
        }

        /// <summary>The sibling wording, EXACT rather than capped: OP06-103 reads "Add up to 1 of
        /// your Characters with 0 power to the top or bottom of the owner's Life cards face-up."
        ///
        /// Worth its own case because the "or less" version above passes — the K.O. clause matches
        /// an earlier branch of the glow filter, so the strict `(\d{3,5})` spelling never bites it.
        /// The exact-power spelling is a different branch, and a three-digit minimum cannot express
        /// zero. This is the check that says whether the divergence is reachable anywhere.</summary>
        private static void ExactZeroPowerIsAlsoMatchable()
        {
            var b = new Board();
            var weak = b.WeakenedOwnCharacter();
            b.Queue("Add up to 1 of your Characters with 0 power to the top or bottom of the owner's Life cards face-up.");
            var pe = b.Mine();
            if (pe == null)
            {
                // Resolving outright with nothing moved would be its own failure; say which it was.
                Check("an exactly-0-power Character is reachable", false,
                      "no prompt was raised for a clause that must choose a Character");
                return;
            }
            Check("a Character at exactly 0 power is a valid target for the \"with 0 power\" wording",
                  GameEngine.IsValidEffectTarget(b.St, pe, weak),
                  $"power={GameEngine.GetPower(b.St, weak)} refused — a three-digit-minimum power "
                  + "pattern cannot express zero");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "zero-power-ko" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-zp-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                St.PendingEffects.Clear();
            }

            /// <summary>A Character actually reduced to 0 power by an effect — the state the clause
            /// is written for, and the one no other fixture here has ever built.</summary>
            public CardInstance WeakenedCharacter()
            {
                var c = Make("OP15-040", "north", "character");   // printed 2000
                N.CharacterArea[northSlot++] = c;
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main",
                    "Give up to 1 of your opponent's Characters -2000 power during this turn.");
                for (int i = 0; i < 4; i++)
                {
                    var pe = Mine();
                    if (pe == null) break;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = c.InstanceId });
                    if (St.EventLog.Count == before) break;
                }
                return c;
            }

            /// <summary>South's OWN Character, reduced to 0 power — "of YOUR Characters".</summary>
            public CardInstance WeakenedOwnCharacter()
            {
                var c = Make("OP15-040", "south", "character");   // printed 2000
                S.CharacterArea[1] = c;
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main",
                    "Give up to 1 of your Characters -2000 power during this turn.");
                for (int i = 0; i < 4; i++)
                {
                    var pe = Mine();
                    if (pe == null) break;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = c.InstanceId });
                    if (St.EventLog.Count == before) break;
                }
                return c;
            }

            public CardInstance HealthyCharacter()
            {
                var c = Make("EB03-002", "north", "character");   // printed 6000, untouched
                N.CharacterArea[northSlot++] = c;
                return c;
            }

            public PendingEffect Mine() =>
                St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");

            public void Queue(string clause) =>
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-zp-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
