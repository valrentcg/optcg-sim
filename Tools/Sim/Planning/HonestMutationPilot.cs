using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Expert;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// A deliberately small, same-day honest evaluation pilot. It does NOT claim to evolve a globally
    /// optimal bot: it searches a narrow, archetype-specific neighbourhood, selects on fixed training
    /// matchups, and then re-tests only the finalists on untouched seeds. Every candidate uses the same
    /// honest determinizer/search path and the same deck/seat/turn-order schedule.
    /// </summary>
    public static class HonestMutationPilot
    {
        private sealed class Candidate
        {
            public string Name;
            public double[] Weights;
            public bool UseGreedyPolicy;
            public bool UseAdvancedPolicy;
            public string AdvancedScope = "all";
            public ExpertPolicyModel ExpertModel;
            public EvalResult Train;
            public EvalResult Holdout;
        }

        private sealed class EvalResult
        {
            public int[] Outcomes; // 1=win, 0=loss, -1=invalid (stuck/throw/tie)
            public bool[] Adjudicated;
            public string[] Failures;
            public int Wins => Outcomes.Count(x => x == 1);
            public int Losses => Outcomes.Count(x => x == 0);
            public int Invalid => Outcomes.Count(x => x < 0);
            public int Decided => Wins + Losses;
            public int AdjudicatedGames => Adjudicated.Count(x => x);
            public int OutrightWins => Outcomes.Select((x, i) => (x, i))
                .Count(x => x.x == 1 && !Adjudicated[x.i]);
        }

        private sealed class Scenario
        {
            public DeckDef Own;
            public DeckDef Opp;
            public DeckContext Context;
            public string Seed;
        }

        public static int Run(DeckRegistry registry, int trainPairs = 6, int holdoutPairs = 10,
            int nodeBudget = 250, string archetype = "aggro", int dop = 4, int seedOffset = 0,
            bool mirror = true)
        {
            var pool = CleanMeta(registry);
            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var ownPool = string.Equals(archetype, "all", StringComparison.OrdinalIgnoreCase)
                ? pool
                : pool.Where(d => contexts[d.Id].Archetype == archetype).ToList();
            if (ownPool.Count == 0)
            {
                Console.WriteLine($"No clean meta decks classified as '{archetype}'.");
                return 1;
            }

            var train = BuildScenarios(ownPool, pool, contexts, trainPairs, 17001 + seedOffset, $"train:{seedOffset}", mirror);
            var holdout = BuildScenarios(ownPool, pool, contexts, holdoutPairs, 29003 + seedOffset, $"holdout:{seedOffset}", mirror);
            var candidates = BuildCandidates();

            Console.WriteLine("=== honest archetype mutation pilot ===");
            Console.WriteLine($"archetype={archetype}; matchup={(mirror ? "mirror" : "field")}; decks={ownPool.Count}; " +
                              $"train={trainPairs} pairs/{trainPairs * 2} games per candidate; " +
                              $"holdout={holdoutPairs} pairs/{holdoutPairs * 2} games per finalist; " +
                              $"nodes={nodeBudget}; dop={dop}; seedOffset={seedOffset}");
            Console.WriteLine("Candidates share the exact matchup/seat/turn-order schedule. Invalid games never count as wins.\n");

            foreach (var c in candidates)
            {
                c.Train = Evaluate(c, train, nodeBudget, dop);
                Console.WriteLine($"  train {c.Name,-14} {c.Train.Wins,2}/{c.Train.Decided,-2} " +
                                  $"({Percent(c.Train)}) outrightWins={c.Train.OutrightWins} " +
                                  $"adjudicated={c.Train.AdjudicatedGames} invalid={c.Train.Invalid}");
            }

            var baseline = candidates[0];
            var finalists = candidates.Skip(1)
                .OrderByDescending(c => PairedDelta(c.Train, baseline.Train).delta)
                .ThenByDescending(c => c.Train.Wins)
                .Take(2).ToList();

            baseline.Holdout = Evaluate(baseline, holdout, nodeBudget, dop);
            foreach (var c in finalists) c.Holdout = Evaluate(c, holdout, nodeBudget, dop);

            Console.WriteLine("\n=== untouched-seed holdout ===");
            PrintHoldout(baseline, baseline);
            foreach (var c in finalists) PrintHoldout(c, baseline);

            var best = finalists
                .OrderByDescending(c => PairedDelta(c.Holdout, baseline.Holdout).delta)
                .ThenByDescending(c => c.Holdout.Wins)
                .First();
            var proof = PairedDelta(best.Holdout, baseline.Holdout);
            var invalid = InvalidDelta(best.Holdout, baseline.Holdout);
            bool provisional = proof.delta >= 2 && best.Holdout.Wins > baseline.Holdout.Wins
                && best.Holdout.OutrightWins > baseline.Holdout.OutrightWins
                && invalid.candidateOnly == 0;

            Console.WriteLine("\n=== verdict ===");
            if (provisional)
            {
                Console.WriteLine($"PROVISIONAL UPGRADE SIGNAL: {best.Name} gained {proof.delta:+#;-#;0} paired wins " +
                                  $"over default on untouched seeds ({proof.better} improved / {proof.worse} regressed).");
                if (invalid.common > 0)
                    Console.WriteLine($"Excluded {invalid.common} common invalid game(s); candidate-only invalids={invalid.candidateOnly}.");
                Console.WriteLine("This is an EOD candidate, not a promotion: confirm with a larger paired run before making it default.");
                Console.WriteLine("Multipliers vs default: " + DescribeMultipliers(best.Weights));
            }
            else
            {
                Console.WriteLine("NO HOLDOUT UPGRADE SIGNAL. Do not promote a mutation from this pilot.");
                Console.WriteLine($"Best was {best.Name}: paired delta {proof.delta:+#;-#;0} " +
                                  $"({proof.better} improved / {proof.worse} regressed), " +
                                  $"wins {best.Holdout.Wins}/{best.Holdout.Decided} vs default " +
                                  $"{baseline.Holdout.Wins}/{baseline.Holdout.Decided}.");
            }
            return 0;
        }

        /// <summary>Direct paired A/B for a policy-family route. Unlike the mutation tournament, this does
        /// not risk dropping the policy candidate when tiny training samples tie at zero, and it does not
        /// spend most of the day's compute re-evaluating weight vectors that already showed no gradient.</summary>
        public static int RunPolicyCheck(DeckRegistry registry, int pairs = 10, int nodeBudget = 100,
            string archetype = "aggro", int dop = 4, int seedOffset = 0, bool mirror = false)
        {
            var pool = CleanMeta(registry);
            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var ownPool = string.Equals(archetype, "all", StringComparison.OrdinalIgnoreCase)
                ? pool
                : pool.Where(d => contexts[d.Id].Archetype == archetype).ToList();
            if (ownPool.Count == 0)
            {
                Console.WriteLine($"No clean meta decks classified as '{archetype}'.");
                return 1;
            }

            var scenarios = BuildScenarios(ownPool, pool, contexts, pairs, 41009 + seedOffset,
                $"policy:{seedOffset}", mirror);
            var weights = ValueFunction.DefaultWeights();
            var search = new Candidate { Name = "honest-search", Weights = weights, UseGreedyPolicy = false };
            var routed = new Candidate { Name = "honest-greedy", Weights = weights, UseGreedyPolicy = true };

            string field = mirror ? "mirror" : "field";
            string searchRun = ProgressBoard.Start("search",
                $"honest-search | {archetype} {field} | n={pairs} | nodes={nodeBudget} | seed+={seedOffset}", scenarios.Count * 2);
            string routedRun = ProgressBoard.Start("routed",
                $"honest-greedy (routed) | {archetype} {field} | n={pairs} | seed+={seedOffset}", scenarios.Count * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");
            Console.WriteLine("  open that file in a browser to watch the bars fill live.\n");

            // Let both bars move together while respecting the caller's total worker budget. Apart from
            // making the live board useful, this also prevents wall-clock drift between arms from turning a
            // long A/B into two sequential jobs. With dop=1 retain the strictly sequential low-resource path.
            if (dop <= 1)
            {
                search.Holdout = Evaluate(search, scenarios, nodeBudget, 1, searchRun);
                ProgressBoard.Finish(searchRun);
                routed.Holdout = Evaluate(routed, scenarios, nodeBudget, 1, routedRun);
                ProgressBoard.Finish(routedRun);
            }
            else
            {
                int searchDop = Math.Max(1, dop / 2);
                int routedDop = Math.Max(1, dop - searchDop);
                var searchTask = Task.Run(() =>
                {
                    try { return Evaluate(search, scenarios, nodeBudget, searchDop, searchRun); }
                    finally { ProgressBoard.Finish(searchRun); }
                });
                var routedTask = Task.Run(() =>
                {
                    try { return Evaluate(routed, scenarios, nodeBudget, routedDop, routedRun); }
                    finally { ProgressBoard.Finish(routedRun); }
                });
                Task.WaitAll(searchTask, routedTask);
                search.Holdout = searchTask.Result;
                routed.Holdout = routedTask.Result;
            }

            var delta = PairedDelta(routed.Holdout, search.Holdout);
            var invalid = InvalidDelta(routed.Holdout, search.Holdout);
            Console.WriteLine("=== direct honest policy A/B ===");
            Console.WriteLine($"archetype={archetype}; matchup={(mirror ? "mirror" : "field")}; " +
                              $"pairs={pairs}/{pairs * 2} games; nodes={nodeBudget}; seedOffset={seedOffset}");
            PrintHoldout(search, search);
            PrintHoldout(routed, search);
            Console.WriteLine($"commonInvalid={invalid.common}; routeOnlyInvalid={invalid.candidateOnly}; " +
                              $"searchOnlyInvalid={invalid.baselineOnly}");
            bool signal = delta.delta >= 2 && routed.Holdout.Wins > search.Holdout.Wins
                && routed.Holdout.OutrightWins > search.Holdout.OutrightWins
                && invalid.candidateOnly == 0;
            Console.WriteLine(signal
                ? $"POLICY ROUTE SIGNAL: +{delta.delta} paired wins ({delta.better} improved/{delta.worse} regressed)."
                : $"NO POLICY ROUTE SIGNAL: {delta.delta:+#;-#;0} paired wins ({delta.better} improved/{delta.worse} regressed).");
            return 0;
        }

        /// <summary>Direct paired test of the rollout-improved honest policy against the proven greedy
        /// floor. Both candidates still play the same held-out IntermediateBot and use identical scenarios.</summary>
        public static int RunAdvancedCheck(DeckRegistry registry, int pairs = 6, string archetype = "all",
            int dop = 4, int seedOffset = 0, bool mirror = false, string advancedScope = "all",
            string opponentKind = "baseline")
        {
            var pool = CleanMeta(registry);
            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var ownPool = string.Equals(archetype, "all", StringComparison.OrdinalIgnoreCase)
                ? pool
                : pool.Where(d => contexts[d.Id].Archetype == archetype).ToList();
            if (ownPool.Count == 0)
            {
                Console.WriteLine($"No clean meta decks classified as '{archetype}'.");
                return 1;
            }

            var scenarios = BuildScenarios(ownPool, pool, contexts, pairs, 51031 + seedOffset,
                $"advanced:{seedOffset}", mirror);
            var weights = ValueFunction.DefaultWeights();
            var greedy = new Candidate { Name = "honest-greedy", Weights = weights, UseGreedyPolicy = true };
            var advanced = new Candidate { Name = "honest-rollout", Weights = weights,
                UseGreedyPolicy = true, UseAdvancedPolicy = true, AdvancedScope = advancedScope };
            AdvancedRolloutPolicy.ResetStats();
            AdvancedPressurePolicy.ResetStats();
            AdvancedActivationPolicy.ResetStats();

            string field = mirror ? "mirror" : "field";
            string greedyRun = ProgressBoard.Start("greedyfloor",
                $"honest-greedy vs {opponentKind} | {archetype} {field} | n={pairs} | seed+={seedOffset}", scenarios.Count * 2);
            string advancedRun = ProgressBoard.Start("advanced",
                $"honest-rollout {advancedScope} vs {opponentKind} | {archetype} {field} | n={pairs} | seed+={seedOffset}", scenarios.Count * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");

            if (dop <= 1)
            {
                greedy.Holdout = Evaluate(greedy, scenarios, 100, 1, greedyRun, opponentKind);
                ProgressBoard.Finish(greedyRun);
                advanced.Holdout = Evaluate(advanced, scenarios, 100, 1, advancedRun, opponentKind);
                ProgressBoard.Finish(advancedRun);
            }
            else
            {
                int aDop = Math.Max(1, dop / 2), gDop = Math.Max(1, dop - Math.Max(1, dop / 2));
                var gt = Task.Run(() =>
                {
                    try { return Evaluate(greedy, scenarios, 100, gDop, greedyRun, opponentKind); }
                    finally { ProgressBoard.Finish(greedyRun); }
                });
                var at = Task.Run(() =>
                {
                    try { return Evaluate(advanced, scenarios, 100, aDop, advancedRun, opponentKind); }
                    finally { ProgressBoard.Finish(advancedRun); }
                });
                Task.WaitAll(gt, at);
                greedy.Holdout = gt.Result;
                advanced.Holdout = at.Result;
            }

            var delta = PairedDelta(advanced.Holdout, greedy.Holdout);
            var invalid = InvalidDelta(advanced.Holdout, greedy.Holdout);
            Console.WriteLine("=== honest advanced-policy A/B ===");
            Console.WriteLine($"archetype={archetype}; matchup={field}; scope={advancedScope}; opponent={opponentKind}; " +
                              $"pairs={pairs}/{pairs * 2} games; seedOffset={seedOffset}");
            PrintHoldout(greedy, greedy);
            PrintHoldout(advanced, greedy);
            PrintIdentityBreakdown(scenarios, greedy.Holdout, advanced.Holdout);
            PrintLeaderBreakdown(scenarios, greedy.Holdout, advanced.Holdout);
            PrintInvalidDetails(scenarios, advanced.Holdout, "advanced");
            int scheduled = advanced.Holdout.Outcomes.Length;
            double rate = scheduled == 0 ? 0 : (double)advanced.Holdout.OutrightWins / scheduled;
            bool reachesTarget = rate >= .70 && invalid.candidateOnly == 0;
            var interval = Wilson95(advanced.Holdout.OutrightWins, scheduled);
            var policyStats = AdvancedRolloutPolicy.Stats();
            var pressureStats = AdvancedPressurePolicy.Stats();
            var activationStats = AdvancedActivationPolicy.Stats();
            Console.WriteLine($"advanced overrides: {policyStats.deviations}/{policyStats.decisions} tactical decisions " +
                              $"({(policyStats.decisions == 0 ? 0 : 100.0 * policyStats.deviations / policyStats.decisions):F1}%)");
            Console.WriteLine($"pressure redirects: {pressureStats.redirects}/{pressureStats.attacks} candidate attacks " +
                              $"({(pressureStats.attacks == 0 ? 0 : 100.0 * pressureStats.redirects / pressureStats.attacks):F1}%)");
            Console.WriteLine($"main activations: {activationStats.activations}/{activationStats.opportunities} opportunities " +
                              $"({(activationStats.opportunities == 0 ? 0 : 100.0 * activationStats.activations / activationStats.opportunities):F1}%)");
            Console.WriteLine($"70% TARGET: {(reachesTarget ? "REACHED IN SCREEN" : "NOT REACHED")} " +
                              $"({advanced.Holdout.OutrightWins}/{scheduled} = {100 * rate:F1}%; " +
                              $"Wilson95 [{100 * interval.lo:F1}%, {100 * interval.hi:F1}%]; " +
                              $"paired {delta.better} better/{delta.worse} worse; " +
                              $"advanced-only invalid={invalid.candidateOnly}).");
            return 0;
        }

        /// <summary>
        /// Paired promotion screen on the local meta decks whose leaders/card pools most closely match the
        /// high-bounty public replay corpus. The incumbent is contract-v2; the candidate replaces its
        /// frozen-Intermediate pressure exploit with the aggregate expert attack-target prior. Both play the
        /// same Champion opponent, same replay-matched decks, seats, and seeds. This measures simulator
        /// strength; action agreement is reported separately by expert-sync and is not called a win rate.
        /// </summary>
        public static int RunExpertCheck(DeckRegistry registry, int pairs = 30, int dop = 6,
            int seedOffset = 0, string corpusDir = ExpertReplayCorpus.DefaultOutputDir)
        {
            var catalog = ExpertReplayCorpus.LoadCatalog(corpusDir);
            var model = ExpertReplayCorpus.LoadModel(corpusDir);
            var pool = ExpertReplayCorpus.MatchedDecks(registry, catalog);
            if (pool.Count < 2)
            {
                Console.WriteLine("Need at least two replay-matched expert decks. Run expert-sync first.");
                return 1;
            }

            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var scenarios = BuildScenarios(pool, pool, contexts, pairs, 61031 + seedOffset,
                $"expert:{seedOffset}", mirror: false);
            var weights = ValueFunction.DefaultWeights();
            var incumbent = new Candidate
            {
                Name = "contract-v2", Weights = weights, UseGreedyPolicy = true,
                UseAdvancedPolicy = true, AdvancedScope = "contract-v2",
            };
            var learned = new Candidate
            {
                Name = "expert-v1", Weights = weights, UseGreedyPolicy = true,
                UseAdvancedPolicy = true, AdvancedScope = "expert-v1", ExpertModel = model,
            };

            ExpertReplayPolicy.ResetStats();
            AdvancedRolloutPolicy.ResetStats();
            AdvancedPressurePolicy.ResetStats();
            AdvancedActivationPolicy.ResetStats();
            string incumbentRun = ProgressBoard.Start("expertbase",
                $"contract-v2 vs champion | replay-matched decks | n={pairs} | seed+={seedOffset}", scenarios.Count * 2);
            string learnedRun = ProgressBoard.Start("expertlearned",
                $"expert-v1 vs champion | replay-matched decks | n={pairs} | seed+={seedOffset}", scenarios.Count * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");
            Console.WriteLine($"expert corpus: {model.ReplayCount} replays / {model.ExpertPlayers} high-bounty player-games; " +
                              $"matched decks: {string.Join(", ", pool.Select(d => d.Id))}");

            if (dop <= 1)
            {
                incumbent.Holdout = Evaluate(incumbent, scenarios, 100, 1, incumbentRun, "champion");
                ProgressBoard.Finish(incumbentRun);
                learned.Holdout = Evaluate(learned, scenarios, 100, 1, learnedRun, "champion");
                ProgressBoard.Finish(learnedRun);
            }
            else
            {
                int firstDop = Math.Max(1, dop / 2), secondDop = Math.Max(1, dop - Math.Max(1, dop / 2));
                var bt = Task.Run(() =>
                {
                    try { return Evaluate(incumbent, scenarios, 100, firstDop, incumbentRun, "champion"); }
                    finally { ProgressBoard.Finish(incumbentRun); }
                });
                var lt = Task.Run(() =>
                {
                    try { return Evaluate(learned, scenarios, 100, secondDop, learnedRun, "champion"); }
                    finally { ProgressBoard.Finish(learnedRun); }
                });
                Task.WaitAll(bt, lt);
                incumbent.Holdout = bt.Result;
                learned.Holdout = lt.Result;
            }

            var delta = PairedDelta(learned.Holdout, incumbent.Holdout);
            var invalid = InvalidDelta(learned.Holdout, incumbent.Holdout);
            int scheduled = learned.Holdout.Outcomes.Length;
            double rate = scheduled == 0 ? 0 : (double)learned.Holdout.OutrightWins / scheduled;
            var interval = Wilson95(learned.Holdout.OutrightWins, scheduled);
            var learnedStats = ExpertReplayPolicy.Stats();

            Console.WriteLine("=== high-bounty replay-matched promotion screen ===");
            Console.WriteLine($"pairs={pairs}/{scheduled} games; opponent=champion; seedOffset={seedOffset}; " +
                              "decklists=replay-matched validated proxies (not reconstructed hidden lists)");
            PrintHoldout(incumbent, incumbent);
            PrintHoldout(learned, incumbent);
            PrintIdentityBreakdown(scenarios, incumbent.Holdout, learned.Holdout);
            PrintLeaderBreakdown(scenarios, incumbent.Holdout, learned.Holdout);
            PrintInvalidDetails(scenarios, learned.Holdout, "expert-v1");
            Console.WriteLine($"expert target decisions: eligible={learnedStats.eligible}; redirects={learnedStats.redirects}; " +
                              $"insufficient-data={learnedStats.insufficientData}");
            bool screen = rate >= .55 && invalid.candidateOnly == 0;
            Console.WriteLine($"55% TARGET: {(screen ? "REACHED IN SCREEN" : "NOT REACHED")} " +
                              $"({learned.Holdout.OutrightWins}/{scheduled} = {100 * rate:F1}%; " +
                              $"Wilson95 [{100 * interval.lo:F1}%, {100 * interval.hi:F1}%]; " +
                              $"paired {delta.better} better/{delta.worse} worse; learned-only invalid={invalid.candidateOnly}).");
            Console.WriteLine(delta.delta > 0 && invalid.candidateOnly == 0
                ? "PROVISIONAL LEARNED UPGRADE. Confirm on a fresh seed block before promotion."
                : "NO LEARNED UPGRADE. Keep contract-v2 and collect more expert replays before widening the prior.");
            return 0;
        }

        /// <summary>Fresh-seed, single-arm gate for the retained contract-v2 policy on the replay-matched
        /// high-bounty suite. This is intentionally separate from RunExpertCheck: a tied learned A/B should
        /// not double the compute cost of confirming the actual 55% competitive-strength claim.</summary>
        public static int RunExpertGate(DeckRegistry registry, int pairs = 30, int dop = 6,
            int seedOffset = 900000, string corpusDir = ExpertReplayCorpus.DefaultOutputDir)
        {
            var catalog = ExpertReplayCorpus.LoadCatalog(corpusDir);
            var pool = ExpertReplayCorpus.MatchedDecks(registry, catalog);
            if (pool.Count < 2)
            {
                Console.WriteLine("Need at least two replay-matched expert decks. Run expert-sync first.");
                return 1;
            }
            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var scenarios = BuildScenarios(pool, pool, contexts, pairs, 71031 + seedOffset,
                $"expert-gate:{seedOffset}", mirror: false);
            var retained = new Candidate
            {
                Name = "contract-v2", Weights = ValueFunction.DefaultWeights(), UseGreedyPolicy = true,
                UseAdvancedPolicy = true, AdvancedScope = "contract-v2",
            };
            string run = ProgressBoard.Start("expertgate",
                $"contract-v2 55% gate vs champion | replay-matched decks | n={pairs} | seed+={seedOffset}",
                scenarios.Count * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");
            try { retained.Holdout = Evaluate(retained, scenarios, 100, dop, run, "champion"); }
            finally { ProgressBoard.Finish(run); }

            int scheduled = retained.Holdout.Outcomes.Length;
            double rate = scheduled == 0 ? 0 : (double)retained.Holdout.OutrightWins / scheduled;
            var interval = Wilson95(retained.Holdout.OutrightWins, scheduled);
            Console.WriteLine("=== untouched-seed high-bounty competitive gate ===");
            Console.WriteLine($"pairs={pairs}/{scheduled} games; opponent=champion; seedOffset={seedOffset}; " +
                              "decklists=replay-matched validated proxies");
            PrintHoldout(retained, retained);
            PrintIdentityBreakdown(scenarios, retained.Holdout, retained.Holdout);
            PrintLeaderBreakdown(scenarios, retained.Holdout, retained.Holdout);
            PrintInvalidDetails(scenarios, retained.Holdout, "contract-v2");
            bool reaches = rate >= .55 && retained.Holdout.Invalid == 0;
            Console.WriteLine($"55% FRESH-SEED GATE: {(reaches ? "PASS" : "FAIL")} " +
                              $"({retained.Holdout.OutrightWins}/{scheduled} = {100 * rate:F1}%; " +
                              $"Wilson95 [{100 * interval.lo:F1}%, {100 * interval.hi:F1}%]; " +
                              $"invalid={retained.Holdout.Invalid}).");
            return reaches ? 0 : 2;
        }

        /// <summary>Exact directional matchup test. Direction A measures the honest Advanced bot piloting
        /// ownDeck into an opponent piloting opponentDeck; direction B reverses the decks. Every scenario is
        /// played with the candidate in both seats, so a directional deck result cannot be mistaken for seat
        /// or turn-order advantage.</summary>
        public static int RunMatchupCheck(DeckRegistry registry, string ownDeckId, string opponentDeckId,
            int pairs = 20, int dop = 8, int seedOffset = 0, string advancedScope = "contract-v2",
            string opponentKind = "champion")
        {
            if (!registry.Has(ownDeckId) || !registry.Has(opponentDeckId))
            {
                Console.WriteLine($"Unknown deck id(s): own={ownDeckId}, opponent={opponentDeckId}.");
                return 1;
            }
            var a = registry.Resolve(ownDeckId);
            var b = registry.Resolve(opponentDeckId);
            var candidate = new Candidate
            {
                Name = "honest-advanced", Weights = ValueFunction.DefaultWeights(), UseGreedyPolicy = true,
                UseAdvancedPolicy = true, AdvancedScope = advancedScope,
            };
            var ab = Enumerable.Range(0, pairs).Select(i => new Scenario
            {
                Own = a, Opp = b, Context = DeckFingerprint.Analyze(a),
                Seed = $"matchup:{seedOffset}:ab:{i}",
            }).ToList();
            var ba = Enumerable.Range(0, pairs).Select(i => new Scenario
            {
                Own = b, Opp = a, Context = DeckFingerprint.Analyze(b),
                Seed = $"matchup:{seedOffset}:ba:{i}",
            }).ToList();

            string abRun = ProgressBoard.Start("matchup-ab",
                $"Advanced {a.Id} vs {opponentKind} {b.Id} | n={pairs} | seed+={seedOffset}", pairs * 2);
            string baRun = ProgressBoard.Start("matchup-ba",
                $"Advanced {b.Id} vs {opponentKind} {a.Id} | n={pairs} | seed+={seedOffset}", pairs * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");
            Console.WriteLine($"A identity={ab[0].Context.Archetype}; B identity={ba[0].Context.Archetype}; " +
                              $"scope={advancedScope}; opponent={opponentKind}");

            EvalResult abr, bar;
            if (dop <= 1)
            {
                abr = Evaluate(candidate, ab, 100, 1, abRun, opponentKind);
                ProgressBoard.Finish(abRun);
                bar = Evaluate(candidate, ba, 100, 1, baRun, opponentKind);
                ProgressBoard.Finish(baRun);
            }
            else
            {
                int abDop = Math.Max(1, dop / 2), baDop = Math.Max(1, dop - Math.Max(1, dop / 2));
                var at = Task.Run(() =>
                {
                    try { return Evaluate(candidate, ab, 100, abDop, abRun, opponentKind); }
                    finally { ProgressBoard.Finish(abRun); }
                });
                var bt = Task.Run(() =>
                {
                    try { return Evaluate(candidate, ba, 100, baDop, baRun, opponentKind); }
                    finally { ProgressBoard.Finish(baRun); }
                });
                Task.WaitAll(at, bt); abr = at.Result; bar = bt.Result;
            }

            Console.WriteLine("=== exact directional honest matchup ===");
            PrintDirection(a, b, abr);
            PrintDirection(b, a, bar);
            int wins = abr.OutrightWins + bar.OutrightWins;
            int scheduled = abr.Outcomes.Length + bar.Outcomes.Length;
            int invalid = abr.Invalid + bar.Invalid;
            var pooled = Wilson95(wins, scheduled);
            Console.WriteLine($"pooled descriptive result: {wins}/{scheduled} = " +
                              $"{(scheduled == 0 ? 0 : 100.0 * wins / scheduled):F1}%; " +
                              $"Wilson95 [{100 * pooled.lo:F1}%, {100 * pooled.hi:F1}%]; invalid={invalid}");
            if (abr.Invalid == 0 && bar.Invalid == 0)
            {
                double ar = abr.Outcomes.Length == 0 ? 0 : (double)abr.OutrightWins / abr.Outcomes.Length;
                double br = bar.Outcomes.Length == 0 ? 0 : (double)bar.OutrightWins / bar.Outcomes.Length;
                Console.WriteLine(ar < br
                    ? $"harder direction: Advanced {a.Id} into {b.Id} ({100 * ar:F1}% vs {100 * br:F1}%)"
                    : br < ar
                        ? $"harder direction: Advanced {b.Id} into {a.Id} ({100 * br:F1}% vs {100 * ar:F1}%)"
                        : "directions tied in this block");
            }
            return invalid == 0 ? 0 : 2;
        }

        private static void PrintDirection(DeckDef own, DeckDef opp, EvalResult result)
        {
            int n = result.Outcomes.Length;
            var ci = Wilson95(result.OutrightWins, n);
            Console.WriteLine($"  Advanced {own.Id} -> {opp.Id}: {result.OutrightWins}/{n} = " +
                              $"{(n == 0 ? 0 : 100.0 * result.OutrightWins / n):F1}%; " +
                              $"Wilson95 [{100 * ci.lo:F1}%, {100 * ci.hi:F1}%]; " +
                              $"adjudicated={result.AdjudicatedGames}; invalid={result.Invalid}");
            for (int i = 0; i < result.Outcomes.Length; i++)
                if (result.Outcomes[i] < 0) Console.WriteLine($"    INVALID game={i}: {result.Failures?[i]}");
        }

        /// <summary>Symmetric honest Advanced self-play for one exact deck matchup. Each seed is played
        /// twice: deck A as south/first and deck B as south/first. Both sides use the same observation,
        /// determinization, policy family, budgets, and engine. This isolates the deck/policy interaction
        /// that a mixed Advanced-vs-Champion directional test cannot.</summary>
        public static int RunAdvancedSelfPlay(DeckRegistry registry, string deckAId, string deckBId,
            int pairs = 30, int dop = 8, int seedOffset = 0,
            string scopeA = "contract-v2", string scopeB = "contract-v2")
        {
            if (!registry.Has(deckAId) || !registry.Has(deckBId))
            {
                Console.WriteLine($"Unknown deck id(s): A={deckAId}, B={deckBId}.");
                return 1;
            }
            var deckA = registry.Resolve(deckAId);
            var deckB = registry.Resolve(deckBId);
            var ctxA = DeckFingerprint.Analyze(deckA);
            var ctxB = DeckFingerprint.Analyze(deckB);
            var outcomes = Enumerable.Repeat(-1, pairs * 2).ToArray(); // 1=A win, 0=B win, -1=invalid
            var adjudicated = new bool[pairs * 2];
            var failures = new string[pairs * 2];
            AdvancedActivationPolicy.ResetStats();
            AdvancedPressurePolicy.ResetStats();
            AdvancedRolloutPolicy.ResetStats();
            string run = ProgressBoard.Start("advanced-selfplay",
                $"Advanced {deckA.Id} vs Advanced {deckB.Id} | pairs={pairs} | seed+={seedOffset}", pairs * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");
            Console.WriteLine($"A identity={ctxA.Archetype}, scope={scopeA}; " +
                              $"B identity={ctxB.Archetype}, scope={scopeB}");

            Parallel.For(0, pairs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, dop) }, i =>
            {
                var first = PlayAdvancedSelfGame(deckA, deckB, ctxA, ctxB,
                    deckASouth: true, $"advanced-self:{seedOffset}:{i}:a-first", scopeA, scopeB);
                outcomes[i * 2] = first.outcome; adjudicated[i * 2] = first.adjudicated;
                failures[i * 2] = first.failure; ProgressBoard.Tick(run, first.outcome);

                var second = PlayAdvancedSelfGame(deckA, deckB, ctxA, ctxB,
                    deckASouth: false, $"advanced-self:{seedOffset}:{i}:b-first", scopeA, scopeB);
                outcomes[i * 2 + 1] = second.outcome; adjudicated[i * 2 + 1] = second.adjudicated;
                failures[i * 2 + 1] = second.failure; ProgressBoard.Tick(run, second.outcome);
            });
            ProgressBoard.Finish(run);

            int valid = outcomes.Count(x => x >= 0);
            int aWins = outcomes.Count(x => x == 1);
            int bWins = outcomes.Count(x => x == 0);
            int invalid = outcomes.Count(x => x < 0);
            int aFirstWins = Enumerable.Range(0, pairs).Count(i => outcomes[i * 2] == 1);
            int aSecondWins = Enumerable.Range(0, pairs).Count(i => outcomes[i * 2 + 1] == 1);
            int adjudicatedGames = adjudicated.Count(x => x);
            var ci = Wilson95(aWins, valid);
            Console.WriteLine("=== symmetric honest Advanced self-play ===");
            Console.WriteLine($"{deckA.Id} wins: {aWins}/{valid} = {(valid == 0 ? 0 : 100.0 * aWins / valid):F1}%; " +
                              $"Wilson95 [{100 * ci.lo:F1}%, {100 * ci.hi:F1}%]");
            Console.WriteLine($"{deckB.Id} wins: {bWins}/{valid} = {(valid == 0 ? 0 : 100.0 * bWins / valid):F1}%");
            Console.WriteLine($"seat/turn-order split for {deckA.Id}: first={aFirstWins}/{pairs}, " +
                              $"second={aSecondWins}/{pairs}; adjudicated={adjudicatedGames}; invalid={invalid}");
            var activation = AdvancedActivationPolicy.Stats();
            var pressure = AdvancedPressurePolicy.Stats();
            var rollout = AdvancedRolloutPolicy.Stats();
            Console.WriteLine($"policy activity: activations={activation.activations}/{activation.opportunities}; " +
                              $"pressure={pressure.redirects}/{pressure.attacks}; " +
                              $"rollout deviations={rollout.deviations}/{rollout.decisions}");
            for (int i = 0; i < outcomes.Length; i++)
                if (outcomes[i] < 0) Console.WriteLine($"  INVALID game={i}: {failures[i]}");
            return invalid == 0 ? 0 : 2;
        }

        /// <summary>Paired A/B of the dedicated battle-Trigger evaluator (TriggerUtilityPolicy vs rollout).</summary>
        public static int RunTriggerPolicyAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "trigger-policy", "the Trigger use/hold decision",
                on => HonestPlannerBot.TriggerPolicyEnabled = on);

        /// <summary>Paired A/B of the WS-3 removal-capability targeting model (RemovalModel vs generic ranking).</summary>
        public static int RunRemovalModelAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "removal-model", "an effect-target selection",
                on => OnePieceTcg.Engine.Bot.IntermediateBot.RemovalModelEnabled = on);

        /// <summary>Paired A/B of the trash-recursion model (value the trash as recur fuel for recursion decks).</summary>
        public static int RunTrashAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "trash-recursion", "a trash-as-resource valuation",
                on => ValueFunction.TrashRecursionAware = on);

        /// <summary>Paired A/B of the effect-threat model (value engines/auras, not just stats).</summary>
        public static int RunThreatModelAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "threat-model", "an effect-based target-value decision",
                on => OnePieceTcg.Engine.Bot.IntermediateBot.ThreatModelEnabled = on);

        /// <summary>Paired A/B of the sacrifice-to-recur unlock (trash a small body to deploy a bigger one
        /// from trash: Yamato 6→8, Momonosuke 5→9, …). Previously a silently-disabled move class.</summary>
        public static int RunSacRecurAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "sac-recur", "a trash-this-to-recur-a-bigger-body activation",
                on => OnePieceTcg.Sim.Planning.AdvancedActivationPolicy.SacrificeRecurEnabled = on);

        /// <summary>Paired A/B of the restand engine (Zoro-style multi-attack via "set this Leader active").</summary>
        public static int RunRestandAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "restand", "a leader-restand multi-attack",
                on => OnePieceTcg.Engine.Bot.IntermediateBot.RestandEngineEnabled = on);

        /// <summary>Paired A/B of the lethal/desperation pivot (all-in when dead next turn).</summary>
        public static int RunLethalPivotAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "lethal-pivot", "a near-certain-death all-in decision",
                on => OnePieceTcg.Engine.Bot.IntermediateBot.LethalPivotEnabled = on);

        /// <summary>Paired A/B of the proactive [DON!! xN] leader-engine setup (Luffy-style activation).</summary>
        public static int RunLeaderSetupAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "leader-setup", "a [DON!! xN] leader activation",
                on => AdvancedActivationPolicy.ProactiveLeaderSetup = on);

        /// <summary>Paired A/B of the WS-1 opportunity-cost hold model (use-Main vs hold-for-Counter).</summary>
        public static int RunOpportunityAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "opportunity", "a use-Main vs hold-for-Counter decision",
                on => OnePieceTcg.Engine.Bot.IntermediateBot.OpportunityHoldEnabled = on);

        /// <summary>Paired A/B of the WS-2 cost-combo targeting (value −cost by the KO-by-cost it unlocks).</summary>
        public static int RunCostComboAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "cost-combo", "a −cost→K.O. targeting decision",
                on => OnePieceTcg.Engine.Bot.IntermediateBot.CostComboAware = on);

        /// <summary>Paired A/B of the WS-1 DON-engine scoring (DON as fuel vs generic DON scoring).</summary>
        public static int RunDonEngineAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset) =>
            RunPolicyAB(registry, deckAId, deckBId, pairs, dop, seedOffset,
                "don-engine", "a DON-engine deck's DON-spending decision",
                on => ValueFunction.DonEngineAware = on);

        /// <summary>Generic paired A/B: play every seed twice with <paramref name="setFlag"/>(true) then
        /// (false), both sides on contract-v2. Identical seeds isolate the flag, so any outcome difference is
        /// caused by the toggled behaviour. Reports each arm's win rate, how many games the flag actually
        /// moved, and the direction of the flips (McNemar) — so we can see whether it helps, hurts, or is neutral.</summary>
        private static int RunPolicyAB(DeckRegistry registry, string deckAId, string deckBId,
            int pairs, int dop, int seedOffset, string tag, string what, Action<bool> setFlag)
        {
            if (!registry.Has(deckAId) || !registry.Has(deckBId))
            {
                Console.WriteLine($"Unknown deck id(s): A={deckAId}, B={deckBId}.");
                return 1;
            }
            var deckA = registry.Resolve(deckAId);
            var deckB = registry.Resolve(deckBId);
            var ctxA = DeckFingerprint.Analyze(deckA);
            var ctxB = DeckFingerprint.Analyze(deckB);
            int g = pairs * 2;

            int[] Play(bool policyOn, string arm)
            {
                setFlag(policyOn);
                var outc = Enumerable.Repeat(-1, g).ToArray();
                string run = ProgressBoard.Start($"ab-{tag}-{arm}",
                    $"A/B {tag} {arm.ToUpperInvariant()} | {deckA.Id} vs {deckB.Id} | pairs={pairs}", g);
                Parallel.For(0, pairs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, dop) }, i =>
                {
                    // Seeds are shared across arms so the ONLY difference is the toggled behaviour.
                    var first = PlayAdvancedSelfGame(deckA, deckB, ctxA, ctxB, deckASouth: true,
                        $"ab-{tag}:{seedOffset}:{i}:a-first", "contract-v2", "contract-v2");
                    outc[i * 2] = first.outcome; ProgressBoard.Tick(run, first.outcome);
                    var second = PlayAdvancedSelfGame(deckA, deckB, ctxA, ctxB, deckASouth: false,
                        $"ab-{tag}:{seedOffset}:{i}:b-first", "contract-v2", "contract-v2");
                    outc[i * 2 + 1] = second.outcome; ProgressBoard.Tick(run, second.outcome);
                });
                ProgressBoard.Finish(run);
                return outc;
            }

            var on = Play(true, "on");
            var off = Play(false, "off");
            setFlag(true); // restore shipped default

            int bothValid = 0, aWinsOn = 0, aWinsOff = 0, moved = 0, towardOn = 0, towardOff = 0;
            int invalidOn = on.Count(x => x < 0), invalidOff = off.Count(x => x < 0);
            for (int i = 0; i < g; i++)
            {
                if (on[i] < 0 || off[i] < 0) continue;
                bothValid++;
                if (on[i] == 1) aWinsOn++;
                if (off[i] == 1) aWinsOff++;
                if (on[i] != off[i])
                {
                    moved++;
                    if (on[i] == 1) towardOn++;   // A won only with the flag on
                    else towardOff++;             // A won only with the flag off
                }
            }
            var ciOn = Wilson95(aWinsOn, bothValid);
            var ciOff = Wilson95(aWinsOff, bothValid);
            Console.WriteLine($"=== A/B: {tag} ON vs OFF (paired seeds) ===");
            Console.WriteLine($"decks: A={deckA.Id} ({ctxA.Archetype}) vs B={deckB.Id} ({ctxB.Archetype}); " +
                              $"pairs={pairs}, seed+={seedOffset}");
            Console.WriteLine($"flag ON : A wins {aWinsOn}/{bothValid} = {Pct(aWinsOn, bothValid)}%  " +
                              $"Wilson95 [{100 * ciOn.lo:F1}%, {100 * ciOn.hi:F1}%]  (invalid {invalidOn})");
            Console.WriteLine($"flag OFF: A wins {aWinsOff}/{bothValid} = {Pct(aWinsOff, bothValid)}%  " +
                              $"Wilson95 [{100 * ciOff.lo:F1}%, {100 * ciOff.hi:F1}%]  (invalid {invalidOff})");
            Console.WriteLine($"seeds the flag MOVED: {moved}/{bothValid}  " +
                              $"(A-win only-ON={towardOn}, A-win only-OFF={towardOff}); " +
                              $"net A-win delta from flag = {aWinsOn - aWinsOff:+0;-0;0}");
            if (moved == 0)
                Console.WriteLine($"NOTE: the flag changed no outcomes here — either {what} was rare/irrelevant " +
                                  "in this matchup, or the two behaviours agreed on every one that mattered.");
            return (invalidOn == 0 && invalidOff == 0) ? 0 : 2;
        }

        private static string Pct(int n, int d) => (d == 0 ? 0 : 100.0 * n / d).ToString("F1");

        /// <summary>WS-1 play-trace: play a few games with the advanced bot piloting a DON-engine deck and
        /// report what it ACTUALLY does with its engine — leader activations, DON-minus events played vs left
        /// in hand, and where distributed DON!! is routed. This finds the sequencing/generation bottleneck the
        /// static-eval A/B could not (it moved 0 games), so the next fix targets real behaviour, not a guess.</summary>
        public static int RunEnelDiagnostic(DeckRegistry registry, string engineId, string oppId, int games, int seedOffset)
        {
            if (!registry.Has(engineId) || !registry.Has(oppId))
            {
                Console.WriteLine($"Unknown deck id(s): {engineId}, {oppId}."); return 1;
            }
            var engineDeck = registry.Resolve(engineId);
            var oppDeck = registry.Resolve(oppId);
            var ctxE = DeckFingerprint.Analyze(engineDeck);
            var ctxO = DeckFingerprint.Analyze(oppDeck);
            Console.WriteLine($"=== DON-engine play-trace: {engineDeck.Id} ({DeckFingerprint.Describe(ctxE)}) ===");

            bool IsDonMinusEvent(string cardId)
            {
                var d = CardData.GetCard(cardId);
                return d != null && d.Type == "event"
                    && System.Text.RegularExpressions.Regex.IsMatch(
                        ((d.Effect ?? "") + " " + (d.Trigger ?? "")).ToLowerInvariant(), @"don!!\s*[−-]\s*\d");
            }
            int donMinusInDeck = engineDeck.List.Where(kv => IsDonMinusEvent(kv.Item1)).Sum(kv => kv.Item2);

            int gTurns = 0, gActivations = 0, gLeaderAct = 0, gPlays = 0, gEvents = 0, gDonMinusPlayed = 0;
            int gAttach = 0, gAttachAmt = 0, gAttachLeader = 0, gAttachChar = 0, gRemovalActions = 0;
            int gHeldDonMinus = 0, gDeckDonMinus = 0, gWins = 0, valid = 0;
            int gRemHand = 0, gRemAct = 0, gTrigUse = 0, gTrigPass = 0, gRemHeldHand = 0;
            int gLeaderCharAtk = 0, gLeaderFaceAtk = 0;
            int gRecur = 0, gLifeAdd = 0, gDonRamp = 0, gFreeze = 0;
            void ClassifyEngine(string cardId)
            {
                var d = CardData.GetCard(cardId);
                if (d == null) return;
                string e = ((d.Effect ?? "") + " " + (d.Trigger ?? "")).ToLowerInvariant();
                if (e.Contains("from your trash")) gRecur++;
                if (e.Contains("to the top of your life") || (e.Contains("add") && e.Contains("life card"))) gLifeAdd++;
                if (e.Contains("add") && e.Contains("don!! card") && e.Contains("don!! deck")) gDonRamp++;
                if (e.Contains("will not become active")) gFreeze++;
            }
            bool IsRemoval(string cardId)
            {
                var d = CardData.GetCard(cardId);
                return d != null && OnePieceTcg.Engine.Bot.Search.RemovalModel.Classify(d.Effect)
                    != OnePieceTcg.Engine.Bot.Search.RemovalKind.None;
            }
            bool IsRemovalCardInHand(CardInstance c) =>
                c != null && IsRemoval(c.CardId)
                && (CardData.GetCard(c.CardId)?.Type == "event" || (GameEngine.GetCard(c)?.Effect ?? "").ToLowerInvariant().Contains("[activate: main]"));
            // Removal cards the deck actually runs (events + Activate:Main abilities that remove) — the denominator.
            int removalCardsInDeck = engineDeck.List.Where(kv =>
                { var d = CardData.GetCard(kv.Item1); return d != null && OnePieceTcg.Engine.Bot.Search.RemovalModel.Classify(d.Effect)
                    != OnePieceTcg.Engine.Bot.Search.RemovalKind.None; }).Sum(kv => kv.Item2);

            for (int i = 0; i < games; i++)
            {
                bool engineFirst = i % 2 == 0;
                var opt = new TurnPlanner.Options { BeamWidth = 8, MaxDepth = 12, NodeBudget = 100, WorkBudget = 30000 };
                var botE = new HonestPlannerBot(engineDeck, oppDeck, ValueFunction.DefaultWeights(), ctxE,
                    opt, engineFirst, kWorlds: 1, useGreedyPolicy: true, useAdvancedPolicy: true,
                    advancedScope: "contract-v2", name: "engine");
                var botO = new HonestPlannerBot(oppDeck, engineDeck, ValueFunction.DefaultWeights(), ctxO,
                    opt, !engineFirst, kWorlds: 1, useGreedyPolicy: true, useAdvancedPolicy: true,
                    advancedScope: "contract-v2", name: "opp");
                GameState st;
                try { st = GameRunner.Play(botE, botO, engineDeck, oppDeck, $"enel-diag:{seedOffset}:{i}", "south", 20000, 60); }
                catch (Exception ex) { Console.WriteLine($"  game {i}: threw {ex.GetType().Name}"); continue; }
                if (GameRunner.Result(st) == null) { Console.WriteLine($"  game {i}: no winner"); continue; }
                valid++;
                if (GameRunner.Result(st) == "south") gWins++;

                // instanceId -> CardId across every zone of the engine seat (cards keep their id as they move).
                var me = st.Players["south"];
                var map = new System.Collections.Generic.Dictionary<string, string>();
                void Index(System.Collections.Generic.IEnumerable<CardInstance> zone) { foreach (var c in zone) if (c != null) map[c.InstanceId] = c.CardId; }
                Index(me.Hand); Index(me.Deck); Index(me.Trash); Index(me.Life); Index(me.CharacterArea);
                if (me.Leader != null) map[me.Leader.InstanceId] = me.Leader.CardId;
                var opp = st.Players["north"];
                string oppLeaderId = opp.Leader?.InstanceId;
                // Include KO'd (trash) and bounced (hand) bodies — a character the leader attacked is often
                // gone from the board by game end, so the final board alone undercounts leader-vs-character.
                var oppCharIds = new System.Collections.Generic.HashSet<string>(
                    opp.CharacterArea.Concat(opp.Trash).Concat(opp.Hand)
                        .Where(c => c != null && CardData.GetCard(c.CardId)?.Type == "character")
                        .Select(c => c.InstanceId));
                string myLeaderId = me.Leader?.InstanceId;

                foreach (var cmd in st.CommandHistory)
                {
                    if (cmd.Seat != "south") continue;
                    switch (cmd.Type)
                    {
                        case "endTurn": gTurns++; break;
                        case "useTrigger": gTrigUse++; break;
                        case "passTrigger": gTrigPass++; break;
                        case "declareAttack":
                            if (cmd.Attacker != null && cmd.Attacker == myLeaderId)
                            {
                                if (cmd.Target == oppLeaderId) gLeaderFaceAtk++;
                                else if (cmd.Target != null && oppCharIds.Contains(cmd.Target)) gLeaderCharAtk++;
                            }
                            break;
                        case "activateMain":
                            gActivations++;
                            if (cmd.Target != null && me.Leader != null && cmd.Target == me.Leader.InstanceId) gLeaderAct++;
                            if (cmd.Target != null && map.TryGetValue(cmd.Target, out var acid) && IsRemoval(acid)) { gRemovalActions++; gRemAct++; }
                            if (cmd.Target != null && map.TryGetValue(cmd.Target, out var acid2)) ClassifyEngine(acid2);
                            break;
                        case "attachDon":
                            gAttach++; gAttachAmt += cmd.Amount ?? 0;
                            if (cmd.Target != null && me.Leader != null && cmd.Target == me.Leader.InstanceId) gAttachLeader++;
                            else gAttachChar++;
                            break;
                        case "playCard":
                            gPlays++;
                            if (map.TryGetValue(cmd.InstanceId ?? "", out var cid))
                            {
                                var d = CardData.GetCard(cid);
                                if (d?.Type == "event") gEvents++;
                                if (IsDonMinusEvent(cid)) gDonMinusPlayed++;
                                if (IsRemoval(cid)) { gRemovalActions++; gRemHand++; }
                                ClassifyEngine(cid);
                            }
                            break;
                    }
                }
                gHeldDonMinus += me.Hand.Count(c => c != null && IsDonMinusEvent(c.CardId));
                gDeckDonMinus += me.Deck.Count(c => c != null && IsDonMinusEvent(c.CardId));
                gRemHeldHand += me.Hand.Count(IsRemovalCardInHand);
            }

            if (valid == 0) { Console.WriteLine("no valid games"); return 2; }
            double per = 1.0 / valid;
            Console.WriteLine($"games={valid}  engine wins={gWins}/{valid}");
            Console.WriteLine($"per game: turns={gTurns * per:F1}  activateMain total={gActivations * per:F1}  " +
                              $"LEADER activations={gLeaderAct * per:F1} ({(gTurns > 0 ? (double)gLeaderAct / gTurns : 0):P0} of turns)");
            Console.WriteLine($"per game: total plays={gPlays * per:F1}  events played={gEvents * per:F1}  " +
                              $"DON-minus events PLAYED={gDonMinusPlayed * per:F1}");
            Console.WriteLine($"per game: REMOVAL total={gRemovalActions * per:F1}  " +
                              $"[from HAND events={gRemHand * per:F1}, from activations={gRemAct * per:F1}]  " +
                              $"removal left UNUSED in hand at end={gRemHeldHand * per:F1}  (deck runs {removalCardsInDeck} removal cards)");
            Console.WriteLine($"per game: TRIGGER decisions — useTrigger={gTrigUse * per:F1}, passTrigger={gTrigPass * per:F1}");
            Console.WriteLine($"per game: LEADER attacks — on CHARACTERS={gLeaderCharAtk * per:F1} (restand precondition), on FACE={gLeaderFaceAtk * per:F1}");
            Console.WriteLine($"per game: ENGINE signals — recursion(from trash)={gRecur * per:F1}, life-add={gLifeAdd * per:F1}, DON-ramp={gDonRamp * per:F1}, freeze={gFreeze * per:F1}");
            Console.WriteLine($"per game: DON-minus events left in HAND at end={gHeldDonMinus * per:F1}  " +
                              $"still in deck={gDeckDonMinus * per:F1}  (deck runs {donMinusInDeck} copies)");
            Console.WriteLine($"per game: attachDon cmds={gAttach * per:F1}  DON routed={gAttachAmt * per:F1}  " +
                              $"(to leader={gAttachLeader * per:F1}, to characters={gAttachChar * per:F1})");
            return 0;
        }

        private static (int outcome, bool adjudicated, string failure) PlayAdvancedSelfGame(
            DeckDef deckA, DeckDef deckB, DeckContext ctxA, DeckContext ctxB, bool deckASouth,
            string seed, string scopeA, string scopeB)
        {
            try
            {
                var optA = new TurnPlanner.Options { BeamWidth = 8, MaxDepth = 12, NodeBudget = 100, WorkBudget = 30000 };
                var optB = new TurnPlanner.Options { BeamWidth = 8, MaxDepth = 12, NodeBudget = 100, WorkBudget = 30000 };
                var botA = new HonestPlannerBot(deckA, deckB, weights: ValueFunction.DefaultWeights(), ctx: ctxA,
                    opt: optA, goFirst: deckASouth, kWorlds: 1, useGreedyPolicy: true,
                    useAdvancedPolicy: true, advancedScope: scopeA, name: "advanced-a");
                var botB = new HonestPlannerBot(deckB, deckA, weights: ValueFunction.DefaultWeights(), ctx: ctxB,
                    opt: optB, goFirst: !deckASouth, kWorlds: 1, useGreedyPolicy: true,
                    useAdvancedPolicy: true, advancedScope: scopeB, name: "advanced-b");
                GameState st;
                string aSeat;
                if (deckASouth)
                {
                    aSeat = "south";
                    st = GameRunner.Play(botA, botB, deckA, deckB, seed, "south", 20000, 60);
                }
                else
                {
                    aSeat = "north";
                    st = GameRunner.Play(botB, botA, deckB, deckA, seed, "south", 20000, 60);
                }
                if (GameRunner.Stuck)
                    return (-1, false, $"stuck status={st.Status} turn={st.TurnNumber} phase={st.Phase}");
                string winner = GameRunner.Result(st);
                if (winner == null) return (-1, false, $"no winner status={st.Status} turn={st.TurnNumber}");
                bool adj = GameRunner.Winner(st) == null;
                return (winner == aSeat ? 1 : 0, adj, null);
            }
            catch (Exception ex)
            {
                return (-1, false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Replays only the advanced arm of a fixed all-deck mirror block and stops after the first
        /// invalid. This is diagnostic only: it cannot replace or amend the pre-registered strength result.</summary>
        public static int RunAdvancedInvalidReplay(DeckRegistry registry, int pairs = 150,
            int dop = 8, int seedOffset = 1180000, string advancedScope = "contract-v2")
        {
            var pool = CleanMeta(registry);
            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var scenarios = BuildScenarios(pool, pool, contexts, pairs, 51031 + seedOffset,
                $"advanced:{seedOffset}", mirror: true);
            var candidate = new Candidate
            {
                Name = "honest-rollout", Weights = ValueFunction.DefaultWeights(), UseGreedyPolicy = true,
                UseAdvancedPolicy = true, AdvancedScope = advancedScope,
            };
            int found = 0, completed = 0;
            object reportLock = new object();
            string run = ProgressBoard.Start("invalid-replay",
                $"invalid replay {advancedScope} | n={pairs} | seed+={seedOffset}", scenarios.Count * 2);
            Console.WriteLine($"progress dashboard: {ProgressBoard.DashboardPath}");
            Parallel.For(0, scenarios.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, dop) },
                (i, loop) =>
                {
                    if (Volatile.Read(ref found) != 0) { loop.Stop(); return; }
                    var s = scenarios[i];
                    foreach (bool south in new[] { true, false })
                    {
                        var r = PlayOne(s, candidate, 100, south);
                        Interlocked.Increment(ref completed);
                        ProgressBoard.Tick(run, r.outcome);
                        if (r.outcome < 0 && Interlocked.CompareExchange(ref found, 1, 0) == 0)
                        {
                            lock (reportLock)
                            {
                                Console.WriteLine($"REPRODUCED INVALID: scenario={i} gameIndex={i * 2 + (south ? 0 : 1)} " +
                                    $"deck={s.Own.Id} candidateSeat={(south ? "south" : "north")} " +
                                    $"seed={s.Seed}:{(south ? "first" : "second")} cause={r.failure}");
                            }
                            loop.Stop();
                            break;
                        }
                    }
                });
            ProgressBoard.Finish(run);
            Console.WriteLine(found != 0
                ? $"INVALID REPLAY: reproduced after {completed}/{scenarios.Count * 2} games."
                : $"INVALID REPLAY: no invalid reproduced in {completed}/{scenarios.Count * 2} games.");
            return found != 0 ? 1 : 0;
        }

        public static int RunAdvancedReplayOne(DeckRegistry registry, int scenarioIndex = 96,
            int seedOffset = 1180000, string advancedScope = "contract-v2", bool candidateSouth = true)
        {
            var pool = CleanMeta(registry);
            var contexts = pool.ToDictionary(d => d.Id, DeckFingerprint.Analyze);
            var scenarios = BuildScenarios(pool, pool, contexts, scenarioIndex + 1, 51031 + seedOffset,
                $"advanced:{seedOffset}", mirror: true);
            var candidate = new Candidate
            {
                Name = "honest-rollout", Weights = ValueFunction.DefaultWeights(), UseGreedyPolicy = true,
                UseAdvancedPolicy = true, AdvancedScope = advancedScope,
            };
            var s = scenarios[scenarioIndex];
            Console.WriteLine($"replay one: scenario={scenarioIndex} deck={s.Own.Id} " +
                $"seat={(candidateSouth ? "south" : "north")} seed={s.Seed}:{(candidateSouth ? "first" : "second")}");
            GameRunner.EnforceCardConservation = true;
            (int outcome, bool adjudicated, string failure) r;
            try { r = PlayOne(s, candidate, 100, candidateSouth); }
            finally { GameRunner.EnforceCardConservation = false; }
            Console.WriteLine($"outcome={r.outcome} adjudicated={r.adjudicated} cause={r.failure ?? "none"}");
            return r.outcome < 0 ? 1 : 0;
        }

        private static List<Candidate> BuildCandidates()
        {
            var baseW = ValueFunction.DefaultWeights();
            var list = new List<Candidate> { new Candidate { Name = "default", Weights = baseW } };

            list.Add(new Candidate { Name = "race-mild", Weights = Multiply(baseW,
                ("MyBoardPow", .75), ("OppBoardPow", .75), ("OppLife", 1.5),
                ("FinishPressure", 1.5), ("LethalProximity", 1.5)) });
            list.Add(new Candidate { Name = "race-strong", Weights = Multiply(baseW,
                ("MyBoardPow", .5), ("OppBoardPow", .5), ("OppLife", 2.0),
                ("FinishPressure", 2.0), ("LethalProximity", 2.0)) });

            // Deterministic mutations in the small subspace that controls racing versus board grinding.
            // Signs are preserved; only relative emphasis changes. This avoids another 25-gene global
            // random walk whose signal is drowned by archetypes that want opposite play.
            var genes = new[] { "OppLife", "MyBoardPow", "OppBoardPow", "OppRested",
                "MyActiveAtk", "FinishPressure", "LethalProximity", "MyDoubleAttackers" };
            var rng = new Random(20260716);
            for (int m = 0; m < 5; m++)
            {
                var w = (double[])baseW.Clone();
                foreach (var gene in genes)
                {
                    int i = Array.IndexOf(ValueFunction.Names, gene);
                    double logFactor = (rng.NextDouble() * 2.0 - 1.0) * 0.70;
                    w[i] *= Math.Exp(logFactor); // roughly 0.50x .. 2.01x
                }
                list.Add(new Candidate { Name = $"mutation-{m + 1}", Weights = w });
            }
            // A policy-family mutation: for aggro, test the existing competent rule policy through the
            // exact same honest determinization boundary. This changes decision logic rather than trying
            // another weight vector in a search evaluator that showed no measurable local gradient.
            list.Add(new Candidate { Name = "honest-greedy", Weights = baseW, UseGreedyPolicy = true });
            return list;
        }

        private static List<Scenario> BuildScenarios(List<DeckDef> ownPool, List<DeckDef> all,
            Dictionary<string, DeckContext> contexts, int count, int seed, string phase, bool mirror)
        {
            var rng = new Random(seed);
            var result = new List<Scenario>(count);
            for (int i = 0; i < count; i++)
            {
                // Round-robin the target archetype so a small run cannot accidentally test one leader only.
                var own = ownPool[(i + seed) % ownPool.Count];
                // Mirror the deck. Cross-archetype deck strength drove the first pilot to a saturated 9%
                // win rate where almost every mutation lost the same games. Mirroring removes deck advantage
                // and measures the thing being mutated: how well the honest planner pilots this archetype.
                var opp = mirror ? own : all[rng.Next(all.Count)];
                result.Add(new Scenario { Own = own, Opp = opp, Context = contexts[own.Id], Seed = $"mut:{phase}:{i}" });
            }
            return result;
        }

        private static EvalResult Evaluate(Candidate candidate, List<Scenario> scenarios, int nodeBudget, int dop,
            string progressRunId = null, string opponentKind = "baseline")
        {
            var outcomes = Enumerable.Repeat(-1, scenarios.Count * 2).ToArray();
            var adjudicated = new bool[scenarios.Count * 2];
            var failures = new string[scenarios.Count * 2];
            Parallel.For(0, scenarios.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, dop) }, i =>
            {
                var s = scenarios[i];
                var first = PlayOne(s, candidate, nodeBudget, candidateSouth: true, opponentKind);
                ProgressBoard.Tick(progressRunId, first.outcome);
                var second = PlayOne(s, candidate, nodeBudget, candidateSouth: false, opponentKind);
                ProgressBoard.Tick(progressRunId, second.outcome);
                outcomes[i * 2] = first.outcome; adjudicated[i * 2] = first.adjudicated;
                failures[i * 2] = first.failure;
                outcomes[i * 2 + 1] = second.outcome; adjudicated[i * 2 + 1] = second.adjudicated;
                failures[i * 2 + 1] = second.failure;
            });
            return new EvalResult { Outcomes = outcomes, Adjudicated = adjudicated, Failures = failures };
        }

        private static (int outcome, bool adjudicated, string failure) PlayOne(
            Scenario s, Candidate candidate, int nodeBudget, bool candidateSouth, string opponentKind = "baseline")
        {
            try
            {
                var opt = new TurnPlanner.Options { BeamWidth = 8, MaxDepth = 12, NodeBudget = nodeBudget,
                    WorkBudget = Math.Max(30000, nodeBudget * 50) };
                var honest = new HonestPlannerBot(s.Own, s.Opp, weights: candidate.Weights, ctx: s.Context,
                    opt: opt, goFirst: candidateSouth, kWorlds: 1,
                    useGreedyPolicy: candidate.UseGreedyPolicy, useAdvancedPolicy: candidate.UseAdvancedPolicy,
                    advancedScope: candidate.AdvancedScope, name: candidate.Name,
                    expertModel: candidate.ExpertModel);
                IAgent baseline = opponentKind?.ToLowerInvariant() switch
                {
                    "aggro" => new AggroAgent(),
                    "conservative" => new ConservativeAgent(),
                    "champion" => new Learning.WeightedAgent(Learning.BotTiers.IntermediateGenome(), "champion"),
                    _ => new BaselineAgent(),
                };
                GameState st;
                string candidateSeat;
                if (candidateSouth)
                {
                    candidateSeat = "south";
                    st = GameRunner.Play(honest, baseline, s.Own, s.Opp, s.Seed + ":first", "south", 20000, 60);
                }
                else
                {
                    candidateSeat = "north";
                    st = GameRunner.Play(baseline, honest, s.Opp, s.Own, s.Seed + ":second", "south", 20000, 60);
                }
                if (GameRunner.Stuck)
                    return (-1, false, $"stuck status={st.Status} turn={st.TurnNumber} phase={st.Phase} " +
                        $"battle={st.Battle?.Step ?? "none"} pending={st.PendingEffects.Count} " +
                        $"choice={(st.ActiveChoice == null ? "none" : st.ActiveChoice.Seat)} " +
                        $"deckLook={(st.DeckLook == null ? "none" : st.DeckLook.Seat)}");
                string winner = GameRunner.Result(st);
                if (winner == null) return (-1, false, $"no winner status={st.Status} turn={st.TurnNumber}");
                bool adj = GameRunner.Winner(st) == null;
                return (winner == candidateSeat ? 1 : 0, adj, null);
            }
            catch (Exception ex)
            {
                return (-1, false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void PrintLeaderBreakdown(List<Scenario> scenarios, EvalResult floor, EvalResult candidate)
        {
            Console.WriteLine("leader breakdown (candidate vs floor):");
            foreach (var g in scenarios.Select((s, i) => (s, i)).GroupBy(x => x.s.Own.Id).OrderBy(x => x.Key))
            {
                var indexes = g.SelectMany(x => new[] { x.i * 2, x.i * 2 + 1 }).ToArray();
                int cw = indexes.Count(i => candidate.Outcomes[i] == 1), cd = indexes.Count(i => candidate.Outcomes[i] >= 0);
                int fw = indexes.Count(i => floor.Outcomes[i] == 1), fd = indexes.Count(i => floor.Outcomes[i] >= 0);
                int better = indexes.Count(i => candidate.Outcomes[i] == 1 && floor.Outcomes[i] == 0);
                int worse = indexes.Count(i => candidate.Outcomes[i] == 0 && floor.Outcomes[i] == 1);
                int inv = indexes.Count(i => candidate.Outcomes[i] < 0);
                Console.WriteLine($"  {g.Key,-24} {cw}/{cd} vs {fw}/{fd}; paired {better} better/{worse} worse; invalid={inv}");
            }
        }

        private static void PrintInvalidDetails(List<Scenario> scenarios, EvalResult result, string label)
        {
            for (int i = 0; i < result.Outcomes.Length; i++)
            {
                if (result.Outcomes[i] >= 0) continue;
                var s = scenarios[i / 2];
                string seat = i % 2 == 0 ? "south" : "north";
                Console.WriteLine($"  INVALID {label}: gameIndex={i} deck={s.Own.Id} opponent={s.Opp.Id} " +
                    $"candidateSeat={seat} seed={s.Seed}:{(seat == "south" ? "first" : "second")} " +
                    $"cause={result.Failures?[i] ?? "unknown"}");
            }
        }

        private static (double lo, double hi) Wilson95(int wins, int n)
        {
            if (n <= 0) return (0, 0);
            const double z = 1.959963984540054;
            double p = (double)wins / n, z2 = z * z;
            double center = (p + z2 / (2 * n)) / (1 + z2 / n);
            double half = z * Math.Sqrt((p * (1 - p) + z2 / (4 * n)) / n) / (1 + z2 / n);
            return (Math.Max(0, center - half), Math.Min(1, center + half));
        }

        private static List<DeckDef> CleanMeta(DeckRegistry registry) => registry.Ids
            .Where(i => !CardData.StarterDecks.ContainsKey(i))
            .Select(registry.Resolve)
            .Where(d => d != null && d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty) == 50
                        && d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max() <= 4)
            .OrderBy(d => d.Id).ToList();

        private static double[] Multiply(double[] source, params (string name, double factor)[] changes)
        {
            var w = (double[])source.Clone();
            foreach (var (name, factor) in changes) w[Array.IndexOf(ValueFunction.Names, name)] *= factor;
            return w;
        }

        private static (int delta, int better, int worse) PairedDelta(EvalResult candidate, EvalResult baseline)
        {
            int better = 0, worse = 0;
            for (int i = 0; i < Math.Min(candidate.Outcomes.Length, baseline.Outcomes.Length); i++)
            {
                if (candidate.Outcomes[i] < 0 || baseline.Outcomes[i] < 0) continue;
                if (candidate.Outcomes[i] > baseline.Outcomes[i]) better++;
                else if (candidate.Outcomes[i] < baseline.Outcomes[i]) worse++;
            }
            return (better - worse, better, worse);
        }

        private static (int common, int candidateOnly, int baselineOnly) InvalidDelta(EvalResult candidate, EvalResult baseline)
        {
            int common = 0, candidateOnly = 0, baselineOnly = 0;
            for (int i = 0; i < Math.Min(candidate.Outcomes.Length, baseline.Outcomes.Length); i++)
            {
                bool ci = candidate.Outcomes[i] < 0, bi = baseline.Outcomes[i] < 0;
                if (ci && bi) common++;
                else if (ci) candidateOnly++;
                else if (bi) baselineOnly++;
            }
            return (common, candidateOnly, baselineOnly);
        }

        private static string Percent(EvalResult r) => r.Decided == 0 ? "n/a" : $"{100.0 * r.Wins / r.Decided:F0}%";

        private static void PrintHoldout(Candidate c, Candidate baseline)
        {
            var d = PairedDelta(c.Holdout, baseline.Holdout);
            Console.WriteLine($"  {c.Name,-14} {c.Holdout.Wins,2}/{c.Holdout.Decided,-2} ({Percent(c.Holdout)}) " +
                              $"pairedDelta={d.delta,+3} ({d.better} better/{d.worse} worse) " +
                              $"outrightWins={c.Holdout.OutrightWins} adjudicated={c.Holdout.AdjudicatedGames} " +
                              $"invalid={c.Holdout.Invalid}");
        }

        private static string DescribeMultipliers(double[] w)
        {
            var b = ValueFunction.DefaultWeights();
            return string.Join(", ", ValueFunction.Names.Select((name, i) => (name, i))
                .Where(x => Math.Abs(w[x.i] - b[x.i]) > 1e-9)
                .Select(x => $"{x.name}×{w[x.i] / b[x.i]:F2}"));
        }

        private static void PrintIdentityBreakdown(List<Scenario> scenarios, EvalResult baseline, EvalResult candidate)
        {
            Console.WriteLine("identity breakdown (candidate vs floor):");
            foreach (var group in scenarios.Select((s, i) => (s, i)).GroupBy(x => x.s.Context.Archetype)
                         .OrderBy(x => x.Key))
            {
                var indices = group.SelectMany(x => new[] { x.i * 2, x.i * 2 + 1 }).ToArray();
                int bw = indices.Count(i => baseline.Outcomes[i] == 1);
                int bd = indices.Count(i => baseline.Outcomes[i] >= 0);
                int cw = indices.Count(i => candidate.Outcomes[i] == 1);
                int cd = indices.Count(i => candidate.Outcomes[i] >= 0);
                int better = indices.Count(i => candidate.Outcomes[i] > baseline.Outcomes[i]
                    && candidate.Outcomes[i] >= 0 && baseline.Outcomes[i] >= 0);
                int worse = indices.Count(i => candidate.Outcomes[i] < baseline.Outcomes[i]
                    && candidate.Outcomes[i] >= 0 && baseline.Outcomes[i] >= 0);
                Console.WriteLine($"  {group.Key,-9} {cw}/{cd} vs {bw}/{bd}; paired {better} better/{worse} worse");
            }
        }
    }
}
