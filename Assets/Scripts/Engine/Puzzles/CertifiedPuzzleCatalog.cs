using System;
using System.Collections.Generic;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Player-facing gold-standard catalog. Version 3 intentionally replaces the 500-entry generated manifest:
    /// every entry is an authored strategic dependency graph and no entry is a safe-card cosmetic variant.
    /// </summary>
    public static class CertifiedPuzzleCatalog
    {
        public const int TargetCount = 30;
        public const int CertificationVersion = 3;

        public static List<AuthoredPuzzle> All()
        {
            var all = new List<AuthoredPuzzle>(TargetCount);
            all.AddRange(GoldStandardPuzzleLibrary.All());
            foreach (var puzzle in MechanicPuzzleLibrary.All())
                all.Add(WithCertifiedGrade(puzzle));

            if (all.Count != TargetCount)
                throw new InvalidOperationException(
                    $"Gold-standard puzzle catalog has {all.Count} entries; expected {TargetCount}.");
            return all;
        }

        private static AuthoredPuzzle WithCertifiedGrade(AuthoredPuzzle source)
        {
            var grade = PuzzleDifficultyGrader.Authored(source.Category);
            return new AuthoredPuzzle
            {
                Id = source.Id,
                Title = source.Title,
                Attacker = source.Attacker,
                Category = source.Category,
                Teaches = source.Teaches,
                Difficulty = grade.Tier,
                DifficultyScore = grade.Score,
                DifficultyEvidence = grade.Evidence,
                PlayerTurnLimit = source.PlayerTurnLimit,
                Objective = source.Objective,
                PublicInformation = source.PublicInformation,
                Mechanics = source.Mechanics,
                Build = source.Build,
            };
        }
    }
}
