using System.Collections.Generic;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Declarative experiment definition (blueprint §15.1). One config → a deterministic, paired,
    /// parallel batch of games plus its summary. Everything here is reproducible from the config +
    /// seedPrefix alone.
    /// </summary>
    public sealed class ExperimentConfig
    {
        public string experimentId { get; set; } = "exp-001";

        /// <summary>Deck ids to cross (south × north). Empty/null ⇒ every registered starter deck.</summary>
        public List<string> decks { get; set; }

        /// <summary>Policy used by the "south"/candidate seat.</summary>
        public string southPolicy { get; set; } = "baseline";

        /// <summary>Opponent population for the "north" seat (§10.4). Each is run against every pairing.</summary>
        public List<string> opponentPolicyPool { get; set; } = new List<string> { "baseline" };

        /// <summary>Games per (deck pairing × opponent policy × starting order).</summary>
        public int gamesPerCondition { get; set; } = 100;

        /// <summary>Run each condition both south-first and north-first (§6.3).</summary>
        public bool swapStartingPlayer { get; set; } = true;

        /// <summary>Share the exact seed across the starting-order swap so the two orders draw from a
        /// matched hidden-state family (§11.1). When false, order is folded into the seed.</summary>
        public bool pairedSeeds { get; set; } = true;

        /// <summary>Emit per-decision telemetry (§12.1). Huge at scale — pair with decisionSampleRate.</summary>
        public bool saveDecisionLogs { get; set; } = false;

        /// <summary>Fraction of games (0..1) whose decisions get logged when saveDecisionLogs is on.</summary>
        public double decisionSampleRate { get; set; } = 0.01;

        public int commandCap { get; set; } = 20000;

        /// <summary>0 ⇒ Environment.ProcessorCount.</summary>
        public int maxDegreeOfParallelism { get; set; } = 0;

        public string seedPrefix { get; set; } = "exp";

        /// <summary>Root for results; the runner writes to &lt;outputDir&gt;/&lt;experimentId&gt;/.</summary>
        public string outputDir { get; set; } = "Results";
    }
}
