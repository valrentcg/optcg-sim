using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OnePieceTcg.Sim.Heuristics
{
    /// <summary>Online aggregator of PAIRED counterfactual differences d = win(A) − win(B) ∈ {−1,0,+1},
    /// bucketed by feature value. Because A and B run on the same seed, d is the causal effect of the
    /// choice on that matched world (§10.5) — not a mere correlation. Emits §12.3 conditional heuristics
    /// with a promotion gate: enough paired samples AND a 95% CI that excludes zero (§11.3).</summary>
    public sealed class Distiller
    {
        public struct Bucket
        {
            public long N;        // paired trials
            public long SumD;     // Σ d
            public long SumD2;    // Σ d²  (for variance of the paired difference)
            public long AWins, BWins;

            public void Add(int d, bool aWon, bool bWon)
            {
                N++; SumD += d; SumD2 += (long)d * d;
                if (aWon) AWins++; if (bWon) BWins++;
            }
            public void Merge(Bucket o) { N += o.N; SumD += o.SumD; SumD2 += o.SumD2; AWins += o.AWins; BWins += o.BWins; }

            public double MeanPP => N == 0 ? 0 : 100.0 * SumD / N;    // effect in percentage points
            public (double lo, double hi) Ci95PP()
            {
                if (N < 2) return (0, 0);
                double mean = (double)SumD / N;
                double var = (SumD2 - (double)SumD * SumD / N) / (N - 1);
                double se = Math.Sqrt(Math.Max(0, var) / N);
                return (100 * (mean - 1.96 * se), 100 * (mean + 1.96 * se));
            }
        }

        private readonly DecisionFamily _fam;
        public Bucket Overall;
        // feature -> bin-label -> bucket
        public readonly Dictionary<string, Dictionary<string, Bucket>> Feat = new();
        // "sDeck vs nDeck" -> bucket (always tracked)
        public readonly Dictionary<string, Bucket> Matchup = new();
        // opponent playstyle -> bucket (§11.3 robustness: does the rule hold vs every style?)
        public readonly Dictionary<string, Bucket> Style = new();

        public Distiller(DecisionFamily fam) { _fam = fam; }

        public void Add(bool aWon, bool bWon, string sDeck, string nDeck, FeatureBag f, string style = "baseline")
        {
            // Only count games that produced a result on BOTH sides (no cap/stall/crash winner-less).
            // A winner-less side leaves the pair undefined; skip it.
            int d = (aWon ? 1 : 0) - (bWon ? 1 : 0);
            Overall.Add(d, aWon, bWon);

            string mk = $"{sDeck} vs {nDeck}";
            var mb = Matchup.GetValueOrDefault(mk); mb.Add(d, aWon, bWon); Matchup[mk] = mb;
            var sb = Style.GetValueOrDefault(style); sb.Add(d, aWon, bWon); Style[style] = sb;

            foreach (var name in _fam.ReportFeatures)
            {
                if (!f.TryGetValue(name, out var v)) continue;
                string bin = Bin(name, v);
                if (!Feat.TryGetValue(name, out var bins)) { bins = new(); Feat[name] = bins; }
                var b = bins.GetValueOrDefault(bin); b.Add(d, aWon, bWon); bins[bin] = b;
            }
        }

        public void Merge(Distiller o)
        {
            Overall.Merge(o.Overall);
            foreach (var kv in o.Matchup) { var b = Matchup.GetValueOrDefault(kv.Key); b.Merge(kv.Value); Matchup[kv.Key] = b; }
            foreach (var kv in o.Style) { var b = Style.GetValueOrDefault(kv.Key); b.Merge(kv.Value); Style[kv.Key] = b; }
            foreach (var fk in o.Feat)
            {
                if (!Feat.TryGetValue(fk.Key, out var bins)) { bins = new(); Feat[fk.Key] = bins; }
                foreach (var bk in fk.Value) { var b = bins.GetValueOrDefault(bk.Key); b.Merge(bk.Value); bins[bk.Key] = b; }
            }
        }

        // ── report (§12.3) ──────────────────────────────────────────────────────
        public string Report(long minSamples)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Heuristic report — {_fam.Name}");
            sb.AppendLine();
            sb.AppendLine($"**Question:** {_fam.Question}  ");
            sb.AppendLine($"**Effect measured:** P(south wins | **{_fam.ChoiceA}**) − P(south wins | **{_fam.ChoiceB}**), " +
                          $"in percentage points (pp), over matched-seed pairs. Positive ⇒ prefer *{_fam.ChoiceA}*.");
            sb.AppendLine();
            var (olo, ohi) = Overall.Ci95PP();
            sb.AppendLine($"**Overall:** {Signed(Overall.MeanPP)} pp  (95% CI [{olo:+0.0;-0.0}, {ohi:+0.0;-0.0}], n={Overall.N:N0})");
            sb.AppendLine($"_Promotion gate: n ≥ {minSamples:N0} paired trials AND the 95% CI excludes 0._");
            sb.AppendLine();

            if (Style.Count > 1)
            {
                sb.AppendLine("**By opponent playstyle** (a robust rule holds vs all of them, §11.3):");
                foreach (var kv in Style.OrderBy(k => k.Key))
                {
                    var (slo, shi) = kv.Value.Ci95PP();
                    sb.AppendLine($"- vs {kv.Key}: {Signed(kv.Value.MeanPP)} pp  (95% CI [{slo:+0.0;-0.0}, {shi:+0.0;-0.0}], n={kv.Value.N:N0})");
                }
                sb.AppendLine();
            }

            var findings = new List<(string cond, Bucket b)>();
            foreach (var fk in _fam.ReportFeatures.Where(Feat.ContainsKey))
                foreach (var bk in Feat[fk])
                    findings.Add(($"{fk} = {bk.Key}", bk.Value));
            foreach (var mk in Matchup)
                findings.Add(($"matchup: {mk.Key}", mk.Value));

            var promoted = findings
                .Where(x => x.b.N >= minSamples)
                .Select(x => (x.cond, x.b, ci: x.b.Ci95PP()))
                .Where(x => x.ci.lo > 0 || x.ci.hi < 0)          // CI excludes 0
                .OrderByDescending(x => Math.Abs(x.b.MeanPP))
                .ToList();

            sb.AppendLine($"## Promoted findings ({promoted.Count})");
            sb.AppendLine();
            if (promoted.Count == 0)
                sb.AppendLine("_None cleared the gate at this sample size. Increase samples or loosen minSamples._");
            foreach (var (cond, b, ci) in promoted)
            {
                string prefer = b.MeanPP >= 0 ? _fam.ChoiceA : _fam.ChoiceB;
                sb.AppendLine($"**Finding:** prefer **{prefer}** when `{cond}`.  ");
                sb.AppendLine($"- Effect: {Signed(b.MeanPP)} pp win probability (choosing {_fam.ChoiceA} over {_fam.ChoiceB})  ");
                sb.AppendLine($"- Validation: {b.N:N0} paired trials, 95% CI [{ci.lo:+0.0;-0.0}, {ci.hi:+0.0;-0.0}] pp  ");
                sb.AppendLine($"- Raw: {_fam.ChoiceA} won {b.AWins:N0}, {_fam.ChoiceB} won {b.BWins:N0}  ");
                sb.AppendLine();
            }

            // Full table for inspection (not gated), so reversals are visible (§11.3).
            sb.AppendLine("## All feature buckets (ungated — includes reversals)");
            sb.AppendLine();
            sb.AppendLine("| condition | effect pp | 95% CI pp | n |");
            sb.AppendLine("|---|---:|:---:|---:|");
            foreach (var (cond, b) in findings.OrderByDescending(x => x.b.N))
            {
                var (lo, hi) = b.Ci95PP();
                sb.AppendLine($"| {cond} | {Signed(b.MeanPP)} | [{lo:+0.0;-0.0}, {hi:+0.0;-0.0}] | {b.N:N0} |");
            }
            return sb.ToString();
        }

        private static string Signed(double pp) => pp.ToString("+0.0;-0.0", CultureInfo.InvariantCulture);

        // Feature-value → bucket label (canonical mapping shared with the PolicyAgent).
        private static string Bin(string feature, double v) => FeatureBins.Bin(feature, v);
    }
}
