using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// One-step policy improvement over <see cref="IntermediateBot"/>. At the current decision it asks the
    /// engine for every legal action, applies each action to a clone, and rolls the resulting position to the
    /// end with the known greedy policy on both seats. The greedy action is always candidate zero, so equal
    /// rollout scores preserve the competent floor. This is deliberately an opponent-model best response,
    /// not a claim of generally solved play.
    ///
    /// The caller must provide an honest determinized world. This class never receives the referee state.
    /// </summary>
    public static class AdvancedRolloutPolicy
    {
        private static long _decisions, _deviations;

        public static void ResetStats()
        {
            Interlocked.Exchange(ref _decisions, 0);
            Interlocked.Exchange(ref _deviations, 0);
        }

        public static (long decisions, long deviations) Stats() =>
            (Interlocked.Read(ref _decisions), Interlocked.Read(ref _deviations));

        public static GameCommand Decide(IReadOnlyList<GameState> worlds, string seat, HashSet<string> blacklist,
            double[] weights, DeckContext context, int maxCandidates = 28, int rolloutCommandCap = 4000)
        {
            var world = worlds?.FirstOrDefault();
            if (world == null || !world.Players.ContainsKey(seat)) return null;

            var raw = new List<GameCommand>();
            var greedy = IntermediateBot.DecideOneCommand(world, seat, blacklist);
            if (greedy != null) raw.Add(greedy); // tie-safe incumbent
            raw.AddRange(LegalActions.Candidates(world, seat));

            var seen = new HashSet<string>();
            raw = raw.Where(c => c != null
                    && !blacklist.Contains(IntermediateBot.Signature(c))
                    && seen.Add(Signature(c)))
                .ToList();
            var legal = LegalActions.Validate(world, seat, raw);
            if (legal.Count == 0) return greedy;

            // The incumbent remains first. Cap only after engine validation so rejected/no-op candidates do
            // not consume the budget. Main-phase candidate sets are normally well below this limit.
            if (legal.Count > maxCandidates) legal = legal.Take(maxCandidates).ToList();

            var scores = new List<(GameCommand cmd, double[] byWorld)>();
            foreach (var (cmd, _) in legal)
            {
                var byWorld = new double[worlds.Count];
                int wi = 0;
                foreach (var sample in worlds)
                {
                    var after = GameClone.Clone(sample);
                    long before = LegalActions.StateFingerprint(after);
                    GameEngine.ApplyCommand(after, cmd);
                    byWorld[wi++] = LegalActions.StateFingerprint(after) == before
                        ? -2_000_000
                        : RolloutScore(after, seat, weights, context, rolloutCommandCap);
                }
                scores.Add((cmd, byWorld));
            }

            // Candidate zero is the greedy incumbent. Override it only by Pareto dominance across the
            // shared sampled worlds: never worse in any world and strictly better in at least one. This
            // is deliberately conservative; the rejected mean-only version traded sampled wins for losses
            // and reproduced at 6 paired improvements / 7 regressions on the independent field block.
            GameCommand best = scores[0].cmd;
            var incumbent = scores[0].byWorld;
            int bestFlips = 0;
            foreach (var (cmd, byWorld) in scores.Skip(1))
            {
                bool noWorse = true;
                int flips = 0;
                for (int i = 0; i < byWorld.Length; i++)
                {
                    int incumbentOutcome = Math.Sign(incumbent[i]);
                    int candidateOutcome = Math.Sign(byWorld[i]);
                    if (candidateOutcome < incumbentOutcome) { noWorse = false; break; }
                    if (candidateOutcome > incumbentOutcome) flips++;
                }
                // This policy is now used only at bounded tactical branches (choice, look, target, defence,
                // Trigger), after clean-main rollouts were rejected. One actual loss-to-win flip with zero
                // sampled regressions is actionable here; position-score-only changes still never qualify.
                if (noWorse && flips >= 1 && flips > bestFlips)
                { bestFlips = flips; best = cmd; }
            }

            Interlocked.Increment(ref _decisions);
            if (Signature(best) != Signature(scores[0].cmd)) Interlocked.Increment(ref _deviations);
            return best;
        }

        private static double RolloutScore(GameState after, string seat, double[] weights,
            DeckContext context, int commandCap)
        {
            var sim = GameClone.Clone(after);
            try { IntermediateBot.PlayFullMatch(sim, commandCap); }
            catch { return -2_000_000; }

            string outright = GameRunner.Winner(sim);
            if (outright != null)
                return outright == seat ? 1_000_000 : -1_000_000;

            // A rare non-finished rollout must not masquerade as a terminal win. Use the official
            // adjudication as the primary signal, with the existing value function only as a tie-breaker.
            string result = GameRunner.Result(sim);
            double terminal = result == seat ? 100_000 : result == null ? 0 : -100_000;
            return terminal + ValueFunction.Score(sim, seat, weights, context);
        }

        private static string Signature(GameCommand c) => string.Join("|",
            c.Type, c.Seat, c.InstanceId, c.Target, c.Attacker, c.Blocker, c.EffectId, c.Amount,
            c.SlotIndex, c.GoingFirst, c.Mulligan,
            c.OrderedInstanceIds == null ? "" : string.Join(",", c.OrderedInstanceIds),
            c.DonInstanceIds == null ? "" : string.Join(",", c.DonInstanceIds));
    }
}
