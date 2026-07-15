using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Search
{
    public sealed class EvalEvolveConfig
    {
        public string experimentId { get; set; } = "search-eval";
        public List<string> deckPool { get; set; }
        public int generations { get; set; } = 25;
        public int gamesPerEval { get; set; } = 40;   // games to score a weight set vs baseline
        public int rolloutCap { get; set; } = 0;      // 0 = tune 1-ply eval; >0 = tune the ROLLOUT bot's eval
        public int commandCap { get; set; } = 20000;
        public int maxDegreeOfParallelism { get; set; } = 0;
        public string outputDir { get; set; } = "Results";
    }

    /// <summary>
    /// Evolves the SearchAgent's evaluation WEIGHTS to beat the baseline (blueprint §12.2 evolutionary
    /// weights, §10.6 value-model tuning). A 1-ply greedy search is only as good as its eval, so this
    /// hill-climbs the weight vector: each generation mutates the champion, scores candidate and champion
    /// by their SearchAgent win-rate vs baseline on matched meta-deck games, and keeps the winner. The
    /// learning curve (champion search win-rate vs baseline) shows the search bot actually getting
    /// stronger than the hand-coded bot as its value function improves.
    /// </summary>
    public static class EvalEvolver
    {
        public static void Run(EvalEvolveConfig cfg, DeckRegistry registry)
        {
            var decks = (cfg.deckPool != null && cfg.deckPool.Count > 0) ? cfg.deckPool : CleanMeta(registry);
            if (decks.Count < 2) throw new InvalidOperationException("Need >=2 clean meta decks.");
            var def = decks.ToDictionary(d => d, registry.Resolve);
            int dop = cfg.maxDegreeOfParallelism > 0 ? cfg.maxDegreeOfParallelism : Environment.ProcessorCount;

            string outDir = Path.GetFullPath(Path.Combine(cfg.outputDir, cfg.experimentId));
            Directory.CreateDirectory(outDir);
            var log = new StreamWriter(Path.Combine(outDir, "search-eval-curve.csv")) { AutoFlush = true };
            log.WriteLine("generation,accepted,champ_winrate_vs_baseline," + string.Join(",", EvalWeights.Genes.Select(g => g.name)));

            Console.WriteLine($"Search-eval evolution '{cfg.experimentId}'  meta decks={decks.Count}  {cfg.gamesPerEval} games/eval  {cfg.generations} gens");
            var rng = new Random(99);
            var champion = new EvalWeights();
            double champWr = Fitness(champion, decks, def, cfg, dop, 0);
            Console.WriteLine($"  gen  0: baseline-eval search vs baseline = {champWr:P1}  [{champion}]");
            log.WriteLine($"0,1,{champWr:0.000}," + string.Join(",", champion.W.Select(x => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))));

            for (int gen = 1; gen <= cfg.generations; gen++)
            {
                var candidate = Mutate(champion, rng);
                int seedBase = gen * 100_000;
                double candWr = Fitness(candidate, decks, def, cfg, dop, seedBase);
                double champWrThisBatch = Fitness(champion, decks, def, cfg, dop, seedBase); // same seeds → paired
                bool accept = candWr > champWrThisBatch;
                if (accept) { champion = candidate; champWr = candWr; }
                log.WriteLine($"{gen},{(accept ? 1 : 0)},{(accept ? candWr : champWrThisBatch):0.000}," +
                              string.Join(",", champion.W.Select(x => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))));
                Console.WriteLine($"  gen {gen,2}: {(accept ? "ACCEPT" : "reject")}  cand={candWr:P1} champ={champWrThisBatch:P1}  best-vs-baseline={champWr:P1}  [{champion}]");
            }

            log.Dispose();
            File.WriteAllText(Path.Combine(outDir, "search-champion.txt"), champion.ToString());
            Console.WriteLine($"\nFinal search eval champion (win rate vs baseline {champWr:P1}): {champion}");
            Console.WriteLine($"Curve → {Path.Combine(outDir, "search-eval-curve.csv")}");
        }

        // win rate of SearchAgent(weights) vs BaselineAgent over a batch of random meta-deck games.
        private static double Fitness(EvalWeights w, List<string> decks, Dictionary<string, DeckDef> def,
            EvalEvolveConfig cfg, int dop, int seedBase)
        {
            long wins = 0, done = 0;
            Parallel.For(0, cfg.gamesPerEval, new ParallelOptions { MaxDegreeOfParallelism = dop }, () => (0L, 0L), (i, _, acc) =>
            {
                var r = new Random(seedBase + i * 31 + 1);
                string dA = decks[r.Next(decks.Count)], dB = decks[r.Next(decks.Count)];
                string first = i % 2 == 0 ? "south" : "north";
                // rollout mode: score the ACTUAL rollout bot (rollouts with the champion policy) —
                // aligns the tuned eval with how the advanced bot really uses it (shortlisting).
                var searchSouth = cfg.rolloutCap > 0
                    ? new SearchAgent(w, null, "search", cfg.rolloutCap, 6, new Learning.WeightedAgent(Learning.BotTiers.IntermediateGenome()))
                    : new SearchAgent(w);
                var rec = MatchDriver.Play(searchSouth, new BaselineAgent(), def[dA], def[dB], $"se:{seedBase}:{i}", first,
                    new MatchDriver.Options { CommandCap = cfg.commandCap });
                long won = acc.Item1 + (rec.winner == "south" ? 1 : 0);
                long dn = acc.Item2 + (rec.winner != null ? 1 : 0);
                return (won, dn);
            }, acc => { Interlocked.Add(ref wins, acc.Item1); Interlocked.Add(ref done, acc.Item2); });
            return done == 0 ? 0 : (double)wins / done;
        }

        private static EvalWeights Mutate(EvalWeights parent, Random rng)
        {
            var c = parent.Clone();
            int n = 1 + rng.Next(2);
            for (int k = 0; k < n; k++)
            {
                int gi = rng.Next(EvalWeights.Genes.Length);
                var (_, _, lo, hi) = EvalWeights.Genes[gi];
                c.W[gi] = Math.Clamp(c.W[gi] + (hi - lo) * 0.25 * (rng.NextDouble() * 2 - 1), lo, hi);
            }
            return c;
        }

        private static List<string> CleanMeta(DeckRegistry reg) => reg.Ids
            .Where(i => !CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
            .OrderBy(x => x).ToList();
    }
}
