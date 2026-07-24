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
            /// <summary>Optional restriction on the ATTACKER's moves (defender moves are never filtered, so a
            /// WIN under a filter is still a real proof against optimal defense). Used to ask counterfactuals
            /// like "can the attacker win using ONLY attacks?" — the basis of the puzzle difficulty grader.</summary>
            public Func<GameCommand, bool> AttackerFilter;
        }

        private sealed class Ctx
        {
            public string Attacker;
            public Options Opt;
            public Dictionary<string, MemoEntry> Memo;
            public long Work;
            public int Nodes;
            public bool OutOfWork => Opt.WorkBudget > 0 && Work >= Opt.WorkBudget;
        }

        private sealed class MemoEntry
        {
            public Lethal Outcome;
            public List<GameCommand> PrincipalVariation;
        }

        public static Result Solve(GameState root, string attacker, Options opt = null)
        {
            opt ??= new Options();
            var ctx = new Ctx
            {
                Attacker = attacker,
                Opt = opt,
                Memo = opt.UseTransposition ? new Dictionary<string, MemoEntry>() : null,
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
                if (ctx.Memo.TryGetValue(key, out var cached))
                {
                    if (cached.PrincipalVariation != null) pv.AddRange(cached.PrincipalVariation);
                    return cached.Outcome;
                }
            }

            string decider = DecidingSeat(state, attacker);
            bool attackerToMove = decider == attacker;

            var legal = LegalActions.Validate(state, decider, LegalActions.Candidates(state, decider));
            if (attackerToMove && ctx.Opt.AttackerFilter != null)
                legal = legal.Where(kv => ctx.Opt.AttackerFilter(kv.Key)).ToList();
            if (legal.Count == 0)
            {
                var stuck = attackerToMove ? Lethal.NoLethal : Lethal.Unknown;
                if (ctx.Memo != null && stuck != Lethal.Unknown) Memoize(ctx, key, stuck, pv);
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
                        if (ctx.Memo != null) Memoize(ctx, key, Lethal.Win, pv);
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
                GameCommand representativeCmd = null;
                List<GameCommand> representativePv = null;
                int representativeLength = -1;
                foreach (var kv in legal)
                {
                    ctx.Work++;
                    var childPv = new List<GameCommand>();
                    var r = Search(kv.Value, ctx, childPv, depth + 1);
                    if (r == Lethal.NoLethal)
                    {
                        pv.Add(kv.Key); pv.AddRange(childPv);
                        if (ctx.Memo != null) Memoize(ctx, key, Lethal.NoLethal, pv);
                        return Lethal.NoLethal;   // a defense escapes -> refutation
                    }
                    if (r == Lethal.Unknown) outcome = Lethal.Unknown;
                    // The proof is universal over every defense. For the displayed line/hints, retain the
                    // defense that forces the longest continuation instead of the first enumerated "pass".
                    // This mirrors how PuzzleRuntime resists and exposes the actual counter/block sequence.
                    if (r == Lethal.Win && childPv.Count > representativeLength)
                    {
                        representativeCmd = kv.Key;
                        representativePv = childPv;
                        representativeLength = childPv.Count;
                    }
                    if (ctx.OutOfWork) { outcome = Lethal.Unknown; break; }
                }
                if (outcome == Lethal.Win && representativeCmd != null)
                {
                    pv.Add(representativeCmd);
                    pv.AddRange(representativePv);
                }
            }

            if (ctx.Memo != null && outcome != Lethal.Unknown) Memoize(ctx, key, outcome, pv);
            return outcome;
        }

        private static void Memoize(Ctx ctx, string key, Lethal outcome, List<GameCommand> pv)
        {
            ctx.Memo[key] = new MemoEntry
            {
                Outcome = outcome,
                PrincipalVariation = pv == null ? new List<GameCommand>() : new List<GameCommand>(pv),
            };
        }

        private static IEnumerable<KeyValuePair<GameCommand, GameState>> Order(
            List<KeyValuePair<GameCommand, GameState>> legal)
        {
            // Setup-before-attack ordering: real puzzle lethals almost always play/activate/attach FIRST, then
            // swing. Trying setup moves before attacks lets the OR node hit a winning line without first
            // exhausting every (losing) pure-attack ordering — a large speedup for the generator's certifier.
            int Rank(GameCommand c) => c.Type switch
            {
                "activateMain" => 0, "playCard" => 1, "attachDon" => 2, "declareAttack" => 3, "endTurn" => 9, _ => 4,
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
            if (s.ActiveChoice != null)
                sb.Append("|C:").Append(s.ActiveChoice.Seat).Append(',').Append(s.ActiveChoice.ControllerSeat)
                  .Append(',').Append(s.ActiveChoice.OptionA).Append(',').Append(s.ActiveChoice.OptionB)
                  .Append(',').Append(s.ActiveChoice.OptionC);
            if (s.DeckLook != null)
            {
                sb.Append("|D:").Append(s.DeckLook.Seat).Append(',').Append(s.DeckLook.Step).Append(',');
                foreach (var c in s.DeckLook.Cards) sb.Append(c.InstanceId).Append(',');
            }
            foreach (var pe in s.PendingEffects)
                sb.Append("|E:").Append(pe.Seat).Append(',').Append(pe.EffectId).Append(',').Append(pe.Timing)
                  .Append(',').Append(pe.Text).Append(',').Append(pe.SelectionsRemaining)
                  .Append(',').Append(pe.RemainingBudget).Append(',').Append(pe.FirstPickId);
            foreach (var m in s.ActiveModifiers.OrderBy(m => m.TargetInstanceId, StringComparer.Ordinal)
                                                .ThenBy(m => m.ModifierType, StringComparer.Ordinal)
                                                .ThenBy(m => m.Keyword, StringComparer.Ordinal))
                sb.Append("|M:").Append(m.SourceInstanceId).Append('>').Append(m.TargetInstanceId)
                  .Append(',').Append(m.ModifierType).Append(',').Append(m.Keyword)
                  .Append(',').Append(m.Duration).Append(',').Append(m.BattleId).Append(',').Append(m.OwnerSeat);
            foreach (var kv in s.TemporaryPowerBonus.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append("|P:").Append(kv.Key).Append('=').Append(kv.Value);
            foreach (var kv in s.AttackCountThisTurn.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append("|A:").Append(kv.Key).Append('=').Append(kv.Value);
            foreach (var id in s.NoBlockerGrantedThisTurn.OrderBy(x => x, StringComparer.Ordinal))
                sb.Append("|NB:").Append(id);
            foreach (var seat in new[] { attacker, GameEngine.OtherSeat(attacker) })
            {
                var p = s.Players[seat];
                sb.Append("\n#").Append(seat)
                  .Append(" don").Append(GameEngine.ActiveDonCount(p)).Append('/').Append(p.CostArea.Count)
                  .Append(" dd").Append(p.DonDeck).Append(" turns").Append(p.TurnsStarted)
                  .Append(" life").Append(p.Life.Count).Append(" trash").Append(p.Trash.Count).Append(" deck").Append(p.Deck.Count);
                sb.Append(" L:"); Piece(sb, s, p.Leader);
                sb.Append(" A:");
                foreach (var c in p.CharacterArea) { Piece(sb, s, c); sb.Append(';'); }
                sb.Append(" H:");
                foreach (var c in p.Hand.OrderBy(c => c.InstanceId, StringComparer.Ordinal))
                    sb.Append(c.CardId).Append('@').Append(c.InstanceId).Append(',');
                sb.Append(" F:");
                foreach (var c in p.Life)
                    sb.Append(c.CardId).Append('@').Append(c.InstanceId).Append(c.FaceUp ? 'u' : 'd').Append(',');
                sb.Append(" X:");
                foreach (var c in p.Trash.OrderBy(c => c.InstanceId, StringComparer.Ordinal))
                    sb.Append(c.CardId).Append('@').Append(c.InstanceId).Append(',');
                sb.Append(" K:");
                foreach (var c in p.Deck)
                    sb.Append(c.CardId).Append('@').Append(c.InstanceId).Append(',');
                sb.Append(" U:");
                foreach (var key in p.AbilityUsedThisTurn.OrderBy(x => x, StringComparer.Ordinal))
                    sb.Append(key).Append(',');
            }
            return sb.ToString();
        }

        private static void Piece(StringBuilder sb, GameState s, CardInstance c)
        {
            if (c == null) { sb.Append('.'); return; }
            sb.Append(c.CardId).Append('@').Append(c.InstanceId).Append(c.Rested ? 'r' : 'a')
              .Append('p').Append(GameEngine.GetPower(s, c))
              .Append('c').Append(GameEngine.GetCost(s, c))
              .Append('d').Append(c.AttachedDonIds.Count)
              .Append('t').Append(c.PlayedOnTurn)
              .Append(c.FaceUp ? 'u' : 'd');
            foreach (var m in c.Modifiers)
                sb.Append('[').Append(m.Source).Append(',').Append(m.PowerDelta).Append(',')
                  .Append(m.CostDelta).Append(',').Append(m.ExpiresAt).Append(']');
        }
    }
}
