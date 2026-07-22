// Advanced-tier bot: the every-legal-action ROLLOUT SEARCH bot discovered by the out-of-ship
// platform (Tools/Sim). At each decision it enumerates every legal move, shortlists the top few by a
// fast weighted eval, plays each finalist out to the end with a strong policy, and picks the move
// with the best result ("trial each action to the end, keep the highest win %"). Beats the
// Intermediate champion ~87% head-to-head. Exposes the same DecideOneCommand interface as the other
// bots so GameManager's tick loop is unchanged.
//
// PERFORMANCE: a single decision runs several full playouts on cloned state (~hundreds of ms). It is
// called once per think-tick and the tick loop already pauses between actions, but the search runs on
// the calling thread — GameManager should invoke it off the main thread if UI hitching is noticeable.

using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    public static class SearchBot
    {
        public const int RolloutCap = 2000;  // commands to play out per rollout before falling back to eval
        public const int Shortlist = 6;       // roll out only the top-K candidates by fast eval

        public static GameCommand DecideOneCommand(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;

            bool searchable =
                (state.DeckLook != null && state.DeckLook.Seat == seat) ||
                (state.ActiveChoice != null && state.ActiveChoice.Seat == seat) ||
                (state.PendingEffects.Any(e => e.Seat == seat) && state.DeckLook == null) ||
                (state.Battle != null && state.Battle.TargetSeat == seat) ||
                (state.ActiveSeat == seat && state.Phase == "main"
                 && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null);
            if (!searchable) return IntermediateBot.DecideOneCommand(state, seat, blacklist);

            var candidates = LegalActions.Candidates(state, seat);
            var legal = LegalActions.Validate(state, seat, candidates);
            // Respect the caller's no-op blacklist. A command the host already applied and found to NOT change
            // the (true) state must never be re-picked, or the bot loops on it forever — e.g. a deckLookSelect
            // the engine rejects (wrong filter/limit) that Validate still rates legal on the search world and
            // the rollout scores highest. Without this filter the search re-issues it every tick, burning to
            // the command cap (the op16-luffy-n-ace deck-look hang). If nothing survives, defer to the greedy
            // fallback, whose resolution path confirms/ends the look once every select is blacklisted.
            if (blacklist != null && blacklist.Count > 0)
                legal = legal.Where(kv => !blacklist.Contains(IntermediateBot.Signature(kv.Key))).ToList();
            if (legal.Count == 0) return IntermediateBot.DecideOneCommand(state, seat, blacklist);

            IEnumerable<KeyValuePair<GameCommand, GameState>> toScore = legal;
            if (Shortlist > 0 && legal.Count > Shortlist)
                toScore = legal.OrderByDescending(x => Evaluation.Score(x.Value, seat)).Take(Shortlist).ToList();

            // For effect-TARGET choices, the multi-turn rollout washes out the immediate board impact of
            // WHICH body was removed (both lines play on for many turns), so removing a chip can tie
            // removing the Blocker that was actually stopping lethal — and the wrong target gets picked.
            // Blend in the immediate post-resolution eval as a reliable tiebreak toward the higher-value
            // removal. Terminal win/loss rollouts return ±1e6 and stay dominant, so genuine lethal lines
            // (e.g. clear the Blocker, then swing for game) still win outright.
            bool effectTarget = state.PendingEffects.Any(e => e.Seat == seat) && state.DeckLook == null;

            GameCommand best = null; double bestScore = double.NegativeInfinity;
            foreach (var kv in toScore)
            {
                double sc = ScoreByRollout(kv.Value, seat);
                if (effectTarget && sc > -1e5 && sc < 1e5) sc += Evaluation.Score(kv.Value, seat);
                if (sc > bestScore) { bestScore = sc; best = kv.Key; }
            }
            return best;
        }

        private static double ScoreByRollout(GameState result, string seat)
        {
            var clone = GameClone.Clone(result);
            Playout(clone, RolloutCap);
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
            return Evaluation.Score(clone, seat); // unfinished playout → static eval of the deeper state
        }

        /// <summary>Rollout policy. FALSE = legacy ChampionBot; TRUE = IntermediateBot (the current STRONG core).
        /// ChampionBot was picked when it was the strongest policy, but it was later RETIRED for losing to the
        /// core — so the search has been evaluating moves by simulating WEAK future play. Rolling out with the
        /// strong core makes every move evaluation more accurate (the value function is only sampled at cutoff).</summary>
        public static bool StrongRollout = false;

        // Play the position out to terminal, driving BOTH seats with the rollout policy (see StrongRollout) so
        // move evaluations reflect realistic future play.
        private static void Playout(GameState state, int cap)
        {
            int total = 0;
            while (state.Status != "finished" && total < cap)
            {
                int s = DriveSeat(state, "south", cap - total);
                total += s;
                int n = DriveSeat(state, "north", cap - total);
                total += n;
                if (s == 0 && n == 0) break;
            }
        }

        private static int DriveSeat(GameState state, string seat, int budget)
        {
            int applied = 0;
            var bl = new HashSet<string>();
            for (int i = 0; i < budget; i++)
            {
                var cmd = StrongRollout ? IntermediateBot.DecideOneCommand(state, seat, bl)
                                        : ChampionBot.DecideOneCommand(state, seat, bl);
                if (cmd == null) break;
                object before = IntermediateBot.SnapshotFor(state, cmd);
                GameEngine.ApplyCommand(state, cmd);
                applied++;
                if (!IntermediateBot.Succeeded(state, cmd, before))
                    bl.Add(IntermediateBot.Signature(cmd));
            }
            return applied;
        }
    }
}
