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
    /// TASK 11 — deterministic per-boundary fixtures. Reachable boundaries are found by driving REAL games
    /// deterministically (deck pair + seed pinned), so each is an engine-VALID state, not a synthetic guess.
    /// For each, the SAME state is used as both the referee (to build descriptors through the seam) and the
    /// test world (to reconstruct the command through the private links), so "reconstructed == authoritative"
    /// is an exact command-shape proof; the command is then applied and its transition checked. Every boundary
    /// is additionally checked for action-set paired-world noninterference; private decisions are checked for
    /// non-entitled secrecy; and poison over prompt / ordering / source / ids is checked unreachable.
    /// </summary>
    public static class BoundaryFixtureTest
    {
        private const string P = "P0IS0N";

        public static int Run(IReadOnlyList<DeckDef> pool)
        {
            Console.WriteLine("=== task 11: deterministic boundary fixtures ===");
            var fixtures = BuildFixtures(pool);
            Console.WriteLine($"  reached boundaries: {string.Join(", ", fixtures.Select(f => f.name))}");

            int fail = 0;
            fail += Section("translator command-shape + missing-field fail-closed (unit)", TranslatorUnit());
            fail += Section("round-trip fidelity through the seam (every reached boundary)", RoundTrip(pool, fixtures));
            fail += Section("paired-world action-set noninterference (every reached boundary)", PairedWorld(pool, fixtures));
            fail += Section("non-entitled seat cannot see choice/deckLook/pending", NonEntitled(pool, fixtures));
            fail += Section("poison prompt/ordering/source unreachable for the non-entitled viewer", PoisonNonEntitled(pool, fixtures));
            fail += Section("hand/trash duplicate CardIds distinguishable + zone-correct", DuplicateHandTrash(pool));

            // Every boundary the reviewer named must actually be present, or the suite is incomplete.
            var need = new[] { "coinflip", "mulligan", "main", "declareAttack", "block", "counter", "trigger", "choice", "deckLook", "pending" };
            var missing = need.Where(n => fixtures.All(f => f.name != n)).ToList();
            if (missing.Count > 0) { Console.WriteLine($"  [FAIL] boundaries never reached in the pool: {string.Join(", ", missing)}"); fail++; }

            Console.WriteLine();
            Console.WriteLine(fail == 0 ? "PASS - every boundary fixture holds." : $"FAIL - {fail} boundary check(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        private static int Section(string name, (bool ok, string detail) r)
        {
            Console.WriteLine($"  [{(r.ok ? "ok  " : "FAIL")}] {name}{(r.detail == null ? "" : " - " + r.detail)}");
            return r.ok ? 0 : 1;
        }

        // =========================================================== fixture construction (deterministic)

        private sealed class Fixture { public string name; public GameState st; public string seat; }

        /// <summary>Drive real games deterministically to reach each boundary. The always-frequent ones use
        /// the first deck pair; choice and deck look are searched across the pool (they need specific card
        /// effects) with a bounded, deterministic pair/seed sweep.</summary>
        private static List<Fixture> BuildFixtures(IReadOnlyList<DeckDef> pool)
        {
            var a = pool[0]; var b = pool[1];
            var list = new List<Fixture>();

            void Add(string name, GameState st, string seat) { if (st != null) list.Add(new Fixture { name = name, st = st, seat = seat }); }

            var cf = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = a, NorthDeckDef = b, Seed = "t11:coinflip" });
            if (cf.Status == "coinflip") Add("coinflip", cf, cf.CoinFlipWinner);

            Add("mulligan", Reach(a, b, "mul", st => st.Status == "mulligan" && !st.Players["south"].MulliganDecided), "south");
            Add("main", Reach(a, b, "main", st => CleanMain(st, "south") && Has(st, "south", "playCard")), "south");
            Add("declareAttack", Reach(a, b, "atk", st => CleanMain(st, "south") && Has(st, "south", "declareAttack")), "south");
            Add("block", ReachBattle(a, b, "block"), null);
            Add("counter", ReachBattle(a, b, "counter"), null);
            Add("trigger", ReachBattle(a, b, "trigger"), null);
            foreach (var f in list.Where(f => f.seat == null).ToList()) f.seat = f.st.Battle.TargetSeat;

            // Choice / deck look / pending: search the pool (they depend on specific effects). Pending is
            // common; choice/deckLook are rarer, so allow a wider sweep.
            var pend = FindInPool(pool, st => st.PendingEffects.Count > 0, out var ps, 60);
            Add("pending", pend, ps);
            var choice = FindInPool(pool, st => st.ActiveChoice != null, out var cs, 400);
            Add("choice", choice, cs);
            var dl = FindInPool(pool, st => st.DeckLook != null, out var dls, 400);
            Add("deckLook", dl, dls);
            return list;
        }

        private static GameState Reach(DeckDef a, DeckDef b, string tag, Func<GameState, bool> hit)
        {
            for (int t = 0; t < 12; t++) { var st = PrivacyTest.ReachFirst(a, b, $"t11:{tag}:{t}", hit, 800); if (st != null) return st; }
            return null;
        }

        private static GameState ReachBattle(DeckDef a, DeckDef b, string step)
            => Reach(a, b, "b:" + step, st => st.Battle != null && st.Battle.Step == step);

        /// <summary>Deterministic sweep over deck pairs + seeds for a boundary that needs specific effects.
        /// Bounded by <paramref name="maxAttempts"/>; the iteration order is fixed, so the result is stable.</summary>
        private static GameState FindInPool(IReadOnlyList<DeckDef> pool, Func<GameState, bool> hit, out string seat, int maxAttempts)
        {
            seat = null;
            int attempts = 0;
            for (int i = 0; i < pool.Count; i++)
                for (int j = 0; j < pool.Count; j++)
                {
                    if (i == j) continue;
                    for (int s = 0; s < 2; s++)
                    {
                        if (attempts++ >= maxAttempts) return null;
                        var st = PrivacyTest.ReachFirst(pool[i], pool[j], $"t11:pool:{i}:{j}:{s}", hit, 700);
                        if (st != null)
                        {
                            seat = st.ActiveChoice?.Seat ?? st.DeckLook?.Seat
                                 ?? (st.PendingEffects.Count > 0 ? st.PendingEffects[0].Seat : null) ?? "south";
                            return st;
                        }
                    }
                }
            return null;
        }

        // Decks for a given seat within a fixture — the fixture may come from any pool pair, so resolve the
        // acting seat's own list by matching the leader; fall back to the pool defaults. OpenList knowledge.
        private static KnowledgeState Know(GameState st, string seat, IReadOnlyList<DeckDef> pool)
        {
            string leader = st.Players[seat].Leader?.CardId;
            string oppLeader = st.Players[Other(seat)].Leader?.CardId;
            DeckDef own = pool.FirstOrDefault(d => d.Leader == leader) ?? pool[0];
            DeckDef opp = pool.FirstOrDefault(d => d.Leader == oppLeader) ?? pool[1];
            return new KnowledgeState { Seat = seat, OwnList = own, OpponentList = opp, Assumption = ListAssumption.OpenList };
        }

        // =========================================================== (1) translator unit

        private static (bool, string) TranslatorUnit()
        {
            var sur = new Dictionary<string, string> { ["s#att"] = "ATK-1", ["s#tgt"] = "TGT-1", ["s#h"] = "HAND-1", ["s#o1"] = "O-1", ["s#o2"] = "O-2" };
            var pend = new Dictionary<string, string> { ["p#0"] = "EFF-1" };
            GameCommand T(ObservedAction a) => RootActionTranslator.ToWorldCommand(a, "south", sur, pend);

            // Correct shapes.
            var atk = T(new ObservedAction { Type = "declareAttack", ActorSurrogate = "s#att", TargetSurrogate = "s#tgt" });
            if (atk == null || atk.Attacker != "ATK-1" || atk.InstanceId != null || atk.Target != "TGT-1") return (false, "declareAttack");
            var re = T(new ObservedAction { Type = "resolveEffect", PendingRef = "p#0", TargetSurrogate = "s#h" });
            if (re == null || re.EffectId != "EFF-1" || re.Target != "HAND-1") return (false, "resolveEffect");
            var pe = T(new ObservedAction { Type = "passEffect", PendingRef = "p#0" });
            if (pe == null || pe.EffectId != "EFF-1") return (false, "passEffect");
            if (T(new ObservedAction { Type = "blockAttack", ActorSurrogate = "s#h" })?.Blocker != "HAND-1") return (false, "blockAttack");
            if (T(new ObservedAction { Type = "playCard", ActorSurrogate = "s#h" })?.InstanceId != "HAND-1") return (false, "playCard");
            if (T(new ObservedAction { Type = "activateMain", ActorSurrogate = "s#h" })?.Target != "HAND-1") return (false, "activateMain");
            var ad = T(new ObservedAction { Type = "attachDon", TargetSurrogate = "s#tgt", Amount = 1 });
            if (ad == null || ad.Target != "TGT-1" || ad.Amount != 1) return (false, "attachDon");
            if (T(new ObservedAction { Type = "resolveChoice", ChoiceOption = "B" })?.Target != "B") return (false, "resolveChoice");
            var ord = T(new ObservedAction { Type = "deckLookConfirmOrder", OrderedSurrogates = new[] { "s#o1", "s#o2" } });
            if (ord == null || !ord.OrderedInstanceIds.SequenceEqual(new[] { "O-1", "O-2" })) return (false, "deckLookConfirmOrder");
            if (T(new ObservedAction { Type = "deckLookScryConfirm", OrderedSurrogates = new[] { "s#o2", "s#o1" } })?.OrderedInstanceIds?.SequenceEqual(new[] { "O-2", "O-1" }) != true) return (false, "scry");
            if (T(new ObservedAction { Type = "deckLookSelect", TargetSurrogate = "s#o1" })?.Target != "O-1") return (false, "deckLookSelect card");
            if (T(new ObservedAction { Type = "chooseTurnOrder", GoingFirst = true })?.GoingFirst != true) return (false, "chooseTurnOrder");
            if (T(new ObservedAction { Type = "mulliganDecision", Mulligan = false })?.Mulligan != false) return (false, "mulligan");

            // Intentional empty targets: deck-look decline and empty effect selection ⇒ Target "".
            if (T(new ObservedAction { Type = "deckLookSelect" })?.Target != "") return (false, "decline should be Target \"\"");
            if (T(new ObservedAction { Type = "resolveEffect", PendingRef = "p#0" })?.Target != "") return (false, "empty effect selection should be Target \"\"");

            // MISSING required fields ⇒ fail closed (null).
            var miss = new (string label, ObservedAction a)[]
            {
                ("declareAttack no actor", new ObservedAction { Type = "declareAttack", TargetSurrogate = "s#tgt" }),
                ("declareAttack no target", new ObservedAction { Type = "declareAttack", ActorSurrogate = "s#att" }),
                ("blockAttack no actor", new ObservedAction { Type = "blockAttack" }),
                ("playCard no actor", new ObservedAction { Type = "playCard" }),
                ("counterWithCard no actor", new ObservedAction { Type = "counterWithCard" }),
                ("attachDon no target", new ObservedAction { Type = "attachDon", Amount = 1 }),
                ("attachDon no amount", new ObservedAction { Type = "attachDon", TargetSurrogate = "s#tgt" }),
                ("resolveChoice no option", new ObservedAction { Type = "resolveChoice" }),
                ("order no list", new ObservedAction { Type = "deckLookConfirmOrder" }),
                ("scry no list", new ObservedAction { Type = "deckLookScryConfirm" }),
                ("resolveEffect no ref", new ObservedAction { Type = "resolveEffect" }),
                ("passEffect no ref", new ObservedAction { Type = "passEffect" }),
                ("chooseTurnOrder no choice", new ObservedAction { Type = "chooseTurnOrder" }),
                ("mulligan no choice", new ObservedAction { Type = "mulliganDecision" }),
                ("unresolved actor", new ObservedAction { Type = "playCard", ActorSurrogate = "s#MISSING" }),
                ("unresolved ordered", new ObservedAction { Type = "deckLookConfirmOrder", OrderedSurrogates = new[] { "s#MISSING" } }),
                ("unknown type", new ObservedAction { Type = "nonsense" }),
            };
            foreach (var (label, a) in miss)
                if (T(a) != null) return (false, $"'{label}' did not fail closed");
            return (true, "all command shapes exact; every missing required field fails closed");
        }

        // =========================================================== (2) round-trip through the seam

        private static (bool, string) RoundTrip(IReadOnlyList<DeckDef> pool, List<Fixture> fixtures)
        {
            int actions = 0;
            foreach (var f in fixtures)
            {
                var k = Know(f.st, f.seat, pool);
                Projection.Project(f.st, k, out var links);                 // deterministic ⇒ same surrogates as the seam
                var decision = ObservedSeam.Build(f.st, f.seat, k, "t11", 0, 0);
                foreach (var a in decision.Context.Actions)
                {
                    var authoritative = decision.Translate(a);
                    var reconstructed = RootActionTranslator.ToWorldCommand(a, f.seat, links.SurrogateToInstanceId, links.PendingRefToEffectId);
                    if (reconstructed == null) return (false, $"{f.name}/{a.Type}: reconstruction failed closed on a legal action");
                    if (!CommandsMatch(authoritative, reconstructed, out var why)) return (false, $"{f.name}/{a.Type}: reconstructed != authoritative ({why})");
                    if (a.Type == "mulliganDecision" || a.Type == "chooseTurnOrder") { actions++; continue; }  // fingerprint-neutral
                    var clone = GameClone.Clone(f.st);
                    long before = LegalActions.StateFingerprint(clone);
                    GameEngine.ApplyCommand(clone, reconstructed);
                    if (LegalActions.StateFingerprint(clone) == before) return (false, $"{f.name}/{a.Type}: reconstructed command did not change the world");
                    actions++;
                }
            }
            return (true, $"{actions} actions across {fixtures.Count} boundaries round-tripped exactly and applied");
        }

        // =========================================================== (3) paired-world noninterference

        private static (bool, string) PairedWorld(IReadOnlyList<DeckDef> pool, List<Fixture> fixtures)
        {
            foreach (var f in fixtures)
            {
                if (f.name == "coinflip" || f.name == "mulligan") continue;   // no hidden opponent state yet to permute
                var k = Know(f.st, f.seat, pool);
                var twin = GameClone.Clone(f.st);
                PermuteHidden(twin, f.seat, 7777);                            // permute ONLY what is hidden from the acting seat
                var da = ObservedSeam.Build(f.st, f.seat, k, "t11", 0, 0);
                var db = ObservedSeam.Build(twin, f.seat, k, "t11", 0, 0);
                var ka = da.Context.Actions.Select(Descriptor).ToList();
                var kb = db.Context.Actions.Select(Descriptor).ToList();
                if (!ka.SequenceEqual(kb)) return (false, $"{f.name}: action set/order changed under a hidden-only permutation");
            }
            return (true, $"identical action sets at {fixtures.Count(f => f.name != "coinflip" && f.name != "mulligan")} boundaries under hidden permutation");
        }

        // =========================================================== (4) non-entitled secrecy

        private static (bool, string) NonEntitled(IReadOnlyList<DeckDef> pool, List<Fixture> fixtures)
        {
            foreach (var name in new[] { "choice", "deckLook", "pending" })
            {
                var f = fixtures.FirstOrDefault(x => x.name == name);
                if (f == null) continue;
                string opp = Other(f.seat);
                var oppObs = Projection.Project(f.st, Know(f.st, opp, pool));
                if (name == "choice" && oppObs.Choice != null) return (false, "opponent saw the actor's choice");
                if (name == "deckLook" && oppObs.DeckLook != null) return (false, "opponent saw the actor's deck look");
                if (name == "pending" && oppObs.Pending.Count != 0) return (false, "opponent saw the actor's pending effect");
                var ownObs = Projection.Project(f.st, Know(f.st, f.seat, pool));
                bool ownHas = name == "choice" ? ownObs.Choice != null : name == "deckLook" ? ownObs.DeckLook != null : ownObs.Pending.Count != 0;
                if (!ownHas) return (false, $"actor cannot see its own {name}");
            }
            return (true, "each private decision visible to its actor, absent for the opponent");
        }

        // =========================================================== (5) poison for the non-entitled viewer

        private static (bool, string) PoisonNonEntitled(IReadOnlyList<DeckDef> pool, List<Fixture> fixtures)
        {
            bool did = false;
            foreach (var name in new[] { "deckLook", "pending", "choice" })
            {
                var f = fixtures.FirstOrDefault(x => x.name == name);
                if (f == null) continue;
                did = true;
                var st = GameClone.Clone(f.st);
                // Poison the PRIVATE prompt/source text and the hidden ORDERING — the acting seat may see some
                // of these, but the OPPONENT must not.
                if (st.DeckLook != null) { st.DeckLook.SourceInstanceId = P + "-DLSRC"; st.DeckLook.SourceName = P + "-DLNAME"; }
                foreach (var e in st.PendingEffects.Where(e => e.Seat == f.seat)) { e.Text = P + "-PROMPT"; e.SourceInstanceId = P + "-EFFSRC"; }
                if (st.ActiveChoice != null && st.ActiveChoice.Seat == f.seat) { st.ActiveChoice.OptionA = P + "-OPTA"; st.ActiveChoice.OptionB = P + "-OPTB"; }
                // A genuinely private log line for the ACTOR only.
                st.EventLog.Add(new LogEntry { Actor = f.seat, PrivateSeat = f.seat, Message = P + "-PRIVLOG", PublicMessage = "did something" });

                string opp = Other(f.seat);
                var oppCtx = ObservedSeam.Build(st, opp, Know(st, opp, pool), "t11", 0, 0);
                var leaked = ReachableStrings(oppCtx.Context).FirstOrDefault(s => s != null && s.Contains(P));
                if (leaked != null) return (false, $"{name}: poison '{leaked}' reachable from the OPPONENT's context");

                // Equality across public-equivalent worlds: permute hidden, the opponent's view is unchanged.
                var twin = GameClone.Clone(st); PermuteHidden(twin, opp, 4242);
                var twinCtx = ObservedSeam.Build(twin, opp, Know(twin, opp, pool), "t11", 0, 0);
                if (!ReachableStrings(oppCtx.Context).Where(s => s != null).OrderBy(x => x).SequenceEqual(
                        ReachableStrings(twinCtx.Context).Where(s => s != null).OrderBy(x => x)))
                    return (false, $"{name}: opponent's observation strings differ across a hidden-only permutation");
            }
            return did ? (true, "opponent sees no private prompt/ordering/source; view stable across hidden permutation")
                       : (false, "no private-decision fixture to poison");
        }

        // =========================================================== (6) hand/trash duplicates

        private static (bool, string) DuplicateHandTrash(IReadOnlyList<DeckDef> pool)
        {
            var st = PrivacyTest.SouthDecision(pool[0], pool[1], "t11:dup", 40);
            if (st == null) return (false, "no base state");
            var p = st.Players["south"];
            if (p.Hand.Count == 0) return (false, "empty hand");
            string dup = p.Hand[0].CardId;
            p.Hand.Insert(0, new CardInstance { InstanceId = "DUP-HAND-A", CardId = dup, Owner = "south", Zone = "hand" });
            p.Trash.Insert(0, new CardInstance { InstanceId = "DUP-TRASH", CardId = dup, Owner = "south", Zone = "trash" });

            var obs = Projection.Project(st, new KnowledgeState { Seat = "south", OwnList = pool[0], Assumption = ListAssumption.UnknownList }, out var links);
            var handSur = obs.Zones.First(z => z.Owner == "south" && z.Zone == "hand").Cards.First(c => c.CardId == dup).SurrogateId;
            var trashSur = obs.Zones.First(z => z.Owner == "south" && z.Zone == "trash").Cards.First(c => c.CardId == dup).SurrogateId;
            if (handSur == trashSur) return (false, "hand and trash copies share a surrogate");
            if (links.SurrogateToInstanceId[handSur] != "DUP-HAND-A") return (false, "hand surrogate did not resolve to the hand instance");
            if (links.SurrogateToInstanceId[trashSur] != "DUP-TRASH") return (false, "trash surrogate did not resolve to the trash instance");
            var handZone = obs.Zones.First(z => z.Owner == "south" && z.Zone == "hand");
            if (handZone.Cards.Where(c => c.CardId == dup).Select(c => c.SurrogateId).Distinct().Count() != handZone.Cards.Count(c => c.CardId == dup))
                return (false, "duplicate hand copies share a surrogate");
            return (true, "same-CardId hand/trash copies keep distinct surrogates and resolve to the correct zone");
        }

        // =========================================================== helpers

        private static bool CleanMain(GameState st, string seat) =>
            st.Status != "finished" && st.ActiveSeat == seat && st.Phase == "main" && st.Battle == null
            && st.PendingEffects.Count == 0 && st.ActiveChoice == null && st.DeckLook == null;

        private static bool Has(GameState st, string seat, string type) => LegalActions.Candidates(st, seat).Any(c => c.Type == type);
        private static string Other(string seat) => seat == "south" ? "north" : "south";

        private static bool CommandsMatch(GameCommand a, GameCommand b, out string why)
        {
            why = null;
            if (a == null || b == null) { why = "null"; return false; }
            string F(string x, string y, string f) => x == y ? null : $"{f} {x}≠{y}";
            foreach (var d in new[]
            {
                F(a.Type, b.Type, "Type"), F(a.Seat, b.Seat, "Seat"), F(a.InstanceId, b.InstanceId, "InstanceId"),
                F(a.Target, b.Target, "Target"), F(a.Attacker, b.Attacker, "Attacker"), F(a.Blocker, b.Blocker, "Blocker"),
                F(a.EffectId, b.EffectId, "EffectId"),
            })
                if (d != null) { why = d; return false; }
            if (a.Amount != b.Amount) { why = "Amount"; return false; }
            if (a.GoingFirst != b.GoingFirst) { why = "GoingFirst"; return false; }
            if (a.Mulligan != b.Mulligan) { why = "Mulligan"; return false; }
            var ao = a.OrderedInstanceIds; var bo = b.OrderedInstanceIds;
            if ((ao == null) != (bo == null) || (ao != null && !ao.SequenceEqual(bo))) { why = "OrderedInstanceIds"; return false; }
            return true;
        }

        private static string Descriptor(ObservedAction a) =>
            string.Join("|", a.Type, a.ActorSurrogate ?? "-", a.TargetSurrogate ?? "-", a.TargetZone ?? "-",
                a.ChoiceOption ?? "-", a.PendingRef ?? "-",
                a.OrderedSurrogates == null ? "-" : string.Join(">", a.OrderedSurrogates),
                a.GoingFirst?.ToString() ?? "-", a.Mulligan?.ToString() ?? "-", a.Amount?.ToString() ?? "-");

        private static void PermuteHidden(GameState s, string seat, int seed)
        {
            var rng = new Random(seed);
            foreach (var kv in s.Players)
            {
                var pool = new List<CardInstance>();
                pool.AddRange(kv.Value.Deck);
                pool.AddRange(kv.Value.Life.Where(c => !c.FaceUp));
                if (kv.Key != seat) pool.AddRange(kv.Value.Hand);   // the acting seat's OWN hand is not hidden from it
                if (pool.Count < 2) continue;
                var ids = pool.Select(c => c.CardId).ToList();
                for (int i = ids.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (ids[i], ids[j]) = (ids[j], ids[i]); }
                for (int i = 0; i < pool.Count; i++) pool[i].CardId = ids[i];
            }
        }

        private static List<string> ReachableStrings(object root, int maxDepth = 16)
        {
            var found = new List<string>();
            var seen = new HashSet<object>(new RefEq());
            void Walk(object o, int depth)
            {
                if (o == null || depth > maxDepth) return;
                if (o is string s) { found.Add(s); return; }
                var t = o.GetType();
                if (t.IsPrimitive || t.IsEnum || o is decimal || o is DateTime || o is TimeSpan) return;
                if (!seen.Add(o)) return;
                if (o is IDictionary dict) { foreach (DictionaryEntry e in dict) { Walk(e.Key, depth + 1); Walk(e.Value, depth + 1); } return; }
                if (o is IEnumerable seq) { foreach (var it in seq) Walk(it, depth + 1); return; }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) Walk(Safe(() => f.GetValue(o)), depth + 1);
                foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (pr.GetIndexParameters().Length == 0 && pr.CanRead) Walk(Safe(() => pr.GetValue(o)), depth + 1);
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
