using System.Collections.Generic;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// THE UNSAFE CONTROL INTERFACE. An <see cref="ILegacyAgent"/> receives the referee
    /// <see cref="GameState"/> directly — it can see every hidden card. It exists ONLY as the explicit
    /// perfect-information baseline every historical measurement used. It is a marker over the existing
    /// <see cref="IAgent"/> so the runner and readers can tell "this agent is allowed to cheat, on purpose"
    /// from an honest one at a glance. Honest agents implement <see cref="IObservedAgent"/> instead and can
    /// never be handed a GameState.
    /// </summary>
    public interface ILegacyAgent : IAgent { }

    /// <summary>An agent that wants to WATCH each command as the runner applies it (before/after states), so
    /// it can maintain a persistent side-record — e.g. a <c>KnowledgeLedger</c> tracking legally-known
    /// deck-top cards. The runner calls this after every <c>ApplyCommand</c> for any agent that implements it.
    /// It is observation only; it must not mutate the states it is handed.</summary>
    public interface ICommandObserver
    {
        void ObserveApplied(GameCommand cmd, GameState before, GameState after);
    }

    /// <summary>
    /// THE HONEST DECISION INTERFACE. An <see cref="IObservedAgent"/> decides from a
    /// <see cref="PlayerDecisionContext"/> and NOTHING ELSE. Its signature carries no
    /// <see cref="GameState"/>, no <see cref="CardInstance"/>, no authoritative <see cref="GameCommand"/>,
    /// and no true instance id — so the boundary is enforced by the TYPE, not by discipline. The runner
    /// projects the observation and enumerates the legal actions; the agent returns one
    /// <see cref="ObservedAction"/> (by its opaque token); the runner translates it back to a real command.
    /// </summary>
    public interface IObservedAgent
    {
        string Name { get; }
        /// <summary>Choose one legal action, or null to act on nothing right now (e.g. it is not this seat's
        /// decision). Returns the chosen <see cref="ObservedAction"/> — the runner reads only its
        /// <see cref="ObservedAction.Token"/> to recover the authoritative command.</summary>
        ObservedAction Decide(PlayerDecisionContext context);
    }

    /// <summary>ONE legal action as an observed agent sees it. The only load-bearing field is the OPAQUE
    /// <see cref="Token"/>: it is the agent's sole handle on the action, minted per-decision by the runner
    /// and meaningless outside it. The remaining fields are a SANITIZED descriptor — enough for a heuristic
    /// to choose intelligently, all of it legally visible to the acting seat — and carry NO true instance
    /// id. A hidden card is never named here; only a legally-identifiable target's CardId appears.</summary>
    public sealed class ObservedAction
    {
        /// <summary>Opaque per-decision handle. The runner maps it back to the authoritative command; the
        /// agent must treat it as meaningless and only ever return one it was given. Deterministic in (match
        /// seed, seat, decision sequence, action ordinal) so paired/seeded runs reproduce.</summary>
        public string Token { get; init; }
        public string Type { get; init; }
        /// <summary>The acting card's CardId (own/public board), when the action has one. Never an id the
        /// seat may not identify; never an InstanceId.</summary>
        public string ActorCardId { get; init; }
        /// <summary>The acting card's SURROGATE — the same id it carries in <see cref="PlayerObservation"/>.
        /// This is what makes DUPLICATE copies distinguishable: two same-CardId characters have distinct
        /// surrogates, so "attack with THIS copy" is unambiguous. Never an InstanceId.</summary>
        public string ActorSurrogate { get; init; }
        /// <summary>The primary target's CardId, when it is a legally-identifiable card.</summary>
        public string TargetCardId { get; init; }
        /// <summary>The primary target's SURROGATE (same as in the observation), or null for a non-card
        /// target. Distinguishes duplicate targets.</summary>
        public string TargetSurrogate { get; init; }
        /// <summary>The target's zone, when meaningful (e.g. "hand", "trash", "character").</summary>
        public string TargetZone { get; init; }
        /// <summary>For a "choose A / B" action, which option this is ("A" or "B") — so the two options are
        /// no longer identical descriptors.</summary>
        public string ChoiceOption { get; init; }
        /// <summary>For a deck-look ordering action, the chosen order expressed as observation surrogates
        /// (top-first). Distinguishes different orderings of the same look.</summary>
        public IReadOnlyList<string> OrderedSurrogates { get; init; }
        /// <summary>For a pending-effect action (resolveEffect / passEffect), the OPAQUE reference of the
        /// pending effect it addresses — the same <see cref="ObservedPending.Ref"/> in the observation. The
        /// runner/world maps it back to the authoritative EffectId; the agent never sees the EffectId.</summary>
        public string PendingRef { get; init; }
        /// <summary>For a pending-effect action, the effect's legally-visible prompt text and source card —
        /// the acting seat is entitled to see its own effect's prompt/source, and needs them to choose.</summary>
        public string PromptText { get; init; }
        public string SourceCardId { get; init; }
        public bool? GoingFirst { get; init; }   // coinflip
        public bool? Mulligan { get; init; }      // mulligan
        public int? Amount { get; init; }         // scalar-carrying commands

        /// <summary>A COMPLETE semantic key that identifies this action independent of per-world instance
        /// ids and per-decision surrogates — so an honest planner can match a command it derived on a
        /// determinized world back to the authoritative action offered here. Duplicate-safe via the
        /// caller-supplied ordinal among otherwise-identical keys.</summary>
        public string SemanticKey(int ordinalAmongEqual) =>
            string.Join("|", Type, ActorSurrogate ?? "-", TargetSurrogate ?? "-", TargetZone ?? "-",
                ChoiceOption ?? "-", PendingRef ?? "-",
                OrderedSurrogates == null ? "-" : string.Join(">", OrderedSurrogates),
                GoingFirst?.ToString() ?? "-", Mulligan?.ToString() ?? "-", Amount?.ToString() ?? "-",
                ordinalAmongEqual);
    }

    /// <summary>Everything an <see cref="IObservedAgent"/> is given to make ONE decision — and nothing that
    /// could reach the referee truth. Contains the seat's <see cref="PlayerObservation"/>, the enumerated
    /// legal <see cref="Actions"/> (opaque tokens + sanitized descriptors), and runner-supplied reproducible
    /// coordinates. It deliberately holds NO GameState, NO token→command map, and NO true instance ids — a
    /// recursive reachability scan of this object must find none of them (that is a tested invariant).</summary>
    public sealed class PlayerDecisionContext
    {
        public PlayerObservation Observation { get; init; }
        public IReadOnlyList<ObservedAction> Actions { get; init; } = System.Array.Empty<ObservedAction>();
        /// <summary>Convenience: the acting seat (same as <see cref="PlayerObservation.Seat"/>).</summary>
        public string Seat => Observation?.Seat;
        /// <summary>A STABLE, PUBLIC, monotonic decision number supplied by the RUNNER (not by any agent's
        /// local counter, which varies with replanning config). Together with <see cref="SeedBasis"/> it lets
        /// an honest agent derive reproducible world seeds that stay comparable across configurations.</summary>
        public int DecisionSequence { get; init; }
        /// <summary>A runner-derived reproducible seed basis for this decision (stable across processes). An
        /// honest agent folds a per-world index into this when the K-world batch lands.</summary>
        public int SeedBasis { get; init; }
    }
}
