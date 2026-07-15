using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Heuristics
{
    // ── policy.json schema (mirrors build_policy.py output) ─────────────────────
    public sealed class Policy
    {
        public Dictionary<string, FamilyPolicy> families { get; set; } = new();

        public static Policy Load(string path) =>
            JsonSerializer.Deserialize<Policy>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    public sealed class FamilyPolicy
    {
        public string family { get; set; }
        public string choiceA { get; set; }
        public string choiceB { get; set; }
        public OverallRule overall { get; set; }
        public List<Rule> rules { get; set; } = new();
    }
    public sealed class OverallRule { public string prefer { get; set; } public double effect_pp { get; set; } public long n { get; set; } }
    public sealed class Rule
    {
        public string feature { get; set; }
        public string value { get; set; }
        public string prefer { get; set; }
        public double effect_pp { get; set; }
        public string holdout { get; set; }
        public long n { get; set; }
    }

    /// <summary>
    /// The advanced-bot prototype: it does NOT hardcode leader rules — it loads a discovered
    /// <see cref="Policy"/> and, at each decision, computes the SAME generic features (its deck vs the
    /// opponent's, its opening hand) and looks up the preferred choice (blueprint §19). Everything not
    /// covered by a policy family falls through to the baseline. Both deck lists are known pre-match
    /// (§8.1), so matchup features are precomputed once.
    ///
    /// Applies the families that flow through the per-command seam: mulligan, counter-economy,
    /// aggression. (Turn-order needs the driver to hand the coin-flip choice to the agent — a small
    /// separate enhancement.)
    /// </summary>
    public sealed class PolicyAgent : IAgent
    {
        private readonly IAgent _fallback;
        private readonly Policy _policy;
        private readonly DeckDef _myDeck, _oppDeck;
        private readonly FeatureBag _matchup;
        private readonly bool _preferValidated;

        public string Name => "policy";

        public PolicyAgent(Policy policy, DeckDef myDeck, DeckDef oppDeck, IAgent fallback = null, bool preferValidated = true)
        {
            _policy = policy; _myDeck = myDeck; _oppDeck = oppDeck;
            _fallback = fallback ?? new BaselineAgent();
            _preferValidated = preferValidated;
            _matchup = Features.Matchup(myDeck, oppDeck);
        }

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            // Turn order — when this seat won the coin flip, choose first/second from the matchup
            // policy (§6). Only fires when the driver runs in AgentChoosesTurnOrder mode.
            if (state.Status == "coinflip")
            {
                if (state.CoinFlipWinner != seat) return null;
                bool goingFirst = Choose("turn-order", _matchup) != "second"; // default to first
                return new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = goingFirst };
            }

            // Mulligan — decide from opening-hand features.
            if (state.Status == "mulligan")
            {
                var p = state.Players[seat];
                if (!p.MulliganDecided)
                {
                    var choice = Choose("mulligan", Features.OpeningHand(state, seat));
                    if (choice != null)
                        return new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = choice == "mulligan" };
                }
                return _fallback.Decide(state, seat, blacklist);
            }

            // Counter economy — conserve (take the hit) or fall through to baseline defense.
            if (state.Battle != null && state.Battle.Step == "counter" && state.Battle.TargetSeat == seat)
            {
                if (Choose("counter-economy", _matchup) == "conserve")
                    return new GameCommand { Type = "passCounter", Seat = seat };
            }

            var cmd = _fallback.Decide(state, seat, blacklist);

            // Aggression — redirect declared attacks at the opponent leader when the policy says so.
            if (cmd != null && cmd.Type == "declareAttack" && Choose("aggression", _matchup) == "face-rush")
            {
                var ol = state.Players[GameEngine.OtherSeat(seat)].Leader;
                if (ol != null) cmd.Target = ol.InstanceId;
            }
            return cmd;
        }

        /// <summary>Look up the preferred choice for a family given current features: the highest-|effect|
        /// rule whose condition the features satisfy (preferring holdout-validated rules), else the
        /// family's overall recommendation, else null (no guidance ⇒ let the baseline decide).</summary>
        private string Choose(string family, FeatureBag feats)
        {
            if (_policy?.families == null || !_policy.families.TryGetValue(family, out var fp) || fp == null) return null;
            Rule best = null;
            foreach (var r in fp.rules ?? Enumerable.Empty<Rule>())
            {
                bool match;
                if (r.feature == "matchup")
                    match = r.value == $"{_myDeck.Id} vs {_oppDeck.Id}";
                else
                    match = feats.TryGetValue(r.feature, out var v) && FeatureBins.Bin(r.feature, v) == r.value;
                if (!match) continue;
                if (_preferValidated && r.holdout != null && r.holdout != "validated") continue;
                if (best == null || Math.Abs(r.effect_pp) > Math.Abs(best.effect_pp)) best = r;
            }
            return best?.prefer ?? fp.overall?.prefer;
        }
    }
}
