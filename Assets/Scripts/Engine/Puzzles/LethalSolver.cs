using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OnePieceTcg.Engine.Bot.Search;   // LegalActions, GameClone (the SHIPPED search substrate)

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// EXACT one-turn forced-win (lethal) solver — the proof engine behind the puzzle / brain-teaser mode.
    /// Ships in the game so the puzzle runtime can verify the player's line live.
    ///
    /// It is NOT the AI's beam search. The bot prunes and models the defender with one policy, so it can
    /// FIND a winning line but cannot PROVE lethal. This is a full AND/OR (minimax) search over the real
    /// engine:
    ///   - Attacker decisions = OR nodes  (lethal if ANY action forces a win).
    ///   - Defender decisions = AND nodes (lethal only if ALL legal defenses still lose) — enumerated
    ///     exhaustively, never pruned, so a WIN is a real proof against optimal defense.
    ///   - Terminal: the attacker's win condition fires -> WIN; the turn ends with no win -> NO-LETHAL.
    ///   - Budget exhausted -> UNKNOWN, which is NEVER conflated with NO-LETHAL.
    /// The engine itself is the legality authority (LegalActions.Validate = clone+apply+diff).
    ///
    /// Scope (v1): a fully-known, deterministic position (known hands, known Life order, no shuffles).
    /// </summary>
    public static class LethalSolver
    {
        public enum Lethal { Win, NoLethal, Unknown }

        public sealed class Result
        {
            public Lethal Outcome;
            /// <summary>One representative proof/refutation line (attacker + defender commands interleaved).</summary>
            public List<GameCommand> PrincipalVariation = new List<GameCommand>();
            public long Work;         // legal moves explored (deterministic cost proxy)
            public int Nodes;         // search nodes visited
            public bool BudgetHit;    // true if the search stopped on the budget (so any UNKNOWN is real)
        }

        public sealed class Options
        {
            public long WorkBudget = 3_000_000;   // legal-move-expansion cap (0 = unlimited). UNKNOWN once spent.
            public int MaxDepth = 60;             // hard cap on command depth within the turn
            public bool UseTransposition = true;
        }

        private sealed class Ctx
        {
            public string Attacker;
            public Options Opt;
            public Dictionary<string, Lethal> Memo;
            public long Work;
            public int Nodes;
            public bool OutOfWork => Opt.WorkBudget > 0 && Work >= Opt.WorkBudget;
        }

        public static Result Solve(GameState root, string attacker, Options opt = null)
        {
            opt ??= new Options();
            var ctx = new Ctx
            {
                Attacker = attacker,
                Opt = opt,
                Memo = opt.UseTransposition ? new Dictionary<string, Lethal>() : null,
            };
            var res = new Result();
            var start = GameClone.Clone(root);
            res.Outcome = Search(start, ctx, res.PrincipalVariation, 0);
            res.Work = ctx.Work;
            res.Nodes = ctx.Nodes;
            res.BudgetHit = ctx.OutOfWork;
            return res;
        }

        private static Lethal Search(GameState state, Ctx ctx, List<GameCommand> pv, int depth)
        {
            ctx.Nodes++;
            string attacker = ctx.Attacker;

            // ---- terminals ----------------------------------------------------------------
            if (state.Status == "finished")
                return WinnerOf(state) == attacker ? Lethal.Win : Lethal.NoLethal;
            // ActiveSeat flips only at end-of-turn: during the attacker's own turn it stays the attacker even
            // while the defender blocks/counters. So a flipped ActiveSeat means the turn ended with no win.
            if (state.ActiveSeat != attacker) return Lethal.NoLethal;
            if (ctx.OutOfWork || depth >= ctx.Opt.MaxDepth) return Lethal.Unknown;

            // ---- transposition ------------------------------------------------------------
            string key = null;
            if (ctx.Memo != null)
            {
                key = PositionKey(state, attacker);
                if (ctx.Memo.TryGetValue(key, out var cached)) return cached;
            }

            string decider = DecidingSeat(state, attacker);
            bool attackerToMove = decider == attacker;

            var legal = LegalActions.Validate(state, decider, LegalActions.Candidates(state, decider));
            if (legal.Count == 0)
            {
                var stuck = attackerToMove ? Lethal.NoLethal : Lethal.Unknown;
                if (ctx.Memo != null && stuck != Lethal.Unknown) ctx.Memo[key] = stuck;
                return stuck;
            }

            Lethal outcome;
            if (attackerToMove)
            {
                // OR node: WIN if any child wins. Progress moves first, endTurn last.
                outcome = Lethal.NoLethal;
                foreach (var kv in Order(legal))
                {
                    ctx.Work++;
                    var childPv = new List<GameCommand>();
                    var r = Search(kv.Value, ctx, childPv, depth + 1);
                    if (r == Lethal.Win)
                    {
                        pv.Add(kv.Key); pv.AddRange(childPv);
                        if (ctx.Memo != null) ctx.Memo[key] = Lethal.Win;
                        return Lethal.Win;
                    }
                    if (r == Lethal.Unknown) outcome = Lethal.Unknown;
                    if (ctx.OutOfWork) { outcome = Lethal.Unknown; break; }
                }
            }
            else
            {
                // AND node (defender): lethal only if EVERY defense still loses.
                outcome = Lethal.Win;
                GameCommand firstCmd = null; List<GameCommand> firstPv = null;
                foreach (var kv in legal)
                {
                    ctx.Work++;
                    var childPv = new List<GameCommand>();
                    var r = Search(kv.Value, ctx, childPv, depth + 1);
                    if (r == Lethal.NoLethal)
                    {
                        pv.Add(kv.Key); pv.AddRange(childPv);
                        if (ctx.Memo != null) ctx.Memo[key] = Lethal.NoLethal;
                        return Lethal.NoLethal;   // a defense escapes -> refutation
                    }
                    if (r == Lethal.Unknown) outcome = Lethal.Unknown;
                    if (firstCmd == null) { firstCmd = kv.Key; firstPv = childPv; }
                    if (ctx.OutOfWork) { outcome = Lethal.Unknown; break; }
                }
                if (outcome == Lethal.Win && firstCmd != null) { pv.Add(firstCmd); pv.AddRange(firstPv); }
            }

            if (ctx.Memo != null && outcome != Lethal.Unknown) ctx.Memo[key] = outcome;
            return outcome;
        }

        private static IEnumerable<KeyValuePair<GameCommand, GameState>> Order(
            List<KeyValuePair<GameCommand, GameState>> legal)
        {
            int Rank(GameCommand c) => c.Type switch
            {
                "declareAttack" => 0, "activateMain" => 1, "playCard" => 2, "attachDon" => 3, "endTurn" => 9, _ => 4,
            };
            return legal.OrderBy(kv => Rank(kv.Key));
        }

        // The seat that owns the current decision.
        public static string DecidingSeat(GameState s, string attacker)
        {
            if (s.ActiveChoice != null) return s.ActiveChoice.Seat;
            if (s.DeckLook != null) return s.DeckLook.Seat;
            if (s.PendingEffects.Count > 0) return s.PendingEffects[0].Seat;
            if (s.Battle != null) return s.Battle.TargetSeat ?? s.Battle.PrioritySeat ?? GameEngine.OtherSeat(attacker);
            return s.ActiveSeat;
        }

        public static string WinnerOf(GameState g)
        {
            for (int i = g.EventLog.Count - 1; i >= 0; i--)
            {
                var m = g.EventLog[i].Message;
                if (string.IsNullOrEmpty(m) || m.IndexOf("wins", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (m.StartsWith("South", StringComparison.Ordinal)) return "south";
                if (m.StartsWith("North", StringComparison.Ordinal)) return "north";
            }
            return null;
        }

        // Complete, canonical key so transposition is SOUND (no false merges).
        private static string PositionKey(GameState s, string attacker)
        {
            var sb = new StringBuilder(256);
            sb.Append('T').Append(s.TurnNumber).Append('|').Append(s.Phase).Append('|').Append(s.ActiveSeat);
            if (s.Battle != null)
                sb.Append("|B:").Append(s.Battle.Step).Append(',').Append(s.Battle.AttackerId).Append(',')
                  .Append(s.Battle.TargetId).Append(',').Append(s.Battle.Blocked).Append(',')
                  .Append(s.Battle.CounterPower).Append(',').Append(s.Battle.RevealedLife?.CardId);
            if (s.ActiveChoice != null) sb.Append("|C:").Append(s.ActiveChoice.Seat);
            if (s.DeckLook != null) sb.Append("|D:").Append(s.DeckLook.Seat).Append(',').Append(s.DeckLook.Cards.Count);
            foreach (var pe in s.PendingEffects) sb.Append("|E:").Append(pe.Seat).Append(',').Append(pe.EffectId).Append(',').Append(pe.Timing);
            foreach (var seat in new[] { attacker, GameEngine.OtherSeat(attacker) })
            {
                var p = s.Players[seat];
                sb.Append("\n#").Append(seat)
                  .Append(" don").Append(GameEngine.ActiveDonCount(p)).Append('/').Append(p.CostArea.Count)
                  .Append(" life").Append(p.Life.Count).Append(" trash").Append(p.Trash.Count).Append(" deck").Append(p.Deck.Count);
                sb.Append(" L:"); Piece(sb, s, p.Leader);
                sb.Append(" A:");
                foreach (var c in p.CharacterArea) { Piece(sb, s, c); sb.Append(';'); }
                sb.Append(" H:");
                foreach (var id in p.Hand.Select(c => c.CardId).OrderBy(x => x, StringComparer.Ordinal)) sb.Append(id).Append(',');
                sb.Append(" F:");
                foreach (var c in p.Life) sb.Append(c.CardId).Append(',');
            }
            return sb.ToString();
        }

        private static void Piece(StringBuilder sb, GameState s, CardInstance c)
        {
            if (c == null) { sb.Append('.'); return; }
            sb.Append(c.CardId).Append(c.Rested ? 'r' : 'a')
              .Append('p').Append(GameEngine.GetPower(s, c))
              .Append('d').Append(c.AttachedDonIds.Count)
              .Append('t').Append(c.PlayedOnTurn);
        }
    }
}
