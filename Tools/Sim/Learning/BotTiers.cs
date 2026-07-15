using System;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Learning
{
    /// <summary>
    /// The three bot tiers this whole discovery process produces, per the user's restructuring:
    ///   • Beginner     = the game's existing hand-coded IntermediateBot (now the entry tier).
    ///   • Intermediate = the champion STYLE evolved by the first Elo population tournament
    ///                    (Results/tournament) — a tuned WeightedAgent.
    ///   • Advanced     = the per-action SEARCH bot: it enumerates every legal action and picks the
    ///                    move that plays out to the best result (rollout), with the evolved eval
    ///                    weights (Results/search-eval). This is the "every legal action discovery bot".
    /// Configs below are the settled outputs of the tournaments; re-run discovery to update them.
    /// </summary>
    public static class BotTiers
    {
        // Champion style — refined by the 37-deck meta tournament (beat the prior champion 53.7%/300).
        public static Genome IntermediateGenome() => new Genome
        {
            MulliganKeep = 4.0, FaceBias = 0.81, CounterLifeFloor = 5.0,
            CounterCharCost = 4.96, BlockBias = 0.95, TurnOrderFirst = 0.75,
        };

        // Best eval weights from search-eval evolution (used by the Advanced rollout bot).
        public static EvalWeights AdvancedEval() => new EvalWeights(new double[]
        { 1.87, 0.60, 1.20, 0.50, -0.38, -0.49, 0.40, 0.38, 0.00, -0.43 });

        public const int AdvancedRolloutCap = 4000;
        public const int AdvancedShortlist = 6;   // roll out the top-6 moves by fast eval (speed/quality balance)

        public static IAgent Make(string tier) => tier.ToLowerInvariant() switch
        {
            "beginner"     => new BaselineNamedAgent("beginner", new BaselineAgent()),
            "intermediate" => new WeightedAgent(IntermediateGenome(), "intermediate"),
            // roll out with the CHAMPION policy (not baseline) so move evaluations are accurate vs
            // strong opponents — the fix for the advanced bot losing to the intermediate champion.
            "advanced"     => new SearchAgent(AdvancedEval(), null, "advanced", AdvancedRolloutCap, AdvancedShortlist,
                                              new WeightedAgent(IntermediateGenome(), "rollout-champ")),
            _ => throw new ArgumentException($"Unknown tier '{tier}'. Tiers: beginner, intermediate, advanced."),
        };
    }
}
