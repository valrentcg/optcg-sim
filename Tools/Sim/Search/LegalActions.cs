using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Search
{
    /// <summary>
    /// Enumerates candidate legal actions for a seat so a search agent can score each one
    /// (blueprint §2 Legal Action Generator, §9 solvers). The engine doesn't expose a legal-move list,
    /// so this generates a plausible SUPERSET with cheap filters (affordability, rested targets, has-Blocker)
    /// and then uses the engine itself as the legality oracle: <see cref="Validate"/> clones the state,
    /// applies each candidate, and keeps only the ones that actually change the game — no need to
    /// re-encode every rule (§1.2 the engine stays the authority on legality).
    ///
    /// Covers the high-frequency decisions (main-phase plays/attacks/DON, and defense counter/block).
    /// Effect-resolution and deck-look choices are left to the base policy for now.
    /// </summary>
    public static class LegalActions
    {
        public static List<GameCommand> Candidates(GameState state, string seat)
        {
            var list = new List<GameCommand>();
            if (state == null || !state.Players.ContainsKey(seat)) return list;
            var me = state.Players[seat];
            var oppSeat = GameEngine.OtherSeat(seat);

            // ---- resolve a "choose one" branch (Choose: A / B) ----
            if (state.ActiveChoice != null && state.ActiveChoice.Seat == seat)
            {
                list.Add(new GameCommand { Type = "resolveChoice", Seat = seat, Target = "A" });
                list.Add(new GameCommand { Type = "resolveChoice", Seat = seat, Target = "B" });
                return list;
            }

            // ---- resolve a "look at top N / search deck" flow (deckLookSelect + confirm) ----
            if (state.DeckLook != null && state.DeckLook.Seat == seat)
            {
                foreach (var c in state.DeckLook.Cards) // pick each eligible card
                    list.Add(new GameCommand { Type = "deckLookSelect", Seat = seat, Target = c.InstanceId });
                list.Add(new GameCommand { Type = "deckLookSelect", Seat = seat, Target = "" });       // decline/none
                list.Add(new GameCommand { Type = "deckLookConfirmOrder", Seat = seat, OrderedInstanceIds = state.DeckLook.Cards.Select(c => c.InstanceId).ToList() });
                list.Add(new GameCommand { Type = "deckLookScryConfirm", Seat = seat, OrderedInstanceIds = state.DeckLook.Cards.Select(c => c.InstanceId).ToList() });
                return list;
            }

            // ---- resolve a pending effect's TARGET (On Play / Activate:Main / trigger targeting) ----
            var myEffect = state.PendingEffects.FirstOrDefault(e => e.Seat == seat);
            if (myEffect != null)
            {
                // generate resolveEffect for every plausibly-targetable instance across zones; the engine
                // oracle (Validate) keeps only the ones this specific effect actually accepts.
                foreach (var id in TargetCandidates(state, seat))
                    list.Add(new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = myEffect.EffectId, Target = id });
                list.Add(new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = myEffect.EffectId, Target = "" }); // empty selection
                // ALWAYS offer passEffect — never gate it on myEffect.Optional. Gating it caused ~10% of
                // games to ABORT: an effect whose targets all fail the card's filter (e.g. Gecko Moria's
                // "play a {Thriller Bark Pirates} cost<=4 from your trash" with no such card in trash)
                // leaves every resolveEffect rejected, while Target="" returns WaitingForTarget and so
                // registers as a no-op. With no legal command the bot returns null, GameRunner's `did == 0`
                // guard fires, and the game silently dies mid-turn — a FORFEIT that rules-8.2 then awards to
                // the opponent. The bot lost games it never played, by playing its OWN card.
                // Optional is the engine's flag for "the PLAYER may decline", which is not the same question
                // as "is passEffect a legal command here" — and this file's whole design is to propose a
                // superset and let the engine oracle in Validate decide. If passing is illegal it is a
                // no-op and gets filtered; if it is legal it un-sticks the game. Costs one clone.
                list.Add(new GameCommand { Type = "passEffect", Seat = seat, EffectId = myEffect.EffectId });
                return list;
            }

            // ---- defense (a battle is targeting this seat) ----
            if (state.Battle != null && state.Battle.TargetSeat == seat)
            {
                switch (state.Battle.Step)
                {
                    case "block":
                        list.Add(new GameCommand { Type = "passBlock", Seat = seat });
                        // Use the ENGINE's authority (HasBlocker = printed OR granted OR modifier) rather
                        // than the printed Keywords array. Reading `GetCard(c).Keywords.Contains("Blocker")`
                        // saw ONLY printed keywords, so a Character granted [Blocker] by an effect could
                        // never be offered as a blocker — the move did not exist for the bot. Same class of
                        // defect as the passEffect/passTrigger gates: a legal action the bot cannot propose.
                        foreach (var c in me.CharacterArea.Where(c => c != null && !c.Rested
                            && GameEngine.HasBlocker(state, c)))
                            list.Add(new GameCommand { Type = "blockAttack", Seat = seat, Blocker = c.InstanceId });
                        break;
                    case "counter":
                        list.Add(new GameCommand { Type = "passCounter", Seat = seat });
                        foreach (var c in me.Hand.Where(c => GameEngine.CanCounterFromHand(state, seat, c)))
                            list.Add(new GameCommand { Type = "counterWithCard", Seat = seat, InstanceId = c.InstanceId });
                        break;
                    case "trigger":
                        // Every other battle step offers BOTH a use and a pass (blockAttack/passBlock,
                        // counterWithCard/passCounter) and the engine accepts passTrigger — but it was never
                        // proposed here, so the bot could only ever FIRE a trigger, never decline one.
                        // When useTrigger is illegal (the revealed Life card has no [Trigger] effect) it is a
                        // no-op, gets filtered, and NO legal command remains ⇒ the game forfeits via
                        // GameRunner's `did == 0` guard. Same failure class as the passEffect gate.
                        // It is also a strategic hole: declining a bad trigger was not representable.
                        list.Add(new GameCommand { Type = "passTrigger", Seat = seat });
                        list.Add(new GameCommand { Type = "useTrigger", Seat = seat });
                        break;
                    default:
                        list.Add(new GameCommand { Type = "resolveAttack", Seat = seat });
                        break;
                }
                return list; // during a battle, defense is the only thing to decide
            }

            // ---- main phase (our active turn, nothing pending) ----
            if (state.ActiveSeat == seat && state.Phase == "main"
                && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null)
            {
                list.Add(new GameCommand { Type = "endTurn", Seat = seat });
                int don = GameEngine.ActiveDonCount(me);
                var opp = state.Players[GameEngine.OtherSeat(seat)];

                // play each affordable hand card
                foreach (var c in me.Hand)
                {
                    var d = CardData.GetCard(c.CardId);
                    if (d != null && d.Type != "leader" && GameEngine.GetCost(state, c) <= don)
                        list.Add(new GameCommand { Type = "playCard", Seat = seat, InstanceId = c.InstanceId });
                }
                // attach 1 DON to each active board character (and leader) — search can stack
                if (don > 0)
                    foreach (var c in BoardAttackers(me))
                        list.Add(new GameCommand { Type = "attachDon", Seat = seat, Target = c.InstanceId, Amount = 1 });
                // activate a Main effect on each board card
                foreach (var c in BoardAttackers(me))
                    list.Add(new GameCommand { Type = "activateMain", Seat = seat, Target = c.InstanceId });
                // declare an attack: each ready attacker × each legal target (opp leader + rested opp chars)
                var targets = new List<string>();
                if (opp.Leader != null) targets.Add(opp.Leader.InstanceId);
                targets.AddRange(opp.CharacterArea.Where(c => c != null && c.Rested).Select(c => c.InstanceId));
                foreach (var atk in BoardAttackers(me).Where(c => !c.Rested))
                    foreach (var tgt in targets)
                        list.Add(new GameCommand { Type = "declareAttack", Seat = seat, Attacker = atk.InstanceId, Target = tgt });
                return list;
            }

            return list; // other states (mulligan/coinflip/effect resolution) handled by the base policy
        }

        // Every instance an effect might target, across both boards + own hand/trash. Broad on purpose;
        // the engine oracle in Validate() prunes to what the specific effect actually accepts.
        private static IEnumerable<string> TargetCandidates(GameState state, string seat)
        {
            var me = state.Players[seat];
            var op = state.Players[GameEngine.OtherSeat(seat)];
            if (me.Leader != null) yield return me.Leader.InstanceId;
            if (op.Leader != null) yield return op.Leader.InstanceId;
            foreach (var c in me.CharacterArea) if (c != null) yield return c.InstanceId;
            foreach (var c in op.CharacterArea) if (c != null) yield return c.InstanceId;
            foreach (var c in me.Hand) yield return c.InstanceId;
            foreach (var c in me.Trash) yield return c.InstanceId;
            if (me.Stage != null) yield return me.Stage.InstanceId;
            if (op.Stage != null) yield return op.Stage.InstanceId;
        }

        // Ready pieces that can act: the leader + non-null characters.
        private static IEnumerable<CardInstance> BoardAttackers(PlayerState p)
        {
            if (p.Leader != null) yield return p.Leader;
            foreach (var c in p.CharacterArea) if (c != null) yield return c;
        }

        /// <summary>Keep only candidates the engine actually accepts: clone, apply, and detect a real
        /// state change. This turns the cheap-filtered superset into the true legal-move set.</summary>
        /// <summary>Clone+apply count on this thread — the atom of search cost, so it is a DETERMINISTIC
        /// proxy for elapsed time (unlike a wall clock, it is identical across runs and machines, which
        /// keeps seeded games reproducible). Search budgets are denominated in these units.
        /// [ThreadStatic] because the tournament searches many games in parallel.</summary>
        [ThreadStatic] public static long WorkUnits;

        /// <summary>The <see cref="WorkUnits"/> value at which the current search must stop (0 = no limit).
        /// Shared so that every layer of the search honours ONE budget: the planner sets it for a turn, and
        /// ValuePolicy's defence lookahead reads it and degrades to cheap myopic scoring once it is spent.
        /// Before this existed, TurnPlanner's budget bounded only its OWN node expansion while the opponent
        /// model it called at every node ran an unbounded battle roll-out underneath it — so a "bounded"
        /// turn could still cost millions of clones. Deterministic (a clone count, never a wall clock), so
        /// seeded games stay bit-for-bit reproducible across runs and machines.</summary>
        [ThreadStatic] public static long WorkDeadline;

        public static bool OutOfWork => WorkDeadline > 0 && WorkUnits >= WorkDeadline;

        /// <summary>Set a deadline <paramref name="budget"/> units from now, returning the previous one so
        /// the caller can restore it (budgets nest: the bot's defence budget sits inside no planner turn,
        /// but a planner turn contains many policy decisions). A budget of 0 means unlimited.</summary>
        public static long PushDeadline(long budget)
        {
            long prev = WorkDeadline;
            if (budget > 0)
            {
                long want = WorkUnits + budget;
                // Never widen an enclosing budget — the tightest deadline in scope always wins.
                WorkDeadline = prev > 0 && prev < want ? prev : want;
            }
            return prev;
        }

        public static void PopDeadline(long prev) => WorkDeadline = prev;

        public static List<(GameCommand cmd, GameState result)> Validate(GameState state, string seat, List<GameCommand> candidates)
        {
            var legal = new List<(GameCommand, GameState)>();
            long before = Fingerprint(state);
            foreach (var cmd in candidates)
            {
                var clone = GameClone.Clone(state);
                WorkUnits++;
                GameEngine.ApplyCommand(clone, cmd);
                if (Fingerprint(clone) != before) legal.Add((cmd, clone));
            }
            return legal;
        }

        // Real-state fingerprint — deliberately EXCLUDES CommandHistory and EventLog, because
        // ApplyCommand appends to both on EVERY command (even rejected no-ops). Keying on those would
        // make every candidate look "legal" and let search loop forever. Only genuine game state counts.
        /// <summary>Public real-state hash (excludes append-only log/history) — used by the game driver
        /// to detect no-op commands generically, without any IntermediateBot dependency.</summary>
        public static long StateFingerprint(GameState s) => Fingerprint(s);

        private static long Fingerprint(GameState s)
        {
            long h = (s.Battle != null ? 7 : 0) + s.PendingEffects.Count * 13
                     + s.TurnNumber * 17 + (s.Phase?.GetHashCode() ?? 0) + (s.ActiveSeat?.GetHashCode() ?? 0)
                     + (s.ActiveChoice != null ? 29 : 0) + (s.DeckLook?.Cards.Count * 37 ?? 0)
                     + (s.Battle?.Step?.GetHashCode() ?? 0) + (s.Status == "finished" ? 999_999_999L : 0);
            foreach (var p in s.Players.Values)
            {
                h = h * 31 + p.Hand.Count * 7 + p.CharacterArea.Count(c => c != null) * 11
                    + p.Trash.Count * 3 + p.Life.Count * 5 + GameEngine.ActiveDonCount(p) * 2 + p.Deck.Count * 101;
                // include rested/power so DON-attach and rest/KO changes register
                foreach (var c in p.CharacterArea) if (c != null) h = h * 17 + GameEngine.GetPower(s, c) + (c.Rested ? 1 : 0);
                if (p.Leader != null) h = h * 17 + GameEngine.GetPower(s, p.Leader) + (p.Leader.Rested ? 1 : 0);
            }
            return h;
        }
    }
}
