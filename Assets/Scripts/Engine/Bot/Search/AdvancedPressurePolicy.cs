using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// Advanced-tier control pressure module (ported from Tools/Sim). Keeps IntermediateBot's competent
    /// deploy / DON / sequencing decisions, but when it is about to attack a rested Character with an
    /// attacker that already out-powers — and therefore connects to — the opposing Leader, it pressures Life
    /// instead. IntermediateBot declines most Leader counters above three Life, which makes those attacks
    /// unusually valuable for a control gameplan. The engine remains the legality oracle and the greedy
    /// command is the fail-closed fallback.
    ///
    /// Part of the full advanced contract; the router applies it to control-archetype decks at clean main.
    /// Runs directly on the real game state (a shipped AI opponent is allowed full information).
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
            // Validate against a clone (never mutates the live state) and only redirect if the attack applies
            // and changes the position; otherwise the greedy incumbent stands.
            if (LegalActions.Validate(world, seat, new List<GameCommand> { pressure }).Count == 0) return greedy;
            Interlocked.Increment(ref _redirects);
            return pressure;
        }
    }
}
