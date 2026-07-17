using System;
using System.Linq;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// A deterministic <see cref="IObservedAgent"/> for EXERCISING THE SEAM end-to-end. It needs no search
    /// and no hidden state: it chooses among the offered <see cref="ObservedAction"/>s using only the
    /// runner-supplied reproducible <see cref="PlayerDecisionContext.SeedBasis"/>. That makes it the vehicle
    /// for driving a full match — coin flip, mulligan, main-phase plays/attacks, block, counter, Trigger,
    /// pending-effect targeting, choices, deck looks — entirely through the observed boundary, proving the
    /// runner can translate every decision type without ever handing the agent a GameState.
    ///
    /// It leans toward DOING something (play/attack/target) over ending or passing, so battles actually
    /// happen and the reactive decision points get reached.
    /// </summary>
    public sealed class RandomObservedAgent : IObservedAgent
    {
        public string Name { get; }
        public RandomObservedAgent(string name = "random-observed") { Name = name; }

        public ObservedAction Decide(PlayerDecisionContext context)
        {
            if (context == null || context.Actions.Count == 0) return null;
            var rng = new Random(context.SeedBasis);   // deterministic per decision (runner-derived)

            var actions = context.Actions;
            var active = actions.Where(a => a.Type != "endTurn" && !a.Type.StartsWith("pass")).ToList();
            // Prefer to act, but always keep ending/passing reachable so turns and battles resolve.
            var pool = active.Count > 0 && rng.NextDouble() < 0.8 ? active : actions;
            return pool[rng.Next(pool.Count)];
        }
    }
}
