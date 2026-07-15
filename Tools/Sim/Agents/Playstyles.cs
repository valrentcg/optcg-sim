using System.Collections.Generic;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Seat-agnostic playstyle agents for the opponent population (§10.2/§10.4). Unlike the
    /// Heuristics decision-controllers (which force ONE decision for a fixed test seat to isolate a
    /// counterfactual), these are full opponents with a consistent style, usable on either seat — so
    /// the self-play league validates heuristics against varied styles rather than only the baseline.
    /// They only bias choices among legal commands; the engine remains the authority on legality.
    /// </summary>

    /// <summary>Aggressive "go face" style: whatever seat it plays, it redirects every attack it
    /// declares onto the opponent's leader (life-race). Attacking the leader is always legal, so this
    /// never stalls. A distinct opponent for measuring how heuristics hold up against pure pressure.</summary>
    public sealed class AggroAgent : IAgent
    {
        private readonly IAgent _inner = new BaselineAgent();
        public string Name => "aggro";

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            var cmd = _inner.Decide(state, seat, blacklist);
            if (cmd != null && cmd.Type == "declareAttack")
            {
                var oppLeader = state.Players[GameEngine.OtherSeat(seat)].Leader;
                if (oppLeader != null) cmd.Target = oppLeader.InstanceId;
            }
            return cmd;
        }
    }

    /// <summary>Conservative "hoard resources" style: never spends counter cards on defense (takes
    /// hits, keeps its hand), otherwise baseline. A distinct opponent at the opposite end of the
    /// resource-spending axis from <see cref="AggroAgent"/>.</summary>
    public sealed class ConservativeAgent : IAgent
    {
        private readonly IAgent _inner = new BaselineAgent();
        public string Name => "conservative";

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state.Battle != null && state.Battle.Step == "counter" && state.Battle.TargetSeat == seat)
                return new GameCommand { Type = "passCounter", Seat = seat };
            return _inner.Decide(state, seat, blacklist);
        }
    }
}
