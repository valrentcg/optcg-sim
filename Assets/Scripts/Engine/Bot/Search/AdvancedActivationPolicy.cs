using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// Advanced-tier clean-main activation layer (ported from the out-of-ship discovery platform,
    /// Tools/Sim). The frozen <see cref="IntermediateBot"/> deliberately never uses [Activate: Main], so a
    /// whole class of high-impact moves was unreachable for it. This policy preserves IntermediateBot's
    /// proven deploy / DON / attack sequence and only inserts an engine-validated, clearly beneficial
    /// [Activate: Main] ability immediately before combat or end of turn. A per-turn attempted set prevents
    /// a repeatable ability from creating an action loop, and the engine remains the sole legality oracle —
    /// an activation that is illegal or does nothing falls through to the greedy incumbent.
    ///
    /// This is the ACTIVATION-FIRST landing of the validated advanced contract. It does not yet include the
    /// control pressure module or the tactical rollout module; those are separate follow-ups. Unlike the
    /// research build it runs directly on the real game state (a shipped AI opponent is allowed full
    /// information, exactly as the previous SearchBot was), so no determinization is involved.
    /// </summary>
    public static class AdvancedActivationPolicy
    {
        private static long _opportunities, _activations;

        public static void ResetStats()
        {
            Interlocked.Exchange(ref _opportunities, 0);
            Interlocked.Exchange(ref _activations, 0);
        }

        public static (long opportunities, long activations) Stats() =>
            (Interlocked.Read(ref _opportunities), Interlocked.Read(ref _activations));

        public static GameCommand Decide(GameState world, string seat, HashSet<string> blacklist,
            HashSet<string> attemptedThisTurn)
        {
            var greedy = IntermediateBot.DecideOneCommand(world, seat, blacklist);
            if (greedy == null || (greedy.Type != "declareAttack" && greedy.Type != "attachDon"
                && greedy.Type != "endTurn")) return greedy;
            Interlocked.Increment(ref _opportunities);

            var p = world.Players[seat];
            foreach (var card in BoardCards(p))
            {
                if (card == null || attemptedThisTurn.Contains(card.InstanceId)
                    || p.AbilityUsedThisTurn.Contains(card.InstanceId)) continue;
                var def = GameEngine.GetCard(card);
                if (def == null || !BeneficialActivateMain(def.Effect)) continue;

                // Mark before validating. Even a repeatable ability is tried at most once this turn; the
                // engine remains the legality oracle, and an invalid/no-op action falls through to the greedy
                // incumbent. LegalActions.Validate clones the state, applies the command, and returns it only
                // if the position actually changed — so it never mutates the live game and never plays a no-op.
                attemptedThisTurn.Add(card.InstanceId);
                var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = card.InstanceId };
                if (LegalActions.Validate(world, seat, new List<GameCommand> { activation }).Count == 0) continue;

                Interlocked.Increment(ref _activations);
                return activation;
            }
            return greedy;
        }

        private static IEnumerable<CardInstance> BoardCards(PlayerState p)
        {
            if (p.Leader != null) yield return p.Leader;
            foreach (var c in p.CharacterArea.Where(c => c != null)) yield return c;
            if (p.Stage != null) yield return p.Stage;
        }

        private static bool BeneficialActivateMain(string effect)
        {
            if (string.IsNullOrEmpty(effect)
                || effect.IndexOf("[Activate: Main]", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string e = effect.ToLowerInvariant();
            if (e.Contains("you may trash") && e.Contains("from your hand")) return false;
            if (e.Contains("trash") && e.Contains("from the top of your life")) return false;
            if (e.Contains("trash this character")) return false;
            return e.Contains("draw ") || e.Contains("k.o.") || e.Contains("set up to") || e.Contains("rest up to")
                || e.Contains("play up to") || e.Contains("add up to") || e.Contains("return up to")
                || e.Contains("gains +") || e.Contains("look at")
                // ST01-001 Luffy / ST01-007 Nami: turn rested DON!! into an extra attacker buff.
                || (e.Contains("give") && e.Contains("rested don!!"));
        }
    }
}
