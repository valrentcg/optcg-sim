using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// Exact, measurable correction for advanced-bot reports 20260917-001429-532 and
    /// 20260917-001539-501. Kept as a small shared policy so the shipping AdvancedContractBot and the
    /// headless research mirror execute the same boundary.
    /// </summary>
    internal static class Op17NewgateDefensePolicy
    {
        /// <summary>
        /// Decline only the initial optional OP17-001 cost when the current attack is already unable to
        /// connect. Equal power is not redundant: OPTCG attacks connect when attack power is greater than
        /// or equal to defense power. Other attack reactions may have useful non-defense side effects and
        /// are intentionally outside this card-specific rule.
        /// </summary>
        internal static bool TryDecline(GameState state, string seat, out GameCommand command)
        {
            command = null;
            if (!ShouldDecline(state, seat, out var effect)) return false;
            command = new GameCommand
            {
                Type = "passEffect",
                Seat = seat,
                EffectId = effect.EffectId,
            };
            return true;
        }

        internal static bool ShouldDecline(GameState state, string seat, out PendingEffect effect)
        {
            effect = null;
            if (state?.Battle == null || string.IsNullOrEmpty(seat)
                || state.Battle.TargetSeat != seat || !state.Players.ContainsKey(seat))
                return false;

            effect = state.PendingEffects.FirstOrDefault(e => e != null
                && e.Seat == seat
                && e.Optional
                && e.SourceCardId == "OP17-001"
                && e.Timing == "onOpponentsAttack"
                && GameEngine.IsUnpaidCostPrefix(e));
            if (effect == null) return false;

            CardInstance attacker = FindBattleCard(state, state.Battle.AttackerSeat, state.Battle.AttackerId);
            CardInstance target = FindBattleCard(state, state.Battle.TargetSeat, state.Battle.TargetId);
            int attackPower = attacker == null ? state.Battle.AttackPower : GameEngine.GetPower(state, attacker);
            int defensePower = (target == null ? state.Battle.DefensePower : GameEngine.GetPower(state, target))
                + state.Battle.CounterPower;
            return attackPower < defensePower;
        }

        private static CardInstance FindBattleCard(GameState state, string seat, string instanceId)
        {
            if (state == null || string.IsNullOrEmpty(seat) || string.IsNullOrEmpty(instanceId)
                || !state.Players.TryGetValue(seat, out var player))
                return null;
            if (player.Leader?.InstanceId == instanceId) return player.Leader;
            return player.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == instanceId);
        }
    }
}
