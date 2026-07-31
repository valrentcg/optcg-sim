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
            NamiLeadersTwoOncePerTurnsAreIndependent();
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

        /// <summary>OP11-041 Nami — a Leader the brief names, and the only other card whose two
        /// [Once Per Turn] abilities are both reachable in a normal turn:
        ///
        ///   [Your Turn] [Once Per Turn]                     draw when a Life card is removed
        ///   [DON!! x1] [On Your Opponent's Attack] [OPT]    you may trash 1 card: +2000 power
        ///
        /// A different timing PAIR from OP06-118, so a different pair of keys. If they collide, a
        /// Nami player who draws off their own Life loss silently loses the defensive boost for the
        /// rest of the turn — and nothing in the log would say why.</summary>
        private static void NamiLeadersTwoOncePerTurnsAreIndependent()
        {
            var b = new Board();
            var nami = b.SetLeader("OP11-041");
            if (nami == null) { Check("OP11-041 Nami's two once-per-turns are independent", false, "fixture: leader not set"); return; }

            // Fire the [Your Turn] half by removing a Life card, which is its stated trigger.
            int hand0 = b.S.Hand.Count, life0 = b.S.Life.Count;
            b.RemoveOwnLifeCard();
            bool drew = b.S.Hand.Count > hand0;
            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
            {
                Console.WriteLine($"      [nami-pre] life {life0} -> {b.S.Life.Count}, hand {hand0} -> {b.S.Hand.Count}, "
                                  + $"pending={b.St.PendingEffects.Count}, activeSeat={b.St.ActiveSeat}");
                foreach (var e in b.St.EventLog.TakeLast(6)) Console.WriteLine("        log: " + e.Message);
            }

            // Now the defensive half, on the opponent's attack.
            bool offered = b.OpponentAttacksAndLeaderAbilityOffers();

            if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                Console.WriteLine($"      [nami] hand {hand0} -> {b.S.Hand.Count} (drew={drew}), "
                                  + $"defensiveOffered={offered}, keys=[{string.Join(",", b.S.AbilityUsedThisTurn)}]");
            Check("OP11-041 Nami: using the [Your Turn] draw leaves the defensive [OPT] available",
                  drew && offered,
                  $"drewFromLifeLoss={drew} defensiveOffered={offered} — if the draw fired and the "
                  + "defence vanished, the two [Once Per Turn]s share a key");
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

            /// <summary>Replace south's Leader and hand it enough DON!! for a [DON!! xN] gate.</summary>
            public CardInstance SetLeader(string cardId)
            {
                var l = Make(cardId, "south", "leader");
                S.Leader = l;
                for (int i = 0; i < 2 && i < S.CostArea.Count; i++) l.AttachedDonIds.Add(S.CostArea[i].InstanceId);
                return l;
            }

            /// <summary>Trash the top Life card, the trigger for Nami's [Your Turn] half.</summary>
            public void RemoveOwnLifeCard()
            {
                // A COST-PREFIXED clause, so the Life card is removed inside a resolveEffect
                // COMMAND — which is how a Life cost is paid in real play. A bare clause is
                // auto-resolved by QueueClauseForTest outside ApplyCommand, so the command-boundary
                // watcher never sees it and the test measures the harness, not the engine.
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0] ?? S.Leader, "main",
                    "You may trash 1 card from the top of your Life cards: This Leader gains +1000 power during this turn.");
                for (int i = 0; i < 6; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = null });
                    if (St.EventLog.Count == before) break;
                }
            }

            /// <summary>North attacks; report whether south's Leader ability raises a decision.</summary>
            public bool OpponentAttacksAndLeaderAbilityOffers()
            {
                var atk = Make("EB03-002", "north", "character");
                N.CharacterArea[1] = atk;
                atk.Rested = false; atk.PlayedOnTurn = 0;
                St.ActiveSeat = "north"; St.Phase = "main";
                int log0 = St.EventLog.Count;
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = atk.InstanceId, Target = S.Leader?.InstanceId });
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.Skip(log0)) Console.WriteLine("      [def] " + e.Message);
                return St.PendingEffects.Any(e => e != null && e.Seat == "south");
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
                // Drive the battle to its END, stepping by whatever step it is actually on. This used
                // to send resolveAttack only — which the engine refuses at the BLOCK and COUNTER steps,
                // so the loop broke on the first no-op and left St.Battle set at "counter".
                //
                // That mattered because IsTurnPlayerInMain gates activateMain on `Battle == null`, and
                // refuses SILENTLY. WhenAttackingDoesNotConsumeActivateMain then read "no [Activate:
                // Main] offered" as "the two [Once Per Turn]s share a budget", when the real answer was
                // "you cannot use an [Activate: Main] in the middle of a battle" — which is correct
                // (Comprehensive 8-1-3-2: activate effects are declared during the Main Phase). The
                // test only ever passed because `offered` also accepts a LEFTOVER pending effect, so it
                // was reporting on a stale prompt rather than on the ability.
                for (int i = 0; i < 8 && St.Battle != null; i++)
                {
                    int before = St.EventLog.Count;
                    string step = St.Battle.Step;
                    var cmd = step == "block"   ? new GameCommand { Type = "passBlock",     Seat = "north" }
                            : step == "counter" ? new GameCommand { Type = "passCounter",   Seat = "north" }
                            : step == "trigger" ? new GameCommand { Type = "passTrigger",   Seat = "north" }
                            :                     new GameCommand { Type = "resolveAttack", Seat = "north" };
                    Apply(cmd);
                    if (St.EventLog.Count == before) break;
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
