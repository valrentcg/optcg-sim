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
        public Func<GameState> Build;
    }

    /// <summary>The puzzle set. The bulk is procedurally generated (<see cref="PuzzleGenerator"/>) and every
    /// entry was certified as EXACTLY forced-lethal offline by the LethalSolver `puzzlegen` pass, so the mode
    /// never ships an unsolvable "puzzle". A few hand-authored starters teach the core ideas explicitly.</summary>
    public static class PuzzleLibrary
    {
        /// <summary>Seeds the offline certifier proved are forced-lethal, grouped Easy→Expert. Reproduced
        /// byte-for-byte by <see cref="PuzzleGenerator.Build"/> in both the client and the headless Sim.</summary>
        public static readonly int[] VerifiedSeeds =
        {
            /* D1 Easy   */ 181, 18, 221, 22, 261, 27, 268, 29, 439, 31, 142, 186, 237, 259, 274, 316, 349, 355, 383, 412, 443, 478,
            /* D2 Medium */ 1, 21, 205, 6, 73, 314, 7, 99, 364, 8, 130, 407, 11, 172, 456, 19, 194, 490, 37, 295, 493, 62, 405, 505, 97, 126, 128, 246,
            /* D3 Hard   */ 43, 39, 74, 192, 56, 80, 88, 75, 123, 115, 117, 152, 132, 161, 165, 190, 169, 228, 207, 255, 252, 229, 262, 254, 241, 320, 332, 243, 361, 402,
            /* D4 Expert */ 2, 91, 32, 4, 108, 102, 16, 135, 103, 26, 141, 171, 30, 157, 226, 57, 158, 275, 58, 166,
        };

        /// <summary>The full verified puzzle set (100), each built deterministically from its seed. `Build`
        /// returns a NEW GameState each call so Restart always gets a clean board.</summary>
        public static List<AuthoredPuzzle> All()
        {
            var list = new List<AuthoredPuzzle>(VerifiedSeeds.Length);
            foreach (var seed in VerifiedSeeds)
            {
                int s = seed;                                   // capture per-iteration for the closure
                var meta = PuzzleGenerator.Build(s);            // title / teaches / difficulty / category
                list.Add(new AuthoredPuzzle
                {
                    Id = "gen-" + s,
                    Title = meta.Title,
                    Attacker = meta.AttackerSeat,
                    Category = meta.Category,
                    Teaches = meta.Teaches,
                    Difficulty = meta.Difficulty,
                    Build = () => PuzzleGenerator.Build(s).State,
                });
            }
            return list;
        }

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
                Build = () => { var (s, S, N) = Blank("puz-3"); Life(N, 1); Counter(N); Char(S, "ST01-013"); Char(S, "ST01-013"); Don(S, 2); return s; },
                // 2x 5000 + 2 DON vs 1 Life + one +1000 counter. One DON on EACH -> both 6000 -> the +1000
                // counter (Leader 5000->6000) can no longer stop either (6000 >= 6000). Overload one (7000/5000)
                // and the counter stops the 5000, only one lands -> not lethal. Teaches even distribution.
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
