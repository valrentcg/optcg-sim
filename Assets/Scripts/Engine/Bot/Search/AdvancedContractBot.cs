using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// The full advanced contract (contract-v2), ported from the out-of-ship discovery platform (Tools/Sim)
    /// where it won ~70% against the frozen IntermediateBot benchmark and ~61% against the evolved Champion.
    /// It keeps IntermediateBot as the competent base and layers deck-identity strategy modules on top:
    ///
    ///   • midrange, clean main → <see cref="AdvancedActivationPolicy"/> (insert a beneficial [Activate: Main])
    ///   • control,  clean main → <see cref="AdvancedPressurePolicy"/> (pressure Life over a rested trade)
    ///   • tactical / resolution decisions (choice, deck-look, effect target, battle Trigger) →
    ///     <see cref="SearchBot"/>'s one-step rollout, which already best-responds those branches
    ///   • everything else (defence, opponent-turn responses, aggro/combo main) → <see cref="IntermediateBot"/>
    ///
    /// The research build ran each module on a determinized world to keep MEASUREMENT honest; a shipped AI
    /// opponent is allowed full information (as the previous SearchBot was), so here the modules run directly
    /// on the real game state. The engine remains the sole legality oracle and each module fails closed to the
    /// greedy incumbent.
    /// </summary>
    public static class AdvancedContractBot
    {
        public static GameCommand Decide(GameState state, string seat, HashSet<string> blacklist,
            HashSet<string> attemptedThisTurn, string archetype)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;

            bool cleanMain = state.ActiveSeat == seat && state.Phase == "main" && state.Battle == null
                && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null;
            if (cleanMain && (archetype == "midrange" || archetype == "activation-engine"))
                return AdvancedActivationPolicy.Decide(state, seat, blacklist, attemptedThisTurn);
            if (cleanMain && archetype == "control")
                return AdvancedPressurePolicy.Decide(state, seat, blacklist);

            // Bounded tactical improvement only where the greedy incumbent has explicit blind spots: choice
            // A/B, deck-look ordering, effect targets, and Trigger use/pass. Defence (block/counter) and every
            // other decision keep the proven greedy policy — the universal clean-main rollout was rejected on
            // independent validation, so it is deliberately NOT applied to clean-main aggro/combo turns.
            bool resolutionDecision =
                (state.ActiveChoice != null && state.ActiveChoice.Seat == seat)
                || (state.DeckLook != null && state.DeckLook.Seat == seat)
                || state.PendingEffects.Any(e => e.Seat == seat)
                || (state.Battle != null && state.Battle.TargetSeat == seat && state.Battle.Step == "trigger");
            // Rollout search treats consuming a Trigger as a legal state change even when its
            // optional payload has no target. Apply the same strict value gate as the incumbent
            // before search, so Advanced cannot trash a useful Event from Life for a no-op.
            if (state.Battle != null && state.Battle.TargetSeat == seat && state.Battle.Step == "trigger"
                && !IntermediateBot.ShouldUseTrigger(state, seat))
                return new GameCommand { Type = "passTrigger", Seat = seat };
            if (resolutionDecision)
                return SearchBot.DecideOneCommand(state, seat, blacklist);

            return IntermediateBot.DecideOneCommand(state, seat, blacklist);
        }

        /// <summary>Label the deck's gameplan from its list, so the router can gate the identity modules.
        /// Minimal port of the platform's DeckFingerprint: a genuine alternate win route ("win the game")
        /// makes it combo; otherwise average non-event cost splits aggro / midrange / control. Cut points are
        /// the platform's, read off the real spread of meta decks. Unknown/empty ⇒ midrange (the safe default,
        /// and the identity the activation module was most directly validated on).</summary>
        public static string ClassifyArchetype(DeckDef deck)
        {
            if (deck?.List == null || deck.List.Count == 0) return "midrange";
            int n = 0; long costSum = 0; bool altWin = false;
            bool leaderActivationEngine = false;
            var leader = CardData.GetCard(deck.Leader);
            if (leader?.Effect?.IndexOf("[Activate: Main]", System.StringComparison.OrdinalIgnoreCase) >= 0)
                leaderActivationEngine = true;
            foreach (var (cardId, qty) in deck.List)
            {
                var d = CardData.GetCard(cardId);
                if (d == null) continue;
                if (d.Type != "event") { n += qty; costSum += (long)d.Cost * qty; }
                string t = ((d.Effect ?? "") + " " + (d.Trigger ?? "")).ToLowerInvariant();
                if (t.Contains("win the game")) altWin = true;
            }
            // A coarse curve label must not disable the card that makes the deck function. Enel and
            // ST03 Crocodile both have cheap lists (historically labelled aggro) but their gameplans
            // require a guarded once-per-turn Leader activation. Route the mechanic explicitly.
            if (leaderActivationEngine) return "activation-engine";
            if (altWin) return "combo";
            double avg = n == 0 ? 0 : (double)costSum / n;
            if (avg < 3.5) return "aggro";
            if (avg < 4.7) return "midrange";
            return "control";
        }
    }
}
