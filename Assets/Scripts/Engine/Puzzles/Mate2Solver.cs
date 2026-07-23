using System.Collections.Generic;
using OnePieceTcg.Engine.Bot.Search;   // LegalActions, GameClone

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// EXACT "mate in two turns" solver — the multi-turn extension of <see cref="LethalSolver"/>. It answers:
    /// is there a SETUP line this turn such that, for EVERY thing the opponent can do on their turn, my NEXT
    /// turn is a forced one-turn lethal?
    ///
    /// Structure (alternating, like the one-turn solver but spanning the turn boundary):
    ///   - My setup turn  = OR nodes  (a mate exists if ANY of my lines forces it). v0 SCOPE: setup only —
    ///     play / activate / attach / endTurn, no attacks this turn (the common "develop the finisher, pass,
    ///     kill next turn" shape). Attacks on the setup turn are a later extension.
    ///   - Opponent's turn = AND nodes (a mate holds only if EVERY opponent line still leaves me lethal). If the
    ///     opponent can K.O. me, or leave a board where my next turn is NOT one-turn-lethal, that line ESCAPES.
    ///   - When the opponent ends their turn -> my next turn: delegate to the proven one-turn LethalSolver.
    ///
    /// Soundness needs a KNOWN opponent deck (their draw is deterministic) — the puzzle builder stacks it.
    /// Budgeted; UNKNOWN is never conflated with NO-MATE. The opponent's turn branches hugely, so this is only
    /// tractable on tightly-constrained boards.
    /// </summary>
    public static class Mate2Solver
    {
        public enum Mate { Win, NoMate, Unknown }

        public sealed class Options
        {
            public long WorkBudget = 3_000_000;
            public int MaxDepth = 120;
            public long NextTurnBudget = 300_000;   // one-turn solve budget for the "my next turn" leaf
        }

        private sealed class Ctx
        {
            public string Me;
            public Options Opt;
            public long Work;
            public bool OutOfWork => Opt.WorkBudget > 0 && Work >= Opt.WorkBudget;
        }

        public static Mate Solve(GameState root, string me, Options opt = null)
        {
            opt ??= new Options();
            var ctx = new Ctx { Me = me, Opt = opt };
            return MySetup(GameClone.Clone(root), ctx, 0);
        }

        // OR over my setup actions this turn (no attacks in v0). endTurn hands control to the opponent.
        private static Mate MySetup(GameState s, Ctx ctx, int depth)
        {
            if (ctx.OutOfWork || depth >= ctx.Opt.MaxDepth) return Mate.Unknown;
            if (s.Status == "finished") return LethalSolver.WinnerOf(s) == ctx.Me ? Mate.Win : Mate.NoMate;
            if (s.ActiveSeat != ctx.Me) return OppTurn(s, ctx, depth);        // I ended my turn

            string decider = LethalSolver.DecidingSeat(s, ctx.Me);
            if (decider != ctx.Me)
            {
                // A non-me decision inside my own turn (my effect routed a choice): auto-resolve down the first
                // legal branch to keep v0 simple.
                var forced = LegalActions.Validate(s, decider, LegalActions.Candidates(s, decider));
                if (forced.Count == 0) return Mate.NoMate;
                ctx.Work++;
                return MySetup(forced[0].Value, ctx, depth + 1);
            }

            var legal = LegalActions.Validate(s, ctx.Me, LegalActions.Candidates(s, ctx.Me));
            Mate best = Mate.NoMate;
            foreach (var kv in legal)
            {
                if (kv.Key.Type == "declareAttack") continue;                 // v0: no attacks on the setup turn
                ctx.Work++;
                var r = MySetup(kv.Value, ctx, depth + 1);
                if (r == Mate.Win) return Mate.Win;
                if (r == Mate.Unknown) best = Mate.Unknown;
            }
            return best;
        }

        // The opponent's whole turn. AND over the opponent's choices; my own defensive choices (if they attack
        // me) are OR (I pick the line that best preserves the mate / keeps me alive). When control returns to me,
        // the leaf is a one-turn lethal check.
        private static Mate OppTurn(GameState s, Ctx ctx, int depth)
        {
            if (ctx.OutOfWork || depth >= ctx.Opt.MaxDepth) return Mate.Unknown;
            string opp = GameEngine.OtherSeat(ctx.Me);
            if (s.Status == "finished") return LethalSolver.WinnerOf(s) == ctx.Me ? Mate.Win : Mate.NoMate;

            if (s.ActiveSeat == ctx.Me)
            {
                // Opponent ended their turn -> my next turn. Forced one-turn lethal?
                var r = LethalSolver.Solve(s, ctx.Me, new LethalSolver.Options { WorkBudget = ctx.Opt.NextTurnBudget });
                return r.Outcome == LethalSolver.Lethal.Win ? Mate.Win
                     : r.Outcome == LethalSolver.Lethal.Unknown ? Mate.Unknown : Mate.NoMate;
            }

            string decider = LethalSolver.DecidingSeat(s, ctx.Me);
            var legal = LegalActions.Validate(s, decider, LegalActions.Candidates(s, decider));
            if (legal.Count == 0) return Mate.NoMate;

            if (decider == opp)
            {
                // AND: EVERY opponent line must still leave me a next-turn lethal.
                bool anyUnknown = false;
                foreach (var kv in legal)
                {
                    ctx.Work++;
                    var r = OppTurn(kv.Value, ctx, depth + 1);
                    if (r == Mate.NoMate) return Mate.NoMate;                 // opponent escapes
                    if (r == Mate.Unknown) anyUnknown = true;
                }
                return anyUnknown ? Mate.Unknown : Mate.Win;
            }
            else
            {
                // My defensive decision during their turn (block/counter to survive): OR — take the best line.
                bool anyUnknown = false;
                foreach (var kv in legal)
                {
                    ctx.Work++;
                    var r = OppTurn(kv.Value, ctx, depth + 1);
                    if (r == Mate.Win) return Mate.Win;
                    if (r == Mate.Unknown) anyUnknown = true;
                }
                return anyUnknown ? Mate.Unknown : Mate.NoMate;
            }
        }
    }
}
