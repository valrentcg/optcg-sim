using System;
using System.Collections.Generic;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Heuristics
{
    /// <summary>An IAgent that forces the test seat's mulligan decision (keep vs mulligan) and captures
    /// the pre-decision opening-hand features, delegating everything else to the baseline. This is how
    /// a counterfactual isolates ONE decision on a matched seed: both variants share the same opening
    /// hand, coin flip, and deck order — only the forced choice differs.</summary>
    public sealed class MulliganController : IAgent
    {
        private readonly IAgent _inner;
        private readonly string _testSeat;
        private readonly bool _forceKeep;
        private readonly Action<FeatureBag> _capture;
        private bool _captured;

        public MulliganController(IAgent inner, string testSeat, bool forceKeep, Action<FeatureBag> capture)
        {
            _inner = inner; _testSeat = testSeat; _forceKeep = forceKeep; _capture = capture;
        }

        public string Name => _inner.Name;

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (seat == _testSeat && state.Status == "mulligan")
            {
                var p = state.Players[seat];
                if (p.MulliganDecided) return null;
                if (!_captured) { _capture?.Invoke(Features.OpeningHand(state, seat)); _captured = true; }
                return new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = !_forceKeep };
            }
            return _inner.Decide(state, seat, blacklist);
        }
    }

    /// <summary>Forces the test seat to CONSERVE its counter cards (always passCounter — take the
    /// hit) while delegating every other decision to the baseline. Paired against plain baseline
    /// defense, this measures the whole-game value of spending counters vs hoarding them — the §7
    /// counter-economy axis that distinguishes an aggressive resource-dump from a conservative,
    /// control style. Counter-only (blocking is a separate resource), for a clean axis.</summary>
    public sealed class CounterController : IAgent
    {
        private readonly IAgent _inner;
        private readonly string _testSeat;

        public CounterController(IAgent inner, string testSeat) { _inner = inner; _testSeat = testSeat; }
        public string Name => _inner.Name;

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (seat == _testSeat && state.Battle != null
                && state.Battle.Step == "counter" && state.Battle.TargetSeat == seat)
                return new GameCommand { Type = "passCounter", Seat = seat };
            return _inner.Decide(state, seat, blacklist);
        }
    }

    /// <summary>Redirects every attack the test seat declares onto the opponent's LEADER (pure
    /// life-race / face aggression), delegating everything else to the baseline. Attacking the
    /// leader is always legal, so this never stalls. Paired against the baseline's own target choice,
    /// it isolates the aggressive "go face" playstyle from mixed board/face play — the offensive axis
    /// the discovery is meant to explore, deck-agnostically.</summary>
    public sealed class AttackTargetController : IAgent
    {
        private readonly IAgent _inner;
        private readonly string _testSeat;

        public AttackTargetController(IAgent inner, string testSeat) { _inner = inner; _testSeat = testSeat; }
        public string Name => _inner.Name;

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            var cmd = _inner.Decide(state, seat, blacklist);
            if (cmd != null && cmd.Type == "declareAttack" && seat == _testSeat)
            {
                var oppLeader = state.Players[GameEngine.OtherSeat(seat)].Leader;
                if (oppLeader != null) cmd.Target = oppLeader.InstanceId; // always a legal target
            }
            return cmd;
        }
    }

    /// <summary>One side of a counterfactual A/B: a label, which seat is forced to start, and how to
    /// wrap the south (test) agent. <c>WrapSouth</c> may also capture in-game pre-decision features.</summary>
    public sealed class Variant
    {
        public string Label;
        public string FirstSeat;   // forced starting seat; null ⇒ "south"
        public Func<IAgent, Action<FeatureBag>, IAgent> WrapSouth;
    }

    /// <summary>A decision to A/B test. Effect measured is P(south wins | ChoiceA) − P(south wins |
    /// ChoiceB), over matched seeds, bucketed by <see cref="ReportFeatures"/> to yield conditional
    /// heuristics in the §12.3 format.</summary>
    public sealed class DecisionFamily
    {
        public string Name;
        public string Question;
        public string ChoiceA, ChoiceB;
        public List<Variant> Variants;     // exactly two: [A, B]
        public List<string> ReportFeatures;
        public bool UsesMatchupFeatures;   // true ⇒ runner seeds features from the deck matchup

        // ── the two built-in families ───────────────────────────────────────────

        /// <summary>Mulligan: keep the opening hand vs mulligan it (§5.4). Conditioned on hand shape.</summary>
        public static DecisionFamily Mulligan() => new DecisionFamily
        {
            Name = "mulligan",
            Question = "Should south KEEP its opening hand (vs mulligan)?",
            ChoiceA = "keep", ChoiceB = "mulligan",
            UsesMatchupFeatures = false,
            Variants = new List<Variant>
            {
                new Variant { Label = "keep",     FirstSeat = null,
                    WrapSouth = (inner, cap) => new MulliganController(inner, "south", forceKeep: true,  capture: cap) },
                new Variant { Label = "mulligan", FirstSeat = null,
                    WrapSouth = (inner, cap) => new MulliganController(inner, "south", forceKeep: false, capture: cap) },
            },
            ReportFeatures = new List<string>
            {
                "hand_has_searcher", "hand_has_t1_play", "hand_curve_ok", "hand_flooded",
                "hand_cost01", "hand_cost2", "hand_counters", "hand_maxdup", "hand_oncolor",
            },
        };

        /// <summary>Turn order: south goes first vs second (§6). Conditioned on the matchup profile.
        /// No agent wrapping needed — the driver forces the starting seat.</summary>
        public static DecisionFamily TurnOrder() => new DecisionFamily
        {
            Name = "turn-order",
            Question = "Should south choose to go FIRST (vs second)?",
            ChoiceA = "first", ChoiceB = "second",
            UsesMatchupFeatures = true,
            Variants = new List<Variant>
            {
                new Variant { Label = "first",  FirstSeat = "south", WrapSouth = (inner, cap) => inner },
                new Variant { Label = "second", FirstSeat = "north", WrapSouth = (inner, cap) => inner },
            },
            ReportFeatures = new List<string>
            {
                "i_am_faster", "my_low_curve", "opp_low_curve", "my_leader_life", "opp_leader_life",
            },
        };

        /// <summary>Counter economy: conserve counter cards (take hits) vs defend with them (§7).
        /// Conditioned on the matchup — the useful discovery is *when* hoarding counters is fine
        /// (e.g. vs slow decks) and when spending is mandatory (vs fast decks).</summary>
        public static DecisionFamily CounterEconomy() => new DecisionFamily
        {
            Name = "counter-economy",
            Question = "Should south CONSERVE its counter cards (vs defending with them)?",
            ChoiceA = "conserve", ChoiceB = "defend",
            UsesMatchupFeatures = true,
            Variants = new List<Variant>
            {
                new Variant { Label = "conserve", FirstSeat = "south", WrapSouth = (inner, cap) => new CounterController(inner, "south") },
                new Variant { Label = "defend",   FirstSeat = "south", WrapSouth = (inner, cap) => inner },
            },
            ReportFeatures = new List<string>
            {
                "i_am_faster", "opp_low_curve", "opp_leader_life", "opp_events",
                "my_counter_density", "i_have_more_counters", "opp_many_blockers",
            },
        };

        /// <summary>Aggression: send every attack at the opponent's leader (life-race) vs the
        /// baseline's mixed targeting. Conditioned on the matchup — reveals when going face wins
        /// (fast decks, low-life opponents) vs when board control is better.</summary>
        public static DecisionFamily Aggression() => new DecisionFamily
        {
            Name = "aggression",
            Question = "Should south send every attack at the opponent LEADER (pure life-race)?",
            ChoiceA = "face-rush", ChoiceB = "baseline-target",
            UsesMatchupFeatures = true,
            Variants = new List<Variant>
            {
                new Variant { Label = "face-rush",       FirstSeat = "south", WrapSouth = (inner, cap) => new AttackTargetController(inner, "south") },
                new Variant { Label = "baseline-target", FirstSeat = "south", WrapSouth = (inner, cap) => inner },
            },
            ReportFeatures = new List<string>
            {
                "i_am_faster", "opp_leader_life", "opp_low_curve",
                "opp_blockers", "opp_many_blockers", "opp_counter_density",
            },
        };

        public static DecisionFamily ByName(string name) => name switch
        {
            "mulligan" => Mulligan(),
            "turn-order" or "turnorder" or "first-second" => TurnOrder(),
            "counter-economy" or "counter" => CounterEconomy(),
            "aggression" or "face" => Aggression(),
            _ => throw new ArgumentException($"Unknown decision family '{name}'. Known: mulligan, turn-order, counter-economy, aggression."),
        };
    }
}
