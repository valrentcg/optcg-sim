using System;
using System.Collections.Generic;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Solution-first puzzle composition. Unlike PuzzleGenerator's one-motif templates and PuzzleHarvester's
    /// "take whatever a replay happens to contain", this synthesizer starts from interacting constraints:
    ///
    ///   - exact DON allocation,
    ///   - a defensive resource that invalidates a lopsided split,
    ///   - blocker removal or Life-to-hand counter timing,
    ///   - plausible resource-wasting decoys.
    ///
    /// Build() only proposes positions. The offline `puzzlesynth` pass must prove forced lethal and pass
    /// PuzzleQualityAnalyzer before a seed is baked into PuzzleLibrary. Card identity varies within curated,
    /// color-legal role pools; the tactical constraint system stays invariant.
    /// </summary>
    public static class PuzzleSynthesizer
    {
        public enum Family { ExactSplit, WallAndSplit, LifeCounterOrder, RushCounterGate }

        public sealed class Candidate
        {
            public int Seed;
            public Family Kind;
            public GameState State;
            public string Attacker = "south";
            public string Category;
            public string Title;
            public string Teaches;
            public int Difficulty;
        }

        private static readonly string[] GreenVanilla5K =
            { "OP01-043", "OP02-028", "OP05-035", "OP06-031", "OP12-035" };
        private static readonly string[] BlueVanilla1K =
            { "EB01-025", "OP01-065", "OP01-066", "OP01-076" };
        private static readonly string[] BlueVanilla2K =
            { "OP11-045", "OP11-053", "OP11-055", "OP12-049", "OP12-055" };
        private static readonly string[] BlueBlockers =
            { "OP03-050", "OP05-052", "OP09-054", "OP16-044" };
        private static readonly (string id, int cost)[] GreenResters =
            { ("EB01-015", 1), ("OP01-048", 2), ("OP01-033", 3) };

        public static Candidate Build(int seed)
        {
            var rng = new Rng(seed);
            var family = (Family)rng.Range(0, 3);
            return family switch
            {
                Family.ExactSplit => BuildExactSplit(seed, ref rng),
                Family.WallAndSplit => BuildWallAndSplit(seed, ref rng),
                Family.LifeCounterOrder => BuildLifeCounterOrder(seed, ref rng),
                _ => BuildRushCounterGate(seed, ref rng),
            };
        }

        private static Candidate BuildExactSplit(int seed, ref Rng rng)
        {
            var (state, mine, opp, ids) = Blank(seed);
            AddCharacter(mine, Pick(GreenVanilla5K, ref rng), ids);
            AddCharacter(mine, PickDifferent(GreenVanilla5K, mine.CharacterArea[0].CardId, ref rng), ids);
            AddLife(opp, "EB03-026", 1, ids);                    // inert, 0-counter Life
            opp.Hand.Add(Card(Pick(BlueVanilla1K, ref rng), "north", "hand", ids));
            AddDon(mine, 2, ids);
            AddDecoy(mine, ids, ref rng);                        // legal-looking but spends the exact DON
            return new Candidate
            {
                Seed = seed, Kind = Family.ExactSplit, State = state,
                Category = "constraint-synth",
                Title = "No Room for Error",
                Teaches = "The counter makes a lopsided DON!! split fail. Give each attacker exactly enough power before committing either swing.",
                Difficulty = 3,
            };
        }

        private static Candidate BuildWallAndSplit(int seed, ref Rng rng)
        {
            var (state, mine, opp, ids) = Blank(seed);
            AddCharacter(mine, Pick(GreenVanilla5K, ref rng), ids);
            AddCharacter(mine, PickDifferent(GreenVanilla5K, mine.CharacterArea[0].CardId, ref rng), ids);
            AddCharacter(opp, Pick(BlueBlockers, ref rng), ids);
            AddLife(opp, "EB03-026", 1, ids);
            opp.Hand.Add(Card(Pick(BlueVanilla1K, ref rng), "north", "hand", ids));
            var rester = GreenResters[rng.Range(0, GreenResters.Length - 1)];
            mine.Hand.Add(Card(rester.id, "south", "hand", ids));
            AddDon(mine, rester.cost + 2, ids);
            AddDecoy(mine, ids, ref rng);
            return new Candidate
            {
                Seed = seed, Kind = Family.WallAndSplit, State = state,
                Category = "constraint-synth",
                Title = "The Narrow Path",
                Teaches = "Three constraints overlap: neutralize the Blocker, preserve enough DON!! for both attackers, and avoid a split the counter can punish.",
                Difficulty = 4,
            };
        }

        private static Candidate BuildLifeCounterOrder(int seed, ref Rng rng)
        {
            var (state, mine, opp, ids) = Blank(seed);
            AddCharacter(mine, Pick(GreenVanilla5K, ref rng), ids);
            AddCharacter(mine, PickDifferent(GreenVanilla5K, mine.CharacterArea[0].CardId, ref rng), ids);
            // The top Life card becomes a known 2K counter after the first hit. With only two DON, the solution
            // is asymmetric: leave one attacker at 5K for the first swing and build the other to 7K for last.
            AddLife(opp, Pick(BlueVanilla2K, ref rng), 1, ids);
            AddDon(mine, 2, ids);
            AddDecoy(mine, ids, ref rng);
            return new Candidate
            {
                Seed = seed, Kind = Family.LifeCounterOrder, State = state,
                Category = "constraint-synth",
                Title = "Read the Whole Position",
                Teaches = "The first hit changes the defense. Attack with the unboosted body first, then use the 7K attacker after the Life card becomes a 2K counter.",
                Difficulty = 4,
            };
        }

        private static Candidate BuildRushCounterGate(int seed, ref Rng rng)
        {
            var (state, mine, opp, ids) = Blank(seed);
            SetLeader(mine, "OP01-001", rested: true);           // Red Zoro
            SetLeader(opp, "ST06-001", rested: false);           // Black Sakazuki
            AddCharacter(opp, "OP02-108", ids);                  // Black 2-cost Blocker
            opp.Hand.Add(Card("OP07-095", "north", "hand", ids)); // Iron Body: +4K for two DON
            AddDon(opp, 2, ids);

            mine.Hand.Add(Card("ST01-012", "south", "hand", ids)); // 5-cost 6K Rush; DON x2=no Blocker
            mine.Hand.Add(Card("ST01-013", "south", "hand", ids)); // plausible resource-wasting decoys
            mine.Hand.Add(Card("ST01-013", "south", "hand", ids));
            AddDon(mine, 8, ids);                                // exactly 5 to play + 3 to reach 9K
            return new Candidate
            {
                Seed = seed, Kind = Family.RushCounterGate, State = state,
                Category = "constraint-synth",
                Title = "One Chance",
                Teaches = "The Rush attacker needs 2 DON!! to switch off the Blocker and a third to reach 9K through Iron Body. Any decoy play spends the winning resource.",
                Difficulty = 4,
            };
        }

        private static (GameState state, PlayerState mine, PlayerState opp, Ids ids) Blank(int seed)
        {
            var state = GameEngine.CreateMatch(new MatchConfig
            {
                SouthDeck = "st02", NorthDeck = "st03", Seed = "synth-" + seed,
            });
            state.Status = "active";
            state.Phase = "main";
            state.ActiveSeat = "south";
            state.TurnNumber = 8;
            var mine = state.Players["south"];
            var opp = state.Players["north"];
            mine.TurnsStarted = 4;
            opp.TurnsStarted = 4;
            for (int i = 0; i < 5; i++) { mine.CharacterArea[i] = null; opp.CharacterArea[i] = null; }
            mine.Hand.Clear(); opp.Hand.Clear();
            mine.Life.Clear(); opp.Life.Clear();
            mine.Trash.Clear(); opp.Trash.Clear();
            mine.CostArea.Clear(); opp.CostArea.Clear();
            mine.Stage = null; opp.Stage = null;
            SetLeader(mine, "EB01-001", rested: true);           // Green/Red Oden; no Activate: Main decoy
            SetLeader(opp, "ST03-001", rested: false);           // Blue Crocodile
            return (state, mine, opp, new Ids(seed));
        }

        private static void SetLeader(PlayerState p, string id, bool rested)
        {
            p.Leader.CardId = id;
            p.Leader.Rested = rested;
            p.Leader.PlayedOnTurn = 0;
            p.Leader.AttachedDonIds.Clear();
        }

        private static void AddCharacter(PlayerState p, string id, Ids ids)
        {
            for (int i = 0; i < p.CharacterArea.Count; i++)
                if (p.CharacterArea[i] == null)
                {
                    p.CharacterArea[i] = Card(id, p.Seat, "character", ids);
                    return;
                }
        }

        private static void AddLife(PlayerState p, string id, int count, Ids ids)
        {
            p.Life.Clear();
            for (int i = 0; i < count; i++) p.Life.Add(Card(id, p.Seat, "life", ids));
        }

        private static void AddDon(PlayerState p, int count, Ids ids)
        {
            for (int i = 0; i < count; i++)
                p.CostArea.Add(new DonInstance { InstanceId = ids.Next("don"), Rested = false });
        }

        private static void AddDecoy(PlayerState mine, Ids ids, ref Rng rng)
        {
            // A color-legal vanilla play that looks productive but consumes the exact resource budget and is
            // summoning-sick. Quality analysis must prove that playing it drops the lethal.
            mine.Hand.Add(Card(Pick(GreenVanilla5K, ref rng), "south", "hand", ids));
        }

        private static CardInstance Card(string id, string owner, string zone, Ids ids) => new CardInstance
        {
            InstanceId = ids.Next(owner + "-" + id),
            CardId = id,
            Owner = owner,
            Zone = zone,
            Rested = false,
            PlayedOnTurn = 0,
        };

        private static string Pick(string[] values, ref Rng rng) => values[rng.Range(0, values.Length - 1)];

        private static string PickDifferent(string[] values, string other, ref Rng rng)
        {
            string picked;
            do picked = Pick(values, ref rng); while (picked == other && values.Length > 1);
            return picked;
        }

        private sealed class Ids
        {
            private readonly int _seed;
            private int _next;
            public Ids(int seed) { _seed = seed; }
            public string Next(string prefix)
            {
                string clean = prefix.Replace(" ", "").Replace("\"", "");
                if (clean.Length > 18) clean = clean.Substring(0, 18);
                return $"{clean}-{_seed}-{_next++}";
            }
        }

        private struct Rng
        {
            private ulong _state;
            public Rng(int seed)
            {
                _state = ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) ^ 0xA0761D6478BD642FUL;
                if (_state == 0) _state = 1;
            }
            private uint Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 7;
                _state ^= _state << 17;
                return (uint)(_state ^ (_state >> 32));
            }
            public int Range(int min, int maxInclusive) =>
                min + (int)(Next() % (uint)(maxInclusive - min + 1));
        }
    }
}
