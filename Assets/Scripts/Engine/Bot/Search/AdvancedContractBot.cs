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
    /// FAIR INFORMATION: the modules must reason from what a PLAYER knows, never the true hidden state. The
    /// whole pipeline runs on a <see cref="BotDeterminizer.FairView"/> of the state — a clone in which the
    /// zones hidden from <paramref name="seat"/> (the opponent's hand, both deck orders, face-down Life) have
    /// their identities permuted (multiset preserved, arrangement randomized). The greedy/identity modules read
    /// only public info and their own hand, so the fair view is behaviour-neutral for them; the search/rollout
    /// (<see cref="SearchBot"/>) and Trigger simulation (<see cref="TriggerUtilityPolicy"/>), which DO play the
    /// hidden state forward, now best-respond a sampled world instead of the truth. Every card the seat can act
    /// on keeps its real id, so a returned command still applies to the true state. The engine remains the sole
    /// legality oracle and each module fails closed to the greedy incumbent.
    /// </summary>
    public static class AdvancedContractBot
    {
        // Diagnostic A/B toggles: skip a module (fall back to the greedy core) to measure that module's
        // contribution to the +6pp the modules add over pure greedy. Default false (all modules active).
        public static bool SkipActivation = false;   // AdvancedActivationPolicy (midrange/activation clean-main)
        public static bool SkipPressure = false;      // AdvancedPressurePolicy (control clean-main)
        public static bool SkipSearch = false;        // SearchBot rollout on resolution/tactical branches
        public static bool SkipTriggerUtility = false; // TriggerUtilityPolicy on the battle Trigger step
        // EXPERIMENT (iter 9): route clean-main decisions through the SearchBot rollout instead of the greedy/
        // activation policy — tests whether SEARCH on the BIG decision space (main phase) wins now (post the 300+
        // effect fixes; a pre-fix "universal clean-main rollout" was rejected). Default false.
        public static bool SearchCleanMain = false;
        // iter 2: route clean-main through 1-ply EVAL-GREEDY (pick the action whose resulting position evals best)
        // using the LEARNED (oracle-distilled) Evaluation weights. This is the ship-form of the distillation win.
        public static bool EvalGreedyMain = false;

        public static GameCommand Decide(GameState trueState, string seat, HashSet<string> blacklist,
            HashSet<string> attemptedThisTurn, string archetype)
        {
            var state = trueState;
            if (state == null || !state.Players.ContainsKey(seat)) return null;

            // Fair information, applied LAZILY. Only the two modules that play the hidden state FORWARD
            // (SearchBot's rollout, TriggerUtilityPolicy's battle sim) may read secrets, so only they get a
            // resampled BotDeterminizer.FairView. The greedy/activation/pressure modules read only public info
            // and their own hand, so the true state is already fair for them — and skipping the per-tick clone
            // on those (the majority of decisions) keeps the bot fast. Determinizing everywhere would be
            // behaviour-identical for them (they don't read hidden zones) but needlessly clones every tick.

            bool cleanMain = state.ActiveSeat == seat && state.Phase == "main" && state.Battle == null
                && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null;
            // EXPERIMENT: search the main phase (the largest decision space) instead of greedy/activation.
            if (SearchCleanMain && cleanMain)
            {
                var fairMain = BotDeterminizer.FairView(state, seat, BotDeterminizer.Seed(state, seat));
                return SearchBot.DecideOneCommand(fairMain, seat, blacklist);
            }
            // iter 2: 1-ply eval-greedy main using the learned eval (oracle-distilled).
            if (EvalGreedyMain && cleanMain)
            {
                var legal = LegalActions.Validate(state, seat, LegalActions.Candidates(state, seat));
                if (legal.Count > 0)
                {
                    GameCommand pick = null; double bestSc = double.NegativeInfinity;
                    foreach (var kv in legal)
                    {
                        if (blacklist.Contains(IntermediateBot.Signature(kv.Key))) continue;
                        double sc = Evaluation.Score(kv.Value, seat);
                        if (sc > bestSc) { bestSc = sc; pick = kv.Key; }
                    }
                    if (pick != null) return pick;
                }
            }
            if (!SkipActivation && cleanMain && (archetype == "midrange" || archetype == "activation-engine"))
                return AdvancedActivationPolicy.Decide(state, seat, blacklist, attemptedThisTurn);
            if (!SkipPressure && cleanMain && archetype == "control")
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
            // Battle Trigger step: decide use vs. take-into-hand with the dedicated utility evaluator
            // (simulate both lines, weigh removal/body/defensive power/Life/DON/draw against the card's
            // [Counter] + card advantage, with a lethal override). This subsumes the old zero-value gate —
            // a no-target/no-value Trigger scores ~0 and is declined — and, unlike the generic rollout,
            // does not wash out a Trigger's marginal defensive value.
            if (!SkipTriggerUtility && state.Battle != null && state.Battle.TargetSeat == seat && state.Battle.Step == "trigger")
            {
                var fair = BotDeterminizer.FairView(state, seat, BotDeterminizer.Seed(state, seat));
                return new GameCommand
                {
                    Type = TriggerUtilityPolicy.ShouldUse(fair, seat) ? "useTrigger" : "passTrigger",
                    Seat = seat,
                };
            }
            if (!SkipSearch && resolutionDecision)
            {
                var fair = BotDeterminizer.FairView(state, seat, BotDeterminizer.Seed(state, seat));
                return SearchBot.DecideOneCommand(fair, seat, blacklist);
            }

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
