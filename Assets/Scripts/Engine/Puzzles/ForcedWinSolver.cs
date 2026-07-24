using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Exact, deterministic forced-win search across turn boundaries.
    ///
    /// Unlike <see cref="LethalSolver"/>, ending the player's turn is not automatically a failure. Every
    /// player-owned decision (main-phase actions and defensive counter/block/trigger choices) is an OR node;
    /// every opponent-owned decision is an AND node. A Win therefore means the player can force the game to
    /// finish in their favour within the configured number of their turns against every enumerated opponent
    /// response. Budget/depth truncation is Unknown, never NoWin.
    ///
    /// This solver deliberately has no lossy transposition table. Multi-turn positions can draw, trigger,
    /// carry timed modifiers and alter ordered decks; a compact one-turn key would be unsound here. It is
    /// intended for tightly composed puzzle states, not unrestricted full-game analysis.
    /// </summary>
    public static class ForcedWinSolver
    {
        public enum ForcedWin { Win, NoWin, Unknown }

        public sealed class Options
        {
            public long WorkBudget = 3_000_000;
            public int MaxDepth = 140;

            /// <summary>How many of the player's turns may be used, including the current turn when it is theirs.</summary>
            public int MaxPlayerTurns = 2;

            /// <summary>
            /// Optional fixed deadline used by PuzzleRuntime when re-solving after each move. This is the
            /// PlayerState.TurnsStarted value of the last turn on which the player may win.
            /// </summary>
            public int FinalPlayerTurnsStarted = -1;

            public Func<GameCommand, bool> PlayerFilter;
            public Func<GameCommand, bool> OpponentFilter;
        }

        public sealed class Result
        {
            public ForcedWin Outcome;
            public List<GameCommand> PrincipalVariation = new List<GameCommand>();
            public long Work;
            public int Nodes;
            public bool BudgetHit;
            public int PlayerDecisionNodes;
            public int OpponentDecisionNodes;
            public long OpponentBranches;
            public int PlayerTurnsReached;
            public int FinalPlayerTurnsStarted;
        }

        private sealed class Context
        {
            public string Player;
            public Options Opt;
            public int RootPlayerTurnsStarted;
            public int FinalPlayerTurnsStarted;
            public long Work;
            public int Nodes;
            public int PlayerDecisionNodes;
            public int OpponentDecisionNodes;
            public long OpponentBranches;
            public int PlayerTurnsReached;
            public bool OutOfWork => Opt.WorkBudget > 0 && Work >= Opt.WorkBudget;
        }

        public static Result Solve(GameState root, string player, Options options = null)
        {
            options ??= new Options();
            var result = new Result();
            if (root == null || string.IsNullOrEmpty(player) || !root.Players.ContainsKey(player))
            {
                result.Outcome = ForcedWin.NoWin;
                return result;
            }

            int started = root.Players[player].TurnsStarted;
            int includedCurrentTurn = root.ActiveSeat == player ? 1 : 0;
            int finalStarted = options.FinalPlayerTurnsStarted >= 0
                ? options.FinalPlayerTurnsStarted
                : started + Math.Max(1, options.MaxPlayerTurns) - includedCurrentTurn;

            var ctx = new Context
            {
                Player = player,
                Opt = options,
                RootPlayerTurnsStarted = started,
                FinalPlayerTurnsStarted = finalStarted,
                PlayerTurnsReached = root.ActiveSeat == player ? 1 : 0,
            };
            result.Outcome = Search(GameClone.Clone(root), ctx, result.PrincipalVariation, 0);
            result.Work = ctx.Work;
            result.Nodes = ctx.Nodes;
            result.BudgetHit = ctx.OutOfWork;
            result.PlayerDecisionNodes = ctx.PlayerDecisionNodes;
            result.OpponentDecisionNodes = ctx.OpponentDecisionNodes;
            result.OpponentBranches = ctx.OpponentBranches;
            result.PlayerTurnsReached = ctx.PlayerTurnsReached;
            result.FinalPlayerTurnsStarted = finalStarted;
            return result;
        }

        private static ForcedWin Search(GameState state, Context ctx, List<GameCommand> pv, int depth)
        {
            ctx.Nodes++;
            if (state.Status == "finished")
                return LethalSolver.WinnerOf(state) == ctx.Player ? ForcedWin.Win : ForcedWin.NoWin;
            if (ctx.OutOfWork || depth >= ctx.Opt.MaxDepth) return ForcedWin.Unknown;

            var playerState = state.Players[ctx.Player];
            int turnsReached = playerState.TurnsStarted - ctx.RootPlayerTurnsStarted
                + (state.ActiveSeat == ctx.Player ? 1 : 0);
            if (turnsReached > ctx.PlayerTurnsReached) ctx.PlayerTurnsReached = turnsReached;

            // Once the final allowed player turn has been passed to the opponent, the deadline has expired.
            if (state.ActiveSeat != ctx.Player
                && playerState.TurnsStarted >= ctx.FinalPlayerTurnsStarted)
                return ForcedWin.NoWin;
            if (playerState.TurnsStarted > ctx.FinalPlayerTurnsStarted)
                return ForcedWin.NoWin;

            string decider = LethalSolver.DecidingSeat(state, ctx.Player);
            bool playerDecision = decider == ctx.Player;
            var legal = LegalActions.Validate(state, decider, LegalActions.Candidates(state, decider));
            if (playerDecision && ctx.Opt.PlayerFilter != null)
                legal = legal.Where(kv => ctx.Opt.PlayerFilter(kv.Key)).ToList();
            else if (!playerDecision && ctx.Opt.OpponentFilter != null)
                legal = legal.Where(kv => ctx.Opt.OpponentFilter(kv.Key)).ToList();

            if (legal.Count == 0)
                return playerDecision ? ForcedWin.NoWin : ForcedWin.Unknown;

            if (playerDecision)
            {
                if (legal.Count > 1) ctx.PlayerDecisionNodes++;
                ForcedWin best = ForcedWin.NoWin;
                foreach (var kv in Order(legal))
                {
                    ctx.Work++;
                    var childPv = new List<GameCommand>();
                    var outcome = Search(kv.Value, ctx, childPv, depth + 1);
                    if (outcome == ForcedWin.Win)
                    {
                        pv.Add(kv.Key);
                        pv.AddRange(childPv);
                        return ForcedWin.Win;
                    }
                    if (outcome == ForcedWin.Unknown) best = ForcedWin.Unknown;
                    if (ctx.OutOfWork) return ForcedWin.Unknown;
                }
                return best;
            }

            // Opponent decisions are universal. One escaping line refutes the forced win.
            if (legal.Count > 1) ctx.OpponentDecisionNodes++;
            ctx.OpponentBranches += legal.Count;
            ForcedWin all = ForcedWin.Win;
            GameCommand representative = null;
            List<GameCommand> representativePv = null;
            int representativeLength = -1;
            foreach (var kv in Order(legal))
            {
                ctx.Work++;
                var childPv = new List<GameCommand>();
                var outcome = Search(kv.Value, ctx, childPv, depth + 1);
                if (outcome == ForcedWin.NoWin)
                {
                    pv.Add(kv.Key);
                    pv.AddRange(childPv);
                    return ForcedWin.NoWin;
                }
                if (outcome == ForcedWin.Unknown) all = ForcedWin.Unknown;
                if (outcome == ForcedWin.Win && childPv.Count > representativeLength)
                {
                    representative = kv.Key;
                    representativePv = childPv;
                    representativeLength = childPv.Count;
                }
                if (ctx.OutOfWork) return ForcedWin.Unknown;
            }
            if (all == ForcedWin.Win && representative != null)
            {
                pv.Add(representative);
                pv.AddRange(representativePv);
            }
            return all;
        }

        private static IEnumerable<KeyValuePair<GameCommand, GameState>> Order(
            List<KeyValuePair<GameCommand, GameState>> legal)
        {
            int Rank(GameCommand c) => c.Type switch
            {
                "activateMain" => 0,
                "playCard" => 1,
                "attachDon" => 2,
                "declareAttack" => 3,
                "counterWithCard" => 4,
                "blockAttack" => 4,
                "endTurn" => 9,
                _ => 5,
            };
            return legal.OrderBy(kv => Rank(kv.Key));
        }
    }
}
