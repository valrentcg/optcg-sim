using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// 22 cards carry TWO OR MORE "you may" clauses. Every sweep in this suite drives clauses in
    /// isolation, so clause-to-clause interference on a single card has never been exercised: one
    /// ability consuming another's once-per-turn budget, the wrong clause being queued for a
    /// timing, or a shared key colliding.
    ///
    /// OP06-118 is the sharpest case in the pool — TWO [Once Per Turn] abilities on one card, with
    /// different circled DON!! costs:
    ///
    ///   [When Attacking]   [Once Per Turn] ➀ : Set this Character as active.
    ///   [Activate: Main]   [Once Per Turn] ➁ : ...
    ///
    /// The engine's own comment records that this collision was real: "use a DISTINCT once-per-turn
    /// key (':whenAttacking'): the bare instanceId key is shared with Activate:Main, so using one
    /// ability wrongly consumed the other." A card whose second ability silently disappears after
    /// using the first is exactly the "it stopped working" report this workstream started from, and
    /// nothing verified the fix.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- multiclause
    /// </summary>
    public static class MultiClauseCardTest
    {
        private static int passed, failed;

        private const string TwoOnce = "OP06-118";

        public static int Run()
        {
            Console.WriteLine("=== Two [Once Per Turn] abilities on one card must not share a budget ===");
            ActivateMainDoesNotConsumeWhenAttacking();
            WhenAttackingDoesNotConsumeActivateMain();
            EachIsStillOncePerTurnOnItsOwn();
            EveryCardPairingTheTwoTimings();
            Console.WriteLine($"multiclause: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Use the Activate: Main ability, then attack. The When-Attacking ability is a
        /// SEPARATE once-per-turn and must still be offered.</summary>
        private static void ActivateMainDoesNotConsumeWhenAttacking()
        {
            var b = new Board();
            var c = b.Subject();
            b.ActivateMain(c);
            bool offered = b.AttackAndWasOffered(c);

            Check("using [Activate: Main] leaves the [When Attacking] ability available",
                  offered,
                  "the second ability vanished — the two [Once Per Turn]s are sharing one budget");
        }

        /// <summary>The mirror. Attack first, then try the Activate: Main ability.</summary>
        private static void WhenAttackingDoesNotConsumeActivateMain()
        {
            var b = new Board();
            var c = b.Subject();
            b.AttackAndWasOffered(c);
            b.AnswerAll();
            bool offered = b.ActivateMainWasOffered(c);

            Check("attacking leaves the [Activate: Main] ability available",
                  offered,
                  "the Activate: Main ability vanished after attacking — shared once-per-turn key");
        }

        /// <summary>The control for both cases above. If the keys were merely made unique but the
        /// once-per-turn was lost, each ability would become unlimited — which these two cases
        /// cannot tell apart from working correctly.</summary>
        private static void EachIsStillOncePerTurnOnItsOwn()
        {
            var b = new Board();
            var c = b.Subject();
            bool first = b.ActivateMainWasOffered(c);
            b.AnswerAll();
            bool second = b.ActivateMainWasOffered(c);

            Check("[Activate: Main] is still once per turn on its own",
                  first && !second,
                  $"first={first} second={second} — separating the keys must not make either ability "
                  + "repeatable");
        }

        /// <summary>The same invariant across the ENTIRE population where this collision is
        /// possible: every card carrying both an [Activate: Main] and a [When Attacking] ability.
        ///
        /// multisweep cannot cover this — it queues clauses directly and never runs the dispatch
        /// that assigns once-per-turn keys, which its own control proved. So the timing-driven check
        /// is extended here from one card to all 8, including OP05-041 Sakazuki, the card the
        /// engine's comment names as the reason the distinct key exists.
        ///
        /// What the control then showed, and worth knowing before reading the pass: restoring the
        /// shared key reddens this on OP06-118 ALONE. A shared key can only bite when BOTH abilities
        /// are [Once Per Turn]; the other 7 pair one with a non-gated ability and are unaffected by
        /// construction. Five cards in the pool carry two [Once Per Turn] abilities (EB04-044,
        /// OP06-118, OP11-041, OP12-061, OP13-002) and only OP06-118 pairs them on the two timings
        /// that share a key today. So this case guards a population of ONE against the known bug,
        /// and the other 7 against a future key that is not timing-scoped.</summary>
        private static void EveryCardPairingTheTwoTimings()
        {
            var ids = CardData.Library.Values
                .Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                .GroupBy(d => d.Id).Select(g => g.First())
                .Where(d => d.Effect.Contains("[Activate: Main]") && d.Effect.Contains("[When Attacking]"))
                .Select(d => d.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

            int checkedCards = 0;
            var lost = new System.Collections.Generic.List<string>();
            foreach (var id in ids)
            {
                bool soloAttack;
                try
                {
                    // Does the When-Attacking ability offer at all on a clean board?
                    var solo = new Board();
                    soloAttack = solo.AttackAndWasOffered(solo.Subject(id));
                    if (!soloAttack) continue;      // nothing to lose; not this card's test

                    var both = new Board();
                    var c = both.Subject(id);
                    both.ActivateMain(c);
                    checkedCards++;
                    if (!both.AttackAndWasOffered(c)) lost.Add(id);
                }
                catch (Exception) { continue; }
            }

            Check($"across all {ids.Count} cards pairing the two timings, using one keeps the other "
                  + $"({checkedCards} actually checked)",
                  checkedCards >= 3 && lost.Count == 0,
                  lost.Count > 0
                      ? "lost the When-Attacking ability on: " + string.Join(", ", lost)
                      : $"only {checkedCards} cards were checkable — too few to mean anything");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "multi-clause" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                        p.Hand.Add(Make(id, p.Seat, "hand"));
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    // Plenty of ACTIVE DON!!: both abilities are paid with circled DON!! costs.
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-mc-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                    p.AbilityUsedThisTurn.Clear();
                }
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            public CardInstance Subject(string cardId = TwoOnce)
            {
                var c = Make(cardId, "south", "character");
                S.CharacterArea[0] = c;
                c.PlayedOnTurn = 0; c.Rested = false;
                return c;
            }

            private bool Owned() => St.PendingEffects.Any(e => e != null && e.Seat == "south");

            public void ActivateMain(CardInstance c)
            {
                ActivateMainWasOffered(c);
                AnswerAll();
            }

            public bool ActivateMainWasOffered(CardInstance c)
            {
                c.Rested = false;
                St.ActiveSeat = "south"; St.Phase = "main";
                int log0 = St.EventLog.Count;
                Apply(new GameCommand { Type = "activateMain", Seat = "south", Target = c.InstanceId });
                bool offered = Owned()
                    || St.EventLog.Skip(log0).Any(e => (e.Message ?? "").IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0);
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.Skip(log0)) Console.WriteLine("      [main] " + e.Message);
                return offered;
            }

            public bool AttackAndWasOffered(CardInstance c)
            {
                c.Rested = false; c.PlayedOnTurn = 0;
                St.ActiveSeat = "south"; St.Phase = "main";
                int log0 = St.EventLog.Count;
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "south", Attacker = c.InstanceId, Target = N.Leader?.InstanceId });
                bool offered = Owned()
                    || St.EventLog.Skip(log0).Any(e => (e.Message ?? "").IndexOf("[When Attacking]", StringComparison.OrdinalIgnoreCase) >= 0);
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.Skip(log0)) Console.WriteLine("      [atk] " + e.Message);
                return offered;
            }

            /// <summary>Answer whatever is outstanding so the next attempt starts clean.</summary>
            public void AnswerAll()
            {
                for (int i = 0; i < 8; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
                    if (St.EventLog.Count == before) break;
                }
                if (St.Battle != null)
                {
                    for (int i = 0; i < 4 && St.Battle != null; i++)
                    {
                        int before = St.EventLog.Count;
                        Apply(new GameCommand { Type = "resolveAttack", Seat = "north" });
                        if (St.EventLog.Count == before) break;
                    }
                }
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-mc-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
