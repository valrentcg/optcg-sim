using System;

namespace OnePieceTcg.Sim
{
    /// <summary>Win-rate accumulator with a Wilson score confidence interval — the blueprint asks
    /// for "variance and confidence interval, not only raw win percentage" everywhere (§6.1, §11).</summary>
    public struct WinTally
    {
        public long Wins;
        public long Games;

        public void Add(bool win) { Games++; if (win) Wins++; }

        public void Merge(WinTally o) { Wins += o.Wins; Games += o.Games; }

        public double Rate => Games == 0 ? 0.0 : (double)Wins / Games;

        /// <summary>Wilson 95% interval half-width-ish bounds (z = 1.96). Returns (low, high).</summary>
        public (double low, double high) Wilson95()
        {
            if (Games == 0) return (0, 0);
            const double z = 1.96;
            double n = Games, p = Rate, z2 = z * z;
            double denom = 1 + z2 / n;
            double center = (p + z2 / (2 * n)) / denom;
            double margin = (z * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n))) / denom;
            return (Math.Max(0, center - margin), Math.Min(1, center + margin));
        }
    }
}
