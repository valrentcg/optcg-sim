// One Piece TCG - Glicko-2 rating math for the Bounty ranked ladder.
//
// This is the HIDDEN skill layer that sits under the visible Bounty (see
// RankedStore.cs). Players never see these numbers; they exist only to answer
// "who should I play" (matchmaking, later) and "how much bounty is this result
// worth" (the gap term in BountyService).
//
// Why Glicko-2 over plain ELO: it carries a rating deviation (RD, "how sure are
// we") and a volatility, so new/returning players converge fast (placements) and
// idle ratings widen (decay) — both for free from one update. A 1v1 card game is
// exactly the domain Glickman designed this for.
//
// Pure, allocation-free, no UnityEngine dependency, so it is trivially unit
// testable and a future Cloud Code port can mirror it line-for-line. Reference:
// Glickman, "Example of the Glicko-2 system" (glicko.net/glicko/glicko2.pdf).

using System;

public static class Glicko2
{
    public const double DefaultRating     = 1500.0;
    public const double DefaultRd         = 350.0;
    public const double DefaultVolatility = 0.06;

    public const double Tau   = 0.5;      // system constant: constrains volatility change
    public const double MinRd = 30.0;     // floor so settled ratings still move a little
    public const double MaxRd = 350.0;    // cap (also the fresh-account value)

    private const double Scale = 173.7178;            // Glicko-2 internal scale factor
    private const double Pi2   = Math.PI * Math.PI;
    private const double Eps   = 1e-6;                // volatility solver convergence

    public struct Result
    {
        public double Rating;
        public double Rd;
        public double Volatility;
    }

    /// <summary>One-game update of `self` against a single opponent.
    /// score = 1 win, 0 loss, 0.5 draw. Inputs/outputs are on the human 1500/350
    /// scale; the Glicko-2 conversion happens internally.</summary>
    public static Result Update(double rating, double rd, double volatility,
                                double oppRating, double oppRd, double score)
    {
        // Step 2: to the Glicko-2 scale.
        double mu   = (rating - 1500.0) / Scale;
        double phi  = ClampD(rd, MinRd, MaxRd) / Scale;
        double muJ  = (oppRating - 1500.0) / Scale;
        double phiJ = ClampD(oppRd, MinRd, MaxRd) / Scale;

        // Step 3-4: estimated variance v and improvement delta.
        double g = 1.0 / Math.Sqrt(1.0 + 3.0 * phiJ * phiJ / Pi2);
        double e = 1.0 / (1.0 + Math.Exp(-g * (mu - muJ)));
        double v = 1.0 / (g * g * e * (1.0 - e));
        double delta = v * g * (score - e);

        // Step 5: new volatility via the Illinois (regula-falsi) algorithm.
        double sigma = volatility <= 0 ? DefaultVolatility : volatility;
        double a = Math.Log(sigma * sigma);
        double phi2 = phi * phi;
        double delta2 = delta * delta;

        double A = a;
        double B;
        if (delta2 > phi2 + v)
        {
            B = Math.Log(delta2 - phi2 - v);
        }
        else
        {
            double k = 1.0;
            while (VolF(a - k * Tau, delta2, phi2, v, a) < 0.0) k += 1.0;
            B = a - k * Tau;
        }

        double fA = VolF(A, delta2, phi2, v, a);
        double fB = VolF(B, delta2, phi2, v, a);
        int guard = 0;
        while (Math.Abs(B - A) > Eps && guard++ < 100)
        {
            double C = A + (A - B) * fA / (fB - fA);
            double fC = VolF(C, delta2, phi2, v, a);
            if (fC * fB <= 0.0) { A = B; fA = fB; }
            else { fA *= 0.5; }
            B = C; fB = fC;
        }
        double newVol = Math.Exp(A / 2.0);

        // Step 6-7: pre-rating-period RD, then new RD and rating.
        double phiStar = Math.Sqrt(phi2 + newVol * newVol);
        double newPhi  = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
        double newMu   = mu + newPhi * newPhi * g * (score - e);

        return new Result
        {
            Rating     = newMu * Scale + 1500.0,
            Rd         = ClampD(newPhi * Scale, MinRd, MaxRd),
            Volatility = newVol,
        };
    }

    /// <summary>Widen RD for inactivity — call once per elapsed rating period so an
    /// idle player's matchmaking net (and bounty decay) opens back up.</summary>
    public static double InflateRd(double rd, double volatility, int periods)
    {
        double phi = ClampD(rd, MinRd, MaxRd) / Scale;
        double sigma = volatility <= 0 ? DefaultVolatility : volatility;
        for (int i = 0; i < Math.Max(0, periods); i++)
            phi = Math.Sqrt(phi * phi + sigma * sigma);
        return ClampD(phi * Scale, MinRd, MaxRd);
    }

    // f(x) from Step 5 of the paper; its root is the log of the new volatility².
    private static double VolF(double x, double delta2, double phi2, double v, double a)
    {
        double ex  = Math.Exp(x);
        double num = ex * (delta2 - phi2 - v - ex);
        double den = 2.0 * (phi2 + v + ex) * (phi2 + v + ex);
        return num / den - (x - a) / (Tau * Tau);
    }

    private static double ClampD(double x, double lo, double hi)
        => x < lo ? lo : (x > hi ? hi : x);
}
