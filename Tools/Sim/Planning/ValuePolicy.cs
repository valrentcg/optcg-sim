using System;
using System.Collections.Generic;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;   // LegalActions, GameClone

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Self-derived reactive policy: for any decision that isn't a full turn plan — DEFENCE
    /// (block / counter / trigger), effect-target resolution, A/B choices, deck-look picks, and
    /// mechanical progression — it enumerates the legal options and picks the one that maximizes the
    /// deciding seat's own <see cref="ValueFunction"/>. No IntermediateBot: every choice comes from the
    /// bot's value function, so the heuristics are entirely derived here.
    ///
    /// Used two ways: (1) the bot's OWN defence/resolution, and (2) inside the planner as the OPPONENT's
    /// best defence — evaluated from the opponent's seat, so it defends to minimize our chances, which is
    /// what makes lethal detection honest (a line only counts as lethal if it beats their best defence).
    ///
    /// DEFENCE LOOKAHEAD (fixes the "never counters/blocks" bug, 2026-07-14): scoring a defensive option
    /// on the state IMMEDIATELY after playing it is myopic and systematically wrong. At that instant the
    /// counter card has left hand (MyHand ⇒ strictly worse) but the battle has NOT resolved, so the damage
    /// it prevents does not exist yet; worse, a counter on the LEADER adds power the score cannot even see
    /// (ValueFunction.BoardPow sums CharacterArea only). Every defensive option therefore scored below
    /// "pass", so the bot passed on every attack and lost ~94% to the baseline. Evolution corroborated it
    /// by driving MyCounterReserve to ~0 — for this bot counters really were worthless. The fix: when the
    /// decision is a battle decision, play each option out until the battle RESOLVES and score that
    /// outcome, where life saved and characters kept are finally visible.
    /// </summary>
    public static class ValuePolicy
    {
        /// <summary>Re-entrancy guard: the battle roll-out calls Decide again for the follow-up decisions,
        /// and those inner calls must score myopically or the lookahead would recurse without bound.</summary>
        [ThreadStatic] private static bool _resolving;

        /// <summary>Max engine commands to play a battle out to resolution while scoring one option.
        /// A safety valve, not the real bound — a battle normally resolves in a handful of commands
        /// (block → counter → damage → trigger).</summary>
        private const int BattleResolveCap = 40;

        /// <summary>Clone budget for ONE decision's whole lookahead, across all candidates. The dominant
        /// cost of the fix was multiplicative: TurnPlanner calls this as its opponent model at EVERY node,
        /// so an unbounded roll-out here is multiplied by the node count. Bounding per-decision keeps that
        /// product finite regardless of how cluttered the board gets.</summary>
        private const long LookaheadWork = 400;

        /// <summary>How many candidates get rolled out, ranked by myopic score. "Pass" is always among them:
        /// keeping the counter card in hand scores best myopically (that bias is the very bug being fixed),
        /// so passing is guaranteed to survive the cut and stay in the running on its resolved merits.</summary>
        private const int LookaheadTopK = 4;

        /// <summary>Measurement switch: turning this off restores the myopic (pre-fix) policy, so the
        /// defence lookahead's real cost and real win-rate contribution can each be A/B'd against the same
        /// build. Never change it in a tournament run — off is the bot that loses 94% to the baseline.</summary>
        public static bool EnableLookahead = true;

        public static GameCommand Decide(GameState state, string seat, double[] w, DeckContext ctx)
        {
            if (state == null || state.Status != "active" || !state.Players.ContainsKey(seat)) return null;
            var legal = LegalActions.Validate(state, seat, LegalActions.Candidates(state, seat));
            if (legal.Count == 0) return null;
            if (legal.Count == 1) return legal[0].cmd;      // forced — nothing to weigh, don't pay to look

            // Myopic score of every option, on the state right after the command (the clone Validate made).
            var scored = new List<(GameCommand cmd, GameState st, double v)>(legal.Count);
            foreach (var (cmd, result) in legal)
                scored.Add((cmd, result, ValueFunction.Score(result, seat, w, ctx)));

            if (!ShouldLookAhead(state, seat))
                return Best(scored);

            // Roll out only the most promising handful, cheapest-to-justify first, and stop at the budget.
            scored.Sort((a, b) => b.v.CompareTo(a.v));
            long prev = LegalActions.PushDeadline(LookaheadWork);
            try
            {
                var rolled = new List<(GameCommand cmd, GameState st, double v)>(LookaheadTopK);
                for (int i = 0; i < scored.Count && rolled.Count < LookaheadTopK; i++)
                {
                    // Out of budget: keep whatever we resolved. Comparing a resolved score against a myopic
                    // one is meaningless (different scales), so a partial ranking is the honest fallback.
                    if (LegalActions.OutOfWork && rolled.Count > 0) break;
                    var (cmd, st, _) = scored[i];
                    _resolving = true;
                    try { ResolveBattle(st, w); } finally { _resolving = false; }
                    rolled.Add((cmd, st, ValueFunction.Score(st, seat, w, ctx)));
                }
                return rolled.Count > 0 ? Best(rolled) : Best(scored);
            }
            finally { LegalActions.PopDeadline(prev); }
        }

        private static GameCommand Best(List<(GameCommand cmd, GameState st, double v)> options)
        {
            GameCommand best = null;
            double bestV = double.NegativeInfinity;
            foreach (var o in options) if (o.v > bestV) { bestV = o.v; best = o.cmd; }
            return best;
        }

        /// <summary>Look ahead only where the payoff is INVISIBLE at decision time: blocking and countering.
        /// Both spend a resource now (a card leaves hand, a blocker rests) to prevent damage that only
        /// exists once the battle resolves, so scoring them in place always ranks them below passing. Every
        /// other decision — trigger, resolveAttack, effect targets, main-phase plays — is already scored on
        /// its true resulting state, so rolling it out would buy nothing and cost a great deal.</summary>
        private static bool ShouldLookAhead(GameState s, string seat)
        {
            if (!EnableLookahead || _resolving || s.Battle == null) return false;
            if (LegalActions.OutOfWork) return false;      // enclosing budget spent → myopic, don't start
            if (s.ActiveChoice != null || s.DeckLook != null || s.PendingEffects.Count > 0) return false;
            if (s.Battle.TargetSeat != seat) return false;
            return s.Battle.Step == "block" || s.Battle.Step == "counter";
        }

        /// <summary>Advance a cloned state until the current battle is over (or the game ends), so the
        /// option that led here can be scored on its OUTCOME rather than mid-battle. Follow-up decisions
        /// are taken by the same value policy, scored myopically via the _resolving guard.</summary>
        private static void ResolveBattle(GameState s, double[] w)
        {
            for (int i = 0; i < BattleResolveCap; i++)
            {
                if (s.Status != "active" || s.Battle == null) return;   // battle finished → done
                if (LegalActions.OutOfWork) return;                     // budget spent → score where we are
                string dec = DecidingSeat(s);
                if (dec == null) return;
                var cmd = Decide(s, dec, w, DeckContext.Generic);
                if (cmd == null) return;
                GameEngine.ApplyCommand(s, cmd);
            }
        }

        /// <summary>Whose decision the state is waiting on. Pending choices/effects can belong to either
        /// seat; otherwise the engine tracks it explicitly as BattleState.PrioritySeat (the defender).</summary>
        private static string DecidingSeat(GameState s)
        {
            if (s.ActiveChoice != null) return s.ActiveChoice.Seat;
            if (s.DeckLook != null) return s.DeckLook.Seat;
            if (s.PendingEffects.Count > 0) return s.PendingEffects[0].Seat;
            return s.Battle?.PrioritySeat;
        }
    }
}
