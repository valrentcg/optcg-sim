using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim.Learning
{
    /// <summary>
    /// A strategy expressed as a small vector of transparent, human-readable weights (blueprint §10.2
    /// ConfigurableWeightedAgent, §12.2 "evolutionary optimization of transparent weights"). Two agents
    /// with the same genome play identically; the Evolver mutates the genome and keeps whatever wins
    /// more. This is the unit of "an approach to playing the game" that gets kept or discarded.
    /// </summary>
    public sealed class Genome
    {
        public double MulliganKeep;      // keep opening hand if it has >= this many early (<=2 cost) plays
        public double FaceBias;          // >=0.5 ⇒ attack the leader; <0.5 ⇒ prefer removing a rested Character
        public double CounterLifeFloor;  // spend counters to protect the Leader only while Life <= this
        public double CounterCharCost;   // spend counters to save a Character only if its cost >= this
        public double BlockBias;         // willingness to activate a Blocker (0..1)
        public double TurnOrderFirst;    // >=0.5 ⇒ choose to go first when winning the flip (LEARNED, not assumed)

        public static Genome Baseline() => new Genome
        { MulliganKeep = 1, FaceBias = 0.5, CounterLifeFloor = 3, CounterCharCost = 5, BlockBias = 0.5, TurnOrderFirst = 1 };

        public Genome Clone() => (Genome)MemberwiseClone();

        // gene bounds for mutation/clamping
        public static readonly (string name, double lo, double hi)[] Genes =
        {
            ("MulliganKeep", 0, 4), ("FaceBias", 0, 1), ("CounterLifeFloor", 0, 5),
            ("CounterCharCost", 0, 8), ("BlockBias", 0, 1), ("TurnOrderFirst", 0, 1),
        };
        public double Get(int i) => i switch { 0 => MulliganKeep, 1 => FaceBias, 2 => CounterLifeFloor, 3 => CounterCharCost, 4 => BlockBias, 5 => TurnOrderFirst, _ => 0 };
        public void Set(int i, double v) { switch (i) { case 0: MulliganKeep = v; break; case 1: FaceBias = v; break; case 2: CounterLifeFloor = v; break; case 3: CounterCharCost = v; break; case 4: BlockBias = v; break; case 5: TurnOrderFirst = v; break; } }
        public override string ToString() => string.Join(" ", Genes.Select((g, i) => $"{g.name}={Get(i).ToString("0.00", CultureInfo.InvariantCulture)}"));
    }

    /// <summary>Plays the game under a <see cref="Genome"/>: it makes the strategically loaded choices
    /// (mulligan, attack target, counter/block spending) from its weights, and delegates the mechanical
    /// rest (which card to play, DON, effect resolution) to the baseline. The engine remains the sole
    /// authority on legality (§1.2) — weights only choose among legal options.</summary>
    public sealed class WeightedAgent : IAgent
    {
        private readonly Genome _g;
        private readonly IAgent _base = new BaselineAgent();
        public string Name { get; }

        public WeightedAgent(Genome g, string name = "weighted") { _g = g; Name = name; }

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            var me = state.Players[seat];

            // --- turn order: choose first/second from the (learned) gene when we win the flip ---
            if (state.Status == "coinflip")
            {
                if (state.CoinFlipWinner != seat) return null;
                return new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = _g.TurnOrderFirst >= 0.5 };
            }

            // --- mulligan: keep only hands with enough early plays ---
            if (state.Status == "mulligan")
            {
                if (me.MulliganDecided) return null;
                int early = me.Hand.Count(c => { var d = CardData.GetCard(c.CardId); return d != null && d.Type == "character" && d.Cost <= 2; });
                bool keep = early >= _g.MulliganKeep;
                return new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = !keep };
            }

            // --- defense: counter / block spending from weights ---
            if (state.Battle != null && state.Battle.TargetSeat == seat)
            {
                if (state.Battle.Step == "counter") return DecideCounter(state, seat, me);
                if (state.Battle.Step == "block") return DecideBlock(state, seat, me);
            }

            // --- otherwise: baseline move generation, but re-target attacks per FaceBias ---
            var cmd = _base.Decide(state, seat, blacklist);
            if (cmd != null && cmd.Type == "declareAttack")
                cmd.Target = ChooseAttackTarget(state, seat, cmd.Target);
            return cmd;
        }

        private string ChooseAttackTarget(GameState state, string seat, string plannedTarget)
        {
            var opp = state.Players[GameEngine.OtherSeat(seat)];
            if (_g.FaceBias >= 0.5 || opp.Leader == null) return opp.Leader?.InstanceId ?? plannedTarget;
            // prefer removing the weakest rested opponent Character; fall back to the leader.
            var restedChar = opp.CharacterArea.Where(c => c != null && c.Rested)
                .OrderBy(c => GameEngine.GetPower(state, c)).FirstOrDefault();
            return restedChar?.InstanceId ?? opp.Leader.InstanceId;
        }

        private GameCommand DecideCounter(GameState state, string seat, PlayerState me)
        {
            var b = state.Battle;
            int need = b.AttackPower - b.DefensePower + 1;
            if (need <= 0) return new GameCommand { Type = "passCounter", Seat = seat };

            bool targetIsLeader = b.TargetId == me.Leader?.InstanceId;
            var target = FindOwn(me, b.TargetId);
            bool worth = targetIsLeader
                ? me.Life.Count <= _g.CounterLifeFloor
                : (target != null && (CardData.GetCard(target.CardId)?.Cost ?? 0) >= _g.CounterCharCost);
            if (!worth) return new GameCommand { Type = "passCounter", Seat = seat };

            var usable = me.Hand.Where(c => GameEngine.CanCounterFromHand(state, seat, c))
                .Select(c => (card: c, cp: GameEngine.GetCounterPower(c), cost: CardData.GetCard(c.CardId)?.Cost ?? 0, type: CardData.GetCard(c.CardId)?.Type))
                .Where(x => x.cp > 0).OrderByDescending(x => x.cp).ToList();
            if (MaxAffordable(state, seat, usable) < need) return new GameCommand { Type = "passCounter", Seat = seat };

            var single = usable.Where(x => x.cp >= need).OrderBy(x => x.cp).ToList();
            var pick = single.Count > 0 ? single[0] : usable.FirstOrDefault();
            if (pick.card == null) return new GameCommand { Type = "passCounter", Seat = seat };
            return new GameCommand { Type = "counterWithCard", Seat = seat, InstanceId = pick.card.InstanceId };
        }

        private static int MaxAffordable(GameState state, string seat, List<(CardInstance card, int cp, int cost, string type)> usable)
        {
            int don = GameEngine.ActiveDonCount(state.Players[seat]); int total = 0;
            var events = new List<(int cp, int cost)>();
            foreach (var x in usable) { if (x.type == "event") events.Add((x.cp, x.cost)); else total += x.cp; }
            foreach (var e in events.OrderByDescending(x => x.cp)) if (e.cost <= don) { don -= e.cost; total += e.cp; }
            return total;
        }

        private GameCommand DecideBlock(GameState state, string seat, PlayerState me)
        {
            var b = state.Battle;
            var attacker = FindAny(state, b.AttackerSeat, b.AttackerId);
            int atkPow = attacker != null ? GameEngine.GetPower(state, attacker) : 0;
            var blockers = me.CharacterArea.Where(c => c != null && !c.Rested
                && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false)).ToList();
            if (blockers.Count == 0) return new GameCommand { Type = "passBlock", Seat = seat };

            bool lethal = b.TargetId == me.Leader?.InstanceId && me.Life.Count == 0;
            var survivors = blockers.Where(x => GameEngine.GetPower(state, x) > atkPow).OrderBy(x => GameEngine.GetPower(state, x)).ToList();
            // Block when it saves the leader from lethal, when life is critical, or when the weights say be defensive.
            bool shouldBlock = lethal || me.Life.Count <= 1 || (survivors.Count > 0 && _g.BlockBias >= 0.5);
            if (!shouldBlock) return new GameCommand { Type = "passBlock", Seat = seat };
            var chosen = survivors.FirstOrDefault() ?? blockers.OrderBy(x => GameEngine.GetPower(state, x)).First();
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
