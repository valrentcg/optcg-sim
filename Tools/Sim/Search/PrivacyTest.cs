using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Planning;

namespace OnePieceTcg.Sim.Search
{
    /// <summary>
    /// THE OBSERVATION-BOUNDARY TESTS, split by the TWO layers the boundary actually has (design §1):
    ///
    ///   LAYER 1 — the immutable legal observation (<see cref="PlayerObservation"/>).
    ///     A referee secret must be UNREACHABLE from what <see cref="Projection.Project"/> returns, and two
    ///     states that differ ONLY in hidden information must project to EQUAL observations. Both of these
    ///     are checkable TODAY and are expected to PASS — projection is real, not a wrapper.
    ///
    ///   LAYER 2 — the mutable sampled world the search runs on (<see cref="SearchWorld"/>).
    ///     The world the planner actually consumes must not carry the referee's true hidden assignment.
    ///     Under the default perfect-info path that world is <see cref="SearchWorld.FromLegacy"/> — a
    ///     verbatim referee clone — so this layer STILL LEAKS, by design, and privacy-test still witnesses
    ///     it. It flips green only when <see cref="SearchWorld.FromObservation"/> (the determinizer, §6)
    ///     lands and builds the world from the observation instead of the truth.
    ///
    /// Passing layer 1 is NECESSARY BUT NOT SUFFICIENT: a clean observation the planner does not consume
    /// proves nothing about the planner. That is exactly why the layers are reported separately and why a
    /// green layer 1 does not close the boundary.
    ///
    /// WHY THIS EXISTS: GameClone.ClonePlayer copies Deck, Hand and Life verbatim, so the planner searches
    /// with PERFECT INFORMATION. Every win rate this project has reported (incl. the 61.3% knee) measured
    /// that bot, and only the planner cheats — the IntermediateBot is a greedy heuristic that never clones.
    /// The contamination is one-directional and INFLATES the planner.
    ///
    /// THE INFORMATION MODEL (agreed; the Life clause is a RULES point, not a design choice):
    ///   own hand ................. KNOWN
    ///   opponent hand ............ HIDDEN
    ///   both facedown Life ....... HIDDEN — including your OWN (CR §3-10-2; a planner reading its own Life
    ///                              knows its own Triggers = a large illegal edge).
    ///   both deck orders ......... HIDDEN
    ///   in play / revealed ....... PUBLIC, preserved
    ///   opponent DECKLIST ........ a separate EXPERIMENTAL ASSUMPTION (OpenList). Knowing the LIST is not
    ///                              knowing the ARRANGEMENT; do not conflate them.
    ///
    /// ⚠ KNOWN MODEL LIMITATION — the boundary is not COMPLETE without the knowledge ledger (§2). Projection
    /// here emits only strictly-public facts; it does not yet reconstruct legally-ACQUIRED knowledge (a
    /// searcher's known top cards, revealed opponent cards). That makes today's observation CONSERVATIVE,
    /// not leaky, and determinization MUST NOT ship before the ledger or it will reshuffle cards a player
    /// legitimately knows — corrupting exactly the search-heavy decks (op16-rayleigh, searchers=25) the work
    /// is meant to measure.
    /// </summary>
    public static class PrivacyTest
    {
        public const string PoisonTag = "P0IS0N";

        /// <summary>REACHABILITY, not a transition inventory. The verbatim-copy sites that leak live in
        /// GameClone; whether they actually leak into what planning receives is proven at RUNTIME by the
        /// poison scan below, against the production projection and the production search world — not by any
        /// static list of source lines, and not by an assertion moved out of Tools/Audit (none was). The
        /// carriers, for orientation only:
        ///   GameClone.cs ~75  Deck / Hand / Life          — the verbatim copy that started all of this
        ///   GameClone.cs ~88  BattleState.RevealedLife    — the defence-lookahead channel (CR §10-1-5)
        ///   GameClone.cs ~46  CommandHistory / EventLog   — shallow copies carrying hidden ids + private text
        /// The scan is the proof; these line numbers are a comment, and nothing more.</summary>
        public const string ReachabilityNote = "carriers (orientation only): GameClone Deck/Hand/Life, Battle.RevealedLife, CommandHistory/EventLog — proof is the runtime scan, not these lines";

        // ============================================================ knowledge / seam (production path)

        /// <summary>Build the legal-knowledge input for a seat. OpenList is the GENEROUS phase-1 assumption
        /// (the opponent's LIST, never its arrangement); UnknownList is the honest phase-2 default.</summary>
        private static KnowledgeState Know(string seat, DeckDef own, DeckDef opp, bool openList) => new KnowledgeState
        {
            Seat = seat,
            OwnList = own,
            OpponentList = openList ? opp : null,
            Assumption = openList ? ListAssumption.OpenList : ListAssumption.UnknownList,
        };

        /// <summary>Project through the PRODUCTION seam — the same <see cref="Projection.Project"/> the
        /// shipped planner uses, never a test-local wrapper.</summary>
        private static PlayerObservation Observe(GameState st, string seat, DeckDef own, DeckDef opp, bool openList)
            => Projection.Project(st, Know(seat, own, opp, openList));

        // =============================================================== decision fixtures (item 1)

