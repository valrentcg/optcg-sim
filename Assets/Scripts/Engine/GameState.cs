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
    [System.Serializable]
    public sealed class CardInstance
    {
        public string InstanceId;
        public string CardId;
        public string Owner;            // "south" | "north"
        public string Zone;             // "leader" | "deck" | "hand" | "life" | "character" | "stage" | "trash"
        public bool Rested;
        public bool FaceUp;             // Life-zone only: turned face-up by an effect
        public List<string> AttachedDonIds = new List<string>();
        // int? is not Unity-serializable (UAC1001) — engine-only state, so exclude it from
        // Unity serialization explicitly. All engine reads/writes are unaffected.
        [System.NonSerialized] public int? PlayedOnTurn;

        // Active per-instance stat modifiers, kept directly on the card so the UI can render
        // "+1000 (Jinbe)" / "-4 cost (Backlight)" chips. CostDelta entries are AUTHORITATIVE:
        // GameEngine.GetCost() sums them onto the printed cost. PowerDelta entries MIRROR the
        // engine's TemporaryPowerBonus/BattlePowerBonus bookkeeping (GetPower still reads those
        // dicts) — they exist so the UI has a single per-card list to display. Expired entries
        // are removed by the engine at the matching time (see ActiveModifier.ExpiresAt).
        public List<ActiveModifier> Modifiers = new List<ActiveModifier>();
    }

    /// <summary>
    /// One active power/cost modifier on a card instance. Serializable for JsonUtility.
    /// ExpiresAt: "endOfTurn" (cleared at the next Refresh Phase, same time as
    /// TemporaryPowerBonus), "endOfBattle" (cleared when the current BattleState is
    /// discarded), or "permanent" (cleared only when the card leaves play).
    /// </summary>
    [System.Serializable]
    public sealed class ActiveModifier
    {
        public string Source;       // display name of the card that applied it, e.g. "Jinbe [ST01-005]"
        public int PowerDelta;      // e.g. +1000 (mirror of the engine power dicts, for UI display)
        public int CostDelta;       // e.g. -4 (authoritative; GameEngine.GetCost applies it)
        public string ExpiresAt;    // "endOfTurn" | "endOfBattle" | "permanent"
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

        // Seat whose decision the battle is currently waiting on. After an attack is declared
        // every remaining decision belongs to the DEFENDER (block -> counter -> final resolve ->
        // trigger), so this is always TargetSeat for the whole battle; it exists as an explicit
        // field so the UI can gate its block/counter/resolve buttons on it without hardcoding.
        public string PrioritySeat;
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
        public int PendingLifeDamage;       // extra life damage still to deal this hit (Double Attack = 1 more)
        public bool NoBlocker;              // opponent cannot activate any Blocker this battle
        public int? BlockerPowerBan;        // opponent cannot Blocker with power >= this value this battle
        public int? BlockerPowerBanMax;     // opponent cannot Blocker with power <= this value this battle (OP01-120 Shanks / OP03-002 Adio: "2000 or less power")
        public int? BlockerCostBanMax;      // opponent cannot Blocker with cost <= this value this battle (OP02-061 Morley / OP02-101 Strawberry: "Blocker of any Character with a cost of 5 or less")

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
        // >0 while a "DON!! -N" cost is being paid one clicked DON!! at a time
        // (set only when the player has a mix of active/rested DON!! so the choice matters).
        public int DonPaymentRemaining;
        // Multi-selection progress for effects that need several picks ("trash 2 cards from
        // your hand", "up to 2 of your opponent's Characters ... cannot attack"). 0 = not yet
        // initialized; the resolver sets it to N on first entry and decrements per valid pick.
        public int SelectionsRemaining;
        // Instance ids already CHOSEN during this multi-pick resolution — an "up to N Characters"
        // effect must target N DISTINCT cards, so a card here is excluded from the glow and rejected
        // if clicked again (prevents e.g. double-applying "give up to 2 opponent Characters −2000"
        // to the same Character). Populated centrally in ResolveEffect after each continuing pick.
        public System.Collections.Generic.List<string> PickedInstanceIds;
        // Shared numeric budget across picks for "total power/cost of N or less" effects
        // (e.g. "K.O. up to 2 Characters with a TOTAL power of 4000 or less"). -1 = unused.
        public int RemainingBudget = -1;
        // First pick's instance id for two-step effects (base-power swaps etc.).
        public string FirstPickId;
        // Instance ids played so far by a multi-pick "play … with different card names" effect
        // (OP16-060 Sengoku leader), so each subsequent pick can be checked for a unique name.
        public List<string> PlayedPickIds = new List<string>();
        // The REMAINDER of a compound effect ("A. Then, B") stashed while clause A is still being
        // resolved over one or more picks. `Text` is truncated to the clause CURRENTLY resolving so
        // the action button shows only that step; when A finishes, `Text` advances to this and the
        // button updates to the next step. Empty = no pending continuation.
        public string PendingContinuation;
        // Progress ledger for the pending-effect panel's animated green/red text (task B).
        // OriginalText is the FULL clause captured at queue time (before truncation / auto-resolve).
        // DoneParts = sub-clauses that executed (shown GREEN); SkippedParts = sub-clauses skipped or
        // whose criteria weren't met (shown RED), both in resolution order. Threaded across the
        // "Then," continuation chain so the whole original text fills progressively on both clients.
        public string OriginalText;
        public List<string> DoneParts = new List<string>();
        public List<string> SkippedParts = new List<string>();
        // For a [Once Per Turn] triggered effect (e.g. Nami OP11-041's [On Your Opponent's Attack]):
        // the AbilityUsedThisTurn key to set ONLY when the effect actually resolves. Declining/skip
        // must NOT consume the once-per-turn, so it re-prompts on the next attack until used. Null =
        // no once-per-turn tracking.
        public string OnceKey;
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

        // INFO PRIVACY: when PrivateSeat is set, only a viewer on that seat sees Message (which
        // may name a hidden card, e.g. a card searched from deck to hand). Every other viewer
        // sees PublicMessage instead — or nothing, if PublicMessage is null. Empty/null
        // PrivateSeat means the entry is fully public (the common case).
        public string PrivateSeat;
        public string PublicMessage;
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

        // Owner-seats whose Character(s) were K.O.'d this turn (for "if your opponent's Character
        // has been K.O.'d during this turn" — OP16-100). Cleared each turn in ApplyStartOfTurn.
        public HashSet<string> CharKoedThisTurn = new HashSet<string>();
        // Per-seat highest base cost of an Event that seat activated this turn (for "if you have
        // activated an Event with a base cost of N or more during this turn" — OP15-002).
        public Dictionary<string, int> HighestEventCostThisTurn = new Dictionary<string, int>();
        // Instance-ids of cards (Leaders/Characters) that BATTLED an opponent's Character this turn — i.e.
        // reached the damage step of an attack whose (final) target was a Character. For "If this Leader has
        // battled your opponent's Character during this turn" (OP12-020 Zoro restand). Cleared each turn.
        public HashSet<string> BattledOppCharThisTurn = new HashSet<string>();
        // Per-seat self-restriction: this turn the seat cannot play Character cards with a base cost >= this value
        // ("Then, you cannot play Character cards with a base cost of N or more during this turn" — the DON-ramp
        // downside of OP12-030/OP13-023/OP13-118/OP14-020). Cleared at the start of that seat's next turn.
        public Dictionary<string, int> NoPlayCharBaseCostAtLeast = new Dictionary<string, int>();
        // Per-seat self-restriction: this turn the seat cannot play ANY cards from hand ("Then, you cannot play
        // cards from your hand during this turn" — OP13-028's DON-refresh downside). Cleared next turn.
        public HashSet<string> NoPlayFromHandThisTurn = new HashSet<string>();
        // Per-seat self-restriction: this turn the seat's attacks cannot target a Leader ("Then, you cannot attack
        // a Leader during this turn" — OP06-026's re-stand downside). Cleared next turn.
        public HashSet<string> CannotAttackLeaderThisTurn = new HashSet<string>();
        // Per-seat self-restriction: this turn the seat cannot add Life cards to hand via their own effects
        // ("Then, you cannot add Life cards to your hand using your own effects during this turn" — OP02-004 etc.).
        public HashSet<string> NoAddLifeToHandThisTurn = new HashSet<string>();
        // Per-seat self-restriction: this turn the seat cannot set DON!! active via a CHARACTER effect
        // ("Then, you cannot set DON!! cards as active using Character effects during this turn" — EB04-016/OP10-030).
        public HashSet<string> NoSetDonActiveViaCharThisTurn = new HashSet<string>();
        // Per-seat instance-id of the card most recently buffed by an "up to 1 … gains +N power" effect — so a
        // follow-up "that card gains an additional +M power" clause (10 Counter events) applies to the same card.
        public Dictionary<string, string> LastPowerBuffTargetId = new Dictionary<string, string>();

        // Seats whose "if your rested Character would be K.O.'d, trash this instead" protection
        // (OP05-030 Rosinante) has already been PAID during the current command's resolution. Once
        // paid, every OTHER rested Character K.O.'d by the SAME effect is also saved (official Kaido
        // ruling). Cleared at the start of each ApplyCommand (= scoped to one effect resolution).
        public HashSet<string> RestedKoProtectionPaid = new HashSet<string>();

        // Seats with an active "if any of your Characters would be K.O.'d in battle during this turn, you may
        // trash 1 card from your hand instead" replacement (EB02-030, registered by countering with it). Consulted
        // by the battle-damage K.O. path; cleared at the start of each turn (like the other this-turn sets).
        public HashSet<string> BattleKoTrashSaveSeats = new HashSet<string>();

        // Alternate name overrides: instanceId → effective name string.
        // Set by "This card's name is also treated as [X]" effects. Cleared when card leaves play.
        public Dictionary<string, string> NameOverrides = new Dictionary<string, string>();

        // Base-power overrides: instanceId → replacement for the PRINTED power ("base power
        // becomes N" effects). GetPower substitutes this before adding bonuses. Entries carry
        // an expiry like power bonuses: "endOfTurn" (next refresh) or "untilNextTurnOf:<seat>".
        public List<BasePowerOverride> BasePowerOverrides = new List<BasePowerOverride>();

        // Power bonuses that outlive the turn ("until the start of your next turn" / "until
        // the end of your opponent's next End Phase"): removed when OwnerSeat starts a turn.
        public List<TimedPowerBonus> TimedPowerBonuses = new List<TimedPowerBonus>();
    }

    /// <summary>"Base power becomes N" override for one card instance.</summary>
    [System.Serializable]
    public sealed class BasePowerOverride
    {
        public string TargetInstanceId;
        public int Value;
        public string OwnerSeat;     // effect controller
        public string Duration;      // "thisTurn" | "untilNextTurn"
    }

    /// <summary>A +/-power bonus spanning turn boundaries. Most expire when OwnerSeat's next
    /// turn starts; "until the end of your next turn" bonuses are carried into that turn and
    /// then use the ordinary end-of-turn cleanup.</summary>
    [System.Serializable]
    public sealed class TimedPowerBonus
    {
        public string TargetInstanceId;
        public int Delta;
        public string OwnerSeat;
        public string Duration;      // "startOfNextTurn" (default) | "endOfNextTurn"
    }

    /// <summary>Mid-resolution state for a "look at top N cards" effect (e.g. Jewelry Bonney)
    /// or a full-deck search effect (e.g. "Search your deck for 1 Character cost ≤ N").</summary>
    public sealed class DeckLookState
    {
        public string Seat;
        public string SourceInstanceId;
        public string SourceName;
        public string FeatureFilter;             // type-tag required to be eligible, e.g. "Supernovas"
        public string NamedCardFilter;            // specific card name required, e.g. "Sanji" (ORed with CardTypeFilter below when both set — "[Sanji] or Event card" style effects)
        public string Step;                       // "select" | "rearrange"
        public List<CardInstance> Cards = new List<CardInstance>();    // cards still pending placement
        public List<CardInstance> Ordered = new List<CardInstance>();  // bottom-of-deck order built during "rearrange"

        // Search-mode fields (SearchMode = true → full-deck search; remaining cards shuffled back)
        public bool SearchMode;
        public int MaxCost;          // -1 = no limit; otherwise only cards with cost ≤ MaxCost are valid
        public int MinCost = -1;     // -1 = no limit; "cost of N or more" / range lower bound (EB02-008, EB03-060)
        public string ExcludeName;   // "other than [Name]" — a card with this name is NOT a valid pick (60 tutors)
        public string CardTypeFilter; // "" = any type; otherwise only cards of this type are valid

        // "Then, trash the rest" variant (e.g. OP03-089 Brannew): after the select step the
        // remaining cards go to the trash instead of the bottom of the deck (no rearrange step).
        public bool TrashRest;

        // Play mode: the selected card is PLAYED to the character area instead of added to
        // hand ("Play up to 1 … from your deck" / "Look at N … play up to 1 …" effects).
        public bool PlayMode;
        public bool PlayRested;      // "play that card rested"
        public int MaxPower = -1;    // "with N power or less" eligibility (printed power)
        public int ExactPower = -1;  // "with N power" (exact printed power, no "or less" — ST30-002 Inazuma)
        public string ColorFilter;   // required card colour, e.g. "green" (OP02-030 "play … green {Land of Wano} …")
        public bool TrashSelected;   // selected card goes to the TRASH ("trash up to N cards")
        public bool RequireTrigger;  // eligibility: card must have printed [Trigger] text
        public int SelectCount = 1;  // how many picks the select step allows
        public bool ToTop;           // rearranged cards go to the TOP of the deck (ST17-003)
        public bool LifeMode;        // cards came from LIFE; confirmed order writes back to Life (ST13-012 Makino)
        public string LifeTargetSeat; // whose Life the looked cards belong to (for opponent's-Life peeks, e.g. OP03-099 Katakuri); null/empty = the looker's own Life

        // A trailing self-hand-disposal clause the deck-look flow itself does NOT perform, e.g.
        // "… place the rest at the bottom … Then, trash 1 card from your hand." (OP16-067 Tsuru,
        // OP09-034 Perona, OP10-057 Leo, OP13-086 …). Deck-look texts skip the ". Then," split, so
        // this tail is captured when the look opens and resolved once the look COMPLETES.
        public string PostLookClause;
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
        /// <summary>"thisTurn" | "thisBattle" | "permanent" | "untilNextTurn" (expires when
        /// OwnerSeat's NEXT turn begins — i.e. lasts through the opponent's next turn; used for
        /// "until the end of your opponent's next turn" restrictions like ST19-001 Smoker).</summary>
        public string Duration;
        public string BattleId;     // for "thisBattle" modifiers — matches BattleState.Id
        public string OwnerSeat;    // seat of the effect's controller; used by "untilNextTurn" expiry
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
        /// <summary>Seat that makes the choice (usually the controller; the OPPONENT for
        /// "Your opponent chooses one:" effects).</summary>
        public string Seat;
        /// <summary>Seat the chosen option resolves FOR (the effect's controller). Null → Seat.</summary>
        public string ControllerSeat;
        public string SourceInstanceId;
        public string SourceCardId;
        public string Timing;
        /// <summary>Full text of option A (the first bullet).</summary>
        public string OptionA;
        /// <summary>Full text of option B (the second bullet).</summary>
        public string OptionB;
        /// <summary>Full text of option C (the third bullet), if the modal has three (OP05-096). Null otherwise.</summary>
        public string OptionC;
    }

}
