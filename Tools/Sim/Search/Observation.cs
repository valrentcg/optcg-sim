using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Search
{
    // ============================================================================================
    // THE OBSERVATION BOUNDARY — production contracts (knowledge-state-design.md §1, §5, §6).
    //
    // Two DISTINCT safe types, because search needs both and they are not the same thing:
    //   • PlayerObservation — a DETACHED, READ-ONLY projection of what a seat may LEGALLY observe. Contains
    //     no GameState, CardInstance, GameCommand or LogEntry — nothing authoritative reachable. This is the
    //     type whose freedom from referee references IS the boundary. (Read-only under controlled
    //     construction; not a deeply-frozen graph — see PlayerObservation's doc-comment.)
    //   • SearchWorld       — the MUTABLE, fully-assigned world the search actually runs on. Built
    //     EITHER by determinizing an observation (honest) OR by wrapping a verbatim referee clone
    //     (the UNSAFE baseline). Its honest factory never accepts a referee GameState.
    //
    // ⚠ TWO PROPERTIES, NOT ONE. "No secret leaked" (secrecy) and "the seat sees exactly what it is
    // entitled to know" (epistemic fidelity) are SEPARATE. A projection can pass secrecy by OMITTING
    // information the seat legally holds — which is a different bug, not a success. This file now carries
    // the public/acting facts a legal decision needs (battle, DON, the defender-visible RevealedLife),
    // but it is still INCOMPLETE: the knowledge ledger (§2) and determinizer (§6) are gated on the §14
    // audit and NOT built, and the enforced observed-AGENT seam (so the planner cannot be handed a
    // GameState at all) is not built either. Nothing here enables honest mode.
    // ============================================================================================

    /// <summary>The decklist assumption behind an observation. <see cref="OpenList"/> is a GENEROUS
    /// information assumption for the phase-1 validity experiment (§8), NOT a strength bound and NOT
    /// deployable against humans. <see cref="UnknownList"/> is the phase-2 requirement.</summary>
    public enum ListAssumption { UnknownList, OpenList }

    /// <summary>What a seat LEGALLY knows, passed explicitly so a determinizer builds its unseen pool
    /// from THIS plus the public zones — never by reading the referee's true hidden assignment. This is a
    /// DETACHED SNAPSHOT: its fact collections are read-only, and its decklists are treated as immutable
    /// match configuration owned by the caller.
    ///
    /// ⚠ DELIBERATELY INCOMPLETE. The ledger-backed facts (<see cref="Counts"/>, <see cref="Segments"/>,
    /// <see cref="Groups"/>) are empty until the knowledge ledger (§2) lands; today an observation carries
    /// only the strictly-public facts plus the caller-supplied list assumption, which makes it a
    /// CONSERVATIVE (less-informed) view — safe, but not yet epistemically complete.</summary>
    public sealed class KnowledgeState
    {
        public string Seat { get; init; }
        public DeckDef OwnList { get; init; }
        public DeckDef OpponentList { get; init; }     // non-null ONLY under OpenList
        public ListAssumption Assumption { get; init; }

        // ---- ledger-backed legal knowledge (empty until §2; the ledger will supply populated lists) ----
        public IReadOnlyList<CountFact> Counts { get; init; } = Array.Empty<CountFact>();
        public IReadOnlyList<SegmentFact> Segments { get; init; } = Array.Empty<SegmentFact>();
        public IReadOnlyList<AmbiguityGroup> Groups { get; init; } = Array.Empty<AmbiguityGroup>();
    }

    /// <summary>"Zone Z of seat S contains AT LEAST N of CardId" — a lower bound a viewer legally holds
    /// (§2.2). Immutable value data only.</summary>
    public sealed class CountFact
    {
        public string Seat { get; init; }
        public string Zone { get; init; }
        public string CardId { get; init; }
        public int AtLeast { get; init; }
    }

    /// <summary>An ordered run of known CardIds anchored at Top(0) or Bottom(0) of a zone (§2.3).
    /// Immutable; positions are SEMANTIC anchors, never raw indexes.</summary>
    public sealed class SegmentFact
    {
        public string Seat { get; init; }
        public string Zone { get; init; }
        public string Anchor { get; init; }
        public IReadOnlyList<string> CardIds { get; init; } = Array.Empty<string>();
    }

    /// <summary>A grouped ambiguity constraint (§4.4): "at least <see cref="MinRemaining"/> of
    /// <see cref="Candidates"/> still sit in <see cref="Source"/>." Preserves correlations that blanket
    /// forgetting would destroy. Skeleton until §2.</summary>
    public sealed class AmbiguityGroup
    {
        public string Source { get; init; }
        public IReadOnlyList<string> Candidates { get; init; } = Array.Empty<string>();
        public int HiddenDepartures { get; init; }
        public int MinRemaining { get; init; }
    }

    /// <summary>A card as ONE seat may see it. Immutable value data that NEVER aliases a referee
    /// <see cref="CardInstance"/>. <see cref="CardId"/> is populated ONLY when the viewer may legally
    /// identify the card (public zones, own hand, face-up Life); it stays null for hidden cards, whose
    /// identity is therefore unrepresentable rather than merely omitted. <see cref="SurrogateId"/> is a
    /// per-decision opaque token, deliberately unrelated to the true InstanceId (§5.1).</summary>
    public sealed class ObservedCard
    {
        public string SurrogateId { get; init; }
        public string CardId { get; init; }                 // null ⇒ this seat may not identify the card
        public bool Rested { get; init; }
        public bool FaceUp { get; init; }
        public int AttachedDonCount { get; init; }
    }

    /// <summary>One zone as a seat sees it. <see cref="Count"/> (total cards) is always public. A hidden
    /// card contributes ONLY to that count: <see cref="Cards"/> carries a record ONLY for a card this seat
    /// may legally IDENTIFY (public zones, own hand, face-up Life). So <c>Cards.Count &lt;= Count</c>, and
    /// <c>Count - Cards.Count</c> is the number of hidden cards — with no per-card placeholder, flags, or
    /// metadata for them, since any of those would be information a viewer does not hold.</summary>
    public sealed class ObservedZone
    {
        public string Owner { get; init; }
        public string Zone { get; init; }
        public int Count { get; init; }
        public IReadOnlyList<ObservedCard> Cards { get; init; } = Array.Empty<ObservedCard>();
        /// <summary>Cards in this zone this seat cannot identify — count only, no records.</summary>
        public int HiddenCount => Count - Cards.Count;
    }

    /// <summary>A seat's DON!! resources — public information (both players see cost areas and DON deck
    /// counts). No hidden state, so identities are irrelevant; counts suffice.</summary>
    public sealed class ObservedDon
    {
        public string Owner { get; init; }
        public int Active { get; init; }        // ready DON!!
        public int Rested { get; init; }        // spent DON!!
        public int DeckRemaining { get; init; } // DON!! still in the DON deck
    }

    /// <summary>The current battle as ONE seat may see it. Public battle facts (step, powers, blocked,
    /// restrictions) are visible to both; the LIFE CARD IN FLIGHT (<see cref="BattleState.RevealedLife"/>)
    /// is visible ONLY to the seat legally entitled to it — the DEFENDER at the Trigger decision (CR
    /// §10-1-5, design §4.5). <see cref="RevealedLifeCardId"/> is null for anyone not so entitled, while
    /// <see cref="HasRevealedLife"/> stays true so the public FACT that a life card is in flight is not
    /// itself hidden — only its identity is.</summary>
    public sealed class ObservedBattle
    {
        public string Step { get; init; }
        public string PrioritySeat { get; init; }
        public string AttackerSeat { get; init; }
        public string TargetSeat { get; init; }
        public string AttackerCardId { get; init; }   // public board card — legal to identify
        public string TargetCardId { get; init; }      // public board card / leader — legal to identify
        public int AttackPower { get; init; }
        public int DefensePower { get; init; }
        public int CounterPower { get; init; }
        public bool Blocked { get; init; }
        public bool NoBlocker { get; init; }
        public int? BlockerPowerBan { get; init; }
        public bool HasRevealedLife { get; init; }
        public string RevealedLifeCardId { get; init; } // non-null ONLY for the entitled defender
    }

    /// <summary>A pending "choose A / B" as the ENTITLED seat sees it — the option texts and the source
    /// card, so the two options are distinguishable and meaningful. Only ever projected for the seat the
    /// choice belongs to.</summary>
    public sealed class ObservedChoice
    {
        public string SourceCardId { get; init; }
        public string OptionA { get; init; }
        public string OptionB { get; init; }
    }

    /// <summary>A pending deck look as the ENTITLED SEARCHER sees it (they are legally looking at these
    /// cards). Each card carries its observation surrogate so a select/ordering action can name it
    /// unambiguously. Never projected for anyone but the searcher.</summary>
    public sealed class ObservedDeckLook
    {
        public string SourceCardId { get; init; }
        public string Step { get; init; }
        public IReadOnlyList<ObservedCard> Cards { get; init; } = Array.Empty<ObservedCard>();
    }

    /// <summary>A pending effect awaiting the acting seat's decision — its legally-visible prompt text and
    /// source card, which the seat needs to choose a target. <see cref="Ref"/> is an OPAQUE per-decision
    /// handle (the pending-effect analogue of a card surrogate): the agent uses it to address this effect,
    /// while the authoritative EffectId stays runner/world-private.</summary>
    public sealed class ObservedPending
    {
        public string Ref { get; init; }
        public string SourceCardId { get; init; }
        public string PromptText { get; init; }
        public string Timing { get; init; }
    }

    /// <summary>Runner-PRIVATE correspondences between an observation's opaque handles and authoritative ids,
    /// in BOTH directions. Never handed to an agent. The seam uses the *ToHandle maps to build descriptors;
    /// a determinized world stores the *ToId maps so a root action's handles translate to THAT world's ids.</summary>
    public sealed class ObservationLinks
    {
        public IReadOnlyDictionary<string, string> InstanceIdToSurrogate { get; init; }
        public IReadOnlyDictionary<string, string> SurrogateToInstanceId { get; init; }
        public IReadOnlyDictionary<string, string> EffectIdToPendingRef { get; init; }
        public IReadOnlyDictionary<string, string> PendingRefToEffectId { get; init; }
    }

    /// <summary>THE LEGAL OBSERVATION — a DETACHED, READ-ONLY PROJECTION. Contains NO GameState,
    /// CardInstance, GameCommand or LogEntry — that absence is the boundary. A planner handed one of these
    /// CANNOT reach the referee truth, because there is no reference to reach it through.
    ///
    /// ⚠ SCOPE OF THE IMMUTABILITY CLAIM (do not overstate it). Every property is init-only and every
    /// collection built by <see cref="Projection"/> is a fresh <see cref="ReadOnlyCollection{T}"/> over
    /// freshly-built value records that never alias a referee object — so nothing HERE can be mutated to
    /// reach the truth. But this is NOT deep immutability: <see cref="Knowledge"/> is retained by reference,
    /// its <see cref="DeckDef"/> lists are mutable objects treated as immutable match CONFIG, and a
    /// caller-supplied <c>IReadOnlyList</c> is only as frozen as the caller made it. It is a read-only
    /// projection under controlled construction, not a deeply-frozen graph.
    ///
    /// ⚠ Search does NOT run on this type: it is (still) incomplete. Search runs on a
    /// <see cref="SearchWorld"/> minted FROM this observation.</summary>
    public sealed class PlayerObservation
    {
        public string Seat { get; init; }
        public string Status { get; init; }
        public string Phase { get; init; }
        public string ActiveSeat { get; init; }
        public int TurnNumber { get; init; }
        public KnowledgeState Knowledge { get; init; }
        public IReadOnlyList<ObservedZone> Zones { get; init; } = Array.Empty<ObservedZone>();
        public IReadOnlyList<ObservedDon> Don { get; init; } = Array.Empty<ObservedDon>();
        /// <summary>The current battle as this seat sees it, or null. Carries the defender-visible
        /// RevealedLife and public battle facts.</summary>
        public ObservedBattle Battle { get; init; }
        /// <summary>A pending A/B choice this seat must resolve, or null.</summary>
        public ObservedChoice Choice { get; init; }
        /// <summary>A pending deck look this seat is legally performing, or null.</summary>
        public ObservedDeckLook DeckLook { get; init; }
        /// <summary>This seat's pending effects awaiting a decision (empty if none).</summary>
        public IReadOnlyList<ObservedPending> Pending { get; init; } = Array.Empty<ObservedPending>();
        /// <summary>Log lines this seat may legally read: a line private to this seat by its full
        /// <c>Message</c>, any other private line by its <c>PublicMessage</c> substitute, and fully-public
        /// lines by their message. Named VisibleLog (not "public") precisely because a viewer's OWN private
        /// line is legally included — "public only" would be inaccurate.</summary>
        public IReadOnlyList<string> VisibleLog { get; init; } = Array.Empty<string>();
    }

    /// <summary>THE EXPLICIT BASELINE ADAPTER — the raw wrapper, honestly named. It holds the referee
    /// <see cref="GameState"/> verbatim, so anything holding one can read <see cref="Raw"/> and see every
    /// hidden card. This is the transitional wart the boundary exists to remove; it is NOT a boundary and
    /// NOT a <c>PlayerObservation</c>. Every historical win rate was measured through this path.</summary>
    public sealed class UnsafeLegacyPlannerView
    {
        public string Seat;
        public GameState Raw;
    }

    /// <summary>THE MUTABLE SAMPLED WORLD the search runs on. <see cref="State"/> is a complete
    /// <see cref="GameState"/>, but its PROVENANCE is the whole point:
    ///   • <see cref="FromObservation"/> (honest) mints a fresh world by determinizing a
    ///     <see cref="PlayerObservation"/> under a derived seed — no referee state in scope.
    ///   • <see cref="FromLegacy"/> (unsafe baseline) wraps the referee clone verbatim.
    /// The honest factory NEVER accepts a referee GameState; that signature is the enforcement.
    ///
    /// ⚠ SINGLE-SAMPLE ONLY. This mints ONE world. Honest search must aggregate root actions across K
    /// worlds (design §6/§9) or it is just K=1 perfect-information Monte Carlo with strategy fusion. The
    /// K-world batch/sampler + root aggregation is deliberately NOT built here yet.</summary>
    public sealed class SearchWorld
    {
        /// <summary>The mutable world the search clones and explores. Under the legacy path this ALIASES
        /// the referee state (safe only because <see cref="Planning.TurnPlanner"/> clones before mutating);
        /// under the honest path it is a freshly minted world that shares nothing with the referee.</summary>
        public GameState State { get; }
        public string Seat { get; }
        /// <summary>True ⇒ built by determinizing an observation (honest). False ⇒ verbatim referee clone
        /// (the cheating baseline). privacy-test reports this so a green run on a false world can never be
        /// mistaken for an honest one.</summary>
        public bool Determinized { get; }

        /// <summary>The determinizer's correspondence from the OBSERVATION's per-decision surrogate to THIS
        /// world's instance id — so a root <c>ObservedAction</c> (which names cards by surrogate) can be
        /// translated into an authoritative command IN THIS WORLD without guessing by CardId. Populated only
        /// on the honest <see cref="FromObservation"/> path (empty until the determinizer lands). This is
        /// what makes "evaluate the SAME root action across K worlds" exact rather than heuristic.</summary>
        public IReadOnlyDictionary<string, string> SurrogateToInstance { get; }
        /// <summary>The correspondence from an observation pending-effect ref to THIS world's EffectId — so a
        /// resolveEffect/passEffect root action addresses the right effect in this world. Determinizer-populated.</summary>
        public IReadOnlyDictionary<string, string> PendingRefToEffectId { get; }

        private SearchWorld(GameState state, string seat, bool determinized,
            IReadOnlyDictionary<string, string> surrogateToInstance = null,
            IReadOnlyDictionary<string, string> pendingRefToEffectId = null)
        {
            State = state; Seat = seat; Determinized = determinized;
            SurrogateToInstance = surrogateToInstance ?? new Dictionary<string, string>();
            PendingRefToEffectId = pendingRefToEffectId ?? new Dictionary<string, string>();
        }

        /// <summary>HONEST PATH. Sample a complete world consistent with the legal observation. Takes ONLY
        /// the immutable observation and a derived seed — a referee GameState is not in scope, so the
        /// sampled hidden cards CANNOT be copied from the truth; they must be derived.
        ///
        /// ⚠ NOT IMPLEMENTED. Determinize(PlayerObservation, seed) is gated on the §14 mutation-site audit
        /// and the knowledge ledger (design §6). This throws rather than silently falling back to the
        /// referee, because a silent fallback is exactly how a cheating world would masquerade as honest.</summary>
        public static SearchWorld FromObservation(PlayerObservation observation, int derivedSeed)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            throw new NotImplementedException(
                "Determinize(PlayerObservation, seed) is gated on the §14 audit + knowledge ledger " +
                "(knowledge-state-design.md §6). Honest mode is not enabled — see SearchWorld doc-comment.");
        }

        /// <summary>UNSAFE BASELINE. Wrap the referee state (by reference; TurnPlanner clones before it
        /// mutates, exactly as the pre-boundary code did, so this is bit-identical to today). This is the
        /// path with PERFECT information; it is named to keep its use auditable and must never be confused
        /// with <see cref="FromObservation"/>.</summary>
        public static SearchWorld FromLegacy(UnsafeLegacyPlannerView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return new SearchWorld(view.Raw, view.Seat, determinized: false);
        }

        /// <summary>HONEST, RUNNER-SIDE. Wrap a world the determinizer has already SAMPLED — its hidden zones
        /// hold cards drawn from the legal decklist pool, NOT the referee's true hidden assignment (which was
        /// discarded). This is the world the honest search runs on. The surrogate/pendingRef correspondences
        /// let a root <c>ObservedAction</c> translate into this world exactly. Built only by
        /// <c>RootWorldSampler.Determinize</c>, which owns the honesty argument for how the sample was drawn.</summary>
        public static SearchWorld FromDeterminized(GameState sampled, string seat,
            IReadOnlyDictionary<string, string> surrogateToInstance,
            IReadOnlyDictionary<string, string> pendingRefToEffectId)
        {
            if (sampled == null) throw new ArgumentNullException(nameof(sampled));
            return new SearchWorld(sampled, seat, determinized: true, surrogateToInstance, pendingRefToEffectId);
        }
    }

    /// <summary>A stable, cross-process derived seed. <c>string.GetHashCode()</c> is randomized per process
    /// (since .NET Core) and MUST NOT seed a reproducible experiment. This is a fixed FNV-1a over the
    /// decision coordinates, so the same (game seed, seat, turn, decision index, world index) always yields
    /// the same world — the determinism the K-world experiment depends on (design §6.3).</summary>
    public static class DerivedSeed
    {
        public static int For(string gameSeed, string seat, int turn, int decisionIndex, int worldIndex)
            => (int)Fnv64(2166136261UL, gameSeed, seat, turn, decisionIndex, worldIndex);

        /// <summary>A DETERMINISTIC 128-bit COLLISION-RESISTANT opaque hash for one action, from the full
        /// decision coordinates. Two independent 64-bit FNV-1a folds ⇒ within-run collision is negligible but
        /// not mathematically impossible, so this hash alone is "collision-resistant", not "structural". The
        /// seam therefore PREPENDS the decision sequence and ordinal in cleartext to the token, which makes
        /// cross-decision uniqueness (and thus stale-token rejection) structural. Deterministic, so
        /// paired/seeded runs reproduce identical tokens.</summary>
        public static string Token(string matchSeed, string seat, int decisionSequence, int ordinal)
        {
            ulong a = Fnv64(14695981039346656037UL, matchSeed, seat, decisionSequence, ordinal, 0x51);
            ulong b = Fnv64(1099511628211UL, seat, matchSeed, ordinal, decisionSequence, 0xA5);
            return a.ToString("x16") + b.ToString("x16");
        }

        private static ulong Fnv64(ulong offset, string s1, string s2, int n1, int n2, int n3)
        {
            unchecked
            {
                const ulong prime = 1099511628211UL;
                ulong h = offset;
                void Mix(string s) { if (s != null) foreach (char c in s) { h ^= c; h *= prime; } h ^= 0x1F; h *= prime; }
                void MixInt(int n) { for (int i = 0; i < 4; i++) { h ^= (byte)(n >> (i * 8)); h *= prime; } }
                Mix(s1); Mix(s2); MixInt(n1); MixInt(n2); MixInt(n3);
                return h;
            }
        }
    }

    /// <summary>THE SEAM: project a referee <see cref="GameState"/> down to what one seat may legally
    /// observe. Secrecy is enforced here (unknown CardIds and true InstanceIds never enter the result), and
    /// so is the START of epistemic fidelity (public/acting facts a legal decision needs — battle, DON, the
    /// defender-visible RevealedLife — ARE included).
    ///
    /// ⚠ INCOMPLETE by design. Without the knowledge ledger (§2) this does not yet reconstruct legally
    /// ACQUIRED knowledge (revealed opponent cards, known deck positions), and it does not yet emit
    /// per-decision ACTION descriptors (the opaque-token targeting the enforced agent seam needs). Those
    /// are staged, not done.</summary>
    public static class Projection
    {
        public static PlayerObservation Project(GameState state, KnowledgeState knowledge)
            => Project(state, knowledge, out _);

        /// <summary>Project, and also hand back the RUNNER-PRIVATE correspondences (<see cref="ObservationLinks"/>)
        /// between the observation's opaque handles (card surrogates, pending-effect refs) and the
        /// authoritative ids, in both directions. The seam uses them to tag legal actions with the exact
        /// surrogate/ref the agent sees — so duplicates and effects are addressable — WITHOUT ever giving the
        /// agent a true InstanceId or EffectId. These maps are never placed in the context handed to the agent.</summary>
        public static PlayerObservation Project(GameState state, KnowledgeState knowledge,
            out ObservationLinks links)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (knowledge == null) throw new ArgumentNullException(nameof(knowledge));
            string seat = knowledge.Seat;

            int surrogate = 0;
            string NextSurrogate() => "s#" + (surrogate++);
            var idToSurrogate = new Dictionary<string, string>();

            var zones = new List<ObservedZone>();
            var don = new List<ObservedDon>();

            foreach (var kv in state.Players.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var owner = kv.Key;
                var p = kv.Value;
                bool ownSeat = owner == seat;

                if (p.Leader != null)
                    zones.Add(Zone(owner, "leader", new[] { p.Leader }, c => true, NextSurrogate, idToSurrogate));
                zones.Add(Zone(owner, "character", p.CharacterArea.Where(c => c != null), c => true, NextSurrogate, idToSurrogate));
                if (p.Stage != null)
                    zones.Add(Zone(owner, "stage", new[] { p.Stage }, c => true, NextSurrogate, idToSurrogate));
                zones.Add(Zone(owner, "trash", p.Trash, c => true, NextSurrogate, idToSurrogate));

                // Hand — identified for its owner only; opponent sees COUNT only.
                zones.Add(Zone(owner, "hand", p.Hand, c => ownSeat, NextSurrogate, idToSurrogate));
                // Deck — order and identity hidden from everyone (legal top-card knowledge is a ledger fact).
                zones.Add(Zone(owner, "deck", p.Deck, c => false, NextSurrogate, idToSurrogate));
                // Life — facedown is secret to BOTH players incl. the owner (CR §3-10-2); only face-UP Life
                // (flipped by an effect) is identified.
                zones.Add(Zone(owner, "life", p.Life, c => c.FaceUp, NextSurrogate, idToSurrogate));

                don.Add(new ObservedDon
                {
                    Owner = owner,
                    Active = p.CostArea.Count(d => !d.Rested),
                    Rested = p.CostArea.Count(d => d.Rested),
                    DeckRemaining = p.DonDeck,
                });
            }

            // VisibleLog: a line private to THIS seat by its full Message; any other private line by its
            // PublicMessage substitute; fully-public lines by their message. Never another seat's Message.
            var visibleLog = new List<string>();
            foreach (var e in state.EventLog)
            {
                if (e == null) continue;
                bool mayReadPrivate = e.PrivateSeat == null || e.PrivateSeat == seat;
                if (mayReadPrivate && e.Message != null) visibleLog.Add(e.Message);
                else if (e.PublicMessage != null) visibleLog.Add(e.PublicMessage);
            }

            // A/B choice, deck look, and pending effects — projected ONLY for the seat they belong to (that
            // seat is entitled to them). Deck-look cards get surrogates registered in the runner-private map
            // so a select/order action can name them.
            ObservedChoice choice = null;
            if (state.ActiveChoice != null && state.ActiveChoice.Seat == seat)
                choice = new ObservedChoice
                {
                    SourceCardId = state.ActiveChoice.SourceCardId,
                    OptionA = state.ActiveChoice.OptionA,
                    OptionB = state.ActiveChoice.OptionB,
                };

            ObservedDeckLook deckLook = null;
            if (state.DeckLook != null && state.DeckLook.Seat == seat)
            {
                var cards = new List<ObservedCard>();
                foreach (var c in state.DeckLook.Cards)
                {
                    string sur = NextSurrogate();
                    if (c.InstanceId != null) idToSurrogate[c.InstanceId] = sur;
                    cards.Add(new ObservedCard { SurrogateId = sur, CardId = c.CardId, Rested = c.Rested, FaceUp = c.FaceUp });
                }
                deckLook = new ObservedDeckLook
                {
                    // Resolve the SOURCE to a legally-visible CardId — never expose DeckLook.SourceInstanceId,
                    // which is an authoritative instance id, not a card-definition id.
                    SourceCardId = ResolvePublicOrOwnCardId(state, seat, state.DeckLook.SourceInstanceId),
                    Step = state.DeckLook.Step,
                    Cards = new ReadOnlyCollection<ObservedCard>(cards),
                };
            }

            // Pending effects for this seat get an OPAQUE per-decision ref (the effect analogue of a card
            // surrogate). The authoritative EffectId is recorded only in the runner-private links.
            var effectIdToRef = new Dictionary<string, string>();
            var refToEffectId = new Dictionary<string, string>();
            var pending = new List<ObservedPending>();
            int pi = 0;
            foreach (var e in state.PendingEffects.Where(e => e.Seat == seat))
            {
                string r = "p#" + (pi++);
                if (e.EffectId != null) { effectIdToRef[e.EffectId] = r; refToEffectId[r] = e.EffectId; }
                pending.Add(new ObservedPending { Ref = r, SourceCardId = e.SourceCardId, PromptText = e.Text, Timing = e.Timing });
            }

            var surrogateToId = idToSurrogate.ToDictionary(kv => kv.Value, kv => kv.Key);
            links = new ObservationLinks
            {
                InstanceIdToSurrogate = idToSurrogate,
                SurrogateToInstanceId = surrogateToId,
                EffectIdToPendingRef = effectIdToRef,
                PendingRefToEffectId = refToEffectId,
            };
            return new PlayerObservation
            {
                Seat = seat,
                Status = state.Status,
                Phase = state.Phase,
                ActiveSeat = state.ActiveSeat,
                TurnNumber = state.TurnNumber,
                Knowledge = knowledge,
                Zones = new ReadOnlyCollection<ObservedZone>(zones),
                Don = new ReadOnlyCollection<ObservedDon>(don),
                Battle = ProjectBattle(state, seat),
                Choice = choice,
                DeckLook = deckLook,
                Pending = new ReadOnlyCollection<ObservedPending>(pending),
                VisibleLog = new ReadOnlyCollection<string>(visibleLog),
            };
        }

        /// <summary>Project the battle for one seat. Public facts to all; the RevealedLife identity ONLY to
        /// the entitled defender (TargetSeat) — masked from the attacker (design §4.5). Board card ids
        /// (attacker/target) are public and legal to identify. Their true InstanceId is NOT surfaced —
        /// action targeting is the opaque-token descriptor work (staged).</summary>
        private static ObservedBattle ProjectBattle(GameState state, string seat)
        {
            var b = state.Battle;
            if (b == null) return null;
            bool viewerIsDefender = seat == b.TargetSeat;
            return new ObservedBattle
            {
                Step = b.Step,
                PrioritySeat = b.PrioritySeat,
                AttackerSeat = b.AttackerSeat,
                TargetSeat = b.TargetSeat,
                AttackerCardId = PublicBoardCardId(state, b.AttackerSeat, b.AttackerId),
                TargetCardId = PublicBoardCardId(state, b.TargetSeat, b.TargetId),
                AttackPower = b.AttackPower,
                DefensePower = b.DefensePower,
                CounterPower = b.CounterPower,
                Blocked = b.Blocked,
                NoBlocker = b.NoBlocker,
                BlockerPowerBan = b.BlockerPowerBan,
                HasRevealedLife = b.RevealedLife != null,
                // ENTITLEMENT: the defender legally knows the life card in flight; the attacker does not.
                RevealedLifeCardId = (viewerIsDefender && b.RevealedLife != null) ? b.RevealedLife.CardId : null,
            };
        }

        /// <summary>Resolve a battle participant's CardId from the PUBLIC board zones (leader / character /
        /// stage) of its seat. Returns null if the instance is not a public board card — never reaches into
        /// a hidden zone, so it cannot leak a hidden identity.</summary>
        private static string PublicBoardCardId(GameState state, string seat, string instanceId)
        {
            if (seat == null || instanceId == null || !state.Players.TryGetValue(seat, out var p)) return null;
            if (p.Leader != null && p.Leader.InstanceId == instanceId) return p.Leader.CardId;
            if (p.Stage != null && p.Stage.InstanceId == instanceId) return p.Stage.CardId;
            var ch = p.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == instanceId);
            return ch?.CardId;
        }

        /// <summary>Resolve an instance id to its CardId only if the id sits in a zone this seat may legally
        /// identify — any player's public board (leader / character / stage) or trash, or the seat's own
        /// hand / face-up Life. Returns null otherwise, so a hidden or unknown source is NOT named and no
        /// true instance id is ever surfaced.</summary>
        private static string ResolvePublicOrOwnCardId(GameState state, string seat, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var kv in state.Players)
            {
                var p = kv.Value;
                if (p.Leader != null && p.Leader.InstanceId == instanceId) return p.Leader.CardId;
                if (p.Stage != null && p.Stage.InstanceId == instanceId) return p.Stage.CardId;
                var ch = p.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == instanceId);
                if (ch != null) return ch.CardId;
                var tr = p.Trash.FirstOrDefault(c => c.InstanceId == instanceId);
                if (tr != null) return tr.CardId;
                if (kv.Key == seat)
                {
                    var h = p.Hand.FirstOrDefault(c => c.InstanceId == instanceId);
                    if (h != null) return h.CardId;
                    var fl = p.Life.FirstOrDefault(c => c.FaceUp && c.InstanceId == instanceId);
                    if (fl != null) return fl.CardId;
                }
            }
            return null;
        }

        private static ObservedZone Zone(string owner, string zone, IEnumerable<CardInstance> cards,
            Func<CardInstance, bool> mayIdentify, Func<string> nextSurrogate,
            IDictionary<string, string> idToSurrogate)
        {
            var all = cards.ToList();
            var observed = new List<ObservedCard>();
            // Emit a record ONLY for a legally-identifiable card. A hidden card contributes to Count and
            // nothing else — no surrogate, no Rested/FaceUp/AttachedDon, which would each be information a
            // viewer does not legally hold.
            foreach (var c in all)
            {
                if (!mayIdentify(c)) continue;
                string sur = nextSurrogate();
                if (c.InstanceId != null) idToSurrogate[c.InstanceId] = sur;   // runner-private linkage
                observed.Add(new ObservedCard
                {
                    SurrogateId = sur,
                    CardId = c.CardId,
                    Rested = c.Rested,
                    FaceUp = c.FaceUp,
                    AttachedDonCount = c.AttachedDonIds?.Count ?? 0,
                });
            }
            return new ObservedZone
            {
                Owner = owner, Zone = zone, Count = all.Count,
                Cards = new ReadOnlyCollection<ObservedCard>(observed),
            };
        }
    }
}
