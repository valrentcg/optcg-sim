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
    /// control pressure module or the tactical rollout module; those are separate follow-ups. It reads only
    /// PUBLIC information and its own hand, and its caller (<see cref="AdvancedContractBot"/>) hands it a
    /// fair-information <see cref="BotDeterminizer.FairView"/> of the state, so it never sees hidden cards.
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
            // which no-ops while the [DON!! xN] gate is unmet — so the ability never fired in play. Here we
            // attach the DON needed to meet the gate FIRST, then activate. It runs only when the line is worth
            // it (rested DON to give + a Character to receive); the play-a-unit alternative is already resolved,
            // since the greedy deploys every affordable unit before we reach here.
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

            // Proactive "trash 1 from hand: negate your opponent's [On Play] effects" (Blackbeard OP09-081).
            // BeneficialActivateMain rejects every trash-from-hand ability (avoids marginal card loss), which
            // also disabled this preventative leader. Fire it only when the opponent actually holds an
            // impactful [On Play] Character to deny AND we can spare the card — full-info, so it's real value.
            if (OnPlayNegateEnabled)
            {
                var negate = OnPlayNegateSetup(world, seat, blacklist, attemptedThisTurn);
                if (negate != null) return negate;
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
                bool sacRecur = SacrificeRecurEnabled && IsSacrificeToRecur(def.Effect, def.Cost, card.Rested);
                if (!BeneficialActivateMain(def.Effect) && !sacRecur) continue;
                // Never sacrifice this live body to recur when the trash holds NO valid recur target — the
                // "play from trash" whiffs and you've traded a Character for the ability's incidental draw
                // (OP10-082 Kuzan into a trash whose only ≤5 {Blackbeard} is Kuzan itself, excluded).
                if (sacRecur && !HasValidTrashRecurTarget(world, seat, def, card)) continue;
                // A DON!! −N activation (e.g. Crocodile's DON!! −4 leader bounce) must clear its DON-return
                // cost. For a no-recovery deck that returned DON!! is real tempo, so the removal must be worth
                // it; for a re-ramp deck it is nearly free. Not marked attempted — a better target may appear.
                if (ActivationDonCostGate && !DonOpportunityModel.ActivationClearsDonCost(world, seat, def.Effect)) continue;

                // Mark before validating. Even a repeatable ability is tried at most once this turn; the
                // engine remains the legality oracle, and an invalid/no-op action falls through to the greedy
                // incumbent. LegalActions.Validate clones the state, applies the command, and returns it only
                // if the position actually changed — so it never mutates the live game and never plays a no-op.
                attemptedThisTurn.Add(card.InstanceId);
                var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = card.InstanceId };
                var validated = LegalActions.Validate(world, seat, new List<GameCommand> { activation });
                if (validated.Count == 0) continue;
                // VALUE GATE (iter 10): only fire if the activation doesn't WORSEN the position. Otherwise this
                // fires ANY whitelisted ability that merely CHANGES state — the oracle showed it over-firing
                // harmful [Activate: Main] abilities (e.g. OP10-082 → 0% win when attacking was 60%). Compare the
                // resulting position's eval to the current; skip a clear value loss. Off by default (A/B).
                if ((ActivationValueGate || ActivationValueGateSeat == seat || ActivationValueGateSeat == "both")
                    && Evaluation.Score(validated[0].Value, seat) < Evaluation.Score(world, seat) - ValueGateMargin)
                    continue;

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

        /// <summary>A/B (iter 10): gate every inserted [Activate: Main] on a value check — skip it if the
        /// resulting position evaluates WORSE than not acting (by <see cref="ValueGateMargin"/>). Targets the
        /// oracle-found over-firing of harmful activations (OP10-082 etc.) in activation-engine/Blackbeard decks.</summary>
        /// <summary>Seat-scoped so a CANDIDATE (this gate ON) can face the CHAMPION (gate off) head-to-head in
        /// the same game (the champion-gauntlet A/B). null = off for both; a seat name = on for that seat;
        /// "both" = on for both (the ship-it setting). </summary>
        public static string ActivationValueGateSeat = null;
        public static bool ActivationValueGate = false;   // legacy global (OR'd in); prefer the seat-scoped field
        public static double ValueGateMargin = 0.0;

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

        /// <summary>True iff the sacrifice-recur ability actually has a VALID body to bring back from the trash
        /// right now: a Character matching the recur clause's {type}, cost cap, and "other than [Name]"
        /// exclusion. Without this the bot fires OP10-082 Kuzan ("trash this: Draw 1. Then play up to 1
        /// {Blackbeard} ≤5 other than [Kuzan] from trash") when the only ≤5 {Blackbeard} in the trash is Kuzan
        /// itself — trashing a live 5-cost body for nothing but the incidental draw. Text-driven, card-id-free.</summary>
        public static bool HasValidTrashRecurTarget(GameState world, string seat, CardDef def, CardInstance source)
        {
            if (def?.Effect == null || !world.Players.TryGetValue(seat, out var p)) return true;
            string e = def.Effect;
            int playIdx = e.IndexOf("play up to", StringComparison.OrdinalIgnoreCase);
            if (playIdx < 0) playIdx = e.IndexOf("play 1", StringComparison.OrdinalIgnoreCase);
            if (playIdx < 0) playIdx = e.IndexOf("play a ", StringComparison.OrdinalIgnoreCase);
            string recur = playIdx >= 0 ? e.Substring(playIdx) : e;

            var featM = System.Text.RegularExpressions.Regex.Match(recur, @"\{([^}]+)\}");
            string feature = featM.Success ? featM.Groups[1].Value.Trim() : null;
            var capM = System.Text.RegularExpressions.Regex.Match(recur, @"cost of (\d+) or less", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            int costCap = capM.Success ? int.Parse(capM.Groups[1].Value) : -1;
            var exM = System.Text.RegularExpressions.Regex.Match(recur, @"other than \[([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string exclude = exM.Success ? exM.Groups[1].Value.Trim() : null;

            foreach (var c in p.Trash)
            {
                if (c == null || (source != null && c.InstanceId == source.InstanceId)) continue;
                var d = GameEngine.GetCard(c);
                if (d == null || d.Type != "character") continue;
                if (!string.IsNullOrEmpty(feature) && !d.HasFeature(feature)) continue;
                if (costCap >= 0 && d.Cost > costCap) continue;
                if (!string.IsNullOrEmpty(exclude) && GameEngine.NameMatches(world, c, exclude)) continue;
                return true;   // a real recur target exists
            }
            return false;
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

        // Next step of the leader-engine line (attach-to-leader to meet [DON!! xN], then activate), or null if
        // not applicable/worthwhile now. Fires only when there are rested DON to give and a Character to receive.
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

            var attachedIds = new HashSet<string>(AllAttachedDonIds(p));
            int restedToGive = p.CostArea.Count(d => d.Rested && !attachedIds.Contains(d.InstanceId));
            bool hasRecipient = p.CharacterArea.Any(c => c != null);
            if (restedToGive < 1 || !hasRecipient) return null;

            int attachedOnLeader = leader.AttachedDonIds?.Count ?? 0;
            if (attachedOnLeader < need)
            {
                if (GameEngine.ActiveDonCount(p) < 1) return null;
                var setup = new GameCommand { Type = "attachDon", Seat = seat, Target = leader.InstanceId, Amount = 1 };
                if (blacklist.Contains(IntermediateBot.Signature(setup))) return null;
                return setup;
            }

            attemptedThisTurn.Add(leader.InstanceId);
            var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = leader.InstanceId };
            if (blacklist.Contains(IntermediateBot.Signature(activation))) return null;
            if (LegalActions.Validate(world, seat, new List<GameCommand> { activation }).Count == 0) return null;

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
        // (the printed conditions key off "battled …"). Meet its [DON!! xN] gate, then activate; the engine
        // enforces the precise condition, so an unmet one simply no-ops and we skip. Text-driven, card-id-free.
        private static GameCommand RestandSetup(GameState world, string seat, HashSet<string> blacklist,
            HashSet<string> attemptedThisTurn)
        {
            var p = world.Players[seat];
            foreach (var card in BoardCards(p))
            {
                if (card == null || !card.Rested
                    || attemptedThisTurn.Contains(card.InstanceId)
                    || p.AbilityUsedThisTurn.Contains(card.InstanceId)) continue;
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
                if (LegalActions.Validate(world, seat, new List<GameCommand> { activation }).Count == 0) continue;
                Interlocked.Increment(ref _activations);
                return activation;
            }
            return null;
        }

        /// <summary>A/B toggle: proactively fire a "[Activate: Main] you may trash 1 from your hand: negate
        /// your opponent's [On Play] effects" ability (Blackbeard OP09-081). Off = keep it disabled.
        /// IMPORTANT — fair information: this AI is meant to see only what a PLAYER sees (its own hand, the
        /// board, both trashes, revealed Life, and the opponent's pre-match decklist), NEVER the opponent's
        /// hand contents. So the decision keys off the opponent's hand SIZE (public), their DON (public), and
        /// the fact that they've already REVEALED [On Play] Characters (in trash / on board — public) — the
        /// evidence a human uses to judge their deck runs impactful on-plays. It never peeks at held cards, so
        /// the timing is an honest guess (and will sometimes miss), exactly as it would be for a person.</summary>
        public static bool OnPlayNegateEnabled = true;

        /// <summary>Minimum cost of a REVEALED opponent [On Play] Character (in their trash or on their board)
        /// that marks their deck as on-play-centric enough to be worth denying. Tunable; 4 = a value body.</summary>
        public static int OnPlayNegateThreshold = 4;

        private static GameCommand OnPlayNegateSetup(GameState world, string seat,
            HashSet<string> blacklist, HashSet<string> attemptedThisTurn)
        {
            var p = world.Players[seat];
            // Only when we can COMFORTABLY spare the card — this is a speculative deny, not a value play.
            if (p.Hand.Count < 4) return null;
            var opp = world.Players[GameEngine.OtherSeat(seat)];
            if (opp.Hand.Count < 2) return null;   // opponent has no real hand to threaten a big on-play with
            // PUBLIC evidence the opponent's deck runs impactful [On Play] Characters: any they've already
            // played (now on the board or in their trash). Never their hidden hand.
            bool RevealedOnPlay(CardInstance c)
            {
                var d = GameEngine.GetCard(c);
                return d != null && d.Type == "character" && d.Cost >= OnPlayNegateThreshold
                    && (d.Effect ?? "").IndexOf("[On Play]", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            bool oppShownOnPlay = opp.Trash.Any(RevealedOnPlay)
                || opp.CharacterArea.Any(c => c != null && RevealedOnPlay(c));
            if (!oppShownOnPlay) return null;

            foreach (var card in BoardCards(p))
            {
                if (card == null || attemptedThisTurn.Contains(card.InstanceId)
                    || p.AbilityUsedThisTurn.Contains(card.InstanceId)) continue;
                if (!IsOnPlayNegateActivate(GameEngine.GetCard(card)?.Effect)) continue;
                attemptedThisTurn.Add(card.InstanceId);
                var activation = new GameCommand { Type = "activateMain", Seat = seat, Target = card.InstanceId };
                if (blacklist.Contains(IntermediateBot.Signature(activation))) continue;
                // The engine is the legality oracle; a no-op (e.g. cost unpayable) validates to nothing and
                // is skipped. The trash-1-from-hand cost is paid on the next tick by DecideEffect, which
                // trashes the least-valuable hand card. The negate payoff needs no further target.
                if (LegalActions.Validate(world, seat, new List<GameCommand> { activation }).Count == 0) continue;
                Interlocked.Increment(ref _activations);
                return activation;
            }
            return null;
        }

        // True for a "[Activate: Main] you may trash <N> from your hand: … your opponent's [On Play] effects
        // are negated …" ability (Blackbeard OP09-081). Inspects only the [Activate: Main] line; card-id-free.
        private static bool IsOnPlayNegateActivate(string effect)
        {
            if (string.IsNullOrEmpty(effect)) return false;
            foreach (var line in effect.Split('\n'))
            {
                if (line.IndexOf("[Activate: Main]", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string e = line.ToLowerInvariant();
                if (e.Contains("trash") && e.Contains("from your hand")
                    && e.Contains("opponent") && e.Contains("[on play]") && e.Contains("negat"))
                    return true;
            }
            return false;
        }

        private static bool BeneficialActivateMain(string effect)
        {
            if (string.IsNullOrEmpty(effect)
                || effect.IndexOf("[Activate: Main]", StringComparison.OrdinalIgnoreCase) < 0) return false;
            // Inspect ONLY the [Activate: Main] clause, not the whole effect. A beneficial verb printed on a
            // DIFFERENT ability line — e.g. Van Augur OP09-083's "[On K.O.] Draw 1 card" — must not make the
            // Activate:Main line (which merely gives an opponent −3 cost, useless without a cost-KO to combo)
            // read as worth resting the Character for. Scanning the full text made the bot rest Van Augur every
            // turn for a no-payoff −cost, losing an attacker. Abilities are printed one per line.
            string e = string.Join("\n",
                effect.Split('\n').Where(l => l.IndexOf("[Activate: Main]", StringComparison.OrdinalIgnoreCase) >= 0))
                .ToLowerInvariant();
            if (e.Length == 0) return false;
            if (e.Contains("you may trash") && e.Contains("from your hand")) return false;
            if (e.Contains("trash") && e.Contains("from the top of your life")) return false;
            if (e.Contains("trash this character")) return false;
            return e.Contains("draw ") || e.Contains("k.o.") || e.Contains("set up to") || e.Contains("rest up to")
                || e.Contains("play up to") || e.Contains("add up to") || e.Contains("return up to")
                || e.Contains("gains +") || e.Contains("look at")
                // Denial: "negate the effect of up to 1 of your opponent's Leader/Character" (OP09-093 Teach —
                // and it's TURN-LIMITED to the turn played, so failing to consider it loses the ability outright).
                || e.Contains("negate the effect")
                // ST01-001 Luffy / ST01-007 Nami: turn rested DON!! into an extra attacker buff.
                || (e.Contains("give") && e.Contains("rested don!!"));
        }
    }
}
