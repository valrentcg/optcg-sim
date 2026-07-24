using System.Collections.Generic;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// 1-ply value-greedy main-phase policy: at a clean-main decision, pick the legal action whose RESULTING
    /// position evaluates best (Evaluation.Score). Non-main decisions delegate to the greedy core. If the eval
    /// is FIT to the oracle's win-rate labels, this approximates the (slow) oracle at play speed — the whole
    /// point of the distillation loop. Scored on the puzzle suite to see if it beats the hand-coded bot.
    /// </summary>
    public sealed class EvalGreedyAgent : IAgent
    {
        public string Name => "eval-greedy";

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            bool cleanMain = state.ActiveSeat == seat && state.Phase == "main" && state.Battle == null
                && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null;
            if (!cleanMain) return IntermediateBot.DecideOneCommand(state, seat, blacklist);

            var cands = OnePieceTcg.Sim.Search.LegalActions.Validate(state, seat,
                OnePieceTcg.Sim.Search.LegalActions.Candidates(state, seat));
            if (cands.Count == 0) return IntermediateBot.DecideOneCommand(state, seat, blacklist);

            GameCommand best = null; double bestScore = double.NegativeInfinity;
            foreach (var kv in cands)
            {
                if (blacklist != null && blacklist.Contains(IntermediateBot.Signature(kv.cmd))) continue;
                double sc = Evaluation.Score(kv.result, seat);
                if (sc > bestScore) { bestScore = sc; best = kv.cmd; }
            }
            return best ?? IntermediateBot.DecideOneCommand(state, seat, blacklist);
        }
    }
}
