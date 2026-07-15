using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Planning
{
    public sealed class PlannerTourConfig
    {
        public string experimentId { get; set; } = "planner-tour";
        public List<string> deckPool { get; set; }
        public int populationSize { get; set; } = 24;
        public int generations { get; set; } = 12;
        public int roundsPerGeneration { get; set; } = 4;
        public int gamesPerPairing { get; set; } = 3;
        // THE SEARCH KNEE — measured 2026-07-14, do not lower these for speed without re-measuring.
        // These were 3/6/120/4000 ("reduced so the tournament is tractable"), and at --work 800 that bot
        // wins 11.7% vs the IntermediateBot while the SHIPPING bot (400/6/10) wins ~42-45% on the SAME
        // untuned weights (z=4.05, p<0.0001). An 11.7% bot and a 43% bot are not the same bot: a starved
        // search truncates constantly, so weights get selected for coping with truncation rather than for
        // playing One Piece. Every tournament run before this one tuned the wrong bot. Beyond
        // nodes=400/beam=6 more work buys nothing (--full ≈ work=20000), so this IS the knee — the
        // cheapest budget that still evolves the bot we actually ship.
        public int beamWidth { get; set; } = 16;
        public int maxDepth { get; set; } = 16;
        public int nodeBudget { get; set; } = 3200;   // THE KNEE (2026-07-15 sweep) — 400 starved cheap decks by ~31pp
        public long workBudget { get; set; } = 200000;  // clone+apply cap per planned turn — bounds the latency tail
        public int commandCap { get; set; } = 20000;
        /// <summary>Turn cap per game. Real games end in ~10-12 turns; the 60-turn default is a CRASH guard,
        /// far too loose to bound throughput. A few bad-weight bots grind to 60 turns and, because one
        /// straggler blocks a whole parallel wave, those games dominate wall-clock. Games that hit the cap
        /// are no-results and are excluded from Elo (as they already were), so this trades a little signal
        /// from long/control games for a large throughput win.</summary>
        public int maxTurns { get; set; } = 24;
        public int maxDegreeOfParallelism { get; set; } = 0;
        public string seedPrefix { get; set; } = "pt";
        public string outputDir { get; set; } = "Results";
        public int eliteKeep { get; set; } = 4;
        public int freshBlood { get; set; } = 4;

        /// <summary>Benchmark the current champion against the fixed BaselineAgent (IntermediateBot) every
        /// N generations. STRICTLY A MEASUREMENT: its result never touches Elo, selection or breeding, so
        /// the baseline stays a HELD-OUT yardstick. If it fed into fitness, the population would overfit to
        /// beating that one opponent and the number would stop being evidence of real strength.
        /// It exists because ladder Elo is RELATIVE to a co-evolving population and cannot tell
        /// "champion improving" from "whole field drifting" — 2026-07-14's run climbed 1533→1642 Elo while
        /// sitting at ~5% vs baseline the entire time. 0 disables.</summary>
        public int benchmarkEvery { get; set; } = 2;
        public int benchmarkGames { get; set; } = 40;

        /// <summary>Rank genomes on MIRROR matches (both play the same deck) instead of two different decks
        /// swapped between them. Deck advantage becomes zero BY CONSTRUCTION rather than merely cancelling
        /// across a pair — which matters because cancellation burns the signal: on a lopsided matchup each
        /// genome simply wins its favoured side, the pair splits 1-1, and BOTH games teach nothing. In a
        /// mirror there is no advantage to cancel, so every game measures the genome.
        /// The mirrored deck rotates across the pool, so all archetypes are still covered; only ASYMMETRIC
        /// matchups are lost, and ValueFunction has no opponent-deck feature to learn from those anyway.
        /// Fitness only — the held-out benchmark keeps playing diverse matchups, so overfitting to mirror
        /// play surfaces as ladder Elo rising while the benchmark stays flat.
        /// Requires an EVEN gamesPerPairing (consecutive k swap seats to cancel first-player advantage).</summary>
        public bool mirrorMatches { get; set; } = true;
    }

    internal sealed class WIndividual { public double[] W; public double Elo = 1500; public string Origin; }

    /// <summary>
    /// Evolves the <see cref="ValueFunction"/> WEIGHTS — the real "knobs" — across a population of
    /// PlannerBots on the meta decks (blueprint §10.4). Each bot uses the full turn-planner (so it
    /// actually attacks / finds lethal); the tournament only decides which *weighting* of the value
    /// features wins most across the field. Elo Swiss matchmaking, breed the winners, cull the losers.
    /// The search budget is deliberately reduced here for speed — the champion weights transfer to the
    /// full-budget bot.
    /// </summary>
    public static class PlannerTournament
    {
        public static void Run(PlannerTourConfig cfg, DeckRegistry registry)
        {
            var decks = (cfg.deckPool != null && cfg.deckPool.Count > 0) ? cfg.deckPool : CleanMeta(registry);
            if (decks.Count < 2) throw new InvalidOperationException("Need >=2 clean meta decks.");
            var def = decks.ToDictionary(d => d, registry.Resolve);
            var ctx = decks.ToDictionary(d => d, d => DeckFingerprint.Analyze(def[d]));   // deck-aware context per deck
            // ConsiderTruncatedLines is required exactly when a work budget can cut the search short —
            // without it a truncated plan falls back to endTurn (passive). It stays off at full budget so
            // it can never change shipping play.
            var opt = new TurnPlanner.Options { BeamWidth = cfg.beamWidth, MaxDepth = cfg.maxDepth, NodeBudget = cfg.nodeBudget, WorkBudget = cfg.workBudget, ConsiderTruncatedLines = cfg.workBudget > 0 };
            int dop = cfg.maxDegreeOfParallelism > 0 ? cfg.maxDegreeOfParallelism : Environment.ProcessorCount;

            string outDir = Path.GetFullPath(Path.Combine(cfg.outputDir, cfg.experimentId));
            Directory.CreateDirectory(outDir);
            var curve = new StreamWriter(Path.Combine(outDir, "planner-ladder.csv")) { AutoFlush = true };
            curve.WriteLine("generation,top_elo,mean_elo," + string.Join(",", ValueFunction.Names));
            var bench = new StreamWriter(Path.Combine(outDir, "benchmark-vs-baseline.csv")) { AutoFlush = true };
            bench.WriteLine("generation,wins,losses,no_result,win_rate,ci_lo,ci_hi");

            int gamesPerGen = (cfg.populationSize / 2) * cfg.gamesPerPairing * cfg.roundsPerGeneration;
            // An ODD gamesPerPairing silently breaks the pairing that cancels seat/deck advantage: the last
            // scenario is played once, so one genome keeps the first-player edge (~51%) for free. That bias
            // is small per game but it is SYSTEMATIC, and selection compounds systematic bias into the
            // population far faster than it averages out random noise.
            if (cfg.gamesPerPairing % 2 != 0)
                Console.WriteLine($"  ⚠ gamesPerPairing={cfg.gamesPerPairing} is ODD — the final scenario is unmirrored, leaving an uncancelled seat bias. Prefer an even value.");
            Console.WriteLine($"Planner tournament '{cfg.experimentId}'  pop={cfg.populationSize}  gens={cfg.generations}  decks={decks.Count}  mirror={cfg.mirrorMatches}");
            Console.WriteLine($"  search budget: beam={cfg.beamWidth} depth={cfg.maxDepth} nodes={cfg.nodeBudget} work={cfg.workBudget} turnCap={cfg.maxTurns}");
            Console.WriteLine($"  {gamesPerGen} games/gen x {cfg.generations} gens = {gamesPerGen * cfg.generations} games, dop={dop}");
            var swAll = System.Diagnostics.Stopwatch.StartNew();

            var rng = new Random(4242);
            var pop = Seed(cfg.populationSize, rng);

            for (int gen = 1; gen <= cfg.generations; gen++)
            {
                // RE-RATE FROM SCRATCH EVERY GENERATION. Elo is only meaningful WITHIN a fixed pool, but
                // Next() replaces 20 of 24 individuals each generation while the 4 elites carried forward
                // ratings earned against opponents that no longer exist. That was rich-get-richer: whoever
                // got lucky in gen 1 started ~56 points above every new genome, Swiss-paired into the other
                // high-Elo bots, and was re-elected on its history rather than its play. Observed in
                // knee-mirror-v1: the gen-2 champion was BIT-IDENTICAL to gen-1's across all 24 genes, so
                // the held-out benchmark just re-measured the same bot and could not possibly move.
                // Resetting makes fitness mean "how it played THIS generation", which is what selection
                // needs. Cross-generation progress is tracked by the held-out benchmark, which is an
                // absolute measure and does not drift with the pool — that is exactly its job.
                foreach (var ind in pop) ind.Elo = 1500;

                for (int round = 0; round < cfg.roundsPerGeneration; round++)
                {
                    var order = pop.OrderBy(_ => rng.Next()).OrderByDescending(x => x.Elo).ToList();
                    var pairs = new List<(WIndividual a, WIndividual b)>();
                    for (int i = 0; i + 1 < order.Count; i += 2) pairs.Add((order[i], order[i + 1]));
                    int seedBase = (gen * 100 + round) * 100_000;

                    // Parallelise across EVERY GAME in the round, not just pairs. Pair-level parallelism
                    // made a round cost (gamesPerPairing x slowest game) and left cores idle whenever
                    // pairs < cores; game-level parallelism lets a straggler overlap with other pairs'
                    // work. Elo is applied afterwards, in pair order, so results stay deterministic.
                    var jobs = new List<(int pi, int k)>();
                    for (int pi = 0; pi < pairs.Count; pi++)
                        for (int k = 0; k < cfg.gamesPerPairing; k++) jobs.Add((pi, k));
                    var results = new string[pairs.Count][];
                    for (int pi = 0; pi < pairs.Count; pi++) results[pi] = new string[cfg.gamesPerPairing];

                    Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = dop }, job =>
                    {
                        var (pi, k) = job;
                        var (a, b) = pairs[pi];

                        // COMMON RANDOM NUMBERS — the scenario (deck(s) + shuffle seed) is keyed off `scen`
                        // ONLY, never off `pi`, so every pairing in the round plays the SAME scenarios and
                        // deck/shuffle luck is common to all pairs, cancelling when their Elos are compared.
                        // Originally both deck choice and seed keyed off `pi`, so most of the Elo spread was
                        // the draw rather than the genome — and that noise WAS the selection pressure.
                        int scen = seedBase + (k / 2);
                        bool swap = (k % 2) == 1;
                        string dA, dB;
                        if (cfg.mirrorMatches)
                        {
                            // MIRROR MATCH: both genomes play the SAME deck, so deck advantage is ZERO BY
                            // CONSTRUCTION and every game is decided by the genome (plus shuffle, which CRN
                            // handles). This is strictly better than swapping two DIFFERENT decks between
                            // the genomes: that only cancels deck advantage ACROSS the pair, so a lopsided
                            // matchup makes each genome win its favoured side — 1-1, both games wasted. Here
                            // there is no advantage to cancel, so no game is wasted.
                            // `scen` rotates the mirrored deck across the whole pool, so the genome is still
                            // evolved against every archetype — it just never plays an ASYMMETRIC matchup.
                            // That costs little: despite the docstring, ValueFunction has no opponent-deck
                            // feature (Attributes uses only MY DeckContext), so asymmetry has little to teach
                            // it. The held-out benchmark still plays DIVERSE matchups, so overfitting to
                            // mirror play would show up as Elo rising while the benchmark stays flat.
                            dA = dB = decks[(scen * 7) % decks.Count];
                        }
                        else
                        {
                            string d1 = decks[(scen * 7) % decks.Count];
                            string d2 = decks[(scen * 13 + 1) % decks.Count];
                            dA = swap ? d2 : d1; dB = swap ? d1 : d2;
                        }
                        // Seat/turn order alternates within the scenario so first-player advantage (~51%)
                        // cancels too — in a mirror that is the ONLY remaining structural asymmetry, which
                        // is exactly why the pair must be even.
                        string first = swap ? "north" : "south";
                        results[pi][k] = PlayGame(a.W, ctx[dA], b.W, ctx[dB], def[dA], def[dB], $"{cfg.seedPrefix}:crn:{scen}", first, opt, cfg.commandCap, cfg.maxTurns);
                    });

                    for (int pi = 0; pi < pairs.Count; pi++)
                    {
                        var (a, b) = pairs[pi];
                        int aWins = 0, games = 0;
                        foreach (var win in results[pi]) if (win != null) { games++; if (win == "south") aWins++; }
                        if (games > 0) UpdateElo(a, b, (double)aWins / games);
                    }
                }

                var ranked = pop.OrderByDescending(x => x.Elo).ToList();
                var top = ranked[0];
                double meanElo = pop.Average(x => x.Elo);
                curve.WriteLine($"{gen},{top.Elo:0},{meanElo:0}," + string.Join(",", top.W.Select(x => x.ToString("0.000", CultureInfo.InvariantCulture))));
                double gps = (gen * gamesPerGen) / swAll.Elapsed.TotalSeconds;
                Console.WriteLine($"  gen {gen,2}: topElo={top.Elo:0} meanElo={meanElo:0}  [{swAll.Elapsed.TotalMinutes:F1}m elapsed, {gps:F2} games/s, ETA {(cfg.generations - gen) * gamesPerGen / gps / 60:F0}m]");

                // HELD-OUT BENCHMARK — measured, never selected on. Nothing below writes to Elo/pop.
                if (cfg.benchmarkEvery > 0 && gen % cfg.benchmarkEvery == 0)
                {
                    var (bw, bl, bnr) = BenchmarkVsBaseline(top.W, decks, def, ctx, opt, cfg, gen, dop);
                    int bdec = bw + bl;
                    double bp = bdec > 0 ? (double)bw / bdec : 0;
                    var (lo, hi) = Wilson(bw, bdec);
                    Console.WriteLine($"        └─ BENCHMARK vs IntermediateBot: {100 * bp:F1}% [{100 * lo:F1}%–{100 * hi:F1}%]  ({bw}W/{bl}L/{bnr}NR, n={bdec}) — measurement only, not selected on");
                    bench.WriteLine($"{gen},{bw},{bl},{bnr},{bp:F4},{lo:F4},{hi:F4}");
                }

                pop = Next(pop, cfg, rng);
            }

            var champ = pop.OrderByDescending(x => x.Elo).First();
            File.WriteAllText(Path.Combine(outDir, "planner-champion.txt"),
                string.Join(", ", ValueFunction.Names.Select((nm, i) => $"{nm}={champ.W[i]:0.000}")));
            curve.Dispose(); bench.Dispose();
            Console.WriteLine("\n=== champion weights ===");
            Console.WriteLine("  " + string.Join(" ", ValueFunction.Names.Select((nm, i) => $"{nm}={champ.W[i]:0.00}")));
            Console.WriteLine($"Ladder → {Path.Combine(outDir, "planner-ladder.csv")}");
        }

        private static string PlayGame(double[] wa, DeckContext ca, double[] wb, DeckContext cb,
            DeckDef da, DeckDef db, string seed, string first, TurnPlanner.Options opt, int cap, int maxTurns)
        {
            var st = GameRunner.Play(new PlannerBot(wa, ca, opt), new PlannerBot(wb, cb, opt), da, db, seed, first, cap, maxTurns);
            // Rules 8.2 adjudication, not Winner(): once the bot defends, games run long and ~40% hit the
            // turn cap. Scoring those as no-result threw away nearly half the ladder's evidence AND skewed
            // it, since only games that end fast could ever reach Elo.
            return GameRunner.Result(st);
        }

        /// <summary>Champion vs the fixed BaselineAgent (IntermediateBot). Read-only: returns counts and
        /// touches nothing in the population. Seats are BALANCED (the planner plays first and second in
        /// equal measure) — the project's old `plannertest` always seated the planner south-and-first,
        /// which flatters whichever side benefits from turn order.</summary>
        private static (int wins, int losses, int noResult) BenchmarkVsBaseline(double[] champW,
            List<string> decks, Dictionary<string, DeckDef> def, Dictionary<string, DeckContext> ctx,
            TurnPlanner.Options opt, PlannerTourConfig cfg, int gen, int dop)
        {
            int wins = 0, losses = 0, nr = 0;
            var idx = new List<int>(); for (int i = 0; i < cfg.benchmarkGames; i++) idx.Add(i);
            var lk = new object();
            Parallel.ForEach(idx, new ParallelOptions { MaxDegreeOfParallelism = dop }, i =>
            {
                string dA = decks[(i * 7 + gen) % decks.Count];
                string dB = decks[(i * 13 + 3) % decks.Count];
                bool champSouth = i % 2 == 0;                       // alternate seat => alternate turn order
                var champ = new PlannerBot(champW, ctx[champSouth ? dA : dB], opt);
                var basel = new BaselineAgent();
                IAgent south = champSouth ? champ : (IAgent)basel;
                IAgent north = champSouth ? (IAgent)basel : champ;
                var st = GameRunner.Play(south, north, def[dA], def[dB], $"bench:{gen}:{i}", "south", cfg.commandCap, cfg.maxTurns);
                // Adjudicate here too, so the yardstick measures the same thing the ladder rewards. If it
                // counted only outright wins while Elo counted adjudications, the benchmark could not see
                // a population that had learned to win on the 8.2 tiebreak — which is the exact turtle
                // regression this benchmark is being trusted to catch.
                var win = GameRunner.Result(st);
                lock (lk)
                {
                    if (win == null) nr++;
                    else if ((win == "south") == champSouth) wins++;
                    else losses++;
                }
            });
            return (wins, losses, nr);
        }

        /// <summary>Wilson 95% interval — with n=40 a point estimate alone invites reading noise as signal.</summary>
        private static (double lo, double hi) Wilson(int k, int n)
        {
            if (n == 0) return (0, 0);
            double p = (double)k / n, z = 1.96, den = 1 + z * z / n;
            double c = (p + z * z / (2.0 * n)) / den;
            double h = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / den;
            return (Math.Max(0, c - h), Math.Min(1, c + h));
        }

        private static void UpdateElo(WIndividual a, WIndividual b, double aScore, double k = 24)
        {
            double ea = 1.0 / (1 + Math.Pow(10, (b.Elo - a.Elo) / 400));
            a.Elo += k * (aScore - ea); b.Elo += k * ((1 - aScore) - (1 - ea));
        }

        private static List<WIndividual> Seed(int n, Random rng)
        {
            var pop = new List<WIndividual> { new WIndividual { W = ValueFunction.DefaultWeights(), Origin = "default" } };
            while (pop.Count < n) pop.Add(new WIndividual { W = Perturb(ValueFunction.DefaultWeights(), rng, 0.6), Origin = "random" });
            return pop;
        }

        /// <summary>Mutate a genome with BOTH an absolute and a RELATIVE kick per gene.
        ///
        /// A purely absolute ±scale cannot explore weights that span two orders of magnitude, and after
        /// ValueFunction's normalisation they do (MyBoardPow≈14.6 down to MyDeck≈0.07). ±0.6 on 14.6 is
        /// a ±4% wiggle, so the dominant genes were effectively FROZEN, while ±0.6 on 0.07 is ±850%, so
        /// the irrelevant genes random-walked. Measured in knee-mirror-v1: across 3 generations the board
        /// genes (50% of the eval) moved 2-5% while MyDeck/OppDeck/MyTrash (0.1% of the eval) flipped
        /// sign and quadrupled — evolution exploring exactly the wrong subspace. A hand-built genome that
        /// beats the default head-to-head sits at MyBoardPow≈2.4, i.e. ~20 consecutive same-direction
        /// mutations away: unreachable in any realistic run.
        /// (Note this got WORSE when features were normalised: the same ±0.6 used to act on a raw 0.9,
        /// which was a meaningful ±67%. Normalisation made weights interpretable and mutation uniform in
        /// EFFECT, but uniform effect is not the same as being able to explore SCALE.)
        ///
        /// The relative term explores scale — MyBoardPow can reach ~2.4 in a couple of steps — while the
        /// absolute term keeps small genes mutable, lets a gene leave exactly 0.0, and allows sign flips
        /// (which a purely multiplicative operator can never do).</summary>
        private static double[] Perturb(double[] w, Random rng, double scale)
        {
            var c = (double[])w.Clone();
            for (int i = 0; i < c.Length; i++)
            {
                double abs = scale * (rng.NextDouble() * 2 - 1);
                double rel = c[i] * scale * (rng.NextDouble() * 2 - 1);
                c[i] += abs + rel;
            }
            return c;
        }

        private static List<WIndividual> Next(List<WIndividual> pop, PlannerTourConfig cfg, Random rng)
        {
            var ranked = pop.OrderByDescending(x => x.Elo).ToList();
            // Elites are carried by GENOME only — Run() re-rates the whole population from 1500 each
            // generation, so an elite must re-earn its place by playing rather than inherit a rating from
            // opponents that have since been culled. Seeding newcomers below the elites (the old 1470/1485)
            // is pointless once ratings reset, and was actively harmful while they did not.
            var next = new List<WIndividual>(ranked.Take(cfg.eliteKeep));
            for (int i = 0; i < cfg.freshBlood && next.Count < cfg.populationSize; i++)
                next.Add(new WIndividual { W = Perturb(ValueFunction.DefaultWeights(), rng, 0.8), Origin = "random" });
            while (next.Count < cfg.populationSize)
            {
                var p1 = Tournament(ranked, rng); var p2 = Tournament(ranked, rng);
                var child = new double[p1.W.Length];
                for (int i = 0; i < child.Length; i++) child[i] = rng.Next(2) == 0 ? p1.W[i] : p2.W[i];
                child = Perturb(child, rng, 0.25);
                next.Add(new WIndividual { W = child, Origin = "bred" });
            }
            return next;
        }

        private static WIndividual Tournament(List<WIndividual> r, Random rng)
        {
            WIndividual best = null;
            for (int i = 0; i < 3; i++) { var c = r[rng.Next(r.Count)]; if (best == null || c.Elo > best.Elo) best = c; }
            return best;
        }

        private static List<string> CleanMeta(DeckRegistry reg) => reg.Ids
            .Where(i => !CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
            .OrderBy(x => x).ToList();
    }
}
