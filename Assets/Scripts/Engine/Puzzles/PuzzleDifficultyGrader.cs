using System;
using System.Collections.Generic;
using System.Globalization;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Converts exact certification evidence into the four player-facing difficulty tiers. Difficulty is not
    /// a recipe label: it is derived from consequential decisions, opening selectivity, legal branching,
    /// interacting defensive layers, action diversity, and turn depth.
    ///
    /// Repeated micro-actions have diminishing weight. For example, attaching six individual DON!! cards is
    /// not six independent strategic ideas, so critical decisions after the fourth add only a small increment.
    /// </summary>
    public static class PuzzleDifficultyGrader
    {
        public sealed class Grade
        {
            public int Tier;
            public double Score;
            public int CriticalDecisions;
            public int WinningFirstMoves;
            public int LegalFirstMoves;
            public string Evidence;
        }

        // Calibrated against the complete v2 catalog. Equal proof evidence always receives the same tier:
        // 1 Easy <= 8.00, 2 Medium <= 10.10, 3 Hard <= 10.80, 4 Expert above 10.80.
        public const double EasyMax = 8.00;
        public const double MediumMax = 10.10;
        public const double HardMax = 10.80;

        private static Dictionary<int, (int critical, int winning, int legal)> _generated;

        public static Grade Generated(int seed, string category)
        {
            EnsureGenerated();
            if (!_generated.TryGetValue(seed, out var m))
                throw new InvalidOperationException($"No difficulty evidence was baked for puzzle seed {seed}.");

            double cappedCritical = Math.Min(m.critical, 4) * 1.30
                + Math.Max(0, m.critical - 4) * 0.35;
            double selectivity = 1.0 - m.winning / (double)Math.Max(1, m.legal);
            double branchBreadth = Math.Log(Math.Max(2, m.legal), 2) * 0.65;
            double score = cappedCritical + selectivity * 3.0 + branchBreadth + FamilyWeight(category);
            // Interacting defensive layers have a floor even when the root happens to offer several equivalent
            // winning commands. A wall-plus-counter or multiple-wall state is never an Easy tutorial.
            score = Math.Max(score, FamilyMinimumScore(category));
            score = Math.Round(score, 2, MidpointRounding.AwayFromZero);

            return new Grade
            {
                Tier = TierFor(score),
                Score = score,
                CriticalDecisions = m.critical,
                WinningFirstMoves = m.winning,
                LegalFirstMoves = m.legal,
                Evidence = $"{m.critical} consequential decisions; {m.winning}/{m.legal} opening moves " +
                           $"preserve the forced win; {FamilyEvidence(category)}",
            };
        }

        public static Grade Authored(string category)
        {
            switch (category)
            {
                case "cost-reduction-removal-order":
                    return Manual(3, 10.55, 5,
                        "five consequential decisions; cost reduction, effect removal, attack order, and exact DON!! interact");
                case "banish-trigger-double-attack":
                    return Manual(3, 10.20, 3,
                        "three consequential decisions; the first hit changes Trigger availability and the remaining attack tree");
                case "restand-resource-sequencing":
                    return Manual(3, 10.70, 3,
                        "three consequential decisions; two different restand costs must survive a Blocker and Counter Event");
                case "two-turn-setup-defense-finish":
                    return Manual(4, 14.50, 8,
                        "eight consequential decisions, nine player action types, 810 opponent branches, and a fixed two-turn proof");
                case "two-turn-board-control":
                    return Manual(4, 13.20, 2,
                        "two consequential strategic decisions, nine player action types, 35 opponent branches, and a fixed two-turn proof");
                default:
                    return Manual(2, 9.00, 2, "authored forced-win proof");
            }
        }

        public static int TierFor(double score)
        {
            if (score <= EasyMax) return 1;
            if (score <= MediumMax) return 2;
            if (score <= HardMax) return 3;
            return 4;
        }

        private static Grade Manual(int tier, double score, int critical, string evidence) => new Grade
        {
            Tier = tier,
            Score = score,
            CriticalDecisions = critical,
            Evidence = evidence,
        };

        private static double FamilyWeight(string category)
        {
            switch (category)
            {
                case "rush-finisher": return 0.50;
                case "blocker-break": return 0.80;
                case "double-strike": return 1.10;
                case "counter-pump": return 1.30;
                case "combo-wall":
                case "double-wall": return 1.80;
                default: return 0.0;
            }
        }

        private static double FamilyMinimumScore(string category)
        {
            switch (category)
            {
                case "double-strike":
                case "counter-pump":
                case "combo-wall":
                case "double-wall":
                    return EasyMax + 0.01;
                default:
                    return 0.0;
            }
        }

        private static string FamilyEvidence(string category)
        {
            switch (category)
            {
                case "don-push": return "one exact DON!!-allocation layer.";
                case "rush-finisher": return "development timing adds a Rush layer.";
                case "blocker-break": return "a visible Blocker adds a removal layer.";
                case "double-strike": return "Double Attack changes the damage-order tree.";
                case "counter-pump": return "hidden Counter and card-demand layers alter the defense tree.";
                case "combo-wall": return "Blocker, hidden Counter, removal, and resource layers overlap.";
                case "double-wall": return "two removal targets and two Blocker responses overlap.";
                default: return "certified forced-win tree.";
            }
        }

        private static void EnsureGenerated()
        {
            if (_generated != null) return;
            _generated = new Dictionary<int, (int, int, int)>();
            foreach (string row in PackedGenerated.Split(';'))
            {
                string[] p = row.Split(',');
                if (p.Length != 4) continue;
                int seed = int.Parse(p[0], CultureInfo.InvariantCulture);
                _generated[seed] = (
                    int.Parse(p[1], CultureInfo.InvariantCulture),
                    int.Parse(p[2], CultureInfo.InvariantCulture),
                    int.Parse(p[3], CultureInfo.InvariantCulture));
            }
        }

        // seed, consequential decisions, winning opening moves, legal opening moves.
        // Emitted by puzzlequalityscan; Unknown alternatives never counted as losses.
        private const string PackedGenerated =
            "2,4,5,8;3,4,1,5;10,2,3,6;12,4,1,6;17,3,3,6;24,5,1,7;26,4,5,8;30,6,3,7;42,2,2,4;43,3,2,6;45,5,2,6;48,5,2,6;67,4,1,5;81,6,4,7;93,4,1,5;97,3,3,6;101,2,2,4;108,4,2,6;115,5,1,7;129,4,1,5;159,6,4,8;163,5,2,6;174,2,2,4;176,5,3,7;180,2,2,4;187,4,1,5;200,4,1,5;203,4,1,6;204,6,3,7;212,3,3,6;214,4,1,5;217,4,1,5;218,5,2,6;227,4,1,5;235,4,1,5;238,6,2,7;247,3,2,6;261,4,1,5;1005,4,1,5;1007,5,2,6;1012,3,3,6;1015,4,1,5;1018,5,2,6;1019,5,1,7;1040,6,4,8;1042,3,5,8;1052,6,4,7;1053,3,3,6;1055,3,3,6;1072,6,4,8;1073,6,3,8;1074,4,1,5;1075,3,2,6;1081,4,1,5;1082,4,1,5;1087,3,3,6;1089,5,1,7;1099,4,1,5;1102,3,3,6;1112,3,5,7;1119,4,3,6;1120,2,2,4;1123,3,2,5;1126,3,2,4;1127,4,1,6;1131,4,3,6;1133,4,3,6;1134,5,2,6;1137,3,2,4;1138,5,3,6;1142,2,2,4;1144,5,2,6;1145,5,2,6;1155,4,3,6;1157,4,3,6;1159,4,1,5;1160,4,1,5;1162,5,2,6;1168,5,5,10;1169,5,5,10;1174,3,3,6;1175,4,5,9;1181,3,2,4;1182,3,3,6;1183,4,3,6;1184,4,1,6;1185,6,4,8;1187,4,1,5;2001,5,2,6;2002,4,4,8;2004,6,3,7;2005,4,2,6;2014,5,1,7;2015,5,5,10;2016,3,4,7;2020,5,3,6;2030,6,4,8;2032,4,3,6;2035,6,4,8;2044,3,2,6;2049,4,1,5;2050,4,5,9;2053,5,3,6;2054,6,3,7;2059,4,3,6;2067,4,1,5;2068,4,1,5;2073,2,2,4;2074,5,5,10;2077,4,3,7;2081,5,5,10;2083,3,2,5;2086,6,4,8;2089,6,4,8;2092,5,5,10;2093,3,5,7;2094,4,3,6;2095,4,5,9;2097,4,5,9;2099,4,3,6;2103,6,4,8;2104,5,1,7;2106,6,4,8;2107,5,5,10;2110,6,4,8;2113,4,3,6;2118,6,4,9;2119,4,5,9;2126,4,3,6;2131,4,3,7;2133,5,4,8;2138,4,3,6;2139,2,2,4;2141,5,5,10;2147,5,3,6;2148,4,4,8;2154,5,2,6;2158,6,4,8;2159,2,2,4;2163,3,1,5;2165,5,5,10;2170,3,3,6;2172,4,1,5;2173,3,3,6;2175,5,3,6;2181,6,4,9;2183,6,4,7;2184,6,3,7;2185,3,2,4;2189,5,4,8;2194,5,2,6;2195,6,4,8;2201,5,1,7;2203,6,3,7;2205,6,3,8;2208,6,4,7;2213,6,3,7;2217,2,2,4;2219,4,1,5;2230,6,4,8;2231,5,5,9;2232,4,1,5;2233,6,4,8;2234,4,3,6;2239,4,5,9;2241,4,3,6;2242,6,4,9;2243,6,3,7;2245,4,3,6;2246,5,5,10;2251,6,2,6;2252,2,2,4;2254,2,2,4;2255,5,2,6;2258,6,4,8;2259,4,1,5;2260,6,4,8;2261,5,1,7;2266,6,4,8;2269,4,1,5;2275,5,4,8;2276,2,3,4;2277,6,3,7;2278,4,1,5;2283,5,1,7;2285,4,3,6;2292,5,4,8;3001,4,1,5;3004,4,1,5;3006,4,1,6;3009,6,4,8;3013,3,3,6;3014,6,4,8;3015,3,3,6;3016,4,4,7;3017,4,1,5;3019,5,3,7;3020,4,1,5;3021,6,4,8;3023,4,1,5;3024,5,3,6;3026,6,3,7;3030,6,4,8;3031,5,5,10;3036,4,3,6;3039,4,3,6;3040,5,5,10;3041,6,3,8;3045,2,2,4;3048,2,2,4;3051,3,2,4;3052,2,2,4;3056,4,3,7;3057,2,2,4;3061,6,4,8;3062,6,4,8;3067,3,5,7;3070,4,1,5;3072,5,5,9;3074,3,4,7;3079,4,1,5;3080,5,2,6;3081,4,1,5;3083,5,3,6;3084,5,3,6;3085,5,2,6;3088,5,5,11;3093,4,2,6;3094,2,4,6;3096,4,4,7;3100,2,2,4;3101,6,4,8;3102,3,6,11;3109,3,2,6;3111,3,3,6;3112,4,1,5;3117,5,2,7;3118,2,4,6;3120,4,1,5;3121,6,4,9;3127,5,2,7;3128,2,2,4;3131,5,8,8;3135,4,1,5;3143,6,4,8;3146,3,2,4;3147,4,4,8;3155,4,1,5;3159,3,6,11;3173,5,5,11;3177,4,3,6;3178,4,1,5;3182,4,1,5;3183,6,4,8;3184,4,1,5;3185,4,5,8;3186,5,1,8;3188,6,4,8;3189,5,3,7;3190,4,5,9;3191,5,2,6;3195,3,3,6;3198,4,3,6;3209,4,3,6;3210,3,6,11;3211,4,1,5;3212,6,4,8;3214,4,5,9;3218,3,3,6;3220,2,2,4;3223,6,3,6;3225,4,1,5;3226,5,2,6;3229,5,4,8;3230,5,2,6;3231,3,3,6;3233,3,2,4;3234,6,4,8;3236,6,4,8;3238,4,4,8;3239,5,5,11;3240,6,4,7;3243,3,3,6;502,5,4,9;508,5,4,8;510,4,1,5;512,4,4,7;514,3,2,6;516,2,2,4;517,2,2,4;520,4,1,5;522,4,1,5;525,4,5,9;526,5,1,7;531,5,4,8;533,5,1,8;537,2,2,4;539,3,3,6;541,2,2,4;543,4,1,5;547,3,2,6;548,3,3,6;549,4,5,9;554,4,1,5;555,5,4,8;559,4,1,5;564,5,4,8;565,3,4,8;1501,4,3,6;1502,3,3,6;1512,4,3,6;1516,4,1,5;1518,5,4,9;1519,4,3,6;1520,5,3,8;1527,4,3,6;2501,2,2,4;2503,3,3,6;2506,5,4,8;2508,4,3,6;2509,2,2,4;2514,2,9,9;2516,4,3,6;2518,4,3,6;2520,5,4,8;2521,4,1,5;2530,3,2,4;2533,5,4,8;2535,4,1,5;2536,5,3,6;2540,4,1,5;2542,5,1,7;2544,2,2,4;2550,4,3,7;2551,5,4,8;2555,2,2,4;2559,3,1,6;2560,2,2,4;2562,5,1,7;2563,5,4,8;2569,3,2,6;3503,5,4,7;3504,5,3,8;3505,4,4,8;3506,3,2,4;3509,4,2,6;3510,5,1,7;3513,3,4,7;3515,4,5,9;3516,5,4,8;3518,5,3,7;3520,2,2,4;3522,5,4,9;3525,5,3,8;3528,5,3,7;3530,5,4,8;3533,4,1,5;3538,4,3,7;3540,2,2,4;3541,4,3,6;3542,5,1,7;3544,2,2,4;3547,5,4,8;3549,3,8,8;3550,4,3,6;3551,3,3,6;4004,5,3,6;4005,3,4,6;4011,5,4,8;4012,5,4,8;4013,5,4,8;4016,5,3,7;4017,5,4,9;4018,5,4,8;4019,5,4,8;4020,5,4,8;4022,2,2,4;5002,4,8,8;5003,2,2,4;5007,4,5,11;5008,2,2,4;5009,4,3,6;5010,4,1,5;5013,4,1,5;5023,2,2,4;5025,4,1,5;5027,5,3,7;5029,3,2,5;5036,2,2,4;5039,3,2,6;5040,4,1,5;5041,4,1,5;5049,4,3,6;5052,4,1,5;5053,4,4,8;5054,4,4,8;5055,2,4,6;5066,4,1,5;6106,3,2,6;6115,4,1,5;6118,4,1,5;6122,2,4,6;6126,4,1,6;6143,5,1,7;6151,5,1,7;6224,4,1,5;6330,4,1,5;6334,4,1,6;6478,4,1,5;6500,4,1,5;6519,4,1,6;6581,5,1,7;6604,3,1,5;6675,4,1,6;6678,5,1,7;6713,4,1,5;6715,5,1,7;6727,5,1,7;6776,5,1,7;6779,4,1,5;6799,5,1,7;6830,3,2,6;6847,2,5,7;8038,5,4,7;8106,4,7,8;8121,5,4,8;8170,5,3,7;8178,5,4,8;8206,5,4,7;8209,5,3,8;8243,5,3,7;8291,5,3,7;8298,5,3,7;8342,5,3,8;8433,5,3,7;9019,4,3,7;9068,4,3,7;9105,4,2,6;9150,4,3,7;9162,4,2,6;9199,4,2,6;9245,4,2,7;9260,4,2,6;9265,4,2,6;9280,4,2,6;9287,4,2,7;9299,5,2,6;9310,4,2,7;9345,4,2,7;9425,4,2,7;7084,3,3,6;7088,3,3,6;7126,3,3,6;7135,2,2,4;7171,2,2,4;7227,2,2,4;7305,2,2,4;7345,2,2,4;7361,2,2,4;7427,2,2,4;7454,2,2,4;7494,3,3,6;7503,3,3,6;7515,2,2,4;7535,3,3,6;7617,3,3,6;7626,2,2,4;7684,3,3,6;7745,2,2,4;7799,2,2,4;7821,3,3,6;7826,3,3,6;7869,3,3,6;7925,2,2,4;7939,3,3,6";
    }
}
