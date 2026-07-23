using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Scores how hard a PROVEN-lethal position is to actually find, from the solved principal variation and
    /// the board. The drivers, in rough order of weight: setup moves that must precede the swings (playing a
    /// piece / firing an ability before attacking is the classic "not just swing" difficulty), spreading DON!!
    /// correctly, the defender's live defenses to play around (counters in hand, blockers on board), how many
    /// more swings than the minimum the line needs (sequencing), and the raw search the solver spent (a proxy
    /// for branchiness). Used to bucket harvested real-game positions into Easy→Expert.
    /// </summary>
    public static class DifficultyEvaluator
    {
        public struct Features
        {
            public int SetupPlays;      // playCard before the first attack
            public int Activates;       // activateMain before the first attack
            public int DonMoves;        // attachDon in the line
            public int Attacks;         // declareAttack in the line
            public int CounterCards;    // defender hand cards with a Counter value
            public int Blockers;        // defender innate blockers available to defend
            public int DefLife;         // opponent Life at the start of the puzzle
            public long Work;           // solver work units
        }

        public static Features Extract(GameState pos, string attacker, LethalSolver.Result solved)
        {
            var f = new Features { Work = solved?.Work ?? 0 };
            var mine = solved?.PrincipalVariation?.Where(c => c.Seat == null || c.Seat == attacker).ToList()
                       ?? new List<GameCommand>();
            bool seenAttack = false;
            foreach (var m in mine)
            {
                switch (m.Type)
                {
                    case "declareAttack": seenAttack = true; f.Attacks++; break;
                    case "attachDon": f.DonMoves++; break;
                    case "playCard": if (!seenAttack) f.SetupPlays++; break;
                    case "activateMain": if (!seenAttack) f.Activates++; break;
                }
            }
            var opp = pos.Players[GameEngine.OtherSeat(attacker)];
            f.DefLife = opp.Life.Count;
            f.CounterCards = opp.Hand.Count(c => (CardData.GetCard(c.CardId)?.Counter ?? 0) > 0);
            f.Blockers = opp.CharacterArea.Count(c => c != null && !c.Rested && InnateBlocker(c));
            return f;
        }

        private static bool InnateBlocker(CardInstance c)
        {
            var e = CardData.GetCard(c.CardId)?.Effect;
            return !string.IsNullOrEmpty(e) && e.TrimStart().StartsWith("[Blocker]", StringComparison.Ordinal);
        }

        public static double Score(GameState pos, string attacker, LethalSolver.Result solved)
        {
            var f = Extract(pos, attacker, solved);
            double s = 2.0;
            s += 3.0 * f.SetupPlays;                              // build the board before swinging
            s += 2.2 * f.Activates;                               // fire an ability first
            s += 1.2 * f.DonMoves;                                // DON!! allocation
            s += 1.5 * Math.Min(4, f.CounterCards);              // play around counters
            s += 1.8 * Math.Min(3, f.Blockers);                  // break/route around blockers
            s += 0.7 * Math.Max(0, f.Attacks - f.DefLife);       // extra swings over the minimum → sequencing
            s += 0.6 * Math.Max(0, Math.Log(f.Work + 1, 2) - 12); // search branchiness beyond a modest baseline
            return s;
        }

        public static int Bucket(double score) =>
            score < 4.5 ? 1 : score < 8.5 ? 2 : score < 13.5 ? 3 : 4;

        // Short human title, themed on the dominant difficulty driver (mirrors the generated-puzzle titles).
        public static string TitleFor(GameState pos, string attacker, LethalSolver.Result solved)
        {
            var f = Extract(pos, attacker, solved);
            if (f.SetupPlays > 0) return "Develop, then close";
            if (f.Activates > 0) return "Ability unlocks lethal";
            if (f.Blockers > 0) return "Break the wall";
            if (f.CounterCards >= 2) return "Beat the counters";
            if (f.DonMoves >= 2) return "Spread your DON!!";
            return "Find the exact line";
        }

        public static string LessonFor(GameState pos, string attacker, LethalSolver.Result solved)
        {
            var f = Extract(pos, attacker, solved);
            if (f.SetupPlays > 0) return "The board as-is isn't enough — put a piece down first, then swing for game.";
            if (f.Activates > 0) return "An ability, not a plain attack, is what opens the door. Fire it before you swing.";
            if (f.Blockers > 0) return "They can block a swing. Remove or route around it so your lethal attack lands.";
            if (f.CounterCards >= 2) return "They're holding counters. Sequence so the swing that matters still gets there.";
            if (f.DonMoves >= 2) return "It's all in the DON!! — spread it so every attack punches past what they can hold.";
            return "Everything's here; the order of your attacks is the whole puzzle.";
        }
    }
}
