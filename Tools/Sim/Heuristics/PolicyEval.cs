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
    public sealed class PolicyEvalConfig
    {
        public string experimentId { get; set; } = "policy-eval";
        public string policyPath { get; set; } = "policy.json";
        public List<string> decks { get; set; }
        public List<string> opponents { get; set; }
        public List<string> opponentPolicies { get; set; } = new List<string> { "baseline", "aggro", "conservative" };
        public int samplesPerMatchup { get; set; } = 100;
        public int commandCap { get; set; } = 20000;
        public int maxDegreeOfParallelism { get; set; } = 0;
        public string seedPrefix { get; set; } = "pe";
        public string outputDir { get; set; } = "Results";
        public bool agentChoosesTurnOrder { get; set; } = false; // let the policy pick first/second (§6)
    }

    /// <summary>
    /// Closes the discovery loop: does the <see cref="PolicyAgent"/> (which applies the DISCOVERED
    /// heuristics) actually win more than plain baseline? For every (my deck × opp deck × opp style ×
    /// matched seed) it plays the SAME game twice — once with the policy piloting south, once with
    /// baseline piloting south, against the identical opponent — and reports the paired win-rate lift
    /// with a confidence interval, overall and per opponent style (§11.1, §11.3). Positive, CI-excludes-0
    /// lift = the heuristics are worth applying.
    /// </summary>
    public static class PolicyEval
    {
        public static void Run(PolicyEvalConfig cfg, DeckRegistry registry)
        {
            var policy = Policy.Load(cfg.policyPath);
            int famCount = policy?.families?.Count ?? 0;
            int ruleCount = policy?.families?.Values.Sum(f => f.rules?.Count ?? 0) ?? 0;
            bool hasGuidance = policy?.families?.Values.Any(f => (f.rules?.Count ?? 0) > 0 || f.overall?.prefer != null) ?? false;

            var decks = (cfg.decks != null && cfg.decks.Count > 0) ? cfg.decks : registry.Ids.OrderBy(k => k).ToList();
            var opps = (cfg.opponents != null && cfg.opponents.Count > 0) ? cfg.opponents : decks;
            foreach (var d in decks.Concat(opps).Distinct())
                if (!registry.Has(d)) throw new ArgumentException($"Unknown deck id '{d}'.");
            var def = decks.Concat(opps).Distinct().ToDictionary(d => d, registry.Resolve);
            var pols = (cfg.opponentPolicies != null && cfg.opponentPolicies.Count > 0)
                ? cfg.opponentPolicies : new List<string> { "baseline" };
            var northByPol = pols.Distinct().ToDictionary(p => p, p => SelfPlayRunner.MakeAgent(p));

            int D = decks.Count, Op = opps.Count, Pol = pols.Count, S = Math.Max(1, cfg.samplesPerMatchup);
            long trials = (long)D * Op * Pol * S;
            string outDir = Path.GetFullPath(Path.Combine(cfg.outputDir, cfg.experimentId));
            Directory.CreateDirectory(outDir);

            Console.WriteLine($"Policy eval '{cfg.experimentId}'  policy={cfg.policyPath} ({famCount} families, {ruleCount} rules)");
            Console.WriteLine($"  policy-south vs baseline-south, matched seeds, opponents [{string.Join(",", pols)}]");
            Console.WriteLine($"  {trials:N0} paired trials → {trials * 2:N0} games\n");

            int dop = cfg.maxDegreeOfParallelism > 0 ? cfg.maxDegreeOfParallelism : Environment.ProcessorCount;
            var mergeLock = new object();
            (WinTally policy, WinTally baseline) Agg = default;
            var pol_ = new Dictionary<string, (WinTally p, WinTally b)>();
            foreach (var p in pols) pol_[p] = default;
            long done = 0; var sw = Stopwatch.StartNew();

            int chunks = (int)Math.Min(trials, (long)dop * 4);
            var ranges = new List<(long, long)>();
            for (int c = 0; c < chunks; c++) { long a = trials * c / chunks, b = trials * (c + 1) / chunks; if (b > a) ranges.Add((a, b)); }

            Parallel.ForEach(ranges, new ParallelOptions { MaxDegreeOfParallelism = dop },
                () => new Local(pols),
                (range, _, loc) =>
                {
                    for (long ti = range.Item1; ti < range.Item2; ti++)
                    {
                        long idx = ti;
                        long i = idx % S; idx /= S;
                        long pl = idx % Pol; idx /= Pol;
                        long op = idx % Op; idx /= Op;
                        long sd = idx;
                        string myD = decks[(int)sd], opD = opps[(int)op], polName = pols[(int)pl];
                        var myDef = def[myD]; var opDef = def[opD];
                        var north = northByPol[polName];
                        string seed = $"{cfg.seedPrefix}:{myD}:{opD}:{polName}:{i}";
                        var opts = new MatchDriver.Options { CommandCap = cfg.commandCap, AgentChoosesTurnOrder = cfg.agentChoosesTurnOrder };

                        var recP = MatchDriver.Play(new PolicyAgent(policy, myDef, opDef), north, myDef, opDef, seed, "south", opts);
                        var recB = MatchDriver.Play(new BaselineAgent(), north, myDef, opDef, seed, "south", opts);
                        if (recP.winner == null || recB.winner == null) continue;
                        bool pw = recP.winner == "south", bw = recB.winner == "south";
                        loc.Add(polName, pw, bw);

                        if (Interlocked.Increment(ref done) % 50_000 == 0)
                            Console.WriteLine($"  {done:N0}/{trials:N0}  ({2 * done / sw.Elapsed.TotalSeconds:N0} games/s)");
                    }
                    return loc;
                },
                loc =>
                {
                    lock (mergeLock)
                    {
                        Agg.policy.Merge(loc.Overall.p); Agg.baseline.Merge(loc.Overall.b);
                        foreach (var kv in loc.ByStyle)
                        {
                            var cur = pol_[kv.Key];
                            cur.p.Merge(kv.Value.p); cur.b.Merge(kv.Value.b);
                            pol_[kv.Key] = cur;
                        }
                    }
                });

            sw.Stop();
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:N1}s.\n");
            void Line(string label, WinTally p, WinTally b)
            {
                double liftPP = 100.0 * (p.Rate - b.Rate);
                Console.WriteLine($"  {label,-14} policy {p.Rate:P1}  baseline {b.Rate:P1}  lift {liftPP:+0.0;-0.0} pp  n={p.Games:N0}");
            }
            Console.WriteLine("Policy win rate vs baseline win rate (same seeds, same opponents):");
            Line("OVERALL", Agg.policy, Agg.baseline);
            foreach (var p in pols) Line("vs " + p, pol_[p].p, pol_[p].b);
            if (!hasGuidance)
                Console.WriteLine("\n  (policy has no applicable guidance — PolicyAgent falls through to baseline, so ~0 lift is expected.)");
        }

        private sealed class Local
        {
            public (WinTally p, WinTally b) Overall;
            public readonly Dictionary<string, (WinTally p, WinTally b)> ByStyle = new();
            public Local(IEnumerable<string> pols) { foreach (var p in pols) ByStyle[p] = default; }
            public void Add(string style, bool policyWon, bool baseWon)
            {
                Overall.p.Add(policyWon); Overall.b.Add(baseWon);
                var c = ByStyle[style];
                c.p.Add(policyWon); c.b.Add(baseWon);
                ByStyle[style] = c;
            }
        }
    }
}
