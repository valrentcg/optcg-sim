using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Proves that a forced-lethal position contains real player decisions instead of merely being a board
    /// where every reasonable attack order wins. LethalSolver answers "does a win exist?"; this layer asks
    /// "how selective is the winning plan?" by solving every alternative at the first few attacker decisions.
    ///
    /// A strict report is safe to use as a content gate: UNKNOWN alternatives never count as losses, and any
    /// incomplete decision audit rejects the candidate. Generated tutorial positions may use looser options;
    /// harvested Hard/Expert positions should use <see cref="StrictHarvest"/>.
    /// </summary>
    public static class PuzzleQualityAnalyzer
    {
        public const int CurrentVersion = 1;

        public sealed class Options
        {
            public long SolveBudget = 90_000;
            public int MaxDecisionDepth = 4;
            public int MinCriticalDecisions = 2;
            public int MinAttackerMoves = 3;
            public int MinLegalMovesAtRoot = 3;
            public int MinLosingMovesAtCriticalDecision = 1;
            // Multiple command orders may express the same plan (attach DON A/B, then play removal). Permit a
            // small commutative set, but never a majority of the available actions.
            public int MaxWinningMovesAtCriticalDecision = 4;
            public double MaxWinningMoveRatioAtCriticalDecision = 0.60;
            public int MaxLegalMovesPerDecision = 48;
            public bool RejectUnknownAlternatives = true;
        }

        public static Options StrictHarvest() => new Options();

        public sealed class Decision
        {
            public int Index;
            public int LegalMoves;
            public int WinningMoves;
            public int LosingMoves;
            public int UnknownMoves;
            public bool Critical;
            public string ChosenType;
        }

        public sealed class Report
        {
            public int Version = CurrentVersion;
            public bool Passed;
            public string Reason;
            public LethalSolver.Lethal AttackOnlyOutcome;
            public int AttackerMoves;
            public int SetupMovesBeforeFirstAttack;
            public int CriticalDecisions;
            public int LegalFirstMoves;
            public int WinningFirstMoves;
            public int LosingFirstMoves;
            public int UnknownFirstMoves;
            public List<Decision> Decisions = new List<Decision>();
        }

        /// <summary>
        /// Commands allowed in an "attack from the existing board" counterfactual. Resolution commands remain
        /// allowed: rich Leaders often create [When Attacking] choices, and blocking those choices made the old
        /// swing-only test incorrectly label an ordinary alpha strike as requiring setup.
        /// </summary>
        public static bool IsAttackLineAction(GameCommand c)
        {
            if (c == null) return false;
            switch (c.Type)
            {
                case "declareAttack":
                case "resolveEffect":
                case "passEffect":
                case "resolveChoice":
                case "deckLookSelect":
                case "deckLookConfirmOrder":
                case "deckLookScryConfirm":
                case "charReplace":
                case "useTrigger":
                case "passTrigger":
                case "resolveAttack":
                    return true;
                default:
                    return false;
            }
        }

        public static Report Analyze(GameState position, string attacker,
            LethalSolver.Result proven = null, Options options = null)
        {
            options ??= StrictHarvest();
            var report = new Report();
            if (position == null || string.IsNullOrEmpty(attacker))
                return Reject(report, "Missing position or attacker.");

            proven ??= LethalSolver.Solve(position, attacker, SolveOptions(options));
            if (proven.Outcome != LethalSolver.Lethal.Win || proven.BudgetHit)
                return Reject(report, "The position is not a fully proven forced lethal.");

            var attackerPv = proven.PrincipalVariation
                .Where(c => c != null && (c.Seat == null || c.Seat == attacker))
                .ToList();
            report.AttackerMoves = attackerPv.Count(IsPlayerFacingAction);
            bool sawAttack = false;
            foreach (var c in attackerPv)
            {
                if (c.Type == "declareAttack") { sawAttack = true; continue; }
                if (!sawAttack && IsSetupAction(c)) report.SetupMovesBeforeFirstAttack++;
            }
            if (report.AttackerMoves < options.MinAttackerMoves)
                return Reject(report, $"Winning line has only {report.AttackerMoves} meaningful attacker moves.");

            var attacksOnly = LethalSolver.Solve(position, attacker, new LethalSolver.Options
            {
                WorkBudget = options.SolveBudget,
                MaxDepth = 60,
                AttackerFilter = IsAttackLineAction,
            });
            report.AttackOnlyOutcome = attacksOnly.Outcome;
            if (attacksOnly.Outcome == LethalSolver.Lethal.Unknown || attacksOnly.BudgetHit)
                return Reject(report, "Could not determine whether an attack-only line wins.");

            var current = GameClone.Clone(position);
            for (int depth = 0; depth < options.MaxDecisionDepth; depth++)
            {
                if (current.Status == "finished" || current.ActiveSeat != attacker) break;
                if (!AdvanceToAttackerDecision(current, attacker, options, out current, out var advanceError))
                    return Reject(report, advanceError);
                if (current.Status == "finished" || current.ActiveSeat != attacker) break;

                var audited = AuditDecision(current, attacker, options, depth + 1);
                report.Decisions.Add(audited.Decision);
                if (depth == 0)
                {
                    report.LegalFirstMoves = audited.Decision.LegalMoves;
                    report.WinningFirstMoves = audited.Decision.WinningMoves;
                    report.LosingFirstMoves = audited.Decision.LosingMoves;
                    report.UnknownFirstMoves = audited.Decision.UnknownMoves;
                    if (report.LegalFirstMoves < options.MinLegalMovesAtRoot)
                        return Reject(report, $"Root has only {report.LegalFirstMoves} meaningful legal moves.");
                }

                if (options.RejectUnknownAlternatives && audited.Decision.UnknownMoves > 0)
                    return Reject(report, $"Decision {depth + 1} has unresolved alternatives.");
                if (audited.Decision.WinningMoves == 0 || audited.ChosenState == null)
                    return Reject(report, $"Decision {depth + 1} has no proven winning continuation.");

                if (audited.Decision.Critical) report.CriticalDecisions++;
                current = audited.ChosenState;
            }

            if (report.CriticalDecisions < options.MinCriticalDecisions)
                return Reject(report,
                    $"Only {report.CriticalDecisions} meaningful decision(s); need {options.MinCriticalDecisions}.");

            // Attack-order puzzles are valid, but only when the audit proved the ordering is selective. This is
            // the key distinction between a real ordering puzzle and "attack with everybody in any order."
            if (report.AttackOnlyOutcome == LethalSolver.Lethal.Win
                && (report.Decisions.Count == 0 || !report.Decisions[0].Critical))
                return Reject(report, "An attack-only line wins and the opening attack is not selective.");

            report.Passed = true;
            report.Reason = report.AttackOnlyOutcome == LethalSolver.Lethal.Win
                ? "Certified attack-order puzzle with multiple consequential decisions."
                : "Certified setup puzzle with multiple consequential decisions.";
            return report;
        }

        private sealed class AuditedDecision
        {
            public Decision Decision;
            public GameState ChosenState;
        }

        private static AuditedDecision AuditDecision(GameState state, string attacker, Options options, int index)
        {
            var legal = LegalActions.Validate(state, attacker, LegalActions.Candidates(state, attacker))
                .Where(kv => kv.Key.Type != "endTurn")
                // Attack-first exposes trivial alpha strikes quickly; the complete list is still audited before
                // a candidate can pass.
                .OrderBy(kv => kv.Key.Type == "declareAttack" ? 0 : 1)
                .ToList();
            var d = new Decision { Index = index, LegalMoves = legal.Count };
            if (legal.Count == 0 || legal.Count > options.MaxLegalMovesPerDecision)
            {
                d.UnknownMoves = legal.Count > options.MaxLegalMovesPerDecision ? legal.Count : 0;
                return new AuditedDecision { Decision = d };
            }

            GameState chosen = null;
            foreach (var kv in legal)
            {
                var r = LethalSolver.Solve(kv.Value, attacker, SolveOptions(options));
                if (r.Outcome == LethalSolver.Lethal.Win && !r.BudgetHit)
                {
                    d.WinningMoves++;
                    if (chosen == null)
                    {
                        chosen = GameClone.Clone(kv.Value);
                        d.ChosenType = kv.Key.Type;
                    }
                }
                else if (r.Outcome == LethalSolver.Lethal.NoLethal)
                    d.LosingMoves++;
                else
                    d.UnknownMoves++;

            }

            d.Critical = d.WinningMoves > 0
                && d.WinningMoves <= options.MaxWinningMovesAtCriticalDecision
                && d.WinningMoves <= Math.Max(1,
                    (int)Math.Floor(legal.Count * options.MaxWinningMoveRatioAtCriticalDecision))
                && d.LosingMoves >= options.MinLosingMovesAtCriticalDecision
                && d.UnknownMoves == 0;
            return new AuditedDecision { Decision = d, ChosenState = chosen };
        }

        private static bool AdvanceToAttackerDecision(GameState start, string attacker, Options options,
            out GameState advanced, out string error)
        {
            advanced = start;
            error = null;
            for (int guard = 0; guard < 80; guard++)
            {
                if (advanced.Status == "finished" || advanced.ActiveSeat != attacker
                    || LethalSolver.DecidingSeat(advanced, attacker) == attacker)
                    return true;

                var proof = LethalSolver.Solve(advanced, attacker, SolveOptions(options));
                if (proof.Outcome != LethalSolver.Lethal.Win || proof.BudgetHit
                    || proof.PrincipalVariation.Count == 0)
                {
                    error = "Could not advance through a certified defender response.";
                    return false;
                }

                var wanted = proof.PrincipalVariation[0];
                string decider = LethalSolver.DecidingSeat(advanced, attacker);
                var legal = LegalActions.Validate(advanced, decider, LegalActions.Candidates(advanced, decider));
                var match = legal.FirstOrDefault(kv => SameCommand(kv.Key, wanted));
                if (match.Key == null)
                {
                    error = "Certified continuation was not present in the legal-action set.";
                    return false;
                }
                advanced = match.Value;
            }
            error = "Defender response did not return control to the attacker.";
            return false;
        }

        private static LethalSolver.Options SolveOptions(Options o) => new LethalSolver.Options
        {
            WorkBudget = o.SolveBudget,
            MaxDepth = 60,
        };

        private static bool IsSetupAction(GameCommand c) =>
            c.Type == "playCard" || c.Type == "activateMain" || c.Type == "attachDon";

        private static bool IsPlayerFacingAction(GameCommand c) =>
            IsSetupAction(c) || c.Type == "declareAttack";

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
