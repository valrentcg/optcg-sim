using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Conservative clean-main activation layer. The frozen IntermediateBot deliberately never uses
    /// [Activate: Main], so this policy preserves its deploy/DON/attack sequence and only inserts an
    /// engine-validated, clearly beneficial activation immediately before combat or end turn.
    /// A per-bot, per-turn attempted set prevents repeatable abilities from creating an action loop.
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

            // Proactive [DON!! xN] leader-engine setup (e.g. lt01 Luffy ST21-001: "[DON!! x1] [Activate: Main]
            // give up to 2 rested DON!! to a Character"). The passive loop below only tries the RAW activation,
            // which no-ops whenever the [DON!! xN] gate is unmet — so the ability never fired in play. Here we
            // attach the DON needed to meet the gate FIRST, then activate. It only runs when the line is
            // actually worth it: rested DON exist to give and a Character can receive them. The play-a-unit
            // alternative is already resolved — the greedy deploys every affordable unit before we reach here,
            // so the setup spends only leftover DON.
            if (ProactiveLeaderSetup)
            {
                var lead = LeaderEngineSetup(world, seat, blacklist, attemptedThisTurn);
                if (lead != null) return lead;
            }

            // Restand engine (Zoro et al.): a card that has attacked can be set active AGAIN to attack more.
            if (IntermediateBot.RestandEngineEnabled)
            {
                var restand = RestandSetup(world, seat, blacklist, attemptedThisTurn);
                if (restand != null) return restand;
            }

            foreach (var card in BoardCards(p))
            {
                if (card == null || attemptedThisTurn.Contains(card.InstanceId)
                    || p.AbilityUsedThisTurn.Contains(card.InstanceId)) continue;
                var def = GameEngine.GetCard(card);
                if (def == null) continue;
                // BeneficialActivateMain blanket-rejects every "trash this Character" ability to avoid marginal
                // self-sacrifice — but that also disabled the deliberate sacrifice-to-recur engine (trash a
                // small body to deploy a BIGGER one from the trash: Yamato 6→8, Momonosuke 5→9, …). Restore
                // that move class through a value-gated, card-id-free path.
                if (!BeneficialActivateMain(def.Effect)
                    && !(SacrificeRecurEnabled && IsSacrificeToRecur(def.Effect, def.Cost, card.Rested))) continue;
                // A DON!! −N activation (e.g. Crocodile's DON!! −4 leader bounce) must clear its DON-return
                // cost. For a no-recovery deck that returned DON!! is real tempo, so the removal must be worth
                // it; for a re-ramp deck it is nearly free. Not marked attempted — a better target may appear.
                if (ActivationDonCostGate
                    && !OnePieceTcg.Engine.Bot.Search.DonOpportunityModel.ActivationClearsDonCost(world, seat, def.Effect)) continue;

                // Mark before returning. Even a repeatable ability is tried at most once this turn; the
                // engine remains the legality oracle, and an invalid action falls through to the incumbent.
                attemptedThisTurn.Add(card.InstanceId);
                var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = card.InstanceId };
                var clone = GameClone.Clone(world);
                long before = LegalActions.StateFingerprint(clone);
                GameEngine.ApplyCommand(clone, activation);
                if (LegalActions.StateFingerprint(clone) == before) continue;

                Interlocked.Increment(ref _activations);
                return activation;
            }
            return greedy;
        }

        /// <summary>A/B toggle: proactively enable a beneficial [DON!! xN]-gated "give rested DON" leader
        /// ability by attaching the required DON to the leader, then activating it.</summary>
        public static bool ProactiveLeaderSetup = true;

        /// <summary>A/B toggle: gate a DON!! −N activation (e.g. Crocodile's DON!! −4 bounce) on whether the
        /// effect clears the tempo cost of the returned DON!! — expensive for a no-recovery deck, cheap for a
        /// re-ramp deck. Off = fire any beneficial activation regardless of its DON-return cost.</summary>
        public static bool ActivationDonCostGate = true;

        /// <summary>A/B toggle: allow a "[Activate: Main] trash this Character: play a body from your trash"
        /// activation — the deliberate sacrifice-to-recur burst engine (Yamato 6→8, Momonosuke 5→9, …).
        /// <see cref="BeneficialActivateMain"/> rejects every "trash this character" ability to avoid marginal
        /// self-sacrifice, which silently disabled this whole tempo-upgrade move class. Off = keep it disabled.
        /// The recur bodies chain naturally: each copy is a distinct instance, so multiple sacrifice-recurs
        /// fire in the same turn, producing the multi-attacker overwhelm turn the archetype is built around.</summary>
        public static bool SacrificeRecurEnabled = true;

        /// <summary>True when an [Activate: Main] ability trashes THIS Character to deploy a card FROM THE TRASH
        /// whose cost is an upgrade (≥ this card's own cost) — or, when no target cost is printed, when this
        /// card is already spent (rested), so sacrificing it forfeits no live attacker. Text-driven and
        /// card-id-free. The engine stays the legality oracle: with an empty/again-invalid trash the activation
        /// no-ops and is skipped, so this never fires into nothing.</summary>
        public static bool IsSacrificeToRecur(string effect, int ownCost, bool cardRested)
        {
            if (string.IsNullOrEmpty(effect)) return false;
            string e = effect.ToLowerInvariant();
            if (e.IndexOf("activate: main", StringComparison.Ordinal) < 0) return false;
            if (!e.Contains("trash this character")) return false;
            if (!e.Contains("from your trash")) return false;
            if (!(e.Contains("play up to") || e.Contains("play 1") || e.Contains("play a "))) return false;
            int recurCost = RecurTargetCost(e);
            if (recurCost >= 0) return recurCost >= ownCost;   // deploy a body at least as big as the one trashed
            return cardRested;                                  // no printed cost → only sacrifice an already-spent body
        }

        // The cost of the body played FROM TRASH: the first "cost of N" / "cost N" AFTER the "play" verb, so a
        // leading trash-REQUIREMENT cost ("trash this Character with a cost of 20 or more") is never mistaken
        // for the recur target. −1 when the recur clause prints no cost.
        private static int RecurTargetCost(string loweredEffect)
        {
            int playIdx = loweredEffect.IndexOf("play", StringComparison.Ordinal);
            if (playIdx < 0) return -1;
            var m = System.Text.RegularExpressions.Regex.Match(
                loweredEffect.Substring(playIdx), @"cost(?: of)? (\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        // Returns the next step of the leader-engine line (attach-to-leader to meet [DON!! xN], then activate),
        // or null if it is not applicable/worthwhile this decision. Fires only when there are rested DON to
        // give and a Character to receive them — otherwise the activation would resolve to nothing.
        private static GameCommand LeaderEngineSetup(GameState world, string seat, HashSet<string> blacklist,
            HashSet<string> attemptedThisTurn)
        {
            var p = world.Players[seat];
            var leader = p.Leader;
            if (leader == null || attemptedThisTurn.Contains(leader.InstanceId)
                || p.AbilityUsedThisTurn.Contains(leader.InstanceId)) return null;

            string e = (GameEngine.GetCard(leader)?.Effect ?? "").ToLowerInvariant();
            var gate = System.Text.RegularExpressions.Regex.Match(e, @"\[don!! x(\d+)\]");
            if (!gate.Success || e.IndexOf("activate: main", StringComparison.Ordinal) < 0
                || !e.Contains("give") || !e.Contains("rested don!!")) return null;
            int need = int.Parse(gate.Groups[1].Value);

            // Preconditions the ability itself needs: rested DON!! to give AND a Character to receive them.
            var attachedIds = new HashSet<string>(AllAttachedDonIds(p));
            int restedToGive = p.CostArea.Count(d => d.Rested && !attachedIds.Contains(d.InstanceId));
            bool hasRecipient = p.CharacterArea.Any(c => c != null);
            if (restedToGive < 1 || !hasRecipient) return null;

            int attachedOnLeader = leader.AttachedDonIds?.Count ?? 0;
            if (attachedOnLeader < need)
            {
                // Need to meet the [DON!! xN] gate. Attach one active DON!! to the leader (which is itself an
                // attacker, so the DON is not wasted even before the activation resolves).
                if (GameEngine.ActiveDonCount(p) < 1) return null;
                var setup = new GameCommand { Type = "attachDon", Seat = seat, Target = leader.InstanceId, Amount = 1 };
                if (blacklist.Contains(IntermediateBot.Signature(setup))) return null;
                return setup;
            }

            // Gate met → activate. Validate on a clone; mark attempted so we never loop on it.
            attemptedThisTurn.Add(leader.InstanceId);
            var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = leader.InstanceId };
            if (blacklist.Contains(IntermediateBot.Signature(activation))) return null;
            var clone = GameClone.Clone(world);
            long before = LegalActions.StateFingerprint(clone);
            GameEngine.ApplyCommand(clone, activation);
            if (LegalActions.StateFingerprint(clone) == before) return null;
            Interlocked.Increment(ref _activations);
            return activation;
        }

        private static IEnumerable<string> AllAttachedDonIds(PlayerState p)
        {
            if (p.Leader?.AttachedDonIds != null) foreach (var id in p.Leader.AttachedDonIds) yield return id;
            foreach (var c in p.CharacterArea)
                if (c?.AttachedDonIds != null) foreach (var id in c.AttachedDonIds) yield return id;
        }

        private static IEnumerable<CardInstance> BoardCards(PlayerState p)
        {
            if (p.Leader != null) yield return p.Leader;
            foreach (var c in p.CharacterArea.Where(c => c != null)) yield return c;
            if (p.Stage != null) yield return p.Stage;
        }

        // A restand ability lets a card attack again this turn — worth pursuing only AFTER it has attacked
        // (the printed conditions key off "battled …"). Meet its [DON!! xN] gate (attach DON!! to it), then
        // activate; the engine enforces the precise condition, so an unmet one simply no-ops and we skip.
        // Text-driven and card-id-free, so it covers every restand deck the bot may be handed.
        private static GameCommand RestandSetup(GameState world, string seat, HashSet<string> blacklist,
            HashSet<string> attemptedThisTurn)
        {
            var p = world.Players[seat];
            foreach (var card in BoardCards(p))
            {
                if (card == null || !card.Rested
                    || attemptedThisTurn.Contains(card.InstanceId)
                    || p.AbilityUsedThisTurn.Contains(card.InstanceId)) continue;
                // Only after this card has actually attacked this turn.
                if (!world.AttackCountThisTurn.TryGetValue(card.InstanceId, out int atk) || atk <= 0) continue;
                string e = (GameEngine.GetCard(card)?.Effect ?? "").ToLowerInvariant();
                if (e.IndexOf("activate: main", StringComparison.Ordinal) < 0) continue;
                if (!(e.Contains("set this leader as active") || e.Contains("set this character as active"))) continue;

                int need = 0;
                var gate = System.Text.RegularExpressions.Regex.Match(e, @"\[don!! x(\d+)\]");
                if (gate.Success) need = int.Parse(gate.Groups[1].Value);
                int attached = card.AttachedDonIds?.Count ?? 0;
                if (attached < need)
                {
                    if (GameEngine.ActiveDonCount(p) < 1) continue;
                    var setup = new GameCommand { Type = "attachDon", Seat = seat, Target = card.InstanceId, Amount = 1 };
                    if (blacklist.Contains(IntermediateBot.Signature(setup))) continue;
                    return setup;
                }
                attemptedThisTurn.Add(card.InstanceId);
                var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = card.InstanceId };
                if (blacklist.Contains(IntermediateBot.Signature(activation))) continue;
                var clone = GameClone.Clone(world);
                long before = LegalActions.StateFingerprint(clone);
                GameEngine.ApplyCommand(clone, activation);
                if (LegalActions.StateFingerprint(clone) == before) continue;   // condition not met → no-op → skip
                Interlocked.Increment(ref _activations);
                return activation;
            }
            return null;
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
                || (e.Contains("give") && e.Contains("rested don!!"));
        }
    }
}
