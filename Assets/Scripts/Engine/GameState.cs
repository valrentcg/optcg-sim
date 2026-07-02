// One Piece TCG - Engine: mutable game state model.
// Pure C#, no UnityEngine dependency. Ported from packages/engine/index.js.
// Design note: unlike the JS version (which deep-cloned state on every command for
// functional purity), this port mutates a single authoritative GameState in place and
// relies on CommandHistory + the seeded RNG for deterministic replay. This is the
// idiomatic Unity/netcode approach and avoids per-command deep-copy cost.
// Drop this into Unity at: Assets/Scripts/Engine/GameState.cs

using System.Collections.Generic;

namespace OnePieceTcg.Engine
{
    /// <summary>A live card in the match. Carries its definition id plus per-instance state.</summary>
    public sealed class CardInstance
    {
        public string InstanceId;
        public string CardId;
        public string Owner;            // "south" | "north"
        public string Zone;             // "leader" | "deck" | "hand" | "life" | "character" | "stage" | "trash"
        public bool Rested;
        public List<string> AttachedDonIds = new List<string>();
        public int? PlayedOnTurn;
    }

    /// <summary>A DON!! card in the cost area. Active (ready) or rested (spent/face-down side).</summary>
    public sealed class DonInstance
    {
        public string InstanceId;
        public bool Rested;
    }

    /// <summary>One player's board and resources.</summary>
    public sealed class PlayerState
    {
        public string Seat;
        public string Name;
        public string DeckName;
        public CardInstance Leader;
        public List<CardInstance> Deck = new List<CardInstance>();
        public List<CardInstance> Hand = new List<CardInstance>();
        public List<CardInstance> Life = new List<CardInstance>();
        // Exactly 5 slots; entries may be null when a slot is empty.
        public List<CardInstance> CharacterArea = new List<CardInstance> { null, null, null, null, null };
        public CardInstance Stage;
        public List<CardInstance> Trash = new List<CardInstance>();
        public List<DonInstance> CostArea = new List<DonInstance>();
        public int DonDeck = 10;
        public int DonInstanceCounter;
        public int TurnsStarted;
        public bool MulliganUsed;
        public bool MulliganDecided;
        public HashSet<string> AbilityUsedThisTurn = new HashSet<string>();
    }

    /// <summary>Active attack/battle state machine. Null when no attack is in progress.</summary>
    public sealed class BattleState
    {
        public string Id;               // unique id for scoping CardModifier.Duration == "thisBattle"
        public string Step;             // "block" | "counter" | "damage" | "trigger"
        public string AttackerSeat;
        public string AttackerId;
        public string TargetSeat;
        public string TargetId;
        public string OriginalTargetId;
        public bool Blocked;
        public int CounterPower;
        public int AttackPower;
        public int DefensePower;
        public CardInstance RevealedLife;   // populated during the trigger step
        public bool NoBlocker;              // opponent cannot activate any Blocker this battle
        public int? BlockerPowerBan;        // opponent cannot Blocker with power >= this value this battle

        // EffectScope.Battle power bonuses — keyed by CardInstance.InstanceId, cleared when
        // the BattleState is discarded (attack resolved, blocked, or cleared).
        public Dictionary<string, int> BattlePowerBonus = new Dictionary<string, int>();
    }

    // ---- Effect scoping -------------------------------------------------------

    /// <summary>
    /// Lifetime of a pending or applied effect.
    /// Instant    – resolves once and is done.
    /// Battle     – power/restriction is active only while the current BattleState exists.
    /// Turn       – power/restriction is active until the controller's next Refresh Phase.
    /// Passive    – always-on while its DON!!-count or board condition is met; never queued.
    /// </summary>
    public enum EffectScope { Instant, Battle, Turn, Passive }

    /// <summary>
    /// Where to look when the player clicks a card to supply a target for a pending effect.
    /// Play  – leader, character area, stage (default).
    /// Hand  – the effect wants the player to choose a card from their hand (e.g. discard/play).
    /// Trash – the effect wants the player to choose a card from their trash (e.g. play from trash).
    /// Any   – either zone is valid.
    /// </summary>
    public enum EffectTargetZone { Play, Hand, Trash, Any }

    /// <summary>An effect waiting for the player to resolve, usually created by timing text like [On Play] or [Trigger].</summary>
    public sealed class PendingEffect
    {
        public string EffectId;
        public string Seat;
        public string SourceInstanceId;
        public string SourceCardId;
        public string Timing;
        public string Text;
        public bool Optional;
        // Scoping and targeting annotations (set by the engine when queuing).
        public EffectScope Scope;
        public EffectTargetZone TargetZone;
    }

    /// <summary>A serializable player action. Optional fields are used per command type.</summary>
    public sealed class GameCommand
    {
        public string Type;
        public string Seat;
        public string InstanceId;
        public string Target;
        public string Attacker;
        public string Blocker;
        public int? Amount;
        public int? SlotIndex;
        public string EffectId;
        public bool? GoingFirst;
        public bool? Mulligan;
        public List<string> OrderedInstanceIds;
        public List<string> DonInstanceIds;
    }

    public sealed class LogEntry
    {
        public string Actor;
        public string Message;
        public string Time;
        public int Turn;
        public long Sequence;   // monotonic order (replaces JS wall-clock timestamp for determinism)
    }

