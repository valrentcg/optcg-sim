using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Planning;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Drives the honest turn-level <see cref="HonestPlannerBot"/> (TurnPlanner beam search on a fair
    /// determinized world) through the SAME self-play driver (MatchDriver / tiers) the shipped Advanced bot
    /// uses, so the two are comparable apples-to-apples (the `honest-play` mode uses a different runner). The
    /// planner needs both decklists at construction; we reconstruct them from the live state on first Decide —
    /// legitimate here because the bot is ALLOWED full pre-match decklist knowledge (the determinizer still
    /// hides per-card position). Ledger (ObserveApplied) isn't wired through MatchDriver, so a searched top-card
    /// is resampled rather than remembered — a small fidelity loss, acceptable for the port-decision comparison.
    /// </summary>
    public sealed class HonestPlannerAgent : IAgent
    {
        public string Name { get; }
        private readonly TurnPlanner.Options _opt;
        private readonly int _kWorlds;
        private HonestPlannerBot _bot;

        public HonestPlannerAgent(TurnPlanner.Options opt = null, int kWorlds = 1, string name = "honest-planner")
        {
            _opt = opt ?? new TurnPlanner.Options { BeamWidth = 8, MaxDepth = 12, NodeBudget = 1200, WorkBudget = 60000 };
            _kWorlds = kWorlds;
            Name = name;
        }

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;
            if (_bot == null)
            {
                var own = Reconstruct(state, seat);
                var opp = Reconstruct(state, GameEngine.OtherSeat(seat));
                bool goFirst = state.FirstPlayer == seat;
                _bot = new HonestPlannerBot(own, opp, ctx: DeckFingerprint.Analyze(own),
                    goFirst: goFirst, opt: _opt, kWorlds: _kWorlds);
            }
            return _bot.Decide(state, seat, blacklist);
        }

        private static DeckDef Reconstruct(GameState state, string seat)
        {
            var p = state.Players[seat];
            var all = new List<CardInstance>();
            all.AddRange(p.Deck); all.AddRange(p.Hand); all.AddRange(p.Life); all.AddRange(p.Trash);
            all.AddRange(p.CharacterArea.Where(c => c != null));
            if (p.Stage != null) all.Add(p.Stage);
            if (state.DeckLook != null && state.DeckLook.Seat == seat)
            {
                if (state.DeckLook.Cards != null) all.AddRange(state.DeckLook.Cards);
                if (state.DeckLook.Ordered != null) all.AddRange(state.DeckLook.Ordered);
            }
            if (state.Battle?.RevealedLife != null && state.Battle.TargetSeat == seat) all.Add(state.Battle.RevealedLife);
            var list = all.Where(c => c != null).GroupBy(c => c.CardId)
                .Select(g => (cardId: g.Key, qty: g.Count())).ToList();
            return new DeckDef { Id = "reconstructed", Leader = p.Leader?.CardId, List = list };
        }
    }
}
