using System;
using System.Collections.Generic;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>A hand-authored puzzle: a fresh-position builder plus teaching metadata. `Build` returns a NEW
    /// GameState each call so Restart always gets a clean board. Every starter is verified lethal by
    /// LethalSolver in the test suite, so the mode never ships an unsolvable "puzzle".</summary>
    public sealed class AuthoredPuzzle
    {
        public string Id;
        public string Title;
        public string Attacker = "south";
        public string Category;      // attack-order / counter-aware / don-allocation ...
        public string Teaches;       // one-line lesson shown after solving
        public int Difficulty = 1;   // 1 Easy · 2 Medium · 3 Hard · 4 Expert
        public double DifficultyScore;
        public string DifficultyEvidence;
        public int PlayerTurnLimit = 1; // 1 = lethal this turn; 2+ = forced win spanning turn boundaries
        public string Objective = "Win this turn.";
        public string[] Mechanics = Array.Empty<string>();
        public Func<GameState> Build;
    }

    /// <summary>The player-facing puzzle set. Hard/Expert positions are composed backward from a required
    /// tactical solution and admitted only after the exact decision audit proves that tempting alternatives
    /// lose. A few hand-authored starters teach the core ideas explicitly.</summary>
    public static class PuzzleLibrary
    {
        /// <summary>Seeds the offline certifier proved are forced-lethal, grouped Easy→Expert. Reproduced
        /// byte-for-byte by <see cref="PuzzleGenerator.Build"/> in both the client and the headless Sim.</summary>
        public static readonly int[] VerifiedSeeds =
        {
            /* D1 Easy   */ 18, 200, 22, 261, 27, 283, 29, 415, 31, 510, 142, 520, 186, 573, 206, 237, 259, 270, 274, 316, 342, 349, 355, 366, 383,
            /* D2 Medium */ 1, 21, 310, 6, 28, 364, 7, 63, 368, 8, 73, 526, 11, 99, 19, 130, 37, 172, 53, 194, 62, 295, 97, 405, 109,
            /* D3 Hard   */ 3, 24, 39, 192, 12, 93, 80, 398, 43, 115, 123, 67, 132, 152, 129, 365, 165, 187, 475, 183, 203, 493, 228, 214, 533, 252, 217, 637, 254, 227,
            /* D4 Expert */ 2, 10, 32, 26, 45, 49, 30, 48, 83, 81, 163, 102, 159, 176, 103, 204, 218, 171, 577, 238,
        };

        /// <summary>
        /// Seeds accepted by the solution-first synthesizer's strict decision audit. These are the Hard/Expert
        /// set: every position has at least two decision points where proven-winning and proven-losing actions
        /// coexist. The manifest intentionally keeps two examples of each tactical family instead of filling
        /// the mode with cosmetic reskins. Re-run `puzzlesynth` after any rules-engine change before altering it.
        /// </summary>
        public static readonly int[] VerifiedSynthSeeds =
        {
            /* LifeCounterOrder */ 1, 5,
            /* RushCounterGate  */ 2, 10,
            /* ExactSplit       */ 3, 8,
            /* WallAndSplit     */ 4, 9,
        };

        /// <summary>
        /// The verified player-facing set. Broad random and solution-first power-math generators remain
        /// available to offline diagnostics, but are excluded from rotation: forced lethal alone did not make
        /// their positions varied or thoughtful. Every entry here is a different mechanic dependency graph,
        /// including two positions whose proof crosses a complete opponent turn.
        /// </summary>
        public static List<AuthoredPuzzle> All()
            => CertifiedPuzzleCatalog.All();

        public static List<AuthoredPuzzle> Starters() => new List<AuthoredPuzzle>
        {
            new AuthoredPuzzle
            {
                Id = "starter-1", Title = "Count the hits", Category = "attack-order",
                Teaches = "You need Life + 1 hits to win. Here you have exactly enough - swing with everything.",
                Build = () => { var (s, S, N) = Blank("puz-1"); Life(N, 2); Char(S, "ST01-012"); Char(S, "ST01-012"); return s; },
                // Leader + 2x 6000 = 3 attackers vs 2 Life (needs 3 hits). No defense. Lethal.
            },
            new AuthoredPuzzle
            {
                Id = "starter-2", Title = "Out-swing the counter", Category = "counter-aware",
                Teaches = "They can hold back one swing. Bring one more attacker than they can stop.",
                Build = () => { var (s, S, N) = Blank("puz-2"); Life(N, 1); Counter(N); Char(S, "ST01-013"); Char(S, "ST01-013"); return s; },
                // Leader + 2x 5000 = 3 attackers vs 1 Life + one +1000 counter. Counter stops one; two still land.
            },
            new AuthoredPuzzle
            {
                Id = "starter-3", Title = "Spread the DON!!", Category = "don-allocation",
                Teaches = "Split your DON!! so EVERY swing is above what their counter can lift the Leader to - don't overload one.",
                Build = () => { var (s, S, N) = Blank("puz-3"); Life(N, 1); Counter(N); Char(S, "ST01-013"); Char(S, "ST01-013"); Don(S, 2); S.Leader.Rested = true; return s; },
                // Rested Leader, 2x 5000 + 2 DON vs 1 Life + one +1000 counter. One DON on EACH -> both
                // 6000 and both connect. Overload one (7000/5000) and the counter stops the 5000, so only one
                // hit lands. The old active-Leader setup had a free third swing and was trivially lethal.
            },
        };

        // ---- board construction --------------------------------------------------------------

        private static (GameState, PlayerState, PlayerState) Blank(string seed)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = seed });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"];
            S.TurnsStarted = 4; N.TurnsStarted = 4;
            for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
            S.Hand.Clear(); N.Hand.Clear(); S.CostArea.Clear();
            S.Leader.Rested = false; S.Leader.PlayedOnTurn = 0;
            N.Leader.Rested = false; N.Leader.PlayedOnTurn = 0;
            N.Life.Clear();
            return (st, S, N);
        }

        private static void Char(PlayerState p, string id)
        {
            // find first empty slot (reset per Blank via the null-fill above)
            for (int i = 0; i < p.CharacterArea.Count; i++)
                if (p.CharacterArea[i] == null) { p.CharacterArea[i] = In(id, p.Seat, "character"); return; }
        }
        private static void Life(PlayerState p, int n) { p.Life.Clear(); for (int i = 0; i < n; i++) p.Life.Add(In("ST01-006", p.Seat, "life")); }
        private static void Counter(PlayerState p) { p.Hand.Add(In("ST01-002", p.Seat, "hand")); }   // Usopp, counter 1000
        private static void Don(PlayerState p, int n) { for (int i = 0; i < n; i++) p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-don-{Guid.NewGuid():N}".Substring(0, 16), Rested = false }); }

        private static CardInstance In(string cardId, string owner, string zone) => new CardInstance
        {
            InstanceId = $"{owner}-{cardId}-{Guid.NewGuid():N}".Substring(0, 22),
            CardId = cardId, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
        };
    }
}