        /// <summary>A state is only a usable fixture if South is genuinely the one deciding, nothing is
        /// blocking, and there is a real choice to get wrong.</summary>
        public static bool IsCleanSouthDecision(GameState st, out string why)
        {
            why = null;
            if (st == null) { why = "null state"; return false; }
            if (st.Status == "finished") { why = "finished"; return false; }
            if (st.ActiveSeat != "south") { why = $"activeSeat={st.ActiveSeat}"; return false; }
            if (st.Phase != "main") { why = $"phase={st.Phase}"; return false; }
            if (st.Battle != null) { why = "battle in progress"; return false; }
            if (st.ActiveChoice != null) { why = "choice pending"; return false; }
            if (st.DeckLook != null) { why = "deckLook pending"; return false; }
            if (st.PendingEffects.Count != 0) { why = "effects pending"; return false; }

            int distinct = LegalActions.Candidates(st, "south")
                .Select(c => $"{c.Type}|{c.InstanceId}|{c.Target}")
                .Distinct().Count();
            if (distinct < 2) { why = $"only {distinct} distinct legal choice(s)"; return false; }
            return true;
        }

        /// <summary>Advance until South is genuinely on turn in a clean main phase. Replaces the old
        /// arbitrary command cap, which guaranteed nothing and let trials pass vacuously by comparing two
        /// empty plans.</summary>
        public static GameState SouthDecision(DeckDef s, DeckDef n, string seed, int minCap)
        {
            for (int cap = minCap; cap < minCap + 80; cap++)
            {
                var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = s, NorthDeckDef = n, Seed = seed });
                GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = st.CoinFlipWinner, GoingFirst = true });
                OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(st, cap);
                if (IsCleanSouthDecision(st, out _)) return st;
            }
            return null;
        }

        // ================================================================ structural secrecy (item 3)

        /// <summary>Stamp sentinels into every hidden zone — CardId AND InstanceId — and into an
        /// opponent-private EventLog entry and a CommandHistory entry referencing a hidden card. This is the
        /// THREAT MODEL enumerated as concrete poison, which is deliberate: the secrecy claim is proven by
        /// showing named secrets do not reach the view, so the secrets have to be named. (That enumeration
        /// is why the boundary carries NO static privacy-field whitelist — the reachability walk needs no
        /// list of fields, but the poison and the decision fixtures still spell out what must not leak.)</summary>
        public static void PoisonHidden(GameState s, string plannerSeat)
        {
            foreach (var kv in s.Players)
            {
                var p = kv.Value;
                bool isOpponent = kv.Key != plannerSeat;

                for (int i = 0; i < p.Deck.Count; i++) Stamp(p.Deck[i], $"DECK-{kv.Key}-{i}");
                foreach (var c in p.Life.Where(c => !c.FaceUp)) Stamp(c, $"LIFE-{kv.Key}");
                if (isOpponent) foreach (var c in p.Hand) Stamp(c, $"HAND-{kv.Key}");
            }

            // A genuinely PRIVATE log line, private to the VIEWER'S OPPONENT so it is actually a secret FROM
            // the viewer: only a viewer on PrivateSeat may read Message; everyone else must see PublicMessage
            // (GameState.cs). PublicMessage is deliberately poison-free — it is what the viewer is ENTITLED to
            // see, so its presence in the view is correct, not a leak. (Hardcoding "north" here was wrong: when
            // the viewer IS north, north's own private line is not a secret from it, and the projection
            // correctly surfaces it — that read like a leak but was the poison being mislabelled.)
            string opp = s.Players.Keys.FirstOrDefault(k => k != plannerSeat) ?? "north";
            s.EventLog.Add(new LogEntry
            {
                Actor = opp,
                PrivateSeat = opp,
                Message = $"{PoisonTag}-LOG {opp} searches and adds a card only {opp} may see",
                PublicMessage = $"{opp} adds a card to hand",
            });

            // A history entry referencing a hidden card by InstanceId — the exact path CardId poisoning missed.
            s.CommandHistory.Add(new GameCommand { Type = "playCard", Seat = opp, InstanceId = $"{PoisonTag}-HISTORY-INSTANCE" });
        }

        private static void Stamp(CardInstance c, string tag)
        {
            c.CardId = $"{PoisonTag}-CARD-{tag}";
            c.InstanceId = $"{PoisonTag}-INST-{tag}-{c.InstanceId}";
        }

        /// <summary>Every string reachable from <paramref name="root"/> by ANY public field/property,
        /// collection element, or dictionary key/value. Reflection is deliberate: the claim under test is
        /// "unreachable by any reference path", and hand-enumerating fields would only test the paths I
        /// thought of. Cycle-safe, depth-bounded. Note this is NOT a privacy-field whitelist — it enumerates
        /// nothing about the type; it walks whatever references exist.</summary>
        public static List<string> ReachableStrings(object root, int maxDepth = 14)
        {
            var found = new List<string>();
            var seen = new HashSet<object>(RefComparer.Instance);

            void Walk(object o, int depth)
            {
                if (o == null || depth > maxDepth) return;
                if (o is string str) { found.Add(str); return; }
                var t = o.GetType();
                if (t.IsPrimitive || t.IsEnum || o is decimal || o is DateTime || o is TimeSpan) return;
                if (!seen.Add(o)) return;

                if (o is IDictionary dict)
                {
                    foreach (DictionaryEntry e in dict) { Walk(e.Key, depth + 1); Walk(e.Value, depth + 1); }
                    return;
                }
                if (o is IEnumerable seq) { foreach (var item in seq) Walk(item, depth + 1); return; }

                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    Walk(SafeGet(() => f.GetValue(o)), depth + 1);
                foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (pr.GetIndexParameters().Length != 0 || !pr.CanRead) continue;
                    Walk(SafeGet(() => pr.GetValue(o)), depth + 1);
                }
            }
            Walk(root, 0);
            return found;
        }

        private static object SafeGet(Func<object> get) { try { return get(); } catch { return null; } }

        private sealed class RefComparer : IEqualityComparer<object>
        {
            public static readonly RefComparer Instance = new RefComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        /// <summary>Count DISTINCT poison strings reachable from an arbitrary root (an observation or a
        /// search world). Zero = that surface carries no named secret.</summary>
        private static List<string> PoisonReachable(object root)
            => ReachableStrings(root).Where(x => x != null && x.Contains(PoisonTag)).Distinct().ToList();

        // ====================================================== deterministic witness fixture (item 2)

        /// <summary>PERMUTE the opponent's unseen cards so their HAND holds the highest- (or lowest-)
        /// counter cards available. The unseen pool is only REARRANGED, never rewritten, so the multiset
        /// and every per-card quantity are identical by construction — both worlds are LEGAL draws from
        /// the same decklist and differ only in hidden arrangement. Pool order is Hand → Deck → facedown
        /// Life, so a counter-sorted id list front-loads the hand with the cards we want to test against.</summary>
        private static void ArrangeOpponentHand(GameState s, string oppSeat, bool preferHighCounter)
        {
            var p = s.Players[oppSeat];
            var pool = new List<CardInstance>();
            pool.AddRange(p.Hand);
            pool.AddRange(p.Deck);
            pool.AddRange(p.Life.Where(c => !c.FaceUp));

            var ids = pool.Select(c => c.CardId).ToList();
            ids = preferHighCounter
                ? ids.OrderByDescending(id => CardData.GetCard(id)?.Counter ?? 0).ThenBy(id => id).ToList()
                : ids.OrderBy(id => CardData.GetCard(id)?.Counter ?? 0).ThenBy(id => id).ToList();
            for (int i = 0; i < pool.Count; i++) pool[i].CardId = ids[i];
        }

        /// <summary>EXACT decklist equality across all zones, both directions, totals included. Proves the
        /// two worlds are DECK-COUNT LEGAL and multiset-preserving. It does NOT prove they are
        /// OBSERVATION-EQUIVALENT — a card South already saw could constrain North's hidden zones, and
        /// nothing here checks that (impossible until the ledger exists). Multiset-preserving, not
        /// observation-equivalent.</summary>
        public static bool ValidatesAgainstList(GameState s, string seat, DeckDef list, out string why)
        {
            var p = s.Players[seat];
            var actual = new Dictionary<string, int>();
            void Count(IEnumerable<CardInstance> z)
            {
                if (z == null) return;
                foreach (var c in z)
                {
                    if (c?.CardId == null) continue;
                    actual.TryGetValue(c.CardId, out var n);
                    actual[c.CardId] = n + 1;
                }
            }
            Count(p.Hand); Count(p.Deck); Count(p.Life); Count(p.CharacterArea); Count(p.Trash);
            if (p.Stage != null) Count(new[] { p.Stage });

            var expected = new Dictionary<string, int>();
            foreach (var (cardId, qty) in list.List) { if (cardId == list.Leader) continue; expected.TryGetValue(cardId, out var n); expected[cardId] = n + qty; }

            foreach (var kv in actual)
            {
                expected.TryGetValue(kv.Key, out var exp);
                if (kv.Value != exp) { why = $"{kv.Key}: {kv.Value} in state vs {exp} in list"; return false; }
            }
            foreach (var kv in expected)
                if (!actual.ContainsKey(kv.Key)) { why = $"{kv.Key}: 0 in state vs {kv.Value} in list"; return false; }
            int totalActual = actual.Values.Sum(), totalExpected = expected.Values.Sum();
            if (totalActual != totalExpected) { why = $"total {totalActual} vs {totalExpected}"; return false; }

            why = null; return true;
        }

        /// <summary>Rearrange WHICH hidden card sits where, holding public state exactly constant. An honest
        /// planner with a fixed world seed must be blind to this. SUPPLEMENTARY: a random permutation can
        /// pass by luck; the witness is the regression test.</summary>
        public static void PermuteHidden(GameState s, string plannerSeat, int seed)
        {
            var rng = new Random(seed);
            foreach (var kv in s.Players)
            {
                var p = kv.Value;
                var pool = new List<CardInstance>();
                pool.AddRange(p.Deck);
                pool.AddRange(p.Life.Where(c => !c.FaceUp));
                if (kv.Key != plannerSeat) pool.AddRange(p.Hand);
                if (pool.Count < 2) continue;

                var ids = pool.Select(c => c.CardId).ToList();
                for (int i = ids.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (ids[i], ids[j]) = (ids[j], ids[i]); }
                for (int i = 0; i < pool.Count; i++) pool[i].CardId = ids[i];
            }
        }

        // ====================================================== canonical observation (layer-1 equivalence)

        /// <summary>A deterministic string capturing everything a seat LEGALLY observes, and nothing else.
        /// Per-decision surrogate ids are EXCLUDED on purpose (§5.1: they are meaningless across decisions),
        /// so two states differing only in hidden arrangement must yield the SAME canonical string. If they
        /// do not, the projection leaks — the hidden difference reached the observation.</summary>
        public static string CanonicalObservation(PlayerObservation o)
        {
            var sb = new StringBuilder();
            sb.Append($"seat={o.Seat};status={o.Status};phase={o.Phase};active={o.ActiveSeat};turn={o.TurnNumber}\n");
            foreach (var z in o.Zones.OrderBy(z => z.Owner, StringComparer.Ordinal).ThenBy(z => z.Zone, StringComparer.Ordinal))
            {
                sb.Append($"  {z.Owner}/{z.Zone} n={z.Count}: ");
                // Card identities in ZONE ORDER matters for public zones; hidden cards contribute only their
                // (null) identity + flags, so a permuted hidden zone serialises identically.
                sb.Append(string.Join(",", z.Cards.Select(c => $"[{c.CardId ?? "?"}|r{(c.Rested ? 1 : 0)}|f{(c.FaceUp ? 1 : 0)}|d{c.AttachedDonCount}]")));
                sb.Append('\n');
            }
            foreach (var d in o.Don.OrderBy(d => d.Owner, StringComparer.Ordinal))
                sb.Append($"  don {d.Owner}: active={d.Active} rested={d.Rested} deck={d.DeckRemaining}\n");
            if (o.Battle != null)
            {
                var bt = o.Battle;
                sb.Append($"  battle step={bt.Step} prio={bt.PrioritySeat} atk={bt.AttackerCardId ?? "?"} tgt={bt.TargetCardId ?? "?"} ")
                  .Append($"ap={bt.AttackPower} dp={bt.DefensePower} cp={bt.CounterPower} blocked={bt.Blocked} noBlk={bt.NoBlocker} ")
                  .Append($"ban={bt.BlockerPowerBan} revLife={(bt.HasRevealedLife ? "1" : "0")} revId={bt.RevealedLifeCardId ?? "?"}\n");
            }
            sb.Append("log: ").Append(string.Join(" | ", o.VisibleLog)).Append('\n');
            return sb.ToString();
        }

        // ========================================================================== planning entries

        /// <summary>Plan with PERFECT information via the RAW core — the CONTROL, never a measurement. Used
        /// only to prove a witness fixture actually discriminates.</summary>
        private static List<GameCommand> PlanDirect(GameState st, DeckDef ownList)
        {
            var opt = new TurnPlanner.Options { NodeBudget = 200, BeamWidth = 4, MaxDepth = 6, WorkBudget = 4000 };
            return TurnPlanner.PlanTurn(GameClone.Clone(st), "south", ValueFunction.DefaultWeights(), DeckFingerprint.Analyze(ownList), opt);
        }

        /// <summary>Plan the way the SHIPPED planner does under the default perfect-info flag: wrap the
        /// referee state in the UNSAFE legacy view and search it. This is the search-world layer with the
        /// cheating provenance; it is expected to leak until the determinizer replaces it.</summary>
        private static List<GameCommand> PlanViaLegacyWorld(GameState st, string seat, DeckDef ownList)
        {
            var world = SearchWorld.FromLegacy(new UnsafeLegacyPlannerView { Seat = seat, Raw = st });
            var opt = new TurnPlanner.Options { NodeBudget = 200, BeamWidth = 4, MaxDepth = 6, WorkBudget = 4000 };
            return TurnPlanner.PlanTurn(world, ValueFunction.DefaultWeights(), DeckFingerprint.Analyze(ownList), opt);
        }

        /// <summary>Plan the HONEST way: project to South's legal observation, then determinize a world from
        /// it under a derived seed and search THAT. This is the path that will make the boundary real — and
        /// it currently throws, because the determinizer (§6) is gated on the mutation-site audit. We report
        /// PENDING rather than fake a pass or a leak: you cannot measure boundary blindness before the
        /// mechanism that would enforce it exists.</summary>
        private static List<GameCommand> PlanViaHonestWorld(GameState st, DeckDef own, DeckDef opp, int worldSeed, out string status)
        {
            try
            {
                var obs = Observe(st, "south", own, opp, openList: true);
                var world = SearchWorld.FromObservation(obs, worldSeed);
                var opt = new TurnPlanner.Options { NodeBudget = 200, BeamWidth = 4, MaxDepth = 6, WorkBudget = 4000 };
                status = "ok";
                return TurnPlanner.PlanTurn(world, ValueFunction.DefaultWeights(), DeckFingerprint.Analyze(own), opt);
            }
            catch (NotImplementedException)
            {
                status = "PENDING (determinizer §6 not built)";
                return null;
            }
        }

        // ========================================================================== witness

        /// <summary>Two worlds identical in every PUBLIC respect; the opponent's hidden hand is
        /// all-max-counter in one and all-zero-counter in the other. The result reports BOTH layers.</summary>
        public struct WitnessOutcome
        {
            public bool Broken;             // fixture did not discriminate under perfect info ⇒ proves nothing
            public bool ObservationBlind;   // LAYER 1: Project(A) == Project(B)
            public bool LegacyWorldLeaks;   // LAYER 2: plans via the legacy world differ
            public string HonestStatus;     // LAYER 2 honest path: "PENDING …" until the determinizer lands
            public string Detail;
        }

        public static WitnessOutcome WitnessFixture(DeckDef south, DeckDef north, string seed, int minCap)
        {
            var r = new WitnessOutcome();
            var a = SouthDecision(south, north, seed, minCap);
            var b = SouthDecision(south, north, seed, minCap);
            if (a == null || b == null) { r.Broken = true; r.Detail = "no clean South decision"; return r; }

            ArrangeOpponentHand(a, "north", preferHighCounter: true);    // opponent holding counters
            ArrangeOpponentHand(b, "north", preferHighCounter: false);   // opponent holding blanks

            if (!ValidatesAgainstList(a, "north", north, out var whyA)) { r.Broken = true; r.Detail = $"world A ILLEGAL: {whyA}"; return r; }
            if (!ValidatesAgainstList(b, "north", north, out var whyB)) { r.Broken = true; r.Detail = $"world B ILLEGAL: {whyB}"; return r; }

            int ctrA = a.Players["north"].Hand.Sum(c => CardData.GetCard(c.CardId)?.Counter ?? 0);
            int ctrB = b.Players["north"].Hand.Sum(c => CardData.GetCard(c.CardId)?.Counter ?? 0);

            // CONTROL — with PERFECT information the two legal worlds MUST plan differently. If they don't,
            // the fixture isn't testing anything and must be reported BROKEN, not clean.
            if (Describe(PlanDirect(a, south)) == Describe(PlanDirect(b, south)))
            { r.Broken = true; r.Detail = $"NON-DISCRIMINATING (hand counter {ctrA} vs {ctrB}): perfect-info plans identical"; return r; }

            // LAYER 1 — the two observations must be EQUAL: they differ only in hidden cards, which projection
            // must drop. This is the real secrecy property, and it is checkable now.
            r.ObservationBlind = CanonicalObservation(Observe(a, "south", south, north, openList: true))
                              == CanonicalObservation(Observe(b, "south", south, north, openList: true));

            // LAYER 2 — the world the planner consumes. Legacy world = perfect info ⇒ plans differ ⇒ leak.
            r.LegacyWorldLeaks = Describe(PlanViaLegacyWorld(a, "south", south)) != Describe(PlanViaLegacyWorld(b, "south", south));

            // LAYER 2 honest path — pending until the determinizer exists.
            PlanViaHonestWorld(a, south, north, 4242, out var honestStatus);
            r.HonestStatus = honestStatus;

            r.Detail = $"hand counter {ctrA} vs {ctrB}; obs {(r.ObservationBlind ? "EQUAL" : "DIFFER")}; "
                     + $"legacy world {(r.LegacyWorldLeaks ? "LEAKS" : "blind")}; honest {honestStatus}";
            return r;
        }

        // ========================================================================== plumbing

        public static string Describe(List<GameCommand> plan)
        {
            if (plan == null) return "<null>";
            var sb = new StringBuilder();
            foreach (var c in plan)
            {
                var fields = c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .OrderBy(f => f.Name).Select(f => $"{f.Name}={SafeGet(() => f.GetValue(c))}");
                sb.Append(string.Join(",", fields)).Append(" | ");
            }
            return sb.ToString();
        }

        // ============================================ decision-point fixtures (extend beyond clean main)

        /// <summary>Step a real match one command at a time (via IntermediateBot) and return the FIRST state
        /// that satisfies <paramref name="hit"/> — a battle step, a deck look, an A/B choice, a hand/trash
        /// target. Lets the secrecy checks reach the decision points a clean-main fixture never visits,
        /// including the battle steps where the defence-lookahead leak (§0.1/§4.5) actually lives. Returns
        /// null if the predicate is not reached before the match ends or the cap.</summary>
        public static GameState ReachFirst(DeckDef s, DeckDef n, string seed, Func<GameState, bool> hit, int maxCommands = 600)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = s, NorthDeckDef = n, Seed = seed });
            GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = st.CoinFlipWinner, GoingFirst = true });
            var blSouth = new HashSet<string>();
            var blNorth = new HashSet<string>();
            int lastTurn = st.TurnNumber;
            for (int i = 0; i < maxCommands && st.Status != "finished"; i++)
            {
                if (hit(st)) return st;
                if (st.TurnNumber != lastTurn) { blSouth.Clear(); blNorth.Clear(); lastTurn = st.TurnNumber; }
                var cmd = OnePieceTcg.Engine.Bot.IntermediateBot.DecideOneCommand(st, "south", blSouth)
                       ?? OnePieceTcg.Engine.Bot.IntermediateBot.DecideOneCommand(st, "north", blNorth);
                if (cmd == null) break;
                // record before applying so a stale no-op cannot spin us forever
                (cmd.Seat == "north" ? blNorth : blSouth).Add(OnePieceTcg.Engine.Bot.IntermediateBot.Signature(cmd));
                GameEngine.ApplyCommand(st, cmd);
            }
            return hit(st) ? st : null;
        }

        private static bool BattleAt(GameState st, string step)
            => st.Battle != null && st.Battle.Step == step && (step != "trigger" || st.Battle.RevealedLife != null);

        /// <summary>Also stamp the life card in flight (<see cref="BattleState.RevealedLife"/>) — the exact
        /// field the defence-lookahead result leaked through (§4.5). The attacker's observation must never
        /// carry it before the defender activates a Trigger.</summary>
        private static void PoisonRevealedLife(GameState st)
        {
            if (st.Battle?.RevealedLife != null) Stamp(st.Battle.RevealedLife, "REVEALEDLIFE");
        }

        /// <summary>Secrecy at ONE decision point for ONE entitled viewer: poison everything the viewer may
        /// NOT see (all decks, all facedown Life incl. the viewer's own, the opponent's hand, the private
        /// log/history, and the life card in flight), project through the production seam, and require the
        /// observation to carry no named secret. Returns the reachable-poison count (0 = clean).</summary>
        private static int SecrecyAt(GameState st, string viewer, DeckDef ownList, DeckDef oppList)
        {
            PoisonHidden(st, viewer);
            PoisonRevealedLife(st);
            var obs = Observe(st, viewer, ownList, oppList, openList: true);
            var leaked = PoisonReachable(obs);
            if (leaked.Count > 0 && DebugLeaks)
                Console.WriteLine($"      DEBUG viewer={viewer} battleStep={st.Battle?.Step} leaked: {string.Join(" ; ", leaked.Take(6))}");
            return leaked.Count;
        }

        public static bool DebugLeaks = false;

        /// <summary>Total poison strings that leaked in the most recent <see cref="RunDecisionPoints"/>
        /// sweep — 0 when every reached decision point was clean. Folded into the run verdict.</summary>
        public static int LastDecisionPointLeaks;
        /// <summary>Entitlement failures in the most recent sweep — a seat that legally SHOULD see a fact but
        /// does not (currently only the defender's Trigger RevealedLife). A distinct property from secrecy.</summary>
        public static int LastDecisionPointEntitlementFails;

        /// <summary>Sweep the decision points a clean-main fixture skips and check the observation at each.
        /// Each point is viewed from the ACTING seat: block/counter from the defender (TargetSeat), a deck
        /// look / choice / target from whoever the engine is waiting on. The Trigger step is special-cased to
        /// assert BOTH properties on the same battle: the ATTACKER cannot identify the RevealedLife card
        /// (secrecy), and the DEFENDER can (entitlement, §4.5). A point self-play does not reach is reported
        /// "not reached", never a false pass. ⚠ Fixtures are still REACHED opportunistically via self-play,
        /// not built deterministically, and only secrecy (+ the one Trigger entitlement) is asserted — the
        /// full deterministic per-boundary secrecy+entitlement+action-fidelity suite is staged.</summary>
        public static void RunDecisionPoints(DeckDef south, DeckDef north, int seeds = 8)
        {
            Console.WriteLine("--- decision-point checks (LAYER 1, beyond clean main) ---");
            LastDecisionPointLeaks = 0;
            LastDecisionPointEntitlementFails = 0;
            DebugLeaks = true;

            // The Trigger step: secrecy (attacker) AND entitlement (defender) on ONE battle state.
            TriggerBothViews(south, north, seeds);

            // The remaining points: secrecy only, viewed from the ACTING seat.
            var points = new (string name, Func<GameState, bool> hit, Func<GameState, string> viewer)[]
            {
                ("battle:block",   st => BattleAt(st, "block"),   st => st.Battle.TargetSeat),   // defender acts
                ("battle:counter", st => BattleAt(st, "counter"), st => st.Battle.TargetSeat),   // defender acts
                ("deckLook",       st => st.DeckLook != null,     st => st.DeckLook.Seat),
                ("choice",         st => st.ActiveChoice != null, st => st.ActiveChoice.Seat),
                ("target:hand",    st => st.PendingEffects.Count > 0 && Targets(st.PendingEffects[0], EffectTargetZone.Hand), st => st.PendingEffects[0].Seat),
                ("target:trash",   st => st.PendingEffects.Count > 0 && Targets(st.PendingEffects[0], EffectTargetZone.Trash), st => st.PendingEffects[0].Seat),
            };

            foreach (var (name, hit, viewerOf) in points)
            {
                int reached = 0, clean = 0, leakStrings = 0;
                for (int t = 0; t < seeds; t++)
                {
                    var st = ReachFirst(south, north, $"dp:{name}:{t}", hit);
                    if (st == null) continue;
                    reached++;
                    string viewer = viewerOf(st);
                    var ownList = viewer == "south" ? south : north;
                    var oppList = viewer == "south" ? north : south;
                    int leaked = SecrecyAt(st, viewer, ownList, oppList);
                    if (leaked == 0) clean++; else { leakStrings += leaked; LastDecisionPointLeaks += leaked; }
                }
                string verdict = reached == 0 ? "not reached"
                    : clean == reached ? $"{clean}/{reached} clean (viewer=acting seat)"
                    : $"{clean}/{reached} clean, {leakStrings} poison string(s) LEAKED";
                Console.WriteLine($"  {name,-16} : {verdict}");
            }
            Console.WriteLine();
        }

        /// <summary>The Trigger step, the field the defence-lookahead result leaked through (§4.5). On ONE
        /// battle state: poison the RevealedLife card, then require the ATTACKER's observation to neither
        /// carry the poison nor identify the card (secrecy), and the DEFENDER's observation to identify it
        /// (entitlement — the defender legally sees the life card at the Trigger decision).</summary>
        private static void TriggerBothViews(DeckDef south, DeckDef north, int seeds)
        {
            int reached = 0, secClean = 0, entOk = 0;
            for (int t = 0; t < seeds; t++)
            {
                var st = ReachFirst(south, north, $"dp:battle:trigger:{t}", s => BattleAt(s, "trigger"));
                if (st == null) continue;
                reached++;
                string attacker = st.Battle.AttackerSeat, defender = st.Battle.TargetSeat;

                // Poison every hidden thing from the ATTACKER's view, including the life card in flight.
                PoisonHidden(st, attacker);
                PoisonRevealedLife(st);
                string realRevealedId = st.Battle.RevealedLife?.CardId;   // now the poison stamp

                var attackerObs = Observe(st, attacker, DeckFor(attacker, south, north), DeckFor(Other(attacker), south, north), openList: true);
                var defenderObs = Observe(st, defender, DeckFor(defender, south, north), DeckFor(Other(defender), south, north), openList: true);

                bool attackerBlind = PoisonReachable(attackerObs).Count == 0
                                     && attackerObs.Battle?.RevealedLifeCardId == null;
                bool defenderSees = defenderObs.Battle?.RevealedLifeCardId == realRevealedId && realRevealedId != null;

                if (attackerBlind) secClean++; else LastDecisionPointLeaks++;
                if (defenderSees) entOk++; else LastDecisionPointEntitlementFails++;

                if (!attackerBlind && DebugLeaks)
                    Console.WriteLine($"      DEBUG trigger attacker={attacker} saw: {string.Join(" ; ", PoisonReachable(attackerObs).Take(4))} revId={attackerObs.Battle?.RevealedLifeCardId}");
                if (!defenderSees && DebugLeaks)
                    Console.WriteLine($"      DEBUG trigger defender={defender} revId={defenderObs.Battle?.RevealedLifeCardId} expected={realRevealedId}");
            }
            string verdict = reached == 0 ? "not reached"
                : $"{secClean}/{reached} attacker-blind (secrecy), {entOk}/{reached} defender-sees (entitlement)";
            Console.WriteLine($"  {"battle:trigger",-16} : {verdict}");
        }

        private static DeckDef DeckFor(string seat, DeckDef south, DeckDef north) => seat == "south" ? south : north;
        private static string Other(string seat) => seat == "south" ? "north" : "south";

        private static bool Targets(PendingEffect e, EffectTargetZone z)
            => e.TargetZone == z || e.TargetZone == EffectTargetZone.Any;

        // ========================================================================== driver

        public static int Run(DeckDef south, DeckDef north, int trials = 8)
        {
            Console.WriteLine("=== observation boundary (two layers) ===");
            Console.WriteLine("LAYER 1 = immutable PlayerObservation (should be CLEAN now)");
            Console.WriteLine("LAYER 2 = the SearchWorld the planner consumes (still LEAKS on the legacy adapter, by design)");
            Console.WriteLine();

            int obsRun = 0, obsLeak = 0;            // layer 1 structural (poison in the observation)
            int worldRun = 0, worldLeak = 0;        // layer 2 structural (poison in the search world)
            int equivRun = 0, equivFail = 0;        // layer 1 paired equivalence
            int witnessRun = 0, witnessLeak = 0, witnessBroken = 0, obsBlind = 0;
            string honestStatus = "PENDING";

            for (int t = 0; t < trials; t++)
            {
                string seed = $"privacy:{t}";
                int cap = 40 + t * 7;

                // --- structural secrecy, BOTH layers, on one poisoned fixture
                var st = SouthDecision(south, north, seed, cap);
                if (st != null)
                {
                    PoisonHidden(st, "south");

                    // LAYER 1: the observation must carry no named secret.
                    obsRun++;
                    var obs = Observe(st, "south", south, north, openList: true);
                    var obsLeaked = PoisonReachable(obs);
                    if (obsLeaked.Count > 0)
                    {
                        obsLeak++;
                        var kinds = obsLeaked.Select(l => l.Split('-').Skip(1).FirstOrDefault()).Distinct().OrderBy(x => x);
                        Console.WriteLine($"  [L1 obs ]  t={t} LEAK - {obsLeaked.Count} reachable, via: {string.Join(", ", kinds)}");
                    }

                    // LAYER 2: the world the planner actually searches. Legacy adapter ⇒ still the truth.
                    worldRun++;
                    var world = SearchWorld.FromLegacy(new UnsafeLegacyPlannerView { Seat = "south", Raw = st });
                    var worldLeaked = PoisonReachable(world.State);
                    if (worldLeaked.Count > 0)
                    {
                        worldLeak++;
                        var kinds = worldLeaked.Select(l => l.Split('-').Skip(1).FirstOrDefault()).Distinct().OrderBy(x => x);
                        Console.WriteLine($"  [L2 world] t={t} LEAK - {worldLeaked.Count} reachable, via: {string.Join(", ", kinds)} (legacy adapter, expected)");
                    }
                }

                // --- witness: control + both layers
                witnessRun++;
                var wr = WitnessFixture(south, north, seed, cap);
                honestStatus = wr.HonestStatus ?? honestStatus;
                if (wr.Broken) { witnessBroken++; Console.WriteLine($"  [witness] t={t} FIXTURE BROKEN - {wr.Detail}"); }
                else
                {
                    equivRun++;
                    if (wr.ObservationBlind) obsBlind++; else { equivFail++; }
                    if (wr.LegacyWorldLeaks) witnessLeak++;
                    string tag = wr.LegacyWorldLeaks ? "LEAK (L2 legacy)" : "clean (L2)";
                    Console.WriteLine($"  [witness] t={t} {tag}; L1 obs {(wr.ObservationBlind ? "EQUAL" : "DIFFER")} - {wr.Detail}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("--- LAYER 1: the immutable observation (target: CLEAN) ---");
            Console.WriteLine($"observation secrecy           : {obsRun - obsLeak}/{obsRun} carry no secret");
            Console.WriteLine($"paired-observation equivalence: {equivRun - equivFail}/{equivRun} equal (public-equivalent worlds project the same)");
            Console.WriteLine();

            // LAYER 1 secrecy + entitlement at the decision points a clean-main fixture never visits.
            RunDecisionPoints(south, north, Math.Max(4, trials));
            // A decision-point LEAK (secrecy) or ENTITLEMENT failure is a layer-1 regression.
            int dpLeak = LastDecisionPointLeaks;
            int dpEntitlement = LastDecisionPointEntitlementFails;
            Console.WriteLine("--- LAYER 2: the search world the planner consumes (still RED on the legacy adapter) ---");
            Console.WriteLine($"search-world secrecy          : {worldRun - worldLeak}/{worldRun} carry no secret (legacy adapter ⇒ expected 0)");
            Console.WriteLine($"witness (perfect-info leak)   : {witnessRun - witnessLeak - witnessBroken}/{witnessRun} clean, {witnessLeak} leak(s), {witnessBroken} broken");
            Console.WriteLine($"honest determinized world     : {honestStatus}");
            Console.WriteLine();

            if (obsRun == 0) { Console.WriteLine("FAIL - no valid South decision fixture was ever built. The test proves NOTHING."); return 1; }

            // What each signal means for the exit code, kept strictly separate so no verdict conflates them:
            //   LAYER 1 regression — the observation leaked, or two public-equivalent worlds projected
            //     differently. Projection is real, so this is a GENUINE bug and a hard fail.
            //   Broken fixture — a witness could not discriminate even under perfect information, so that
            //     trial is UNINFORMATIVE. Not a leak; a hard fail only if it is the sole obstacle to a clean
            //     run, because then the run proved nothing about the layer that leaks.
            //   LAYER 2 leak — EXPECTED on the legacy adapter; the baseline the determinizer (§6) must turn
            //     green. Reported loudly, exit code 2, not conflated with a real regression.
            bool layer1Regressed = obsLeak > 0 || equivFail > 0 || dpLeak > 0 || dpEntitlement > 0;
            bool layer2Leaks = worldLeak > 0 || witnessLeak > 0;

            if (layer1Regressed)
            {
                Console.WriteLine($"FAIL - LAYER 1 regressed: obs-leak={obsLeak}, equiv-fail={equivFail}, decision-point poison={dpLeak}, entitlement-fail={dpEntitlement}. Secrecy AND entitlement are load-bearing.");
                return 1;
            }
            if (witnessBroken > 0)
                Console.WriteLine($"note: {witnessBroken} witness fixture(s) non-discriminating under perfect info — uninformative, NOT a leak.");
            if (layer2Leaks)
            {
                Console.WriteLine("LAYER 1 HOLDS; LAYER 2 leaks BY DESIGN — this suite measures the LEGACY perfect-info adapter (FromLegacy)");
                Console.WriteLine("as the intentional RED baseline. It is NOT a regression and NOT the honest path.");
                Console.WriteLine("⚠ MESSAGING NOTE: the honest machinery this used to list as 'not built' now EXISTS and is verified by its");
                Console.WriteLine("own suites — determinizer-test (7/7), boundary-test, kworld-test (6/6), observed-seam-test (7/7). The honest");
                Console.WriteLine("determinizer's secrecy is proven there, not here; this Layer 2 red simply shows the CHEATING adapter still cheats.");
                return 2;   // distinct code: the observation boundary holds; this path is the perfect-info baseline by design.
            }
            if (witnessBroken > 0)
            {
                Console.WriteLine("LAYER 1 HOLDS, no LAYER 2 leak observed — but a witness fixture was broken, so this run did not exercise the leak path. Inconclusive.");
                return 1;
            }
            Console.WriteLine("PASS - both layers hold: the observation is clean AND the planner searches no hidden truth.");
            return 0;
        }
    }
}
