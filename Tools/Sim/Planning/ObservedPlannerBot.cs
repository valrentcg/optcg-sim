using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;
using OnePieceTcg.Sim.Runner;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// THE HONEST PLANNER. An <see cref="IObservedAgent"/>: it decides from a
    /// <see cref="PlayerDecisionContext"/> and is STRUCTURALLY incapable of receiving a
    /// <see cref="GameState"/> — that is the whole point of the enforced seam.
    ///
    /// What it can do TODAY from the observation alone (no hidden state needed): the coin flip and the
    /// mulligan. What it CANNOT do yet: any decision that requires searching a concrete world (its own turn,
    /// and every reactive/defensive decision). Those go through <see cref="SearchWorld.FromObservation"/>,
    /// which THROWS — the determinizer (§6) is gated on the mutation-site audit. It throws rather than fall
    /// back to a perfect-info world, because a silent fallback is exactly how a cheating agent would
    /// masquerade as honest. So a full honest match is not runnable until the determinizer lands; the seam
    /// itself is exercised end-to-end by <see cref="RandomObservedAgent"/>, which needs no search.
    /// </summary>
    public sealed class ObservedPlannerBot : IObservedAgent
    {
        private readonly double[] _w;
        private readonly DeckContext _ctx;
        private readonly TurnPlanner.Options _opt;
        private readonly bool _goFirst;

        public string Name { get; }

        public ObservedPlannerBot(double[] weights = null, DeckContext ctx = null, TurnPlanner.Options opt = null,
            bool goFirst = true, string name = "observed-planner")
        {
            _ctx = ctx ?? DeckContext.Generic;
            _w = ValueFunction.ApplyArchetype(weights ?? ValueFunction.DefaultWeights(), _ctx.Archetype);
            _opt = opt ?? new TurnPlanner.Options();
            _goFirst = goFirst;
            Name = name;
        }

        public ObservedAction Decide(PlayerDecisionContext context)
        {
            if (context?.Observation == null || context.Actions.Count == 0) return null;
            var obs = context.Observation;

            // Coin flip — no hidden info needed.
            if (obs.Status == "coinflip")
                return context.Actions.FirstOrDefault(a => a.GoingFirst == _goFirst) ?? context.Actions[0];

            // Mulligan — decided from the OWN HAND, which the observation carries in full. No GameState.
            if (obs.Status == "mulligan")
            {
                var hand = obs.Zones.FirstOrDefault(z => z.Owner == obs.Seat && z.Zone == "hand");
                int earlyPlays = hand?.Cards.Count(c =>
                {
                    var d = CardData.GetCard(c.CardId);
                    return d != null && d.Type == "character" && d.Cost <= 2;
                }) ?? 0;
                bool mulligan = !(earlyPlays >= 1);
                return context.Actions.FirstOrDefault(a => a.Mulligan == mulligan) ?? context.Actions[0];
            }

            // SEARCH OVER ROOT ACTIONS, NOT WORLD COMMANDS. The authoritative choice set is exactly
            // context.Actions. We evaluate the SAME root action across the sampled world(s) — translating it
            // into each world through that world's surrogate↔instance correspondence — and return the best
            // root action DIRECTLY. We never let the search pick an arbitrary world command and then guess
            // which root action it meant; that guessing is the defect this replaces.
            //
            // ⚠ GATED: SearchWorld.FromObservation throws until the determinizer (§6) lands, and K-world
            // aggregation is task-11+. The shape below is the honest architecture, not a reachable path.
            return SelectBestRootAction(context, obs);
        }

        /// <summary>Score each offered root <see cref="ObservedAction"/> by translating it into the sampled
        /// world(s) and evaluating there, then return the best. No CardId heuristic, no Type fallback — the
        /// returned action IS one of the authoritative offers. Gated on the determinizer.</summary>
        private ObservedAction SelectBestRootAction(PlayerDecisionContext context, PlayerObservation obs)
        {
            // One world today; a WorldBatch over context.SeedBasis + world index when K-world lands. Throws
            // (determinizer gated) — deliberately, rather than fall back to a perfect-info world.
            var world = SearchWorld.FromObservation(obs, context.SeedBasis);

            ObservedAction best = null;
            double bestVal = double.NegativeInfinity;
            foreach (var action in context.Actions)
            {
                // Reconstruct the command IN THIS WORLD via the type-aware, fail-closed translator (declareAttack
                // ⇒ Attacker, resolveEffect ⇒ EffectId, …). Then ENGINE-VALIDATE it before scoring: a command
                // that does not change the world is not a legal move here and is skipped.
                var worldCmd = RootActionTranslator.ToWorldCommand(action, world.Seat,
                    world.SurrogateToInstance, world.PendingRefToEffectId);
                if (worldCmd == null) continue;                       // unresolved reference ⇒ fail closed
                var eval = GameClone.Clone(world.State);
                long before = LegalActions.StateFingerprint(eval);
                GameEngine.ApplyCommand(eval, worldCmd);
                if (LegalActions.StateFingerprint(eval) == before) continue;   // not legal in this world
                double v = ValueFunction.Score(eval, obs.Seat, _w, _ctx);
                if (v > bestVal) { bestVal = v; best = action; }
            }
            return best ?? context.Actions[0];
        }
    }
}
