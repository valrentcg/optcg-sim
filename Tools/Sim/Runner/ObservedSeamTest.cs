using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Planning;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// Proves the ENFORCED observed-agent seam holds (P0 #1) with assertions matched to the acceptance
    /// criteria: actions are ENGINE-VALIDATED (not a superset), descriptors are FAITHFUL (duplicates,
    /// options, deck-look order, pending prompt all distinguishable), tokens are DETERMINISTIC and
    /// fail-closed, the legacy boundary is EXPLICIT (unknown agents rejected), and the end-to-end run
    /// asserts a real finish (not an adjudicated cap).
    /// </summary>
    public static class ObservedSeamTest
    {
        private static readonly Type[] Forbidden =
        {
            typeof(GameState), typeof(CardInstance), typeof(GameCommand), typeof(LogEntry),
            typeof(BattleState), typeof(PlayerState), typeof(PendingEffect), typeof(DeckLookState),
            typeof(ChoiceState), typeof(DonInstance),
        };

        public static int Run(DeckDef south, DeckDef north)
        {
            Console.WriteLine("=== enforced observed-agent seam ===");
            int fail = 0;
            fail += Section("API: IObservedAgent/context name no GameState", CheckApi());
            fail += Section("reachability: no referee object / instance id / secret in context", CheckReachability(south, north));
            fail += Section("actions are engine-validated (each changes state)", CheckValidated(south, north));
            fail += Section("descriptors are faithful (distinct actions ⇒ distinct descriptors)", CheckFaithful(south, north));
            fail += Section("action fidelity + deterministic tokens + fail-closed", CheckFidelity(south, north));
            fail += Section("legacy boundary is explicit (unknown agent rejected)", CheckLegacyBoundary(south, north));
            fail += Section("end-to-end: match driven through the seam", CheckFullMatch(south, north));
            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "PASS - observed agents cannot receive/reach a GameState; actions are validated, faithful, and fail closed."
                : $"FAIL - {fail} seam check(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        private static int Section(string name, (bool ok, string detail) r)
        {
            Console.WriteLine($"  [{(r.ok ? "ok  " : "FAIL")}] {name}{(r.detail == null ? "" : " - " + r.detail)}");
            return r.ok ? 0 : 1;
        }

        // ---- (1) API-level -------------------------------------------------------------------------

        private static (bool, string) CheckApi()
        {
            var decide = typeof(IObservedAgent).GetMethod(nameof(IObservedAgent.Decide));
            foreach (var pr in decide.GetParameters())
                if (Forbidden.Contains(pr.ParameterType))
                    return (false, $"IObservedAgent.Decide takes {pr.ParameterType.Name}");

            foreach (var t in new[] { typeof(PlayerDecisionContext), typeof(ObservedAction),
                                      typeof(PlayerObservation), typeof(ObservedZone), typeof(ObservedCard),
                                      typeof(ObservedBattle), typeof(ObservedDon), typeof(ObservedChoice),
                                      typeof(ObservedDeckLook), typeof(ObservedPending) })
                foreach (var m in DeclaredMemberTypes(t))
                    if (Forbidden.Contains(m.type))
                        return (false, $"{t.Name}.{m.name} is {m.type.Name}");
            return (true, null);
        }

        private static IEnumerable<(string name, Type type)> DeclaredMemberTypes(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) yield return (f.Name, f.FieldType);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) yield return (p.Name, p.PropertyType);
        }

        // ---- (2) reachability ----------------------------------------------------------------------

        private static (bool, string) CheckReachability(DeckDef south, DeckDef north)
        {
            var probes = new (string name, Func<GameState, bool> hit, Func<GameState, string> viewer)[]
            {
                ("main",           st => PrivacyTest.IsCleanSouthDecision(st, out _),                              st => "south"),
                ("battle:counter", st => st.Battle != null && st.Battle.Step == "counter",                        st => st.Battle.TargetSeat),
                ("battle:trigger", st => st.Battle != null && st.Battle.Step == "trigger" && st.Battle.RevealedLife != null, st => st.Battle.AttackerSeat),
            };
            foreach (var (name, hit, viewerOf) in probes)
            {
                GameState st = name == "main"
                    ? PrivacyTest.SouthDecision(south, north, $"seam:{name}", 40)
                    : PrivacyTest.ReachFirst(south, north, $"seam:{name}", hit);
                if (st == null) continue;
                string seat = viewerOf(st);
                var trueIds = AllInstanceIds(st);
                PrivacyTest.PoisonHidden(st, seat);
                if (st.Battle?.RevealedLife != null) st.Battle.RevealedLife.CardId = PrivacyTest.PoisonTag + "-REVLIFE";

                var decision = Build(st, seat, south, north, ListAssumption.OpenList, 0);
                var objs = ReachableObjects(decision.Context);
                var badType = objs.FirstOrDefault(o => Forbidden.Contains(o.GetType()));
                if (badType != null) return (false, $"{name}: {badType.GetType().Name} reachable from context");
                var strings = objs.OfType<string>().ToList();
                if (strings.Any(s => s.Contains(PrivacyTest.PoisonTag))) return (false, $"{name}: poison string reachable");
                var leakedId = strings.FirstOrDefault(s => trueIds.Contains(s));
                if (leakedId != null) return (false, $"{name}: true instance id '{leakedId}' reachable");
            }

            // SYNTHETIC deck-look probe — these decks don't reach a real deck look, but the source-instance
            // leak must still be pinned. Inject a DeckLook whose SourceInstanceId is a POISON instance id and
            // require it to be unreachable from the searcher's context (it must be resolved to a CardId or
            // dropped, never surfaced as the raw instance id).
            var ds = PrivacyTest.SouthDecision(south, north, "seam:decklook-src", 40);
            if (ds != null)
            {
                var looked = ds.Players["south"].Deck.Take(3).ToList();
                ds.DeckLook = new DeckLookState
                {
                    Seat = "south", Step = "select",
                    SourceInstanceId = PrivacyTest.PoisonTag + "-SRCINST",
                    Cards = looked,
                };
                var dsIds = AllInstanceIds(ds);
                foreach (var c in looked) dsIds.Add(c.InstanceId);
                var decision = Build(ds, "south", south, north, ListAssumption.UnknownList, 0);
                var strings = ReachableObjects(decision.Context).OfType<string>().ToList();
                if (strings.Any(s => s.Contains(PrivacyTest.PoisonTag)))
                    return (false, "deckLook: DeckLook.SourceInstanceId reachable from context (true instance id leaked)");
                var leaked = strings.FirstOrDefault(s => dsIds.Contains(s));
                if (leaked != null)
                    return (false, $"deckLook: true instance id '{leaked}' reachable from context");
                if (decision.Context.Observation.DeckLook == null || decision.Context.Observation.DeckLook.Cards.Count == 0)
                    return (false, "deckLook: searcher not entitled to see the looked cards");
            }
            return (true, null);
        }

        // ---- (3) validated actions (defect 1) ------------------------------------------------------

        private static (bool, string) CheckValidated(DeckDef south, DeckDef north)
        {
            int checkedActions = 0;
            foreach (var (name, st, seat) in DecisionFixtures(south, north, includeCoinflip: true))
            {
                var decision = Build(st, seat, south, north, ListAssumption.UnknownList, 0);

                // Coin flip and mulligan are legal BY CONSTRUCTION (the two allowed options) and are
                // fingerprint-neutral (the fingerprint tracks board state, not the mulligan/turn-order flag),
                // so they are enumerated directly, not filtered by Validate. Assert their by-construction shape.
                if (st.Status == "coinflip" || st.Status == "mulligan")
                {
                    if (decision.ActionCount != 2) return (false, $"{name}: expected 2 by-construction options, got {decision.ActionCount}");
                    continue;
                }

                // Everything else came through LegalActions.Validate (the superset filtered by the engine),
                // so EVERY offered action must actually change authoritative state.
                foreach (var a in decision.Context.Actions)
                {
                    var cmd = decision.Translate(a);
                    if (cmd == null) return (false, $"{name}: offered token did not translate");
                    var clone = GameClone.Clone(st);
                    long before = LegalActions.StateFingerprint(clone);
                    GameEngine.ApplyCommand(clone, cmd);
                    if (LegalActions.StateFingerprint(clone) == before)
                        return (false, $"{name}: offered action '{a.Type}' is a NO-OP (not validated legal)");
                    checkedActions++;
                }
            }
            return checkedActions == 0 ? (false, "no Validate-path actions were checked")
                                       : (true, $"{checkedActions} validated actions each changed state");
        }

        // ---- (4) faithful descriptors (defect 2) ---------------------------------------------------

        private static (bool, string) CheckFaithful(DeckDef south, DeckDef north)
        {
            bool sawChoice = false, sawDeckLook = false, sawPending = false;
            foreach (var (name, st, seat) in DecisionFixtures(south, north))
            {
                var decision = Build(st, seat, south, north, ListAssumption.UnknownList, 0);
                var acts = decision.Context.Actions;

                // Distinct legal actions must have DISTINCT faithful descriptors — otherwise duplicate copies
                // (or A/B options) are indistinguishable, which is exactly the defect.
                var keys = acts.Select(FaithfulKey).ToList();
                if (keys.Distinct().Count() != keys.Count)
                {
                    var dup = keys.GroupBy(k => k).First(g => g.Count() > 1).Key;
                    return (false, $"{name}: two distinct actions share descriptor «{dup}»");
                }

                var obs = decision.Context.Observation;

                // Every actor/target surrogate an action names must be an actual surrogate in the observation
                // — that IS the linkage between actions and the cards the agent sees (defect 2).
                var obsSurrogates = new HashSet<string>(
                    obs.Zones.SelectMany(z => z.Cards).Select(c => c.SurrogateId)
                       .Concat(obs.DeckLook?.Cards.Select(c => c.SurrogateId) ?? Enumerable.Empty<string>()));
                foreach (var a in acts)
                {
                    if (a.ActorSurrogate != null && !obsSurrogates.Contains(a.ActorSurrogate))
                        return (false, $"{name}: action actor surrogate '{a.ActorSurrogate}' not in observation");
                    if (a.TargetSurrogate != null && !obsSurrogates.Contains(a.TargetSurrogate))
                        return (false, $"{name}: action target surrogate '{a.TargetSurrogate}' not in observation");
                }

                if (obs.Choice != null) { sawChoice = true; if (obs.Choice.OptionA == null && obs.Choice.OptionB == null) return (false, $"{name}: choice has no option text"); }
                if (obs.DeckLook != null)
                {
                    sawDeckLook = true;
                    var order = acts.FirstOrDefault(a => a.OrderedSurrogates != null);
                    if (obs.DeckLook.Cards.Count > 0 && order == null) return (false, $"{name}: deck look has cards but no ordering action");
                }
                if (obs.Pending.Count > 0)
                {
                    sawPending = true;
                    if (acts.Any(a => a.Type == "resolveEffect") && acts.All(a => a.PromptText == null && a.Type != "passEffect"))
                        return (false, $"{name}: pending targeting exposes no prompt");
                }
            }
            return (true, $"choice={sawChoice} deckLook={sawDeckLook} pending={sawPending}");
        }

        // ---- (5) fidelity + deterministic tokens + fail-closed -------------------------------------

        private static (bool, string) CheckFidelity(DeckDef south, DeckDef north)
        {
            var reached = new List<string>();
            foreach (var (name, st, seat) in DecisionFixtures(south, north, includeCoinflip: true))
            {
                var knowledge = Know(seat, south, north, ListAssumption.UnknownList);
                var decision = ObservedSeam.Build(st, seat, knowledge, "fid", 3, 7);
                if (decision.ActionCount == 0) continue;
                reached.Add(name);

                foreach (var a in decision.Context.Actions)
                {
                    var cmd = decision.Translate(a);
                    if (cmd == null) return (false, $"{name}: token did not translate");
                    if (cmd.Type != a.Type) return (false, $"{name}: type {cmd.Type} != descriptor {a.Type}");
                    if (cmd.Seat != seat) return (false, $"{name}: command seat {cmd.Seat} != {seat}");
                }

                // Deterministic tokens: rebuilding the SAME decision reproduces identical tokens.
                var again = ObservedSeam.Build(st, seat, knowledge, "fid", 3, 7);
                var t1 = decision.Context.Actions.Select(a => a.Token).ToList();
                var t2 = again.Context.Actions.Select(a => a.Token).ToList();
                if (!t1.SequenceEqual(t2)) return (false, $"{name}: tokens not deterministic across identical builds");

                // Fail-closed: null / forged / STALE (different decision sequence) tokens translate to nothing.
                if (decision.Translate(null) != null) return (false, $"{name}: null choice translated");
                if (decision.Translate(new ObservedAction { Token = "forged#0" }) != null) return (false, $"{name}: forged token translated");
                var other = ObservedSeam.Build(st, seat, knowledge, "fid", 4, 9);   // different decision sequence
                if (other.ActionCount > 0 && decision.Translate(other.Context.Actions[0]) != null)
                    return (false, $"{name}: STALE token from another decision translated (not fail-closed)");
            }
            if (reached.Count < 4) return (false, $"only reached {reached.Count}: {string.Join(",", reached)}");
            Console.WriteLine($"         reached: {string.Join(", ", reached)}");
            return (true, null);
        }

        // ---- (6) explicit legacy boundary (defect 5) -----------------------------------------------

        private static (bool, string) CheckLegacyBoundary(DeckDef south, DeckDef north)
        {
            // A plain IAgent works (wrapped as legacy). An arbitrary object that is neither ILegacyAgent nor
            // IObservedAgent must be REJECTED, not silently handed the GameState.
            try
            {
                GameRunner.Play((object)"not-an-agent", (object)new RandomObservedAgent(), south, north,
                    "seam:reject", "south", ListAssumption.UnknownList, cmdCap: 50, maxTurns: 2);
                return (false, "an unknown agent object was NOT rejected");
            }
            catch (InvalidOperationException) { /* expected: fail closed */ }
            return (true, "unknown agent object rejected fail-closed");
        }

        // ---- (7) end-to-end ------------------------------------------------------------------------

        private static (bool, string) CheckFullMatch(DeckDef south, DeckDef north)
        {
            int ran = 0, finished = 0, capped = 0, stuck = 0;
            for (int t = 0; t < 4; t++)
            {
                GameState st;
                try
                {
                    st = GameRunner.Play((object)new RandomObservedAgent("A"), (object)new RandomObservedAgent("B"),
                        south, north, $"seam-match:{t}", "south", ListAssumption.UnknownList, cmdCap: 20000, maxTurns: 60);
                }
                catch (Exception e) { return (false, $"match {t} threw: {e.GetType().Name}: {e.Message}"); }
                ran++;
                if (GameRunner.Stuck) stuck++;
                else if (st.Status == "finished" && GameRunner.Winner(st) != null) finished++;
                else capped++;
            }
            // A real full-match proof requires EVERY attempted match to finish outright: finished == ran,
            // capped == 0, stuck == 0. A single capped/stuck game means the seam did not drive a full match.
            string detail = $"{finished}/{ran} finished outright, {capped} capped, {stuck} stuck (all driven through the seam)";
            if (finished != ran || capped != 0 || stuck != 0)
                return (false, detail + " — full-match drive requires finished==ran, capped==0, stuck==0");
            return (true, detail);
        }

        // ---- helpers -------------------------------------------------------------------------------

        /// <summary>Reach a spread of decision types for the fidelity/faithfulness checks. Opportunistic (the
        /// exhaustive deterministic per-boundary suite is task 11); coin flip is built directly since the
        /// runner forces turn order outside the seam.</summary>
        private static IEnumerable<(string name, GameState st, string seat)> DecisionFixtures(
            DeckDef south, DeckDef north, bool includeCoinflip = false)
        {
            if (includeCoinflip)
            {
                var cf = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = south, NorthDeckDef = north, Seed = "seam:coinflip" });
                if (cf.Status == "coinflip") yield return ("coinflip", cf, cf.CoinFlipWinner);
            }
            var probes = new (string name, Func<GameState, bool> hit, Func<GameState, string> seat)[]
            {
                ("mulligan",       st => st.Status == "mulligan" && !st.Players["south"].MulliganDecided, st => "south"),
                ("main",           st => PrivacyTest.IsCleanSouthDecision(st, out _),  st => "south"),
                ("battle:block",   st => st.Battle != null && st.Battle.Step == "block",   st => st.Battle.TargetSeat),
                ("battle:counter", st => st.Battle != null && st.Battle.Step == "counter", st => st.Battle.TargetSeat),
                ("battle:trigger", st => st.Battle != null && st.Battle.Step == "trigger", st => st.Battle.TargetSeat),
                ("choice",         st => st.ActiveChoice != null,  st => st.ActiveChoice.Seat),
                ("deckLook",       st => st.DeckLook != null,      st => st.DeckLook.Seat),
                ("pendingTarget",  st => st.PendingEffects.Count > 0, st => st.PendingEffects[0].Seat),
            };
            foreach (var (name, hit, seatOf) in probes)
            {
                GameState st = null;
                for (int t = 0; t < 6 && st == null; t++) st = PrivacyTest.ReachFirst(south, north, $"seam:fix:{name}:{t}", hit);
                if (st != null) yield return (name, st, seatOf(st));
            }
        }

        private static ObservedSeam.Decision Build(GameState st, string seat, DeckDef s, DeckDef n, ListAssumption a, int seq)
            => ObservedSeam.Build(st, seat, Know(seat, s, n, a), "seam", seq, 0);

        private static KnowledgeState Know(string seat, DeckDef s, DeckDef n, ListAssumption a) => new KnowledgeState
        {
            Seat = seat, OwnList = seat == "south" ? s : n,
            OpponentList = a == ListAssumption.OpenList ? (seat == "south" ? n : s) : null, Assumption = a,
        };

        private static string FaithfulKey(ObservedAction a) =>
            string.Join("|", a.Type, a.ActorSurrogate ?? "-", a.TargetSurrogate ?? "-", a.TargetZone ?? "-",
                a.ChoiceOption ?? "-", a.OrderedSurrogates == null ? "-" : string.Join(">", a.OrderedSurrogates),
                a.GoingFirst?.ToString() ?? "-", a.Mulligan?.ToString() ?? "-", a.Amount?.ToString() ?? "-",
                a.ActorCardId ?? "-", a.TargetCardId ?? "-");

        private static HashSet<string> AllInstanceIds(GameState st)
        {
            var ids = new HashSet<string>();
            void Add(CardInstance c) { if (c?.InstanceId != null) ids.Add(c.InstanceId); }
            foreach (var p in st.Players.Values)
            {
                Add(p.Leader); Add(p.Stage);
                foreach (var z in new[] { p.Deck, p.Hand, p.Life, p.Trash }) foreach (var c in z) Add(c);
                foreach (var c in p.CharacterArea) Add(c);
            }
            if (st.Battle?.RevealedLife != null) Add(st.Battle.RevealedLife);
            return ids;
        }

        private static List<object> ReachableObjects(object root, int maxDepth = 16)
        {
            var found = new List<object>();
            var seen = new HashSet<object>(new RefEq());
            void Walk(object o, int depth)
            {
                if (o == null || depth > maxDepth) return;
                if (o is string) { found.Add(o); return; }
                var t = o.GetType();
                if (t.IsPrimitive || t.IsEnum || o is decimal || o is DateTime || o is TimeSpan) return;
                if (!seen.Add(o)) return;
                found.Add(o);
                if (o is IDictionary dict) { foreach (DictionaryEntry e in dict) { Walk(e.Key, depth + 1); Walk(e.Value, depth + 1); } return; }
                if (o is IEnumerable seq) { foreach (var it in seq) Walk(it, depth + 1); return; }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) Walk(Safe(() => f.GetValue(o)), depth + 1);
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.GetIndexParameters().Length == 0 && p.CanRead) Walk(Safe(() => p.GetValue(o)), depth + 1);
            }
            Walk(root, 0);
            return found;
        }

        private static object Safe(Func<object> f) { try { return f(); } catch { return null; } }

        private sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
