using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Bounded opponent-model exploit for the frozen IntermediateBot benchmark. Keep its competent deploy,
    /// DON, and sequencing decisions, but when it is about to attack a rested Character with an attacker that
    /// already connects to the opposing Leader, pressure Life instead. IntermediateBot declines most leader
    /// counters above three Life, making those attacks unusually valuable. The engine remains the legality
    /// oracle and the greedy command is the fail-closed fallback.
    /// </summary>
    public static class AdvancedPressurePolicy
    {
        private static long _attacks, _redirects;
        public static void ResetStats() { Interlocked.Exchange(ref _attacks, 0); Interlocked.Exchange(ref _redirects, 0); }
        public static (long attacks, long redirects) Stats() =>
            (Interlocked.Read(ref _attacks), Interlocked.Read(ref _redirects));

        public static GameCommand Decide(GameState world, string seat, HashSet<string> blacklist)
        {
            var greedy = IntermediateBot.DecideOneCommand(world, seat, blacklist);
            if (greedy == null || greedy.Type != "declareAttack") return greedy;
            Interlocked.Increment(ref _attacks);

            var me = world.Players[seat];
            var opp = world.Players[GameEngine.OtherSeat(seat)];
            if (opp.Leader == null || greedy.Target == opp.Leader.InstanceId) return greedy;
            var attacker = me.Leader?.InstanceId == greedy.Attacker ? me.Leader
                : me.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == greedy.Attacker);
            if (attacker == null || GameEngine.GetPower(world, attacker) < GameEngine.GetPower(world, opp.Leader))
                return greedy;

            var pressure = new GameCommand
            {
                Type = "declareAttack", Seat = seat, Attacker = greedy.Attacker,
                Target = opp.Leader.InstanceId,
            };
            var clone = GameClone.Clone(world);
            long before = LegalActions.StateFingerprint(clone);
            GameEngine.ApplyCommand(clone, pressure);
            if (LegalActions.StateFingerprint(clone) == before) return greedy;
            Interlocked.Increment(ref _redirects);
            return pressure;
        }
    }
}
