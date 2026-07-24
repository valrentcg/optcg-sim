using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Difficulty/content gate for multi-turn puzzles. A forced win alone is not enough: this audit follows
    /// the solver's longest adversarial proof line and, at each player decision, re-solves every legal
    /// alternative against all opponent responses. A decision is critical only when at least one move
    /// preserves the forced win and at least one provably loses it; Unknown never counts as a loss.
    /// </summary>
    public static class ForcedWinQualityAnalyzer
    {
        public sealed class Options
        {
            public long RootBudget = 3_000_000;
            public long AlternativeBudget = 400_000;
            public int MaxDepth = 180;
            public int MaxCommandsAudited = 80;
            public int MaxPlayerDecisionsAudited = 12;
            public int MaxLegalMovesPerDecision = 64;
            public int MinCriticalDecisions = 3;
            public int MinDistinctPlayerActionTypes = 5;
            public double MaxWinningMoveRatio = 0.75;
        }

        public sealed class Decision
        {
            public int CommandIndex;
            public string ChosenType;
            public int LegalMoves;
            public int WinningMoves;
            public int LosingMoves;
            public int UnknownMoves;
            public bool Critical;
        }

        public sealed class Report
        {
            public bool Passed;
            public string Reason;
            public ForcedWinSolver.ForcedWin Outcome;
            public LethalSolver.Lethal OneTurnOutcome;
            public int PlayerTurnsReached;
            public int PlayerDecisions;
            public int CriticalDecisions;
            public int DistinctPlayerActionTypes;
            public long OpponentBranches;
            public List<Decision> Decisions = new List<Decision>();
        }

        public static Report Analyze(GameState position, string player, int playerTurnLimit,
            Options options = null)
        {
            options ??= new Options();
            var report = new Report();
            if (position == null || string.IsNullOrEmpty(player) || playerTurnLimit < 2)
                return Reject(report, "A multi-turn position, player, and turn limit are required.");

            var one = LethalSolver.Solve(position, player, new LethalSolver.Options
            {
                WorkBudget = options.AlternativeBudget,
                MaxDepth = 100,
            });
            report.OneTurnOutcome = one.Outcome;
            if (one.Outcome != LethalSolver.Lethal.NoLethal)
                return Reject(report, "The position is already lethal this turn.");

            var root = ForcedWinSolver.Solve(position, player, new ForcedWinSolver.Options
            {
                WorkBudget = options.RootBudget,
                MaxDepth = options.MaxDepth,
                MaxPlayerTurns = playerTurnLimit,
            });
            report.Outcome = root.Outcome;
            report.PlayerTurnsReached = root.PlayerTurnsReached;
            report.OpponentBranches = root.OpponentBranches;
            if (root.Outcome != ForcedWinSolver.ForcedWin.Win)
                return Reject(report, "The cross-turn win is not fully proven.");
            if (root.PlayerTurnsReached < 2)
                return Reject(report, "The proof never reaches a second player turn.");

            int finalStarted = root.FinalPlayerTurnsStarted;
            var current = GameClone.Clone(position);
            var playerTypes = new HashSet<string>(StringComparer.Ordinal);
            int auditedPlayerDecisions = 0;

            for (int commandIndex = 0;
                 commandIndex < options.MaxCommandsAudited && current.Status != "finished";
                 commandIndex++)
            {
                string decider = LethalSolver.DecidingSeat(current, player);
                var legal = LegalActions.Validate(current, decider, LegalActions.Candidates(current, decider));
                if (legal.Count == 0)
                    return Reject(report, "The proof line reached a state with no enumerated legal command.");

                var proof = ForcedWinSolver.Solve(current, player, new ForcedWinSolver.Options
                {
                    WorkBudget = options.RootBudget,
                    MaxDepth = options.MaxDepth,
                    MaxPlayerTurns = playerTurnLimit,
                    FinalPlayerTurnsStarted = finalStarted,
                });
                if (proof.Outcome != ForcedWinSolver.ForcedWin.Win || proof.PrincipalVariation.Count == 0)
                    return Reject(report, "Could not continue the adversarial proof line.");

                var wanted = proof.PrincipalVariation[0];
                var chosen = legal.FirstOrDefault(kv => SameCommand(kv.Key, wanted));
                if (chosen.Key == null)
                    return Reject(report, "A proof command was absent from the legal-action set.");

                if (decider == player)
                {
                    playerTypes.Add(chosen.Key.Type);
                    if (legal.Count > 1 && auditedPlayerDecisions < options.MaxPlayerDecisionsAudited)
                    {
                        auditedPlayerDecisions++;
                        report.PlayerDecisions++;
                        var d = AuditDecision(legal, chosen.Key.Type, player, playerTurnLimit,
                            finalStarted, commandIndex, options);
                        report.Decisions.Add(d);
                        // Unknown alternatives never count as losses or make a decision "critical". Continue
                        // auditing the line: later block/counter decisions may still be fully resolved and
                        // consequential even when an expensive root alternative exceeds the audit budget.
                        if (d.Critical) report.CriticalDecisions++;
                    }
                }
                current = chosen.Value;
            }

            report.DistinctPlayerActionTypes = playerTypes.Count;
            if (current.Status != "finished" || LethalSolver.WinnerOf(current) != player)
                return Reject(report, "The audited proof line did not reach the player's win.");
            if (report.CriticalDecisions < options.MinCriticalDecisions)
                return Reject(report,
                    $"Only {report.CriticalDecisions} consequential decisions; need {options.MinCriticalDecisions}.");
            if (report.DistinctPlayerActionTypes < options.MinDistinctPlayerActionTypes)
                return Reject(report,
                    $"Only {report.DistinctPlayerActionTypes} player action types; need {options.MinDistinctPlayerActionTypes}.");

            report.Passed = true;
            report.Reason = "Certified multi-turn forced win with selective decisions across an adversarial reply.";
            return report;
        }

        private static Decision AuditDecision(
            List<KeyValuePair<GameCommand, GameState>> legal,
            string chosenType,
            string player,
            int playerTurnLimit,
            int finalStarted,
            int commandIndex,
            Options options)
        {
            var d = new Decision
            {
                CommandIndex = commandIndex,
                ChosenType = chosenType,
                LegalMoves = legal.Count,
            };
            if (legal.Count > options.MaxLegalMovesPerDecision)
            {
                d.UnknownMoves = legal.Count;
                return d;
            }

            foreach (var kv in legal)
            {
                var r = ForcedWinSolver.Solve(kv.Value, player, new ForcedWinSolver.Options
                {
                    WorkBudget = options.AlternativeBudget,
                    MaxDepth = options.MaxDepth,
                    MaxPlayerTurns = playerTurnLimit,
                    FinalPlayerTurnsStarted = finalStarted,
                });
                if (r.Outcome == ForcedWinSolver.ForcedWin.Win) d.WinningMoves++;
                else if (r.Outcome == ForcedWinSolver.ForcedWin.NoWin) d.LosingMoves++;
                else d.UnknownMoves++;
            }

            d.Critical = d.WinningMoves > 0
                && d.LosingMoves > 0
                && d.UnknownMoves == 0
                && d.WinningMoves <= Math.Max(1, (int)Math.Floor(d.LegalMoves * options.MaxWinningMoveRatio));
            return d;
        }

        private static Report Reject(Report report, string reason)
        {
            report.Passed = false;
            report.Reason = reason;
            return report;
        }

        private static bool SameCommand(GameCommand a, GameCommand b)
        {
            if (a == null || b == null || a.Type != b.Type) return false;
            return a.Seat == b.Seat && a.InstanceId == b.InstanceId && a.Target == b.Target
                && a.Attacker == b.Attacker && a.Blocker == b.Blocker && a.EffectId == b.EffectId
                && (a.Amount ?? 1) == (b.Amount ?? 1)
                && (a.SlotIndex ?? -1) == (b.SlotIndex ?? -1);
        }
    }
}
