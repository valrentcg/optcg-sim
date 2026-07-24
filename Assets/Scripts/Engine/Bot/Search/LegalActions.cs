// Legal-action enumerator for the Advanced (search) bot. Generates a candidate SUPERSET with cheap
// filters, then uses the engine itself as the legality oracle (Validate: clone + apply, keep the
// moves that actually change state). Covers main-phase plays/attacks/DON/activation, defense
// counter/block/trigger, effect targeting (On Play / Main / trigger), A/B choices, deck-look picks,
// and passes. Mirror of the out-of-ship Tools/Sim enumerator. Pure C#.

using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    public static class LegalActions
    {
        public static List<GameCommand> Candidates(GameState state, string seat)
        {
            var list = new List<GameCommand>();
            if (state == null || !state.Players.ContainsKey(seat)) return list;
            var me = state.Players[seat];

            if (state.ActiveChoice != null && state.ActiveChoice.Seat == seat)
            {
                list.Add(new GameCommand { Type = "resolveChoice", Seat = seat, Target = "A" });
                list.Add(new GameCommand { Type = "resolveChoice", Seat = seat, Target = "B" });
                return list;
            }

            if (state.PendingCharReplace != null && state.PendingCharReplace.Seat == seat)
            {
                // 6th-character rule: choose one of my own Characters to trash for the pending play, or skip.
                foreach (var c in me.CharacterArea.Where(c => c != null))
                    list.Add(new GameCommand { Type = "charReplace", Seat = seat, Target = c.InstanceId });
                list.Add(new GameCommand { Type = "charReplace", Seat = seat, Target = "" });
                return list;
            }

            if (state.DeckLook != null && state.DeckLook.Seat == seat)
            {
                foreach (var c in state.DeckLook.Cards)
                    list.Add(new GameCommand { Type = "deckLookSelect", Seat = seat, Target = c.InstanceId });
                list.Add(new GameCommand { Type = "deckLookSelect", Seat = seat, Target = "" });
                list.Add(new GameCommand { Type = "deckLookConfirmOrder", Seat = seat, OrderedInstanceIds = state.DeckLook.Cards.Select(c => c.InstanceId).ToList() });
                list.Add(new GameCommand { Type = "deckLookScryConfirm", Seat = seat, OrderedInstanceIds = state.DeckLook.Cards.Select(c => c.InstanceId).ToList() });
                return list;
            }

            var myEffect = state.PendingEffects.FirstOrDefault(e => e.Seat == seat);
            if (myEffect != null)
            {
                foreach (var id in TargetCandidates(state, seat))
                    list.Add(new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = myEffect.EffectId, Target = id });
                list.Add(new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = myEffect.EffectId, Target = "" });
                if (myEffect.Optional)
                    list.Add(new GameCommand { Type = "passEffect", Seat = seat, EffectId = myEffect.EffectId });
                return list;
            }

            if (state.Battle != null && state.Battle.TargetSeat == seat)
            {
                switch (state.Battle.Step)
                {
                    case "block":
                        list.Add(new GameCommand { Type = "passBlock", Seat = seat });
                        foreach (var c in me.CharacterArea.Where(c => c != null && !c.Rested && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false)))
                            list.Add(new GameCommand { Type = "blockAttack", Seat = seat, Blocker = c.InstanceId });
                        break;
                    case "counter":
                        list.Add(new GameCommand { Type = "passCounter", Seat = seat });
                        foreach (var c in me.Hand.Where(c => GameEngine.CanCounterFromHand(state, seat, c)))
                            list.Add(new GameCommand { Type = "counterWithCard", Seat = seat, InstanceId = c.InstanceId });
                        break;
                    case "trigger":
                        list.Add(new GameCommand { Type = "useTrigger", Seat = seat });
                        list.Add(new GameCommand { Type = "passTrigger", Seat = seat });
                        break;
                    default:
                        list.Add(new GameCommand { Type = "resolveAttack", Seat = seat });
                        break;
                }
                return list;
            }

            if (state.ActiveSeat == seat && state.Phase == "main"
                && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null
                && state.PendingCharReplace == null)
            {
                list.Add(new GameCommand { Type = "endTurn", Seat = seat });
                int don = GameEngine.ActiveDonCount(me);
                var opp = state.Players[GameEngine.OtherSeat(seat)];
                foreach (var c in me.Hand)
                {
                    var d = CardData.GetCard(c.CardId);
                    if (d == null || d.Type == "leader" || GameEngine.GetCost(state, c) > don) continue;
                    // Don't waste a pure targeted-removal EVENT (e.g. a K.O. event) when it has no legal target
                    // — the classic case being an all-immune {Five Elders} board (7+ trash). Burning the card +
                    // DON for a guaranteed no-op is never right, so drop it from the candidate set.
                    if (d.Type == "event" && !GameEngine.RemovalEventHasTarget(state, seat, d)) continue;
                    if (d.Type == "character" && !me.CharacterArea.Any(s => s == null))
                    {
                        // Full board: the 6th-Character rule lets you play over an existing Character,
                        // trashing it. Emit a replace of the WEAKEST current Character (bounded branching);
                        // the search decides whether it's worth it. Without a SlotIndex the engine would
                        // just reject the play ("No open character slot") — the source of the log spam.
                        int weakest = -1, weakestPow = int.MaxValue;
                        for (int s = 0; s < me.CharacterArea.Count; s++)
                        {
                            var occ = me.CharacterArea[s];
                            if (occ == null) continue;
                            int pw = GameEngine.GetPower(state, occ);
                            if (pw < weakestPow) { weakestPow = pw; weakest = s; }
                        }
                        if (weakest >= 0)
                            list.Add(new GameCommand { Type = "playCard", Seat = seat, InstanceId = c.InstanceId, SlotIndex = weakest });
                    }
                    else
                    {
                        list.Add(new GameCommand { Type = "playCard", Seat = seat, InstanceId = c.InstanceId });
                    }
                }
                // Only offer to attach DON!! to cards that can actually ATTACK this turn — attaching
                // to a summoning-sick Character (that can't swing) just wastes the DON's power boost.
                if (don > 0)
                    foreach (var c in Board(me).Where(c => !c.Rested && !GameEngine.IsSummoningSick(state, c)))
                        list.Add(new GameCommand { Type = "attachDon", Seat = seat, Target = c.InstanceId, Amount = 1 });
                foreach (var c in Board(me))
                    list.Add(new GameCommand { Type = "activateMain", Seat = seat, Target = c.InstanceId });
                var targets = new List<string>();
                if (opp.Leader != null) targets.Add(opp.Leader.InstanceId);
                targets.AddRange(opp.CharacterArea.Where(c => c != null && c.Rested).Select(c => c.InstanceId));
                foreach (var atk in Board(me).Where(c => !c.Rested && !GameEngine.IsSummoningSick(state, c)))
                    foreach (var tgt in targets)
                        list.Add(new GameCommand { Type = "declareAttack", Seat = seat, Attacker = atk.InstanceId, Target = tgt });
            }
            return list;
        }

        private static IEnumerable<CardInstance> Board(PlayerState p)
        {
            if (p.Leader != null) yield return p.Leader;
            foreach (var c in p.CharacterArea) if (c != null) yield return c;
        }

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

        /// <summary>Keep only candidates the engine accepts: clone, apply, keep the ones that change
        /// real game state (fingerprint excludes the always-growing log/history).</summary>
        public static List<KeyValuePair<GameCommand, GameState>> Validate(GameState state, string seat, List<GameCommand> candidates)
        {
            var legal = new List<KeyValuePair<GameCommand, GameState>>();
            long before = Fingerprint(state);
            foreach (var cmd in candidates)
            {
                var clone = GameClone.Clone(state);
                GameEngine.ApplyCommand(clone, cmd);
                if (Fingerprint(clone) != before) legal.Add(new KeyValuePair<GameCommand, GameState>(cmd, clone));
            }
            return legal;
        }

        private static long Fingerprint(GameState s)
        {
            long h = (s.Battle != null ? 7 : 0) + s.PendingEffects.Count * 13
                     + s.TurnNumber * 17 + (s.Phase == null ? 0 : s.Phase.GetHashCode()) + (s.ActiveSeat == null ? 0 : s.ActiveSeat.GetHashCode())
                     + (s.ActiveChoice != null ? 29 : 0) + (s.DeckLook == null ? 0 : s.DeckLook.Cards.Count * 37)
                     + (s.Battle == null || s.Battle.Step == null ? 0 : s.Battle.Step.GetHashCode()) + (s.Status == "finished" ? 999999999L : 0);
            // Pending effects can advance without changing their COUNT: a target may receive a cost/keyword
            // modifier and a ". Then," remainder replaces the first clause with the second. Counting only the
            // queue made those perfectly legal target selections look like no-ops, so search could not reason
            // about cards such as EB01-046 Brook or OP06-101 O-Nami.
            foreach (var e in s.PendingEffects)
            {
                h = h * 31 + (e.EffectId?.GetHashCode() ?? 0);
                h = h * 31 + (e.Text?.GetHashCode() ?? 0);
                h = h * 31 + e.SelectionsRemaining;
                h = h * 31 + e.RemainingBudget;
            }
            foreach (var m in s.ActiveModifiers)
            {
                h = h * 31 + (m.TargetInstanceId?.GetHashCode() ?? 0);
                h = h * 31 + (m.ModifierType?.GetHashCode() ?? 0);
                h = h * 31 + (m.Keyword?.GetHashCode() ?? 0);
                h = h * 31 + (m.Duration?.GetHashCode() ?? 0);
            }
            foreach (var p in s.Players.Values)
            {
                h = h * 31 + p.Hand.Count * 7 + p.CharacterArea.Count(c => c != null) * 11
                    + p.Trash.Count * 3 + p.Life.Count * 5 + GameEngine.ActiveDonCount(p) * 2 + p.Deck.Count * 101;
                foreach (var c in p.CharacterArea) if (c != null) h = h * 17 + GameEngine.GetPower(s, c) + (c.Rested ? 1 : 0);
                if (p.Leader != null) h = h * 17 + GameEngine.GetPower(s, p.Leader) + (p.Leader.Rested ? 1 : 0);
                foreach (var c in p.CharacterArea.Where(c => c != null).Concat(new[] { p.Leader }.Where(c => c != null)))
                {
                    h = h * 31 + c.AttachedDonIds.Count;
                    foreach (var m in c.Modifiers)
                        h = h * 31 + m.PowerDelta * 3 + m.CostDelta * 5 + (m.ExpiresAt?.GetHashCode() ?? 0);
                }
            }
            return h;
        }
    }
}
