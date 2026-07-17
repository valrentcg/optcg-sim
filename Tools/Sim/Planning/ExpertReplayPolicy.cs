using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Sim.Expert;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// A deliberately narrow behavior-cloning layer learned from high-bounty public replays. It changes
    /// only attack target selection, the strongest repeated signal that can be extracted without replaying
    /// private hands. All inputs come from an honest determinized world; the engine validates the proposed
    /// command and the proven greedy policy remains the fail-closed fallback.
    /// </summary>
    public static class ExpertReplayPolicy
    {
        private static long _eligibleAttacks, _redirects, _insufficientData;

        public static void ResetStats()
        {
            Interlocked.Exchange(ref _eligibleAttacks, 0);
            Interlocked.Exchange(ref _redirects, 0);
            Interlocked.Exchange(ref _insufficientData, 0);
        }

        public static (long eligible, long redirects, long insufficientData) Stats() =>
            (Interlocked.Read(ref _eligibleAttacks), Interlocked.Read(ref _redirects),
                Interlocked.Read(ref _insufficientData));

        public static GameCommand Decide(GameState world, string seat, HashSet<string> blacklist,
            ExpertPolicyModel model)
        {
            var greedy = IntermediateBot.DecideOneCommand(world, seat, blacklist);
            if (greedy == null || greedy.Type != "declareAttack" || model == null) return greedy;

            var me = world.Players[seat];
            var opp = world.Players[GameEngine.OtherSeat(seat)];
            string leaderId = GameEngine.GetCard(me.Leader)?.Id;
            var preference = model.Preference(leaderId);
            if (preference == null)
            {
                Interlocked.Increment(ref _insufficientData);
                return greedy;
            }

            Interlocked.Increment(ref _eligibleAttacks);
            // Do not manufacture a weak global tendency. Redirect only when the high-bounty corpus shows a
            // decisive preference for life pressure. Board-control leaders retain the incumbent target.
            if (preference.LeaderAttackRate < 0.72 || opp.Leader == null
                || greedy.Target == opp.Leader.InstanceId) return greedy;

            var attacker = me.Leader?.InstanceId == greedy.Attacker ? me.Leader
                : me.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == greedy.Attacker);
            if (attacker == null || GameEngine.GetPower(world, attacker) < GameEngine.GetPower(world, opp.Leader))
                return greedy;

            var learned = new GameCommand
            {
                Type = "declareAttack", Seat = seat, Attacker = greedy.Attacker,
                Target = opp.Leader.InstanceId,
            };
            var clone = GameClone.Clone(world);
            long before = LegalActions.StateFingerprint(clone);
            GameEngine.ApplyCommand(clone, learned);
            if (LegalActions.StateFingerprint(clone) == before) return greedy;
            Interlocked.Increment(ref _redirects);
            return learned;
        }
    }
}
