using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Heuristics
{
    /// <summary>Declarative config for one counterfactual heuristic-discovery run.</summary>
    public sealed class HeuristicConfig
    {
        public string experimentId { get; set; } = "heur-001";
        public string family { get; set; } = "turn-order";     // "mulligan" | "turn-order"
        public List<string> decks { get; set; }                 // test (south) decks; empty ⇒ all registered
        public List<string> opponents { get; set; }             // north decks; empty ⇒ same as decks
        public List<string> opponentPolicies { get; set; } = new List<string> { "baseline" }; // north styles (§11.3)
        public int samplesPerMatchup { get; set; } = 100;       // matched seeds per (sDeck,nDeck,style)
        public int commandCap { get; set; } = 20000;
        public int maxDegreeOfParallelism { get; set; } = 0;
        public string seedPrefix { get; set; } = "heur";
        public string outputDir { get; set; } = "Results";
        public long minSamples { get; set; } = 300;             // promotion gate
    }

    /// <summary>
    /// Runs a decision family as a PAIRED counterfactual across the whole deck field: for every
    /// (south deck × north deck) and every matched seed, it plays both choices (A and B) on the same
    /// seed and records which side won, plus the pre-decision features. The distiller turns those
    /// matched pairs into deck-agnostic conditional heuristics (§12) — rules keyed by generic
    /// deck-comparison / hand-shape features, never by leader name. This is the engine that lets the
    /// advanced bot decide by comparing its deck to the opponent's rather than by a hardcoded lookup.
    /// </summary>
    public static class HeuristicRunner
    {
        private sealed class TrialRow
        {
            public string exp; public long t; public string fam;
            public string sDeck; public string nDeck; public string nPol; public string seed;
            public bool aWon; public bool bWon; public bool valid;
            public Dictionary<string, double> f;
        }

        public static void Run(HeuristicConfig cfg, DeckRegistry registry)
        {
            var fam = DecisionFamily.ByName(cfg.family);
            if (fam.Variants.Count != 2) throw new InvalidOperationException("families must be binary (A,B).");

            var decks = (cfg.decks != null && cfg.decks.Count > 0) ? cfg.decks : registry.Ids.OrderBy(k => k).ToList();
            var opps = (cfg.opponents != null && cfg.opponents.Count > 0) ? cfg.opponents : decks;
            foreach (var d in decks.Concat(opps).Distinct())
                if (!registry.Has(d)) throw new ArgumentException($"Unknown deck id '{d}'.");
            var def = decks.Concat(opps).Distinct().ToDictionary(d => d, registry.Resolve);

            var pols = (cfg.opponentPolicies != null && cfg.opponentPolicies.Count > 0)
                ? cfg.opponentPolicies : new List<string> { "baseline" };
            var northByPol = pols.Distinct().ToDictionary(p => p, p => SelfPlayRunner.MakeAgent(p)); // stateless, shareable

            int D = decks.Count, Op = opps.Count, Pol = pols.Count, S = Math.Max(1, cfg.samplesPerMatchup);
            long trials = (long)D * Op * Pol * S;
            long games = trials * 2;

            string outDir = Path.GetFullPath(Path.Combine(cfg.outputDir, cfg.experimentId));
            Directory.CreateDirectory(outDir);

            Console.WriteLine($"Heuristic discovery '{cfg.experimentId}'  family={fam.Name}");
            Console.WriteLine($"  {fam.Question}");
            Console.WriteLine($"  decks(south)={D}  opponents(north)={Op}  styles={Pol} [{string.Join(",", pols)}]  samples/cell={S}");
            Console.WriteLine($"  paired trials = {trials:N0}   → {games:N0} games");
            Console.WriteLine($"  output → {outDir}");

            int dop = cfg.maxDegreeOfParallelism > 0 ? cfg.maxDegreeOfParallelism : Environment.ProcessorCount;
            var baseline = new BaselineAgent();
            var global = new Distiller(fam);
            var mergeLock = new object();
            long done = 0, skipped = 0;
            int shardCounter = 0;
            var sw = Stopwatch.StartNew();

            int chunks = (int)Math.Min(trials, (long)dop * 4);
            var ranges = new List<(long, long)>();
            for (int c = 0; c < chunks; c++)
            {
                long a = trials * c / chunks, b = trials * (c + 1) / chunks;
                if (b > a) ranges.Add((a, b));
            }

            Parallel.ForEach(ranges, new ParallelOptions { MaxDegreeOfParallelism = dop },
                () => (Shard)null,
                (range, _, shard) =>
                {
                    shard ??= new Shard(outDir, Interlocked.Increment(ref shardCounter), fam);
                    for (long ti = range.Item1; ti < range.Item2; ti++)
                    {
                        long idx = ti;
                        long i = idx % S; idx /= S;
                        long pl = idx % Pol; idx /= Pol;
                        long op = idx % Op; idx /= Op;
                        long sd = idx; // < D

                        string sDeck = decks[(int)sd], nDeck = opps[(int)op], polName = pols[(int)pl];
                        var sDef = def[sDeck]; var nDef = def[nDeck];
                        var north = northByPol[polName];
                        string seed = $"{cfg.seedPrefix}:{fam.Name}:{sDeck}:{nDeck}:{polName}:{i}";

                        // Features: matchup-level up front; hand-level captured by the controller.
                        var feats = fam.UsesMatchupFeatures ? Features.Matchup(sDef, nDef) : new FeatureBag();
                        void Capture(FeatureBag fb) { foreach (var kv in fb) feats[kv.Key] = kv.Value; }

                        var opts = new MatchDriver.Options { CommandCap = cfg.commandCap };

                        var a = fam.Variants[0];
                        var b = fam.Variants[1];
                        var recA = MatchDriver.Play(a.WrapSouth(baseline, Capture), north, sDef, nDef, seed, a.FirstSeat ?? "south", opts);
                        var recB = MatchDriver.Play(b.WrapSouth(baseline, Capture), north, sDef, nDef, seed, b.FirstSeat ?? "south", opts);

                        bool valid = recA.winner != null && recB.winner != null;
                        bool aWon = recA.winner == "south";
                        bool bWon = recB.winner == "south";
                        if (valid) shard.Dist.Add(aWon, bWon, sDeck, nDeck, feats, polName);
                        else Interlocked.Increment(ref skipped);

                        shard.Trials.WriteLine(Json.Line(new TrialRow
                        {
                            exp = cfg.experimentId, t = ti, fam = fam.Name,
                            sDeck = sDeck, nDeck = nDeck, nPol = polName, seed = seed,
                            aWon = aWon, bWon = bWon, valid = valid,
                            f = new Dictionary<string, double>(feats),
                        }));

                        long n = Interlocked.Increment(ref done);
                        if (n % 50_000 == 0)
                            Console.WriteLine($"  {n:N0}/{trials:N0} trials  ({2 * n / sw.Elapsed.TotalSeconds:N0} games/s)");
                    }
                    return shard;
                },
                shard =>
                {
                    if (shard == null) return;
                    shard.Dispose();
                    lock (mergeLock) global.Merge(shard.Dist);
                });

            sw.Stop();
            Console.WriteLine($"Done: {trials:N0} paired trials ({games:N0} games) in {sw.Elapsed.TotalSeconds:N1}s " +
                              $"({games / Math.Max(0.001, sw.Elapsed.TotalSeconds):N0} games/s).  skipped(no-result)={skipped:N0}");

            string report = global.Report(cfg.minSamples);
            File.WriteAllText(Path.Combine(outDir, "heuristics.md"), report);
            var (lo, hi) = global.Overall.Ci95PP();
            Console.WriteLine($"  Overall effect ({fam.ChoiceA} vs {fam.ChoiceB}): {global.Overall.MeanPP:+0.0;-0.0} pp " +
                              $"[{lo:+0.0;-0.0}, {hi:+0.0;-0.0}]  n={global.Overall.N:N0}");
            Console.WriteLine($"  Report → {Path.Combine(outDir, "heuristics.md")}");
            Console.WriteLine($"  Trial shards → {outDir}\\trials.part*.jsonl");
        }

        private sealed class Shard : IDisposable
        {
            public readonly StreamWriter Trials;
            public readonly Distiller Dist;
            public Shard(string outDir, int id, DecisionFamily fam)
            {
                Trials = new StreamWriter(Path.Combine(outDir, $"trials.part{id:D3}.jsonl"), false) { AutoFlush = false };
                Dist = new Distiller(fam);
            }
            public void Dispose() { Trials.Flush(); Trials.Dispose(); }
        }
    }
}
