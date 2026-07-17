using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Sim.Expert;
using OnePieceTcg.Sim.Search;
using OnePieceTcg.Sim.Runner;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// THE HONEST PLANNER (K=1). On its own turn it DETERMINIZES the position — samples the hidden zones from
    /// the legal decklist pool (<see cref="RootWorldSampler"/>), discarding the referee's true hidden
    /// assignment — and searches THAT sampled world. Because the determinizer preserves own/public instance
    /// ids and only rewrites hidden cards, the plan it finds references the real cards and applies directly.
    /// Reactive decisions (defence, effect resolution) still go through <see cref="ValuePolicy"/> on a
    /// determinized world, so they too never read the true hidden cards.
    ///
    /// ⚠ ENFORCEMENT NOTE: this is an <see cref="ILegacyAgent"/> — it RECEIVES the referee <see cref="GameState"/>
    /// (the runner-side determinizer needs the runnable structure) and is honest by CONSTRUCTION, not yet by
    /// type. Folding it under the strict <see cref="IObservedAgent"/> seam (runner-side search so the policy
    /// never receives any GameState) is the remaining enforcement step. It never SEARCHES the true hidden
    /// assignment, which is the property privacy-test's Layer 2 measures.
    ///
    /// ⚠ K=1 + pre-ledger (design §2/§6): one sampled world, uniform over the unseen pool ⇒ it does not yet
    /// respect legally-acquired knowledge, and it is fair-information-but-strategy-fused. Requires OpenList.
    /// </summary>
    public sealed class HonestPlannerBot : ILegacyAgent, ICommandObserver
    {
        /// <summary>A/B research toggle for the dedicated battle-Trigger evaluator. When true (default and
        /// shipped behaviour) the Trigger step is decided by <see cref="OnePieceTcg.Engine.Bot.Search.TriggerUtilityPolicy"/>;
        /// when false the honest path falls back to the generic K-world rollout for that step, so identical
        /// seeds can be replayed both ways to isolate the policy's effect. Set once per arm (never mid-run —
        /// it is read concurrently by the Parallel.For workers).</summary>
        public static bool TriggerPolicyEnabled = true;

        private readonly double[] _w;
        private readonly DeckContext _ctx;
        private readonly TurnPlanner.Options _opt;
        private readonly bool _goFirst;
        private readonly DeckDef _ownList, _oppList;
        private readonly KnowledgeLedger _ledger = new KnowledgeLedger();

        private int _decision;
        private string _seat;   // this bot's seat, learned on first Decide; scopes the ledger to its own deck
        private readonly int _kWorlds;
        private readonly bool _useGreedyPolicy;
        private readonly bool _useAdvancedPolicy;
        private readonly string _advancedScope;
        private readonly ExpertPolicyModel _expertModel;
        private readonly HashSet<string> _advancedActivatedThisTurn = new HashSet<string>();
        private int _advancedActivationTurn = -1;

        /// <summary>The runner calls this after every applied command so the ledger can track the legally-known
        /// top of THIS seat's own deck (gained from its own to-top deck looks, invalidated on shuffle/draw).</summary>
        public void ObserveApplied(GameCommand cmd, GameState before, GameState after)
        { if (_seat != null) _ledger.Observe(cmd, before, after, _seat); }

        public string Name { get; }

        public HonestPlannerBot(DeckDef ownList, DeckDef oppList, double[] weights = null, DeckContext ctx = null,
            TurnPlanner.Options opt = null, bool goFirst = true, string name = "honest", int kWorlds = 1,
            bool? useGreedyPolicy = null, bool useAdvancedPolicy = false, string advancedScope = "all",
            ExpertPolicyModel expertModel = null)
        {
            _ownList = ownList; _oppList = oppList;
            _ctx = ctx ?? DeckContext.Generic;
            _w = ValueFunction.ApplyArchetype(weights ?? ValueFunction.DefaultWeights(), _ctx.Archetype);
            _opt = opt ?? new TurnPlanner.Options();
            _goFirst = goFirst;
            _kWorlds = System.Math.Max(1, kWorlds);
            // First proven deck-identity route: the low-budget search evaluator had no usable local gradient.
            // Direct honest A/Bs favored the determinized rule policy in every currently recognized identity
            // (aggro, midrange, control, combo) on untouched field seeds on 2026-07-16. Nullable keeps experiments
            // able to force either arm; production callers use the evidence-backed route automatically. Unknown
            // future identities stay on search until they are measured instead of being silently generalized.
            _useGreedyPolicy = useGreedyPolicy ?? HasProvenPolicyRoute(_ctx.Archetype);
            _useAdvancedPolicy = useAdvancedPolicy;
            _advancedScope = advancedScope ?? "all";
            _expertModel = expertModel;
            Name = name;
        }

        private static bool HasProvenPolicyRoute(string archetype) =>
            string.Equals(archetype, "aggro", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(archetype, "midrange", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(archetype, "control", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(archetype, "combo", System.StringComparison.OrdinalIgnoreCase);

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;
            _seat = seat;   // scope the ledger to this bot's own deck

            if (state.Status == "coinflip")
                return state.CoinFlipWinner == seat
                    ? new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = _goFirst }
                    : null;

            if (state.Status == "mulligan")
            {
                var p = state.Players[seat];
                if (p.MulliganDecided) return null;
                int early = p.Hand.Count(c => { var d = CardData.GetCard(c.CardId); return d != null && d.Type == "character" && d.Cost <= 2; });
                return new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = !(early >= 1) };
            }

            // Own turn: REPLAN EVERY COMMAND (receding horizon). A turn plan is computed on a determinized
            // world whose hidden zones (incl. the deck) are RESAMPLED; committing more than its FIRST command
            // is unsafe, because a later step may resolve a look/search over FAKE deck cards whose ids do not
            // exist in the real state (it would no-op and desync the turn). The first command only ever
            // references cards this seat can act on now — own hand/board, an active deck look — whose ids the
            // determinizer PRESERVES, so it applies to the real state. We re-determinize and re-plan after it.
            int decision = _decision++;   // one stable index per decision point; worlds vary within it

            // Experimental archetype-policy route. The greedy policy is never allowed to inspect the
            // referee state: it receives only a freshly sampled legal world. Its commands reference cards
            // this seat may act on (own/private-visible cards or public board cards), whose instance ids the
            // determinizer preserves, so the command applies directly to the authoritative state. This is a
            // policy-family experiment, not a perfect-information fallback.
            if (_useAdvancedPolicy || _useGreedyPolicy)
            {
                bool cleanMain = state.ActiveSeat == seat && state.Phase == "main" && state.Battle == null
                    && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null;
                bool resolutionDecision = state.ActiveChoice?.Seat == seat
                    || state.DeckLook?.Seat == seat
                    || state.PendingEffects.Any(e => e.Seat == seat)
                    || (state.Battle != null && state.Battle.TargetSeat == seat && state.Battle.Step == "trigger");
                bool defenseDecision = state.Battle != null && state.Battle.TargetSeat == seat
                    && (state.Battle.Step == "block" || state.Battle.Step == "counter");
                bool tacticalDecision = _advancedScope == "resolution" ? resolutionDecision
                    : _advancedScope == "defense" ? defenseDecision
                    : resolutionDecision || defenseDecision;
                bool pressureEnabled = _advancedScope == "pressure"
                    || _advancedScope == "pressure-resolution"
                    || ((_advancedScope == "contract-v1" || _advancedScope == "contract-v2")
                        && _ctx.Archetype == "control");
                bool activationEnabled = _advancedScope == "activation"
                    || ((_advancedScope == "contract-v2" || _advancedScope == "expert-v1")
                        && (_ctx.Archetype == "midrange" || _ctx.RequiresMainActivation));
                bool expertEnabled = _advancedScope == "expert-v1" && _expertModel != null;
                if (_advancedActivationTurn != state.TurnNumber)
                {
                    _advancedActivationTurn = state.TurnNumber;
                    _advancedActivatedThisTurn.Clear();
                }
                if (_useAdvancedPolicy && cleanMain && activationEnabled)
                {
                    var activationWorld = Determinize(state, seat, decision, 0).State;
                    return AdvancedActivationPolicy.Decide(
                        activationWorld, seat, blacklist, _advancedActivatedThisTurn);
                }
                if (_useAdvancedPolicy && cleanMain && expertEnabled)
                {
                    var expertWorld = Determinize(state, seat, decision, 0).State;
                    return ExpertReplayPolicy.Decide(expertWorld, seat, blacklist, _expertModel);
                }
                if (_useAdvancedPolicy && cleanMain && pressureEnabled)
                {
                    var pressureWorld = Determinize(state, seat, decision, 0).State;
                    return AdvancedPressurePolicy.Decide(pressureWorld, seat, blacklist);
                }
                if (_advancedScope == "pressure") tacticalDecision = false;
                if (_advancedScope == "pressure-resolution" || _advancedScope == "contract-v1"
                    || _advancedScope == "contract-v2" || _advancedScope == "expert-v1")
                    tacticalDecision = resolutionDecision;
                // The universal clean-main rollout was rejected on independent validation (19/40 vs
                // greedy 20/40). Keep the proven greedy turn policy and apply experimental improvement only
                // at bounded tactical branches where the incumbent has explicit blind spots: choice A/B,
                // deck-look ordering, effect targets, block/counter, and Trigger use/pass.
                // Battle Trigger step: use the dedicated "resolve now vs. take into hand" evaluator instead
                // of the full-game rollout, which washes out a Trigger's marginal (often defensive) value.
                // It is observation-safe, but run it on a determinized world anyway so this path never reads
                // the referee state, and the use/pass command carries no card id so it maps straight back.
                bool triggerStep = state.Battle != null && state.Battle.TargetSeat == seat
                    && state.Battle.Step == "trigger";
                if (_useAdvancedPolicy && tacticalDecision && triggerStep && TriggerPolicyEnabled)
                {
                    var triggerWorld = Determinize(state, seat, decision, 0).State;
                    bool use = OnePieceTcg.Engine.Bot.Search.TriggerUtilityPolicy.ShouldUse(triggerWorld, seat);
                    return new GameCommand { Type = use ? "useTrigger" : "passTrigger", Seat = seat };
                }
                if (_useAdvancedPolicy && tacticalDecision)
                {
                    int count = System.Math.Max(4, _kWorlds);
                    var worlds = new List<GameState>(count);
                    for (int w = 0; w < count; w++) worlds.Add(Determinize(state, seat, decision, w).State);
                    return AdvancedRolloutPolicy.Decide(worlds, seat, blacklist, _w, _ctx);
                }
                var honestWorld = Determinize(state, seat, decision, 0).State;
                return IntermediateBot.DecideOneCommand(honestWorld, seat, blacklist);
            }

            if (TurnPlanner.IsMyDecision(state, seat) && state.ActiveSeat == seat)
            {
                bool cleanMain = state.Phase == "main" && state.Battle == null
                    && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null;
                if (cleanMain)
                    return PlanKWorld(state, seat, decision);
                // A pending own-turn decision (deck look / choice / effect target): resolve it on a
                // determinized world so it never reads the truth; the chosen command references the active
                // decision's preserved-id cards, so it applies to the real state. (K=1 — reactive stays single.)
                return ValuePolicy.Decide(Determinize(state, seat, decision, 0).State, seat, _w, _ctx);
            }

            // Defence / opponent-turn responses: decide on a determinized world, then map the chosen command
            // back to the real state. The determinizer preserves the ids of cards this seat can act on (own
            // hand/board, public board), so a defensive command (block/counter/pass) applies directly.
            return ValuePolicy.Decide(Determinize(state, seat, decision, 0).State, seat, _w, _ctx);
        }

        /// <summary>K-WORLD (voting). Search the FULL turn on each of K determinized worlds and VOTE on the
        /// first move: the move most worlds agree is best wins, which reduces K=1 strategy fusion (a move that
        /// is good across many sampled hands beats one that is great against a single false hand). The first
        /// move references own hand/board (ids the determinizer preserves), so its signature is comparable
        /// across worlds and it applies to the real state. K=1 reproduces the single-world path exactly.</summary>
        private GameCommand PlanKWorld(GameState state, string seat, int decision)
        {
            var firstMoves = new List<GameCommand>(_kWorlds);
            for (int w = 0; w < _kWorlds; w++)
            {
                var plan = TurnPlanner.PlanTurn(Determinize(state, seat, decision, w), _w, _ctx, _opt);
                firstMoves.Add(plan.Count > 0 ? plan[0] : new GameCommand { Type = "endTurn", Seat = seat });
            }
            return VoteFirstMove(firstMoves);
        }

        /// <summary>Return the move the most worlds chose as best; ties broken by earliest first-seen world
        /// (deterministic — never relies on dictionary ordering). This is where aggregation BITES: when worlds
        /// disagree, the plurality move wins, which can differ from world-0's move.</summary>
        public static GameCommand VoteFirstMove(IReadOnlyList<GameCommand> firstMoves)
        {
            var order = new List<string>();
            var byKey = new Dictionary<string, (GameCommand cmd, int votes)>();
            foreach (var move in firstMoves)
            {
                string key = Sig(move);
                if (byKey.TryGetValue(key, out var e)) byKey[key] = (e.cmd, e.votes + 1);
                else { byKey[key] = (move, 1); order.Add(key); }
            }
            string best = order[0];
            foreach (var key in order) if (byKey[key].votes > byKey[best].votes) best = key;
            return byKey[best].cmd;
        }

        // Every field that can distinguish two votable first moves — incl. SlotIndex / ordered / DON ids, so a
        // future play-to-slot or DON-carrying main action can never silently merge two distinct moves into one
        // vote key (reviewer P2 hardening).
        private static string Sig(GameCommand c) =>
            string.Join("|", c.Type, c.Seat, c.InstanceId, c.Target, c.Attacker, c.Blocker, c.EffectId, c.Amount, c.SlotIndex,
                c.OrderedInstanceIds == null ? "" : string.Join(",", c.OrderedInstanceIds),
                c.DonInstanceIds == null ? "" : string.Join(",", c.DonInstanceIds));

        private SearchWorld Determinize(GameState state, string seat, int decision, int worldIndex)
        {
            var knowledge = new KnowledgeState
            {
                Seat = seat, OwnList = _ownList, OpponentList = _oppList, Assumption = ListAssumption.OpenList,
                // Feed the ledger's legally-known deck-top so the determinizer PLACES those cards instead of
                // reshuffling them — the honest bot remembers what it legitimately searched.
                Segments = _ledger.Segments(seat),
            };
            int seed = DerivedSeed.For(state.Seed, seat, state.TurnNumber, decision, worldIndex);
            // buildLinks:false — this bot searches world.State via TurnPlanner and never translates a root
            // ObservedAction, so the surrogate/pendingRef maps are unused. Same world, cheaper per determinize.
            return RootWorldSampler.Determinize(state, knowledge, seed, buildLinks: false);
        }
    }
}
