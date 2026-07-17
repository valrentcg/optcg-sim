using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// THE ENFORCED BOUNDARY, runner-side. The ONLY place holding both the referee <see cref="GameState"/>
    /// and the opaque tokens an <see cref="IObservedAgent"/> chooses among. It:
    ///   1. projects the acting seat's <see cref="PlayerObservation"/> (and the runner-private
    ///      InstanceId→surrogate map),
    ///   2. enumerates the seat's legal <see cref="GameCommand"/>s and VALIDATES them against the engine so
    ///      only state-changing (actually legal) actions are offered,
    ///   3. mints a DETERMINISTIC per-decision-unique token per action and keeps the token→command map HERE,
    ///   4. builds a FAITHFUL descriptor (surrogate-linked actor/target, choice option, deck-look order,
    ///      pending prompt/source) so duplicate copies and options are distinguishable,
    ///   5. translates the agent's chosen token back to the authoritative command, FAILING CLOSED on any
    ///      token it did not mint this decision.
    /// </summary>
    public static class ObservedSeam
    {
        public sealed class Decision
        {
            public PlayerDecisionContext Context { get; }
            private readonly Dictionary<string, GameCommand> _map;

            internal Decision(PlayerDecisionContext context, Dictionary<string, GameCommand> map)
            {
                Context = context;
                _map = map;
            }

            /// <summary>Translate the agent's choice back to an authoritative command. FAILS CLOSED: a null
            /// choice, a null token, or a token this decision did not mint (stale/forged) yields null.</summary>
            public GameCommand Translate(ObservedAction chosen)
            {
                if (chosen?.Token == null) return null;
                return _map.TryGetValue(chosen.Token, out var cmd) ? cmd : null;
            }

            public int ActionCount => _map.Count;
        }

        /// <summary>Build the decision for <paramref name="seat"/>. The runner supplies the knowledge (it owns
        /// the decklists + assumption) and the reproducible coordinates (match seed + decision sequence).</summary>
        public static Decision Build(GameState state, string seat, KnowledgeState knowledge,
            string matchSeed, int decisionSequence, int seedBasis)
        {
            var observation = Projection.Project(state, knowledge, out var links);
            var commands = EnumerateLegal(state, seat);

            var map = new Dictionary<string, GameCommand>(commands.Count);
            var actions = new List<ObservedAction>(commands.Count);
            for (int i = 0; i < commands.Count; i++)
            {
                var cmd = commands[i];
                // Token = the decision sequence and ordinal in CLEARTEXT (outside the hash) + a deterministic
                // 128-bit hash of the full coordinates. The cleartext prefix makes stale rejection STRUCTURAL:
                // two different decision sequences produce literally different token strings, so a token from
                // another decision is guaranteed absent from this map — not merely collision-improbable. The
                // hash binds the token to (match seed, seat) and adds opacity; it is deterministic, so
                // paired/seeded runs reproduce identical tokens.
                string token = decisionSequence + ":" + i + ":" + DerivedSeed.Token(matchSeed, seat, decisionSequence, i);
                map[token] = cmd;
                actions.Add(Describe(state, seat, token, cmd, links));
            }

            var ctx = new PlayerDecisionContext
            {
                Observation = observation,
                Actions = actions.AsReadOnly(),
                DecisionSequence = decisionSequence,
                SeedBasis = seedBasis,
            };
            return new Decision(ctx, map);
        }

        /// <summary>Legal, VALIDATED commands for the seat. Coin flip and mulligan are legal by construction
        /// (the two allowed options) and enumerated directly. Everything else goes through
        /// <see cref="LegalActions.Candidates"/> — a documented SUPERSET — and is then filtered by
        /// <see cref="LegalActions.Validate"/>, which clones+applies each and keeps only the ones that
        /// actually change authoritative state. So an offered action is a real, state-changing legal move,
        /// not a plausible candidate.</summary>
        private static List<GameCommand> EnumerateLegal(GameState state, string seat)
        {
            if (state == null || !state.Players.TryGetValue(seat, out var p)) return new List<GameCommand>();

            if (state.Status == "coinflip")
                return state.CoinFlipWinner == seat
                    ? new List<GameCommand>
                        {
                            new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = true },
                            new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = false },
                        }
                    : new List<GameCommand>();

            if (state.Status == "mulligan")
                return p.MulliganDecided
                    ? new List<GameCommand>()
                    : new List<GameCommand>
                        {
                            new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = false },
                            new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = true },
                        };

            // Superset → engine-validated legal set.
            return LegalActions.Validate(state, seat, LegalActions.Candidates(state, seat))
                .Select(v => v.cmd).ToList();
        }

        /// <summary>Build the FAITHFUL, sanitized descriptor for one command. Card references resolve to the
        /// exact SURROGATE the observation used (so duplicates are distinguishable) and to a CardId only when
        /// the seat may legally identify it; the true InstanceId is never surfaced. Options, deck-look order,
        /// and the pending prompt/source are included so distinct actions have distinct descriptors.</summary>
        private static ObservedAction Describe(GameState state, string seat, string token, GameCommand cmd,
            ObservationLinks links)
        {
            // The actor lives in whichever field this command TYPE uses; resolve it per-type so the surrogate
            // maps back to the right field on reconstruction (declareAttack ⇒ Attacker, blockAttack ⇒ Blocker,
            // else ⇒ InstanceId). resolveChoice's Target is an option letter, not a card.
            string actorInstance = cmd.Type == "declareAttack" ? cmd.Attacker
                                 : cmd.Type == "blockAttack" ? cmd.Blocker
                                 : cmd.Type == "activateMain" ? cmd.Target
                                 : cmd.InstanceId;
            string targetInstance = cmd.Type == "resolveChoice" || cmd.Type == "activateMain" ? null : cmd.Target;
            string Sur(string id) => id != null && links.InstanceIdToSurrogate.TryGetValue(id, out var s) ? s : null;

            string pendingRef = null, promptText = null, sourceCardId = null;
            if (cmd.EffectId != null && links.EffectIdToPendingRef.TryGetValue(cmd.EffectId, out var pr))
            {
                pendingRef = pr;
                var e = state.PendingEffects.FirstOrDefault(x => x.Seat == seat && x.EffectId == cmd.EffectId);
                promptText = e?.Text; sourceCardId = e?.SourceCardId;
            }

            IReadOnlyList<string> ordered = cmd.OrderedInstanceIds?.Select(Sur).ToList();

            return new ObservedAction
            {
                Token = token,
                Type = cmd.Type,
                ActorCardId = IdentifyForSeat(state, seat, actorInstance),
                ActorSurrogate = Sur(actorInstance),
                TargetCardId = IdentifyForSeat(state, seat, targetInstance),
                TargetSurrogate = Sur(targetInstance),
                TargetZone = ZoneHint(cmd),
                ChoiceOption = cmd.Type == "resolveChoice" ? cmd.Target : null,
                OrderedSurrogates = ordered,
                PendingRef = pendingRef,
                PromptText = promptText,
                SourceCardId = sourceCardId,
                GoingFirst = cmd.Type == "chooseTurnOrder" ? cmd.GoingFirst : null,
                Mulligan = cmd.Type == "mulliganDecision" ? cmd.Mulligan : null,
                Amount = cmd.Amount,
            };
        }

        private static string ZoneHint(GameCommand cmd) => cmd.Type switch
        {
            "counterWithCard" => "hand",
            "blockAttack" => "character",
            "resolveChoice" => "choice",
            "deckLookSelect" => "deckLook",
            _ => null,
        };

        /// <summary>Return the CardId of <paramref name="instanceId"/> IF the seat may legally identify it —
        /// its own hand, either player's public board (leader / character / stage) and trash, its own face-up
        /// Life, and its own pending deck-look cards. Returns null otherwise, so a hidden card is never named
        /// and a non-card token (choice "A"/"B", decline "") resolves to null. Never scans a hidden zone.</summary>
        private static string IdentifyForSeat(GameState state, string seat, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;

            foreach (var kv in state.Players)
            {
                var p = kv.Value;
                bool own = kv.Key == seat;

                if (p.Leader != null && p.Leader.InstanceId == instanceId) return p.Leader.CardId;
                if (p.Stage != null && p.Stage.InstanceId == instanceId) return p.Stage.CardId;
                var ch = p.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == instanceId);
                if (ch != null) return ch.CardId;
                var tr = p.Trash.FirstOrDefault(c => c.InstanceId == instanceId);
                if (tr != null) return tr.CardId;

                if (own)
                {
                    var h = p.Hand.FirstOrDefault(c => c.InstanceId == instanceId);
                    if (h != null) return h.CardId;
                    var fl = p.Life.FirstOrDefault(c => c.FaceUp && c.InstanceId == instanceId);
                    if (fl != null) return fl.CardId;
                }
            }

            if (state.DeckLook != null && state.DeckLook.Seat == seat)
            {
                var d = state.DeckLook.Cards.FirstOrDefault(c => c.InstanceId == instanceId);
                if (d != null) return d.CardId;
            }
            return null;
        }
    }
}
