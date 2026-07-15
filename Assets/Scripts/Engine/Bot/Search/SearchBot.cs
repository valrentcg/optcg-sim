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
            if (legal.Count == 0) return IntermediateBot.DecideOneCommand(state, seat, blacklist);

            IEnumerable<KeyValuePair<GameCommand, GameState>> toScore = legal;
            if (Shortlist > 0 && legal.Count > Shortlist)
                toScore = legal.OrderByDescending(x => Evaluation.Score(x.Value, seat)).Take(Shortlist).ToList();

            GameCommand best = null; double bestScore = double.NegativeInfinity;
            foreach (var kv in toScore)
            {
                double sc = ScoreByRollout(kv.Value, seat);
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

        // Play the position out to terminal, driving BOTH seats with the strong Champion policy so
        // move evaluations are accurate against strong opponents (baseline rollouts mis-evaluate).
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
                var cmd = ChampionBot.DecideOneCommand(state, seat, bl);
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
