// Weighted position evaluation for the Advanced (search) bot. Refactored so the raw FEATURE vector is exposed
// (Features) and the WEIGHTS are loadable (SetWeights) — this is what lets the eval be FIT to the oracle's
// win-rate labels (puzzle-suite regression / distillation) instead of hand-tuned. Score = dot(Features, W).

using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    public static class Evaluation
    {
        // Feature names, in order (see Features()). Keep in sync with W.
        public static readonly string[] FeatureNames =
        {
            "powerDiff/1k", "charDiff", "lifeDiff", "handDiff", "myActiveDon", "counterReserve/1k",
            "blockerDiff", "restedOpp", "myTrash/5", "aggroOppLowLife", "oppOpenDon", "attachedDonDiff", "deckMargin/5",
            // richer / nonlinear features (iter 2) — the R² lever:
            "myLowLifeDanger", "oppLowLifePress", "myLeaderReach", "oppLeaderThreat", "myBoardPow/1k",
            "iHaveBlockerUp", "myUnrestedChars", "bias",
        };

        // Weights. Loadable so a fit-to-oracle pass replaces them. New features default 0 (regression learns them).
        public static double[] W =
        {
            1.87, 0.60, 1.20, 0.50, -0.38, -0.49, 0.40, 0.38, 0.00, -0.43, -0.15, 0.20, 0.05,
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        };

        public static int FeatureCount => FeatureNames.Length;

        /// <summary>Load a fitted weight vector (from the puzzle-suite regression). Length must match FeatureCount.</summary>
        public static void SetWeights(double[] w)
        {
            if (w != null && w.Length == W.Length) W = (double[])w.Clone();
        }

        /// <summary>The RAW feature vector for a NON-TERMINAL position, from `seat`'s perspective. Score is the
        /// dot product with W. Exposed so the weights can be fit to oracle win-rate labels by regression.</summary>
        public static double[] Features(GameState s, string seat)
        {
            string opp = GameEngine.OtherSeat(seat);
            var me = s.Players[seat]; var op = s.Players[opp];
            int myPow = me.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            int opPow = op.CharacterArea.Where(c => c != null).Sum(c => GameEngine.GetPower(s, c));
            int myChars = me.CharacterArea.Count(c => c != null), opChars = op.CharacterArea.Count(c => c != null);
            int myBlock = me.CharacterArea.Count(c => c != null && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false));
            int opBlock = op.CharacterArea.Count(c => c != null && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false));
            int restedOpp = op.CharacterArea.Count(c => c != null && c.Rested);
            int counterReserve = me.Hand.Sum(c => GameEngine.GetCounterPower(c));
            double aggro = System.Math.Max(0, 5 - op.Life.Count);
            int oppOpenDon = GameEngine.ActiveDonCount(op);
            int myAttachedDon = AttachedDon(me) - AttachedDon(op);
            int deckMargin = System.Math.Min(20, me.Deck.Count - op.Deck.Count);
            // richer features
            double myLowLifeDanger = System.Math.Pow(System.Math.Max(0, 3 - me.Life.Count), 2);      // last 2-3 life precious (nonlinear)
            double oppLowLifePress = System.Math.Pow(System.Math.Max(0, 3 - op.Life.Count), 2);       // pushing opp toward lethal
            int opLeadPow = op.Leader != null ? GameEngine.GetPower(s, op.Leader) : 5000;
            int myLeadPow = me.Leader != null ? GameEngine.GetPower(s, me.Leader) : 5000;
            int myLeaderReach = me.CharacterArea.Count(c => c != null && !c.Rested && GameEngine.GetPower(s, c) >= opLeadPow); // my attackers that connect to opp leader
            int oppLeaderThreat = op.CharacterArea.Count(c => c != null && GameEngine.GetPower(s, c) >= myLeadPow);           // opp bodies that threaten my leader
            int myUnrested = me.CharacterArea.Count(c => c != null && !c.Rested);
            double iHaveBlockerUp = me.CharacterArea.Any(c => c != null && !c.Rested && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false)) ? 1 : 0;
            return new double[]
            {
                (myPow - opPow) / 1000.0,
                (myChars - opChars),
                (me.Life.Count - op.Life.Count),
                (me.Hand.Count - op.Hand.Count),
                GameEngine.ActiveDonCount(me),
                counterReserve / 1000.0,
                (myBlock - opBlock),
                restedOpp,
                me.Trash.Count / 5.0,
                aggro,
                oppOpenDon,
                myAttachedDon,
                deckMargin / 5.0,
                myLowLifeDanger,
                oppLowLifePress,
                myLeaderReach,
                oppLeaderThreat,
                myPow / 1000.0,
                iHaveBlockerUp,
                myUnrested,
                1.0, // bias
            };
        }

        public static double Score(GameState s, string seat)
        {
            if (s.Status == "finished")
            {
                for (int i = s.EventLog.Count - 1; i >= 0; i--)
                {
                    var m = s.EventLog[i].Message;
                    if (string.IsNullOrEmpty(m) || !m.Contains("wins")) continue;
                    if (m.StartsWith("South")) return seat == "south" ? 1000 : -1000;
                    if (m.StartsWith("North")) return seat == "north" ? 1000 : -1000;
                }
                return 0;
            }
            var f = Features(s, seat);
            double sum = 0;
            for (int i = 0; i < W.Length && i < f.Length; i++) sum += W[i] * f[i];
            return sum;
        }

        private static int AttachedDon(PlayerState p)
        {
            int n = p.Leader?.AttachedDonIds?.Count ?? 0;
            foreach (var c in p.CharacterArea) if (c != null) n += c.AttachedDonIds?.Count ?? 0;
            return n;
        }
    }
}
