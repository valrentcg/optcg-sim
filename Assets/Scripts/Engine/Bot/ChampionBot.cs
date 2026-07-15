// Intermediate-tier bot: the champion STYLE discovered by the out-of-ship Elo population tournament
// (Tools/Sim, Results/tournament — Elo 1606). It plays like IntermediateBot except for a handful of
// tuned strategic thresholds (mulligan keep bar, attack targeting, counter/block spending, turn
// order) that the tournament evolved. Exposes the same single-decision-per-tick interface as
// IntermediateBot so GameManager drives it identically. Pure C#, no UnityEngine dependency.
//
// Genome (settled tournament champion):
//   MulliganKeep=1  FaceBias=0.5  CounterLifeFloor=2.27  CounterCharCost=5  BlockBias=0.5  TurnOrderFirst=1

using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot
{
    public static class ChampionBot
    {
        // ---- tuned thresholds (discovered champion genome) ----
        // Refined by the 37-deck meta tournament (2026-07-14): a more defensive-aggressive champion
        // (keep only strong hands, protect the Leader hard, block whenever possible, pressure the
        // opponent's leader). Beat the previous champion 53.7% head-to-head over 300 games.
        private const double MulliganKeep = 4.0;      // keep hands with >= this many early (<=2 cost) plays
        private const double FaceBias = 0.81;         // >=0.5 ⇒ send attacks at the leader
        private const double CounterLifeFloor = 5.0;  // spend counters to protect the Leader while Life <= this
        private const double CounterCharCost = 4.96;  // spend counters to save a Character only if its cost >= this
        private const double BlockBias = 0.95;        // willingness to activate a Blocker
        private const bool GoFirst = true;            // turn-order choice when we win the flip (gene 0.75 ⇒ first)

        /// <summary>One decision for <paramref name="seat"/> (or null). Mirrors
        /// IntermediateBot.DecideOneCommand so GameManager's tick loop is unchanged.</summary>
        public static GameCommand DecideOneCommand(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;
            var me = state.Players[seat];

            // turn order
            if (state.Status == "coinflip")
            {
                if (state.CoinFlipWinner != seat) return null;
                return new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = GoFirst };
            }

            // mulligan
            if (state.Status == "mulligan")
            {
                if (me.MulliganDecided) return null;
                int early = me.Hand.Count(c => { var d = CardData.GetCard(c.CardId); return d != null && d.Type == "character" && d.Cost <= 2; });
                return new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = !(early >= MulliganKeep) };
            }

            // defense (counter / block) with tuned thresholds
            if (state.Battle != null && state.Battle.TargetSeat == seat)
            {
                if (state.Battle.Step == "counter") return DecideCounter(state, seat, me);
                if (state.Battle.Step == "block") return DecideBlock(state, seat, me);
            }

            // otherwise use IntermediateBot's decision, but re-target attacks per FaceBias
            var cmd = IntermediateBot.DecideOneCommand(state, seat, blacklist);
            if (cmd != null && cmd.Type == "declareAttack" && FaceBias >= 0.5)
            {
                var oppLeader = state.Players[GameEngine.OtherSeat(seat)].Leader;
                if (oppLeader != null) cmd.Target = oppLeader.InstanceId; // always a legal target
            }
            return cmd;
        }

        // GameManager reuses IntermediateBot's SnapshotFor/Succeeded/Signature for its no-op blacklist;
        // ChampionBot returns the same command shapes, so those work unchanged.

        private static GameCommand DecideCounter(GameState state, string seat, PlayerState me)
        {
            var b = state.Battle;
            int need = b.AttackPower - b.DefensePower + 1;
            if (need <= 0) return new GameCommand { Type = "passCounter", Seat = seat };

            bool targetIsLeader = b.TargetId == me.Leader?.InstanceId;
            var target = FindOwn(me, b.TargetId);
            bool worth = targetIsLeader
                ? me.Life.Count <= CounterLifeFloor
                : (target != null && (CardData.GetCard(target.CardId)?.Cost ?? 0) >= CounterCharCost);
            if (!worth) return new GameCommand { Type = "passCounter", Seat = seat };

            var usable = me.Hand.Where(c => GameEngine.CanCounterFromHand(state, seat, c))
                .Select(c => (card: c, cp: GameEngine.GetCounterPower(c), type: CardData.GetCard(c.CardId)?.Type, cost: CardData.GetCard(c.CardId)?.Cost ?? 0))
                .Where(x => x.cp > 0).OrderByDescending(x => x.cp).ToList();
            if (MaxAffordable(state, seat, usable) < need) return new GameCommand { Type = "passCounter", Seat = seat };

            var single = usable.Where(x => x.cp >= need).OrderBy(x => x.cp).ToList();
            var pick = single.Count > 0 ? single[0] : (usable.Count > 0 ? usable[0] : default);
            if (pick.card == null) return new GameCommand { Type = "passCounter", Seat = seat };
            return new GameCommand { Type = "counterWithCard", Seat = seat, InstanceId = pick.card.InstanceId };
        }

        private static int MaxAffordable(GameState state, string seat, List<(CardInstance card, int cp, string type, int cost)> usable)
        {
            int don = GameEngine.ActiveDonCount(state.Players[seat]); int total = 0;
            var events = new List<(int cp, int cost)>();
            foreach (var x in usable) { if (x.type == "event") events.Add((x.cp, x.cost)); else total += x.cp; }
            foreach (var e in events.OrderByDescending(x => x.cp)) if (e.cost <= don) { don -= e.cost; total += e.cp; }
            return total;
        }

        private static GameCommand DecideBlock(GameState state, string seat, PlayerState me)
        {
            var b = state.Battle;
            var attacker = FindAny(state, b.AttackerSeat, b.AttackerId);
            int atkPow = attacker != null ? GameEngine.GetPower(state, attacker) : 0;
            var blockers = me.CharacterArea.Where(c => c != null && !c.Rested
                && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false)).ToList();
            if (blockers.Count == 0) return new GameCommand { Type = "passBlock", Seat = seat };

            bool lethal = b.TargetId == me.Leader?.InstanceId && me.Life.Count == 0;
            var survivors = blockers.Where(x => GameEngine.GetPower(state, x) > atkPow).OrderBy(x => GameEngine.GetPower(state, x)).ToList();
            bool shouldBlock = lethal || me.Life.Count <= 1 || (survivors.Count > 0 && BlockBias >= 0.5);
            if (!shouldBlock) return new GameCommand { Type = "passBlock", Seat = seat };
            var chosen = survivors.Count > 0 ? survivors[0] : blockers.OrderBy(x => GameEngine.GetPower(state, x)).First();
            return new GameCommand { Type = "blockAttack", Seat = seat, Blocker = chosen.InstanceId };
        }

        private static CardInstance FindOwn(PlayerState p, string id)
        {
            if (p.Leader?.InstanceId == id) return p.Leader;
            return p.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == id) ?? (p.Stage?.InstanceId == id ? p.Stage : null);
        }
        private static CardInstance FindAny(GameState state, string seat, string id)
        {
            if (string.IsNullOrEmpty(seat) || !state.Players.ContainsKey(seat)) return null;
            return FindOwn(state.Players[seat], id);
        }
    }
}
