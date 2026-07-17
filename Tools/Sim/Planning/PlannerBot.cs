using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// ⚠ THE PERFECT-INFORMATION CONTROL (an <see cref="ILegacyAgent"/>, NOT honest). It receives the
    /// referee <see cref="GameState"/> and searches a <see cref="SearchWorld"/> built from the UNSAFE legacy
    /// view — a verbatim referee clone. Every historical measurement was this bot; it is preserved, and
    /// still the default, ONLY as the explicit perfect-info baseline. The HONEST agent is
    /// <see cref="ObservedPlannerBot"/>, which is structurally incapable of receiving a GameState.
    ///
    /// On its own turn it asks <see cref="TurnPlanner"/> for the best full-turn sequence; every reactive
    /// decision (defence, effect resolution, mulligan, coin flip) comes from <see cref="ValuePolicy"/>. The
    /// value-function WEIGHTS are the tunable genome the tournament evolves.
    /// </summary>
    public sealed class PlannerBot : ILegacyAgent
    {
        private readonly double[] _w;
        private readonly DeckContext _ctx;
        private readonly TurnPlanner.Options _opt;
        private readonly bool _goFirst;

        private int _planTurn = -1;
        private List<GameCommand> _plan = new List<GameCommand>();
        private int _idx;

        /// <summary>Replan before EVERY command instead of once per turn (receding horizon), executing only
        /// the plan's first action. The turn plan is optimised against a MODELLED opponent defence; as soon
        /// as the real opponent defends differently, every remaining command was chosen for a game that is
        /// no longer being played. A stale command that has become a no-op is blacklisted by GameRunner,
        /// which BREAKS the turn loop if it is re-offered — so a diverged plan can end the turn early.
        /// Costs a full plan per command; this flag exists to measure whether that buys anything.</summary>
        public static bool ReplanEveryCommand = false;

        public string Name { get; }

        public PlannerBot(double[] weights = null, DeckContext ctx = null, TurnPlanner.Options opt = null,
            bool goFirst = true, string name = "planner")
        {
            _ctx = ctx ?? DeckContext.Generic;
            // Rules of engagement: bend the weights toward how THIS deck wins, decided once from the list at
            // match start. Identity unless --arch-weights is on. Applied here rather than at the call sites
            // so every driver (duel, tournament, the shipped game) gets the same bot.
            _w = ValueFunction.ApplyArchetype(weights ?? ValueFunction.DefaultWeights(), _ctx.Archetype);
            _opt = opt ?? new TurnPlanner.Options();
            _goFirst = goFirst;
            Name = name;
        }

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;

            if (state.Status == "coinflip")
                return state.CoinFlipWinner == seat
                    ? new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = _goFirst }
                    : null;

            if (state.Status == "mulligan")
            {
                var p = state.Players[seat];
                if (p.MulliganDecided) return null;
                int early = p.Hand.Count(c => { var d = CardData.GetCard(c.CardId); return d != null && d.Type == "character" && d.Cost <= 2; });
                return new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = !(early >= 1) };
            }

            // Our own turn: serve the planned sequence (plan once per turn, execute command per tick).
            if (TurnPlanner.IsMyDecision(state, seat) && state.ActiveSeat == seat)
            {
                if (ReplanEveryCommand)
                {
                    var fresh = TurnPlanner.PlanTurn(BuildWorld(state, seat), _w, _ctx, _opt);
                    if (fresh.Count > 0) return fresh[0];       // act on the first move, then replan
                }
                else if (_planTurn != state.TurnNumber)
                {
                    _plan = TurnPlanner.PlanTurn(BuildWorld(state, seat), _w, _ctx, _opt);
                    _planTurn = state.TurnNumber;
                    _idx = 0;
                }
                if (!ReplanEveryCommand && _idx < _plan.Count) return _plan[_idx++];
                // plan exhausted: cleanly end the turn, unless a stray decision is still pending (then resolve it).
                bool cleanMain = state.Phase == "main" && state.Battle == null
                    && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null;
                if (cleanMain) return new GameCommand { Type = "endTurn", Seat = seat };
                return ValuePolicy.Decide(state, seat, _w, _ctx);
            }

            // Defence and opponent-turn responses — value-driven, self-derived.
            return ValuePolicy.Decide(state, seat, _w, _ctx);
        }

        /// <summary>Build the world the planner searches. This bot is the PERFECT-INFO CONTROL, so it always
        /// wraps the referee state in the unsafe legacy view (by reference — TurnPlanner clones before it
        /// mutates, so behaviour is bit-identical to the pre-boundary code). The honest projection+sampling
        /// path lives in <see cref="ObservedPlannerBot"/>, which never sees a GameState.</summary>
        private SearchWorld BuildWorld(GameState state, string seat)
            => SearchWorld.FromLegacy(new UnsafeLegacyPlannerView { Seat = seat, Raw = state });
    }
}
