using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim.Search
{
    /// <summary>
    /// Per-action search bot (the Phase-2 "chess" agent): at each main-phase / defense decision it
    /// ENUMERATES every legal move, tries each on a CLONE, scores the resulting position with the
    /// weighted <see cref="Evaluation"/>, and plays the highest-scoring move (blueprint §9 solvers,
    /// §10.6 shallow search). This is 1-ply search with a value function — the foundation to deepen
    /// into multi-ply rollouts. States it doesn't yet search (mulligan, coin flip, effect/deck-look
    /// resolution) fall through to a base policy.
    /// </summary>
    public sealed class SearchAgent : IAgent
    {
        private readonly EvalWeights _w;
        private readonly IAgent _base;
        private readonly IAgent _rolloutPolicy;  // policy used to PLAY OUT rollouts (strong ⇒ accurate)
        private readonly int _rolloutCap;   // >0 ⇒ score moves by a PLAYOUT to terminal
        private readonly int _shortlist;    // in rollout mode, only roll out the top-K by fast 1-ply eval
        public string Name { get; }

        public SearchAgent(EvalWeights w = null, IAgent basePolicy = null, string name = "search",
            int rolloutCap = 0, int shortlist = 4, IAgent rolloutPolicy = null)
        {
            _w = w ?? new EvalWeights();
            _base = basePolicy ?? new BaselineAgent();
            _rolloutPolicy = rolloutPolicy ?? _base;   // default: play out with the base policy
            _rolloutCap = rolloutCap;
            _shortlist = shortlist;
            Name = name;
        }

        // Score a resulting position: either the static eval (1-ply) or a baseline playout to terminal
        // (rollout — "if I make this move and the game plays out, do I win?"). The playout is the
        // strongest cheap signal and captures long-term consequences a 1-ply eval misses.
        private double ScoreResult(GameState result, string seat)
        {
            if (_rolloutCap <= 0) return Evaluation.Score(result, seat, _w);
            var clone = GameClone.Clone(result);
            MatchDriver.Playout(clone, _rolloutPolicy, _rolloutPolicy, _rolloutCap);
            if (clone.Status == "finished")
            {
                for (int i = clone.EventLog.Count - 1; i >= 0; i--)
                {
                    var m = clone.EventLog[i].Message;
                    if (string.IsNullOrEmpty(m) || !m.Contains("wins")) continue;
                    if (m.StartsWith("South")) return seat == "south" ? 1e6 : -1e6;
                    if (m.StartsWith("North")) return seat == "north" ? 1e6 : -1e6;
                }
            }
            return Evaluation.Score(clone, seat, _w); // unfinished playout → fall back to static eval of the deeper state
        }

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            bool searchable =
                (state.DeckLook != null && state.DeckLook.Seat == seat) ||                    // deck look / search
                (state.ActiveChoice != null && state.ActiveChoice.Seat == seat) ||            // A/B choice
                (state.PendingEffects.Any(e => e.Seat == seat) && state.DeckLook == null) ||  // effect targeting
                (state.Battle != null && state.Battle.TargetSeat == seat) ||                  // defense
                (state.ActiveSeat == seat && state.Phase == "main"                            // main phase
                 && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null);
            if (!searchable) return _base.Decide(state, seat, blacklist);

            var candidates = LegalActions.Candidates(state, seat);
            var legal = LegalActions.Validate(state, seat, candidates);
            if (legal.Count == 0) return _base.Decide(state, seat, blacklist);

            // In rollout mode, shortlist the top-K candidates by the cheap 1-ply eval first, then
            // spend the expensive rollout only on those — most moves are obviously bad and don't
            // deserve a full playout. This keeps the strong rollout signal at a fraction of the cost.
            IEnumerable<(GameCommand cmd, GameState result)> toScore = legal;
            if (_rolloutCap > 0 && _shortlist > 0 && legal.Count > _shortlist)
                toScore = legal.OrderByDescending(x => Evaluation.Score(x.result, seat, _w)).Take(_shortlist).ToList();

            GameCommand best = null; double bestScore = double.NegativeInfinity;
            foreach (var (cmd, result) in toScore)
            {
                double sc = ScoreResult(result, seat);
                if (sc > bestScore) { bestScore = sc; best = cmd; }
            }
            return best;
        }
    }
}
