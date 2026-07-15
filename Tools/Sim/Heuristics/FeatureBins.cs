using System;
using System.Globalization;

namespace OnePieceTcg.Sim.Heuristics
{
    /// <summary>Canonical feature→bin-label mapping, shared by the Distiller (which WRITES rule bins)
    /// and the PolicyAgent (which must compute the SAME bin to look a rule up). Keeping this in one
    /// place guarantees a rule discovered as "my_low_curve = 0.1" is matched by the agent computing
    /// the identical label at runtime.</summary>
    public static class FeatureBins
    {
        public static bool IsBool(string f) =>
            f.StartsWith("hand_has") || f.EndsWith("_ok") || f == "hand_flooded" || f == "i_am_faster"
            || f == "i_have_more_counters" || f == "opp_many_blockers";

        public static bool IsFloat(string f) =>
            f.Contains("avg_cost") || f.Contains("low_curve") || f.Contains("density");

        public static string Bin(string feature, double v)
        {
            if (IsBool(feature)) return v >= 0.5 ? "true" : "false";
            if (IsFloat(feature))
            {
                double step = feature.Contains("cost") ? 0.5 : 0.1;
                double r = Math.Round(v / step) * step;
                return r.ToString("0.0", CultureInfo.InvariantCulture);
            }
            int iv = (int)Math.Round(v);
            return iv >= 6 ? "6+" : iv.ToString(CultureInfo.InvariantCulture);
        }
    }
}