    /// <summary>Full match state. The single source of truth the engine mutates.</summary>
    public sealed class GameState
    {
        public int Version = 1;
        public string Seed = "starter-slice-001";
        public string FirstPlayer = "south";
        public string CoinFlipWinner;       // seat that won the coin flip and gets to choose turn order
        public string Status = "setup";     // "coinflip" | "setup" | "active" | "finished"
        public string ActiveSeat = "south";
        public string Phase = "setup";      // "refresh" | "draw" | "don" | "main" | "battle" | "end" | "finished"
        public int TurnNumber;
        public SelectionRef Selected;       // UI selection echo (kept on state to mirror JS)
        public BattleState Battle;
        public List<PendingEffect> PendingEffects = new List<PendingEffect>();
        public int EffectSequence;
        public List<GameCommand> CommandHistory = new List<GameCommand>();
        public List<LogEntry> EventLog = new List<LogEntry>();
        public Dictionary<string, PlayerState> Players = new Dictionary<string, PlayerState>();
        public long LogSequence;            // increments per log entry

        // "During this turn" power buffs (instanceId -> bonus) and no-Blocker grants
        // (instanceId of the attacker that was granted it), both cleared every turn change.
        public Dictionary<string, int> TemporaryPowerBonus = new Dictionary<string, int>();
        public HashSet<string> NoBlockerGrantedThisTurn = new HashSet<string>();

        // Active "look at top N cards of deck" effect (e.g. Jewelry Bonney). Null when none in progress.
        public DeckLookState DeckLook;

        // Keyword grants and restriction flags with turn/battle duration.
        // Use AddModifier() / HasModifier() / CleanupXxxModifiers() in GameEngine.
        public List<CardModifier> ActiveModifiers = new List<CardModifier>();

        // Monotonic counter used to generate unique BattleState.Id values.
        public int BattleSequence;

        // Active "Choose one" branch — non-null when a card effect needs the player to pick A or B.
        // Resolved via the "resolveChoice" command with Target = "A" or "B".
        public ChoiceState ActiveChoice;

        // How many attacks each card (by InstanceId) has declared this turn.
        // Double Attack cards may attack twice (count < 2); others are blocked after 1.
        // Cleared at the start of every turn in ApplyStartOfTurn.
        public Dictionary<string, int> AttackCountThisTurn = new Dictionary<string, int>();

        // Alternate name overrides: instanceId → effective name string.
        // Set by "This card's name is also treated as [X]" effects. Cleared when card leaves play.
        public Dictionary<string, string> NameOverrides = new Dictionary<string, string>();
    }

    /// <summary>Mid-resolution state for a "look at top N cards" effect (e.g. Jewelry Bonney)
    /// or a full-deck search effect (e.g. "Search your deck for 1 Character cost ≤ N").</summary>
    public sealed class DeckLookState
    {
        public string Seat;
        public string SourceInstanceId;
        public string SourceName;
        public string FeatureFilter;             // type-tag required to be eligible, e.g. "Supernovas"
        public string Step;                       // "select" | "rearrange"
        public List<CardInstance> Cards = new List<CardInstance>();    // cards still pending placement
        public List<CardInstance> Ordered = new List<CardInstance>();  // bottom-of-deck order built during "rearrange"

        // Search-mode fields (SearchMode = true → full-deck search; remaining cards shuffled back)
        public bool SearchMode;
        public int MaxCost;          // -1 = no limit; otherwise only cards with cost ≤ MaxCost are valid
        public string CardTypeFilter; // "" = any type; otherwise only cards of this type are valid
    }

    /// <summary>
    /// A temporary modifier applied to a card — keyword grant (Rush, Double Attack, Blocker, etc.)
    /// or a flag restriction (cannotAttack, freeze, cannotBeKod, canAttackActive, noBlocker, etc.).
    /// Cleaned up automatically when its Duration expires (turn end or battle end).
    /// Power bonuses continue to use TemporaryPowerBonus / BattlePowerBonus dicts for simplicity.
    /// </summary>
    public sealed class CardModifier
    {
        public string SourceInstanceId;
        public string TargetInstanceId;
        /// <summary>
        /// "keyword"       — grants a keyword; see Keyword field.
        /// "cannotAttack"  — this card cannot attack.
        /// "cannotBeKod"   — this card cannot be K.O.'d (in battle or by effect per source).
        /// "canAttackActive" — this card may attack active (non-rested) cards.
        /// "freeze"        — this card is rested and cannot be set active until modifier expires.
        /// "cannotBeRested"— this card cannot be rested by effects.
        /// "noBlocker"     — the target attacker forces "cannot activate Blocker" this battle.
        /// "doubleAttack"  — this card may attack twice per turn.
        /// </summary>
        public string ModifierType;
        public string Keyword;      // when ModifierType == "keyword" (e.g. "Rush", "Double Attack", "Blocker")
        public string Duration;     // "thisTurn" | "thisBattle" | "permanent"
        public string BattleId;     // for "thisBattle" modifiers — matches BattleState.Id
    }

    /// <summary>Lightweight selection reference (mirrors JS state.selected).</summary>
    public sealed class SelectionRef
    {
        public string InstanceId;
        public string Seat;
    }

    /// <summary>
    /// Active "Choose one" branch — set when an effect with two mutually exclusive options needs
    /// the player to pick. Null when no choice is pending. Resolved via the "resolveChoice" command
    /// with Target = "A" or "B".
    /// </summary>
    public sealed class ChoiceState
    {
        public string Seat;
        public string SourceInstanceId;
        public string SourceCardId;
        public string Timing;
        /// <summary>Full text of option A (the first bullet).</summary>
        public string OptionA;
        /// <summary>Full text of option B (the second bullet).</summary>
        public string OptionB;
    }

}
