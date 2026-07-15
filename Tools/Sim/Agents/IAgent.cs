using System.Collections.Generic;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The single decision seam for the simulation platform (blueprint §2 "Policy / Value
    /// Evaluator"). An agent inspects <paramref name="state"/> for <paramref name="seat"/> and
    /// returns exactly ONE <see cref="GameCommand"/>, or null when it has nothing to do right now
    /// (opponent's turn, waiting on their defense, etc.). <c>blacklist</c> carries the signatures
    /// of commands already found to be no-ops in the current decision context — the same protocol
    /// IntermediateBot uses internally — so an agent never spins reissuing a rejected command.
    ///
    /// This is where an "advanced bot" drops in: a shallow-search agent, a tuned weighted agent,
    /// or a learned policy/value model. None of them touch the engine; they only rank/choose among
    /// what the engine already permits. Legality is always the engine's call (§1.2).
    /// </summary>
    public interface IAgent
    {
        string Name { get; }
        GameCommand Decide(GameState state, string seat, HashSet<string> blacklist);
    }

    /// <summary>
    /// Day-one baseline (blueprint §10.2): delegates straight to the shipping IntermediateBot's
    /// single-step decision. Every experiment measures candidates against this, and a newly ported
    /// deck is playable through it with no custom leader profile.
    /// </summary>
    public sealed class BaselineAgent : IAgent
    {
        public string Name => "baseline-intermediate";

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
            => IntermediateBot.DecideOneCommand(state, seat, blacklist);
    }

    /// <summary>
    /// A RandomLegalAgent stand-in for engine stress testing (§10.2). It reuses the baseline's
    /// candidate proposal but is a distinct policy identity so the runner treats it as its own
    /// population member. Kept intentionally thin until a true legal-action enumerator lands
    /// (see README "Not built yet"): the baseline already proposes only legal commands.
    /// </summary>
    public sealed class BaselineNamedAgent : IAgent
    {
        private readonly IAgent _inner;
        public BaselineNamedAgent(string name, IAgent inner) { Name = name; _inner = inner; }
        public string Name { get; }
        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
            => _inner.Decide(state, seat, blacklist);
    }
}
