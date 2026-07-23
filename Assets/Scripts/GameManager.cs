// One Piece TCG - polished runtime Canvas board.
// This replaces the temporary OnGUI board with real Unity UI objects while keeping
// the same pure C# rules engine underneath.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

public partial class GameManager : MonoBehaviour
{
    private const string AssetBase =
        @"C:\Users
perr\Documents\Codex\2026-06-23\can\one-piece-tcg-platform\apps\simulator\assets\cards";
    private const string DonImageFallbackPath =
        @"C:\Users
perr\Documents\Codex\2026-06-23\can\work\MOOgiwara\MOOgiwara-main\client\public\cards\donCardAltArt.png";
    private const string BackImageFallbackPath =
        @"C:\Users
perr\Documents\Codex\2026-06-23\can\work\MOOgiwara\MOOgiwara-main\client\public\cards\optcg_card_back.jpg";

    private static readonly Color Ink = new Color32(238, 242, 247, 255);
    private static readonly Color Muted = new Color32(159, 171, 190, 255);
    private static readonly Color Panel = new Color32(19, 24, 33, 232);
    private static readonly Color Line = new Color32(72, 83, 103, 120);
    private static readonly Color ZoneFill = new Color32(18, 24, 34, 96);
    private static readonly Color Gold = new Color32(226, 190, 102, 255);
    private static readonly Color RedAccent = new Color32(230, 84, 84, 255);
    // Cobalt theme (matches the design mock): cyan accent on dark navy/teal felt.
    private static readonly Color Accent   = new Color32(79, 195, 224, 255);   // #4fc3e0
    private static readonly Color Accent2  = new Color32(134, 214, 238, 255);  // #86d6ee
    private static readonly Color BadgeInk = new Color32(6, 32, 44, 255);      // #06202c
    private static readonly Color MenuBg   = new Color32(12, 23, 38, 255);     // #0c1726
    private static readonly Color MenuB    = new Color32(120, 180, 220, 46);   // rgba(120,180,220,.18)
    private static readonly Color ZoneBorder = new Color32(120, 180, 220, 66); // rgba(120,180,220,.26)
    private static readonly Color ZoneYou  = new Color32(40, 124, 124, 56);    // rgba(40,124,124,.22)
    private static readonly Color ZoneOpp  = new Color32(44, 94, 144, 56);     // rgba(44,94,144,.22)
    private static readonly Color MatTop   = new Color32(13, 33, 60, 255);     // ~#0d213c (radial mid)
    private static readonly Color MatBottom= new Color32(13, 38, 50, 255);     // ~#0d2632
    private static readonly Color SeamCyan = new Color32(79, 195, 224, 150);
    private static readonly Color LogBgDark= new Color32(14, 30, 46, 184);     // rgba(14,30,46,.72)
    private static readonly Color LogIdCyan= new Color32(95, 208, 236, 255);   // #5fd0ec
    private static readonly Color BtnBg     = new Color32(120, 180, 220, 28);  // rgba(120,180,220,.11)
    private static readonly Color ZoneFaintFill = new Color32(120, 180, 230, 11); // rgba(120,180,230,.045) - small zones

    private GameState state;
    private MatchConfig currentMatchConfig;
    private bool replaySaved;
    // Wall-clock start of the live match (Time.realtimeSinceStartup), captured in
    // NewMatch/EnterNetworkedMatch so the finished replay + match-history summary
    // can record a real duration instead of deriving a proxy from TurnCount.
    private float matchStartRealtime;
    // commandElapsedSeconds[i] = matchStartRealtime-relative timestamp when CommandHistory[i]
    // was dispatched live — parallel array to state.CommandHistory, captured in Dispatch()/
    // OnNetworkCommandReceived(), persisted via ReplayStore.Save, and consumed by
    // ReplayIndex/the replay player's Real-Time mode. Cleared in NewMatch/EnterNetworkedMatch.
    private readonly List<float> commandElapsedSeconds = new List<float>();
    private bool isReplayMode;
    private ReplayRecord loadedReplay;
    private int replayCursor;

    // "neutral" (tournament/broadcast left-right split, both seats upright, no seat is "you")
    // | "south" | "north" (classic single-perspective layout — the same board normal live play
    // uses — with that seat rendered at the bottom, as if you were seated there). Only "neutral"
    // rotates/repositions anything; "south"/"north" just reuse the classic (non-replay) layout
    // code as-is via BottomSeat/TopSeat and this flag, so the same hand-tuned zone math services
    // all three views. See DrawReplayControlBar's view-mode pill for the UI.
    private string replayViewMode = "neutral";
    private bool IsReplayRotated => isReplayMode && replayViewMode == "neutral";
    // "both" | "south" | "north" | "none" — which seat's hand renders face-up during replay.
    // Independent of replayViewMode; see DrawFannedHandRow.
    private string replayHandVisibility = "both";

    // ── Replay player (Phase 2) ─────────────────────────────────────────────
    // replayActions/replayTurnStartCursors/replayTurnBoundaries are built ONCE per replay
    // session (EnterReplayMode -> ReplayIndex.Build) by a single full resimulation pass —
    // see ReplayIndex.cs for why that's cheap. Everything below just indexes into them.
    private List<ReplayAction> replayActions;
    private Dictionary<int, int> replayTurnStartCursors;
    private List<int> replayTurnBoundaries;
    private bool replayHasTimingData;   // false for older replays with no CommandElapsedSeconds

    private bool replayPlaying;
    // "realtime" | "actionByAction" | "turnByTurn". "condensed" and "custom" were merged into
    // "actionByAction" (which now owns the user-selectable delay-between-steps setting) — the
    // three-way split felt redundant in practice.
    private string replayMode = "actionByAction";
    private float replayCustomDelaySeconds = 1f;   // used when replayMode == "actionByAction"
    private float replaySpeedMultiplier = 1f;      // 0.25x .. 4x, scales every mode's base delay
    private float replayNextAdvanceAt;             // Time.unscaledTime threshold for the next auto-step
    private int? replayStopAtCursor;               // auto-play halts once replayCursor reaches this (turn-jump target)
    // Categories the spec calls out as worth fast-forwarding straight to via the timeline's
    // prev/next-attack, prev/next-card-played, prev/next-major-change controls.
    private static readonly HashSet<string> MajorReplayCategories = new HashSet<string> { "attack", "play", "effect", "don", "endTurn" };

    // Set by MainMenuManager's replay browser before EnsureBoard(); when populated,
    // Start() enters read-only replay playback instead of starting a new match.
    public static ReplayRecord PendingReplayLoad;
    // Set by MainMenuManager's lobby hub before EnsureBoard(); when populated, Start()
    // enters a networked match instead of local hotseat play. LocalSeat restricts
    // interaction to the seat this client actually controls (see Dispatch()).
    public static string PendingNetworkedSeed;
    public static string PendingNetworkedSeat;
    public static bool PendingNetworkedRanked;   // set by the Ranked queue launch path only
    public static string PendingNetworkedMode;   // "ranked"|"casual"|"custom" for a networked match
    public static bool PendingNetworkedForgiveness;   // custom lobby: in-match rewind toggle enabled
    public static BlitzConfig PendingNetworkedBlitz;   // custom lobby: timed-match settings (null = untimed)
    // Deck picks for a networked match (from the lobby's SELECT DECK flow). Sent
    // inside the match-start payload so both clients hold both decks; either may
    // be null, in which case CreateMatch falls back to the ST01/ST02 defaults.
    public static NetworkDeck PendingNetworkedSouthDeck;
    public static NetworkDeck PendingNetworkedNorthDeck;
    // Player display names for the center turn indicator. Set by MainMenuManager before
    // EnsureBoard() for networked matches; hotseat falls back to "Player 1"/"Player 2".
    public static string PendingSouthName;
    public static string PendingNorthName;
    private string southDisplayName = "Player 1";
    private string northDisplayName = "Player 2";
    private string DisplayName(string seat) => seat == "north" ? northDisplayName : southDisplayName;
    private bool isNetworked;
    private bool isRankedMatch;   // networked match launched from the Ranked queue → reports to RankedStore
    private string networkedMode; // "ranked"|"casual"|"custom" for a networked match (else null)
    // Ranked identity captured at match start, WHILE both players are connected: a mid-match leave
    // disconnects the opponent, after which LobbyManager.OpponentPlayerId() is gone — so we cache it
    // here to still file the dual-report when a surrender/leave ends the game.
    private string cachedRankedMatchId;
    private string cachedRankedOppId;
    private string localSeat;
    // Set when the networked peer disconnects mid-match; Render() shows the
    // "OPPONENT LEFT — YOU WIN!" modal until the player returns to the menu.
    private bool opponentLeft;
    // Result screen for a finished ranked/casual match: a "YOU WIN!/YOU LOSE." modal
    // whose only exit is Main Menu (→ ReturnToMenu, which tears down Netcode). Funnelling
    // every match through it guarantees a clean teardown and stops the player lingering in
    // a finished match. "View Board" hides it (matchResultHidden) to inspect the final
    // state; a "Show Result" chip brings it back. The in-match menu is locked out meanwhile.
    private string finishedResultText;   // "YOU WIN!" / "YOU LOSE." / "MATCH OVER" — set on finish
    private bool matchResultHidden;      // View Board pressed — result popup temporarily hidden
    // Custom-online rematch handshake (both players must ask; host then publishes a shared
    // seed and both restart in place over the still-open session — no teardown, no re-queue).
    private bool rematchLocalRequested;  // I clicked Rematch
    private bool rematchPeerRequested;   // opponent clicked Rematch
    private bool rematchStarting;        // seed agreed, restart under way (guards double-fire)
    // ---- In-match chat (networked only; see DrawMatchChatPanel) ----
    private bool chatOpen;
    private bool chatUnread;
    private string chatDraft = "";
    private InputField chatInputField;
    private bool ChatInputFocused => chatInputField != null && chatInputField.isFocused;
    // ---- End-of-match: add the networked opponent as a friend (result overlay) ----
    private string addFriendState = "idle";   // idle | sending | sent | error | alreadyFriend
    private bool addFriendChecked;             // whether the "already a friend?" check has run for this result
    // ---- Presence (networked only): what WE are hovering, and the opponent's last payload ----
    private string presenceHoverCardId;
    private int presenceRaisedHandIndex = -1;
    private int presenceHandDragFrom = -1;   // slot we're dragging within our own hand (live reorder), -1 = none
    private int presenceHandDragTo = -1;     // current target slot of that reorder drag
    private PresencePayload opponentPresence;
    private RectTransform opponentPresenceGlow;
    // Top-hand (opponent) fan holders + their rest positions, so PresenceReceived can lift
    // the face-down cards the opponent is inspecting. Rebuilt every Render.
    private readonly List<RectTransform> opponentHandSlots = new List<RectTransform>();
    private readonly List<Vector2> opponentHandSlotHomes = new List<Vector2>();
    // Cached label for the coin-flip "waiting on opponent" state (networked only); Update()
    // mutates its text directly for the blinking-dots animation instead of re-rendering the
    // whole board every frame. Unity's fake-null makes the null-check safe even after the
    // overlay's GameObject gets torn down by the next Render() pass.
    private Text coinFlipWaitingText;
    private string coinFlipWaitingBaseMessage;
    // Coin-flip reveal animation: a spinning coin plays first; the winner + Go First/Second choice
    // only appear once it lands. Both reset when the coin-flip phase ends so the next match re-spins.
    private bool coinFlipRevealed;
    private bool coinFlipSpinStarted;
    // Which seat renders at the bottom/top of THIS client's screen. "south" for hotseat and
    // Versus Self (unchanged, matches every existing hardcoded assumption in this file) -
    // for a networked match, the locally-controlled seat is always drawn at the bottom
    // regardless of whether that's "south" or "north" in the shared GameState, since the
    // underlying seat identity must stay the same on both clients for the deterministic
    // command-replay sync to work (see MatchNetworkSync.cs) - only the rendering flips.
    // "north" replay view mode swaps which seat renders at the bottom, so the classic
    // (non-rotated) single-perspective layout shows the match as if seated as North instead
    // of South — see replayViewMode/IsReplayRotated.
    // Sandbox (see GameManager.Sandbox.cs) can flip which seat renders at the bottom so a single
    // player can drive either side of the board directly. This override wins over the normal
    // south/localSeat pick; replay's own north-view flip is unaffected because sandbox and replay
    // are mutually exclusive modes.
    private string BottomSeat => isSandbox ? sandboxViewSeat
        : (isReplayMode && replayViewMode == "north") ? "north"
        : isNetworked ? localSeat
        : (povSeat ?? "south");   // povSeat: chosen perspective for a restored-from-code solo game
    private string TopSeat => BottomSeat == "south" ? "north" : "south";
    private string selectedId;
    private string selectedSeat;
    private string selectedDonSeat;
    private int selectedDonAnchorIndex = -1;
    private readonly List<string> selectedDonIds = new List<string>();
    // ---- DON cost-staging slots (client-side planning aid; never mutates engine/game state) ----
    private readonly List<int> donGroupSizes = new List<int>();                 // partition of the active DON into groups
    private int donGroupTurn = -1;
    private string donGroupSeat;
    private int donGroupLastActive = -1;
    // When non-null, the side panel shows that player's trash list instead of normal actions.
    private string trashViewSeat;

    // Client-side-only working order for the DeckLook "rearrange" step (drag to reorder, then
    // Confirm submits the whole order at once). Reset whenever the overlay isn't in that step.
    private List<CardInstance> deckLookWorkingOrder;
    private bool deckLookAnimating;
    private bool isDraggingHandCard;
    private readonly Dictionary<string, RectTransform> deckLookCardRects = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, RectTransform> boardDeckPileRects = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, RectTransform> handCardRects = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, RectTransform> stageZoneRects = new Dictionary<string, RectTransform>();  // per-seat Stage zone (for Blitz clock placement)
    private readonly Dictionary<string, RectTransform> cardTargetRects = new Dictionary<string, RectTransform>();
    private RectTransform southHandRow;
    private RectTransform northHandRow;
    private GameObject hoverTargetArrowRoot;

    private Canvas canvas;
    private RectTransform boardRoot;
    private RectTransform playRoot;   // centred play column (mat + hands); background stays full-bleed on boardRoot
    private Vector2 boardCardSize = Vector2.zero;
    private bool isDraggingAttack;
    private RectTransform sideRoot;
    private RectTransform leftRoot;
    private RectTransform previewRoot;
    private RectTransform blockerPreviewRoot;   // right-hand info panel shown when hovering a [Blocker] shield
    private CardInstance previewLockCard;   // last left-clicked card, shown in the docked left preview
    private bool menuOpen;                   // game menu (upper-right) open/closed
    private bool soundMenuOpen;              // sound settings panel (opened from the game menu)
    private bool surrenderConfirmOpen;       // "are you sure you want to surrender?" confirm modal
    private RectTransform deckLookOverlay;    // full-screen search/look overlay (lives on the canvas)
    private RectTransform trashOverlay;       // trash-viewer popup (confined to the local play area)
    private RectTransform mulliganOverlay;    // opening-hand overlay (deck-look style)
    private bool trashSortLowestFirst = true; // next SORT click: lowest cost at the top (toggles)
    private RectTransform trashOverlayCatcher; // full-screen click-outside-to-close catcher (browse mode)
    private RectTransform resultPeekChip;     // "◂ SHOW RESULT" chip (lives on the canvas; torn down each Render)
    // ── Bot (Versus A.I.) ─────────────────────────────────────────────────────
    // Two difficulty tiers share one tick loop and one per-turn blacklist:
    //   "beginner"     — AiTick(): Strawtable-style greedy heuristics, no search.
    //   "intermediate" — IntermediateAiTick(): delegates to Engine/Bot/IntermediateBot.cs,
    //                    which was validated against real high-ranked-ladder replay data.
    // Both drive one command per "think" tick so moves are watchable, and both rely on
    // aiTriedThisTurn to avoid re-attempting an action the engine rejected (state
    // unchanged → would otherwise infinite-loop); IntermediateBot keys it with its own
    // Signature() so the same set works for either tier without collisions in practice.
    private string aiSeat;
    private string aiDifficulty = "beginner"; // "beginner"|"intermediate"|"advanced" — set from PendingAiDifficulty in NewMatch()
    private float aiNextActionAt;
    private readonly HashSet<string> aiTriedThisTurn = new HashSet<string>();
    // Advanced tier only: instance-ids whose [Activate: Main] the activation layer already tried this turn,
    // so a repeatable ability cannot loop. Keyed by card instance id, distinct from aiTriedThisTurn's
    // command signatures. Cleared on each turn change alongside aiTriedThisTurn.
    private readonly HashSet<string> aiActivatedThisTurn = new HashSet<string>();
    // Advanced tier only: the AI deck's archetype, classified once at match start, gates which strategy
    // module the contract router applies (midrange→activation, control→pressure). Default midrange.
    private string aiArchetype = "midrange";
    private int aiLastTurnSeen = -1;
    private int lastBannerTurn = -1;          // last (turn, seat) a turn banner was shown for
    private string lastBannerSeat;
    private string mulliganAnimShownKey;      // seat+hand fingerprint already dealt-animated (re-animates after a mulligan redraw)
    private string mulliganRedrawSeat;        // seat whose post-mulligan fresh 5 should be shown/animated
    // ── Universal card-movement animation ────────────────────────────────────
    // After every Render, card positions are snapshotted; any card whose ZONE changed
    // since the previous snapshot gets a ghost flight from old to new position (event use
    // flares, counters fly hand→trash, life→hand, board→life, DON!! attaches, ...).
    private sealed class CardPose { public Vector3 pos; public Vector2 size; public string zone; public bool don; public string cardId; public string owner; public RectTransform rt; public RectTransform faceRt; public Quaternion rot; }
    private readonly Dictionary<string, CardPose> lastCardPoses = new Dictionary<string, CardPose>();
    private readonly HashSet<string> suppressMoveAnim = new HashSet<string>();   // ids the LOCAL player just drag-placed (their drag was the animation)
    private int activeMoveGhosts;             // in-flight zone-move ghosts (bot waits on these)
    private RectTransform northHalfRect;      // playmat halves (turn-particle rim path)
    private float turnFusePhase;              // 0..1 rim-lap position of the particle swarm (persists across renders)
    private RectTransform turnCrestRect;      // center "TURN N · X'S TURN" pill (particles hide behind it)
    private RectTransform southHalfRect;
    // ── SFX ──────────────────────────────────────────────────────────────────
    private AudioSource sfxSource;
    private AudioClip cardDrawClip;
    private AudioClip attackClip;
    private bool sfxLoadStarted;
    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat("optcg.sfx", 0.8f);
        set { PlayerPrefs.SetFloat("optcg.sfx", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }
    // Phase-tracker display override: sequences REFRESH → DRAW → DON at turn start so
    // the pills mirror what the animations are showing (the engine jumps straight to
    // "main" inside one command).
    private string phaseDisplayOverride;
    private Coroutine phaseSeqCo;
    private readonly Dictionary<string, RectTransform> moveZoneAnchors = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, RectTransform> donMoveRects = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, List<RectTransform>> lifeMoveRects = new Dictionary<string, List<RectTransform>>();
    private readonly Dictionary<string, RectTransform> presenceGlowRects = new Dictionary<string, RectTransform>();  // hover-glow targets beyond board cards (DON, piles, life)
    private readonly List<RectTransform> opponentPresenceHandGlows = new List<RectTransform>();
    private string lastPresenceHover;         // last applied opponent hover (skip identical packets — re-creating the glow at 10Hz reads as blinking)
    private string lastPresenceRaised;
    private string lastPresenceGroups;
    private bool mulliganPeeking;             // player toggled the mulligan overlay away to see the board
    private List<string> deckLookScryTopIds;  // scry step: ids chosen to stay on TOP, in click order
    private bool deckLookPeeking;              // true while the player has toggled the overlay away to check the board/hand
    private DeckLookState deckLookRevealSession; // which DeckLookState (by reference) we've already started/finished revealing
    private bool deckLookRevealing;              // true while cards are still being drawn in one at a time; selection is disabled meanwhile
    private RectTransform handHoverRoot;
    private Image previewImage;
    private Text previewTitle;
    private Font font;
    private Font monoFont;
    private Font titleFont;
    private Font jpFont;   // Japanese-capable (for the ドン!! DON badge)
    private Image southHandPanelImage;
    private Image northHandPanelImage;

    private readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, Texture2D> colorIconCache = new Dictionary<string, Texture2D>();
    private readonly Dictionary<Texture2D, Texture2D> iconOutlinedCache = new Dictionary<Texture2D, Texture2D>();
    private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private readonly Dictionary<int, Sprite> roundedCardBorderSprites = new Dictionary<int, Sprite>();
    private Texture2D backTex;
    private Sprite backSprite;
    private Texture2D donTex;
    private Sprite donSprite;
    private Texture2D donBackTex;
    private Sprite donBackSprite;
    private Sprite roundedCardMaskSprite;
    private Sprite arrowHeadSprite;

    // Single-scene design: the main menu (MainMenuManager) is the entry point.
    // It creates the board on demand via EnsureBoard() when the player presses
    // ENTER. Set this to true if you want to launch straight into a match
    // (e.g. for development) without going through the menu first.
    public static bool BootBoardImmediately = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBoot()
    {
        // Don't auto-create the board by default — otherwise it would render
        // underneath the menu. The menu calls EnsureBoard() on launch instead.
        if (BootBoardImmediately)
            EnsureBoard();
    }

    /// <summary>Creates the game board if one isn't already present.</summary>
    public static void EnsureBoard()
    {
        if (Object.FindAnyObjectByType<GameManager>() == null)
            new GameObject("GameManager").AddComponent<GameManager>();
    }

    [System.Serializable]
    private sealed class OfficialCardPayload
    {
        public OfficialCardRecord[] cards = new OfficialCardRecord[0];
    }

    [System.Serializable]
    private sealed class OfficialCardRecord
    {
        public string id = "";
        public string type = "";
        public string name = "";
        public string color = "";
        public int cost = 0;
        public int life = 0;
        public int power = 0;
        public int counter = 0;
        public string effect = "";
        public string trigger = "";
        public string[] keywords = new string[0];
        // Affiliation/attribute tags, e.g. ["Straw Hat Crew"], ["Supernovas"]. Optional in JSON.
        public string[] features = new string[0];
        public string feature = "";
        public string attribute = "";   // ＜Slash＞/＜Strike＞/… icon; drives battle-K.O.-immunity clauses
    }
    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        // Techy/mono faces to approximate the design mock (Chakra Petch / JetBrains Mono). Falls back
        // through common Windows fonts; if none exist these stay null and TextObject uses the default.
        try { monoFont = Font.CreateDynamicFontFromOSFont(new[] { "JetBrains Mono", "Consolas", "Cascadia Mono", "Courier New" }, 14); } catch { monoFont = null; }
        try { titleFont = Font.CreateDynamicFontFromOSFont(new[] { "Chakra Petch", "Bahnschrift", "Eurostile", "Segoe UI Semibold", "Segoe UI" }, 16); } catch { titleFont = null; }
        // Japanese-capable face for the ドン!! DON badge (legacy Text won't render katakana from Arial).
        try { jpFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Segoe UI" }, 16); } catch { jpFont = null; }
        EnsureEventSystem();
        BuildShell();
    }

    private void Start()
    {
        LoadOfficialCardLibrary();
        if (PendingReplayLoad != null)
        {
            var record = PendingReplayLoad;
            PendingReplayLoad = null;
            EnterReplayMode(record);
        }
        else if (PendingNetworkedSeed != null)
        {
            var seed = PendingNetworkedSeed;
            var seat = PendingNetworkedSeat;
            PendingNetworkedSeed = null;
            PendingNetworkedSeat = null;
            EnterNetworkedMatch(seed, seat);
        }
        else if (PendingSandbox)
        {
            PendingSandbox = false;
            NewSandbox();
        }
        else if (!string.IsNullOrEmpty(PendingRestoreCode))
        {
            var code = PendingRestoreCode;
            PendingRestoreCode = null;
            BeginRestore(code);
        }
        else
        {
            NewMatch();
        }
    }

    private void OnDestroy()
    {
        MatchNetworkSync.CommandReceived -= OnNetworkCommandReceived;
        MatchNetworkSync.ChatReceived -= OnNetworkChatReceived;
        MatchNetworkSync.PresenceReceived -= OnNetworkPresenceReceived;
        MatchNetworkSync.RematchRequested -= OnRematchRequested;
        MatchNetworkSync.RematchStartReceived -= OnRematchStartReceived;
        UnsubscribeRewind();
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null) nm.OnClientDisconnectCallback -= OnPeerDisconnected;
    }

    private void Update()
    {
        // Coalesced re-render when async CDN card art/definitions arrive: many
        // fetches can complete in one frame — rebuild once, and never mid-drag
        // (Render() would destroy the dragged object under the EventSystem).
        if (_artRefreshQueued && !isDraggingHandCard && !isDraggingAttack)
        {
            _artRefreshQueued = false;
            Render();
        }

        if (coinFlipWaitingText != null)
        {
            int dots = (int)(Time.unscaledTime * 2f) % 4;
            coinFlipWaitingText.text = coinFlipWaitingBaseMessage + new string('.', dots);
        }

        if (isReplayMode) ReplayAutoAdvanceTick();

        // Blitz/timed match: drain the clock owner's clock + update the HUD digits every frame.
        if (BlitzActive) { BlitzTick(); BlitzHudTick(); }

        // Bot: one action per think-tick whenever any decision belongs to the AI seat
        // (turn actions, effect targets, choices, battle responses). Difficulty picks which
        // decision core drives that single action. Three tiers, produced by the out-of-ship
        // discovery platform (Tools/Sim):
        //   All three tiers share the SAME strong decision core (Engine/Bot/IntermediateBot.cs, which carries
        //   every validated playtest win) so difficulty is a smooth progression, not different bots:
        //   "advanced"     — IntermediateBot core + SearchBot rollout on tactical branches, gated by AI-deck
        //                    archetype (Engine/Bot/Search/AdvancedContractBot.cs; see AdvancedAiTick).
        //   "intermediate" — IntermediateBot core at FULL strength (all wins), no search.
        //   "beginner"     — IntermediateBot core but with the resource-discipline habits reverted (see
        //                    ApplyBotDifficultyKnobs): it still pressures Life (the main lesson) but plays DON!!
        //                    and counters sloppily, so it is clearly beatable while never looking brain-dead.
        //   (ChampionBot.cs is retired from the ladder — it predates the wins and now loses to the core.)
        if (aiSeat != null && !isReplayMode && state != null && state.Status != "finished"
            && Time.unscaledTime >= aiNextActionAt)
        {
            ApplyBotDifficultyKnobs();
            bool acted = aiDifficulty == "advanced" ? AdvancedAiTick()
                       : IntermediateAiTick();
            if (acted) aiNextActionAt = Time.unscaledTime + 2.0f;                          // thinking pause
            else aiNextActionAt = Mathf.Max(aiNextActionAt, Time.unscaledTime + 0.25f);    // respect delays the tick set
        }

        // Presence OUT: stream our current hover/raised-hand state to the opponent every frame;
        // MatchNetworkSync.SendPresence self-rate-limits (~10/s) and no-ops with no peer. Sending
        // the FULL current state each call means a dropped packet is corrected by the next one,
        // and hover-end naturally goes out as an empty payload.
        if (isNetworked && !isReplayMode && state != null)
        {
            MatchNetworkSync.SendPresence(new PresencePayload
            {
                hoverCardId = presenceHoverCardId,
                raisedHandIndexes = presenceRaisedHandIndex >= 0 ? new[] { presenceRaisedHandIndex } : new int[0],
                donGroups = (localSeat == state.ActiveSeat && donGroupSizes.Count > 1) ? donGroupSizes.ToArray() : new int[0],
                handDragFrom = presenceHandDragFrom,
                handDragTo = presenceHandDragTo,
            });
        }

        // While the chat input has keyboard focus, keystrokes must never reach game handling.
        if (ChatInputFocused) return;

        if (selectedDonIds.Count == 0) return;

        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            ClearDonSelection();
    }

    private void LoadOfficialCardLibrary()
    {
        if (CardAssets.UseCdn) { LoadOfficialCardLibraryAsync(); return; }
        string path = CardAssets.LocalPath("official-card-library.json");
        if (!File.Exists(path)) return;
        try { ParseOfficialCardLibrary(File.ReadAllText(path)); }
        catch (System.Exception ex) { Debug.LogWarning($"Official card library failed to load: {ex.Message}"); }
    }

    // WebGL/CDN: fetched over HTTP once the asset index is ready; the board
    // re-renders (coalesced in Update) when definitions land.
    private async void LoadOfficialCardLibraryAsync()
    {
        // Safety net: if the game scene is entered without the menu having run
        // the boot sequence, kick the (idempotent) init ourselves.
        _ = CardAssets.InitAsync();
        while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
        string json = await CardAssets.ReadTextAsync("official-card-library.json");
        if (string.IsNullOrEmpty(json)) { Debug.LogWarning("Official card library unavailable from CDN."); return; }
        ParseOfficialCardLibrary(json);
        _artRefreshQueued = true;
    }

    private void ParseOfficialCardLibrary(string json)
    {
        try
        {
            var payload = JsonUtility.FromJson<OfficialCardPayload>("{\"cards\":" + json + "}");
            if (payload?.cards == null) return;

            var seen = new HashSet<string>();
            foreach (var card in payload.cards)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.id) || !seen.Add(card.id)) continue;
                CardData.UpsertCard(
                    card.id,
                    card.name,
                    card.type,
                    card.color,
                    card.cost,
                    card.power,
                    card.life > 0 ? card.life : (int?)null,
                    card.counter,
                    card.keywords,
                    NormalizeEffectText(card.effect),
                    NormalizeEffectText(card.trigger),
                    NormalizeFeatures(card),
                    null,
                    card.attribute);
            }
            Debug.Log($"Loaded {seen.Count} official One Piece card definitions.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Official card library failed to load: {ex.Message}");
        }
    }

    // ~200 library entries glue ability clauses together with no newline between them
    // ("…Characters.[On Your Opponent's Attack] …"), which breaks the engine's per-clause
    // parsing. Insert a newline before a timing tag whenever it directly follows the end of
    // a sentence ('.', ')' or a closing quote) — and ONLY then, so mid-sentence tag
    // references ("a card with a [Trigger]") and combined tags ("[Main]/[Counter]") survive.
    private static readonly string[] ClauseTags =
    {
        "[On Play]", "[Activate: Main]", "[When Attacking]", "[On Block]", "[On K.O.]",
        "[Main]", "[Counter]", "[Trigger]", "[End of Your Turn]", "[Start of Your Turn]",
        "[On Your Opponent's Attack]", "[End of Opponent's Turn]",
    };

    private static string NormalizeEffectText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var tag in ClauseTags)
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                "(?<=[.)\u201D\"])" + @"\s*" + System.Text.RegularExpressions.Regex.Escape(tag),
                "\n" + tag);
        }
        return System.Text.RegularExpressions.Regex.Replace(text, "\n+", "\n").Trim();
    }

    private static string[] NormalizeFeatures(OfficialCardRecord card)
    {
        if (card == null) return new string[0];
        var values = new List<string>();
        if (card.features != null)
        {
            foreach (var feature in card.features)
            {
                if (!string.IsNullOrWhiteSpace(feature)) values.Add(feature.Trim());
            }
        }
        if (!string.IsNullOrWhiteSpace(card.feature))
        {
            foreach (var feature in card.feature.Split('/'))
            {
                if (!string.IsNullOrWhiteSpace(feature)) values.Add(feature.Trim());
            }
        }
        return values.Distinct().ToArray();
    }

    // Set by MainMenuManager's versus-self deck-picker flow before EnsureBoard();
    // when both are populated, NewMatch() plays these two custom decks against
    // each other instead of the ST01-vs-ST02 default. Left set across "New
    // Match" (a rematch keeps the same matchup) and cleared only by picking
    // new decks or going back through the menu.
    public static string PendingSouthDeckId;
    public static string PendingNorthDeckId;

    /// <summary>Enters the board playing `southDeckId` vs `northDeckId` (both deck-builder deck ids).</summary>
    public static void LaunchVersusSelf(string southDeckId, string northDeckId)
    {
        PendingSouthDeckId = southDeckId;
        PendingNorthDeckId = northDeckId;
        PendingAiNorth = false;
        EnsureBoard();
    }

    /// <summary>Human (south) vs Bot piloting `aiDeckId` (north). `difficulty` is
    /// "beginner" (default, the original greedy heuristic bot) or "intermediate"
    /// (Engine/Bot/IntermediateBot.cs).</summary>
    public static bool PendingAiNorth;
    public static string PendingAiDifficulty = "beginner";
    public static void LaunchVersusAi(string southDeckId, string aiDeckId, string difficulty = "beginner")
    {
        PendingSouthDeckId = southDeckId;
        PendingNorthDeckId = aiDeckId;
        PendingAiNorth = true;
        PendingAiDifficulty = string.IsNullOrEmpty(difficulty) ? "beginner" : difficulty;
        EnsureBoard();
    }

    // Converts a deck-builder DeckData (leaderId + main-deck DeckEntry list) into
    // the engine's DeckDef. Returns null if the deck has no leader assigned.
    private static DeckDef BuildDeckDef(DeckData d)
    {
        if (d == null || string.IsNullOrEmpty(d.leaderId)) return null;
        var list = new List<(string cardId, int qty)> { (d.leaderId, 1) };
        if (d.cards != null)
            foreach (var e in d.cards)
                if (e != null && e.count > 0) list.Add((e.id, e.count));
        return new DeckDef
        {
            Id = d.id,
            Name = string.IsNullOrEmpty(d.name) ? "Custom Deck" : d.name,
            Leader = d.leaderId,
            List = list,
        };
    }

    private void NewMatch()
    {
        isReplayMode = false;
        loadedReplay = null;
        replaySaved = false;
        matchStartRealtime = Time.realtimeSinceStartup;
        commandElapsedSeconds.Clear();
        var config = new MatchConfig { Seed = System.Guid.NewGuid().ToString("N") };
        if (!string.IsNullOrEmpty(PendingSouthDeckId) && !string.IsNullOrEmpty(PendingNorthDeckId))
        {
            var southDef = BuildDeckDef(DeckStore.Get(PendingSouthDeckId));
            var northDef = BuildDeckDef(DeckStore.Get(PendingNorthDeckId));
            if (southDef != null && northDef != null)
            {
                config.SouthDeckDef = southDef;
                config.NorthDeckDef = northDef;
            }
        }
        currentMatchConfig = config;
        aiSeat = PendingAiNorth ? "north" : null;
        aiDifficulty = string.IsNullOrEmpty(PendingAiDifficulty) ? "beginner" : PendingAiDifficulty;
        // Advanced router gate: classify the AI deck's archetype once from its list.
        aiArchetype = OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.ClassifyArchetype(config.NorthDeckDef);
        aiTriedThisTurn.Clear();
        aiActivatedThisTurn.Clear();
        aiLastTurnSeen = -1;
        // Fresh match: stale animation bookkeeping from the previous game must not
        // suppress the new game's opening-deal / Life-deal animations.
        mulliganAnimShownKey = null;
        mulliganRedrawSeat = null;
        handDealAnimating = false;
        mulliganDealAnimating = 0;
        lastCardPoses.Clear();
        suppressMoveAnim.Clear();
        // South is the local player on this device — show their account/guest name (the profile
        // name, e.g. "Valren") rather than a generic "Player 1", which is only the last-resort
        // fallback when no account or guest name is available.
        southDisplayName = AccountManager.CurrentUsername ?? AccountManager.CachedUsername
            ?? AccountManager.GuestDisplayName ?? "Player 1";
        northDisplayName = aiSeat == null ? "Player 2"
            : aiDifficulty == "advanced" ? "Advanced Bot"
            : aiDifficulty == "intermediate" ? "Intermediate Bot"
            : "Beginner Bot";
        state = GameEngine.CreateMatch(config);
        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);
        finishedResultText = null;
        matchResultHidden = false;
        BlitzInit(PendingBlitzConfig);   // timed-match clocks (null/Standard = untimed)
        Render();
    }

    private void Dispatch(GameCommand command)
    {
        if (isReplayMode) return; // read-only playback: ignore stray interactive input
        // Networked match: block acting as the other seat (e.g. a stray click reaching a
        // handler before UI catches up) rather than auditing every call site individually.
        if (isNetworked && !string.IsNullOrEmpty(command.Seat) && command.Seat != localSeat) return;
        // Sandbox: snapshot before each engine command so attacks/plays/end-turn are all undoable.
        if (isSandbox) PushUndo();
        state = GameEngine.ApplyCommand(state, command);
        // Blitz: grant increment after the engine commits a meaningful action (§5/§6/§7).
        if (BlitzActive) BlitzOnCommandApplied(command, true);
        if (command.Type == "declareAttack" && state.Battle != null) PlayAttackSfx();
        commandElapsedSeconds.Add(Time.realtimeSinceStartup - matchStartRealtime);
        NormalizeSelection();
        // Sandbox / restored-position games never record stats/replays — they're scratch/hypothetical.
        if (state.Status == "finished" && !replaySaved && !isSandbox && !isRestoredGame)
        {
            replaySaved = true;
            SaveFinishedMatchRecords();
        }
        // Give the AI a visible beat before it answers a freshly-declared attack. When the bot has no
        // blocker the engine auto-skips the block step, so its FIRST response is the counter — which
        // otherwise fires on the very next frame (aiNextActionAt already elapsed), snapping the power
        // up the instant the attack lands so the pre-counter matchup is never seen. Pushing the timer
        // out here makes the counter read as a distinct action ("6k into 6k", pause, then the counter).
        if (aiSeat != null && state.Battle != null && state.Battle.TargetSeat == aiSeat
            && (state.Battle.Step == "block" || state.Battle.Step == "counter"))
            aiNextActionAt = Mathf.Max(aiNextActionAt, Time.unscaledTime + 1.5f);
        if (isNetworked) MatchNetworkSync.SendCommand(command);
        Render();
    }

    // ── Replay playback ──────────────────────────────────────────────────────
    // A finished match is fully reproducible from {Seed, deck ids, CommandHistory}
    // (see ReplayStore.cs), so "scrubbing" to any point just resimulates from a
    // fresh CreateMatch up to that command index through the same ApplyCommand
    // path live play uses — no separate undo/snapshot machinery needed.

    private void EnterReplayMode(ReplayRecord record)
    {
        isReplayMode = true;
        loadedReplay = record;
        var config = new MatchConfig { Seed = record.Seed };
        if (!string.IsNullOrEmpty(record.SouthDeckId))
        {
            var def = BuildDeckDef(DeckStore.Get(record.SouthDeckId));
            if (def != null) config.SouthDeckDef = def;
        }
        if (!string.IsNullOrEmpty(record.NorthDeckId))
        {
            var def = BuildDeckDef(DeckStore.Get(record.NorthDeckId));
            if (def != null) config.NorthDeckDef = def;
        }
        currentMatchConfig = config;
        previewLockCard = null;
        ClearDonSelection(false);

        // One-time enrichment pass (turn/seat/phase/category/log-lines per command) — see
        // ReplayIndex.cs. Cheap: GameEngine.ApplyCommand is pure, I/O-free data mutation.
        replayActions = ReplayIndex.Build(record, config);
        replayTurnStartCursors = ReplayIndex.TurnStartCursors(replayActions);
        replayTurnBoundaries = ReplayIndex.TurnBoundaryCursors(replayActions);
        // Older replays (recorded before CommandElapsedSeconds existed) have no per-step timing
        // at all — every ElapsedSeconds reads 0, which made Real-Time mode play back instantly
        // (and, as a side effect, rebuilt the whole UI every single frame, eating clicks meant
        // for the transport buttons). See ReplayStepDelay's "realtime" case for the fallback.
        replayHasTimingData = replayActions.Exists(a => a.ElapsedSeconds > 0.01f);
        LoadReplaySettings();
        // Every replay opens paused, in Real-Time mode, at the very start — the user explicitly
        // asked for this over auto-play (a replay that starts moving before you've even oriented
        // yourself is disorienting). This overrides whatever mode was persisted from a previous
        // session; the persisted mode still applies once the user picks Play and starts cycling
        // modes themselves.
        replayMode = "realtime";
        replayStopAtCursor = null;

        ResimulateReplayTo(0);
        replayPlaying = false;
    }

    private void ResimulateReplayTo(int index)
    {
        if (loadedReplay == null) return;
        restoreExportNote = null;   // clear the "position copied" toast on any navigation
        replayCursor = Mathf.Clamp(index, 0, loadedReplay.CommandHistory.Count);
        state = GameEngine.CreateMatch(currentMatchConfig);
        for (int i = 0; i < replayCursor; i++)
            state = GameEngine.ApplyCommand(state, loadedReplay.CommandHistory[i].ToCommand());
        selectedId = null;
        selectedSeat = null;
        Render();
    }

    // ── Replay player driver (Phase 2) ──────────────────────────────────────
    // Everything below decides WHEN to call ResimulateReplayTo(replayCursor +/- 1) and never
    // bulk-jumps during auto-play — per the Phase 0 audit, the generic ghost-animation system
    // (CaptureAndAnimateCardMoves, unconditional in Render()) already animates a clean
    // single-step diff correctly; bulk jumps (e.g. straight to a turn boundary) produce a
    // chaotic simultaneous flock capped at 24 moves. Turn/attack/etc. "jumps" during active
    // playback are therefore implemented as fast single-stepping toward a stop-at cursor, not
    // an instant ResimulateReplayTo(target) — see ReplayNextTurn/ReplayJumpToNextCategory.
    // Pure seeking (drag the timeline, click Start/End) still bulk-jumps and snaps instantly,
    // which is the expected/normal feel for scrubbing in any video player.

    private void ReplayAutoAdvanceTick()
    {
        if (!replayPlaying || loadedReplay == null) return;
        int total = loadedReplay.CommandHistory.Count;
        if (replayCursor >= total) { replayPlaying = false; replayStopAtCursor = null; return; }
        if (replayStopAtCursor.HasValue && replayCursor >= replayStopAtCursor.Value)
        {
            replayPlaying = false;
            replayStopAtCursor = null;
            return;
        }
        if (Time.unscaledTime < replayNextAdvanceAt) return;

        float delay = ReplayStepDelay(replayCursor + 1);
        // "allow important animations to finish before advancing, unless instant/highly
        // accelerated" — gate on activeMoveGhosts only when the step delay itself is
        // meaningful; instant/4x+ modes never wait on animation.
        bool waitForAnim = delay > 0.05f && replaySpeedMultiplier < 4f;
        if (waitForAnim && activeMoveGhosts > 0) return;

        ReplayStepForward();
        replayNextAdvanceAt = Time.unscaledTime + delay;
    }

    private float ReplayStepDelay(int cursor)
    {
        float baseDelay;
        switch (replayMode)
        {
            case "realtime":
                if (replayHasTimingData)
                {
                    // No upper clamp — "Real-Time" means faithful to how long the actual match
                    // took, thinking time included. An artificial cap here was quietly speeding
                    // up every long pause, which defeated the mode's whole point.
                    baseDelay = Mathf.Max(0f, ReplayElapsedDelta(cursor));
                }
                else
                {
                    // No per-step timing recorded (older replay) — spread the match's actual
                    // recorded DurationSeconds evenly across its steps rather than playing
                    // instantly, which is both inaccurate and (by rebuilding the UI every
                    // frame) makes the transport buttons unresponsive.
                    int totalCmds = loadedReplay?.CommandHistory.Count ?? 0;
                    baseDelay = (loadedReplay != null && totalCmds > 0 && loadedReplay.DurationSeconds > 0)
                        ? (float)loadedReplay.DurationSeconds / totalCmds
                        : 1f;
                }
                break;
            case "turnByTurn":
                // Fast fixed pace used only while a turn-jump/manual multi-step play is in
                // flight — this mode doesn't auto-play on its own otherwise.
                baseDelay = 0.15f;
                break;
            default: // "actionByAction" — user-selectable delay between steps (absorbed "custom")
                baseDelay = replayCustomDelaySeconds;
                break;
        }
        return replaySpeedMultiplier > 0f ? baseDelay / replaySpeedMultiplier : 0f;
    }

    private float ReplayElapsedDelta(int cursor)
    {
        if (replayActions == null || cursor <= 0 || cursor > replayActions.Count) return 0.5f;
        float curT = replayActions[cursor - 1].ElapsedSeconds;
        float prevT = cursor >= 2 ? replayActions[cursor - 2].ElapsedSeconds : 0f;
        return Mathf.Max(0f, curT - prevT);
    }

    private void ReplayStepForward()
    {
        if (loadedReplay == null || replayCursor >= loadedReplay.CommandHistory.Count) { replayPlaying = false; return; }
        ResimulateReplayTo(replayCursor + 1);
    }

    private void ReplayStepBackward()
    {
        if (replayCursor <= 0) return;
        replayPlaying = false;
        ResimulateReplayTo(replayCursor - 1);
    }

    private void ReplayPlay()
    {
        if (loadedReplay == null) return;
        if (replayCursor >= loadedReplay.CommandHistory.Count) { ResimulateReplayTo(0); return; } // already re-renders
        replayPlaying = true;
        replayNextAdvanceAt = Time.unscaledTime;
        Render();
    }

    // Explicit Render() here (unlike the step/jump methods, which already re-render via
    // ResimulateReplayTo) — flipping replayPlaying alone doesn't touch the board, so without
    // this the Play/Pause button's own label and the status text wouldn't update until
    // something else happened to trigger a redraw, reading as "the button didn't do anything."
    private void ReplayPause() { replayPlaying = false; Render(); }

    private void ReplayTogglePlayPause()
    {
        if (replayPlaying) ReplayPause(); else ReplayPlay();
    }

    private void ReplayJumpToTurnStart(int turn)
    {
        if (replayTurnStartCursors != null && replayTurnStartCursors.TryGetValue(turn, out int c)) ResimulateReplayTo(c);
    }

    // "Next Turn"/"Prev Turn" PLAY through the intervening actions (fast, animated) rather
    // than snapping instantly, per spec ("Selecting Next Turn should play all actions from
    // the current turn and stop when the following player's turn begins").
    private void ReplayNextTurn()
    {
        if (replayTurnBoundaries == null || loadedReplay == null) return;
        int target = loadedReplay.CommandHistory.Count;
        foreach (var b in replayTurnBoundaries)
        {
            if (b > replayCursor) { target = b; break; }
        }
        replayMode = "turnByTurn";
        replayStopAtCursor = target;
        replayPlaying = true;
        replayNextAdvanceAt = Time.unscaledTime;
    }

    private void ReplayPrevTurn()
    {
        if (replayTurnBoundaries == null) return;
        int target = 0;
        for (int i = replayTurnBoundaries.Count - 1; i >= 0; i--)
        {
            if (replayTurnBoundaries[i] < replayCursor) { target = replayTurnBoundaries[i]; break; }
        }
        replayPlaying = false;
        replayStopAtCursor = null;
        ResimulateReplayTo(target);
    }

    // ── Turn-by-Turn mode's own controls ─────────────────────────────────────
    // Distinct from the standard action-oriented transport row (spec: turn-by-turn should let
    // you play a single turn, play through every remaining turn, and jump backward/forward
    // either one turn or five at a time) — see DrawReplayControlBar's turnByTurn branch.

    // Snap-jump by a fixed number of turns (used for the ±5 buttons — a "big" turn jump you
    // don't want to sit and watch animate, unlike the single-turn Prev/Next which play through).
    private void ReplayJumpTurnsRelative(int deltaTurns)
    {
        if (replayTurnStartCursors == null || replayTurnStartCursors.Count == 0 || state == null) return;
        var turns = new List<int>(replayTurnStartCursors.Keys);
        turns.Sort();
        int idx = turns.IndexOf(state.TurnNumber);
        if (idx < 0) idx = 0;
        int targetIdx = Mathf.Clamp(idx + deltaTurns, 0, turns.Count - 1);
        replayPlaying = false;
        replayStopAtCursor = null;
        ResimulateReplayTo(replayTurnStartCursors[turns[targetIdx]]);
    }

    // "Play out a single turn": jumps to the CURRENT turn's start (if not already there) and
    // plays through to the next turn boundary, then stops — the whole turn, not just whatever's
    // left of it.
    private void ReplayPlayCurrentTurn()
    {
        if (replayTurnStartCursors == null || loadedReplay == null || state == null) return;
        int turnStart = replayTurnStartCursors.TryGetValue(state.TurnNumber, out int c) ? c : replayCursor;
        if (replayCursor != turnStart) ResimulateReplayTo(turnStart);
        int target = loadedReplay.CommandHistory.Count;
        if (replayTurnBoundaries != null)
            foreach (var b in replayTurnBoundaries) { if (b > turnStart) { target = b; break; } }
        replayStopAtCursor = target;
        replayPlaying = true;
        replayNextAdvanceAt = Time.unscaledTime;
        Render();
    }

    // "Play through all turns": the main Play/Pause toggle in turn-by-turn mode — plays
    // continuously to the end rather than stopping at the next boundary (unlike Play-current-turn).
    private void ReplayPlayAll()
    {
        replayStopAtCursor = null;
        ReplayPlay();
    }

    // Instant jumps (used by the timeline's prev/next-attack, prev/next-card-played,
    // prev/next-major-change controls) — these snap rather than play-through, matching how a
    // scrub action feels in any video player.
    private void ReplayJumpToNextCategory(string category, bool majorOnly = false)
    {
        if (replayActions == null) return;
        for (int i = replayCursor; i < replayActions.Count; i++)
        {
            var a = replayActions[i];
            if (majorOnly ? MajorReplayCategories.Contains(a.Category) : a.Category == category)
            {
                replayPlaying = false;
                ResimulateReplayTo(a.Cursor);
                return;
            }
        }
    }

    private void ReplayJumpToPrevCategory(string category, bool majorOnly = false)
    {
        if (replayActions == null) return;
        for (int i = replayCursor - 2; i >= 0; i--)
        {
            var a = replayActions[i];
            if (majorOnly ? MajorReplayCategories.Contains(a.Category) : a.Category == category)
            {
                replayPlaying = false;
                ResimulateReplayTo(a.Cursor);
                return;
            }
        }
    }

    private void ExitReplayToMenu()
    {
        isReplayMode = false;
        loadedReplay = null;
        replayActions = null;
        replayTurnStartCursors = null;
        replayTurnBoundaries = null;
        replayPlaying = false;
        replayStopAtCursor = null;
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
        MainMenuManager.EnsureMenu();
    }

    // ── Networked match ──────────────────────────────────────────────────────
    // Both clients call GameEngine.CreateMatch with the same Seed (the host
    // generates it and sends it once via MatchNetworkSync.SendMatchStart), so both
    // start from an identical state without transmitting any state itself - only
    // the Seed, then each subsequent GameCommand as it's dispatched. See
    // MatchNetworkSync.cs and Dispatch() above for the send/receive halves.

    private void EnterNetworkedMatch(string seed, string seat)
    {
        isReplayMode = false;
        loadedReplay = null;
        replaySaved = false;
        matchStartRealtime = Time.realtimeSinceStartup;
        commandElapsedSeconds.Clear();
        isNetworked = true;
        isRankedMatch = PendingNetworkedRanked;   // only true for Ranked-queue matches
        PendingNetworkedRanked = false;
        networkedMode = PendingNetworkedMode;
        PendingNetworkedMode = null;
        isForgiveness = PendingNetworkedForgiveness;
        PendingNetworkedForgiveness = false;
        localSeat = seat;
        BlitzInit(PendingNetworkedBlitz);
        PendingNetworkedBlitz = null;
        // Capture now, while connected — these must survive a mid-match disconnect so a leave/
        // surrender can still be ranked-reported (see cachedRankedOppId).
        cachedRankedMatchId = LobbyManager.CurrentSession?.Id;
        cachedRankedOppId = LobbyManager.OpponentPlayerId();

        var config = new MatchConfig { Seed = seed };
        // Deck picks travel in the match-start payload (see MatchStartPayload) and
        // land here via the Pending fields. Both clients receive the same pair, so
        // both build identical matches. Null (either seat) = ST01/ST02 default.
        var southDef = PendingNetworkedSouthDeck?.ToDeckDef();
        var northDef = PendingNetworkedNorthDeck?.ToDeckDef();
        PendingNetworkedSouthDeck = null;
        PendingNetworkedNorthDeck = null;
        if (southDef != null) config.SouthDeckDef = southDef;
        if (northDef != null) config.NorthDeckDef = northDef;
        currentMatchConfig = config;
        state = GameEngine.CreateMatch(config);
        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);

        // Display names (set by MainMenuManager.LaunchNetworkedMatch; consumed here so a later
        // hotseat match can't inherit them).
        southDisplayName = string.IsNullOrEmpty(PendingSouthName) ? "Player 1" : PendingSouthName;
        northDisplayName = string.IsNullOrEmpty(PendingNorthName) ? "Player 2" : PendingNorthName;
        PendingSouthName = null;
        PendingNorthName = null;
        opponentLeft = false;
        finishedResultText = null;
        matchResultHidden = false;
        chatOpen = false;
        chatUnread = false;
        chatMessages.Clear();
        addFriendState = "idle";
        addFriendChecked = false;
        opponentPresence = null;
        presenceHoverCardId = null;
        presenceRaisedHandIndex = -1;

        MatchNetworkSync.CommandReceived -= OnNetworkCommandReceived;
        MatchNetworkSync.CommandReceived += OnNetworkCommandReceived;
        MatchNetworkSync.ChatReceived -= OnNetworkChatReceived;
        MatchNetworkSync.ChatReceived += OnNetworkChatReceived;
        MatchNetworkSync.PresenceReceived -= OnNetworkPresenceReceived;
        MatchNetworkSync.PresenceReceived += OnNetworkPresenceReceived;
        MatchNetworkSync.RematchRequested -= OnRematchRequested;
        MatchNetworkSync.RematchRequested += OnRematchRequested;
        MatchNetworkSync.RematchStartReceived -= OnRematchStartReceived;
        MatchNetworkSync.RematchStartReceived += OnRematchStartReceived;
        if (isForgiveness) SubscribeRewind();
        rewindWaiting = rewindPromptOpen = false;
        rewindNote = null;
        rematchLocalRequested = rematchPeerRequested = rematchStarting = false;
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientDisconnectCallback -= OnPeerDisconnected;
            nm.OnClientDisconnectCallback += OnPeerDisconnected;
        }
        Render();
    }

    // Netcode client disconnect during a live networked match: if the match isn't already
    // decided, the remaining player wins by forfeit. Purely a UI outcome — no command is
    // dispatched and no state is mutated; the match record is deliberately NOT saved
    // (SaveFinishedMatchRecords expects an engine-finished state).
    private void OnPeerDisconnected(ulong clientId)
    {
        if (!isNetworked || opponentLeft || isReplayMode) return;
        if (state == null || state.Status == "finished") return;
        // Treat the disconnect as the opponent conceding: finish the match with us as the winner and
        // RECORD it (history/stats + ranked report), so a mid-match leave is a real loss for them —
        // not just a cosmetic "you win". We derive this locally from the disconnect; the leaver's own
        // client independently records its loss (see ReturnToMenu), and the server's dual-report
        // reconciles the two. Record BEFORE flipping opponentLeft so SaveFinishedMatchRecords still
        // files the ranked report. This is also the fallback when a leaver's concede message doesn't
        // arrive before their teardown.
        state = GameEngine.ApplyCommand(state, new GameCommand { Type = "concede", Seat = OtherSeatLocal(localSeat) });
        if (!replaySaved) { replaySaved = true; SaveFinishedMatchRecords(); }
        opponentLeft = true;   // drives the "OPPONENT LEFT — YOU WIN!" overlay
        Render();
    }

    private void OnNetworkCommandReceived(GameCommand command)
    {
        if (!isNetworked || state == null) return;
        // Trigger privacy: when the DEFENDER (the remote player, from this client's point of
        // view) activates a life-card Trigger, capture which card it was BEFORE the command
        // consumes it, so the attacker can be shown the reveal in the right-side preview.
        string activatedTriggerCardId = null;
        if (command.Type == "useTrigger" && command.Seat != localSeat
            && state.Battle != null && state.Battle.RevealedLife != null)
            activatedTriggerCardId = state.Battle.RevealedLife.CardId;
        state = GameEngine.ApplyCommand(state, command);
        // Blitz: award the opponent's increment on their committed action too, so both clients
        // apply the same deterministic increments (decrement/flag-fall stay owner-authoritative).
        if (BlitzActive) BlitzOnCommandApplied(command, true);
        if (command.Type == "declareAttack" && state.Battle != null) PlayAttackSfx();
        commandElapsedSeconds.Add(Time.realtimeSinceStartup - matchStartRealtime);
        NormalizeSelection();
        if (state.Status == "finished" && !replaySaved)
        {
            replaySaved = true;
            SaveFinishedMatchRecords();
        }
        Render();
        // AFTER the defender chose to activate: show the attacker what triggered.
        if (activatedTriggerCardId != null) ShowPreviewById(activatedTriggerCardId);
    }

    // Runs once per finished match (guarded by replaySaved at both call sites —
    // local Dispatch and the networked command receiver). Writes the full local
    // replay first, then fires the compact account-level summary at Cloud Save.
    // The summary is built from the local player's perspective: south for solo
    // play (replays are already saved from the south viewpoint), localSeat for
    // networked matches — the same orientation ReplayStore's WinnerName implies.
    private void SaveFinishedMatchRecords()
    {
        int durationSeconds = Mathf.Max(0, Mathf.RoundToInt(Time.realtimeSinceStartup - matchStartRealtime));
        var record = ReplayStore.Save(state, currentMatchConfig, durationSeconds, commandElapsedSeconds);
        if (record == null) return;

        // Full per-turn state log (GameLogStore.cs) for ML training / puzzle extraction —
        // re-simulates the just-saved record's CommandHistory once, off the live game loop,
        // so this never adds per-turn overhead during play. Same id as the replay JSON so the
        // two can be cross-referenced. Category mirrors the Solo-portal difficulty toggle;
        // "networked" is a placeholder bucket until Ranked/Casual matchmaking actually ships
        // (both are still ModeStatus.Soon in MainMenuManager today).
        string aiLogCategory = aiSeat != null ? "soloAi-" + aiDifficulty
            : isNetworked ? "networked"
            : "soloSelf";
        // BottomSeat mirrors the property of the same logic elsewhere in this file: "south"
        // for hotseat/solo/versus-AI, localSeat for a networked match. myUsername is the
        // signed-in account (or guest label) actually playing on this client.
        string myUsername = AccountManager.CurrentUsername ?? AccountManager.CachedUsername ?? AccountManager.GuestDisplayName;
        string mySeat = isNetworked ? localSeat : "south";
        string southLogUsername, northLogUsername;
        if (mySeat == "south")
        {
            southLogUsername = myUsername;
            // Versus-self: the same local person plays both seats. Versus-AI: the other
            // seat is a bot, tag it with the bot's display name instead of a real username.
            // Networked: best-effort — no confirmed opponent-identity channel wired here yet.
            northLogUsername = aiSeat != null ? northDisplayName : (isNetworked ? northDisplayName : myUsername);
        }
        else
        {
            northLogUsername = myUsername;
            southLogUsername = isNetworked ? southDisplayName : myUsername;
        }
        GameLogStore.Export(state, currentMatchConfig, record, aiLogCategory, southLogUsername, northLogUsername);

        string youSeatForSummary = isNetworked && !string.IsNullOrEmpty(localSeat) ? localSeat : "south";
        var summary = MatchHistoryStore.BuildSummary(state, record, youSeatForSummary,
            DisplayName(OtherSeatLocal(youSeatForSummary)));
        // Result-screen text from the local player's perspective (win/loss only; an
        // ambiguous/simultaneous end falls back to a neutral "MATCH OVER").
        finishedResultText = summary?.result == "win" ? "YOU WIN!"
            : summary?.result == "loss" ? "YOU LOSE." : "MATCH OVER";
        // Fire-and-forget: SaveMatchAsync catches + logs its own failures (and
        // skips guests entirely), so match end can never be blocked by network.
        if (summary != null)
        {
            // Game type for history/stats: networked ranked/casual/custom, or solo ai/self.
            summary.mode = isNetworked
                ? (isRankedMatch ? "ranked" : (string.IsNullOrEmpty(networkedMode) ? "custom" : networkedMode))
                : (aiSeat != null ? "ai" : "self");
            _ = MatchHistoryStore.SaveMatchAsync(summary);
            // Lifetime + seasonal aggregates (StatsStore.cs). Also fire-and-forget:
            // stats failures log and skip, they never block or break match end.
            _ = StatsStore.RecordMatchAsync(summary);
            // Bounty ranked (RankedStore.cs): the authoritative rating lives on the
            // server (Deploy/ranked-worker). Report our half of a finished PvP match
            // — the server moves ratings only when both players' reports agree, so a
            // lone client can't forge a result. Solo/bot games don't count. Same
            // fire-and-forget contract; never blocks match end.
            // A surrender / leave-to-menu is now a real, reportable result (the old !opponentLeft
            // guard is gone): both clients reach the same finished state via the concede command, so
            // both file their halves. Prefer the ids cached at match start — after a mid-match
            // disconnect the live session lookup is empty.
            if (isNetworked && isRankedMatch)
            {
                string rankedMatchId = !string.IsNullOrEmpty(cachedRankedMatchId) ? cachedRankedMatchId : LobbyManager.CurrentSession?.Id;
                string rankedOppId = !string.IsNullOrEmpty(cachedRankedOppId) ? cachedRankedOppId : LobbyManager.OpponentPlayerId();
                if (!string.IsNullOrEmpty(rankedMatchId) && !string.IsNullOrEmpty(rankedOppId))
                    _ = RankedStore.ReportMatchAsync(rankedMatchId, rankedOppId, summary.result == "win");
            }
        }
    }

    private void NormalizeSelection()
    {
        if (state == null) return;
        ReconcileDonGroups();
        NormalizeDonSelection();

        // Clear trash viewer when a blocking state appears.
        if (trashViewSeat != null && (state.Battle != null || state.PendingEffects.Count > 0 || state.DeckLook != null || state.ActiveChoice != null || state.PendingCharReplace != null))
            trashViewSeat = null;

        if (string.IsNullOrEmpty(selectedId)) return;

        // Hand-card selection (selectedSeat has "-hand" suffix).
        if (selectedSeat != null && selectedSeat.EndsWith("-hand"))
        {
            var hSeat = selectedSeat.Replace("-hand", "");
            bool valid = state.Phase == "main" && state.Battle == null && state.PendingEffects.Count == 0
                && state.ActiveSeat == hSeat
                && state.Players.TryGetValue(hSeat, out var hp)
                && hp.Hand.Any(c => c.InstanceId == selectedId);
            if (!valid) { selectedId = null; selectedSeat = null; }
            return;
        }

        // Board-card selection.
        if (state.Battle != null || selectedSeat != state.ActiveSeat || FindAny(selectedSeat, selectedId) == null)
        {
            selectedId = null;
            selectedSeat = null;
        }
    }

    private void NormalizeDonSelection()
    {
        if (string.IsNullOrEmpty(selectedDonSeat) || selectedDonIds.Count == 0) return;
        if (state == null || state.Status != "active" || state.Phase != "main" || state.Battle != null ||
            state.PendingEffects.Count > 0 || state.DeckLook != null || state.ActiveChoice != null ||
            state.PendingCharReplace != null ||
            state.ActiveSeat != selectedDonSeat || !state.Players.TryGetValue(selectedDonSeat, out var player))
        {
            ClearDonSelection(false);
            return;
        }

        var activeIds = new HashSet<string>(player.CostArea.Where(d => !d.Rested).Select(d => d.InstanceId));
        selectedDonIds.RemoveAll(id => !activeIds.Contains(id));
        if (selectedDonIds.Count == 0) ClearDonSelection(false);
    }

    private bool CanSelectDon(string seat, DonInstance don)
    {
        return state != null && don != null && !don.Rested &&
            state.Status == "active" && state.Phase == "main" && state.Battle == null &&
            state.PendingEffects.Count == 0 && state.DeckLook == null && state.ActiveChoice == null &&
            state.ActiveSeat == seat && state.Players.ContainsKey(seat);
    }

    private bool IsDonSelected(string instanceId)
    {
        return !string.IsNullOrEmpty(instanceId) && selectedDonIds.Contains(instanceId);
    }

    // True while THIS seat is mid-way through paying a "DON!! −N" cost by clicking DON!! (so both
    // cost-area and attached DON!! should be highlighted + clickable to return).
    private bool DonMinusPaymentActive(string seat)
    {
        if (state == null || state.PendingEffects.Count == 0) return false;
        var pe = state.PendingEffects[0];
        return pe.DonPaymentRemaining > 0 && pe.Seat == seat && (!isNetworked || seat == localSeat);
    }

    // True while THIS seat is choosing which rested DON!! to GIVE to their Leader (Nami-class
    // "Give up to N rested DON!! card to your Leader"). Rested cost-area DON!! glow + are clickable.
    private bool DonGivePickActive(string seat)
    {
        if (state == null || state.PendingEffects.Count == 0) return false;
        var pe = state.PendingEffects[0];
        if (pe.Seat != seat || (isNetworked && seat != localSeat)) return false;
        if (aiSeat != null && seat == aiSeat) return false;
        string t = pe.Text ?? "";
        return t.IndexOf("rested DON!!", System.StringComparison.OrdinalIgnoreCase) >= 0
            && t.IndexOf("Give", System.StringComparison.OrdinalIgnoreCase) >= 0
            && t.IndexOf("Leader", System.StringComparison.OrdinalIgnoreCase) >= 0
            && t.IndexOf("Characters", System.StringComparison.OrdinalIgnoreCase) < 0;
    }

    // Return a specific ATTACHED DON!! to the deck as one step of a DON!! −N payment (the engine
    // detaches it — attached DON!! on your field are valid targets for the return).
    private void ReturnAttachedDon(string seat, string donId)
    {
        if (!DonMinusPaymentActive(seat) || string.IsNullOrEmpty(donId)) return;
        var pe = state.PendingEffects[0];
        Dispatch(new GameCommand { Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId, Target = donId });
    }

    // First attached DON!! id on a player's field (Leader, then Characters, then Stage), or null —
    // used by the AI to pay a DON!! −N cost from attached DON!! when the cost area is empty.
    private static string FirstAttachedDonId(PlayerState p)
    {
        if (p.Leader?.AttachedDonIds != null && p.Leader.AttachedDonIds.Count > 0) return p.Leader.AttachedDonIds[0];
        foreach (var c in p.CharacterArea)
            if (c?.AttachedDonIds != null && c.AttachedDonIds.Count > 0) return c.AttachedDonIds[0];
        if (p.Stage?.AttachedDonIds != null && p.Stage.AttachedDonIds.Count > 0) return p.Stage.AttachedDonIds[0];
        return null;
    }

    private void SelectDon(string seat, string instanceId, int index, bool shift, bool render = true)
    {
        if (state == null || string.IsNullOrEmpty(seat) || !state.Players.TryGetValue(seat, out var player)) return;
        if (index < 0 || index >= player.CostArea.Count) return;
        var don = player.CostArea[index];
        if (don == null || don.InstanceId != instanceId) return;

        // A "DON!! -N" cost is being paid one DON!! at a time — route this click there
        // (the player chooses active vs rested DON!!, which matters for the rest of the turn).
        if (state.PendingEffects.Count > 0)
        {
            var peDon = state.PendingEffects[0];
            if (peDon.DonPaymentRemaining > 0 && peDon.Seat == seat)
            {
                Dispatch(new GameCommand { Type = "resolveEffect", Seat = peDon.Seat, EffectId = peDon.EffectId, Target = instanceId });
                return;
            }
            // A "give rested DON!! to your Leader" effect — clicking a rested DON!! gives THAT one.
            if (don.Rested && DonGivePickActive(seat))
            {
                Dispatch(new GameCommand { Type = "resolveEffect", Seat = peDon.Seat, EffectId = peDon.EffectId, Target = instanceId });
                return;
            }
        }

        if (!CanSelectDon(seat, don)) return;

        selectedId = null;
        selectedSeat = null;
        trashViewSeat = null;

        if (selectedDonSeat != seat)
        {
            selectedDonSeat = seat;
            selectedDonAnchorIndex = -1;
            selectedDonIds.Clear();
        }

        if (shift && selectedDonAnchorIndex >= 0)
        {
            selectedDonIds.Clear();
            int start = Mathf.Min(selectedDonAnchorIndex, index);
            int end = Mathf.Max(selectedDonAnchorIndex, index);
            for (int i = start; i <= end && i < player.CostArea.Count; i++)
            {
                var rangeDon = player.CostArea[i];
                if (CanSelectDon(seat, rangeDon) && !selectedDonIds.Contains(rangeDon.InstanceId))
                    selectedDonIds.Add(rangeDon.InstanceId);
            }
        }
        else if (selectedDonIds.Contains(instanceId))
        {
            selectedDonIds.Remove(instanceId);
            if (selectedDonIds.Count == 0)
            {
                selectedDonSeat = null;
                selectedDonAnchorIndex = -1;
            }
        }
        else
        {
            selectedDonIds.Add(instanceId);
            selectedDonAnchorIndex = index;
        }

        if (render) Render();
    }

    private void EnsureDonSelectedForDrag(string seat, string instanceId, int index)
    {
        if (string.IsNullOrEmpty(selectedDonSeat) || selectedDonSeat != seat || !selectedDonIds.Contains(instanceId))
        {
            selectedDonSeat = null;
            selectedDonIds.Clear();
            selectedDonAnchorIndex = -1;
            SelectDon(seat, instanceId, index, false, false);
        }
    }

    private void ClearDonSelection(bool render = true)
    {
        selectedDonSeat = null;
        selectedDonAnchorIndex = -1;
        selectedDonIds.Clear();
        if (render) Render();
    }

    private void CancelDonSelectionFromBoardClick()
    {
        bool changed = false;
        if (selectedDonIds.Count > 0) { ClearDonSelection(false); changed = true; }
        // Clicking empty board space also deselects a selected board card (e.g. a character you
        // clicked to activate its ability) — clicking off it cancels the selection.
        if (!string.IsNullOrEmpty(selectedId) || !string.IsNullOrEmpty(selectedSeat))
        {
            selectedId = null; selectedSeat = null; changed = true;
        }
        if (changed) Render();
    }

    // ============ DON grouping (in-place: right-click to split, drag to move) ============
    // Purely visual. Active DON show in the cost area as gap-separated groups whose spacing auto-
    // justifies to fill the region (even 10 groups of 1). RIGHT-CLICK a DON to split its group there
    // (or, on a group's first DON, merge it back into the previous group). DRAG a DON onto a DON in a
    // different group to move it across. donGroupSizes partitions the ACTIVE DON in order; engine untouched.
    private bool DonPlanningAllowed()
    {
        return state != null && state.Status == "active" && state.Phase == "main"
            && state.Battle == null && state.PendingEffects.Count == 0
            && state.DeckLook == null && state.ActiveChoice == null
            && !string.IsNullOrEmpty(state.ActiveSeat) && state.Players.ContainsKey(state.ActiveSeat);
    }

    private int ActiveSeatActiveDon()
    {
        return DonPlanningAllowed() ? GameEngine.ActiveDonCount(state.Players[state.ActiveSeat]) : 0;
    }

    private void EnsureGroupSizes()
    {
        int active = ActiveSeatActiveDon();
        if (donGroupSizes.Count == 0 && active > 0) donGroupSizes.Add(active);
    }

    private void NormalizeGroupSizes()
    {
        int active = ActiveSeatActiveDon();
        if (donGroupSizes.Count == 0) return;
        int sum = 0;
        for (int i = 0; i < donGroupSizes.Count; i++) sum += donGroupSizes[i];
        if (sum > active)
        {
            int over = sum - active;
            for (int i = donGroupSizes.Count - 1; i >= 0 && over > 0; i--)
            {
                int take = Mathf.Min(donGroupSizes[i], over);
                donGroupSizes[i] -= take;
                over -= take;
            }
        }
        else if (sum < active)
        {
            donGroupSizes[donGroupSizes.Count - 1] += (active - sum);
        }
        for (int i = donGroupSizes.Count - 1; i >= 0; i--) if (donGroupSizes[i] <= 0) donGroupSizes.RemoveAt(i);
    }

    private void ReconcileDonGroups()
    {
        if (!DonPlanningAllowed())
        {
            if (donGroupSizes.Count > 0) donGroupSizes.Clear();
            donGroupLastActive = -1;
            return;
        }
        if (state.TurnNumber != donGroupTurn || state.ActiveSeat != donGroupSeat)
        {
            donGroupSizes.Clear();
            donGroupTurn = state.TurnNumber;
            donGroupSeat = state.ActiveSeat;
            donGroupLastActive = -1;
        }
        NormalizeGroupSizes();
        donGroupLastActive = ActiveSeatActiveDon();
    }

    private int ActiveIndexOfDon(PlayerState p, string instanceId)
    {
        int k = 0;
        for (int i = 0; i < p.CostArea.Count; i++)
        {
            if (p.CostArea[i].Rested) continue;
            if (p.CostArea[i].InstanceId == instanceId) return k;
            k++;
        }
        return -1;
    }

    private int GroupIndexOfActive(int activeIndex)
    {
        int acc = 0;
        for (int g = 0; g < donGroupSizes.Count; g++)
        {
            if (activeIndex < acc + donGroupSizes[g]) return g;
            acc += donGroupSizes[g];
        }
        return donGroupSizes.Count - 1;
    }

    // Right-click: split this DON's group here, or (if it's a group's first DON) merge into the previous.
    private void RightClickDon(string seat, string instanceId)
    {
        if (!DonPlanningAllowed() || seat != state.ActiveSeat) return;
        EnsureGroupSizes();
        NormalizeGroupSizes();
        int ai = ActiveIndexOfDon(state.Players[seat], instanceId);
        if (ai < 0) return;
        int acc = 0, g = -1, offset = 0;
        for (int gi = 0; gi < donGroupSizes.Count; gi++)
        {
            if (ai < acc + donGroupSizes[gi]) { g = gi; offset = ai - acc; break; }
            acc += donGroupSizes[gi];
        }
        if (g < 0) return;
        if (offset == 0)
        {
            if (g == 0) return;                              // first DON overall - nothing before to merge
            donGroupSizes[g - 1] += donGroupSizes[g];
            donGroupSizes.RemoveAt(g);
        }
        else
        {
            int rest = donGroupSizes[g] - offset;
            donGroupSizes[g] = offset;
            donGroupSizes.Insert(g + 1, rest);
        }
        donGroupTurn = state.TurnNumber;
        donGroupSeat = state.ActiveSeat;
        Render();
    }

    // Drag-drop a DON onto a DON in another group: that DON moves across (counts are fungible).
    private void MoveDonToGroup(string seat, string instanceId, int targetGroup)
    {
        if (!DonPlanningAllowed() || seat != state.ActiveSeat) return;
        EnsureGroupSizes();
        NormalizeGroupSizes();
        int ai = ActiveIndexOfDon(state.Players[seat], instanceId);
        if (ai < 0) return;
        int src = GroupIndexOfActive(ai);
        if (src < 0 || targetGroup < 0 || targetGroup >= donGroupSizes.Count || src == targetGroup) return;
        donGroupSizes[targetGroup] += 1;
        donGroupSizes[src] -= 1;
        if (donGroupSizes[src] <= 0) donGroupSizes.RemoveAt(src);
        donGroupTurn = state.TurnNumber;
        donGroupSeat = state.ActiveSeat;
        Render();
    }

    // Builds one cost-area DON with full hover/select/drag/right-click interactivity. groupTag >= 0
    // marks it as a drag-between-groups target (its group index).
    private void AddCostDon(RectTransform parent, DonInstance don, string seat, int origIndex, float x, float cardW, float cardH, bool inverted, int groupTag)
    {
        var holder = new GameObject("Fitted DON").AddComponent<RectTransform>();
        holder.SetParent(parent, false);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = new Vector2(cardW, cardH);
        holder.anchoredPosition = new Vector2(x, 0f);
        AddDonCardVisual(holder, don.Rested, true);
        if (IsDonSelected(don.InstanceId))
        {
            AddMysticalCardOutline(holder, true);
            AddOutline(holder.gameObject, new Color32(255, 214, 112, 230), 2.4f);
        }
        else if (state != null && state.PendingEffects.Count > 0
                 && state.PendingEffects[0].DonPaymentRemaining > 0
                 && state.PendingEffects[0].Seat == seat)
        {
            // Valid pick for a pending DON!! -N payment — highlight green.
            AddOutline(holder.gameObject, new Color32(96, 240, 150, 230), 2.6f);
        }
        else if (don.Rested && DonGivePickActive(seat))
        {
            // Valid pick for a "give rested DON!! to your Leader" effect — standard GREEN rim glow
            // (AddUsableGlow), matching every other valid target, not the gold selection outline.
            AddUsableGlow(holder);
            holder.SetAsLastSibling();
        }
        if (don.Rested) holder.localRotation = Quaternion.Euler(0, 0, inverted ? 270f : 90f);
        else if (inverted) holder.localRotation = Quaternion.Euler(0, 0, 180f);
        var catcher = PanelObject("DON Hover Catcher", holder, new Color(0, 0, 0, 0));
        Stretch(catcher, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        catcher.gameObject.AddComponent<DonHover>().Init(this);
        catcher.gameObject.AddComponent<DonSelector>().Init(this, canvas, seat, don.InstanceId, origIndex);
        donMoveRects[don.InstanceId] = holder;
        if (isNetworked)
        {
            presenceGlowRects[don.InstanceId] = holder;
            catcher.gameObject.AddComponent<ZoneHoverPresence>().Init(this, don.InstanceId);
        }
        if (groupTag >= 0) catcher.gameObject.AddComponent<DonGroupTag>().GroupIndex = groupTag;
    }

    // Grouped cost-area display from donGroupSizes: rested DON cluster, then one cluster per group,
    // gaps auto-justified so the groups fill the cost region (and shrink to fit when crowded).
    private void DrawGroupedDonRow(RectTransform parent, IList<DonInstance> donCards, string seat, bool inverted, IList<int> groupsOverride = null)
    {
        var groupSizes = groupsOverride ?? donGroupSizes;
        if (donCards == null || donCards.Count == 0) return;
        var rested = new List<(DonInstance don, int index)>();
        var active = new List<(DonInstance don, int index)>();
        for (int i = 0; i < donCards.Count; i++)
        {
            if (donCards[i].Rested) rested.Add((donCards[i], i));
            else active.Add((donCards[i], i));
        }
        var clusterCards = new List<List<(DonInstance don, int index)>>();
        var clusterTags = new List<int>();
        if (rested.Count > 0) { clusterCards.Add(rested); clusterTags.Add(-1); }
        int pos = 0;
        for (int g = 0; g < groupSizes.Count && pos < active.Count; g++)
        {
            int take = Mathf.Min(groupSizes[g], active.Count - pos);
            if (take <= 0) continue;
            var grp = new List<(DonInstance don, int index)>();
            for (int k = 0; k < take; k++) grp.Add(active[pos + k]);
            clusterCards.Add(grp);
            clusterTags.Add(g);
            pos += take;
        }
        if (pos < active.Count)
        {
            var grp = new List<(DonInstance don, int index)>();
            for (int k = pos; k < active.Count; k++) grp.Add(active[k]);
            clusterCards.Add(grp);
            clusterTags.Add(groupSizes.Count);
        }
        if (clusterCards.Count == 0) return;

        // DON at full board-card size (no height clamp to the inset DON-row band); the horizontal
        // fit-scale below still prevents spilling past the cost-area width.
        float cardW = boardCardSize.x > 1f ? boardCardSize.x : 70f;
        float cardH = boardCardSize.y > 1f ? boardCardSize.y : cardW / CardAspect;
        float availW = parent.rect.width > 1f ? parent.rect.width : (boardRoot != null ? boardRoot.rect.width * 0.55f : 600f);
        float intra = cardW * 0.34f;
        int nClusters = clusterCards.Count;
        float SumSpans()
        {
            float w = 0f;
            for (int c = 0; c < nClusters; c++) w += cardW + (clusterCards[c].Count - 1) * intra;
            return w;
        }
        // Groups sit TIGHT together — a fixed, small inter-group gap, with the whole cluster centered —
        // rather than spreading to fill the entire cost-area width the instant a 2nd group is made.
        float gap = nClusters > 1 ? cardW * 0.5f : 0f;
        float need = SumSpans() + gap * Mathf.Max(0, nClusters - 1);
        if (need > availW && need > 0f)
        {
            float scale = availW / need;
            cardW *= scale; cardH *= scale; intra *= scale; gap *= scale;
        }
        float sumSpans = SumSpans();
        float total = sumSpans + gap * Mathf.Max(0, nClusters - 1);
        float x = -total * 0.5f + cardW * 0.5f;
        for (int c = 0; c < nClusters; c++)
        {
            var grp = clusterCards[c];
            int tag = clusterTags[c];
            for (int k = 0; k < grp.Count; k++)
                AddCostDon(parent, grp[k].don, seat, grp[k].index, x + k * intra, cardW, cardH, inverted, tag);
            x += (grp.Count - 1) * intra + cardW + gap;
        }
    }

    private bool IsDonAttachTarget(string seat, CardInstance card)
    {
        if (state == null || card == null || string.IsNullOrEmpty(seat) || !state.Players.ContainsKey(seat)) return false;
        if (FindAny(seat, card.InstanceId) == null) return false;
        var def = GameEngine.GetCard(card);
        return def.Type == "leader" || def.Type == "character";
    }

    private bool CanAttachSelectedDonTo(string seat, CardInstance card)
    {
        if (selectedDonIds.Count == 0 || selectedDonSeat != seat || !IsDonAttachTarget(seat, card)) return false;
        if (state == null || state.Status != "active" || state.Phase != "main" || state.Battle != null ||
            state.PendingEffects.Count > 0 || state.DeckLook != null || state.ActiveChoice != null ||
            state.PendingCharReplace != null ||
            state.ActiveSeat != seat) return false;
        return true;
    }

    private bool AttachSelectedDonTo(string seat, CardInstance card)
    {
        if (!CanAttachSelectedDonTo(seat, card)) return false;
        var donIds = selectedDonIds.ToList();
        ClearDonSelection(false);
        foreach (var dId in donIds) suppressMoveAnim.Add(dId);   // player placed these themselves
        Dispatch(new GameCommand
        {
            Type = "attachDon",
            Seat = seat,
            Target = card.InstanceId,
            Amount = donIds.Count,
            DonInstanceIds = donIds,
        });
        return true;
    }

    private void EnsureEventSystem()
    {
        foreach (var oldModule in Object.FindObjectsByType<StandaloneInputModule>(FindObjectsInactive.Include))
            Destroy(oldModule);

        var existing = Object.FindAnyObjectByType<EventSystem>();
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildShell()
    {
        var canvasGo = new GameObject("OPTCG Canvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;   // snap UI vertices to whole pixels - sharpens small label text
        canvasGo.AddComponent<GraphicRaycaster>();

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 0.5f;
        // Rasterize dynamic-font text (and other generated UI bitmaps) at higher resolution so the
        // small labels render crisp instead of blurry.
        scaler.dynamicPixelsPerUnit = 4f;

        boardRoot = PanelObject("Board", canvas.transform, new Color32(13, 16, 23, 255));
        Stretch(boardRoot, new Vector2(0, 0), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

        // Side menu columns are translucent so the full-width coloured board shows through behind them
        // ("fused into the board"). Right = actions, left = event log; the play space sits centred between.
        sideRoot = PanelObject("Side Panel", canvas.transform, MenuBg);
        Stretch(sideRoot, new Vector2(0.83f, 0), Vector2.one, Vector2.zero, Vector2.zero);

        leftRoot = PanelObject("Left Panel", canvas.transform, MenuBg);
        Stretch(leftRoot, Vector2.zero, new Vector2(0.17f, 1), Vector2.zero, Vector2.zero);

        previewRoot = PanelObject("Card Preview", canvas.transform, new Color(0, 0, 0, 0));
        Stretch(previewRoot, new Vector2(0.76f, 0.195f), new Vector2(0.998f, 0.805f), Vector2.zero, Vector2.zero);
        var previewGroup = previewRoot.gameObject.AddComponent<CanvasGroup>();
        previewGroup.blocksRaycasts = false;
        previewGroup.interactable = false;
        previewRoot.gameObject.SetActive(false);

        // Card-shaped region so both the art and its glow keep the real card aspect.
        // (Otherwise the art letterboxes inside a wider mask -> square corners, and the
        // glow hugs the holder rect instead of the card.)
        const float previewCardAspect = 168f / 235f;   // ~0.715, standard card proportion
        var previewCardRegion = PanelObject("Preview Card Region", previewRoot, new Color(0, 0, 0, 0));
        Stretch(previewCardRegion, new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.98f), Vector2.zero, Vector2.zero);

        // Glow behind the card, fitted to the card aspect.
        var previewGlowHolder = PanelObject("Preview Glow Holder", previewCardRegion, new Color(0, 0, 0, 0));
        var previewGlowFitter = previewGlowHolder.gameObject.AddComponent<AspectRatioFitter>();
        previewGlowFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        previewGlowFitter.aspectRatio = previewCardAspect;
        AddMysticalCardOutline(previewGlowHolder, true);

        // Card art on top, fitted to the same aspect so its rounded mask lands on the art edges.
        var previewMask = RoundedCardVisual("Preview Image", previewCardRegion, null, out previewImage);
        previewImage.raycastTarget = false;
        previewImage.preserveAspect = false;
        var previewImageFitter = previewMask.gameObject.AddComponent<AspectRatioFitter>();
        previewImageFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        previewImageFitter.aspectRatio = previewCardAspect;
        previewTitle = TextObject("Preview Title", previewRoot, "", 18, Ink, TextAnchor.MiddleCenter);
        Stretch(previewTitle.rectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.15f), Vector2.zero, Vector2.zero);

        // Blocker hover-info popout. Mirrors the CARD preview's geometry EXACTLY (same outer rect, same card
        // aspect, same rim-glow-on-an-inset-holder) so switching between hovering a card and a shield never
        // makes the box jump horizontally and the gold glow stays on-screen. A warm-brown vertical gradient
        // (board top-down wash styling) fills the box; NO hard border (it would read as an edge under the glow).
        blockerPreviewRoot = PanelObject("Blocker Preview", canvas.transform, new Color(0, 0, 0, 0));
        Stretch(blockerPreviewRoot, new Vector2(0.76f, 0.195f), new Vector2(0.998f, 0.805f), Vector2.zero, Vector2.zero);
        var bpGroup = blockerPreviewRoot.gameObject.AddComponent<CanvasGroup>();
        bpGroup.blocksRaycasts = false; bpGroup.interactable = false;
        blockerPreviewRoot.gameObject.SetActive(false);

        var bpRegion = PanelObject("Blocker Preview Region", blockerPreviewRoot, new Color(0, 0, 0, 0));
        Stretch(bpRegion, new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.98f), Vector2.zero, Vector2.zero);
        const float bpAspect = 168f / 235f;   // standard card proportion, same as the card preview

        // Gold rim glow on a card-aspect holder — contained on-screen exactly like the card preview's glow.
        var bpGlowHolder = PanelObject("Blocker Preview Glow", bpRegion, new Color(0, 0, 0, 0));
        var bpGlowFit = bpGlowHolder.gameObject.AddComponent<AspectRatioFitter>();
        bpGlowFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        bpGlowFit.aspectRatio = bpAspect;
        AddMysticalCardOutline(bpGlowHolder, true);

        // Card-shaped box: warm-brown base + a vertical gradient (lighter at top → base at bottom).
        var bpBox = PanelObject("Blocker Preview Box", bpRegion, (Color)new Color32(44, 32, 20, 255));
        var bpBoxFit = bpBox.gameObject.AddComponent<AspectRatioFitter>();
        bpBoxFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        bpBoxFit.aspectRatio = bpAspect;
        RoundBig(bpBox);
        var bpGrad = new GameObject("Blocker Preview Gradient").AddComponent<RectTransform>();
        bpGrad.SetParent(bpBox, false);
        Stretch(bpGrad, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));  // inset so square corners tuck inside the rounding
        var bpGradImg = bpGrad.gameObject.AddComponent<RawImage>();
        bpGradImg.texture = GetVGradientTexture();
        bpGradImg.color = new Color32(96, 68, 38, 150);   // warm brown, fades toward the darker base at the bottom
        bpGradImg.raycastTarget = false;

        var bpShield = PanelObject("Blocker Preview Shield", bpBox, new Color(0, 0, 0, 0));
        Stretch(bpShield, new Vector2(0.26f, 0.56f), new Vector2(0.74f, 0.88f), Vector2.zero, Vector2.zero);
        var bpImg = bpShield.GetComponent<Image>();
        var bpArt = LoadBlockerShieldSprite();
        if (bpArt != null) { bpImg.sprite = bpArt; bpImg.preserveAspect = true; bpImg.color = Color.white; }
        else { bpImg.color = new Color(0.16f, 0.52f, 0.85f, 0.95f); Round(bpShield); }
        bpImg.raycastTarget = false;

        var bpTitle = TextObject("Blocker Preview Title", bpBox, "Blocker", 20, new Color(0.95f, 0.78f, 0.50f, 1f), TextAnchor.MiddleCenter, titleFont);
        Stretch(bpTitle.rectTransform, new Vector2(0.06f, 0.45f), new Vector2(0.94f, 0.55f), Vector2.zero, Vector2.zero);
        bpTitle.raycastTarget = false;
        var bpDesc = TextObject("Blocker Preview Desc", bpBox,
            "After your opponent declares an attack, you may rest this Character to make it the new target of that attack.",
            14, Ink, TextAnchor.UpperCenter);
        Stretch(bpDesc.rectTransform, new Vector2(0.09f, 0.14f), new Vector2(0.91f, 0.44f), Vector2.zero, Vector2.zero);
        bpDesc.raycastTarget = false;
        bpDesc.raycastTarget = false;
    }

    private void Render()
    {
        ClearDragGhosts();
        HideHandHoverPreview();
        // Also drop the floating board preview: a click that plays/moves a card rebuilds the board and
        // destroys the hovered card's CardHover before its OnPointerExit fires, which would otherwise
        // leave the preview stuck up. Re-hovering re-shows it.
        HidePreview();
        HideBlockerPreview();
        Clear(boardRoot);
        Clear(sideRoot);
        Clear(leftRoot);
        // The result peek chip lives on the canvas (not boardRoot), so Clear() above does NOT touch it.
        // Tear it down every Render and let the result block below recreate it only while it's needed —
        // otherwise it both stacks a fresh copy each frame and lingers on screen after a re-match.
        if (resultPeekChip != null) { Destroy(resultPeekChip.gameObject); resultPeekChip = null; }
        handCardRects.Clear();
        boardDeckPileRects.Clear();
        cardTargetRects.Clear();
        presenceGlowRects.Clear();
        donMoveRects.Clear();
        moveZoneAnchors.Clear();
        lifeMoveRects.Clear();
        lastPresenceHover = lastPresenceRaised = null;   // rebuilt board → re-apply presence once
        // Presence visuals live on board objects that Clear() just destroyed.
        opponentPresenceGlow = null;
        opponentHandSlots.Clear();
        opponentHandSlotHomes.Clear();
        chatInputField = null;
        HideHoverTargetArrow();
        // Self-heal: if a drag's OnDrop (e.g. playing a character) triggers this Render() and
        // destroys the dragged object before its OnEndDrag runs, Unity's EventSystem sees the
        // dragged GameObject as already-destroyed and silently skips calling OnEndDrag at all -
        // so isDraggingHandCard would otherwise get stuck true forever, suppressing every card's
        // hover preview. A full Render() means no drag can still legitimately be in progress.
        isDraggingHandCard = false;
        if (state == null) return;

        // Uniform board card size, derived deterministically from the CanvasScaler reference and
        // the screen so it is identical on the very first render (coin-flip screen) and every render
        // after. Previously this read boardRoot.rect.height before the canvas had been scaled on the
        // first frame, so cards loaded oversized and then snapped smaller once layout settled.
        var _sc = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
        float _refW = _sc != null ? _sc.referenceResolution.x : 1600f;
        float _refH = _sc != null ? _sc.referenceResolution.y : 900f;
        float _match = _sc != null ? _sc.matchWidthOrHeight : 0.5f;
        float _sw = Mathf.Max(1, Screen.width), _sh = Mathf.Max(1, Screen.height);
        float _scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(_sw / _refW, 2f), Mathf.Log(_sh / _refH, 2f), _match));
        float _canvasH = _sh / Mathf.Max(_scale, 0.0001f);
        float _ch = (_canvasH * 0.5f) * 0.28f;
        boardCardSize = new Vector2(_ch * CardAspect, _ch);

        DrawTableSurface();
        // Centred play column: the table/background fills the WHOLE window (DrawTableSurface, on
        // boardRoot), while the playmat + hands + zones live here, centred, leaving side margins for
        // menus/action UI later. Same internal layout, just shifted to centre.
        playRoot = PanelObject("Play Area", boardRoot, new Color(0, 0, 0, 0));
        // Replay mode reclaims the leftRoot/sideRoot columns (below: hidden and left un-drawn
        // during replay — their live-play content, action buttons/opponent chat/event log,
        // doesn't apply to read-only playback) for "more horizontal space" per the replay
        // spec's landscape-layout ask. First pass: same board geometry, just wider — a true
        // side-by-side player layout is a much larger, separately-scoped re-geometry of
        // DrawMatHalf's ~15 hand-tuned zone rectangles that needs visual iteration to get
        // right, not a blind rewrite (see the Phase 0 audit notes). Phase 4/5's new replay
        // chrome (control bar, action panel) will overlay this wider area as floating panels.
        var playBounds = isReplayMode ? new Vector2(0.02f, 0f) : new Vector2(0.17f, 0f);
        var playBoundsMax = isReplayMode ? new Vector2(0.98f, 1f) : new Vector2(0.83f, 1f);
        Stretch(playRoot, playBounds, playBoundsMax, Vector2.zero, Vector2.zero);
        var _praw = playRoot.GetComponent<Image>();
        if (_praw != null) _praw.raycastTarget = false;
        // RotateHalfForReplay/RotateFullForReplay/DrawExternalHand's replay branch all read
        // mat.rect/playRoot.rect/boardRoot.rect synchronously to size a rotated element. On the
        // very first Render() after the canvas is freshly created (entering a replay), those
        // rects can still reflect a stale/default layout for one frame — sizing everything
        // wrong until the next Render() self-corrects, which read as "the board is zoomed in,
        // then snaps to the right size." Forcing the layout to settle right here, before any of
        // that math runs, makes it correct from the first frame instead.
        if (IsReplayRotated) Canvas.ForceUpdateCanvases();
        DrawReferencePlaymat();
        DrawExternalHand(state.Players[TopSeat], TopSeat, true);
        DrawExternalHand(state.Players[BottomSeat], BottomSeat, false);
        // Battle status banner removed - the live power badges on the cards convey the same info now.
        // These are all "make a live decision" overlays (go first/second, keep/mulligan, pick a
        // searched card) — replay is observation-only, and the action banner + timeline already
        // narrate what was chosen at each step, so they're skipped entirely rather than shown
        // as inert (but still clickable-looking) modal dialogs. A likely source of the earlier
        // "pause button doesn't work" report too: these draw full-screen backdrops that can eat
        // clicks meant for whatever's underneath, including the control bar.
        if (!isReplayMode)
        {
            DrawCoinFlipOverlay();
            DrawMulliganOverlay();
            DrawDeckLookOverlay();
        }
        DrawTrashOverlay();
        DrawResolvedTargetingArrows();
        if (isReplayMode) DrawReplayActionOverlay();
        if (!isReplayMode)
        {
            DrawSidePanel();
            DrawLeftPanel();
        }
        leftRoot.gameObject.SetActive(!isReplayMode);
        sideRoot.gameObject.SetActive(!isReplayMode);
        if (isNetworked && !isReplayMode) DrawMatchChatPanel();
        if (isReplayMode) DrawReplayControlBar();
        if (isReplayMode) DrawReplayActionPanel();
        if (opponentLeft) DrawOpponentLeftOverlay();
        else if (ResultScreenActive())
        {
            if (matchResultHidden) DrawMatchResultPeekChip();
            else DrawMatchResultOverlay();
        }
        // Sandbox tools draw last so the editor button + panels overlay the normal board.
        if (isSandbox) DrawSandbox();
        // Restore-from-code POV picker overlays everything until the player chooses a perspective.
        if (restorePickPending) DrawRestorePovOverlay();
        // Forgiveness-mode rewind control + approve/decline overlays (networked custom lobbies).
        DrawRewind();
        // Blitz/Ranked timed-match clock HUD.
        DrawBlitz();
        // Re-apply the opponent's hover/raised-hand presence to the freshly built board.
        ApplyOpponentPresence();
        // Snapshot positions and fly ghosts for any card whose zone changed this render.
        CaptureAndAnimateCardMoves();
        MaybeShowTurnBanner();
        // (Roaming rim sparkles removed — the push/pull bar's active-half glow is the turn cue now.)
    }

    // Subtle ambient sparks in the ACTIVE player's board colour — cool blue for
    // north, teal for south. A handful of tiny star sparks drift slowly around that
    // player's half of the playmat, twinkling in and out. Quiet, decorative, and
    // drawn UNDER hands/cards so it never obstructs anything.
    private void DrawTurnFuse()
    {
        if (state == null || state.Status != "active" || isReplayMode) return;
        var half = state.ActiveSeat == TopSeat ? northHalfRect : southHalfRect;
        if (half == null || playRoot == null) return;
        var accent = state.ActiveSeat == TopSeat
            ? new Color(0.42f, 0.62f, 1f)      // north: blue
            : new Color(0.22f, 0.88f, 0.76f);  // south: teal
        var root = new GameObject("Turn Sparks").AddComponent<RectTransform>();
        root.SetParent(playRoot, false);
        Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        // Slot the sparks directly above the playmat in draw order so hands, cards and
        // overlays (later siblings of the mat) all render ON TOP of them.
        var matAncestor = half;
        while (matAncestor.parent != null && matAncestor.parent != playRoot)
            matAncestor = matAncestor.parent as RectTransform;
        if (matAncestor != null && matAncestor.parent == playRoot)
            root.SetSiblingIndex(matAncestor.GetSiblingIndex() + 1);
        var grp = root.gameObject.AddComponent<CanvasGroup>();
        grp.blocksRaycasts = false;
        grp.interactable = false;
        root.gameObject.AddComponent<TurnFuse>().Init(this, half, accent);
    }

    private sealed class TurnFuse : MonoBehaviour
    {
        // A loose swarm of particles progressing around the RIM of the active player's
        // half. New particles twinkle INTO existence just around the moving front,
        // linger roughly in place (tiny drift), and twinkle OUT at the rear of the
        // trail — reading as a soft comet of dust working its way around the board.
        private GameManager mgr;
        private RectTransform half;
        private Color accent;
        private const int Pool = 72;
        private readonly RectTransform[] parts = new RectTransform[Pool];
        private readonly Image[] partImgs = new Image[Pool];
        private readonly Vector2[] drift = new Vector2[Pool];
        private readonly float[] age = new float[Pool];
        private readonly float[] life = new float[Pool];
        private readonly float[] spin = new float[Pool];
        private readonly float[] size = new float[Pool];
        private readonly float[] seed = new float[Pool];
        private readonly float[] baseAng = new float[Pool];   // rim tangent at spawn (streaks align with travel)
        private float nextSpawnAt;
        private const float LapSeconds = 30f;
        private const float Inset = 5f;

        public void Init(GameManager manager, RectTransform halfRect, Color accentCol)
        {
            mgr = manager;
            half = halfRect;
            accent = accentCol;
            var starSprite = mgr.LoadFxSprite("fuse_spark") ?? mgr.GetSoftDotSprite();
            for (int i = 0; i < Pool; i++)
            {
                var img = mgr.ImageObject("Rim Particle", transform, starSprite);
                img.raycastTarget = false;
                img.color = new Color(0f, 0f, 0f, 0f);
                parts[i] = img.rectTransform;
                partImgs[i] = img;
                life[i] = 0f;   // inactive until spawned at the moving front
            }
            // Pre-seed the trail behind the current front so a fresh render doesn't
            // start with an empty rim while the swarm rebuilds.
            for (int i = 0; i < Pool / 2; i++)
            {
                int slot = SpawnAt(Mathf.Repeat(mgr.turnFusePhase - UnityEngine.Random.Range(0f, 0.10f), 1f));
                if (slot >= 0) age[slot] = UnityEngine.Random.Range(0f, life[slot] * 0.8f);
            }
        }

        // Spawns one particle near rim position t (with loose scatter); returns slot or -1.
        private int SpawnAt(float t)
        {
            for (int i = 0; i < Pool; i++)
            {
                if (life[i] > 0f) continue;
                if (parts[i] == null) continue;   // image destroyed (teardown/failed init): skip the slot
                float tt = Mathf.Repeat(t, 1f);
                Vector3 basePos = PerimeterPoint(tt);
                // Streaks lie along the rim: remember the local tangent direction.
                Vector3 fwd = PerimeterPoint(Mathf.Repeat(tt + 0.002f, 1f)) - basePos;
                baseAng[i] = fwd.sqrMagnitude > 0.0001f ? Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg : 0f;
                // Loose density: scatter around the rim point in both axes.
                basePos += new Vector3(UnityEngine.Random.Range(-4f, 4f), UnityEngine.Random.Range(-4f, 4f), 0f);
                parts[i].position = basePos;
                parts[i].localRotation = Quaternion.Euler(0f, 0f, baseAng[i] + UnityEngine.Random.Range(-14f, 14f));
                // Mostly stationary — just a whisper of drift so the trail shimmers.
                drift[i] = new Vector2(UnityEngine.Random.Range(-2.5f, 2.5f), UnityEngine.Random.Range(-1.5f, 3f));
                age[i] = 0f;
                life[i] = UnityEngine.Random.Range(2.4f, 3.6f);   // longer life = longer fading tail behind the front
                spin[i] = UnityEngine.Random.Range(-16f, 16f);   // gentle — keeps streaks roughly rim-aligned
                size[i] = UnityEngine.Random.Range(3.5f, 7f);
                seed[i] = UnityEngine.Random.Range(0f, 100f);
                return i;
            }
            return -1;
        }

        private void Update()
        {
            if (mgr == null || half == null) return;
            // Children gone but the component still ticking (mid-teardown, failed init):
            // stop instead of spawning into destroyed slots every frame.
            if (parts[0] == null && parts[Pool - 1] == null) { enabled = false; return; }
            // The invisible "front" works its way around the rim; particles are born there.
            mgr.turnFusePhase = Mathf.Repeat(mgr.turnFusePhase + Time.deltaTime / LapSeconds, 1f);
            if (Time.unscaledTime >= nextSpawnAt)
            {
                nextSpawnAt = Time.unscaledTime + UnityEngine.Random.Range(0.030f, 0.055f);
                // Spawn just around the front (slightly ahead through slightly behind).
                SpawnAt(mgr.turnFusePhase + UnityEngine.Random.Range(-0.004f, 0.002f));
            }

            // The center "TURN N · ..." crest sits on the rim path — particles must not
            // show through it. Test against its (slightly padded) world rect each frame.
            Vector3 crestMin = Vector3.zero, crestMax = Vector3.zero;
            bool hasCrest = mgr.turnCrestRect != null;
            if (hasCrest)
            {
                var cc = new Vector3[4];
                mgr.turnCrestRect.GetWorldCorners(cc);
                const float pad = 8f;
                crestMin = new Vector3(Mathf.Min(cc[0].x, cc[2].x) - pad, Mathf.Min(cc[0].y, cc[2].y) - pad);
                crestMax = new Vector3(Mathf.Max(cc[0].x, cc[2].x) + pad, Mathf.Max(cc[0].y, cc[2].y) + pad);
            }
            for (int i = 0; i < Pool; i++)
            {
                if (life[i] <= 0f || parts[i] == null) continue;
                age[i] += Time.deltaTime;
                if (age[i] >= life[i]) { life[i] = 0f; partImgs[i].color = new Color(0f, 0f, 0f, 0f); continue; }
                float f = age[i] / life[i];                                 // 0 → 1
                parts[i].position += (Vector3)(drift[i] * Time.deltaTime);
                parts[i].localRotation = Quaternion.Euler(0f, 0f,
                    parts[i].localEulerAngles.z + spin[i] * Time.deltaTime);
                // Twinkle in fast at the front, twinkle out slow at the rear;
                // per-particle noise makes the shimmer non-uniform.
                float envelope = f < 0.22f ? Mathf.SmoothStep(0f, 1f, f / 0.22f)
                                           : Mathf.SmoothStep(1f, 0f, (f - 0.22f) / 0.78f);
                float twinkle = 0.55f + 0.45f * Mathf.PerlinNoise(Time.unscaledTime * 3.4f, seed[i]);
                float alpha = 0.78f * envelope * twinkle;
                var pos = parts[i].position;
                if (hasCrest && pos.x > crestMin.x && pos.x < crestMax.x && pos.y > crestMin.y && pos.y < crestMax.y)
                    alpha = 0f;   // passing behind the turn crest
                // Elongated streak lying along the rim.
                float s = size[i] * (0.7f + 0.5f * envelope);
                parts[i].sizeDelta = new Vector2(s * 2.3f, s * 0.9f);
                var col = Color.Lerp(accent, Color.white, 0.55f * envelope * twinkle);
                partImgs[i].color = new Color(col.r, col.g, col.b, alpha);
            }
        }

        // World-space point at parameter t (0..1) along the half's inset perimeter,
        // clockwise from the top-left corner.
        private Vector3 PerimeterPoint(float t)
        {
            var c = new Vector3[4];
            half.GetWorldCorners(c);   // 0=BL 1=TL 2=TR 3=BR
            float w = (c[2] - c[1]).magnitude - Inset * 2f;
            float h = (c[1] - c[0]).magnitude - Inset * 2f;
            if (w <= 0f || h <= 0f) return c[0];
            float per = 2f * (w + h);
            float d = t * per;
            Vector3 right = (c[2] - c[1]).normalized;
            Vector3 up = (c[1] - c[0]).normalized;
            Vector3 tl = c[1] + right * Inset - up * Inset;
            if (d < w) return tl + right * d;                              // top edge →
            d -= w;
            if (d < h) return tl + right * w - up * d;                     // right edge ↓
            d -= h;
            if (d < w) return tl + right * (w - d) - up * h;               // bottom edge ←
            d -= w;
            return tl - up * (h - d);                                      // left edge ↑
        }
    }

    // MTG-Arena-style turn-transition banner: a glowing light streak sweeps across the
    // middle of the screen with "YOUR TURN" / "OPPONENT'S TURN" (or the player's name in
    // hotseat), gold for the bottom/local player and cool blue for the opponent.
    private void MaybeShowTurnBanner()
    {
        if (state == null || state.Status != "active" || isReplayMode) return;
        if (state.TurnNumber == lastBannerTurn && state.ActiveSeat == lastBannerSeat) return;
        bool firstObservation = lastBannerTurn < 0 && state.TurnNumber > 1;
        lastBannerTurn = state.TurnNumber;
        lastBannerSeat = state.ActiveSeat;
        if (firstObservation) return;   // joining mid-match: don't flash a stale banner

        // Sequence the phase pills alongside the turn-start animations.
        if (phaseSeqCo != null) StopCoroutine(phaseSeqCo);
        phaseSeqCo = StartCoroutine(PhaseIntroSequence());

        bool mine = isNetworked ? state.ActiveSeat == localSeat : state.ActiveSeat == BottomSeat;
        string label;
        if (isNetworked) label = mine ? "YOUR TURN" : "OPPONENT'S TURN";
        else
        {
            // Non-networked: use the human display name ("You"/"Advanced Bot"/…), never the
            // engine seat identifier ("South"/"North").
            label = DisplayName(state.ActiveSeat).ToUpperInvariant() + "'S TURN";
        }
        var accent = mine ? new Color(1f, 0.72f, 0.22f) : new Color(0.38f, 0.72f, 1f);
        StartCoroutine(ShowTurnBanner(label, accent));
    }

    // END (previous turn wrapping) → REFRESH → DRAW → DON, then hand back to the live
    // mapping (MAIN/ATTACK). Each step re-renders only the pill row via Render-free
    // override + a light redraw of the tracker on the next natural Render; we force one
    // Render per step so the pills visibly walk even when nothing else changes.
    private IEnumerator PhaseIntroSequence()
    {
        phaseDisplayOverride = "END";
        RedrawPhaseTracker();
        yield return new WaitForSeconds(0.6f);
        phaseDisplayOverride = "REFRESH";
        RedrawPhaseTracker();
        yield return new WaitForSeconds(0.9f);
        phaseDisplayOverride = "DRAW";
        RedrawPhaseTracker();
        yield return new WaitForSeconds(1.0f);
        phaseDisplayOverride = "DON";
        RedrawPhaseTracker();
        yield return new WaitForSeconds(1.0f);
        phaseDisplayOverride = null;
        RedrawPhaseTracker();
        phaseSeqCo = null;
    }

    // Rebuilds just the phase pill row in place (cheap, no full Render).
    private void RedrawPhaseTracker()
    {
        if (sideRoot == null || state == null) return;
        // Destroy ALL existing pill rows, not just Find's first — Destroy() is deferred to end-of-frame,
        // so a stale (already-hidden) row can linger in the hierarchy and Find would grab THAT one,
        // leaving the real active row alive alongside the new one (two active pills). SetActive(false)
        // hides each immediately so none renders this frame.
        for (int i = sideRoot.childCount - 1; i >= 0; i--)
        {
            var child = sideRoot.GetChild(i);
            if (child.name == "Phase Tracker") { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }
        DrawPhaseTracker();
        // Keep any open game/sound menu above the freshly re-added pill row (else it hides behind it).
        sideRoot.Find("Game Menu")?.SetAsLastSibling();
        sideRoot.Find("Sound Menu")?.SetAsLastSibling();
    }

    private IEnumerator ShowTurnBanner(string label, Color accent)
    {
        var root = new GameObject("Turn Banner").AddComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        Stretch(root, new Vector2(0f, 0.34f), new Vector2(1f, 0.66f), Vector2.zero, Vector2.zero);
        root.SetAsLastSibling();
        var grp = root.gameObject.AddComponent<CanvasGroup>();
        grp.blocksRaycasts = false;
        grp.interactable = false;
        grp.alpha = 0f;

        float screenW = boardRoot != null && boardRoot.rect.width > 1f ? boardRoot.rect.width : 1600f;
        var dot = GetSoftDotSprite();

        // Dark vignette band for contrast (soft dot stretched to a wide pill).
        var band = ImageObject("Banner Band", root, dot);
        band.color = new Color(0f, 0f, 0f, 0.62f);
        band.raycastTarget = false;
        var bandRt = band.rectTransform;
        bandRt.anchorMin = bandRt.anchorMax = new Vector2(0.5f, 0.5f);
        bandRt.sizeDelta = new Vector2(screenW * 1.35f, 340f);

        // Main colored streak + hot white core.
        var streak = ImageObject("Banner Streak", root, dot);
        streak.color = new Color(accent.r, accent.g, accent.b, 0.85f);
        streak.raycastTarget = false;
        var streakRt = streak.rectTransform;
        streakRt.anchorMin = streakRt.anchorMax = new Vector2(0.5f, 0.5f);
        var hot = ImageObject("Banner Streak Hot", root, dot);
        hot.color = new Color(1f, 0.99f, 0.94f, 0.9f);
        hot.raycastTarget = false;
        var hotRt = hot.rectTransform;
        hotRt.anchorMin = hotRt.anchorMax = new Vector2(0.5f, 0.5f);

        // Sweeping light: a bright knot that races across the streak once.
        var sweep = ImageObject("Banner Sweep", root, dot);
        sweep.color = new Color(1f, 1f, 1f, 0f);
        sweep.raycastTarget = false;
        var sweepRt = sweep.rectTransform;
        sweepRt.anchorMin = sweepRt.anchorMax = new Vector2(0.5f, 0.5f);
        sweepRt.sizeDelta = new Vector2(260f, 120f);

        // Drifting embers.
        var embers = new List<RectTransform>();
        var emberSeeds = new List<Vector3>();
        for (int i = 0; i < 9; i++)
        {
            var e = ImageObject("Banner Ember", root, dot);
            e.raycastTarget = false;
            e.color = new Color(accent.r, Mathf.Min(1f, accent.g + 0.15f), accent.b, 0f);
            var ert = e.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            float ex = ((i * 73) % 100 / 100f - 0.5f) * screenW * 0.7f;
            float ey = ((i * 37) % 100 / 100f - 0.5f) * 90f;
            float esz = 6f + (i * 29) % 100 / 100f * 14f;
            ert.sizeDelta = new Vector2(esz, esz);
            ert.anchoredPosition = new Vector2(ex, ey);
            embers.Add(ert);
            emberSeeds.Add(new Vector3(ex, ey, 14f + (i * 53) % 100 / 100f * 30f));
        }

        // Text: dark drop layer + bright main layer.
        var shadow = TextObject("Banner Text Shadow", root, label, 58, new Color(0f, 0f, 0f, 0.85f), TextAnchor.MiddleCenter, titleFont);
        shadow.fontStyle = FontStyle.Bold;
        shadow.raycastTarget = false;
        Stretch(shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, -3f), new Vector2(3f, -3f));
        var text = TextObject("Banner Text", root, label, 58, new Color(1f, 0.97f, 0.88f), TextAnchor.MiddleCenter, titleFont);
        text.fontStyle = FontStyle.Bold;
        text.raycastTarget = false;
        AddOutline(text.gameObject, new Color(accent.r * 0.8f, accent.g * 0.62f, accent.b * 0.4f, 1f), 2.6f);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        const float inDur = 0.30f, hold = 1.05f, outDur = 0.45f;
        float t = 0f;
        while (t < inDur)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / inDur);
            float e = SmoothStep(f);
            // Ease-out-back overshoot for the text.
            float back = 1f + 1.9f * Mathf.Pow(f - 1f, 3f) + 0.9f * Mathf.Pow(f - 1f, 2f);
            grp.alpha = e;
            streakRt.sizeDelta = new Vector2(Mathf.Lerp(screenW * 0.12f, screenW * 1.2f, e), Mathf.Lerp(230f, 120f, e));
            hotRt.sizeDelta = new Vector2(Mathf.Lerp(screenW * 0.06f, screenW * 0.8f, e), Mathf.Lerp(120f, 40f, e));
            text.rectTransform.localScale = shadow.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, back);
            yield return null;
        }
        // Hold: embers drift up, the sweep knot races across once.
        t = 0f;
        while (t < hold && root != null)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / hold);
            for (int i = 0; i < embers.Count; i++)
            {
                if (embers[i] == null) continue;
                var seed = emberSeeds[i];
                embers[i].anchoredPosition = new Vector2(seed.x, seed.y + f * seed.z);
                var img = embers[i].GetComponent<Image>();
                if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, 0.75f * Mathf.Sin(f * Mathf.PI));
            }
            float sw = Mathf.Clamp01(f * 1.6f);
            sweepRt.anchoredPosition = new Vector2(Mathf.Lerp(-screenW * 0.45f, screenW * 0.45f, sw), 0f);
            sweep.color = new Color(1f, 1f, 1f, 0.55f * Mathf.Sin(sw * Mathf.PI));
            yield return null;
        }
        t = 0f;
        while (t < outDur && root != null)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / outDur);
            grp.alpha = 1f - SmoothStep(f);
            streakRt.sizeDelta = new Vector2(screenW * (1.2f + 0.4f * f), 120f * (1f - 0.55f * f));
            hotRt.sizeDelta = new Vector2(screenW * 0.8f * (1f + 0.35f * f), 40f * (1f - 0.7f * f));
            text.rectTransform.localScale = shadow.rectTransform.localScale = Vector3.one * (1f + 0.06f * f);
            yield return null;
        }
        if (root != null) Destroy(root.gameObject);
    }

    // Plateau dot: solid centre (~55% radius) with a quick smooth fade — used for the
    // ribbon CORE and prongs so they read crisp instead of blurry.
    private Sprite _coreDotSprite;
    private Sprite GetCoreDotSprite()
    {
        if (_coreDotSprite != null) return _coreDotSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - r) * 2.4f);   // plateau then fast falloff
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _coreDotSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _coreDotSprite;
    }

    private void EnsureSfx()
    {
        if (sfxSource == null)
        {
            sfxSource = gameObject.GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }
        if (!sfxLoadStarted)
        {
            sfxLoadStarted = true;
            StartCoroutine(LoadSfxClip("card_draw", clip => cardDrawClip = clip));
            StartCoroutine(LoadSfxClip("attack", clip => attackClip = clip));
        }
    }

    private void PlayAttackSfx()
    {
        EnsureSfx();
        if (attackClip != null && sfxSource != null) sfxSource.PlayOneShot(attackClip, SfxVolume);
    }

    private IEnumerator LoadSfxClip(string name, System.Action<AudioClip> assign)
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "sfx", name + ".wav");
        if (!System.IO.File.Exists(path)) yield break;
        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var raw = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                assign(TrimSilence(raw));
            }
        }
    }

    // Strips leading/trailing dead air from a clip so the sound lands the instant
    // it's triggered (draw SFX feels snappy). Threshold is relative to the clip's
    // own peak so quiet-but-real tails survive; keeps a tiny fade-in/out margin.
    private static AudioClip TrimSilence(AudioClip clip)
    {
        if (clip == null || clip.samples <= 0) return clip;
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return clip;

        float peak = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float a = data[i] < 0f ? -data[i] : data[i];
            if (a > peak) peak = a;
        }
        if (peak <= 0.0001f) return clip;
        float thresh = peak * 0.04f;   // 4% of peak = "audible"

        int first = 0, last = data.Length - 1;
        while (first < data.Length && Mathf.Abs(data[first]) < thresh) first++;
        while (last > first && Mathf.Abs(data[last]) < thresh) last--;
        if (first >= last) return clip;

        // Snap to frame boundaries and keep a ~5ms head / ~30ms tail margin.
        int head = Mathf.Max(0, first / clip.channels - clip.frequency / 200);
        int tail = Mathf.Min(clip.samples - 1, last / clip.channels + clip.frequency / 33);
        int frames = tail - head + 1;
        if (frames <= 0 || (head == 0 && tail == clip.samples - 1)) return clip;

        var trimmedData = new float[frames * clip.channels];
        System.Array.Copy(data, head * clip.channels, trimmedData, 0, trimmedData.Length);
        var trimmed = AudioClip.Create(clip.name + "_trim", frames, clip.channels, clip.frequency, false);
        trimmed.SetData(trimmedData, 0);
        return trimmed;
    }

    private void PlayCardDrawSfx(float delay = 0f)
    {
        EnsureSfx();
        if (cardDrawClip == null) return;
        if (delay <= 0f) sfxSource.PlayOneShot(cardDrawClip, SfxVolume);
        else StartCoroutine(PlaySfxDelayed(cardDrawClip, delay));
    }

    private IEnumerator PlaySfxDelayed(AudioClip clip, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (clip != null && sfxSource != null) sfxSource.PlayOneShot(clip, SfxVolume);
    }

    // Soft radial dot used by the energy arrows, banner streaks and glow effects.
    private Sprite _softDotSprite;
    private Sprite GetSoftDotSprite()
    {
        if (_softDotSprite != null) return _softDotSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _softDotSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _softDotSprite;
    }

    private void CaptureAndAnimateCardMoves()
    {
        if (state == null) return;
        Canvas.ForceUpdateCanvases();
        var now = new Dictionary<string, CardPose>();
        foreach (var seatKey in state.Players.Keys)
        {
            var pMv = state.Players[seatKey];
            System.Action<string, string, string, RectTransform, bool> addPose = (id, cardId, zone, rt, don) =>
            {
                if (rt == null || string.IsNullOrEmpty(id)) return;
                // The "Card Face" child carries the rest/tap rotation (kept off the root so attached DON
                // stay upright); capture it so FLIP can animate a card resting/un-resting in place.
                var faceRt = rt.Find("Card Face") as RectTransform;
                now[id] = new CardPose
                {
                    pos = rt.position, size = rt.rect.size, zone = zone, don = don, cardId = cardId,
                    owner = seatKey, rt = rt, faceRt = faceRt, rot = faceRt != null ? faceRt.rotation : rt.rotation
                };
            };
            System.Func<string, RectTransform> anchor = k => moveZoneAnchors.TryGetValue(k, out var a) ? a : null;

            if (pMv.Leader != null && cardTargetRects.TryGetValue(pMv.Leader.InstanceId, out var lr)) addPose(pMv.Leader.InstanceId, pMv.Leader.CardId, "board", lr, false);
            foreach (var c in pMv.CharacterArea) if (c != null && cardTargetRects.TryGetValue(c.InstanceId, out var cr)) addPose(c.InstanceId, c.CardId, "board", cr, false);
            if (pMv.Stage != null && cardTargetRects.TryGetValue(pMv.Stage.InstanceId, out var sr)) addPose(pMv.Stage.InstanceId, pMv.Stage.CardId, "board", sr, false);

            bool bottomHand = seatKey == BottomSeat;
            for (int i = 0; i < pMv.Hand.Count; i++)
            {
                var hc = pMv.Hand[i];
                RectTransform rt = null;
                if (handCardRects.TryGetValue(hc.InstanceId, out var hr) && hr != null) rt = hr;
                else if (!bottomHand && i < opponentHandSlots.Count) rt = opponentHandSlots[i];
                if (rt == null) rt = anchor("hand:" + seatKey);
                addPose(hc.InstanceId, hc.CardId, "hand:" + seatKey, rt, false);
            }
            lifeMoveRects.TryGetValue(seatKey, out var lifeRects);
            for (int i = 0; i < pMv.Life.Count; i++)
            {
                var rt = lifeRects != null && i < lifeRects.Count && lifeRects[i] != null ? lifeRects[i] : anchor("life:" + seatKey);
                addPose(pMv.Life[i].InstanceId, pMv.Life[i].CardId, "life:" + seatKey, rt, false);
            }
            for (int i = 0; i < pMv.Trash.Count; i++)
            {
                var tc = pMv.Trash[i];
                RectTransform rt = cardTargetRects.TryGetValue(tc.InstanceId, out var tr) && tr != null ? tr : anchor("trash:" + seatKey);
                addPose(tc.InstanceId, tc.CardId, "trash:" + seatKey, rt, false);
            }
            var deckAnchor = boardDeckPileRects.TryGetValue(seatKey, out var da) ? da : null;
            foreach (var dc in pMv.Deck) addPose(dc.InstanceId, dc.CardId, "deck:" + seatKey, deckAnchor, false);
            foreach (var dn in pMv.CostArea)
            {
                RectTransform rt = donMoveRects.TryGetValue(dn.InstanceId, out var dr) && dr != null ? dr : anchor("cost:" + seatKey);
                addPose(dn.InstanceId, null, "cost:" + seatKey, rt, true);
            }
            System.Action<CardInstance> attached = host =>
            {
                if (host == null || !cardTargetRects.TryGetValue(host.InstanceId, out var hr2) || hr2 == null) return;
                foreach (var dId in host.AttachedDonIds) addPose(dId, null, "don@" + host.InstanceId, hr2, true);
            };
            attached(pMv.Leader);
            foreach (var c in pMv.CharacterArea) attached(c);
            attached(pMv.Stage);
        }

        // The Life setup deal (deck → Life) may share its render with the post-mulligan
        // hand redeal (the final decision triggers both). AnimateHandDeal owns the
        // deck → hand flights that render, so suppress everything EXCEPT the Life deal
        // instead of gating the whole pass — otherwise Life just pops into place.
        bool suppressNonLife = handDealAnimating || mulliganRedrawSeat != null;
        bool animOk = state.Status == "active" && lastCardPoses.Count > 0;
        if (animOk)
        {
            // Turn-start sequencing: the phase pills say DRAW then DON, so the flights match —
            // the deck draw flies first, then each new DON!! follows one at a time.
            bool hasTurnDraw = false;
            foreach (var kv in now)
                if (lastCardPoses.TryGetValue(kv.Key, out var o) && o.zone.StartsWith("deck:") && kv.Value.zone.StartsWith("hand:")) { hasTurnDraw = true; break; }
            int donSeq = 0;
            int moves = 0;
            int lifeDealt = 0;
            int handDrawSeq = 0;   // staggers multi-card draws so each flies + sounds one at a time
            foreach (var kv in now)
            {
                if (!lastCardPoses.TryGetValue(kv.Key, out var old))
                {
                    // Brand-new DON!! in the cost area = drawn from the DON!! deck this action.
                    if (kv.Value.don && kv.Value.zone.StartsWith("cost:") && !suppressMoveAnim.Contains(kv.Key)
                        && moveZoneAnchors.TryGetValue("dondeck:" + kv.Value.owner, out var dda) && dda != null)
                    {
                        if (++moves > 12) break;
                        var fromDonDeck = new CardPose { pos = dda.position, size = kv.Value.size, zone = "dondeck:" + kv.Value.owner, don = true, owner = kv.Value.owner };
                        // After the draw finishes (if there was one), then one DON at a time.
                        float donDelay = (hasTurnDraw ? 0.55f : 0f) + 0.40f * donSeq;
                        donSeq++;
                        PlayCardDrawSfx(donDelay);
                        StartCoroutine(AnimateCardMoveGhost(fromDonDeck, kv.Value, null, false, false, HideForFlight(kv.Value), donDelay));
                    }
                    continue;
                }
                if (old.zone == kv.Value.zone) continue;
                if (suppressMoveAnim.Contains(kv.Key)) continue;
                bool isLifeDeal = old.zone.StartsWith("deck:") && kv.Value.zone.StartsWith("life:");
                if (suppressNonLife && !isLifeDeal) continue;   // hand redeal render: AnimateHandDeal covers those flights
                if ((old.pos - kv.Value.pos).sqrMagnitude < 25f) continue;
                if (++moves > 24) break;   // mass reshuffles: skip the ghost flood
                // Life setup (deck → Life): deal bottom-of-Life first, staggered upward.
                float moveDelay = 0f;
                bool isHandDraw = old.zone.StartsWith("deck:") && kv.Value.zone.StartsWith("hand:");
                if (isLifeDeal)
                {
                    var lifeOwner = state.Players[kv.Value.owner];
                    int li = lifeOwner.Life.FindIndex(c => c.InstanceId == kv.Key);
                    moveDelay = 0.20f * Mathf.Max(0, li);   // ≥ SFX length: one full sound per card
                    lifeDealt++;
                }
                else if (isHandDraw)
                {
                    // Multi-card draws (Galdino/Baby 5 "draw 2", any effect draw) fly and sound ONE
                    // card at a time — 0.22s ≥ the draw SFX length so each gets its own distinct sound.
                    moveDelay = 0.22f * handDrawSeq;
                    handDrawSeq++;
                }
                bool flare = old.zone.StartsWith("hand:") && kv.Value.zone.StartsWith("trash:");
                // Card-draw sound: deck → hand, Life → hand (damage/effects) and the
                // Life setup deal (deck → Life), timed with each card's flight.
                if ((old.zone.StartsWith("deck:") && kv.Value.zone.StartsWith("hand:"))
                    || (old.zone.StartsWith("life:") && kv.Value.zone.StartsWith("hand:"))
                    || (old.zone.StartsWith("deck:") && kv.Value.zone.StartsWith("life:")))
                    PlayCardDrawSfx(moveDelay);
                bool showFace = !kv.Value.don &&
                    (kv.Value.zone == "board" || kv.Value.zone.StartsWith("trash:") || old.zone == "board" || old.zone.StartsWith("trash:")
                     || (kv.Value.zone.StartsWith("hand:") && kv.Value.owner == BottomSeat && (!isNetworked || kv.Value.owner == localSeat)));
                // Grey the flight if it's landing as a summoning-sick character (matches the static board),
                // so a freshly-played character doesn't fly in full colour and then turn grey on landing.
                bool ghostDesat = false;
                if (showFace && !kv.Value.don && kv.Value.zone == "board" && kv.Value.owner == state.ActiveSeat
                    && state.Players.TryGetValue(kv.Value.owner, out var owP))
                {
                    var ci = owP.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == kv.Key);
                    ghostDesat = ci != null && !ci.Rested && GameEngine.IsSummoningSick(state, ci)
                                 && !CanAttackCharactersNow(ci, kv.Value.owner);
                }
                // NOTE: zone changes (hand↔board plays, draws, discards, life, DON) keep the ghost — a
                // top-level object that renders above the hand fan and animates size. FLIP-ing the real
                // board card here would let the hand layer occlude it mid-flight, so it's intentionally
                // NOT converted. Same-zone reflow/rest is handled by the FLIP pass below.
                StartCoroutine(AnimateCardMoveGhost(old, kv.Value, kv.Value.don ? null : kv.Value.cardId, showFace, flare, HideForFlight(kv.Value), moveDelay, ghostDesat));
            }

            // FLIP pass — persistent-identity glide for cards that STAY in the same zone but shift
            // position or rotate (character-row reflow when a card leaves/enters; a card resting/
            // un-resting in place). The real card is snapped back to its previous on-screen pose, then
            // eased to its new one — no ghost, no pop. (Zone changes still use the ghost pass above.)
            // Skipped during hand-deal/mulligan renders, which drive their own hand-card flights.
            if (!suppressNonLife)
            foreach (var kv in now)
            {
                if (!lastCardPoses.TryGetValue(kv.Key, out var old)) continue;
                if (old.zone != kv.Value.zone) continue;                              // zone changes -> ghost
                if (kv.Value.don) continue;                                           // Pass 1: cards only
                var rt = kv.Value.rt;
                if (rt == null || IsMoveAnchorRect(rt) || kv.Value.zone.StartsWith("don@")) continue;  // need a real card
                if (suppressMoveAnim.Contains(kv.Key)) continue;
                bool posChanged = (old.pos - kv.Value.pos).sqrMagnitude > 4f;
                bool rotChanged = kv.Value.faceRt != null && old.faceRt != null
                    && Quaternion.Angle(old.rot, kv.Value.rot) > 1.5f;
                if (!posChanged && !rotChanged) continue;
                // Move the holder (card + summoning-sick veil + blocker shield) as a unit for board
                // characters, so overlays don't get left behind; the root alone for hand cards.
                var moveRt = FlipUnit(rt);
                var flip = moveRt.gameObject.AddComponent<CardFlip>();
                if (posChanged) flip.InitPos(moveRt, old.pos, moveRt.position);
                if (rotChanged) flip.InitRot(kv.Value.faceRt, old.rot, kv.Value.rot);
            }
        }
        lastCardPoses.Clear();
        foreach (var kv in now) lastCardPoses[kv.Key] = kv.Value;
        suppressMoveAnim.Clear();
    }

    // The rect a FLIP should move: for a board character, its "Character Holder" (so the summoning-sick
    // veil + blocker shield ride along instead of being stranded at the slot); otherwise the card root
    // itself — hand cards and leader/stage have no such overlays, and a hand card's parent is a shared
    // row we must not move.
    private RectTransform FlipUnit(RectTransform cardRoot)
    {
        var parent = cardRoot != null ? cardRoot.parent as RectTransform : null;
        return (parent != null && parent.name == "Character Holder") ? parent : cardRoot;
    }

    // FLIP glide for a real card: snapped to its previous on-screen pose, then eased to its new one, so
    // a same-zone position/rotation change reads as continuous motion instead of a pop. Ease-out cubic;
    // the card is the actual rendered object (art, badges, DON, rest rotation all intact), not a ghost.
    private sealed class CardFlip : MonoBehaviour
    {
        private RectTransform posRt; private Vector3 posStart, posTarget; private bool doPos;
        private RectTransform rotRt; private Quaternion rotStart, rotTarget; private bool doRot;
        private float t;
        private const float Dur = 0.22f;

        public void InitPos(RectTransform rt, Vector3 start, Vector3 target)
        {
            posRt = rt; posStart = start; posTarget = target; doPos = true;
            if (posRt != null) posRt.position = start;   // invert now, so there's no 1-frame flash at target
        }
        public void InitRot(RectTransform rt, Quaternion start, Quaternion target)
        {
            rotRt = rt; rotStart = start; rotTarget = target; doRot = true;
            if (rotRt != null) rotRt.rotation = start;
        }

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Dur);
            float e = 1f - Mathf.Pow(1f - k, 3f);   // ease-out cubic
            if (doPos && posRt != null) posRt.position = Vector3.LerpUnclamped(posStart, posTarget, e);
            if (doRot && rotRt != null) rotRt.rotation = Quaternion.SlerpUnclamped(rotStart, rotTarget, e);
            if (k >= 1f)
            {
                if (doPos && posRt != null) posRt.position = posTarget;
                if (doRot && rotRt != null) rotRt.rotation = rotTarget;
                Destroy(this);
            }
        }
    }

    // Hides the freshly rendered destination card so it doesn't sit there while its own
    // ghost is still flying in; returns the CanvasGroup-carrying rect to reveal on landing.
    private RectTransform HideForFlight(CardPose to)
    {
        if (to == null || to.rt == null) return null;
        if (to.zone.StartsWith("don@")) return null;         // the "rect" is the HOST character
        if (IsMoveAnchorRect(to.rt)) return null;            // shared pile anchor, not this card
        var g = to.rt.GetComponent<CanvasGroup>();
        if (g == null) g = to.rt.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        return to.rt;
    }

    private bool IsMoveAnchorRect(RectTransform rt)
    {
        if (rt == null) return true;
        foreach (var kv in moveZoneAnchors) if (kv.Value == rt) return true;
        if (boardDeckPileRects.ContainsValue(rt)) return true;
        return false;
    }

    // One ghost flight from a card's previous on-screen spot to its new one. `flare`
    // (event/counter use from hand) adds a gold outline + a stronger scale pulse.
    private IEnumerator AnimateCardMoveGhost(CardPose from, CardPose to, string cardId, bool showFace, bool flare, RectTransform reveal = null, float delay = 0f, bool desaturate = false)
    {
        activeMoveGhosts++;
        try {
        var ghost = new GameObject("Move Ghost").AddComponent<RectTransform>();
        // DON!! flights (deck → cost area, attach/return) happen entirely on the table
        // surface, so they fly UNDER the hand rows: slot them just above the playmat in
        // playRoot instead of canvas-top, where they'd streak over the player's hand.
        bool underHand = cardId == null && playRoot != null;
        if (underHand)
        {
            ghost.SetParent(playRoot, false);
            var mat = playRoot.Find("Reference Playmat") as RectTransform;
            if (mat != null) ghost.SetSiblingIndex(mat.GetSiblingIndex() + 1);
        }
        else
        {
            ghost.SetParent(canvas.transform, false);
            ghost.SetAsLastSibling();
        }
        Vector2 gSize = from.size.x > 4f ? from.size : (boardCardSize.x > 1f ? boardCardSize : new Vector2(70f, 98f));
        // Zone-anchor poses (whole hand row, pile zones) are huge — a ghost must always
        // be CARD sized, never region sized.
        Vector2 gCap = boardCardSize.x > 1f ? boardCardSize : new Vector2(70f, 98f);
        if (gSize.x > gCap.x * 1.6f || gSize.y > gCap.y * 1.6f) gSize = gCap;
        ghost.sizeDelta = gSize;
        ghost.position = from.pos;
        // Match the DESTINATION's orientation for the whole flight — a card played onto
        // the opponent's (upside-down) side must look upside down from the moment it
        // appears, not land and then flip.
        if (to.rt != null) ghost.rotation = to.rt.rotation;
        Sprite sprite = cardId != null ? (showFace ? GetCardSprite(cardId) : GetBackSprite()) : GetDonSprite();
        var art = AddRoundedCardImage(ghost, "Art", sprite);
        art.raycastTarget = false;
        // Fly DARKENED if it's landing as a summoning-sick character (colours kept, just dimmed — matches
        // the static board), so it doesn't pop dim→bright on landing. Multiplying the art colour ≈ card*0.30.
        if (desaturate) art.color = new Color(0.30f, 0.30f, 0.30f, 1f);
        var grp = ghost.gameObject.AddComponent<CanvasGroup>();
        grp.blocksRaycasts = false;
        grp.interactable = false;
        if (flare) AddMysticalCardOutline(ghost, true);
        if (delay > 0f)
        {
            grp.alpha = 0f;                      // invisible until its stagger slot
            yield return new WaitForSeconds(delay);
            if (ghost == null) yield break;
            grp.alpha = 1f;
        }

        const float duration = 0.38f;
        float t = 0f;
        Vector3 a = from.pos, b = to.pos;
        while (t < duration && ghost != null)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            ghost.position = Vector3.Lerp(a, b, SmoothStep(f));
            float pulse = 1f + (flare ? 0.35f : 0.10f) * Mathf.Sin(f * Mathf.PI);
            ghost.localScale = Vector3.one * pulse;
            if (f > 0.85f) grp.alpha = Mathf.InverseLerp(1f, 0.85f, f);
            // Cross-fade: the real card fades IN as the ghost fades out at the landing.
            if (reveal != null && f > 0.85f)
            {
                var rg = reveal.GetComponent<CanvasGroup>();
                if (rg != null) rg.alpha = Mathf.InverseLerp(0.85f, 1f, f);
            }
            yield return null;
        }
        if (reveal != null)
        {
            var rg = reveal.GetComponent<CanvasGroup>();
            if (rg != null) rg.alpha = 1f;
        }
        if (ghost != null) Destroy(ghost.gameObject);
        } finally { activeMoveGhosts--; }
    }

    // Modal shown when the networked peer disconnects before the match finished.
    private void DrawOpponentLeftOverlay()
    {
        var dim = PanelObject("Opponent Left Dim", boardRoot, new Color32(8, 10, 14, 200));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.SetAsLastSibling();

        var panel = PanelObject("Opponent Left Panel", boardRoot, (Color)new Color32(14, 30, 46, 250));
        Stretch(panel, new Vector2(0.32f, 0.40f), new Vector2(0.68f, 0.60f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 2f);
        panel.SetAsLastSibling();

        var label = TextObject("Opponent Left Text", panel, "OPPONENT LEFT — YOU WIN!", 20, Ink, TextAnchor.MiddleCenter, titleFont);
        label.fontStyle = FontStyle.Bold;
        Stretch(label.rectTransform, new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.94f), Vector2.zero, Vector2.zero);

        var buttons = RowObject("Opponent Left Buttons", panel, 10, TextAnchor.MiddleCenter);
        Stretch(buttons, new Vector2(0.10f, 0.10f), new Vector2(0.90f, 0.46f), Vector2.zero, Vector2.zero);
        AddButton(buttons, "MAIN MENU", ReturnToMenu);
    }

    // The finished-match result screen (all modes; replays keep their own controls). Buttons
    // vary by mode — ranked/casual: Main Menu only; custom: +Rematch/Change Deck; solo:
    // +Rematch. Its Main Menu is the only exit, so the match always tears down cleanly.
    private bool ResultScreenActive() =>
        state != null && state.Status == "finished" && !isReplayMode && !opponentLeft;

    // When the local player has LOST a finished match, reveal the opponent's hidden info
    // (hand + life). Both clients simulate the full deterministic state, so the opponent's
    // real cards are already in local state — this is a pure rendering flip, only after the
    // match is decided, and it applies in every game mode. Not shown during replays (which
    // have their own visibility controls).
    private bool RevealOnLoss() =>
        state != null && state.Status == "finished" && !isReplayMode
        && finishedResultText == "YOU LOSE.";

    // At the END of a match (win OR lose), reveal BOTH players' remaining hand + life so the result
    // screen's "View Board" shows the full final position. Both sides simulate the same deterministic
    // state, so every card is already in local state — this is a pure rendering flip, only once the
    // match is decided. (RevealOnLoss covered only the opponent, and only on a loss.) Not in replays.
    private bool RevealFinishedInfo() =>
        state != null && state.Status == "finished" && !isReplayMode;

    // "YOU WIN!/YOU LOSE." modal for a finished ranked/casual match. Its Main Menu button
    // routes through ReturnToMenu, which tears down Netcode — the only intended exit.
    private void DrawMatchResultOverlay()
    {
        var dim = PanelObject("Match Result Dim", boardRoot, new Color32(8, 10, 14, 210));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.SetAsLastSibling();

        bool won = finishedResultText == "YOU WIN!";
        Color accentCol = won ? new Color(0.42f, 0.85f, 0.55f) : new Color(0.93f, 0.48f, 0.48f);

        bool solo = !isNetworked;
        bool custom = isNetworked && networkedMode == "custom";
        // Offer to friend the opponent after any networked match (unless already friends / guest).
        bool canAddOpp = isNetworked && !AccountManager.IsGuest
            && !string.IsNullOrEmpty(cachedRankedOppId) && addFriendState != "alreadyFriend";
        bool extraButtons = solo || custom || canAddOpp;   // wider panel for the 3–4-button layouts

        var panel = PanelObject("Match Result Panel", boardRoot, (Color)new Color32(14, 30, 46, 250));
        Stretch(panel, new Vector2(extraButtons ? 0.26f : 0.33f, 0.36f), new Vector2(extraButtons ? 0.74f : 0.67f, 0.64f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, accentCol, 2f);
        panel.SetAsLastSibling();

        var label = TextObject("Match Result Text", panel, finishedResultText ?? "MATCH OVER", 28, accentCol, TextAnchor.MiddleCenter, titleFont);
        label.fontStyle = FontStyle.Bold;
        Stretch(label.rectTransform, new Vector2(0.04f, 0.54f), new Vector2(0.96f, 0.94f), Vector2.zero, Vector2.zero);

        string subText = solo ? "Match complete"
            : custom ? "Custom match complete"
            : (networkedMode == "ranked" ? "Ranked" : "Casual") + " match complete";
        var sub = TextObject("Match Result Sub", panel, subText, 12, Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(sub.rectTransform, new Vector2(0.06f, 0.44f), new Vector2(0.94f, 0.54f), Vector2.zero, Vector2.zero);

        var buttons = RowObject("Match Result Buttons", panel, 10, TextAnchor.MiddleCenter);
        Stretch(buttons, new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.40f), Vector2.zero, Vector2.zero);
        if (solo)
        {
            // Solo rematch = replay the same matchup (fresh seed) — the proven NewMatch path.
            AddButton(buttons, "REMATCH", NewMatch);
        }
        else if (custom)
        {
            if (rematchLocalRequested)
                AddButton(buttons, rematchPeerRequested ? "STARTING…" : "WAITING…", RequestRematch, false);
            else
                AddButton(buttons, "REMATCH", RequestRematch);
        }
        AddButton(buttons, "MAIN MENU", ReturnToMenu);
        if (custom) AddButton(buttons, "CHANGE DECK", ReturnToLobby);   // back to lobby (swap deck + restart)
        if (canAddOpp)
        {
            // Kick off the "are we already friends?" check once; it flips addFriendState and re-renders.
            if (!addFriendChecked) { addFriendChecked = true; CheckOpponentFriendship(); }
            string addLabel = addFriendState == "sending" ? "SENDING…"
                : addFriendState == "sent" ? "REQUEST SENT"
                : addFriendState == "error" ? "RETRY ADD FRIEND"
                : "ADD FRIEND";
            AddButton(buttons, addLabel, AddOpponentFriendClicked,
                addFriendState != "sending" && addFriendState != "sent");
        }
        AddButton(buttons, "VIEW BOARD", () => { matchResultHidden = true; Render(); });
    }

    // Async check run once when the result overlay first appears: if the opponent is already a
    // confirmed friend, hide the "Add Friend" button (flip state + re-render).
    private async void CheckOpponentFriendship()
    {
        string oppId = cachedRankedOppId;
        if (string.IsNullOrEmpty(oppId) || AccountManager.IsGuest) return;
        try
        {
            bool isFriend = await FriendsManager.IsFriendAsync(oppId);
            if (this == null) return;
            if (isFriend && addFriendState == "idle")
            {
                addFriendState = "alreadyFriend";
                if (finishedResultText != null && !matchResultHidden) Render();
            }
        }
        catch { /* offline — leave the button shown; the send handles duplicates */ }
    }

    private async void AddOpponentFriendClicked()
    {
        string oppId = cachedRankedOppId;
        if (string.IsNullOrEmpty(oppId) || addFriendState == "sending" || addFriendState == "sent") return;
        addFriendState = "sending";
        Render();
        try
        {
            var result = await FriendsManager.AddFriendByIdAsync(oppId);
            if (this == null) return;
            addFriendState = result.Ok ? "sent" : "error";
        }
        catch { if (this != null) addFriendState = "error"; }
        if (this != null && finishedResultText != null && !matchResultHidden) Render();
    }

    // ── Bug reporting ─────────────────────────────────────────────────────────
    // Right-click a card → this fires; the manager opens the report modal for that card.
    private sealed class CardContextClick : MonoBehaviour, IPointerClickHandler
    {
        private GameManager manager;
        private CardInstance card;
        public void Init(GameManager m, CardInstance c) { manager = m; card = c; }
        public void OnPointerClick(PointerEventData e)
        {
            if (e != null && e.button == PointerEventData.InputButton.Right && manager != null && card != null)
                manager.OpenBugReport(card);
        }
    }

    private GameObject bugReportModal;

    // Opens a small modal for the right-clicked card: a free-text field for the player's description,
    // Submit/Cancel. On submit it records the FULL game context (BugReportStore.Build → JSONL) so the
    // exact position is reproducible later. Self-managed (parented to the canvas, torn down on close),
    // so the immediate-mode Render() never destroys or duplicates it.
    internal void OpenBugReport(CardInstance card)
    {
        if (card == null || state == null || bugReportModal != null) return;
        var def = GameEngine.GetCard(card);

        // Full-screen dim backdrop that also blocks clicks to the board underneath.
        var backdrop = PanelObject("Bug Report Backdrop", canvas.transform, new Color(0f, 0f, 0f, 0.62f));
        Stretch(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        backdrop.GetComponent<Image>().raycastTarget = true;
        backdrop.SetAsLastSibling();
        bugReportModal = backdrop.gameObject;

        var panel = PanelObject("Bug Report Panel", backdrop, (Color)new Color32(16, 30, 46, 252));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(520f, 340f);
        panel.anchoredPosition = Vector2.zero;
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 1.6f);

        var title = TextObject("Bug Title", panel, "🐛  Report a Bug", 18, Ink, TextAnchor.UpperLeft, titleFont);
        Stretch(title.rectTransform, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f), Vector2.zero, Vector2.zero);

        string zone = string.IsNullOrEmpty(card.Zone) ? "?" : card.Zone;
        var sub = TextObject("Bug Card", panel, $"Card: {def?.Name ?? card.CardId} [{card.CardId}] · {zone} · turn {state.TurnNumber}",
            11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(sub.rectTransform, new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.86f), Vector2.zero, Vector2.zero);

        var prompt = TextObject("Bug Prompt", panel, "What went wrong? (what you expected vs. what happened)",
            11, Accent2, TextAnchor.UpperLeft);
        Stretch(prompt.rectTransform, new Vector2(0.05f, 0.70f), new Vector2(0.95f, 0.78f), Vector2.zero, Vector2.zero);

        // Multi-line input field.
        var fieldGo = new GameObject("Bug Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        var fieldRt = fieldGo.GetComponent<RectTransform>();
        fieldRt.SetParent(panel, false);
        Stretch(fieldRt, new Vector2(0.05f, 0.22f), new Vector2(0.95f, 0.68f), Vector2.zero, Vector2.zero);
        fieldGo.GetComponent<Image>().color = new Color32(20, 34, 50, 235);
        Round(fieldRt);
        AddRoundedCardBorder(fieldRt, MenuB, 1f);
        var field = fieldGo.GetComponent<InputField>();
        var ph = TextObject("Bug Placeholder", fieldRt, "Describe the bug...", 12, Muted, TextAnchor.UpperLeft);
        var txt = TextObject("Bug Field Text", fieldRt, "", 12, Ink, TextAnchor.UpperLeft);
        Stretch(ph.rectTransform, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f), Vector2.zero, Vector2.zero);
        Stretch(txt.rectTransform, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f), Vector2.zero, Vector2.zero);
        txt.horizontalOverflow = HorizontalWrapMode.Wrap; txt.verticalOverflow = VerticalWrapMode.Overflow;
        ph.horizontalOverflow = HorizontalWrapMode.Wrap; ph.verticalOverflow = VerticalWrapMode.Overflow;
        field.textComponent = txt;
        field.placeholder = ph;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.characterLimit = 1000;
        field.ActivateInputField();

        // Submit (captures state now) + Cancel.
        var submit = PanelObject("Bug Submit", panel, Accent);
        Stretch(submit, new Vector2(0.52f, 0.05f), new Vector2(0.95f, 0.17f), Vector2.zero, Vector2.zero);
        Round(submit);
        var submitTxt = TextObject("Bug Submit Text", submit, "Submit Report", 12, BadgeInk, TextAnchor.MiddleCenter, titleFont);
        Stretch(submitTxt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        submit.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            string desc = field.text ?? "";
            if (string.IsNullOrWhiteSpace(desc)) { ph.text = "Please describe the bug first."; ph.color = (Color)new Color32(232, 120, 120, 255); return; }
            var report = BugReportStore.Build(state, currentMatchConfig, card, card.Zone, desc.Trim(), localSeat);
            bool ok = BugReportStore.Save(report);
            ShowBugReportConfirmation(panel, ok, report?.Id);
        });

        var cancel = PanelObject("Bug Cancel", panel, (Color)new Color32(40, 54, 72, 235));
        Stretch(cancel, new Vector2(0.05f, 0.05f), new Vector2(0.48f, 0.17f), Vector2.zero, Vector2.zero);
        Round(cancel);
        var cancelTxt = TextObject("Bug Cancel Text", cancel, "Cancel", 12, Ink, TextAnchor.MiddleCenter, titleFont);
        Stretch(cancelTxt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cancel.gameObject.AddComponent<Button>().onClick.AddListener(CloseBugReport);
    }

    // Replaces the modal body with a brief confirmation, then a Close button.
    private void ShowBugReportConfirmation(RectTransform panel, bool ok, string id)
    {
        for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);
        var msg = TextObject("Bug Confirm", panel,
            ok ? $"✓  Thanks — bug report saved.\nID: {id}\n\nIt records the exact game state so it can be reproduced and fixed."
               : "⚠  Could not save the report (see log).",
            13, ok ? Ink : (Color)new Color32(232, 120, 120, 255), TextAnchor.MiddleCenter);
        Stretch(msg.rectTransform, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
        var close = PanelObject("Bug Close", panel, Accent);
        Stretch(close, new Vector2(0.30f, 0.08f), new Vector2(0.70f, 0.22f), Vector2.zero, Vector2.zero);
        Round(close);
        var closeTxt = TextObject("Bug Close Text", close, "Close", 12, BadgeInk, TextAnchor.MiddleCenter, titleFont);
        Stretch(closeTxt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        close.gameObject.AddComponent<Button>().onClick.AddListener(CloseBugReport);
    }

    private void CloseBugReport()
    {
        if (bugReportModal != null) Destroy(bugReportModal);
        bugReportModal = null;
    }

    // Shown instead of the modal after "View Board": a small chip to bring the result back
    // (still the only route to Main Menu — the in-match menu stays locked out).
    private void DrawMatchResultPeekChip()
    {
        // Parent to the CANVAS (not boardRoot) and SetAsLastSibling so it sits above leftRoot/sideRoot —
        // the old top-LEFT placement hid behind the left card-preview panel, and the top-CENTER one sat
        // right on the opponent's hand (which renders across the top-center). Anchor to the top-RIGHT
        // corner instead: clear of both the centre hand strip and the left preview, still prominent.
        var chip = PanelObject("Result Peek Chip", canvas.transform, (Color)new Color32(14, 30, 46, 245));
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.sizeDelta = new Vector2(200f, 42f);
        chip.anchoredPosition = new Vector2(-14f, -10f);
        Round(chip);
        AddRoundedCardBorder(chip, Accent, 1.4f);
        chip.SetAsLastSibling();
        var t = TextObject("Result Peek Text", chip, "◂ SHOW RESULT", 13, Ink, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = chip.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { matchResultHidden = false; Render(); });
        resultPeekChip = chip;   // cached so Render() can tear it down (see the top of Render)
    }

    // Thin control strip across the top of the board, only shown during replay playback.
    // Speed multiplier presets (spec: "Suggested playback speeds").
    private static readonly float[] ReplaySpeedPresets = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 4f };
    // Custom-delay presets (spec: "Instant, 0.25s, 0.5s, 1s, 2s, 3s, 5s"). "Original match
    // timing" (the spec's 8th option) isn't a delay value — picking it switches replayMode to
    // "realtime" instead; see the control bar's mode/delay buttons below.
    private static readonly float[] ReplayDelayPresets = { 0f, 0.25f, 0.5f, 1f, 2f, 3f, 5f };
    private static readonly string[] ReplayModeOrder = { "actionByAction", "realtime", "turnByTurn" };
    private bool replayControlsCollapsed;
    // Side action-log panel (Phase 5) — starts collapsed so entering a replay isn't
    // immediately half-obscured; the control bar (always visible) is enough to start playing.
    private bool replayActionPanelCollapsed = true;
    private readonly HashSet<int> replayCollapsedTurns = new HashSet<int>();
    // Info-display toggle (Phase 8): the current-action banner + active-card glow from
    // DrawReplayActionOverlay. Persisted like the rest of the replay layout prefs below.
    private bool replayShowDescriptions = true;

    // ── Persisted replay layout/settings (Phase 8) ───────────────────────────
    // Spec: "the interface should remember the user's preferred layout and settings between
    // uses." Mirrors the "optcg.xxx" PlayerPrefs convention used for SFX volume elsewhere in
    // this file. Loaded once when entering a replay; saved on every user-driven change rather
    // than on exit, so a crash/force-quit mid-replay doesn't lose the preference.
    private void LoadReplaySettings()
    {
        replayMode = PlayerPrefs.GetString("optcg.replay.mode", "actionByAction");
        if (System.Array.IndexOf(ReplayModeOrder, replayMode) < 0) replayMode = "actionByAction";
        replayCustomDelaySeconds = PlayerPrefs.GetFloat("optcg.replay.delay", 1f);
        replaySpeedMultiplier = PlayerPrefs.GetFloat("optcg.replay.speed", 1f);
        replayControlsCollapsed = PlayerPrefs.GetInt("optcg.replay.controlsCollapsed", 0) != 0;
        replayActionPanelCollapsed = PlayerPrefs.GetInt("optcg.replay.actionPanelCollapsed", 1) != 0;
        replayShowDescriptions = PlayerPrefs.GetInt("optcg.replay.showDescriptions", 1) != 0;
        replayViewMode = PlayerPrefs.GetString("optcg.replay.viewMode", "neutral");
        if (System.Array.IndexOf(ReplayViewModeOrder, replayViewMode) < 0) replayViewMode = "neutral";
        replayHandVisibility = PlayerPrefs.GetString("optcg.replay.handVisibility", "both");
        if (System.Array.IndexOf(ReplayHandVisibilityOrder, replayHandVisibility) < 0) replayHandVisibility = "both";
    }

    private void SaveReplaySettings()
    {
        PlayerPrefs.SetString("optcg.replay.mode", replayMode);
        PlayerPrefs.SetFloat("optcg.replay.delay", replayCustomDelaySeconds);
        PlayerPrefs.SetFloat("optcg.replay.speed", replaySpeedMultiplier);
        PlayerPrefs.SetInt("optcg.replay.controlsCollapsed", replayControlsCollapsed ? 1 : 0);
        PlayerPrefs.SetInt("optcg.replay.actionPanelCollapsed", replayActionPanelCollapsed ? 1 : 0);
        PlayerPrefs.SetInt("optcg.replay.showDescriptions", replayShowDescriptions ? 1 : 0);
        PlayerPrefs.SetString("optcg.replay.viewMode", replayViewMode);
        PlayerPrefs.SetString("optcg.replay.handVisibility", replayHandVisibility);
        PlayerPrefs.Save();
    }

    private void DrawReplayControlBar()
    {
        if (replayControlsCollapsed)
        {
            var tab = PanelObject("Replay Reopen Tab", boardRoot, new Color32(8, 14, 21, 230));
            tab.anchorMin = new Vector2(1f, 0f); tab.anchorMax = new Vector2(1f, 0f);
            tab.pivot = new Vector2(1f, 0f);
            tab.sizeDelta = new Vector2(120f, 32f);
            tab.anchoredPosition = new Vector2(-12f, 12f);
            Round(tab);
            AddRoundedCardBorder(tab, MenuB, 1f);
            var tabText = TextObject("t", tab, "▲ REPLAY", 11, Ink, TextAnchor.MiddleCenter, monoFont);
            tabText.fontStyle = FontStyle.Bold;
            Stretch(tabText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var tabBtn = tab.gameObject.AddComponent<Button>();
            tabBtn.onClick.AddListener(() => { replayControlsCollapsed = false; SaveReplaySettings(); Render(); });
            return;
        }

        int total = loadedReplay?.CommandHistory.Count ?? 0;
        // Condensed, League-of-Legends-replay-bar-style layout: one thin strip of small
        // mode/speed pills + status text on top, one thin strip of icon transport buttons +
        // timeline scrubber (sharing a row) below — instead of the old two big rows of
        // fixed-118px labeled buttons.
        // Centered, ~44% of the window width — not a full-width bar. A full-width bar reads as
        // a heavy strip across the whole screen; a shorter, centered one (like most video/League
        // replay bars) sits over the seam without competing with the board on either side.
        const float barH = 64f;
        var bar = PanelObject("Replay Control Bar", boardRoot, new Color32(8, 14, 21, 235));
        bar.anchorMin = new Vector2(0.28f, 0f);
        bar.anchorMax = new Vector2(0.72f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, barH);
        bar.anchoredPosition = Vector2.zero;
        AddRoundedCardBorder(bar, MenuB, 1f);

        var topRow = PanelObject("Replay Top Row", bar, new Color(0, 0, 0, 0));
        Stretch(topRow, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -26f), new Vector2(-40f, -4f));

        var statusLabel = TextObject("Replay Status", topRow,
            $"{(replayPlaying ? "▶" : "❚❚")} step {replayCursor}/{total} · turn {state.TurnNumber}",
            9, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(statusLabel.rectTransform, Vector2.zero, new Vector2(0.32f, 1f), Vector2.zero, Vector2.zero);

        var pillRow = PanelObject("Pills", topRow, new Color(0, 0, 0, 0));
        Stretch(pillRow, new Vector2(0.32f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        var pillHlg = pillRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        pillHlg.spacing = 5f;
        pillHlg.childAlignment = TextAnchor.MiddleRight;
        pillHlg.childControlWidth = false;
        pillHlg.childControlHeight = false;
        pillHlg.childForceExpandWidth = false;
        pillHlg.childForceExpandHeight = false;

        string modeLabel = replayMode switch
        {
            "realtime" => "REAL-TIME",
            "turnByTurn" => "TURN-BY-TURN",
            _ => "ACTION-BY-ACTION",
        };
        AddCompactPill(pillRow, modeLabel, CycleReplayMode);
        if (replayMode == "actionByAction")
        {
            string delayLabel = replayCustomDelaySeconds <= 0f ? "INSTANT" : $"{replayCustomDelaySeconds:0.##}s";
            AddCompactPill(pillRow, delayLabel, CycleReplayDelay);
        }
        AddCompactPill(pillRow, $"{replaySpeedMultiplier:0.##}x", CycleReplaySpeed);
        AddCompactPill(pillRow, replayShowDescriptions ? "DESC ON" : "DESC OFF",
            () => { replayShowDescriptions = !replayShowDescriptions; SaveReplaySettings(); Render(); });
        string viewLabel = replayViewMode switch
        {
            "south" => "VIEW: PLAYER 1",
            "north" => "VIEW: PLAYER 2",
            _ => "VIEW: NEUTRAL",
        };
        AddCompactPill(pillRow, viewLabel, CycleReplayViewMode);
        string handsLabel = replayHandVisibility switch
        {
            "south" => "HANDS: P1",
            "north" => "HANDS: P2",
            "none" => "HANDS: NONE",
            _ => "HANDS: BOTH",
        };
        AddCompactPill(pillRow, handsLabel, CycleReplayHandVisibility);

        var bottomRow = PanelObject("Replay Transport", bar, new Color(0, 0, 0, 0));
        Stretch(bottomRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 4f), new Vector2(-40f, 34f));
        var hlg = bottomRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 5f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        // Controlled width+height so the timeline's LayoutElement.flexibleWidth can absorb all
        // remaining space after the fixed-width icon buttons — see AddIconButton/DrawReplayTimeline.
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // Plain ASCII arrows, not multi-glyph Unicode combos — guaranteed to render at any size
        // and to actually fit inside a small icon button (the Unicode combo glyphs didn't).
        if (replayMode == "turnByTurn")
        {
            // Turn-by-Turn gets its own button set (spec: play a single turn, play through every
            // remaining turn, jump back/forward one turn or five) — the standard action-stepping
            // row (Prev/Next action) doesn't apply here.
            AddIconButton(bottomRow, "|<<", () => { replayPlaying = false; replayStopAtCursor = null; ResimulateReplayTo(0); }, replayCursor > 0, 34f);
            AddIconButton(bottomRow, "<<5", () => ReplayJumpTurnsRelative(-5), replayCursor > 0, 34f);
            AddIconButton(bottomRow, "<", ReplayPrevTurn, replayCursor > 0, 30f);
            AddIconButton(bottomRow, "▶1", ReplayPlayCurrentTurn, replayCursor < total, 34f);
            AddIconButton(bottomRow, replayPlaying ? "❚❚" : "▶", replayPlaying ? ReplayPause : (UnityEngine.Events.UnityAction)ReplayPlayAll, true, 34f);
            AddIconButton(bottomRow, ">", ReplayNextTurn, replayCursor < total, 30f);
            AddIconButton(bottomRow, ">>5", () => ReplayJumpTurnsRelative(5), replayCursor < total, 34f);
            AddIconButton(bottomRow, "|>>", () => { replayPlaying = false; replayStopAtCursor = null; ResimulateReplayTo(total); }, replayCursor < total, 34f);
        }
        else
        {
            AddIconButton(bottomRow, "|<<", () => { replayPlaying = false; replayStopAtCursor = null; ResimulateReplayTo(0); }, replayCursor > 0, 34f);
            AddIconButton(bottomRow, "|<", ReplayPrevTurn, replayCursor > 0, 34f);
            AddIconButton(bottomRow, "<", ReplayStepBackward, replayCursor > 0, 30f);
            AddIconButton(bottomRow, replayPlaying ? "❚❚" : "▶", ReplayTogglePlayPause, true, 34f);
            AddIconButton(bottomRow, ">", ReplayStepForward, replayCursor < total, 30f);
            AddIconButton(bottomRow, ">|", ReplayNextTurn, replayCursor < total, 34f);
            AddIconButton(bottomRow, ">>|", () => { replayPlaying = false; replayStopAtCursor = null; ResimulateReplayTo(total); }, replayCursor < total, 34f);
        }

        var timelineArea = new GameObject("Timeline Area").AddComponent<RectTransform>();
        timelineArea.SetParent(bottomRow, false);
        var timelineLe = timelineArea.gameObject.AddComponent<LayoutElement>();
        timelineLe.flexibleWidth = 1f;
        timelineLe.minWidth = 80f;
        DrawReplayTimeline(timelineArea, total);

        var collapseBtn = PanelObject("Collapse", bar, new Color(0, 0, 0, 0));
        collapseBtn.anchorMin = new Vector2(1f, 0.5f); collapseBtn.anchorMax = new Vector2(1f, 0.5f);
        collapseBtn.pivot = new Vector2(1f, 0.5f);
        collapseBtn.sizeDelta = new Vector2(28f, 28f);
        collapseBtn.anchoredPosition = new Vector2(-8f, 0f);
        Round(collapseBtn);
        AddRoundedCardBorder(collapseBtn, MenuB, 1f);
        var collapseText = TextObject("t", collapseBtn, "▼", 12, Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(collapseText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var collapseButton = collapseBtn.gameObject.AddComponent<Button>();
        collapseButton.onClick.AddListener(() => { replayControlsCollapsed = true; SaveReplaySettings(); Render(); });
    }

    // Timeline scrubber: an SFX-slider-style Track/Fill/Handle (see BuildSfxSlider) driving
    // ResimulateReplayTo directly, with small clickable markers overlaid at a curated subset
    // of cursors (spec: turn changes, attacks, card plays, effects, life changes — not EVERY
    // action, which would be unreadable at this scale on a long match).
    private void DrawReplayTimeline(RectTransform parent, int total)
    {
        var track = PanelObject("Timeline Track", parent, new Color(1f, 1f, 1f, 0.10f));
        Stretch(track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Round(track);

        var fillArea = PanelObject("Fill Area", track, new Color(0, 0, 0, 0));
        Stretch(fillArea, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fillArea.GetComponent<Image>().raycastTarget = false;
        var fill = PanelObject("Fill", fillArea, Accent);
        fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.sizeDelta = Vector2.zero;
        fill.anchoredPosition = Vector2.zero;
        Round(fill);
        fill.GetComponent<Image>().raycastTarget = false;

        var handleArea = PanelObject("Handle Area", track, new Color(0, 0, 0, 0));
        Stretch(handleArea, Vector2.zero, Vector2.one, new Vector2(7f, 0f), new Vector2(-7f, 0f));
        handleArea.GetComponent<Image>().raycastTarget = false;
        var handle = PanelObject("Handle", handleArea, new Color32(230, 240, 248, 255));
        handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(10f, 18f);
        handle.anchoredPosition = Vector2.zero;
        Round(handle);

        // Markers drawn UNDER the fill/handle (added to the track before the Slider component,
        // so they sit visually behind the drag surface but are still individually clickable —
        // Slider itself only raycasts its handle, not the whole track, in this build's setup).
        if (replayActions != null && total > 0)
        {
            foreach (var a in replayActions)
            {
                bool isTurnStart = replayTurnStartCursors != null
                    && replayTurnStartCursors.TryGetValue(a.Turn, out int sc) && sc == a.Cursor;
                bool isMarker = isTurnStart || a.Category == "attack" || a.Category == "play"
                    || a.Category == "life" || a.Category == "effect";
                if (!isMarker) continue;

                float t = (float)a.Cursor / total;
                var marker = PanelObject(isTurnStart ? "Turn Marker" : "Marker", track, MarkerColor(a.Category, isTurnStart));
                marker.anchorMin = new Vector2(t, 0f);
                marker.anchorMax = new Vector2(t, 1f);
                marker.pivot = new Vector2(0.5f, 0.5f);
                marker.sizeDelta = new Vector2(isTurnStart ? 3f : 2f, isTurnStart ? 0f : -6f);
                marker.anchoredPosition = Vector2.zero;

                var capturedCursor = a.Cursor;
                var markerBtn = marker.gameObject.AddComponent<Button>();
                markerBtn.transition = Selectable.Transition.None;
                markerBtn.onClick.AddListener(() => { replayPlaying = false; ResimulateReplayTo(capturedCursor); });
            }
        }

        var slider = track.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.minValue = 0f;
        slider.maxValue = Mathf.Max(1, total);
        slider.wholeNumbers = true;
        slider.value = replayCursor;
        slider.onValueChanged.AddListener(v =>
        {
            replayPlaying = false;
            replayStopAtCursor = null;
            int target = Mathf.RoundToInt(v);
            if (target != replayCursor) ResimulateReplayTo(target);
        });
    }

    private static Color MarkerColor(string category, bool isTurnStart)
    {
        if (isTurnStart) return new Color(1f, 1f, 1f, 0.55f);
        switch (category)
        {
            case "attack": return new Color(1f, 0.36f, 0.12f, 0.85f);
            case "play": return new Color(0.42f, 0.82f, 0.52f, 0.75f);
            case "life": return new Color(0.95f, 0.3f, 0.3f, 0.85f);
            case "effect": return new Color(0.55f, 0.7f, 1f, 0.75f);
            default: return new Color(1f, 1f, 1f, 0.3f);
        }
    }

    private void CycleReplayMode()
    {
        int idx = System.Array.IndexOf(ReplayModeOrder, replayMode);
        replayMode = ReplayModeOrder[(idx + 1 + ReplayModeOrder.Length) % ReplayModeOrder.Length];
        replayStopAtCursor = null;
        SaveReplaySettings();
        Render();
    }

    private void CycleReplayDelay()
    {
        int idx = System.Array.IndexOf(ReplayDelayPresets, replayCustomDelaySeconds);
        if (idx < 0) idx = 3; // default fallback (1s) if the value somehow drifted off-preset
        replayCustomDelaySeconds = ReplayDelayPresets[(idx + 1) % ReplayDelayPresets.Length];
        SaveReplaySettings();
        Render();
    }

    private void CycleReplaySpeed()
    {
        int idx = System.Array.IndexOf(ReplaySpeedPresets, replaySpeedMultiplier);
        if (idx < 0) idx = 3; // default fallback (1x)
        replaySpeedMultiplier = ReplaySpeedPresets[(idx + 1) % ReplaySpeedPresets.Length];
        SaveReplaySettings();
        Render();
    }

    private static readonly string[] ReplayViewModeOrder = { "neutral", "south", "north" };
    private void CycleReplayViewMode()
    {
        int idx = System.Array.IndexOf(ReplayViewModeOrder, replayViewMode);
        replayViewMode = ReplayViewModeOrder[(idx + 1 + ReplayViewModeOrder.Length) % ReplayViewModeOrder.Length];
        SaveReplaySettings();
        Render();
    }

    private static readonly string[] ReplayHandVisibilityOrder = { "both", "south", "north", "none" };
    private void CycleReplayHandVisibility()
    {
        int idx = System.Array.IndexOf(ReplayHandVisibilityOrder, replayHandVisibility);
        replayHandVisibility = ReplayHandVisibilityOrder[(idx + 1 + ReplayHandVisibilityOrder.Length) % ReplayHandVisibilityOrder.Length];
        SaveReplaySettings();
        Render();
    }

    // ── Side action-log panel (Phase 5) ─────────────────────────────────────
    // Turn-grouped, collapsible breakdown of the whole match, built straight off
    // replayActions (see ReplayIndex.cs) — no separate data model needed. Uses this file's
    // own VerticalLayoutGroup+ContentSizeFitter scroll pattern (see the combat-log/chat-panel
    // scroll views elsewhere in this file) rather than MainMenuManager's MakeMenuScroll, since
    // that helper lives on a different class.
    private void DrawReplayActionPanel()
    {
        if (replayActionPanelCollapsed)
        {
            var tab = PanelObject("Action Panel Reopen Tab", boardRoot, new Color32(8, 14, 21, 230));
            tab.anchorMin = new Vector2(1f, 1f); tab.anchorMax = new Vector2(1f, 1f);
            tab.pivot = new Vector2(1f, 1f);
            tab.sizeDelta = new Vector2(34f, 130f);
            tab.anchoredPosition = new Vector2(-8f, -90f);
            Round(tab);
            AddRoundedCardBorder(tab, MenuB, 1f);
            var tabText = TextObject("t", tab, "◂ LOG", 11, Ink, TextAnchor.MiddleCenter, monoFont);
            tabText.fontStyle = FontStyle.Bold;
            Stretch(tabText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var tabBtn = tab.gameObject.AddComponent<Button>();
            tabBtn.onClick.AddListener(() => { replayActionPanelCollapsed = false; SaveReplaySettings(); Render(); });
            return;
        }

        const float panelW = 320f;
        const float bottomReserve = 108f; // clears the control bar
        var panel = PanelObject("Replay Action Panel", boardRoot, new Color32(10, 16, 24, 235));
        panel.anchorMin = new Vector2(1f, 0f); panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.sizeDelta = new Vector2(panelW, -bottomReserve);
        panel.anchoredPosition = new Vector2(0f, 0f);
        AddRoundedCardBorder(panel, MenuB, 1f);

        var header = PanelObject("Header", panel, new Color(0, 0, 0, 0));
        Stretch(header, new Vector2(0f, 1f), Vector2.one, new Vector2(12f, -64f), new Vector2(-40f, -8f));
        var title = TextObject("Title", header, "MATCH TIMELINE", 12, Ink, TextAnchor.UpperLeft, monoFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0.55f), Vector2.one, Vector2.zero, Vector2.zero);

        // Compact pills (not AddButton's fixed 118px) so all three fit across this 320px-wide
        // panel's usable width — "Close" is a small corner icon button (mirroring the control
        // bar's collapse arrow) instead of competing for row space. "Main Menu" lives here
        // (rather than as an EXIT pill on the control bar) since leaving a replay is a
        // navigation action, and this is the panel already showing the match overview.
        var toolsRow = PanelObject("Tools", header, new Color(0, 0, 0, 0));
        Stretch(toolsRow, new Vector2(0f, 0f), new Vector2(1f, 0.55f), Vector2.zero, Vector2.zero);
        var trHlg = toolsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        trHlg.spacing = 5f; trHlg.childAlignment = TextAnchor.MiddleLeft;
        trHlg.childControlWidth = false; trHlg.childControlHeight = false;
        trHlg.childForceExpandWidth = false; trHlg.childForceExpandHeight = false;
        AddCompactPill(toolsRow, "Export Position", ExportReplayPosition);
        AddCompactPill(toolsRow, "Expand All", () => { replayCollapsedTurns.Clear(); Render(); });
        AddCompactPill(toolsRow, "Collapse All", () =>
        {
            replayCollapsedTurns.Clear();
            if (replayActions != null) foreach (var a in replayActions) replayCollapsedTurns.Add(a.Turn);
            Render();
        });
        AddCompactPill(toolsRow, "Main Menu", () => ExitReplayToMenu());

        var closeBtn = PanelObject("Close", panel, new Color(0, 0, 0, 0));
        closeBtn.anchorMin = new Vector2(1f, 1f); closeBtn.anchorMax = new Vector2(1f, 1f);
        closeBtn.pivot = new Vector2(1f, 1f);
        closeBtn.sizeDelta = new Vector2(28f, 28f);
        closeBtn.anchoredPosition = new Vector2(-8f, -8f);
        Round(closeBtn);
        AddRoundedCardBorder(closeBtn, MenuB, 1f);
        var closeText = TextObject("t", closeBtn, "✕", 12, Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(closeText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var closeButton = closeBtn.gameObject.AddComponent<Button>();
        closeButton.onClick.AddListener(() => { replayActionPanelCollapsed = true; SaveReplaySettings(); Render(); });

        // "Position copied" confirmation toast (bottom-centre, screen-anchored). Cleared on the
        // next replay navigation (ResimulateReplayTo).
        if (!string.IsNullOrEmpty(restoreExportNote))
        {
            var toast = PanelObject("Export Toast", boardRoot, (Color)new Color32(14, 30, 46, 245));
            toast.anchorMin = new Vector2(0.5f, 0f); toast.anchorMax = new Vector2(0.5f, 0f);
            toast.pivot = new Vector2(0.5f, 0f);
            toast.sizeDelta = new Vector2(560f, 34f);
            toast.anchoredPosition = new Vector2(0f, 82f);
            Round(toast);
            AddRoundedCardBorder(toast, Accent, 1.2f);
            toast.SetAsLastSibling();
            var tt = TextObject("t", toast, restoreExportNote, 11, Accent2, TextAnchor.MiddleCenter, monoFont);
            Stretch(tt.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
        }

        var listArea = PanelObject("List Area", panel, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -72f));

        if (replayActions == null || replayActions.Count == 0)
        {
            var empty = TextObject("Empty", listArea, "No actions recorded.", 11, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, -24f), Vector2.zero);
            return;
        }

        // Group into turns, in order (replayActions is already turn-ordered by construction).
        var turns = new List<(int turn, string seat, List<ReplayAction> actions)>();
        foreach (var a in replayActions)
        {
            if (turns.Count == 0 || turns[turns.Count - 1].turn != a.Turn)
                turns.Add((a.Turn, a.ActiveSeat, new List<ReplayAction>()));
            turns[turns.Count - 1].actions.Add(a);
        }

        var viewport = PanelObject("Viewport", listArea, new Color(0f, 0f, 0f, 0.001f));
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("Timeline Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3f;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;
        AttachScrollbar(scroll);

        const float turnHeaderH = 28f, actionRowH = 24f;
        RectTransform currentActionRect = null;
        foreach (var t in turns)
        {
            bool collapsed = replayCollapsedTurns.Contains(t.turn);
            var headerRow = PanelObject($"Turn {t.turn} Header", content, new Color32(24, 38, 54, 230));
            var headerLE = headerRow.gameObject.AddComponent<LayoutElement>();
            headerLE.preferredHeight = turnHeaderH; headerLE.minHeight = turnHeaderH;
            Round(headerRow);

            var chevron = TextObject("Chevron", headerRow, collapsed ? "▸" : "▾", 11, Muted, TextAnchor.MiddleCenter, monoFont);
            chevron.rectTransform.anchorMin = new Vector2(0f, 0f);
            chevron.rectTransform.anchorMax = new Vector2(0f, 1f);
            chevron.rectTransform.pivot = new Vector2(0f, 0.5f);
            chevron.rectTransform.sizeDelta = new Vector2(22f, 0f);
            chevron.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            var capturedTurnToggle = t.turn;
            var chevronBtn = chevron.gameObject.AddComponent<Button>();
            chevronBtn.onClick.AddListener(() =>
            {
                if (!replayCollapsedTurns.Add(capturedTurnToggle)) replayCollapsedTurns.Remove(capturedTurnToggle);
                Render();
            });

            var headerLabel = TextObject("Label", headerRow, $"Turn {t.turn} — {DisplayName(t.seat)}", 11, Ink, TextAnchor.MiddleLeft, monoFont);
            headerLabel.fontStyle = FontStyle.Bold;
            Stretch(headerLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 0f), new Vector2(-8f, 0f));
            var headerBtn = headerRow.gameObject.AddComponent<Button>();
            headerBtn.transition = Selectable.Transition.None;
            var capturedTurnJump = t.turn;
            headerBtn.onClick.AddListener(() => { replayPlaying = false; ReplayJumpToTurnStart(capturedTurnJump); });

            if (collapsed) continue;
            foreach (var a in t.actions)
            {
                bool isCurrent = a.Cursor == replayCursor;
                var actionRow = PanelObject("Action Row", content,
                    isCurrent ? new Color(Accent.r, Accent.g, Accent.b, 0.30f) : new Color(1f, 1f, 1f, 0.02f));
                var rowLE = actionRow.gameObject.AddComponent<LayoutElement>();
                rowLE.preferredHeight = actionRowH; rowLE.minHeight = actionRowH;
                if (isCurrent) { Round(actionRow); currentActionRect = actionRow; }

                string seatTag = a.Command?.Seat == "south" ? "S" : a.Command?.Seat == "north" ? "N" : "·";
                var actionLabel = TextObject("Label", actionRow, $"{a.Cursor}. [{seatTag}] {TrimForReplayLog(a.Summary)}",
                    10, isCurrent ? Ink : Muted, TextAnchor.MiddleLeft, monoFont);
                Stretch(actionLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 0f), new Vector2(-8f, 0f));
                var actionBtn = actionRow.gameObject.AddComponent<Button>();
                actionBtn.transition = Selectable.Transition.None;
                var capturedCursor = a.Cursor;
                actionBtn.onClick.AddListener(() => { replayPlaying = false; ResimulateReplayTo(capturedCursor); });
            }
        }

        // Auto-scroll so the current action stays roughly centred in view — same
        // ForceUpdateCanvases-then-set-normalized-position idiom the combat log/chat panels
        // already use elsewhere in this file, just computed from the real settled layout
        // rather than scrolling all the way to one end.
        if (currentActionRect != null)
        {
            Canvas.ForceUpdateCanvases();
            float rowY = -currentActionRect.anchoredPosition.y;
            float contentH = content.rect.height;
            float viewportH = viewport.rect.height;
            float scrollable = Mathf.Max(1f, contentH - viewportH);
            float norm = 1f - Mathf.Clamp01((rowY - viewportH * 0.5f) / scrollable);
            scroll.verticalNormalizedPosition = Mathf.Clamp01(norm);
        }
    }

    private static string TrimForReplayLog(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > 64 ? s.Substring(0, 61) + "..." : s;
    }

    // Human-readable description of a DeckLookState's eligibility filter, with a trailing space
    // if non-empty (so callers can splice it directly before "card"). Never renders bare "{}" —
    // each filter is only interpolated when actually present.
    private string DeckLookFilterDesc(DeckLookState dl)
    {
        if (!string.IsNullOrEmpty(dl.NamedCardFilter))
        {
            string typePart = !string.IsNullOrEmpty(dl.CardTypeFilter) ? $" or {dl.CardTypeFilter}" : "";
            return $"[{dl.NamedCardFilter}]{typePart} ";
        }
        string filterDesc = !string.IsNullOrEmpty(dl.FeatureFilter) ? $"{{{dl.FeatureFilter}}} " : "";
        string typeDesc   = !string.IsNullOrEmpty(dl.CardTypeFilter) ? dl.CardTypeFilter + " " : "";
        return filterDesc + typeDesc;
    }

    // "Look at top N cards" overlay (e.g. Jewelry Bonney) - cards pop out extremely large across
    // the center of the screen (augment-pick-screen style). "select" is click-driven (handled by
    // OnCardClick's DeckLook branch); "rearrange" is drag-to-reorder (DeckLookCardDrag) against a
    // finalized by Confirm, which then animates the cards flying into the board deck before the
    // order is actually submitted to the engine.
    private void DrawDeckLookOverlay()
    {
        // The overlay lives on the canvas (above all panels), which Render()'s Clear() does NOT touch,
        // so we must tear down the previous one ourselves or it stacks up every render.
        if (deckLookOverlay != null)
        {
            Destroy(deckLookOverlay.gameObject);
            deckLookOverlay = null;
        }

        if (state.DeckLook == null)
        {
            deckLookWorkingOrder = null;
            deckLookAnimating = false;
            deckLookPeeking = false;
            deckLookRevealSession = null;
            deckLookRevealing = false;
            deckLookScryTopIds = null;
            return;
        }
        var dl = state.DeckLook;
        // INFO PRIVACY: the looked-at cards are private to the searching player, so hide them from the
        // local viewer whenever the search belongs to someone ELSE — the networked opponent, OR the AI
        // opponent in a solo match. Both sides are simulated locally (the cards are in state either
        // way), so we must NOT render them face-up; the non-owner sees only a generic scrim. Hotseat /
        // versus-self is exempt: one person controls both seats, so everything is already theirs.
        bool searchIsOpponents = (isNetworked && dl.Seat != localSeat)
            || (!isNetworked && aiSeat != null && dl.Seat == aiSeat);
        if (searchIsOpponents)
        {
            // Do NOT pop a full-screen scrim for the opponent's (networked peer or solo AI) private
            // search: it merely blocks the human's view of a decision that isn't theirs and resolves on
            // its own. The pending search is already stated in the action log, which is enough. (The
            // previous overlay was already torn down at the top of this method, so nothing lingers.)
            return;
        }
        bool selecting = dl.Step == "select";
        bool scrying = dl.Step == "scry";
        if (scrying && deckLookScryTopIds == null) deckLookScryTopIds = new List<string>();

        // Peeking: player toggled the overlay away to check the board/hand before deciding.
        // Skip the dim + cards entirely so the board underneath (already drawn this Render pass)
        // stays visible; just leave a small button up to bring the overlay back.
        if (deckLookPeeking)
        {
            var peekBtn = PanelObject("Deck Look Peek Btn", canvas.transform, (Color)new Color32(34, 58, 78, 245));
            Stretch(peekBtn, new Vector2(0.80f, 0.918f), new Vector2(0.935f, 0.965f), Vector2.zero, Vector2.zero);
            peekBtn.SetAsLastSibling();
            RoundBig(peekBtn);
            AddRoundedCardBorder(peekBtn, Accent, 1.4f);
            var peekText = TextObject("Text", peekBtn, "Show Cards", 15, Ink, TextAnchor.MiddleCenter, monoFont);
            peekText.fontStyle = FontStyle.Bold;
            Stretch(peekText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var peekButton = peekBtn.gameObject.AddComponent<Button>();
            peekButton.onClick.AddListener(() => { deckLookPeeking = false; Render(); });
            deckLookOverlay = peekBtn;
            return;
        }

        if (!selecting && !scrying)
        {
            bool valid = deckLookWorkingOrder != null && deckLookWorkingOrder.Count == dl.Cards.Count && deckLookWorkingOrder.All(c => dl.Cards.Contains(c));
            if (!valid) deckLookWorkingOrder = new List<CardInstance>(dl.Cards);
        }
        else
        {
            deckLookWorkingOrder = null;
        }

        // Brand new search/look session: reveal cards one at a time from the deck before the
        // player can select, instead of popping all of them into view at once. Guarded by
        // reference identity on dl (a fresh DeckLookState instance per StartDeckLook/StartDeckSearch
        // call), so re-entering this step later (e.g. after the reveal finishes) doesn't restart it.
        bool isNewRevealSession = selecting && dl != deckLookRevealSession;
        if (isNewRevealSession)
        {
            deckLookRevealSession = dl;
            deckLookRevealing = true;
            deckLookPeeking = false;
        }
        bool revealingNow = selecting && deckLookRevealing;

        // Parent to the canvas (not boardRoot) and pin to the very top so the overlay covers the
        // entire game - including the left/side panels - and nothing masks the spread of cards.
        var dim = PanelObject("Deck Look Dim", canvas.transform, new Color32(5, 7, 10, 225));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.SetAsLastSibling();
        deckLookOverlay = dim;

        string titleText;
        if (revealingNow)
        {
            titleText = $"{dl.SourceName} draws from the deck...";
        }
        else if (dl.SearchMode)
        {
            string filterDesc = DeckLookFilterDesc(dl);
            string costDesc   = dl.MaxCost >= 0 ? $" (cost ≤ {dl.MaxCost})" : "";
            titleText = $"{dl.SourceName}: search — choose 1 {filterDesc}card to add to hand{costDesc}";
        }
        else if (scrying)
        {
            // LifeMode scry (Katakuri / Power Mochi "look at Life, place top/bottom") reuses this
            // overlay but the cards are Life cards, not deck cards — label it accordingly.
            string scryZone = dl.LifeMode ? "Life cards" : "deck";
            titleText = $"{dl.SourceName}: click cards to keep on TOP of the {scryZone} (click order = top order) — the rest go to the bottom";
        }
        else if (selecting && dl.FromTrash)
        {
            titleText = $"{dl.SourceName}: play up to {dl.SelectCount} Character(s) from your trash"
                + (dl.DifferentNames ? " — different names (a played name greys out)" : "");
        }
        else
        {
            titleText = selecting
                ? $"{dl.SourceName}: choose up to 1 {DeckLookFilterDesc(dl)}card to add to your hand"
                : "Drag to set the order these return to the bottom of the deck, then Confirm";
        }
        // Title: centered, hugging the card row (same rhythm as the mulligan overlay).
        var title = TextObject("Deck Look Title", dim, titleText, 22, Ink, TextAnchor.LowerCenter);
        Stretch(title.rectTransform, new Vector2(0.08f, 0.845f), new Vector2(0.92f, 0.93f), Vector2.zero, Vector2.zero);

        deckLookCardRects.Clear();

        var row = new GameObject("Deck Look Row").AddComponent<RectTransform>();
        row.SetParent(dim, false);
        // In search mode always show all (revealed) cards in select step; rearrange never happens.
        var displayCards = (selecting || scrying || dl.SearchMode) ? dl.Cards : deckLookWorkingOrder;
        var cardSize = DeckLookCardSize(displayCards?.Count ?? 0, selecting || scrying || dl.SearchMode);
        Stretch(row, new Vector2(0.03f, 0.215f), new Vector2(0.97f, 0.835f), Vector2.zero, Vector2.zero);
        DrawDeckLookCards(row, displayCards, selecting || scrying || dl.SearchMode, cardSize);

        // Scry: badge the chosen TOP cards with their placement order.
        if (scrying && deckLookScryTopIds != null)
        {
            for (int ti = 0; ti < deckLookScryTopIds.Count; ti++)
            {
                if (deckLookCardRects.TryGetValue(deckLookScryTopIds[ti], out var scryHolder) && scryHolder != null)
                    AddBadge(scryHolder, $"TOP {ti + 1}", new Vector2(0.06f, 0.84f), new Vector2(0.62f, 0.97f),
                        new Color32(26, 92, 46, 240));
            }
        }

        if (revealingNow)
        {
            // Hide every card behind a CanvasGroup; the draw coroutine reveals them one at a time
            // and flips deckLookRevealing off (letting the normal select UI take over) once done.
            var holders = new List<RectTransform>(dl.Cards.Count);
            foreach (var card in dl.Cards)
            {
                deckLookCardRects.TryGetValue(card.InstanceId, out var holder);
                if (holder != null)
                {
                    var group = holder.GetComponent<CanvasGroup>();
                    if (group == null) group = holder.gameObject.AddComponent<CanvasGroup>();
                    group.alpha = 0f;
                    group.blocksRaycasts = false;
                    group.interactable = false;
                }
                holders.Add(holder);
            }
            if (isNewRevealSession) StartCoroutine(AnimateDeckLookDraw(dl, holders, cardSize));
            return; // no Take None / Confirm buttons until every card has landed
        }

        // Resolve buttons live ON the overlay (it covers the side-panel actions, so those aren't
        // reachable while a search/look is up).
        // Action buttons: condensed, directly below the cards; the board-view toggle
        // centered beneath them (matches the mulligan overlay).
        if (scrying)
            AddOverlayButton(dim, $"CONFIRM ({deckLookScryTopIds?.Count ?? 0} ON TOP, {dl.Cards.Count - (deckLookScryTopIds?.Count ?? 0)} TO BOTTOM)",
                new Vector2(0.36f, 0.125f), new Vector2(0.64f, 0.185f),
                () => Dispatch(new GameCommand { Type = "deckLookScryConfirm", Seat = dl.Seat, OrderedInstanceIds = new List<string>(deckLookScryTopIds ?? new List<string>()) }));
        else if (dl.SearchMode)
            AddOverlayButton(dim, dl.GameStartStage ? "TAKE NONE" : "TAKE NONE / SHUFFLE", new Vector2(0.40f, 0.125f), new Vector2(0.60f, 0.185f),
                () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
        else if (selecting)
            AddOverlayButton(dim, "TAKE NONE", new Vector2(0.415f, 0.125f), new Vector2(0.585f, 0.185f),
                () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
        else
            AddOverlayButton(dim, deckLookAnimating ? "PLACING..." : "CONFIRM ORDER", new Vector2(0.415f, 0.125f), new Vector2(0.585f, 0.185f),
                ConfirmDeckLookOrder, !deckLookAnimating);

        AddOverlayButton(dim, "VIEW BOARD / HAND", new Vector2(0.40f, 0.052f), new Vector2(0.60f, 0.112f),
            () => { deckLookPeeking = true; Render(); });
    }

    // ---- Trash viewer popup ---------------------------------------------------------------
    // Large browser spanning the play area. Opens by clicking either trash pile (or
    // automatically when an effect targets the trash). Card size scales with the pile
    // (a 50-card trash still fits on screen), the OWNER can drag cards to re-arrange or
    // auto-sort the pile by cost, and SHOW/HIDE tucks the window away to see the board.
    private readonly Dictionary<string, RectTransform> trashCardRects = new Dictionary<string, RectTransform>();
    private int trashGridCols = 1;
    private Vector2 trashGridCell = new Vector2(96f, 134f);
    private Vector2 trashGridOrigin;
    private const float TrashGridSpacing = 8f;

    private void DrawTrashOverlay()
    {
        if (trashOverlay != null)
        {
            Destroy(trashOverlay.gameObject);
            trashOverlay = null;
        }
        if (trashOverlayCatcher != null)
        {
            Destroy(trashOverlayCatcher.gameObject);
            trashOverlayCatcher = null;
        }
        if (state == null || state.DeckLook != null) return;

        string seatToShow = null;
        PendingEffect trashEffect = null;
        if (state.PendingEffects.Count > 0)
        {
            var pe = state.PendingEffects[0];
            bool wantsTrash = pe.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Trash
                || (pe.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Any
                    && (pe.Text ?? "").IndexOf("trash", System.StringComparison.OrdinalIgnoreCase) >= 0);
            // Auto-open only for OUR OWN trash effect. In a networked match that's localSeat; in a solo
            // match it's anything that isn't the AI's seat (hotseat has no AI seat, so both are ours).
            // Previously the solo branch opened for ANY seat, so the AI's trash-search popped the bot's
            // whole trash onto the human's screen unbidden.
            bool trashIsOurs = isNetworked ? pe.Seat == localSeat : (aiSeat == null || pe.Seat != aiSeat);
            if (wantsTrash && trashIsOurs)
            {
                seatToShow = pe.Seat;
                trashEffect = pe;
            }
        }
        if (seatToShow == null) seatToShow = trashViewSeat;
        if (seatToShow == null || !state.Players.TryGetValue(seatToShow, out var trashPlayer)) return;
        var trashCards = trashPlayer.Trash;

        // Browse mode: clicking anywhere OUTSIDE the panel minimizes the trash view
        // (a full-screen invisible catcher behind the panel; the panel blocks it).
        if (trashEffect == null)
        {
            var outside = PanelObject("Trash Outside Catcher", canvas.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(outside, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            outside.SetAsLastSibling();
            var outsideBtn = outside.gameObject.AddComponent<Button>();
            outsideBtn.transition = Selectable.Transition.None;
            outsideBtn.onClick.AddListener(() => { trashViewSeat = null; Render(); });
            trashOverlayCatcher = outside;
        }

        // Big panel across the play area — kept clear of both trash-pile corners so the
        // pile itself stays clickable to close the viewer (click pile or its cards to toggle).
        var panel = PanelObject("Trash Overlay", canvas.transform, new Color32(7, 11, 17, 242));
        Stretch(panel, new Vector2(0.265f, 0.06f), new Vector2(0.735f, 0.94f), Vector2.zero, Vector2.zero);
        panel.SetAsLastSibling();
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 1.6f);
        trashOverlay = panel;

        bool ownerView = !isNetworked ? true : seatToShow == localSeat;
        bool canArrange = trashEffect == null && ownerView && (!isNetworked || seatToShow == localSeat);

        string titleText;
        if (trashEffect != null)
        {
            var srcDef = CardData.GetCard(trashEffect.SourceCardId);
            string extra = trashEffect.SelectionsRemaining > 1 ? $" ({trashEffect.SelectionsRemaining} picks left)" : "";
            titleText = $"{(srcDef != null ? srcDef.Name : "Effect")}: choose a card from the trash{extra}";
        }
        else
        {
            titleText = $"{trashPlayer.Name}'s trash — {trashCards.Count} card(s)" + (canArrange ? "   ·   drag to arrange" : "");
        }
        var title = TextObject("Trash Overlay Title", panel, titleText, 16, Ink, TextAnchor.MiddleLeft, monoFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0.02f, 0.945f), new Vector2(0.60f, 0.995f), Vector2.zero, Vector2.zero);

        // Header buttons: SKIP (optional effect) · SORT BY COST (owner, toggles direction).
        // No CLOSE/HIDE — clicking the trash pile (or its cards) toggles the viewer.
        float bx = 0.985f;
        if (trashEffect != null && trashEffect.Optional)
        {
            AddOverlayButton(panel, "SKIP", new Vector2(bx - 0.12f, 0.945f), new Vector2(bx, 0.995f),
                () => Dispatch(new GameCommand { Type = "passEffect", Seat = trashEffect.Seat, EffectId = trashEffect.EffectId }));
            bx -= 0.13f;
        }
        if (canArrange && trashCards.Count > 1)
        {
            string sortLabel = trashSortLowestFirst ? "SORT: COST LOW→HIGH" : "SORT: COST HIGH→LOW";
            AddOverlayButton(panel, sortLabel, new Vector2(bx - 0.26f, 0.945f), new Vector2(bx, 0.995f),
                () =>
                {
                    Dispatch(new GameCommand { Type = "sortTrash", Seat = seatToShow, Amount = trashSortLowestFirst ? 1 : -1 });
                    trashSortLowestFirst = !trashSortLowestFirst;
                });
        }

        // Card grid area — cell size computed so the WHOLE pile fits (up to ~50+ cards),
        // scaling the cards down as the pile grows. Newest card first.
        var gridArea = new GameObject("Trash Grid Area").AddComponent<RectTransform>();
        gridArea.SetParent(panel, false);
        Stretch(gridArea, new Vector2(0.012f, 0.015f), new Vector2(0.988f, 0.935f), Vector2.zero, Vector2.zero);

        if (trashCards.Count == 0)
        {
            var empty = TextObject("Trash Empty", gridArea, "(trash is empty)", 15, Muted, TextAnchor.MiddleCenter, monoFont);
            Stretch(empty.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return;
        }

        // Solve the largest card size whose grid fits the area.
        Canvas.ForceUpdateCanvases();
        float areaW = Mathf.Max(200f, gridArea.rect.width);
        float areaH = Mathf.Max(200f, gridArea.rect.height);
        if (areaW < 10f || areaH < 10f) { areaW = 980f; areaH = 640f; }   // first-frame fallback
        int n = trashCards.Count;
        // For every row count, the card height is limited by BOTH the column width and the
        // row height; take the layout that yields the biggest card. 2 cards → near full
        // height; 50 cards → small grid. Scales continuously with pile size.
        float bestH = 40f; int bestCols = Mathf.Max(1, n);
        for (int rows = 1; rows <= 10; rows++)
        {
            int cols = Mathf.CeilToInt(n / (float)rows);
            float cwByW = (areaW - (cols - 1) * TrashGridSpacing) / cols;
            float chByH = (areaH - (rows - 1) * TrashGridSpacing) / rows;
            float ch = Mathf.Min(cwByW * 7f / 5f, chByH);
            if (ch > bestH) { bestH = ch; bestCols = cols; }
        }
        // Cap: tiny piles shouldn't produce massive cards — max is only slightly larger
        // than a board card; the grid still scales all the way down for ~50-card piles.
        float trashCapH = boardCardSize.y > 1f ? boardCardSize.y * 1.35f : 190f;
        if (bestH > trashCapH)
        {
            bestH = trashCapH;
            bestCols = Mathf.Max(1, Mathf.FloorToInt((areaW + TrashGridSpacing) / (bestH * 5f / 7f + TrashGridSpacing)));
        }
        trashGridCell = new Vector2(bestH * 5f / 7f, bestH);
        trashGridCols = Mathf.Max(1, bestCols);
        trashGridOrigin = new Vector2(-areaW * 0.5f + trashGridCell.x * 0.5f,
                                       areaH * 0.5f - trashGridCell.y * 0.5f);

        trashCardRects.Clear();
        for (int i = 0; i < trashCards.Count; i++)
        {
            var tc = trashCards[trashCards.Count - 1 - i];   // newest first
            var holder = new GameObject("Trash Overlay Card").AddComponent<RectTransform>();
            holder.SetParent(gridArea, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = trashGridCell;
            holder.anchoredPosition = TrashSlotPosition(i);
            AddCard(holder, tc, null, true, Vector2.zero, true);
            trashCardRects[tc.InstanceId] = holder;
            if (trashEffect != null && !GameEngine.IsValidEffectTarget(state, trashEffect, tc))
            {
                var dimGroup = holder.gameObject.AddComponent<CanvasGroup>();
                dimGroup.alpha = 0.35f;
            }
            if (canArrange)
            {
                var drag = holder.gameObject.AddComponent<TrashCardDrag>();
                drag.Init(this, seatToShow, tc.InstanceId, i, trashCards.Count);
            }
        }
    }

    // Grid slot centre for display index i (row-major, newest first, centered origin).
    private Vector2 TrashSlotPosition(int i)
    {
        int col = i % trashGridCols;
        int row = i / trashGridCols;
        return trashGridOrigin + new Vector2(col * (trashGridCell.x + TrashGridSpacing),
                                             -row * (trashGridCell.y + TrashGridSpacing));
    }

    // Pointer position → display index within the trash grid (clamped).
    private int TrashSlotFromPointer(RectTransform gridArea, Vector2 screenPos, int count)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(gridArea, screenPos, null, out local))
            return -1;
        float stepX = trashGridCell.x + TrashGridSpacing;
        float stepY = trashGridCell.y + TrashGridSpacing;
        int col = Mathf.Clamp(Mathf.RoundToInt((local.x - trashGridOrigin.x) / stepX), 0, trashGridCols - 1);
        int row = Mathf.Clamp(Mathf.RoundToInt((trashGridOrigin.y - local.y) / stepY), 0, Mathf.CeilToInt(count / (float)trashGridCols) - 1);
        return Mathf.Clamp(row * trashGridCols + col, 0, count - 1);
    }

    // Drag-to-arrange for the owner's trash pile. Display order is newest-first, so the
    // drop converts the display index back to a list index before dispatching reorderTrash.
    private sealed class TrashCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GameManager manager;
        private string seat;
        private string instanceId;
        private int displayIndex;
        private int count;
        private RectTransform holder;
        private RectTransform gridArea;
        private CanvasGroup group;
        private bool dragging;

        public void Init(GameManager owner, string trashSeat, string id, int index, int total)
        {
            manager = owner;
            seat = trashSeat;
            instanceId = id;
            displayIndex = index;
            count = total;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            holder = transform as RectTransform;
            gridArea = holder != null ? holder.parent as RectTransform : null;
            if (holder == null || gridArea == null) return;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            holder.SetAsLastSibling();
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || holder == null || gridArea == null) return;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(gridArea, eventData.position, null, out local))
                holder.anchoredPosition = local;
            // Lightweight preview: only the dragged card follows the cursor; the grid
            // re-lays out for real on drop, which reads clearly at these card sizes.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (group != null) group.blocksRaycasts = true;
            if (!dragging || holder == null || gridArea == null) { dragging = false; return; }
            dragging = false;
            int target = manager.TrashSlotFromPointer(gridArea, eventData.position, count);
            if (target < 0 || target == displayIndex) { manager.Render(); return; }
            // display index (newest-first) → list index (oldest-first): list = count-1-display.
            // ReorderTrash inserts BEFORE the list index, so convert the drop slot too.
            int listTarget = count - 1 - target;
            manager.Dispatch(new GameCommand
            {
                Type = "reorderTrash",
                Seat = seat,
                InstanceId = instanceId,
                SlotIndex = listTarget + (target < displayIndex ? 1 : 0),
            });
        }
    }

    private void AddOverlayButton(RectTransform parent, string label, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, bool enabled = true)
    {
        var b = PanelObject(label + " Overlay Btn", parent, enabled ? (Color)new Color32(34, 58, 78, 245) : (Color)new Color32(24, 34, 44, 180));
        Stretch(b, min, max, Vector2.zero, Vector2.zero);
        RoundBig(b);
        AddRoundedCardBorder(b, enabled ? Accent : (Color)new Color32(60, 72, 90, 120), 1.4f);
        var t = TextObject("Text", b, label, 15, enabled ? Ink : (Color)new Color32(130, 140, 156, 170), TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = b.gameObject.AddComponent<Button>();
        btn.interactable = enabled;
        btn.onClick.AddListener(action);
    }

    // Landscape-biased column count for a fit-to-screen grid of `count` cards (>6). Grows with count so a
    // big trash-play (many {Five Elders}) — or a full-deck search — fits without overflowing.
    private int DeckLookGridColumns(int count)
    {
        if (count <= 6) return Mathf.Min(3, Mathf.Max(1, Mathf.CeilToInt(count / 2f)));
        return Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count * 1.9f)), 4, 12);
    }

    private Vector2 DeckLookCardSize(int count, bool selecting)
    {
        // Many cards: scale the card DOWN so an entire grid (all rows) fits the overlay's card area, instead
        // of a fixed size that runs off the bottom of the screen (playtest: a trash full of Five Elders, and
        // full-deck searches). Keeps card aspect ~0.715 (w/h).
        if (count > 6)
        {
            int cols = DeckLookGridColumns(count);
            int rows = Mathf.CeilToInt((float)count / cols);
            float availW = Screen.width * 0.90f;    // the card row spans ~0.03..0.97 of width
            float availH = Screen.height * 0.58f;   //   and ~0.215..0.835 of height
            float cellW = availW / cols - 22f;
            float cellH = availH / rows - 16f;
            cellW = Mathf.Min(cellW, cellH * 0.715f);
            cellW = Mathf.Clamp(cellW, 66f, selecting ? 230f : 200f);
            return new Vector2(cellW, cellW / 0.715f);
        }
        if (!selecting)
        {
            if (count <= 3) return new Vector2(300f, 420f);
            if (count == 4) return new Vector2(252f, 353f);
            if (count == 5) return new Vector2(218f, 305f);
            if (count == 6) return new Vector2(180f, 252f);
            return new Vector2(150f, 210f);
        }
        if (count <= 3) return new Vector2(360f, 504f);
        if (count == 4) return new Vector2(285f, 399f);
        if (count == 5) return new Vector2(235f, 329f);
        if (count == 6) return new Vector2(270f, 378f);
        return new Vector2(230f, 322f);
    }

    private void DrawDeckLookCards(RectTransform row, IList<CardInstance> cards, bool selecting, Vector2 cardSize)
    {
        if (!selecting)
        {
            DrawDeckLookReorderCards(row, cards, cardSize);
            return;
        }

        if (selecting && cards != null && cards.Count > 0 && cards.Count <= 5)
        {
            // Mulligan-style single row with manual slots: cards can be drag-rearranged
            // (cosmetic) and slide around each other live, exactly like the mulligan screen.
            if (deckLookWorkingOrder == null || deckLookWorkingOrder.Count != cards.Count
                || deckLookWorkingOrder.Exists(c => !ContainsInstance(cards, c.InstanceId)))
                deckLookWorkingOrder = new List<CardInstance>(cards);
            for (int i = 0; i < deckLookWorkingOrder.Count; i++)
            {
                var holder = new GameObject("Deck Look Card").AddComponent<RectTransform>();
                holder.SetParent(row, false);
                ApplyDeckLookSlot(holder, row, i, deckLookWorkingOrder.Count, cardSize);
                AddDeckLookChoiceCardVisual(holder, deckLookWorkingOrder[i], true, cardSize);
                var selDrag = holder.gameObject.AddComponent<DeckLookCardDrag>();
                selDrag.Init(this, deckLookWorkingOrder[i].InstanceId, cardSize);
            }
            return;
        }

        if (cards != null && cards.Count > 5)
        {
            var grid = row.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cardSize;
            // Tighter gaps once the grid gets big (matches the fit math in DeckLookCardSize so all rows fit).
            grid.spacing = cards.Count > 6 ? new Vector2(22f, 16f) : (selecting ? new Vector2(34f, 24f) : new Vector2(20f, 18f));
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = DeckLookGridColumns(cards.Count);
        }
        else
        {
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = selecting ? 32 : 18;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        if (cards == null) return;
        foreach (var card in cards) AddDeckLookChoiceCard(row, card, selecting, cardSize);
    }

    private void DrawDeckLookReorderCards(RectTransform row, IList<CardInstance> cards, Vector2 cardSize)
    {
        if (cards == null || cards.Count == 0) return;
        for (int i = 0; i < cards.Count; i++)
        {
            var holder = new GameObject("Deck Look Card").AddComponent<RectTransform>();
            holder.SetParent(row, false);
            ApplyDeckLookSlot(holder, row, i, cards.Count, cardSize);
            AddDeckLookChoiceCardVisual(holder, cards[i], false, cardSize);
        }
    }

    private void ApplyDeckLookSlot(RectTransform holder, RectTransform row, int slot, int count, Vector2 cardSize)
    {
        if (holder == null || row == null) return;
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = cardSize;
        ApplyRectPose(holder, DeckLookSlotPosition(row, slot, count, cardSize), Quaternion.identity, Vector3.one);
    }

    private void SlideDeckLookSlot(RectTransform holder, RectTransform row, int slot, int count, Vector2 cardSize, float lift = 0f, float scale = 1f)
    {
        if (holder == null || row == null) return;
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = cardSize;
        var target = DeckLookSlotPosition(row, slot, count, cardSize) + new Vector2(0f, lift);
        AnimateRectPose(holder, target, Quaternion.identity, Vector3.one * scale, 0.12f);
    }

    private Vector2 DeckLookSlotPosition(RectTransform row, int slot, int count, Vector2 cardSize)
    {
        count = Mathf.Max(1, count);
        slot = Mathf.Clamp(slot, 0, count - 1);
        var width = row != null ? row.rect.width : 0f;
        if (width <= 1f && boardRoot != null) width = boardRoot.rect.width * 0.94f;
        if (width <= 1f) width = Screen.width * 0.77f;
        var step = count <= 1 ? 0f : Mathf.Min(cardSize.x + 72f, Mathf.Max(1f, (width - cardSize.x) / (count - 1)));
        var start = -step * (count - 1) * 0.5f;
        return new Vector2(start + step * slot, 0f);
    }

    private int DeckLookTargetSlot(RectTransform row, Vector2 screenPosition, int count, Vector2 cardSize)
    {
        if (count <= 1) return 0;
        // Each slot's screen-space centre X.
        var slotScreenX = new float[count];
        for (int i = 0; i < count; i++)
        {
            var slotLocal = DeckLookSlotPosition(row, i, count, cardSize);
            var slotWorld = row.TransformPoint(new Vector3(slotLocal.x, slotLocal.y, 0f));
            slotScreenX[i] = RectTransformUtility.WorldToScreenPoint(null, slotWorld).x;
        }
        // Shift at the BOUNDARY between two slots (midpoint of their centres = the shared edge), not
        // when the cursor reaches the next slot's centre - so a card moves as soon as you cross the
        // edge between cards instead of dragging all the way over the neighbour.
        int target = count - 1;
        for (int i = 0; i < count - 1; i++)
        {
            var boundary = (slotScreenX[i] + slotScreenX[i + 1]) * 0.5f;
            if (screenPosition.x < boundary) { target = i; break; }
        }
        return target;
    }

    private RectTransform DeckLookChoiceRow(RectTransform parent, string name, Vector2 min, Vector2 max, float spacing)
    {
        var row = new GameObject(name).AddComponent<RectTransform>();
        row.SetParent(parent, false);
        Stretch(row, min, max, Vector2.zero, Vector2.zero);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static bool ContainsInstance(IList<CardInstance> cards, string instanceId)
    {
        for (int i = 0; i < cards.Count; i++) if (cards[i].InstanceId == instanceId) return true;
        return false;
    }

    private void AddDeckLookChoiceCard(RectTransform parent, CardInstance card, bool selecting, Vector2 cardSize)
    {
        var holder = new GameObject("Deck Look Card").AddComponent<RectTransform>();
        holder.SetParent(parent, false);
        holder.sizeDelta = cardSize;
        SetPreferred(holder, cardSize);
        AddDeckLookChoiceCardVisual(holder, card, selecting, cardSize);
    }

    private void AddDeckLookChoiceCardVisual(RectTransform holder, CardInstance card, bool selecting, Vector2 cardSize)
    {
        var selectable = !selecting || IsDeckLookSelectable(card);
        AddDeckLookChoiceFrame(holder, selecting, selectable);
        AddCard(holder, card, null, true, Vector2.zero, true, false, -1, true);
        // Persistent green rim glow around every VALID pick (not only on hover), so search targets
        // read as valid at a glance — e.g. Imu's start-of-game {Mary Geoise} Stage choices.
        if (selecting && selectable)
        {
            var glowFace = (holder.Find("Card Face") as RectTransform) ?? holder;
            AddUsableGlow(glowFace);
        }
        holder.gameObject.AddComponent<DeckLookSlot>().InstanceId = card.InstanceId;
        // Always tracked (not just during rearrange-drag) so the draw-in-progress reveal animation
        // can look up each card's on-screen holder to hide/reveal and fly a ghost toward.
        deckLookCardRects[card.InstanceId] = holder;

        if (!selecting)
        {
            var drag = holder.gameObject.AddComponent<DeckLookCardDrag>();
            drag.Init(this, card.InstanceId, cardSize);
        }
    }

    private bool IsDeckLookSelectable(CardInstance card)
    {
        var dl = state?.DeckLook;
        if (dl == null || card == null) return false;
        if (dl.Step == "scry") return true;   // scry: every looked card is choosable (top vs bottom)
        if (dl.Step != "select") return false;
        var def = GameEngine.GetCard(card);
        if (def == null) return false;
        if (!string.IsNullOrEmpty(dl.NamedCardFilter))
        {
            // "[Name] or Type card" effects (e.g. Charlotte Pudding: "[Sanji] or Event card") — OR, not AND.
            bool nameMatch = string.Equals(GameEngine.GetEffectiveName(state, card), dl.NamedCardFilter, System.StringComparison.OrdinalIgnoreCase);
            bool typeMatch = !string.IsNullOrEmpty(dl.CardTypeFilter) && string.Equals(def.Type, dl.CardTypeFilter, System.StringComparison.OrdinalIgnoreCase);
            if (!nameMatch && !typeMatch) return false;
        }
        else
        {
            if (!string.IsNullOrEmpty(dl.FeatureFilter) && !def.HasFeature(dl.FeatureFilter)) return false;
            if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !string.Equals(def.Type, dl.CardTypeFilter, System.StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (dl.RequireTrigger && string.IsNullOrEmpty(def.Trigger)) return false;
        if (dl.MaxCost >= 0 && def.Cost > dl.MaxCost) return false;
        if (dl.MinCost >= 0 && def.Cost < dl.MinCost) return false;
        if (dl.MaxPower >= 0 && def.Power > dl.MaxPower) return false;
        if (dl.ExactPower >= 0 && def.Power != dl.ExactPower) return false;
        if (!string.IsNullOrEmpty(dl.ExcludeName) && GameEngine.NameMatches(state, card, dl.ExcludeName)) return false;
        if (!string.IsNullOrEmpty(dl.ColorFilter) && (def.Color ?? "").IndexOf(dl.ColorFilter, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
        // "with different card names": once a name is played this look, its other copies grey out
        // (the green glow drops) so the player sees exactly which trash Characters are still valid.
        if (dl.DifferentNames && dl.PlayedNames != null
            && dl.PlayedNames.Any(n => string.Equals(n, GameEngine.GetEffectiveName(state, card), System.StringComparison.OrdinalIgnoreCase)))
            return false;
        // "Play mode" search effects reject a Character pick outright when the board is full
        // (ResolveDeckLookSelect re-inserts it and logs "No open character slot to play into."
        // without advancing state) — so it was never really selectable to begin with. Excluding
        // it here stops both a false "you can pick this" glow for a human and Basic Bot picking
        // the same unplayable card forever (found via an all-starter-decks batch test).
        if (dl.PlayMode && def.Type == "character" && state.Players.TryGetValue(dl.Seat, out var dlP)
            && !dlP.CharacterArea.Exists(c => c == null))
            return false;
        return true;
    }

    // True when this card can be used right this moment - drives the GREEN hover glow. During a
    // deck search it means the card meets the search criteria; otherwise it means the card is
    // playable now (enough active DON for its cost in main phase) or a valid counter this counter
    // step. All conditions come from the engine, so the glow can never lie about playability.
    private bool IsCardUsableNow(CardInstance card)
    {
        if (card == null || state == null || state.Status != "active") return false;
        if (state.DeckLook != null && state.DeckLook.Step == "select") return IsDeckLookSelectable(card);
        // While an effect is choosing a target (e.g. a Trigger playing a card from hand), valid picks
        // glow green just like normally-playable cards.
        if (state.PendingEffects.Count > 0 && GameEngine.IsValidEffectTarget(state, state.PendingEffects[0], card))
            return true;
        // 6th-character replace: my own Characters are the "play over this one" picks.
        if (state.PendingCharReplace != null && IsCharReplaceTarget(card)) return true;
        return !string.IsNullOrEmpty(card.Owner) && GameEngine.IsPlayableNow(state, card.Owner, card);
    }

    // True when the 6th-character replace prompt is up and belongs to the LOCAL (human) player, and
    // `card` is one of that player's own board Characters — the valid "play over this one" picks.
    private bool IsCharReplaceTarget(CardInstance card)
    {
        var cr = state?.PendingCharReplace;
        if (cr == null || card == null || card.Zone != "character") return false;
        bool mineToResolve = isNetworked ? cr.Seat == localSeat : (aiSeat == null || cr.Seat != aiSeat);
        if (!mineToResolve) return false;
        return state.Players.TryGetValue(cr.Seat, out var p)
               && p.CharacterArea.Any(c => c != null && c.InstanceId == card.InstanceId);
    }

    // Single source of truth for "hovering this card should keep the GREEN valid-target glow, not flip
    // to the neutral gold hover": usable/playable now, a valid pending-effect target (action resolution),
    // or a valid deck-look/scry pick — including game-setup looks like Imu (where Status != "active" makes
    // IsCardUsableNow bail early). Used by every hover-glow + hover-preview path so a valid target reads
    // green consistently.
    internal bool IsGreenTargetNow(CardInstance card) =>
        card != null && (IsCardUsableNow(card) || IsDeckLookSelectable(card));

    // True when an effect is choosing a target, this card sits in that effect's target ZONE, but it is
    // not a legal pick - so the UI flags it with the red "invalid" glow on hover (matching attack targets).
    public bool IsInvalidEffectTarget(CardInstance card)
    {
        if (card == null || state == null || state.PendingEffects.Count == 0) return false;
        var pe = state.PendingEffects[0];
        if (GameEngine.IsValidEffectTarget(state, pe, card)) return false;
        switch (pe.TargetZone)
        {
            case OnePieceTcg.Engine.EffectTargetZone.Hand:  return card.Zone == "hand";
            case OnePieceTcg.Engine.EffectTargetZone.Trash: return card.Zone == "trash";
            case OnePieceTcg.Engine.EffectTargetZone.Play:
            case OnePieceTcg.Engine.EffectTargetZone.Any:   return card.Zone == "character" || card.Zone == "leader";
            default: return false;
        }
    }

    private void SelectDeckLookCard(string instanceId)
    {
        if (state?.DeckLook == null || state.DeckLook.Step != "select")
        {
            Debug.Log($"DeckLook select ignored: no active select step for '{instanceId}'.");
            return;
        }
        var card = state.DeckLook.Cards?.Find(c => c.InstanceId == instanceId);
        if (!IsDeckLookSelectable(card))
        {
            var name = card != null ? GameEngine.GetCard(card)?.Name : "(not found)";
            Debug.Log($"DeckLook select ignored: '{name}' / '{instanceId}' is not selectable.");
            return;
        }

        var seat = state.DeckLook.Seat;
        var beforeHand = state.Players.TryGetValue(seat, out var beforePlayer) ? beforePlayer.Hand.Count : -1;
        var beforeStep = state.DeckLook.Step;
        var selectedName = GameEngine.GetCard(card)?.Name ?? instanceId;
        Debug.Log($"DeckLook select submit: {selectedName} / {instanceId} for {seat}; hand before {beforeHand}; step {beforeStep}.");
        // Capture the picked card's on-screen position BEFORE the dispatch tears down the overlay, so
        // the card can fly from the search into the hand instead of appearing there instantly.
        Vector3 pickStart = (deckLookCardRects.TryGetValue(instanceId, out var pickHolder) && pickHolder != null)
            ? pickHolder.position : Vector3.zero;
        var pickSprite = GetCardSprite(card.CardId);
        Dispatch(new GameCommand { Type = "deckLookSelect", Seat = seat, Target = instanceId });
        var afterHand = state.Players.TryGetValue(seat, out var afterPlayer) ? afterPlayer.Hand.Count : -1;
        var afterStep = state.DeckLook != null ? state.DeckLook.Step : "(closed)";
        Debug.Log($"DeckLook select result: {selectedName} / {instanceId}; hand after {afterHand}; step {afterStep}.");
        // Only animate a true "add to hand" pick (the hand grew) — a play / trash has its own feedback.
        if (pickStart != Vector3.zero && afterHand > beforeHand)
            StartCoroutine(AnimateSearchPickToHand(pickStart, instanceId, pickSprite));
    }

    // A searched card flies from its spot in the search overlay into its new slot in the hand (it used
    // to appear there instantly). The real hand card is hidden until the ghost lands, so it reads as one
    // card moving. Skips gracefully if the landing slot can't be found.
    private IEnumerator AnimateSearchPickToHand(Vector3 startWorldPos, string instanceId, Sprite sprite)
    {
        yield return null;   // let the post-select render place the card into the hand
        var parent = canvas != null ? canvas.transform as RectTransform : null;
        if (parent == null) yield break;
        RectTransform target = cardTargetRects.TryGetValue(instanceId, out var t) ? t : null;
        if (target == null) yield break;

        var hideGrp = target.GetComponent<CanvasGroup>();
        if (hideGrp == null) hideGrp = target.gameObject.AddComponent<CanvasGroup>();
        hideGrp.alpha = 0f;

        var ghost = new GameObject("Search Pick Ghost").AddComponent<RectTransform>();
        ghost.SetParent(parent, false);
        ghost.SetAsLastSibling();
        ghost.sizeDelta = target.rect.size;
        ghost.position = startWorldPos;
        ghost.localScale = Vector3.one * 1.35f;   // starts larger (search card size), shrinks into the hand
        var art = AddRoundedCardImage(ghost, "Art", sprite);
        art.raycastTarget = false;
        var grp = ghost.gameObject.AddComponent<CanvasGroup>(); grp.blocksRaycasts = false;

        float t2 = 0f; const float dur = 0.4f;
        while (t2 < dur && ghost != null && target != null)
        {
            t2 += Time.unscaledDeltaTime;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t2 / dur));
            ghost.position = Vector3.Lerp(startWorldPos, target.position, f);
            ghost.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, f);
            ghost.rotation = Quaternion.Slerp(Quaternion.identity, target.rotation, f);
            yield return null;
        }
        if (hideGrp != null) hideGrp.alpha = 1f;
        if (ghost != null) Destroy(ghost.gameObject);
    }

    private void AddDeckLookChoiceFrame(RectTransform holder, bool selecting, bool selectable = true)
    {
        // Framing removed per design: no purple bloom / yellow corner brackets in the search or
        // return-to-deck UIs. Selectable cards are indicated by the green hover glow instead.
        // Kept as a no-op so existing call sites remain valid.
    }

    private void AddChoiceCorner(RectTransform parent, Vector2 anchor, Color color, float length, float width, float inset)
    {
        var xSign = anchor.x > 0.5f ? -1f : 1f;
        var ySign = anchor.y > 0.5f ? -1f : 1f;

        var horizontal = PanelObject("Choice Frame Corner H", parent, color);
        horizontal.GetComponent<Image>().raycastTarget = false;
        horizontal.anchorMin = anchor;
        horizontal.anchorMax = anchor;
        horizontal.pivot = anchor;
        horizontal.sizeDelta = new Vector2(length, width);
        horizontal.anchoredPosition = new Vector2(xSign * inset, ySign * inset);

        var vertical = PanelObject("Choice Frame Corner V", parent, color);
        vertical.GetComponent<Image>().raycastTarget = false;
        vertical.anchorMin = anchor;
        vertical.anchorMax = anchor;
        vertical.pivot = anchor;
        vertical.sizeDelta = new Vector2(width, length);
        vertical.anchoredPosition = new Vector2(xSign * inset, ySign * inset);
    }

    private void ConfirmDeckLookOrder()
    {
        if (deckLookAnimating || deckLookWorkingOrder == null) return;
        deckLookAnimating = true;
        StartCoroutine(AnimateDeckLookConfirm(new List<CardInstance>(deckLookWorkingOrder), state.DeckLook.Seat));
    }

    private IEnumerator AnimateDeckLookConfirm(List<CardInstance> order, string seat)
    {
        const float stagger = 0.18f;
        const float duration = 1.05f;

        var deckTarget = GetBoardDeckTarget(seat);
        if (deckTarget != null)
        {
            var returnLayer = CreateDeckReturnLayer(deckTarget);
            var ghosts = new List<RectTransform>();
            foreach (var card in order)
            {
                if (deckLookCardRects.TryGetValue(card.InstanceId, out var rect) && rect != null)
                {
                    var ghost = CreateDeckLookReturnGhost(card, rect);
                    var sourceGroup = rect.GetComponent<CanvasGroup>();
                    if (sourceGroup == null) sourceGroup = rect.gameObject.AddComponent<CanvasGroup>();
                    sourceGroup.alpha = 0f;
                    sourceGroup.blocksRaycasts = false;
                    if (ghost != null) ghosts.Add(ghost);
                }
            }
            ApplyFlyingReturnOrder(ghosts);
            for (int i = 0; i < ghosts.Count; i++)
                StartCoroutine(AnimateCardToDeck(ghosts[i], deckTarget, returnLayer, ghosts, i * stagger, duration));
            Debug.Log($"DeckLook return animation launched {ghosts.Count}/{order.Count} card(s) toward {seat} deck.");
            yield return new WaitForSeconds(stagger * Mathf.Max(0, ghosts.Count - 1));
            yield return new WaitForSeconds(duration + 0.18f);
            if (returnLayer != null) Destroy(returnLayer.gameObject);
        }
        else
        {
            Debug.LogWarning($"DeckLook return animation could not find board deck target for seat '{seat}'.");
            yield return new WaitForSeconds(duration + 0.18f);
        }

        Dispatch(new GameCommand { Type = "deckLookConfirmOrder", Seat = seat, OrderedInstanceIds = order.Select(c => c.InstanceId).ToList() });
        deckLookAnimating = false;
        deckLookWorkingOrder = null;
    }

    private RectTransform CreateDeckReturnLayer(RectTransform targetDeck)
    {
        if (targetDeck == null) return null;
        var layer = new GameObject("Deck Bottom Insert Layer").AddComponent<RectTransform>();
        layer.SetParent(targetDeck, false);
        Stretch(layer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        layer.SetAsFirstSibling();
        return layer;
    }

    private RectTransform CreateDeckLookReturnGhost(CardInstance card, RectTransform source)
    {
        if (card == null || source == null) return null;
        var ghost = new GameObject("Deck Look Return Ghost").AddComponent<RectTransform>();
        ghost.SetParent(boardRoot, false);
        ghost.SetAsLastSibling();
        ghost.sizeDelta = source.rect.size;
        ghost.position = source.position;
        ghost.rotation = source.rotation;
        ghost.localScale = Vector3.one;
        AddDeckLookChoiceFrame(ghost, false);
        var art = AddRoundedCardImage(ghost, "Art", GetCardSprite(card.CardId));
        art.raycastTarget = false;
        var group = ghost.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        return ghost;
    }

    private RectTransform GetBoardDeckTarget(string seat)
    {
        if (!string.IsNullOrEmpty(seat) && boardDeckPileRects.TryGetValue(seat, out var pile) && pile != null) return pile;
        return boardDeckPileRects.Values.FirstOrDefault(rect => rect != null);
    }

    private IEnumerator AnimateCardToDeck(RectTransform rect, RectTransform targetDeck, RectTransform returnLayer, List<RectTransform> orderedGhosts, float delay, float duration)
    {
        if (rect == null || targetDeck == null) yield break;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        rect.SetParent(boardRoot, true);
        ApplyFlyingReturnOrder(orderedGhosts);
        var group = rect.GetComponent<CanvasGroup>();
        if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        var startPos = rect.position;
        var startRotation = rect.rotation;
        var startScale = rect.localScale;

        Image artImage = null;
        foreach (var img in rect.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name == "Art") { artImage = img; break; }
        }

        bool flipped = false;
        bool tuckedUnder = false;
        float t = 0f;
        while (t < duration && rect != null)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            rect.position = Vector3.Lerp(startPos, targetDeck.position, SmoothStep(frac));
            rect.rotation = Quaternion.Slerp(startRotation, targetDeck.rotation, SmoothStep(frac));

            // Squash-and-flip: scale.x dives to near-zero at the midpoint (the card edge-on),
            // then opens back up - the sprite swaps to the back right at that thinnest point so
            // the flip reads as the card actually turning over, not just a sprite pop.
            float squash = frac < 0.5f ? Mathf.Lerp(1f, 0.04f, frac / 0.5f) : Mathf.Lerp(0.04f, 1f, (frac - 0.5f) / 0.5f);
            float shrink = Mathf.Lerp(1f, 0.34f, frac);
            rect.localScale = new Vector3(startScale.x * squash * shrink, startScale.y * shrink, startScale.z);

            if (!flipped && frac >= 0.5f)
            {
                flipped = true;
                if (artImage != null) artImage.sprite = GetBackSprite();
            }
            if (!tuckedUnder && frac >= 0.78f)
            {
                tuckedUnder = true;
                if (returnLayer != null)
                {
                    returnLayer.SetAsFirstSibling();
                    rect.SetParent(returnLayer, true);
                    ApplyDeckReturnLayerOrder(returnLayer, orderedGhosts);
                }
            }
            yield return null;
        }
        if (rect != null) Destroy(rect.gameObject);
    }

    private void ApplyFlyingReturnOrder(List<RectTransform> orderedGhosts)
    {
        if (orderedGhosts == null) return;
        for (int i = orderedGhosts.Count - 1; i >= 0; i--)
        {
            var ghost = orderedGhosts[i];
            if (ghost != null && ghost.parent == boardRoot)
                ghost.SetAsLastSibling();
        }
    }

    private static void ApplyDeckReturnLayerOrder(RectTransform returnLayer, List<RectTransform> orderedGhosts)
    {
        if (returnLayer == null || orderedGhosts == null) return;
        returnLayer.SetAsFirstSibling();
        for (int i = orderedGhosts.Count - 1; i >= 0; i--)
        {
            var ghost = orderedGhosts[i];
            if (ghost != null && ghost.parent == returnLayer)
                ghost.SetAsLastSibling();
        }
    }

    private static float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // Reverse of the return-to-deck flight above: cards fly OUT of the deck pile and into their
    // reveal slot, one at a time, before the player is allowed to select anything. The slots
    // themselves are already laid out normally (via DrawDeckLookCards) and hidden behind a
    // CanvasGroup by the caller; each ghost's arrival simply reveals its slot and self-destructs.
    private IEnumerator AnimateDeckLookDraw(DeckLookState dl, List<RectTransform> holders, Vector2 cardSize)
    {
        const float stagger = 0.22f;
        const float duration = 0.5f;

        var deckSource = GetBoardDeckTarget(dl.Seat);
        if (deckSource != null)
        {
            for (int i = 0; i < holders.Count; i++)
            {
                if (holders[i] == null || i >= dl.Cards.Count) continue;
                var ghost = CreateDeckLookDrawGhost(deckSource, cardSize);
                var frontSprite = GetCardSprite(dl.Cards[i].CardId);
                if (ghost != null) StartCoroutine(AnimateCardFromDeck(ghost, holders[i], frontSprite, i * stagger, duration));
            }
            yield return new WaitForSeconds(stagger * Mathf.Max(0, holders.Count - 1));
            yield return new WaitForSeconds(duration + 0.15f);
        }
        else
        {
            // No deck pile rect to fly from (shouldn't normally happen) - just reveal everything.
            foreach (var holder in holders)
            {
                var group = holder != null ? holder.GetComponent<CanvasGroup>() : null;
                if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; group.interactable = true; }
            }
        }

        // Only this method's own session gets to clear deckLookRevealing - if a newer search/look
        // has since replaced state.DeckLook, that session's own coroutine owns the flag instead.
        if (state != null && state.DeckLook == dl)
        {
            deckLookRevealing = false;
            Render();
        }
    }

    // Mulligan deal: fly each of the 5 opening cards from the seat's deck pile to its
    // overlay slot, staggered, flipping from card back to face at the midpoint (reuses
    // AnimateCardFromDeck). Ghosts parent to the overlay itself so they render above
    // the full-screen dim.
    private IEnumerator AnimateMulliganDeal(string seat, List<CardInstance> cards, List<RectTransform> holders, Vector2 cardSize, RectTransform overlayRoot, string dealKey)
    {
        mulliganDealAnimating++;   // holds the Basic Bot's decisions (its dispatch would re-render and cut this short)
        try
        {
            const float stagger = 0.20f;   // ≥ the draw SFX length so each card gets its own full sound
            const float duration = 0.45f;
            var deckSource = GetBoardDeckTarget(seat);
            if (deckSource == null || overlayRoot == null)
            {
                foreach (var holder in holders)
                {
                    var g = holder != null ? holder.GetComponent<CanvasGroup>() : null;
                    if (g != null) { g.alpha = 1f; g.blocksRaycasts = true; g.interactable = true; }
                }
                mulliganAnimShownKey = dealKey;   // nothing to animate; don't retry forever
                yield break;
            }
            for (int i = 0; i < holders.Count && i < cards.Count; i++)
            {
                if (holders[i] == null) continue;
                if (overlayRoot == null) yield break;   // overlay torn down mid-deal: rebuilt one restarts
                var ghost = new GameObject("Mulligan Deal Ghost").AddComponent<RectTransform>();
                ghost.SetParent(overlayRoot, false);
                ghost.SetAsLastSibling();
                ghost.sizeDelta = cardSize;
                ghost.position = deckSource.position;
                ghost.rotation = deckSource.rotation;
                ghost.localScale = Vector3.one * 0.34f;
                var art = AddRoundedCardImage(ghost, "Art", GetBackSprite());
                art.raycastTarget = false;
                var gGroup = ghost.gameObject.AddComponent<CanvasGroup>();
                gGroup.blocksRaycasts = false;
                gGroup.interactable = false;
                PlayCardDrawSfx();
                // Spawn-then-wait (not precomputed delays): a long frame (scene-load hitch)
                // used to consume several delays at once, launching the first cards together.
                StartCoroutine(AnimateCardFromDeck(ghost, holders[i], GetCardSprite(cards[i].CardId), 0f, duration));
                yield return new WaitForSeconds(stagger);
            }
            yield return new WaitForSeconds(0.5f);   // let the last card land before the bot may act
            if (overlayRoot != null) mulliganAnimShownKey = dealKey;   // completed uninterrupted — never replay for this hand
        }
        finally { mulliganDealAnimating--; }
    }

    // Mulligan chosen: the current 5 fly back into the deck pile, the deck riffles (shuffle), THEN the
    // real mulligan is dispatched — the engine returns the hand + draws a fresh 5, whose deal is shown
    // by AnimateMulliganDeal (via mulliganRedrawSeat). Held by mulliganDealAnimating so a bot / a stray
    // re-render can't cut it short. Reuses the deck-look return-flight ghosts.
    private IEnumerator AnimateMulliganReturnAndShuffle(string seat, List<CardInstance> oldHand)
    {
        mulliganDealAnimating++;
        try
        {
            const float stagger = 0.08f;
            const float duration = 0.5f;
            var deckTarget = GetBoardDeckTarget(seat);
            if (deckTarget != null && oldHand != null)
            {
                var returnLayer = CreateDeckReturnLayer(deckTarget);
                var ghosts = new List<RectTransform>();
                foreach (var card in oldHand)
                {
                    if (card != null && mulliganCardRects.TryGetValue(card.InstanceId, out var rect) && rect != null)
                    {
                        var ghost = CreateDeckLookReturnGhost(card, rect);
                        // NB: '??' bypasses Unity's overloaded null check (fake-null), so use an explicit
                        // '== null' test — matching the return-to-deck pattern elsewhere in this file.
                        var sg = rect.GetComponent<CanvasGroup>();
                        if (sg == null) sg = rect.gameObject.AddComponent<CanvasGroup>();
                        sg.alpha = 0f; sg.blocksRaycasts = false;
                        if (ghost != null) ghosts.Add(ghost);
                    }
                }
                // Un-dim: hide the mulligan overlay so the BOARD looks normal while the hand flies back
                // into the deck and the deck bridge-shuffles (ghosts + deck pile live on the board, so
                // they stay visible). The fresh-hand keep/mulligan overlay re-renders after the dispatch.
                if (mulliganOverlay != null) mulliganOverlay.gameObject.SetActive(false);
                for (int i = 0; i < ghosts.Count; i++)
                    StartCoroutine(AnimateCardToDeck(ghosts[i], deckTarget, returnLayer, ghosts, i * stagger, duration));
                yield return new WaitForSeconds(stagger * Mathf.Max(0, ghosts.Count - 1) + duration + 0.12f);
                yield return StartCoroutine(AnimateDeckShuffle(deckTarget));
                if (returnLayer != null) Destroy(returnLayer.gameObject);
            }
            mulliganRedrawSeat = seat;   // the fresh 5 deal is animated on the next render
            Dispatch(new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = true });
        }
        finally { mulliganDealAnimating--; }
    }

    // Bridge/riffle shuffle: the deck splits into a LEFT and RIGHT half that fan apart, then the two
    // halves interleave back together card-by-card (alternating sides) into the pile — reads as the
    // whole deck being shuffled, not just a few cards. Ghosts parent to the deck pile so they ride it.
    private IEnumerator AnimateDeckShuffle(RectTransform deckTarget)
    {
        if (deckTarget == null) yield break;
        PlayCardDrawSfx();
        const int perHalf = 6;                       // 12 riffle cards total
        var size = deckTarget.rect.size;
        var lefts = new List<RectTransform>();
        var rights = new List<RectTransform>();

        RectTransform MakeGhost()
        {
            var g = new GameObject("Shuffle Card").AddComponent<RectTransform>();
            g.SetParent(deckTarget, false);
            g.SetAsLastSibling();
            g.sizeDelta = size;
            g.anchoredPosition = Vector2.zero;
            var art = AddRoundedCardImage(g, "Art", GetBackSprite());
            art.raycastTarget = false;
            return g;
        }
        for (int i = 0; i < perHalf; i++) { lefts.Add(MakeGhost()); rights.Add(MakeGhost()); }

        // Split: the two halves slide apart and fan up.
        float t = 0f; const float splitDur = 0.26f;
        while (t < splitDur)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / splitDur));
            for (int i = 0; i < perHalf; i++)
            {
                float lift = f * (6f + i * 3f);
                if (lefts[i] != null)  { lefts[i].anchoredPosition  = new Vector2(-f * 60f, lift); lefts[i].localRotation  = Quaternion.Euler(0, 0,  f * 8f); }
                if (rights[i] != null) { rights[i].anchoredPosition = new Vector2( f * 60f, lift); rights[i].localRotation = Quaternion.Euler(0, 0, -f * 8f); }
            }
            yield return null;
        }

        // Riffle: drop one card from each side alternately back to centre — the interleave.
        for (int i = perHalf - 1; i >= 0; i--)
        {
            StartCoroutine(DropShuffleCard(rights[i], new Vector2(3f, 0f)));
            yield return new WaitForSeconds(0.035f);
            StartCoroutine(DropShuffleCard(lefts[i], new Vector2(-3f, 0f)));
            yield return new WaitForSeconds(0.035f);
        }
        yield return new WaitForSeconds(0.2f);

        foreach (var g in lefts) if (g != null) Destroy(g.gameObject);
        foreach (var g in rights) if (g != null) Destroy(g.gameObject);
    }

    // One riffle card snapping from its fanned position back into the pile centre.
    private IEnumerator DropShuffleCard(RectTransform g, Vector2 finalOffset)
    {
        if (g == null) yield break;
        var start = g.anchoredPosition;
        var startRot = g.localRotation;
        float t = 0f; const float dur = 0.13f;
        while (t < dur && g != null)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            g.anchoredPosition = Vector2.Lerp(start, finalOffset, f);
            g.localRotation = Quaternion.Slerp(startRot, Quaternion.identity, f);
            g.localScale = new Vector3(1f, 1f - 0.07f * Mathf.Sin(f * Mathf.PI), 1f);
            yield return null;
        }
        if (g != null) { g.anchoredPosition = finalOffset; g.localScale = Vector3.one; g.localRotation = Quaternion.identity; }
    }

    private RectTransform CreateDeckLookDrawGhost(RectTransform deckSource, Vector2 cardSize)
    {
        if (deckSource == null) return null;
        var ghost = new GameObject("Deck Look Draw Ghost").AddComponent<RectTransform>();
        ghost.SetParent(boardRoot, false);
        ghost.SetAsLastSibling();
        ghost.sizeDelta = cardSize;
        ghost.position = deckSource.position;
        ghost.rotation = deckSource.rotation;
        ghost.localScale = Vector3.one * 0.34f;
        AddDeckLookChoiceFrame(ghost, false);
        var art = AddRoundedCardImage(ghost, "Art", GetBackSprite());
        art.raycastTarget = false;
        var group = ghost.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        return ghost;
    }

    private IEnumerator AnimateCardFromDeck(RectTransform rect, RectTransform targetHolder, Sprite frontSprite, float delay, float duration)
    {
        if (rect == null) yield break;
        // Waiting ghosts stay INVISIBLE until their stagger delay elapses — otherwise the
        // next card is already sitting on the deck while the previous one flies, which
        // reads as two cards leaving together.
        var selfGroup = rect.GetComponent<CanvasGroup>();
        if (delay > 0f)
        {
            if (selfGroup != null) selfGroup.alpha = 0f;
            yield return new WaitForSeconds(delay);
        }
        if (rect == null) yield break;
        if (selfGroup != null) selfGroup.alpha = 1f;

        var startPos = rect.position;
        var startRotation = rect.rotation;
        var endPos = targetHolder != null ? targetHolder.position : startPos;
        var endRotation = targetHolder != null ? targetHolder.rotation : startRotation;

        Image artImage = null;
        foreach (var img in rect.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name == "Art") { artImage = img; break; }
        }

        bool flipped = false;
        float t = 0f;
        while (t < duration && rect != null)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            rect.position = Vector3.Lerp(startPos, endPos, SmoothStep(frac));
            rect.rotation = Quaternion.Slerp(startRotation, endRotation, SmoothStep(frac));

            // Mirrors the return-to-deck flip, reversed: the card opens up from edge-on (thin) as
            // it leaves the deck, swapping from its back to its face right at the midpoint.
            float grow = Mathf.Lerp(0.34f, 1f, frac);
            float squash = frac < 0.5f ? Mathf.Lerp(0.04f, 1f, frac / 0.5f) : 1f;
            rect.localScale = new Vector3(grow * squash, grow, 1f);

            if (!flipped && frac >= 0.5f)
            {
                flipped = true;
                if (artImage != null) artImage.sprite = frontSprite;
            }
            yield return null;
        }

        if (targetHolder != null)
        {
            var group = targetHolder.GetComponent<CanvasGroup>();
            if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; group.interactable = true; }
        }
        if (rect != null) Destroy(rect.gameObject);
    }

    private sealed class DeckLookSlot : MonoBehaviour
    {
        public string InstanceId;
    }

    private sealed class DeckLookCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GameManager manager;
        private string instanceId;
        private Vector2 cardSize;
        private RectTransform holder;
        private RectTransform row;
        private CanvasGroup sourceGroup;
        private List<CardInstance> previewOrder;
        private int previewIndex;

        public void Init(GameManager owner, string id, Vector2 size)
        {
            manager = owner;
            instanceId = id;
            cardSize = size;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (manager.deckLookAnimating || manager.deckLookWorkingOrder == null) return;
            holder = transform as RectTransform;
            row = holder != null ? holder.parent as RectTransform : null;
            if (holder == null || row == null) return;

            previewOrder = new List<CardInstance>(manager.deckLookWorkingOrder);
            previewIndex = previewOrder.FindIndex(c => c.InstanceId == instanceId);
            if (previewIndex < 0) return;

            sourceGroup = GetComponent<CanvasGroup>();
            if (sourceGroup == null) sourceGroup = gameObject.AddComponent<CanvasGroup>();
            sourceGroup.alpha = 1f;
            sourceGroup.blocksRaycasts = false;
            holder.SetAsLastSibling();
            ApplyPreviewPositions(true);
            UpdateLivePreview(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (previewOrder == null || holder == null || row == null) return;
            UpdateLivePreview(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (sourceGroup != null)
            {
                sourceGroup.alpha = 1f;
                sourceGroup.blocksRaycasts = true;
            }

            if (!manager.deckLookAnimating && previewOrder != null)
            {
                manager.deckLookWorkingOrder = new List<CardInstance>(previewOrder);
                ApplyPreviewPositions(false);
            }

            previewOrder = null;
            holder = null;
            row = null;
            sourceGroup = null;
        }

        private void OnDisable()
        {
            if (sourceGroup != null)
            {
                sourceGroup.alpha = 1f;
                sourceGroup.blocksRaycasts = true;
            }
        }

        private void UpdateLivePreview(PointerEventData eventData)
        {
            int count = previewOrder.Count;
            int newIndex = manager.DeckLookTargetSlot(row, eventData.position, count, cardSize);
            bool changed = false;
            if (newIndex != previewIndex)
            {
                var oldIndex = previewOrder.FindIndex(c => c.InstanceId == instanceId);
                if (oldIndex < 0) return;
                var card = previewOrder[oldIndex];
                previewOrder.RemoveAt(oldIndex);
                newIndex = Mathf.Clamp(newIndex, 0, previewOrder.Count);
                previewOrder.Insert(newIndex, card);
                previewIndex = newIndex;
                changed = true;
            }
            if (changed) ApplyPreviewPositions(true, true);
        }

        private void ApplyPreviewPositions(bool lifted, bool animate = false)
        {
            if (previewOrder == null || row == null) return;
            int count = previewOrder.Count;
            for (int slot = 0; slot < count; slot++)
            {
                var card = previewOrder[slot];
                if (!manager.deckLookCardRects.TryGetValue(card.InstanceId, out var cardHolder) || cardHolder == null) continue;
                if (card.InstanceId == instanceId && lifted)
                {
                    if (animate) manager.SlideDeckLookSlot(cardHolder, row, slot, count, cardSize, 24f, 1.05f);
                    else
                    {
                        manager.ApplyDeckLookSlot(cardHolder, row, slot, count, cardSize);
                        cardHolder.anchoredPosition += new Vector2(0f, 24f);
                        cardHolder.localScale = Vector3.one * 1.05f;
                    }
                    cardHolder.SetAsLastSibling();
                }
                else if (animate) manager.SlideDeckLookSlot(cardHolder, row, slot, count, cardSize);
                else manager.ApplyDeckLookSlot(cardHolder, row, slot, count, cardSize);
            }
        }
    }

    // Mulligan choice — deck-look-style overlay: your freshly dealt 5 cards pop out large
    // across the center of the screen with KEEP / MULLIGAN buttons underneath, plus a
    // "View Board / Hand" toggle that hides the overlay to inspect the table (a small
    // "Show Cards" button brings it back). Hotseat shows one undecided seat at a time
    // (south first); networked PvP only ever shows the local seat's hand.
    private void DrawMulliganOverlay()
    {
        // Canvas-parented like the deck-look overlay — Render()'s Clear() doesn't touch the
        // canvas, so tear down the previous overlay ourselves.
        if (mulliganOverlay != null)
        {
            Destroy(mulliganOverlay.gameObject);
            mulliganOverlay = null;
        }
        // Post-mulligan: the fresh 5 deal INTO THE BOARD HAND (deck → hand-slot flight),
        // no overlay. While the deal plays, hold off drawing the next decision overlay.
        if (handDealAnimating) return;
        if (mulliganRedrawSeat != null)
        {
            string dealSeat = mulliganRedrawSeat;
            mulliganRedrawSeat = null;
            if (!isNetworked || dealSeat == localSeat)
            {
                handDealAnimating = true;
                StartCoroutine(AnimateHandDeal(dealSeat));
                return;
            }
        }

        if (state.Status != "mulligan")
        {
            mulliganPeeking = false;
            return;
        }

        // Whose decision is on screen right now?
        string seat = null;
        var mulliganSeats = isNetworked ? new[] { BottomSeat }
            : aiSeat != null ? new[] { GameEngine.OtherSeat(aiSeat) }
            : new[] { "south", "north" };
        foreach (var cand in mulliganSeats)
            if (!state.Players[cand].MulliganDecided) { seat = cand; break; }

        if (seat == null)
        {
            // Local player(s) done — just a small status chip while waiting on the peer.
            var wait = TextObject("Mulligan Waiting", boardRoot,
                "READY — waiting for opponent...", 12, Color.white, TextAnchor.MiddleCenter, monoFont);
            Stretch(wait.rectTransform, new Vector2(0.3f, 0.48f), new Vector2(0.7f, 0.52f), Vector2.zero, Vector2.zero);
            wait.raycastTarget = false;
            return;
        }

        var p = state.Players[seat];

        // Peeking: overlay dismissed to look at the board; leave a button to bring it back.
        if (mulliganPeeking)
        {
            var peekBtn = PanelObject("Mulligan Peek Btn", canvas.transform, (Color)new Color32(34, 58, 78, 245));
            Stretch(peekBtn, new Vector2(0.80f, 0.918f), new Vector2(0.935f, 0.965f), Vector2.zero, Vector2.zero);
            peekBtn.SetAsLastSibling();
            RoundBig(peekBtn);
            AddRoundedCardBorder(peekBtn, Accent, 1.4f);
            var peekText = TextObject("Text", peekBtn, "Show Cards", 15, Ink, TextAnchor.MiddleCenter, monoFont);
            peekText.fontStyle = FontStyle.Bold;
            Stretch(peekText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var peekButton = peekBtn.gameObject.AddComponent<Button>();
            peekButton.onClick.AddListener(() => { mulliganPeeking = false; Render(); });
            mulliganOverlay = peekBtn;
            return;
        }

        // Full-screen dim over everything (side panels included), like the search overlay.
        var dim = PanelObject("Mulligan Dim", canvas.transform, new Color32(5, 7, 10, 225));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.SetAsLastSibling();
        mulliganOverlay = dim;

        // Title: centered, tight above the card row.
        string who = isNetworked ? "Your opening hand" : (p.Name + "'s opening hand");
        var title = TextObject("Mulligan Title", dim,
            who + " — keep these 5, or mulligan once for a fresh 5", 22, Ink, TextAnchor.LowerCenter);
        Stretch(title.rectTransform, new Vector2(0.10f, 0.845f), new Vector2(0.90f, 0.92f), Vector2.zero, Vector2.zero);

        // Going first / second — shown on the mulligan screen so the keep-or-mulligan call has context.
        bool mulGoingFirst = seat == state.FirstPlayer;
        var orderLabel = TextObject("Mulligan Order", dim,
            (isNetworked ? "You're going " : (p.Name + " goes ")) + (mulGoingFirst ? "FIRST" : "SECOND"),
            16, Accent, TextAnchor.LowerCenter, monoFont);
        orderLabel.fontStyle = FontStyle.Bold;
        Stretch(orderLabel.rectTransform, new Vector2(0.10f, 0.923f), new Vector2(0.90f, 0.968f), Vector2.zero, Vector2.zero);

        var dragHint = TextObject("Mulligan Drag Hint", dim,
            "drag cards to arrange your hand", 11, Muted, TextAnchor.UpperCenter, monoFont);
        Stretch(dragHint.rectTransform, new Vector2(0.30f, 0.805f), new Vector2(0.70f, 0.842f), Vector2.zero, Vector2.zero);
        dragHint.raycastTarget = false;

        // The 5 dealt cards — slot-positioned (not a layout group) so they can be
        // drag-reordered exactly like the deck-look rearrange step. Dropping a card
        // dispatches the engine's reorderHand command, so the new order is real.
        var row = new GameObject("Mulligan Row").AddComponent<RectTransform>();
        row.SetParent(dim, false);
        Stretch(row, new Vector2(0.03f, 0.215f), new Vector2(0.97f, 0.80f), Vector2.zero, Vector2.zero);
        var cardSize = DeckLookCardSize(p.Hand.Count, true) * 0.92f;
        // Deal animation: the first time this exact hand is shown (including the fresh 5
        // after a mulligan), the cards fly out of the deck pile one by one, flipping face
        // up mid-flight — same feel as the search/deck-look reveal. The fingerprint is
        // order-independent so drag-reordering doesn't re-trigger it.
        var handIds = new List<string>();
        foreach (var hc in p.Hand) handIds.Add(hc.InstanceId);
        handIds.Sort(System.StringComparer.Ordinal);
        string dealKey = seat + ":" + string.Join(",", handIds.ToArray());
        bool animateDeal = dealKey != mulliganAnimShownKey;

        mulliganCardRects.Clear();
        var dealHolders = new List<RectTransform>();
        var dealCards = new List<CardInstance>();
        for (int mi = 0; mi < p.Hand.Count; mi++)
        {
            var card = p.Hand[mi];
            var holder = new GameObject("Mulligan Card").AddComponent<RectTransform>();
            holder.SetParent(row, false);
            ApplyDeckLookSlot(holder, row, mi, p.Hand.Count, cardSize);
            AddCard(holder, card, null, true, Vector2.zero, true);
            mulliganCardRects[card.InstanceId] = holder;
            var drag = holder.gameObject.AddComponent<MulliganCardDrag>();
            drag.Init(this, seat, card.InstanceId, cardSize);
            if (animateDeal)
            {
                var hideGroup = holder.gameObject.AddComponent<CanvasGroup>();
                hideGroup.alpha = 0f;
                hideGroup.blocksRaycasts = false;
                hideGroup.interactable = false;
                dealHolders.Add(holder);
                dealCards.Add(card);
            }
        }
        if (animateDeal)
        {
            // NOTE: mulliganAnimShownKey is set by the coroutine on COMPLETION, not here —
            // if a re-render tears the overlay down mid-deal (coin-flip outro, a bot
            // dispatch), the rebuilt overlay restarts the deal instead of popping the
            // cards in fully visible.
            StartCoroutine(AnimateMulliganDeal(seat, dealCards, dealHolders, cardSize, dim, dealKey));
        }

        // Decision buttons — condensed, side by side, directly under the cards;
        // the board-view toggle centered beneath them (matches the sketch).
        string capSeat = seat;
        AddOverlayButton(dim, "KEEP HAND", new Vector2(0.335f, 0.125f), new Vector2(0.495f, 0.185f),
            () => Dispatch(new GameCommand { Type = "mulliganDecision", Seat = capSeat, Mulligan = false }));
        var capP = p;
        AddOverlayButton(dim, "MULLIGAN", new Vector2(0.505f, 0.125f), new Vector2(0.665f, 0.185f),
            () =>
            {
                if (mulliganDealAnimating > 0) return;   // ignore taps mid-animation
                // Animate the current 5 flying back into the deck + a shuffle, THEN mulligan (which
                // draws the fresh 5, itself animated via mulliganRedrawSeat / AnimateMulliganDeal).
                StartCoroutine(AnimateMulliganReturnAndShuffle(capSeat, new List<CardInstance>(capP.Hand)));
            });
        AddOverlayButton(dim, "VIEW BOARD / HAND", new Vector2(0.40f, 0.052f), new Vector2(0.60f, 0.112f),
            () => { mulliganPeeking = true; Render(); });

        // Hotseat courtesy note: whose turn to decide next.
        if (!isNetworked && !state.Players[OtherSeatLocal(seat)].MulliganDecided && seat == "south")
        {
            var next = TextObject("Mulligan Next", dim, DisplayName(OtherSeatLocal(seat)) + " decides next", 11, Muted, TextAnchor.MiddleCenter, monoFont);
            Stretch(next.rectTransform, new Vector2(0.35f, 0.012f), new Vector2(0.65f, 0.042f), Vector2.zero, Vector2.zero);
        }
    }

    // Post-mulligan hand deal: the fresh 5 fly one at a time from the deck pile into
    // their real positions in the player's on-board hand (face-up for the bottom/local
    // hand, card backs for the top hand). No overlay — the cards land where they live.
    private bool handDealAnimating;
    private int mulliganDealAnimating;    // opening-hand overlay deals in progress (AnimateMulliganDeal; counter — an interrupted deal's teardown must not unblock a restarted one)

    private IEnumerator AnimateHandDeal(string seat)
    {
        // Let the current Render finish laying out the new hand first.
        yield return null;
        const float stagger = 0.20f;   // ≥ the draw SFX length: one full sound per card
        const float duration = 0.45f;
        var pDeal = state != null && state.Players.ContainsKey(seat) ? state.Players[seat] : null;
        var deckSource = GetBoardDeckTarget(seat);
        if (pDeal == null || deckSource == null)
        {
            handDealAnimating = false;
            Render();
            yield break;
        }

        bool faceUp = !isNetworked || seat == localSeat;   // hotseat: both hands render face-up
        var targets = new List<RectTransform>();
        var sprites = new List<Sprite>();
        if (faceUp)
        {
            foreach (var hc in pDeal.Hand)
            {
                if (handCardRects.TryGetValue(hc.InstanceId, out var rt) && rt != null)
                {
                    targets.Add(rt);
                    sprites.Add(GetCardSprite(hc.CardId));
                }
            }
        }
        else
        {
            for (int i = 0; i < opponentHandSlots.Count; i++)
            {
                if (opponentHandSlots[i] != null)
                {
                    targets.Add(opponentHandSlots[i]);
                    sprites.Add(GetBackSprite());
                }
            }
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var hideGroup = targets[i].GetComponent<CanvasGroup>();
            if (hideGroup == null) hideGroup = targets[i].gameObject.AddComponent<CanvasGroup>();
            hideGroup.alpha = 0f;
        }
        for (int i = 0; i < targets.Count; i++)
        {
            var ghost = new GameObject("Hand Deal Ghost").AddComponent<RectTransform>();
            ghost.SetParent(canvas.transform, false);
            ghost.SetAsLastSibling();
            ghost.sizeDelta = new Vector2(boardCardSize.x, boardCardSize.y);
            ghost.position = deckSource.position;
            ghost.rotation = deckSource.rotation;
            ghost.localScale = Vector3.one * 0.34f;
            var art = AddRoundedCardImage(ghost, "Art", GetBackSprite());
            art.raycastTarget = false;
            var gGroup = ghost.gameObject.AddComponent<CanvasGroup>();
            gGroup.blocksRaycasts = false;
            gGroup.interactable = false;
            PlayCardDrawSfx();
            StartCoroutine(AnimateCardFromDeck(ghost, targets[i], sprites[i], 0f, duration));
            yield return new WaitForSeconds(stagger);
        }
        yield return new WaitForSeconds(duration + 0.1f);
        handDealAnimating = false;
        Render();
    }

    // Drag-to-reorder for the mulligan overlay: same slot mechanics as the deck-look
    // rearrange step, but the drop is committed through the engine's reorderHand command
    // so the arranged order IS the real hand order when the match starts.
    private readonly Dictionary<string, RectTransform> mulliganCardRects = new Dictionary<string, RectTransform>();

    private sealed class MulliganCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GameManager manager;
        private string seat;
        private string instanceId;
        private Vector2 cardSize;
        private RectTransform holder;
        private RectTransform row;
        private CanvasGroup group;
        private List<string> previewOrder;
        private int startIndex;

        public void Init(GameManager owner, string handSeat, string id, Vector2 size)
        {
            manager = owner;
            seat = handSeat;
            instanceId = id;
            cardSize = size;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            holder = transform as RectTransform;
            row = holder != null ? holder.parent as RectTransform : null;
            if (holder == null || row == null || manager.state == null) return;
            previewOrder = manager.state.Players[seat].Hand.Select(c => c.InstanceId).ToList();
            startIndex = previewOrder.IndexOf(instanceId);
            if (startIndex < 0) { previewOrder = null; return; }
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            holder.SetAsLastSibling();
            UpdatePreview(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (previewOrder == null || holder == null) return;
            UpdatePreview(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (group != null) group.blocksRaycasts = true;
            if (previewOrder == null || holder == null || row == null) return;
            int target = manager.DeckLookTargetSlot(row, eventData.position, previewOrder.Count, cardSize);
            previewOrder = null;
            holder = null;
            row = null;
            if (target != startIndex)
                // ReorderHand treats SlotIndex as a pre-removal insertion point, so moving
                // right needs +1 to land on the intended final slot (same as EngineHandTargetIndex).
                manager.Dispatch(new GameCommand { Type = "reorderHand", Seat = seat, InstanceId = instanceId, SlotIndex = startIndex < target ? target + 1 : target });
            else
                manager.Render();   // snap everything back
        }

        private void UpdatePreview(PointerEventData eventData)
        {
            // Dragged card follows the cursor.
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(row, eventData.position, null, out local))
                holder.anchoredPosition = local;
            // Others slide into the projected order.
            int target = manager.DeckLookTargetSlot(row, eventData.position, previewOrder.Count, cardSize);
            var projected = new List<string>(previewOrder);
            projected.Remove(instanceId);
            projected.Insert(Mathf.Clamp(target, 0, projected.Count), instanceId);
            for (int i = 0; i < projected.Count; i++)
            {
                if (projected[i] == instanceId) continue;
                RectTransform other;
                if (manager.mulliganCardRects.TryGetValue(projected[i], out other) && other != null)
                    manager.SlideDeckLookSlot(other, row, i, projected.Count, cardSize);
            }
        }
    }

    private static string OtherSeatLocal(string seat) => seat == "south" ? "north" : "south";

    private void DrawCoinFlipOverlay()
    {
        if (state.Status != "coinflip") { coinFlipWaitingText = null; coinFlipRevealed = false; coinFlipSpinStarted = false; return; }

        var dim = PanelObject("Coin Flip Dim", boardRoot, new Color32(8, 10, 14, 200));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var panel = PanelObject("Coin Flip Panel", boardRoot, (Color)new Color32(14, 30, 46, 250));
        Stretch(panel, new Vector2(0.30f, 0.38f), new Vector2(0.70f, 0.62f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 2f);

        var winner = state.Players[state.CoinFlipWinner];

        // Spin first: a coin flips for ~1.3s (both clients see it), and only when it lands does the
        // winner + Go First/Second choice appear. The coroutine parents the coin to `panel`; nothing
        // forces a re-render mid-spin (no bot acts during the coin flip), so it plays uninterrupted.
        if (!coinFlipRevealed)
        {
            coinFlipWaitingText = null;
            var flipLabel = TextObject("Coin Flip Text", panel, "Flipping the coin…", 15, Muted, TextAnchor.UpperCenter, titleFont);
            Stretch(flipLabel.rectTransform, new Vector2(0.06f, 0.74f), new Vector2(0.94f, 0.96f), Vector2.zero, Vector2.zero);
            if (!coinFlipSpinStarted) { coinFlipSpinStarted = true; StartCoroutine(AnimateCoinFlip(panel)); }
            return;
        }

        // Networked PvP: only the coin-flip winner sees the Go First/Second choice - the other
        // client gets a waiting message (with an animated ellipsis, see Update()) instead of a
        // second copy of buttons they have no business clicking. Hotseat/Versus Self is
        // unaffected (isNetworked is false there), matching how mulligan was scoped earlier.
        if (isNetworked && state.CoinFlipWinner != localSeat)
        {
            coinFlipWaitingBaseMessage = $"Waiting for {DisplayName(state.CoinFlipWinner)} to decide going first or second";
            var waitLabel = TextObject("Coin Flip Text", panel, coinFlipWaitingBaseMessage,
                16, Ink, TextAnchor.MiddleCenter, titleFont);
            waitLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(waitLabel.rectTransform, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
            coinFlipWaitingText = waitLabel;
            return;
        }

        coinFlipWaitingText = null;
        var label = TextObject("Coin Flip Text", panel, $"{DisplayName(state.CoinFlipWinner)} won the coin flip!\nGoing first or second?", 16, Ink, TextAnchor.MiddleCenter, titleFont);
        Stretch(label.rectTransform, new Vector2(0.04f, 0.55f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero);

        var buttons = RowObject("Coin Flip Buttons", panel, 14, TextAnchor.MiddleCenter);
        Stretch(buttons, new Vector2(0.10f, 0.12f), new Vector2(0.90f, 0.48f), Vector2.zero, Vector2.zero);
        AddButton(buttons, "Go First", () => Dispatch(new GameCommand { Type = "chooseTurnOrder", Seat = state.CoinFlipWinner, GoingFirst = true }));
        AddButton(buttons, "Go Second", () => Dispatch(new GameCommand { Type = "chooseTurnOrder", Seat = state.CoinFlipWinner, GoingFirst = false }));
    }

    // Spins a gold coin (edge-on squash + an up-and-down arc) on the coin-flip panel for ~1.3s, then
    // flips coinFlipRevealed and re-renders to show the winner + first/second choice. Guarded against
    // the panel being torn down mid-spin. Uses unscaled time so it plays regardless of any pause.
    private IEnumerator AnimateCoinFlip(RectTransform panel)
    {
        if (panel == null) { coinFlipRevealed = true; yield break; }
        var coin = PanelObject("Coin", panel, (Color)new Color32(226, 188, 74, 255));
        coin.anchorMin = coin.anchorMax = new Vector2(0.5f, 0.46f);
        coin.pivot = new Vector2(0.5f, 0.5f);
        coin.sizeDelta = new Vector2(78f, 78f);
        coin.anchoredPosition = Vector2.zero;
        RoundCircle(coin);                       // a real disc, not a rounded square
        var coinImg = coin.GetComponent<Image>();
        // H / T face — swaps each half-flip and inherits the coin's squash, so it turns with the disc.
        var faceText = TextObject("Coin Face", coin, "H", 38, (Color)new Color32(58, 42, 10, 255), TextAnchor.MiddleCenter, titleFont);
        faceText.fontStyle = FontStyle.Bold;
        Stretch(faceText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        faceText.raycastTarget = false;

        const float dur = 1.35f;
        float t = 0f;
        while (t < dur && coin != null && panel != null)
        {
            t += Time.unscaledDeltaTime;
            float spins = t * 8.5f;                                 // ~8-9 half-flips over the toss
            float cos = Mathf.Cos(spins);
            float squash = Mathf.Abs(cos);                         // 0 = edge-on, 1 = face-on
            coin.localScale = new Vector3(1f, squash * 0.86f + 0.14f, 1f);
            coin.anchoredPosition = new Vector2(0f, Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI) * 66f); // toss arc
            // Shade the face darker at the edge-on point so the flip reads as a real 3-D turn.
            if (coinImg != null)
            {
                float b = 0.55f + 0.45f * squash;
                coinImg.color = new Color(0.89f * b, 0.74f * b, 0.29f * b, 1f);
            }
            if (faceText != null)
            {
                faceText.text = cos >= 0f ? "H" : "T";             // front face = Heads, back = Tails
                var fc = faceText.color; fc.a = squash;            // fade the letter out at the edge-on point
                faceText.color = fc;
            }
            yield return null;
        }
        // Land face-on and HOLD so the player can read the H/T result before the winner is shown.
        if (coin != null) { coin.localScale = Vector3.one; coin.anchoredPosition = Vector2.zero; }
        if (coinImg != null) coinImg.color = new Color(0.89f, 0.74f, 0.29f, 1f);
        if (faceText != null) { var fc = faceText.color; fc.a = 1f; faceText.color = fc; }
        yield return new WaitForSecondsRealtime(0.9f);
        coinFlipRevealed = true;
        Render();
    }

    private void DrawTableSurface()
    {
        // Full-bleed coloured playmat background spanning the ENTIRE window: north (blue) fills the top
        // half, south (green) the bottom half, with a thin centre divide. The play space (zones, cards)
        // is drawn at full size on top of this in playRoot; the colour bleeds out to every edge.
        // Cobalt felt: dark navy (north) over dark teal (south), full-bleed behind the opaque menus.
        // Tournament/replay layout: players sit left/right instead of top/bottom, so the wash
        // split follows suit, and the seam + turn-crest pill get wrapped in a rotated container
        // below (RotateFullForReplay) so they read as a vertical line down the middle instead
        // of a horizontal one crossing both halves.
        var north = PanelObject("North Table Wash", boardRoot, MatTop);
        var south = PanelObject("South Table Wash", boardRoot, MatBottom);
        if (IsReplayRotated)
        {
            // South (bottom seat) -> left, North (top seat) -> right, per the tournament pivot.
            Stretch(north, new Vector2(0.5f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            Stretch(south, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        }
        else
        {
            Stretch(north, new Vector2(0f, 0.5f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            Stretch(south, Vector2.zero, new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        }
        AddBoardCancel(north);
        AddBoardCancel(south);

        // Everything below is confined to the centered play column (x 0.17-0.83 of the window).
        // (Center brightening glow removed — the push/pull bar is the center focal element now,
        //  and the soft radial lift was reading as a hazy band around the bar.)

        // (Active-side glow removed - the active player's empty-slot placement indicators are the
        // whose-turn cue now.)

        // Center seam + turn-crest pill all live under this parent — boardRoot normally, or a
        // wrapper rotated -90° (same swapped-size-then-rotate trick as RotateHalfForReplay) in
        // replay mode, so the whole assembly (line + pill + text) reads as a vertical seam
        // matching the rotated board instead of the normal-mode horizontal one.
        RectTransform crestParent = boardRoot;
        if (IsReplayRotated)
        {
            crestParent = PanelObject("Center Seam Assembly", boardRoot, new Color(0, 0, 0, 0));
            RotateFullForReplay(crestParent, boardRoot);
        }

        // Push/Pull life bar across the center seam — a tug-of-war between each player's deck colours,
        // with a gold seam at the life balance point. Replaces the old center "TURN · X'S TURN" pill
        // (the turn wording still lives in the right side panel). Stays on boardRoot so it draws ABOVE
        // the mat felt (parenting into playRoot buried it behind the later-drawn character-area panels).
        turnCrestRect = DrawPushPullBar(crestParent);

        // Field corner brackets (cyan L's) at the four extreme corners of the whole play space. The arms
        // hug the outer perimeter (top/bottom + side edges) rather than cutting through the middle of the
        // board, and taper inward like the centre seam.
        // (Corner vertical lines removed - visual clutter.)
    }

    // ── Push/Pull life bar (center of the board) ──────────────────────────────
    // Deck-colour hexes (verbatim from the deck palette): a tug-of-war bar where each player's half is
    // painted in their leader's colour(s) and a gold seam sits at the life balance point.
    private static readonly Dictionary<string, Color32> PushPullDeckColors = new Dictionary<string, Color32>(System.StringComparer.OrdinalIgnoreCase)
    {
        { "Red", new Color32(214, 68, 68, 255) },   { "Green", new Color32(70, 180, 110, 255) },
        { "Blue", new Color32(70, 140, 220, 255) },  { "Purple", new Color32(160, 110, 210, 255) },
        { "Black", new Color32(38, 40, 52, 255) }, { "Yellow", new Color32(230, 200, 90, 255) },
    };

    private List<Color> PushPullColorsFor(string seat)
    {
        var list = new List<Color>();
        if (state != null && state.Players.TryGetValue(seat, out var p) && p.Leader != null)
        {
            var def = GameEngine.GetCard(p.Leader);
            foreach (var part in (def?.Color ?? "").Split('/'))
                if (PushPullDeckColors.TryGetValue(part.Trim(), out var c)) list.Add(c);
        }
        if (list.Count == 0) list.Add(new Color32(90, 100, 120, 255));
        return list;
    }

    private readonly Dictionary<string, Texture2D> _pushPullFillCache = new Dictionary<string, Texture2D>();
    private Texture2D PushPullFillTexture(List<Color> colors)
    {
        string key = string.Join(",", colors.ConvertAll(c => ColorUtility.ToHtmlStringRGB(c)));
        if (_pushPullFillCache.TryGetValue(key, out var cached) && cached != null) return cached;
        const int w = 96;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < w; x++)
        {
            float t = colors.Count <= 1 ? 0f : (float)x / (w - 1) * (colors.Count - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, colors.Count - 1);
            int j = Mathf.Min(i + 1, colors.Count - 1);
            tex.SetPixel(x, 0, Color.Lerp(colors[i], colors[j], t - i));
        }
        tex.Apply();
        _pushPullFillCache[key] = tex;
        return tex;
    }

    private Texture2D _pushPullGlossTex;
    private Texture2D PushPullGlossTexture()
    {
        if (_pushPullGlossTex != null) return _pushPullGlossTex;
        const int h = 32;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);   // 0 bottom, 1 top
            Color c = v > 0.55f
                ? new Color(1f, 1f, 1f, Mathf.Lerp(0f, 0.14f, (v - 0.55f) / 0.45f))
                : new Color(0f, 0f, 0f, Mathf.Lerp(0.20f, 0f, v / 0.55f));
            tex.SetPixel(0, y, c);
        }
        tex.Apply();
        _pushPullGlossTex = tex;
        return tex;
    }

    private float _pushPullShownFrac = -1f;   // animating balance point, persisted across renders

    private RectTransform DrawPushPullBar(RectTransform parent)
    {
        int myLife = state.Players.TryGetValue(BottomSeat, out var mp) ? mp.Life.Count : 0;
        int oppLife = state.Players.TryGetValue(TopSeat, out var op) ? op.Life.Count : 0;
        int total = myLife + oppLife;
        float target = total == 0 ? 0.5f : (float)myLife / total;
        target = Mathf.Clamp(target, 0.02f, 0.98f);
        if (_pushPullShownFrac < 0f) _pushPullShownFrac = target;
        float frac = _pushPullShownFrac;   // build at the currently-shown value; a driver eases it to target
        var myColors = PushPullColorsFor(BottomSeat);
        var oppColors = PushPullColorsFor(TopSeat);
        bool myTurn = state.ActiveSeat == BottomSeat;

        var bar = new GameObject("Push Pull Bar").AddComponent<RectTransform>();
        bar.SetParent(parent, false);
        // Align the span to the character-area outer edges. Those zones are playRoot fractions
        // (north char left = 0.02, south char right = 0.98); playRoot occupies boardRoot x 0.17-0.83,
        // so playRoot-x f maps to boardRoot-x (0.17 + 0.66f) → 0.02→0.183, 0.98→0.817.
        bar.anchorMin = new Vector2(0.183f, 0.5f);
        bar.anchorMax = new Vector2(0.817f, 0.5f);
        bar.pivot = new Vector2(0.5f, 0.5f);
        bar.offsetMin = new Vector2(0f, -16f);
        bar.offsetMax = new Vector2(0f, 16f);
        bar.SetAsLastSibling();
        // (The quiet fade lives on the track subtree below — NOT the whole bar — so the diamond/chevron
        //  knob can stay more opaque than the semi-transparent fills.)

        // Small life totals centred in the gutter at each end (my life left, opponent's right), deck-
        // colour gradient. Parented to `parent` so they carry their own softer fade, not the bar's.
        AddPushPullEndNumber(parent, 0.177f, myLife, myColors);
        AddPushPullEndNumber(parent, 0.823f, oppLife, oppColors);

        // Exact life is shown by the life-pile badges + side panel; the bar just conveys the balance,
        // so no numbers here (they'd overlap the life/deck piles at the bar's ends anyway).
        const float sideInset = 0f, trackH = 10f;

        var track = PanelObject("Bar Track", bar, (Color)new Color32(8, 17, 32, 120));   // semi-transparent groove (no group fade now)
        track.anchorMin = new Vector2(0f, 0.5f); track.anchorMax = new Vector2(1f, 0.5f);
        track.pivot = new Vector2(0.5f, 0.5f);
        track.offsetMin = new Vector2(sideInset, -trackH * 0.5f);
        track.offsetMax = new Vector2(-sideInset, trackH * 0.5f);
        // Capsule ends: a Mask over a rounded sprite clips the square-cornered fills to a rounded
        // pill (the RoundedCard shader's radius tracks the *min* dimension, so a thin bar barely
        // rounds — the stencil Mask gives proper rounded caps regardless of thinness).
        Round(track);
        var trackMask = track.gameObject.AddComponent<Mask>();
        trackMask.showMaskGraphic = true;
        // Fade the FILLS directly (via their own colour alpha) rather than a CanvasGroup on the whole
        // track — a group would also crush the active-turn glow. This way the fills stay quiet but the
        // glow layers (children of the half) render at full strength.
        Color activeFill = new Color(1f, 1f, 1f, 0.44f);
        Color idleFill = new Color(0.60f, 0.60f, 0.66f, 0.30f);

        var myHalf = new GameObject("My Territory").AddComponent<RectTransform>();
        myHalf.SetParent(track, false);
        myHalf.anchorMin = new Vector2(0f, 0f); myHalf.anchorMax = new Vector2(frac, 1f);
        myHalf.offsetMin = Vector2.zero; myHalf.offsetMax = Vector2.zero;
        var myImg = myHalf.gameObject.AddComponent<RawImage>();
        myImg.texture = PushPullFillTexture(myColors);
        myImg.color = myTurn ? activeFill : idleFill;
        myImg.raycastTarget = false;

        var oppHalf = new GameObject("Opp Territory").AddComponent<RectTransform>();
        oppHalf.SetParent(track, false);
        oppHalf.anchorMin = new Vector2(frac, 0f); oppHalf.anchorMax = new Vector2(1f, 1f);
        oppHalf.offsetMin = Vector2.zero; oppHalf.offsetMax = Vector2.zero;
        var oppImg = oppHalf.gameObject.AddComponent<RawImage>();
        oppImg.texture = PushPullFillTexture(oppColors);
        oppImg.color = !myTurn ? activeFill : idleFill;
        oppImg.raycastTarget = false;

        // Living turn glow on the active half — a colour glow hugging the long edges with a slow
        // smoky shimmer drifting within.
        var activeHalf = myTurn ? myHalf : oppHalf;
        AddPushPullTurnGlow(activeHalf, myTurn ? myColors : oppColors, seamOnLeft: !myTurn);

        var gloss = new GameObject("Gloss").AddComponent<RectTransform>();
        gloss.SetParent(track, false);
        gloss.anchorMin = Vector2.zero; gloss.anchorMax = Vector2.one;
        gloss.offsetMin = Vector2.zero; gloss.offsetMax = Vector2.zero;
        var glossImg = gloss.gameObject.AddComponent<RawImage>();
        glossImg.texture = PushPullGlossTexture();
        glossImg.color = new Color(1f, 1f, 1f, 0.2f);   // fills no longer group-faded, so set directly
        glossImg.raycastTarget = false;

        // (No gold seam line — the colour boundary + diamond mark the balance point; a line through
        // the diamond read as clutter.)

        // Small diamond knob at the balance point (no big round glow — it read as a distracting gold
        // block). Lives OUTSIDE the track as a bar child, pinned to frac by a tiny driver.
        var knob = new GameObject("Seam Knob").AddComponent<RectTransform>();
        knob.SetParent(bar, false);
        knob.anchorMin = knob.anchorMax = new Vector2(0.5f, 0.5f);
        knob.pivot = new Vector2(0.5f, 0.5f);
        knob.sizeDelta = new Vector2(12f, 12f);
        var knobAnchor = knob.gameObject.AddComponent<PushPullSeamAnchor>();
        knobAnchor.Init(bar, frac, sideInset);
        // Mostly opaque so the fill colours don't show through the diamond/chevron.
        var knobGroup = knob.gameObject.AddComponent<CanvasGroup>();
        knobGroup.alpha = 0.9f;
        knobGroup.blocksRaycasts = false;
        knobGroup.interactable = false;

        // Diamond as an UN-ROTATED sprite (rhombus baked into the texture) — NOT a rotated square. A
        // rotated quad snaps to the pixel grid differently than the un-rotated chevron, so at some
        // sub-pixel knob positions (after the bar animates) the two would stagger. Both un-rotated Images
        // snap identically, so they stay locked together at every position.
        var diamondImg = ImageObject("Seam Diamond", knob, GetDiamondSprite());
        diamondImg.color = new Color32(255, 240, 200, 255);
        diamondImg.raycastTarget = false;
        var drt = diamondImg.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(9f, 9f);
        drt.anchoredPosition = Vector2.zero;

        // Small turn chevron riding with the diamond, pointing toward the active player's side of the
        // board (down = my/bottom turn, up = opponent/top turn). Tucked close — its wide end may overlap
        // the bar as long as the point clearly aims at the active side. Symmetric gap either way.
        AddPushPullChevron(knob, myTurn, myTurn ? -6.5f : 6.5f, (Color)new Color32(255, 240, 200, 255));

        // Smoothly ease the halves/knob from the shown value to the true balance point.
        bar.gameObject.AddComponent<PushPullFracAnim>().Init(this, myHalf, oppHalf, null, knobAnchor, target, frac);

        return track;   // rim particles mask behind the center bar
    }

    // Living per-turn glow for the active half: soft colour that HUGS the bar's long edges (like a
    // card's border glow) with a slow smoky shimmer drifting within. Children of `activeHalf`, so the
    // capsule Mask clips it to the bar.
    private void AddPushPullTurnGlow(RectTransform activeHalf, List<Color> colors, bool seamOnLeft)
    {
        Color baseCol = Color.Lerp(colors[0], Color.white, 0.5f);   // brighter, whiter active-turn tint

        // Smoky shimmer: three tileable-noise layers drifting in different directions/speeds.
        var smokeA = AddPushPullSmoke(activeHalf, baseCol, 0.34f, new Rect(0f, 0f, 3.5f, 2.2f));
        var smokeB = AddPushPullSmoke(activeHalf, baseCol, 0.26f, new Rect(0.5f, 0.3f, 2.4f, 3.1f));
        var smokeC = AddPushPullSmoke(activeHalf, baseCol, 0.20f, new Rect(0.2f, 0.7f, 4.4f, 1.7f));

        // Border hug: brightest along the top & bottom edges, fading to the centre.
        var edge = new GameObject("Turn Edge Glow").AddComponent<RectTransform>();
        edge.SetParent(activeHalf, false);
        edge.anchorMin = Vector2.zero; edge.anchorMax = Vector2.one;
        edge.offsetMin = Vector2.zero; edge.offsetMax = Vector2.zero;
        var edgeImg = edge.gameObject.AddComponent<RawImage>();
        edgeImg.texture = GetEdgeGlowVTexture();
        edgeImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, 0.62f);
        edgeImg.raycastTarget = false;

        activeHalf.gameObject.AddComponent<PushPullTurnGlow>().Init(edgeImg,
            new[] { smokeA, smokeB, smokeC },
            new[] { new Vector2(0.07f, 0.04f), new Vector2(-0.05f, 0.07f), new Vector2(0.09f, -0.03f) });
    }

    // A small even-thickness chevron pointing up or down. Uses two SEPARATE native sprites (apex-bottom
    // and apex-top), each drawn symmetric about its centre-line, with NO RectTransform rotation — so the
    // up and down variants render through the exact same un-rotated path and share any residual offset.
    // Both are plain Images (like the diamond) so their pixel-snapping matches it.
    private const float PushPullChevronNudgeX = 0f;   // single tunable if a constant offset remains

    private void AddPushPullChevron(RectTransform parent, bool pointDown, float yOffset, Color color)
    {
        var img = ImageObject("Turn Chevron", parent, GetTurnChevronSprite(pointDown));
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(11f, 6.5f);
        rt.anchoredPosition = new Vector2(PushPullChevronNudgeX, yOffset);
    }

    // Native chevron sprite, apex at bottom (down) or top (up), even thickness via distance-to-two-
    // segments, drawn perfectly symmetric about x = W/2. FullRect mesh so no tight-bounds trimming.
    private Sprite _turnChevronDown, _turnChevronUp;
    private Sprite GetTurnChevronSprite(bool down)
    {
        if (down && _turnChevronDown != null) return _turnChevronDown;
        if (!down && _turnChevronUp != null) return _turnChevronUp;

        const int W = 48, H = 28;
        const float half = 3.0f;   // stroke half-thickness (supersampled px; texture y=0 is the bottom row)
        var apex = new Vector2(W * 0.5f, down ? H * 0.20f : H * 0.80f);
        var pL = new Vector2(W * 0.16f, down ? H * 0.76f : H * 0.24f);
        var pR = new Vector2(W * 0.84f, down ? H * 0.76f : H * 0.24f);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(SegDist(p, pL, apex), SegDist(p, pR, apex));
                float a = Mathf.Clamp01(half + 0.5f - d);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        if (down) _turnChevronDown = sp; else _turnChevronUp = sp;
        return sp;
    }

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a, ap = p - a;
        float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-4f, Vector2.Dot(ab, ab)));
        return Vector2.Distance(p, a + ab * t);
    }

    // Filled diamond (rhombus) baked into a texture so it renders as an UN-ROTATED Image — matching the
    // chevron's snapping so the two never stagger as the bar animates. |x|+|y| (L1) gives the diamond.
    private Sprite _diamondSprite;
    private Sprite GetDiamondSprite()
    {
        if (_diamondSprite != null) return _diamondSprite;
        const int S = 32;
        float c = (S - 1) * 0.5f;
        float r = S * 0.44f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Abs(x - c) + Mathf.Abs(y - c);   // L1 distance -> diamond
                float a = Mathf.Clamp01(r - d + 0.5f);           // ~1px AA edge
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _diamondSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _diamondSprite;
    }

    // Soft-blurred rounded-rect silhouette for card drop shadows. Solid core inset by `feather`, alpha
    // feathering smoothly to 0 over that band; 9-sliced (border = feather+corner) so it stretches to any
    // card size without distorting the soft edge.
    private Sprite _cardShadowSprite;
    private Sprite GetCardShadowSprite()
    {
        if (_cardShadowSprite != null) return _cardShadowSprite;
        const int S = 48;
        const float feather = 9f, cornerR = 8f;
        float half = (S - 1) * 0.5f;
        float bHalf = half - feather;   // half-extent of the solid core
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float qx = Mathf.Abs(x - half) - bHalf + cornerR;
                float qy = Mathf.Abs(y - half) - bHalf + cornerR;
                float d = Mathf.Min(Mathf.Max(qx, qy), 0f)
                    + Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) - cornerR;
                float a = Mathf.Clamp01(1f - d / feather);   // 1 inside, fades over the feather band
                a = a * a * (3f - 2f * a);                   // smootherstep softness
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        float border = feather + cornerR;
        _cardShadowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return _cardShadowSprite;
    }

    // A small life total centred in the narrow gutter between the bar's end and the play-area edge,
    // painted with the deck's colour(s) as a vertical gradient (top lighter -> bottom deck colour,
    // or colour[0] -> colour[last] for multicolour).
    private void AddPushPullEndNumber(RectTransform parent, float anchorX, int life, List<Color> colors)
    {
        var t = TextObject("Life End Num", parent, life.ToString(), 12, Color.white, TextAnchor.MiddleCenter, titleFont);
        t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(20f, 18f);
        rt.anchoredPosition = Vector2.zero;

        var grad = t.gameObject.AddComponent<TextGradient>();
        grad.top = Color.Lerp(colors[0], Color.white, 0.5f);
        grad.bottom = colors[colors.Count - 1];

        var cg = t.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0.7f;
        cg.blocksRaycasts = false;
    }

    // Vertical colour gradient across any UGUI Text/Graphic mesh (top -> bottom).
    private sealed class TextGradient : BaseMeshEffect
    {
        public Color top = Color.white, bottom = Color.white;
        private static readonly List<UIVertex> _verts = new List<UIVertex>();
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            vh.GetUIVertexStream(_verts);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < _verts.Count; i++)
            {
                minY = Mathf.Min(minY, _verts[i].position.y);
                maxY = Mathf.Max(maxY, _verts[i].position.y);
            }
            float h = Mathf.Max(0.0001f, maxY - minY);
            for (int i = 0; i < _verts.Count; i++)
            {
                var v = _verts[i];
                float f = (v.position.y - minY) / h;
                v.color = Color.Lerp(bottom, top, f) * (Color)v.color;
                _verts[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(_verts);
        }
    }

    private RawImage AddPushPullSmoke(RectTransform parent, Color col, float alpha, Rect uv)
    {
        var go = new GameObject("Turn Smoke").AddComponent<RectTransform>();
        go.SetParent(parent, false);
        go.anchorMin = Vector2.zero; go.anchorMax = Vector2.one;
        go.offsetMin = Vector2.zero; go.offsetMax = Vector2.zero;
        var img = go.gameObject.AddComponent<RawImage>();
        img.texture = GetSmokeNoiseTexture();
        img.color = new Color(col.r, col.g, col.b, alpha);
        img.raycastTarget = false;
        img.uvRect = uv;
        return img;
    }

    // Vertical gradient bright at both long edges, dark in the middle (border-hug glow).
    private static Texture2D _edgeGlowVTex;
    private static Texture2D GetEdgeGlowVTexture()
    {
        if (_edgeGlowVTex != null) return _edgeGlowVTex;
        const int h = 32;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            float d = Mathf.Min(v, 1f - v) / 0.5f;      // 0 at edges, 1 at centre
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 2.2f)));
        }
        tex.Apply();
        _edgeGlowVTex = tex;
        return tex;
    }

    // Tileable smoky value-noise (sum of integer-frequency sines -> seamless when scrolled/repeated).
    private static Texture2D _smokeNoiseTex;
    private static Texture2D GetSmokeNoiseTexture()
    {
        if (_smokeNoiseTex != null) return _smokeNoiseTex;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / S, v = (float)y / S;
                float n = 0.5f
                    + 0.30f * Mathf.Sin((2f * u + 1f * v) * 2f * Mathf.PI)
                    + 0.22f * Mathf.Sin((1f * u - 3f * v) * 2f * Mathf.PI + 1.3f)
                    + 0.16f * Mathf.Sin((4f * u + 2f * v) * 2f * Mathf.PI + 2.1f)
                    + 0.12f * Mathf.Sin((3f * u - 1f * v) * 2f * Mathf.PI + 3.7f);
                n = Mathf.Clamp01(n);
                n *= n;   // sharpen into wispier clumps
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(n * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _smokeNoiseTex = tex;
        return tex;
    }


    // Gentle alpha pulse for the gold seam glow (~2.6s loop).
    private sealed class PushPullPulse : MonoBehaviour
    {
        private RawImage img; private float baseA; private float t;
        public void Init(RawImage image) { img = image; baseA = image.color.a; }
        private void Update()
        {
            if (img == null) return;
            t += Time.unscaledDeltaTime;
            float k = 0.5f + 0.5f * Mathf.Sin(t / 2.6f * 2f * Mathf.PI);
            var c = img.color; c.a = Mathf.Lerp(baseA * 0.7f, baseA * 1.3f, k); img.color = c;
        }
    }

    // Pins a child of the bar to the balance point (frac). The track is inset from the bar edges by
    // `sideInset` on each side, so the seam's local X within the bar depends on the live bar width.
    private sealed class PushPullSeamAnchor : MonoBehaviour
    {
        private RectTransform self, bar; private float frac, sideInset;
        public void Init(RectTransform barRt, float f, float inset)
        { self = (RectTransform)transform; bar = barRt; frac = f; sideInset = inset; }
        public void SetFrac(float f) { frac = f; }
        private void Update()
        {
            if (bar == null) return;
            float w = bar.rect.width;
            if (w <= 1f) return;
            float trackW = w - 2f * sideInset;
            self.anchoredPosition = new Vector2(-w * 0.5f + sideInset + frac * trackW, 0f);
        }
    }

    // Eases the halves/seam/knob from the shown balance point toward the true one, so life changes
    // slide smoothly instead of snapping. Persists progress back onto the owner across rebuilds.
    private sealed class PushPullFracAnim : MonoBehaviour
    {
        private GameManager owner;
        private RectTransform myHalf, oppHalf, seam;
        private PushPullSeamAnchor knob;
        private float target, shown;

        public void Init(GameManager o, RectTransform my, RectTransform opp, RectTransform sm,
            PushPullSeamAnchor kn, float tgt, float start)
        { owner = o; myHalf = my; oppHalf = opp; seam = sm; knob = kn; target = tgt; shown = start; }

        private void Apply()
        {
            if (myHalf != null) myHalf.anchorMax = new Vector2(shown, 1f);
            if (oppHalf != null) oppHalf.anchorMin = new Vector2(shown, 0f);
            if (seam != null) { seam.anchorMin = new Vector2(shown, 0f); seam.anchorMax = new Vector2(shown, 1f); }
            if (knob != null) knob.SetFrac(shown);
        }

        private void Update()
        {
            if (Mathf.Abs(shown - target) > 0.0004f)
            {
                shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.6f));
                Apply();
            }
            if (owner != null) owner._pushPullShownFrac = shown;
        }
    }

    // Living per-turn glow: breathes the border-hug edge glow and drifts the smoky noise layers.
    private sealed class PushPullTurnGlow : MonoBehaviour
    {
        private RawImage edge; private float edgeBaseA;
        private RawImage[] smoke; private Vector2[] vel; private float[] smokeBaseA;
        private float t;

        public void Init(RawImage edgeImg, RawImage[] smokeImgs, Vector2[] velocities)
        {
            edge = edgeImg; edgeBaseA = edgeImg.color.a;
            smoke = smokeImgs; vel = velocities;
            smokeBaseA = new float[smokeImgs.Length];
            for (int i = 0; i < smokeBaseA.Length; i++) smokeBaseA[i] = smokeImgs[i].color.a;
        }

        private void Update()
        {
            t += Time.unscaledDeltaTime;

            if (edge != null)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(t * 1.0f);
                var c = edge.color; c.a = Mathf.Lerp(edgeBaseA * 0.55f, edgeBaseA, k); edge.color = c;
            }

            if (smoke == null) return;
            for (int i = 0; i < smoke.Length; i++)
            {
                if (smoke[i] == null) continue;
                var r = smoke[i].uvRect;
                r.x += vel[i].x * Time.unscaledDeltaTime;
                r.y += vel[i].y * Time.unscaledDeltaTime;
                smoke[i].uvRect = r;
                float k = 0.5f + 0.5f * Mathf.Sin(t * 0.8f + i * 1.7f);
                var c = smoke[i].color; c.a = Mathf.Lerp(smokeBaseA[i] * 0.5f, smokeBaseA[i], k); smoke[i].color = c;
            }
        }
    }

    // A single vertical corner line anchored at a normalized board point (which corner via left/topSide).
    // Spans ~1/3 of the play height and tapers from solid AT the corner to transparent inward, mirroring
    // the centre seam's gradient. (No horizontal arm - just a straight vertical line.)
    private void AddFieldBracket(RectTransform parent, Vector2 anchor, bool leftSide, bool topSide)
    {
        var col = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        float th = 1f;   // integer thickness for crisp rendering
        float boardH = parent.rect.height > 1f ? parent.rect.height : 900f;
        float armY = boardH * 0.30f;   // ~1/3 of the play space's height
        var pivot = new Vector2(leftSide ? 0f : 1f, topSide ? 1f : 0f);

        // Vertical line - bright at the corner, fading inward.
        var v = new GameObject("Field Bracket V").AddComponent<RectTransform>();
        v.SetParent(parent, false);
        v.anchorMin = v.anchorMax = anchor;
        v.pivot = pivot;
        v.sizeDelta = new Vector2(th, armY);
        v.anchoredPosition = Vector2.zero;
        var vImg = v.gameObject.AddComponent<RawImage>();
        vImg.texture = GetVGradientTexture();
        vImg.color = col;
        vImg.raycastTarget = false;
        vImg.uvRect = topSide ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 1f, 1f, -1f);
    }

    private void DrawReferencePlaymat()
    {
        // Mat + halves are transparent now - the colour comes from the full-bleed wash behind
        // (DrawTableSurface). These keep the original geometry so the zones sit exactly where they did,
        // and still carry the EventDrop targets.
        var mat = PanelObject("Reference Playmat", playRoot, new Color(0, 0, 0, 0));
        Stretch(mat, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddBoardCancel(mat);

        var topHalf = PanelObject("North Playmat Half", mat, new Color(0, 0, 0, 0));
        if (IsReplayRotated)
            RotateHalfForReplay(topHalf, mat, true);
        else
            Stretch(topHalf, new Vector2(0, 0.50f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        northHalfRect = topHalf;
        AddBoardCancel(topHalf);
        var topEventDrop = topHalf.gameObject.AddComponent<EventDrop>();
        topEventDrop.Init(this, TopSeat);
        var bottomHalf = PanelObject("South Playmat Half", mat, new Color(0, 0, 0, 0));
        if (IsReplayRotated)
            RotateHalfForReplay(bottomHalf, mat, false);
        else
            Stretch(bottomHalf, Vector2.zero, new Vector2(1, 0.50f), Vector2.zero, Vector2.zero);
        southHalfRect = bottomHalf;
        AddBoardCancel(bottomHalf);
        var bottomEventDrop = bottomHalf.gameObject.AddComponent<EventDrop>();
        bottomEventDrop.Init(this, BottomSeat);

        DrawMatHalf(topHalf, state.Players[TopSeat], TopSeat, true);
        DrawMatHalf(bottomHalf, state.Players[BottomSeat], BottomSeat, false);
    }

    // Tournament/broadcast pivot: south -> left, north -> right, cards facing each other across
    // the seam ("it's really just rotating the board 90 degrees" — see the reference photo
    // discussion). A literal -90° rotation of the whole half, rather than redesigning its
    // contents: DrawMatHalf's zone rects and card rotations are authored once, for a normal
    // top/bottom board, and this reorients the whole rigid subtree (zone positions AND card
    // art) together. Both halves get the SAME -90° rotation, not mirrored +/-90 — north's
    // "toward the seam" direction was already the opposite of south's in the un-rotated
    // layout (top=0 vs top=1 conventions), so an identical rotation keeps them opposite,
    // landing correctly on the left/right side of the seam respectively. `half`'s local size
    // is pre-swapped (W<->H of the target on-screen box) so the POST-rotation bounding box
    // lands exactly on its half of the screen.
    private void RotateHalfForReplay(RectTransform half, RectTransform mat, bool topSeatHalf)
    {
        float fullW = mat.rect.width > 1f ? mat.rect.width : 1600f;
        float fullH = mat.rect.height > 1f ? mat.rect.height : 900f;
        half.anchorMin = half.anchorMax = new Vector2(topSeatHalf ? 0.75f : 0.25f, 0.5f);
        half.pivot = new Vector2(0.5f, 0.5f);
        half.sizeDelta = new Vector2(fullH, fullW * 0.5f);
        half.anchoredPosition = Vector2.zero;
        half.localRotation = Quaternion.Euler(0f, 0f, -90f);
    }

    // Same trick as RotateHalfForReplay, but for an element that should span (and rotate
    // around) the FULL board rather than one half — the center seam + turn-crest assembly.
    private void RotateFullForReplay(RectTransform rt, RectTransform sizeRef)
    {
        float fullW = sizeRef.rect.width > 1f ? sizeRef.rect.width : 1600f;
        float fullH = sizeRef.rect.height > 1f ? sizeRef.rect.height : 900f;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(fullH, fullW); // swapped
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.Euler(0f, 0f, -90f);
    }

    private void DrawMatHalf(RectTransform half, PlayerState p, string seat, bool top)
    {
        // (Removed the gold active-side highlight per design — both halves stay the same felt.)

        // The tournament/replay pivot (south -> left, north -> right, "cards facing each other")
        // is implemented as a literal 90° ROTATION of the whole `half` transform in
        // DrawReferencePlaymat, not a redesign of this method — every zone rect, the DON row,
        // and every card's rotation below is authored exactly as it always was for the normal
        // top/bottom board; rotating the container reorients all of it (position AND card art)
        // together as one rigid transform, so it stays correct here unconditionally.

        // Far edge (toward the deck/trash) extends over them to line up with their outer edge; the near
        // edge clears the life column. Point-mirrored for north.
        // Character row reaches out to the deck/trash on its far side; near side clears the life column.
        // Squished vertically (was 0.64-0.97) to free room below for padding; the charRow inset is
        // tightened so the cards stay full size despite the shorter zone.
        var characterMin = top ? new Vector2(0.02f, 0.03f) : new Vector2(0.16f, 0.67f);
        var characterMax = top ? new Vector2(0.84f, 0.33f) : new Vector2(0.98f, 0.97f);
        var character = MatZone(half, "CHARACTER AREA",
            characterMin,
            characterMax,
            top ? new Color32(69, 99, 126, 230) : new Color32(81, 132, 65, 230),
            top);
        // NOTE: no RectMask2D here. A RectMask2D on this zone disables the stencil-based
        // rounded Mask on the cards inside it, making them show square corners.
        // Fitted row that fills the zone width so cards read at near-full size (reference mat look).
        var charRow = new GameObject("Character Cards").AddComponent<RectTransform>();
        charRow.SetParent(character, false);
        Stretch(charRow, new Vector2(0.015f, 0.02f), new Vector2(0.985f, 0.98f), Vector2.zero, Vector2.zero);
        int charSlots = Mathf.Max(1, p.CharacterArea.Count);
        var charLayout = FittedRow(charSlots, 0.190f, 0.012f, 0.06f, true);
        for (int i = 0; i < p.CharacterArea.Count; i++)
        {
            float cx = charLayout.start + i * charLayout.step;
            AddCharacterSlot(charRow, p, seat, i, top, cx, charLayout.cardWidth);
        }

        var costMin = top ? new Vector2(0.22f, 0.69f) : new Vector2(0.22f, 0.02f);
        var costMax = top ? new Vector2(0.78f, 0.98f) : new Vector2(0.78f, 0.31f);
        var cost = MatZone(half, "COST AREA",
            costMin,
            costMax,
            top ? new Color32(54, 80, 110, 230) : new Color32(75, 123, 57, 230),
            top);
        // NOTE: no RectMask2D here. A RectMask2D on this zone disables the stencil-based
        // rounded Mask on the cards inside it, making them show square corners.
        // (The DON!! card row itself is created AFTER the leader zone below, so leader-attached
        // DON!! layer between the cost background and the cost-area DON!! cards.)

        // STAGE zone BACKGROUND is created HERE — BEFORE the Leader zone — so DON!! attached to the
        // Leader (a later sibling; its DON!! fan toward the Stage) render OVER the Stage panel instead
        // of being clipped behind it. Field cards sit above the zone art. The Stage CARD is added below.
        var stageMin = top ? new Vector2(0.31f, 0.36f) : new Vector2(0.58f, 0.34f);
        var stageMax = top ? new Vector2(0.42f, 0.66f) : new Vector2(0.69f, 0.64f);
        var stage = MatZone(half, "STAGE", stageMin, stageMax, new Color32(151, 179, 92, 220), top);

        var leaderMin = top ? new Vector2(0.445f, 0.36f) : new Vector2(0.445f, 0.34f);
        var leaderMax = top ? new Vector2(0.555f, 0.66f) : new Vector2(0.555f, 0.64f);
        var leader = MatZone(half, "LEADER", leaderMin, leaderMax, new Color32(217, 224, 210, 235), top);
        moveZoneAnchors["cost:" + seat] = cost;
        // The cost-area DON!! row is created HERE (a sibling drawn after the leader zone) so
        // DON!! attached to the Leader tuck UNDER the cost-area DON cards — but stay ABOVE the
        // cost zone's background panel (the leader zone renders after the cost zone).
        var donRow = new GameObject("Cost DON Cards").AddComponent<RectTransform>();
        // Parented to `half` (not playRoot) and positioned in half-LOCAL coordinates: leader is
        // created just before this (a sibling under `half`), so DON!! attached to the Leader
        // still tucks under these cost-area DON cards by creation order. Hand fans are created
        // later in Render(), directly under playRoot, so they still always render above this.
        donRow.SetParent(half, false);
        float dyMin = Mathf.Lerp(costMin.y, costMax.y, 0.06f);
        float dyMax = Mathf.Lerp(costMin.y, costMax.y, 0.94f);
        Stretch(donRow,
            new Vector2(Mathf.Lerp(costMin.x, costMax.x, 0.02f), dyMin),
            new Vector2(Mathf.Lerp(costMin.x, costMax.x, 0.98f), dyMax),
            Vector2.zero, Vector2.zero);
        bool ownDonGroups = state != null && seat == state.ActiveSeat
            && (!isNetworked || seat == localSeat) && donGroupSizes.Count > 1;
        if (ownDonGroups)
            DrawGroupedDonRow(donRow, p.CostArea, seat, top);
        else if (state != null && isNetworked && seat == state.ActiveSeat && seat != localSeat
                 && opponentPresence != null && opponentPresence.donGroups != null && opponentPresence.donGroups.Length > 1)
            DrawGroupedDonRow(donRow, p.CostArea, seat, top, opponentPresence.donGroups);
        else
            DrawFittedDonRow(donRow, p.CostArea, seat, top);
        AddCardToZone(leader, p.Leader, seat, true, top);
        var leaderSz = FittedCardSize(leader);
        SnugZone(leader, leaderMin, leaderMax, leaderSz.x * 1.06f, leaderSz.y * 1.06f);

        // (STAGE zone background created above, before the Leader zone, for correct DON!! layering.)
        var stageDrop = stage.gameObject.AddComponent<StageDrop>();
        stageDrop.Init(this, seat);
        if (p.Stage != null) AddCardToZone(stage, p.Stage, seat, true, top);
        var stageSz = FittedCardSize(stage);
        SnugZone(stage, stageMin, stageMax, stageSz.x * 1.06f, stageSz.y * 1.06f);

        // Deck up the right side, flanking the character row (opposite Life).
        var deckMin = top ? new Vector2(0.00f, 0.36f) : new Vector2(0.87f, 0.34f);
        var deckMax = top ? new Vector2(0.13f, 0.66f) : new Vector2(1.00f, 0.64f);
        var deck = MatZone(half, "DECK", deckMin, deckMax, new Color32(221, 226, 211, 225), top);
        boardDeckPileRects[seat] = AddPileCardToZone(deck, "Deck", p.Deck.Count, true, null, top);
        if (isNetworked) { presenceGlowRects["pile:deck:" + seat] = deck; deck.gameObject.AddComponent<ZoneHoverPresence>().Init(this, "pile:deck:" + seat); }
        var deckSz = FittedCardSize(deck);
        float deckDepth = p.Deck.Count > 0 ? StackDepth(p.Deck.Count) : 0f;
        SnugZone(deck, deckMin, deckMax, deckSz.x * 1.06f, deckSz.y * 1.06f + deckDepth);

        // DON!! deck mid-side beside the leader row (reference mat).
        var donDeckMin = top ? new Vector2(0.87f, 0.69f) : new Vector2(0.00f, 0.02f);
        var donDeckMax = top ? new Vector2(1.00f, 0.98f) : new Vector2(0.13f, 0.31f);
        var donDeck = MatZone(half, "DON!! DECK", donDeckMin, donDeckMax, new Color32(221, 226, 211, 225), top);
        moveZoneAnchors["dondeck:" + seat] = donDeck;
        AddDonDeckPileToZone(donDeck, p.DonDeck, top);
        if (isNetworked) { presenceGlowRects["pile:dondeck:" + seat] = donDeck; donDeck.gameObject.AddComponent<ZoneHoverPresence>().Init(this, "pile:dondeck:" + seat); }
        var donSz = FittedCardSize(donDeck);
        float donDepth = p.DonDeck > 0 ? StackDepth(p.DonDeck) : 0f;
        SnugZone(donDeck, donDeckMin, donDeckMax, donSz.x * 1.06f, donSz.y * 1.06f + donDepth);

        var trashMin = top ? new Vector2(0.00f, 0.69f) : new Vector2(0.87f, 0.02f);
        var trashMax = top ? new Vector2(0.13f, 0.98f) : new Vector2(1.00f, 0.31f);
        var trash = MatZone(half, "TRASH", trashMin, trashMax, new Color32(172, 169, 73, 220), top);
        moveZoneAnchors["trash:" + seat] = trash;
        AddPileCardToZone(trash, "Trash", p.Trash.Count, false, p.Trash.LastOrDefault(), top);
        if (isNetworked) { presenceGlowRects["pile:trash:" + seat] = trash; trash.gameObject.AddComponent<ZoneHoverPresence>().Init(this, "pile:trash:" + seat); }
        // Clicking a trash pile (yours or the opponent's) opens the trash viewer for that side.
        {
            string trashSeatCap = p.Seat;
            var trashBtn = trash.gameObject.GetComponent<Button>();
            if (trashBtn == null) trashBtn = trash.gameObject.AddComponent<Button>();
            trashBtn.transition = Selectable.Transition.None;
            trashBtn.onClick.AddListener(() =>
            {
                trashViewSeat = trashViewSeat == trashSeatCap ? null : trashSeatCap;
                Render();
            });
        }
        var trashSz = FittedCardSize(trash);
        float trashDepth = p.Trash.Count > 1 ? StackDepth(p.Trash.Count) : 0f;
        SnugZone(trash, trashMin, trashMax, trashSz.x * 1.06f, trashSz.y * 1.06f + trashDepth);

        // Life in the player-edge corner (reference mat).
        // Life: tall column up the left side, flanking the character row.
        var lifeMin = top ? new Vector2(0.85f, 0.03f) : new Vector2(0.02f, 0.40f);
        var lifeMax = top ? new Vector2(0.98f, 0.60f) : new Vector2(0.15f, 0.97f);
        var life = MatZone(half, "LIFE", lifeMin, lifeMax, new Color32(226, 230, 216, 235), top);
        moveZoneAnchors["life:" + seat] = life;
        // At the end of the match, reveal BOTH players' remaining Life cards face-up (View Board);
        // during play they stay hidden.
        AddLifeStackToZone(life, p.Life.Count, top, seat, p.Life, RevealFinishedInfo());
        // "Add from the top or bottom of your Life" effects (Zeus OP11-106): overlay this Life zone
        // with clickable TOP / BOTTOM halves so the player picks the card right on the board.
        MaybeAddLifeTargetPicker(life, p, seat);
    }

    private void DrawExternalHand(PlayerState p, string seat, bool top)
    {
        var panel = PanelObject(seat + " External Hand", playRoot, new Color(0, 0, 0, 0));
        if (IsReplayRotated)
        {
            // Each hand sits along its player's OUTER edge (south -> left edge of screen,
            // north -> right edge), roughly where that player's DON!! deck lands — not at the
            // screen's top/bottom, which is the normal-mode convention that no longer applies
            // once players sit side by side. Rotated -90° like the rest of the board (same
            // swapped-size-then-rotate trick as RotateHalfForReplay) so the fan visually
            // matches instead of standing out as the only upright element on screen.
            float fullW = playRoot.rect.width > 1f ? playRoot.rect.width : 1600f;
            float fullH = playRoot.rect.height > 1f ? playRoot.rect.height : 900f;
            const float boxWFrac = 0.19f, boxHFrac = 0.40f;
            panel.anchorMin = panel.anchorMax = new Vector2(top ? 0.98f : 0.02f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(boxHFrac * fullH, boxWFrac * fullW); // swapped
            panel.anchoredPosition = Vector2.zero;
            // North's hand needs the extra 180° on top of the shared -90° (its fan reads
            // upside-down otherwise) — south's doesn't.
            panel.localRotation = Quaternion.Euler(0f, 0f, top ? 90f : -90f);
        }
        else
        {
            Stretch(panel, top ? new Vector2(0.10f, 0.885f) : new Vector2(0.10f, -0.080f), top ? new Vector2(0.90f, 1.080f) : new Vector2(0.90f, 0.115f), Vector2.zero, Vector2.zero);
        }
        var panelImage = panel.GetComponent<Image>();
        // This invisible panel's bounds overlap the Cost Area zone underneath it (it extends
        // toward the screen edge for the hand-fan layout). Leaving it raycastable at all times
        // silently swallows hover for the DON cards under it. It only actually needs to be a
        // raycast target while a hand card is being dragged (so HandDrop's OnDrop can fire on
        // it) - see CardDrag's Begin/EndDrag, which toggles this via SetHandDropRaycastActive.
        if (panelImage != null) panelImage.raycastTarget = false;
        if (seat == "south") southHandPanelImage = panelImage; else northHandPanelImage = panelImage;
        var handDrop = panel.gameObject.AddComponent<HandDrop>();
        handDrop.Init(this, seat);

        // (Hand title/count text removed per design.)
        var row = new GameObject(seat + " Hand Fan").AddComponent<RectTransform>();
        row.SetParent(panel, false);
        if (IsReplayRotated)
        {
            // Symmetric margin for both seats — the normal-mode near/far-edge asymmetry (bigger
            // margin on the "near" side, per `top`) doesn't translate cleanly once the panel
            // itself is rotated ±90° in opposite directions per seat; it was making north's fan
            // content sit shifted toward the centre relative to south's.
            Stretch(row, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f), Vector2.zero, Vector2.zero);
        }
        else
        {
            Stretch(row, new Vector2(0.02f, top ? 0.10f : 0.02f), new Vector2(0.98f, top ? 0.98f : 0.90f), Vector2.zero, Vector2.zero);
        }
        moveZoneAnchors["hand:" + seat] = row;
        // During replay, the fan's own curve/tilt math always uses the "south" (top=false) sign
        // convention for BOTH seats — the panel's own rotation (RotateHalfForReplay-style,
        // ±90° depending on seat) already handles which way each hand faces. Passing the real
        // `top` here as well double-mirrors north's curve (it and the panel rotation cancel
        // out), which is what made its fan bow the wrong way.
        DrawFannedHandRow(row, p.Hand, seat + "-hand", IsReplayRotated ? false : top);
    }

    private void DrawLeftPanel()
    {
        // Opponent plate up top.
        DrawPlayerPlate(leftRoot, state.Players.ContainsKey(TopSeat) ? state.Players[TopSeat] : null, TopSeat, state.ActiveSeat == TopSeat,
            new Vector2(0.06f, 0.938f), new Vector2(0.94f, 0.982f));

        // Docked card preview: the selected card, else your leader.
        // The docked preview locks to the last left-clicked card; defaults to your leader.
        CardInstance focus = previewLockCard;
        if (focus == null && state.Players.TryGetValue(BottomSeat, out var sp)) focus = sp.Leader;

        var pvLabel = TextObject("Card Preview Label", leftRoot, "CARD PREVIEW", 9, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(pvLabel.rectTransform, new Vector2(0.06f, 0.912f), new Vector2(0.94f, 0.93f), Vector2.zero, Vector2.zero);

        var cardHolder = new GameObject("Preview Card").AddComponent<RectTransform>();
        cardHolder.SetParent(leftRoot, false);
        cardHolder.anchorMin = cardHolder.anchorMax = new Vector2(0.5f, 0.768f);
        cardHolder.pivot = new Vector2(0.5f, 0.5f);
        cardHolder.sizeDelta = new Vector2(170f, 238f);
        cardHolder.anchoredPosition = Vector2.zero;
        if (focus != null)
        {
            RoundedCardVisual("Preview Art", cardHolder, GetCardSprite(focus.CardId), out var pimg);
            pimg.raycastTarget = false;
        }
        else
        {
            var ph = PanelObject("Preview Empty", cardHolder, new Color(1f, 1f, 1f, 0.02f));
            Stretch(ph, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Round(ph);
            AddRoundedCardBorder(ph, MenuB, 1f);
        }

        var def = focus != null ? GameEngine.GetCard(focus) : null;
        bool showPower = def != null && (def.Type == "character" || def.Type == "leader");

        // The card art in the preview already shows the card's name, so a separate name label
        // under it was redundant clutter (user request to drop it). Chips (power/cost/type) stay.

        var chips = RowObject("Preview Chips", leftRoot, 4, TextAnchor.MiddleLeft);
        Stretch(chips, new Vector2(0.06f, 0.566f), new Vector2(0.94f, 0.594f), Vector2.zero, Vector2.zero);
        chips.gameObject.AddComponent<RectMask2D>();   // never bleed past the panel
        if (def != null)
        {
            AddPreviewChip(chips, def.Type != null ? def.Type.ToUpper() : "CARD", true);
            if (!string.IsNullOrEmpty(def.Color)) AddPreviewChip(chips, def.Color.ToUpper(), false);
            if (def.Life.HasValue) AddPreviewChip(chips, "LIFE " + def.Life.Value, false);
            else AddPreviewChip(chips, (def.Type == "leader" ? "" : "COST " + def.Cost), false);
            // Power last - not every card has it.
            if (showPower) AddPreviewChip(chips, "POWER " + GameEngine.GetPower(state, focus), false);
        }

        // Status detail: one line per active indicator on the focused card, colored to
        // match the on-card chips ("Cannot attack (this turn).", "Power +2000 ...", ...).
        var previewBadges = focus != null ? GameEngine.GetStatusBadges(state, focus) : null;
        if (previewBadges != null && previewBadges.Count > 0)
        {
            var sbLines = new System.Text.StringBuilder();
            foreach (var sb in previewBadges)
            {
                string hex = sb.Kind == "buff" ? "6fe08f" : sb.Kind == "debuff" ? "ff8b8b"
                           : sb.Kind == "restrict" ? "ffce7a" : "8fc3ff";
                sbLines.Append("<color=#").Append(hex).Append(">● ").Append(sb.Detail).Append("</color>\n");
            }
            var sbBox = PanelObject("Preview Status", leftRoot, LogBgDark);
            Stretch(sbBox, new Vector2(0.06f, 0.352f), new Vector2(0.94f, 0.442f), Vector2.zero, Vector2.zero);
            RoundBig(sbBox);
            AddRoundedCardBorder(sbBox, MenuB, 1f);
            var sbTxt = TextObject("Preview Status Text", sbBox, sbLines.ToString().TrimEnd('\n'), 10, Ink, TextAnchor.UpperLeft);
            sbTxt.supportRichText = true;
            sbTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            sbTxt.verticalOverflow = VerticalWrapMode.Truncate;
            sbTxt.resizeTextForBestFit = true;
            sbTxt.resizeTextMinSize = 8;
            sbTxt.resizeTextMaxSize = 10;
            Stretch(sbTxt.rectTransform, new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f), Vector2.zero, Vector2.zero);
        }

        var effBox = PanelObject("Preview Effect", leftRoot, LogBgDark);
        Stretch(effBox, new Vector2(0.06f, 0.45f), new Vector2(0.94f, 0.552f), Vector2.zero, Vector2.zero);
        RoundBig(effBox);
        AddRoundedCardBorder(effBox, MenuB, 1f);

        // Scrollable effect text (same viewport/content/ScrollRect pattern as the combat log), so cards
        // with a lot of rules text get a scrollbar instead of truncating.
        var effVp = PanelObject("Preview Effect Viewport", effBox, new Color(0, 0, 0, 0));
        Stretch(effVp, new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f), Vector2.zero, Vector2.zero);
        effVp.gameObject.AddComponent<RectMask2D>();

        var eff = TextObject("Preview Effect Text", effVp, def != null && !string.IsNullOrEmpty(def.Effect) ? def.Effect : "", 11, Ink, TextAnchor.UpperLeft);
        var effRt = eff.rectTransform;
        effRt.anchorMin = new Vector2(0f, 1f); effRt.anchorMax = new Vector2(1f, 1f);
        effRt.pivot = new Vector2(0.5f, 1f);
        effRt.sizeDelta = Vector2.zero; effRt.anchoredPosition = Vector2.zero;
        eff.horizontalOverflow = HorizontalWrapMode.Wrap;
        eff.verticalOverflow = VerticalWrapMode.Overflow;   // grow to full height; the ScrollRect scrolls it
        eff.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var effScroll = effVp.gameObject.AddComponent<ScrollRect>();
        effScroll.content = effRt;
        effScroll.viewport = effVp;
        effScroll.horizontal = false;
        effScroll.vertical = true;
        effScroll.movementType = ScrollRect.MovementType.Clamped;
        effScroll.scrollSensitivity = 16f;
        AttachScrollbar(effScroll);

        DrawCombatLogPanel();
    }

    // BRIGHT accent per kind (AddBadge derives a dark fill from it + uses it as the text colour).
    private static Color StatusBadgeColor(string kind)
    {
        switch (kind)
        {
            case "buff":     return new Color32(120, 235, 155, 255);   // bright green
            case "debuff":   return new Color32(255, 140, 140, 255);   // bright red
            case "restrict": return new Color32(255, 206, 122, 255);   // bright amber
            default:         return new Color32(143, 195, 255, 255);   // bright blue (info)
        }
    }

    private void AddPreviewChip(RectTransform parent, string label, bool primary)
    {
        if (string.IsNullOrEmpty(label)) return;
        var chip = PanelObject(label + " Chip", parent, primary ? Accent : new Color(1f, 1f, 1f, 0.04f));
        float chipW = 10f + label.Length * 5.6f;
        chip.sizeDelta = new Vector2(chipW, 16f);   // RowObject's HLG sizes by sizeDelta, not LayoutElement
        SetPreferred(chip, new Vector2(chipW, 16f));
        Round(chip);
        if (!primary) AddRoundedCardBorder(chip, ZoneBorder, 1f);
        var t = TextObject("t", chip, label, 8, primary ? BadgeInk : Ink, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
    }

    private static readonly System.Text.RegularExpressions.Regex LogCardIdRx =
        new System.Text.RegularExpressions.Regex(@"\[[A-Za-z0-9]{1,6}-\d{1,4}\]");

    // Compact clipboard button for the combat-log / match-chat label bands. Text is built
    // lazily on click (captures latest state) and written to the OS clipboard.
    private void AddCopyChip(RectTransform parent, string label, System.Func<string> textProvider, Vector2 anchorMin, Vector2 anchorMax)
    {
        var chip = PanelObject("Copy Chip", parent, new Color32(34, 58, 78, 235));
        Stretch(chip, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        Round(chip);
        AddRoundedCardBorder(chip, MenuB, 1f);
        var t = TextObject("Copy Chip Text", chip, label, 9, Ink, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = chip.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { GUIUtility.systemCopyBuffer = textProvider() ?? string.Empty; });
    }

    // Plain-text combat log the local viewer is allowed to see (same per-seat privacy as
    // DrawCombatLogPanel) — one "[Actor] message" line per visible event, for the Copy button.
    // Relabel the engine's fixed seat names ("South"/"North") with the players' display names for
    // anything shown to the user (esp. networked PvP — names come from PendingSouth/NorthName). Whole
    // word only so card ids/text stay intact. The engine/bot/log-stores keep "South"/"North" as their
    // stable identifiers; this is render-time only. See DisplayName().
    private string HumanizeLog(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return msg;
        string s = DisplayName("south"), n = DisplayName("north");
        if (!string.IsNullOrEmpty(s) && s != "South")
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"\bSouth\b", _ => s);
        if (!string.IsNullOrEmpty(n) && n != "North")
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"\bNorth\b", _ => n);
        return msg;
    }

    private string BuildCombatLogText()
    {
        var sb = new System.Text.StringBuilder();
        if (state?.EventLog == null) return "";
        foreach (var e in state.EventLog)
        {
            string message = e.Message;
            if (!string.IsNullOrEmpty(e.PrivateSeat) && e.PrivateSeat != BottomSeat)
            {
                if (string.IsNullOrEmpty(e.PublicMessage)) continue;
                message = e.PublicMessage;
            }
            message = HumanizeLog(message);
            string actorName = null;
            if (!string.IsNullOrEmpty(e.Actor) && e.Actor != "system" &&
                state.Players.TryGetValue(e.Actor, out var actorP) && actorP != null)
                actorName = DisplayName(e.Actor);
            if (!string.IsNullOrEmpty(actorName))
            {
                if (message != null && message.StartsWith(actorName + " "))
                    message = message.Substring(actorName.Length + 1);
                sb.Append('[').Append(actorName).Append("] ").AppendLine(message);
            }
            else sb.AppendLine(message);
        }
        return sb.ToString();
    }

    // Plain-text match chat for the Copy button (the input itself supports native paste).
    private string BuildChatText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in chatMessages) sb.Append(m.Sender).Append(": ").AppendLine(m.Text);
        return sb.ToString();
    }

    private void DrawCombatLogPanel()
    {
        var logLabel = TextObject("Combat Log Label", leftRoot, "COMBAT LOG", 9, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(logLabel.rectTransform, new Vector2(0.06f, 0.425f), new Vector2(0.94f, 0.443f), Vector2.zero, Vector2.zero);
        // Copy the full combat history to the clipboard (right end of the label band).
        AddCopyChip(leftRoot, "Copy", BuildCombatLogText, new Vector2(0.80f, 0.421f), new Vector2(0.94f, 0.447f));

        var logPanel = PanelObject("Table Combat Log", leftRoot, LogBgDark);
        Stretch(logPanel, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.418f), Vector2.zero, Vector2.zero);
        RoundBig(logPanel);
        AddRoundedCardBorder(logPanel, MenuB, 1f);

        // Scrollable viewport so the full match history can be inspected with the mouse wheel.
        var viewport = PanelObject("Log Viewport", logPanel, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.98f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("Log Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        // Zero the offsets so content width == viewport width. A freshly created RectTransform keeps a
        // non-zero default sizeDelta, which with the centred pivot pushed the whole log off the left edge.
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;
        AttachScrollbar(scroll);

        float width = viewport.rect.width;
        if (width < 8f) width = 210f;   // first-frame fallback before layout settles
        width -= 8f;                    // padding allowance

        foreach (var e in state.EventLog)
        {
            // INFO PRIVACY: a private entry names a card only the owning seat may see. The viewer
            // is BottomSeat (south in solo/versus-AI, localSeat when networked). Non-owners see the
            // redacted PublicMessage, or nothing when there is no public form.
            string message = e.Message;
            if (!string.IsNullOrEmpty(e.PrivateSeat) && e.PrivateSeat != BottomSeat)
            {
                if (string.IsNullOrEmpty(e.PublicMessage)) continue;
                message = e.PublicMessage;
            }
            string actorName = null;
            if (!string.IsNullOrEmpty(e.Actor) && e.Actor != "system" &&
                state.Players.TryGetValue(e.Actor, out var actorP) && actorP != null)
                actorName = DisplayName(e.Actor);
            BuildLogEntry(content, actorName, HumanizeLog(message), width);
        }

        // Snap to the most recent entry; the player scrolls up to review earlier turns.
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;
    }

    // Lay a log line out word-by-word with manual wrapping. Each word is split into plain text and the
    // bracketed card-id token ([OP11-100]); ONLY the id token is a hover link (accent colour + bold),
    // so hovering ordinary words does nothing while hovering the id pops the card preview.
    private void BuildLogEntry(RectTransform parent, string actorName, string message, float width)
    {
        var entry = new GameObject("Log Entry").AddComponent<RectTransform>();
        entry.SetParent(parent, false);

        int fontSize = 11;
        float lineH = fontSize * 1.35f;
        float spaceW = fontSize * 0.30f;
        float x = 0f, y = 0f;
        bool firstInLine = true;

        string msg = message ?? "";

        // Leading "[Player Name]" tag identifying who performed the action. The engine messages
        // already lead with the player's name, so strip that to avoid "[South] South ..." doubling.
        if (!string.IsNullOrEmpty(actorName))
        {
            if (msg.StartsWith(actorName + " "))
                msg = msg.Substring(actorName.Length + 1);

            var tag = TextObject("Actor", entry, "[" + actorName + "]", fontSize, Accent2, TextAnchor.UpperLeft, monoFont);
            tag.horizontalOverflow = HorizontalWrapMode.Overflow;
            tag.verticalOverflow = VerticalWrapMode.Overflow;
            tag.fontStyle = FontStyle.Bold;
            tag.raycastTarget = false;
            float tw = tag.preferredWidth;
            var trt = tag.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(tw, lineH);
            trt.anchoredPosition = new Vector2(0f, 0f);
            x = tw;
            firstInLine = false;
        }

        foreach (var word in (msg).Split(' '))
        {
            if (word.Length == 0) continue;

            // Break the word into segments: plain runs and any [id] tokens within it (handles a
            // trailing period like "[ST01-007]." by keeping the "." as a separate plain run).
            var texts = new System.Collections.Generic.List<string>();
            var cards = new System.Collections.Generic.List<bool>();
            int idx = 0;
            foreach (System.Text.RegularExpressions.Match mm in LogCardIdRx.Matches(word))
            {
                if (mm.Index > idx) { texts.Add(word.Substring(idx, mm.Index - idx)); cards.Add(false); }
                texts.Add(mm.Value); cards.Add(true);
                idx = mm.Index + mm.Length;
            }
            if (idx < word.Length) { texts.Add(word.Substring(idx)); cards.Add(false); }

            var segs = new System.Collections.Generic.List<Text>();
            var widths = new System.Collections.Generic.List<float>();
            float wordW = 0f;
            for (int k = 0; k < texts.Count; k++)
            {
                var seg = TextObject("Seg", entry, texts[k], fontSize,
                    cards[k] ? LogIdCyan : new Color32(207, 224, 240, 255), TextAnchor.UpperLeft);
                seg.horizontalOverflow = HorizontalWrapMode.Overflow;
                seg.verticalOverflow = VerticalWrapMode.Overflow;
                if (cards[k]) seg.fontStyle = FontStyle.Bold;
                float w = seg.preferredWidth;
                segs.Add(seg); widths.Add(w); wordW += w;
            }

            if (!firstInLine && x + spaceW + wordW > width)
            {
                x = 0f; y -= lineH; firstInLine = true;
            }
            float cx = x + (firstInLine ? 0f : spaceW);

            for (int k = 0; k < segs.Count; k++)
            {
                var srt = segs[k].rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
                srt.pivot = new Vector2(0f, 1f);
                srt.sizeDelta = new Vector2(widths[k], lineH);
                srt.anchoredPosition = new Vector2(cx, y);
                if (cards[k])
                {
                    segs[k].raycastTarget = true;
                    segs[k].gameObject.AddComponent<LogCardLink>().Init(this, texts[k].Trim('[', ']'));
                }
                else
                {
                    segs[k].raycastTarget = false;
                }
                cx += widths[k];
            }
            x = cx;
            firstInLine = false;
        }

        float totalH = -y + lineH + 2f;
        var le = entry.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = totalH;
        le.minHeight = totalH;
    }

    private void DrawBattleStatusOverlay()
    {
        if (state.Battle == null) return;

        var panel = PanelObject("Battle Status", playRoot, new Color32(45, 24, 28, 235));
        Stretch(panel, new Vector2(0.34f, 0.455f), new Vector2(0.945f, 0.535f), Vector2.zero, Vector2.zero);
        AddOutline(panel.gameObject, RedAccent, 2f);

        var b = state.Battle;
        var attacker = FindAny(b.AttackerSeat, b.AttackerId);
        var target = FindAny(b.TargetSeat, b.TargetId);
        var attackerName = attacker != null ? GameEngine.GetCard(attacker).Name : "Attacker";
        var targetName = target != null ? GameEngine.GetCard(target).Name : "Target";
        int liveAtk = attacker != null ? GameEngine.GetPower(state, attacker) : b.AttackPower;
        int liveDef = target != null ? (GameEngine.GetPower(state, target) + b.CounterPower) : b.DefensePower;
        var text = $"{attackerName} ({liveAtk}) attacking {targetName} ({liveDef})  |  {StepLabel(b.Step)}";
        var label = TextObject("Battle Status Text", panel, text, 17, Ink, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform, new Vector2(0.02f, 0), new Vector2(0.98f, 1), Vector2.zero, Vector2.zero);
    }

    private void DrawResolvedTargetingArrows()
    {
        if (state == null || state.DeckLook != null) return;
        if (state.Battle == null) return;

        var b = state.Battle;
        if (!cardTargetRects.TryGetValue(b.AttackerId, out var source) || source == null) return;
        if (!cardTargetRects.TryGetValue(b.TargetId, out var target) || target == null) return;

        var root = NewArrowRoot("Battle Target Arrow", boardRoot);
        DrawCurvedTargetingArrow(root, source, target, new Color(1f, 0.36f, 0.12f, 0.96f), 14f);

        // Live power readouts on each combatant during the attack (attacker = warm, defender = cool).
        var atkInst = FindAny(b.AttackerSeat, b.AttackerId);
        var defInst = FindAny(b.TargetSeat, b.TargetId);
        int liveAtk = atkInst != null ? GameEngine.GetPower(state, atkInst) : b.AttackPower;
        int liveDef = defInst != null ? (GameEngine.GetPower(state, defInst) + b.CounterPower) : b.DefensePower;
        AddPowerBadge(root, source, RectScreenCenter(source), liveAtk, new Color(1f, 0.34f, 0.18f, 1f));
        AddPowerBadge(root, target, RectScreenCenter(target), liveDef, new Color(0.36f, 0.72f, 1f, 1f));
    }

    // A bold power readout pill placed over a combatant's card during a battle. Screen-space (overlay),
    // sized from the card so it scales with the board; text best-fits the pill.
    private void AddPowerBadge(RectTransform root, RectTransform card, Vector2 screenPos, int value, Color accent)
    {
        float w = RectScreenRadius(card, 0.95f);
        if (w < 24f) w = 24f;
        float h = w * 0.44f;

        var pill = PanelObject("Power Badge", root, new Color32(16, 12, 18, 235));
        pill.GetComponent<Image>().raycastTarget = false;
        pill.sizeDelta = new Vector2(w, h);
        pill.position = screenPos;
        AddOutline(pill.gameObject, accent, 2.6f);

        var t = TextObject("Power Badge Text", pill, value.ToString(), 22, Color.white, TextAnchor.MiddleCenter);
        t.fontStyle = FontStyle.Bold;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 8;
        t.resizeTextMaxSize = 72;
        Stretch(t.rectTransform, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f), Vector2.zero, Vector2.zero);
    }

    // ── Replay visual sync (Phase 7) ─────────────────────────────────────────
    // Most of "visual sync" already happens for free: CaptureAndAnimateCardMoves diffs zone
    // positions every Render() regardless of isReplayMode (Phase 0 audit), and
    // DrawResolvedTargetingArrows already draws the battle arrow off the live state.Battle,
    // which is a real, correctly-reconstructed field after ResimulateReplayTo. What's missing
    // is a spotlight on which card(s) the CURRENT command touched and a plain-text description
    // of it — the "brief text description" + "active-card highlight" + "target indicators"
    // requirements — since nothing upstream tracks "the card this specific step was about."
    private void DrawReplayActionOverlay()
    {
        if (!replayShowDescriptions) return;
        if (replayActions == null || replayCursor <= 0 || replayCursor > replayActions.Count) return;
        var action = replayActions[replayCursor - 1];
        var cmd = action.Command;
        if (cmd == null) return;

        // Active-card + target highlight: glow every card this command actually named.
        void Glow(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return;
            if (cardTargetRects.TryGetValue(instanceId, out var rect) && rect != null)
                AddEffectGlow(rect);
        }
        Glow(cmd.InstanceId);
        Glow(cmd.Attacker);
        Glow(cmd.Blocker);
        Glow(cmd.Target);

        // Brief text description, top-center of the board, synced to the current step.
        string text = TrimForReplayLog(action.Summary);
        if (string.IsNullOrEmpty(text)) return;
        var banner = PanelObject("Replay Action Banner", boardRoot, new Color32(10, 14, 20, 210));
        banner.anchorMin = new Vector2(0.5f, 1f); banner.anchorMax = new Vector2(0.5f, 1f);
        banner.pivot = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(Mathf.Clamp(text.Length * 7.5f + 40f, 220f, 720f), 34f);
        banner.anchoredPosition = new Vector2(0f, -8f);
        Round(banner);
        AddRoundedCardBorder(banner, Accent, 1f);
        var bannerText = TextObject("t", banner, text, 13, Ink, TextAnchor.MiddleCenter, monoFont);
        Stretch(bannerText.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
    }

    private void ShowContextTargetArrow(CardInstance targetCard)
    {
        HideHoverTargetArrow();
        if (state == null || targetCard == null || state.DeckLook != null) return;
        if (!cardTargetRects.TryGetValue(targetCard.InstanceId, out var target) || target == null) return;

        RectTransform source = null;
        Color color = new Color(1f, 0.78f, 0.24f, 0.95f);

        if (state.PendingEffects.Count > 0)
        {
            var effect = state.PendingEffects[0];
            if (!string.IsNullOrEmpty(effect.SourceInstanceId))
                cardTargetRects.TryGetValue(effect.SourceInstanceId, out source);
        }
        else if (!string.IsNullOrEmpty(selectedId) && selectedSeat == state.ActiveSeat && targetCard.Owner != selectedSeat)
        {
            cardTargetRects.TryGetValue(selectedId, out source);
            color = new Color(1f, 0.32f, 0.10f, 0.95f);
        }

        if (source == null || source == target) return;
        hoverTargetArrowRoot = NewArrowRoot("Hover Target Arrow", canvas.transform).gameObject;
        DrawCurvedTargetingArrow(hoverTargetArrowRoot.transform as RectTransform, source, target, color, 13f);
    }

    private void HideHoverTargetArrow()
    {
        if (hoverTargetArrowRoot == null) return;
        Destroy(hoverTargetArrowRoot);
        hoverTargetArrowRoot = null;
    }

    private RectTransform NewArrowRoot(string name, Transform parent)
    {
        var root = new GameObject(name).AddComponent<RectTransform>();
        root.SetParent(parent, false);
        Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        root.SetAsLastSibling();
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        return root;
    }

    private void DrawCurvedTargetingArrow(RectTransform root, RectTransform source, RectTransform target, Color color, float thickness)
    {
        if (root == null || source == null || target == null) return;
        var sourceCenter = RectScreenCenter(source);
        var targetCenter = RectScreenCenter(target);
        var delta = targetCenter - sourceCenter;
        if (delta.magnitude < 20f) return;

        var dir = delta.normalized;
        // Origin sits just past the card edge so the ribbon never overlays the source art.
        var start = sourceCenter + dir * RectScreenRadius(source, 0.56f);
        var end = targetCenter - dir * RectScreenRadius(target, 0.50f);
        RenderEnergyArrow(root, start, end, color, thickness, true);
    }

    // Same curved arrow, but the tip follows an arbitrary screen point (the cursor) instead of a
    // target rect. Used by the live drag-to-attack arrow.
    private void DrawCurvedTargetingArrowToPoint(RectTransform root, RectTransform source, Vector2 screenPoint, Color color, float thickness)
    {
        if (root == null || source == null) return;
        var sourceCenter = RectScreenCenter(source);
        var delta = screenPoint - sourceCenter;
        if (delta.magnitude < 12f) return;
        var dir = delta.normalized;
        var start = sourceCenter + dir * RectScreenRadius(source, 0.56f);
        var end = screenPoint;
        // Rebuilt every frame while dragging — no travelling sparks (they'd re-spawn per frame).
        RenderEnergyArrow(root, start, end, color, thickness, false);
    }

    // ── Solid targeting arrow ───────────────────────────────────────────────
    // Restored design (the assets outlived the code): a CLEAN, BOLD, OPAQUE arrow —
    // an opaque tapered body (arrow_core_opaque cross-section) over a soft halo
    // (arrow_glow_soft), capped by a flat solid triangle head (arrow_head_solid),
    // all tinted with the arrow color. No prongs, sparks or particles.
    private void RenderEnergyArrow(RectTransform root, Vector2 start, Vector2 end, Color color, float thickness, bool withFlow)
    {
        var delta = end - start;
        float dist = delta.magnitude;
        if (dist < 24f) return;
        var dir = delta / dist;
        var n = new Vector2(-dir.y, dir.x);
        // Bow varies continuously with horizontal offset — no side-flip crossing centre.
        float arc = Mathf.Clamp((end.x - start.x) * 0.22f, -80f, 80f);
        var p0 = start;
        var p1 = start + dir * (dist * 0.25f) + n * arc;
        var p2 = end - dir * (dist * 0.25f) + n * arc;
        var p3 = end;

        // Head geometry (400x300 art: tip at x≈0.89, blade tail at x≈0.14).
        float headW = Mathf.Clamp(dist * 0.20f, thickness * 4.0f, thickness * 7.5f);
        float headH = headW * (300f / 400f);
        const float tipFrac = 0.89f;
        float bodySpan = Mathf.Clamp01(1f - (headW * 0.55f) / dist);

        var jointPrev = CubicBezier(p0, p1, p2, p3, bodySpan - 0.02f);
        var joint = CubicBezier(p0, p1, p2, p3, bodySpan);
        var hDir = (joint - jointPrev).normalized;
        float headAng = Mathf.Atan2(hDir.y, hDir.x) * Mathf.Rad2Deg;

        // Body: soft halo + opaque core, both from vertical-profile strips whose columns
        // are uniform — quads can never show seams through taper or curvature.
        var coreSprite = LoadFxSprite("arrow_core_opaque");
        var coreCol = Color.Lerp(color, Color.white, 0.12f);
        const int SegN = 30;
        for (int i = 0; i < SegN; i++)
        {
            float t0 = i / (float)SegN * bodySpan;
            float t1 = (i + 1) / (float)SegN * bodySpan;
            var a = CubicBezier(p0, p1, p2, p3, t0);
            var b = CubicBezier(p0, p1, p2, p3, t1);
            var mid = (a + b) * 0.5f;
            float segLen = (b - a).magnitude * 1.07f;
            float segAng = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float taper = 1f - 0.30f * (i / (float)SegN);   // gentle taper toward the head
            var c = ImageObject("Arrow Body", root, coreSprite);
            c.color = new Color(coreCol.r, coreCol.g, coreCol.b, 0.97f);
            c.raycastTarget = false;
            c.rectTransform.position = mid;
            c.rectTransform.sizeDelta = new Vector2(segLen, thickness * 1.15f * taper);
            c.rectTransform.localRotation = Quaternion.Euler(0, 0, segAng);
        }

        // Head: soft tinted glow copy behind, then the solid tinted triangle with its
        // tip landing exactly on the target point.
        Vector2 headCenter = p3 - (Vector2)(hDir * (headW * (tipFrac - 0.5f)));
        var headSolid = LoadFxSprite("arrow_head_solid");
        var hs = ImageObject("Arrow Head", root, headSolid);
        hs.color = new Color(coreCol.r, coreCol.g, coreCol.b, 0.97f);
        hs.raycastTarget = false;
        hs.rectTransform.position = headCenter;
        hs.rectTransform.sizeDelta = new Vector2(headW, headH);
        hs.rectTransform.localRotation = Quaternion.Euler(0, 0, headAng);
    }

    // Runtime-loaded FX sprites (StreamingAssets/fx/<name>.png). Cached; soft dot fallback.
    private readonly Dictionary<string, Sprite> _fxSprites = new Dictionary<string, Sprite>();
    private Sprite LoadFxSprite(string name)
    {
        if (_fxSprites.TryGetValue(name, out var cached) && cached != null) return cached;
        Sprite sprite = null;
        try
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "fx", name + ".png");
            if (System.IO.File.Exists(path))
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (tex.LoadImage(bytes))
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }
        catch (System.Exception) { }
        if (sprite == null) sprite = GetSoftDotSprite();
        _fxSprites[name] = sprite;
        return sprite;
    }

    private static Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band    // Soft-edged chevron (">" pointing +X) drawn as a signed-distance band — smooth at any size.
    private Sprite _chevronSprite;
    private Sprite GetChevronSprite()
    {
        if (_chevronSprite != null) return _chevronSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x / (S - 1f), fy = y / (S - 1f) - 0.5f;   // fy in [-0.5, 0.5]
                // Chevron band: the "arm line" runs from (0.15,±0.5) to (0.85,0); distance to it.
                float armX = Mathf.Lerp(0.15f, 0.85f, 1f - Mathf.Abs(fy) * 2f);
                float d = Mathf.Abs(fx - armX);
                float aVal = Mathf.Clamp01(1f - d / 0.16f);
                aVal = aVal * aVal * (3f - 2f * aVal);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(aVal * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _chevronSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _chevronSprite;
    }

    // Sleek kite arrowhead with a concave base (MTGA-style), soft anti-aliased edges,
    // pointing +X. Layered twice (color + white core) at render time.
    private Sprite _energyHeadSprite;
    private Sprite GetEnergyHeadSprite()
    {
        if (_energyHeadSprite != null) return _energyHeadSprite;
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x / (S - 1f), fy = Mathf.Abs(y / (S - 1f) - 0.5f) * 2f;   // fy in [0,1]
                // Kite: leading edge tip at fx=1; width shrinks toward the tip; concave back edge.
                float halfWidth = (1f - fx) < 0f ? 0f : Mathf.Pow(1f - fx, 0.62f) * 0.92f;
                float backCut = 0.22f * (1f - fy * fy);   // concave notch at the back
                bool inside = fx > backCut && fy < halfWidth;
                float edge = Mathf.Min(
                    (halfWidth - fy) * 3.2f,
                    (fx - backCut) * 6f);
                float aVal = inside ? Mathf.Clamp01(edge) : 0f;
                aVal = aVal * aVal * (3f - 2f * aVal);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(aVal * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _energyHeadSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _energyHeadSprite;
    }

    private void AddArrowSegment(RectTransform root, Vector2 a, Vector2 b, Color color, float thickness)
    {
        var delta = b - a;
        var length = delta.magnitude;
        if (length <= 0.1f) return;
        var img = ImageObject("Target Arrow Segment", root, null);
        img.color = color;
        img.raycastTarget = false;
        img.rectTransform.position = (a + b) * 0.5f;
        img.rectTransform.sizeDelta = new Vector2(length + thickness * 0.35f, thickness);
        img.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void AddArrowHead(RectTransform root, Vector2 point, Vector2 direction, Color color, float size)
    {
        var img = ImageObject("Target Arrow Head", root, GetArrowHeadSprite());
        img.color = color;
        img.raycastTarget = false;
        // 'point' is where the TIP should land (e.g. exactly on the cursor). The sprite's tip sits
        // ~0.46 of its height above centre, so pull the head back along 'direction' by that much.
        img.rectTransform.position = point - direction * (size * 0.46f);
        img.rectTransform.sizeDelta = new Vector2(size * 1.2f, size);
        img.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
    }

    private static Vector2 Bezier(Vector2 start, Vector2 control, Vector2 end, float t)
    {
        var a = Vector2.Lerp(start, control, t);
        var b = Vector2.Lerp(control, end, t);
        return Vector2.Lerp(a, b, t);
    }

    private static Vector2 RectScreenCenter(RectTransform rect)
    {
        return RectTransformUtility.WorldToScreenPoint(null, rect.position);
    }

    private static float RectScreenRadius(RectTransform rect, float scale)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var center = RectScreenCenter(rect);
        var max = 0f;
        for (int i = 0; i < corners.Length; i++)
            max = Mathf.Max(max, Vector2.Distance(center, RectTransformUtility.WorldToScreenPoint(null, corners[i])));
        return max * scale;
    }

    // Four L-shaped corner brackets framing a zone (accent color), drawn in the zone's 0-1 anchor space.
    private void AddCornerBrackets(RectTransform zone, Color col)
    {
        float len = 0.09f, th = 0.02f;
        var corners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        foreach (var c in corners)
        {
            var h = PanelObject("Bracket H", zone, col);
            h.GetComponent<Image>().raycastTarget = false;
            float hx0 = c.x == 0 ? 0f : 1f - len, hx1 = c.x == 0 ? len : 1f;
            float hy0 = c.y == 0 ? 0f : 1f - th,  hy1 = c.y == 0 ? th : 1f;
            Stretch(h, new Vector2(hx0, hy0), new Vector2(hx1, hy1), Vector2.zero, Vector2.zero);

            var v = PanelObject("Bracket V", zone, col);
            v.GetComponent<Image>().raycastTarget = false;
            float vx0 = c.x == 0 ? 0f : 1f - th,  vx1 = c.x == 0 ? th : 1f;
            float vy0 = c.y == 0 ? 0f : 1f - len, vy1 = c.y == 0 ? len : 1f;
            Stretch(v, new Vector2(vx0, vy0), new Vector2(vx1, vy1), Vector2.zero, Vector2.zero);
        }
    }

    private RectTransform MatZone(RectTransform parent, string label, Vector2 min, Vector2 max, Color color, bool top)
    {
        // Cobalt fills: only the wide Character/Cost rows take the stronger team tint; every other zone
        // (leader, stage, deck, trash, DON, life) uses the very faint zoneF, like the mock.
        bool bigZone = label.Contains("CHARACTER") || label.Contains("COST");
        var fill = bigZone ? (top ? ZoneOpp : ZoneYou) : ZoneFaintFill;
        var zone = PanelObject(label + " Mat Zone", parent, fill);
        Stretch(zone, min, max, Vector2.zero, Vector2.zero);
        // HOLLOW rounded border (AddOutline floods solid quads, which is what made every zone a pale
        // block). A real border sprite gives the mock's crisp thin-outlined boxes.
        AddRoundedCardBorder(zone, ZoneBorder, 1.6f);
        AddBoardCancel(zone);
        // (Zone caption text removed per design - the zones are identified by their contents/position.)
        return zone;
    }

    // A cyan count chip at a pile's top-right corner (deck/trash/DON/life), matching the mock.
    private void AddCountBadge(RectTransform zone, int count)
    {
        var badge = PanelObject("Count Badge", zone, Accent);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, 1f);
        badge.pivot = new Vector2(0.5f, 0.5f);
        badge.sizeDelta = new Vector2(21f, 21f);
        badge.anchoredPosition = new Vector2(-4f, -4f);
        RoundCircle(badge);
        var t = TextObject("Count", badge, count.ToString(), 11, BadgeInk, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private RectTransform AddPileCardToZone(RectTransform zone, string label, int count, bool faceDown, CardInstance topCard, bool inverted = false)
    {
        var holder = new GameObject(label + " Card Holder").AddComponent<RectTransform>();
        holder.SetParent(zone, false);
        FitCardAspect(zone, holder);
        if (inverted) holder.localRotation = Quaternion.Euler(0, 0, 180f);

        if (topCard != null)
        {
            if (count > 1)
            {
                AddFaceUpStack(holder, topCard, count);
                holder.anchoredPosition += new Vector2(0f, (inverted ? -1f : 1f) * StackDepth(count) * 0.5f);
            }
            else
            {
                AddCard(holder, topCard, topCard.Owner, true, Vector2.zero, true);
            }
        }
        else if (count > 0)
        {
            AddBackStack(holder, count);
            // Push the holder toward the zone top by half the stack depth so the deepest (bottom-most)
            // card lands near the zone's bottom edge - the zone then fits snug to the full pile.
            holder.anchoredPosition += new Vector2(0f, (inverted ? -1f : 1f) * StackDepth(count) * 0.5f);
        }

        AddCountBadge(zone, count);
        return holder;
    }

    private void AddDonDeckPileToZone(RectTransform zone, int count, bool inverted = false)
    {
        var holder = new GameObject("DON Deck Holder").AddComponent<RectTransform>();
        holder.SetParent(zone, false);
        FitCardAspect(zone, holder);
        if (inverted) holder.localRotation = Quaternion.Euler(0, 0, 180f);
        if (count > 0)
        {
            AddBackStack(holder, count, GetDonBackSprite(), true);
            holder.anchoredPosition += new Vector2(0f, (inverted ? -1f : 1f) * StackDepth(count) * 0.5f);
        }

        AddCountBadge(zone, count);
    }

    private void AddLifeStackToZone(RectTransform zone, int count, bool top, string seat = null,
        IList<CardInstance> lifeCards = null, bool revealAll = false)
    {
        int visible = Mathf.Min(count, 5);
        // Sideways cards stacked vertically. No per-card border or divider - hovering a card shows the
        // highlight, which is separation enough.
        float gap = boardCardSize.x * 0.32f;

        for (int i = 0; i < visible; i++)
        {
            var card = PanelObject("Life Card", zone, new Color(0, 0, 0, 0));
            FitCardAspect(zone, card);
            card.localRotation = Quaternion.Euler(0, 0, top ? 270f : 90f);
            card.anchoredPosition += new Vector2(0f, (i - (visible - 1) * 0.5f) * gap);
            // Face-down normally. Show the FACE for a card an effect turned face-up (CardInstance
            // .FaceUp — e.g. Nami's [On K.O.] "turn 1 Life card face-up" cost) or when the whole Life
            // is revealed at end of match; fall back to the back sprite if the face isn't loaded yet.
            bool faceUp = lifeCards != null && i < lifeCards.Count && (revealAll || lifeCards[i].FaceUp);
            Sprite lifeFace = faceUp ? GetCardSprite(lifeCards[i].CardId) : null;
            RoundedCardVisual(lifeFace != null ? "Life Face" : "Life Back", card, lifeFace ?? GetBackSprite(), out var img);
            img.raycastTarget = false;
            card.gameObject.AddComponent<BackHover>().Init(this);
            // Per-CARD presence: the opponent's glow lands on this exact Life card,
            // not the whole Life region.
            if (isNetworked && seat != null)
            {
                string lifeToken = "life:" + seat + ":" + i;
                presenceGlowRects[lifeToken] = card;
                card.gameObject.AddComponent<ZoneHoverPresence>().Init(this, lifeToken);
            }
            if (seat != null)
            {
                if (!lifeMoveRects.TryGetValue(seat, out var lml)) lifeMoveRects[seat] = lml = new List<RectTransform>();
                while (lml.Count <= i) lml.Add(null);
                lml[i] = card;
            }
        }

        AddCountBadge(zone, count);
    }

    private void AddDonCardVisual(RectTransform parent, bool rested, bool stretch)
    {
        var root = PanelObject("DON!! Card", parent, new Color(0, 0, 0, 0));
        if (stretch) Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddOutline(root.gameObject, new Color(0, 0, 0, 0), 1f);
        RoundedCardVisual("DON Art", root, GetDonSprite(), out var img);
        img.raycastTarget = false;
        // Rested DON stay fully opaque/bold - the rotation alone conveys "rested" (matches characters).

        var hover = root.gameObject.AddComponent<DonHover>();
        hover.Init(this);
    }

    private void DrawMatBackground()
    {
        var top = PanelObject("North Mat Wash", boardRoot, new Color32(25, 37, 51, 255));
        Stretch(top, new Vector2(0, 0.53f), new Vector2(1, 1), new Vector2(18, -18), new Vector2(-18, -8));
        var bottom = PanelObject("South Mat Wash", boardRoot, new Color32(31, 43, 36, 255));
        Stretch(bottom, new Vector2(0, 0), new Vector2(1, 0.47f), new Vector2(18, 18), new Vector2(-18, 8));
    }

    private void DrawPlayerBoard(PlayerState p, string seat, bool top)
    {
        var root = new GameObject(seat + " Board").AddComponent<RectTransform>();
        root.SetParent(boardRoot, false);
        Stretch(root, new Vector2(0.02f, top ? 0.55f : 0.03f), new Vector2(0.98f, top ? 0.98f : 0.45f), Vector2.zero, Vector2.zero);

        var active = state.Status == "active" && state.ActiveSeat == seat;
        var shell = PanelObject(seat + " Shell", root, active ? new Color32(44, 40, 32, 222) : new Color32(16, 22, 31, 205));
        Stretch(shell, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        shell.GetComponent<Image>().color = active ? new Color32(48, 44, 36, 228) : new Color32(16, 22, 31, 205);
        if (active) AddOutline(shell.gameObject, Gold, 2f);

        DrawHeader(root, p, seat, top);
        DrawCharacters(root, p, seat, top);
        DrawCoreZones(root, p, seat, top);
        DrawCostArea(root, p, seat, top);
        DrawHand(root, p, seat, top);
    }

    private void DrawHeader(RectTransform root, PlayerState p, string seat, bool top)
    {
        var y0 = top ? 0.88f : 0.86f;
        var y1 = top ? 0.99f : 0.98f;
        var title = TextObject(seat + " Title", root, $"{p.Name}  |  {p.DeckName}", 17, Ink, TextAnchor.MiddleLeft);
        Stretch(title.rectTransform, new Vector2(0.02f, y0), new Vector2(0.58f, y1), Vector2.zero, Vector2.zero);

        var counts = $"Deck {p.Deck.Count}    Life {p.Life.Count}    DON {GameEngine.ActiveDonCount(p)}/{GameEngine.RestedDonCount(p)}";
        var countText = TextObject(seat + " Counts", root, counts, 15, Muted, TextAnchor.MiddleRight);
        Stretch(countText.rectTransform, new Vector2(0.52f, y0), new Vector2(0.98f, y1), Vector2.zero, Vector2.zero);
    }

    private void DrawCharacters(RectTransform root, PlayerState p, string seat, bool top)
    {
        var zone = ZonePanel(root, "Character Area", top ? new Vector2(0.02f, 0.05f) : new Vector2(0.02f, 0.60f), top ? new Vector2(0.98f, 0.26f) : new Vector2(0.98f, 0.82f));
        var row = RowObject("Characters", zone, 8, TextAnchor.MiddleCenter);
        Stretch(row, new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.72f), Vector2.zero, Vector2.zero);
        foreach (var card in p.CharacterArea)
        {
            if (card != null) AddCard(row, card, seat, true, new Vector2(64, 90));
            else AddEmptySlot(row, "", new Vector2(64, 90));
        }
    }

    private void DrawCoreZones(RectTransform root, PlayerState p, string seat, bool top)
    {
        var yMin = top ? 0.30f : 0.31f;
        var yMax = top ? 0.57f : 0.56f;
        var x = 0.02f;
        var step = 0.094f;

        var leader = ZonePanel(root, "Leader", new Vector2(x, yMin), new Vector2(x + 0.076f, yMax));
        AddCardToZone(leader, p.Leader, seat, true);
        x += step;

        var stage = ZonePanel(root, "Stage", new Vector2(x, yMin), new Vector2(x + 0.076f, yMax));
        stageZoneRects[seat] = stage;   // Blitz clock anchors beside this
        if (p.Stage != null) AddCardToZone(stage, p.Stage, seat, true);
        else AddCenteredText(stage, "Stage");
        x += step;

        boardDeckPileRects[seat] = AddPile(root, "Deck", p.Deck.Count, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), true);
        x += step;
        AddPile(root, "DON!! Deck", p.DonDeck, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), false);
        x += step;
        var lifePile = AddPile(root, "Life", p.Life.Count, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), true);
        MaybeAddLifeTargetPicker(lifePile, p, seat);
        x += step;
        AddPile(root, "Trash", p.Trash.Count, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), p.Trash.Count > 0);
    }

    private void DrawCostArea(RectTransform root, PlayerState p, string seat, bool top)
    {
        var zone = ZonePanel(root, "Cost Area", top ? new Vector2(0.60f, 0.30f) : new Vector2(0.60f, 0.31f), top ? new Vector2(0.98f, 0.57f) : new Vector2(0.98f, 0.56f));
        var row = RowObject("DON Row", zone, 6, TextAnchor.MiddleLeft);
        Stretch(row, new Vector2(0.03f, 0.16f), new Vector2(0.98f, 0.76f), Vector2.zero, Vector2.zero);
        foreach (var don in p.CostArea) AddDon(row, don);
        if (p.CostArea.Count == 0) AddEmptySlot(row, "No DON!!", new Vector2(58, 36));
    }

    private void DrawHand(RectTransform root, PlayerState p, string seat, bool top)
    {
        var zone = ZonePanel(root, top ? $"Hand ({p.Hand.Count})" : $"Hand ({p.Hand.Count}) - click to play", top ? new Vector2(0.02f, 0.62f) : new Vector2(0.02f, 0.04f), top ? new Vector2(0.56f, 0.84f) : new Vector2(0.98f, 0.27f));
        var row = RowObject("Hand Row", zone, 6, TextAnchor.MiddleLeft);
        Stretch(row, new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.74f), Vector2.zero, Vector2.zero);
        foreach (var card in p.Hand) AddCard(row, card, seat == "south" ? "south-hand" : seat, seat == "south", new Vector2(64, 90));
    }

    private void DrawBattleBand()
    {
        var band = PanelObject("Battle Band", boardRoot, state.Battle == null ? new Color32(18, 24, 34, 255) : new Color32(54, 28, 32, 255));
        Stretch(band, new Vector2(0.02f, 0.47f), new Vector2(0.98f, 0.53f), Vector2.zero, Vector2.zero);
        AddOutline(band.gameObject, state.Battle == null ? Line : RedAccent, 1.5f);

        var text = "No active attack";
        if (state.Battle != null)
        {
            var b = state.Battle;
            var attacker = FindAny(b.AttackerSeat, b.AttackerId);
            var target = FindAny(b.TargetSeat, b.TargetId);
            var attackerName = attacker != null ? GameEngine.GetCard(attacker).Name : "Attacker";
            var targetName = target != null ? GameEngine.GetCard(target).Name : "Target";
            int liveAtk2 = attacker != null ? GameEngine.GetPower(state, attacker) : b.AttackPower;
            int liveDef2 = target != null ? (GameEngine.GetPower(state, target) + b.CounterPower) : b.DefensePower;
            text = $"{attackerName} ({liveAtk2}) attacking {targetName} ({liveDef2})  |  {StepLabel(b.Step)}";
        }
        var label = TextObject("Battle Text", band, text, 18, Ink, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform, new Vector2(0.02f, 0), new Vector2(0.98f, 1), Vector2.zero, Vector2.zero);
    }

    private void DrawSidePanel()
    {
        // Header: title + clock.
        var title = TextObject("Title", sideRoot, "One Piece TCG", 20, Ink, TextAnchor.MiddleLeft, titleFont);
        Stretch(title.rectTransform, new Vector2(0.06f, 0.96f), new Vector2(0.60f, 0.985f), Vector2.zero, Vector2.zero);
        var clock = TextObject("Clock", sideRoot, System.DateTime.Now.ToString("HH:mm"), 14, Accent, TextAnchor.MiddleRight, monoFont);
        Stretch(clock.rectTransform, new Vector2(0.58f, 0.96f), new Vector2(0.83f, 0.985f), Vector2.zero, Vector2.zero);

        // Locked out while the ranked/casual result screen is up — the only exit is its
        // Main Menu button (guarantees the match tears down cleanly).
        if (!ResultScreenActive()) DrawMenuButton();

        var sub = TextObject("Subtitle", sideRoot, $"TURN {state.TurnNumber}  ·  ROOM LOCAL", 10, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(sub.rectTransform, new Vector2(0.06f, 0.938f), new Vector2(0.62f, 0.956f), Vector2.zero, Vector2.zero);
        bool youActive = state.ActiveSeat == BottomSeat;
        var turnBadge = TextObject("Turn Badge", sideRoot, youActive ? "Your turn" : "Opp turn", 10, Accent2, TextAnchor.MiddleRight);
        Stretch(turnBadge.rectTransform, new Vector2(0.62f, 0.938f), new Vector2(0.94f, 0.956f), Vector2.zero, Vector2.zero);

        DrawPhaseTracker();

        var actionsLabel = TextObject("Actions Label", sideRoot, "ACTIONS", 9, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(actionsLabel.rectTransform, new Vector2(0.06f, 0.866f), new Vector2(0.94f, 0.886f), Vector2.zero, Vector2.zero);

        // Action list is the dominant block (mock: flex:1) - fills the space down to the chat.
        var actionBubble = PanelObject("Action Bubble", sideRoot, LogBgDark);
        Stretch(actionBubble, new Vector2(0.06f, 0.34f), new Vector2(0.94f, 0.86f), Vector2.zero, Vector2.zero);
        RoundBig(actionBubble);
        AddRoundedCardBorder(actionBubble, MenuB, 1f);

        var actionPanel = PanelObject("Action Panel", sideRoot, new Color(0, 0, 0, 0));
        Stretch(actionPanel, new Vector2(0.085f, 0.35f), new Vector2(0.915f, 0.852f), Vector2.zero, Vector2.zero);
        DrawContextActions(actionPanel);

        // Networked matches use the left-edge match chat panel (DrawMatchChatPanel) instead.
        if (!isNetworked) DrawChatPanel();
        DrawPlayerPlate(sideRoot, state.Players.ContainsKey(BottomSeat) ? state.Players[BottomSeat] : null, BottomSeat, state.ActiveSeat == BottomSeat, new Vector2(0.06f, 0.094f), new Vector2(0.94f, 0.138f));
        AddEndTurnPanel();

        // Built last so the dropdown overlays everything else in the side panel.
        if (menuOpen && !ResultScreenActive()) DrawGameMenu();
        if (soundMenuOpen) DrawSoundMenu();
        if (surrenderConfirmOpen) DrawSurrenderConfirm();
    }

    // Hamburger button in the upper-right corner; toggles the game menu.
    private void DrawMenuButton()
    {
        var btn = PanelObject("Menu Button", sideRoot, menuOpen ? Accent : (Color)new Color32(34, 58, 78, 235));
        Stretch(btn, new Vector2(0.885f, 0.961f), new Vector2(0.95f, 0.985f), Vector2.zero, Vector2.zero);
        Round(btn);
        AddRoundedCardBorder(btn, menuOpen ? Accent2 : MenuB, 1f);
        for (int i = 0; i < 3; i++)
        {
            var bar = PanelObject("Bar", btn, menuOpen ? (Color)BadgeInk : (Color)Accent2);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = new Vector2(11f, 1.8f);
            bar.anchoredPosition = new Vector2(0f, (1 - i) * 3.7f);
            Round(bar);
        }
        var button = btn.gameObject.AddComponent<Button>();
        button.onClick.AddListener(() => { menuOpen = !menuOpen; Render(); });
    }

    // Dropdown menu under the hamburger. Options populate here; for now: New Match, Close.
    private void DrawGameMenu()
    {
        var menu = PanelObject("Game Menu", sideRoot, (Color)new Color32(12, 23, 38, 250));
        // Four items: New Match, Main Menu, Sound (opens the sound panel), Close.
        Stretch(menu, new Vector2(0.52f, 0.64f), new Vector2(0.965f, 0.95f), Vector2.zero, Vector2.zero);
        RoundBig(menu);
        AddRoundedCardBorder(menu, Accent, 1.3f);
        menu.SetAsLastSibling();

        // Main Menu is the TOP slot; New Match / Surrender sits just below it.
        bool netActive = isNetworked && !isReplayMode && state != null && state.Status == "active" && !opponentLeft;
        AddMenuItem(menu, "Main Menu", new Vector2(0.07f, 0.79f), new Vector2(0.93f, 0.955f),
            () => { menuOpen = false; ReturnToMenu(); });
        if (netActive)
            AddMenuItem(menu, "Surrender", new Vector2(0.07f, 0.545f), new Vector2(0.93f, 0.71f),
                () => { menuOpen = false; surrenderConfirmOpen = true; Render(); });
        else
            AddMenuItem(menu, isSandbox ? "New Sandbox" : "New Match", new Vector2(0.07f, 0.545f), new Vector2(0.93f, 0.71f),
                () => { menuOpen = false; if (isSandbox) NewSandbox(); else NewMatch(); });
        AddMenuItem(menu, "Sound", new Vector2(0.07f, 0.30f), new Vector2(0.93f, 0.465f),
            () => { menuOpen = false; soundMenuOpen = true; Render(); });
        AddMenuItem(menu, "Close", new Vector2(0.07f, 0.045f), new Vector2(0.93f, 0.21f),
            () => { menuOpen = false; Render(); });
    }

    // Confirmation before surrendering a live match (opened from the game menu's "Surrender" item).
    // Guards against a mis-click throwing the game — surrender is irreversible.
    private void DrawSurrenderConfirm()
    {
        var dim = PanelObject("Surrender Dim", boardRoot, new Color32(8, 10, 14, 210));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.SetAsLastSibling();

        Color danger = new Color(0.93f, 0.48f, 0.48f);
        var panel = PanelObject("Surrender Panel", boardRoot, (Color)new Color32(14, 30, 46, 250));
        Stretch(panel, new Vector2(0.32f, 0.38f), new Vector2(0.68f, 0.62f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, danger, 2f);
        panel.SetAsLastSibling();

        var label = TextObject("Surrender Title", panel, "SURRENDER?", 22, danger, TextAnchor.MiddleCenter, titleFont);
        label.fontStyle = FontStyle.Bold;
        Stretch(label.rectTransform, new Vector2(0.06f, 0.58f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero);

        var sub = TextObject("Surrender Sub", panel, "You'll lose this match. This can't be undone.", 12, Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(sub.rectTransform, new Vector2(0.06f, 0.42f), new Vector2(0.94f, 0.58f), Vector2.zero, Vector2.zero);

        var buttons = RowObject("Surrender Buttons", panel, 10, TextAnchor.MiddleCenter);
        Stretch(buttons, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.38f), Vector2.zero, Vector2.zero);
        AddButton(buttons, "CANCEL", () => { surrenderConfirmOpen = false; Render(); });
        AddButton(buttons, "SURRENDER", ConfirmSurrender);
    }

    // Concede the current match: the opponent wins, the result is recorded + ranked-reported, and
    // (networked) the 'concede' command is sent so the opponent's client ends on the same result.
    // The engine command (GameEngine.Concede) is the single source of truth on both sides. Dispatch
    // handles the local apply, the finished-match records, and the network send.
    private void ConfirmSurrender()
    {
        surrenderConfirmOpen = false;
        if (state == null || state.Status == "finished") { Render(); return; }
        Dispatch(new GameCommand { Type = "concede", Seat = isNetworked ? localSeat : "south" });
    }

    // Dedicated sound-settings panel (opened from the game menu's "Sound" item):
    // an SFX volume slider with a live percent readout and a close button.
    private void DrawSoundMenu()
    {
        var panel = PanelObject("Sound Menu", sideRoot, (Color)new Color32(12, 23, 38, 252));
        Stretch(panel, new Vector2(0.10f, 0.60f), new Vector2(0.965f, 0.86f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 1.3f);
        panel.SetAsLastSibling();

        var title = TextObject("Sound Title", panel, "SOUND", 12, Ink, TextAnchor.MiddleLeft, monoFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0.07f, 0.80f), new Vector2(0.6f, 0.96f), Vector2.zero, Vector2.zero);

        var lbl = TextObject("SFX Label", panel, "SFX VOLUME", 9, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(lbl.rectTransform, new Vector2(0.07f, 0.60f), new Vector2(0.6f, 0.76f), Vector2.zero, Vector2.zero);
        var pct = TextObject("SFX Percent", panel, Mathf.RoundToInt(SfxVolume * 100f) + "%", 9, Accent2, TextAnchor.MiddleRight, monoFont);
        Stretch(pct.rectTransform, new Vector2(0.6f, 0.60f), new Vector2(0.93f, 0.76f), Vector2.zero, Vector2.zero);

        BuildSfxSlider(panel, new Vector2(0.07f, 0.40f), new Vector2(0.93f, 0.56f), pct);

        AddMenuItem(panel, "Close", new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.30f),
            () => { soundMenuOpen = false; Render(); });
    }

    // A working uGUI slider built by hand (0–1, persisted via GameManager.SfxVolume).
    // Every RectTransform gets explicit anchors/sizes — freshly created rects default to
    // a centred 100×100 block, which is exactly what made the first version explode.
    private void BuildSfxSlider(RectTransform parent, Vector2 min, Vector2 max, Text percentReadout)
    {
        var track = PanelObject("SFX Track", parent, new Color(1f, 1f, 1f, 0.10f));
        Stretch(track, min, max, Vector2.zero, Vector2.zero);
        Round(track);

        var fillArea = PanelObject("Fill Area", track, new Color(0f, 0f, 0f, 0f));
        Stretch(fillArea, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fillArea.GetComponent<Image>().raycastTarget = false;
        var fill = PanelObject("Fill", fillArea, Accent);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);   // Slider drives anchorMax.x = value
        fill.pivot = new Vector2(0f, 0.5f);
        fill.sizeDelta = Vector2.zero;
        fill.anchoredPosition = Vector2.zero;
        Round(fill);
        fill.GetComponent<Image>().raycastTarget = false;

        var handleArea = PanelObject("Handle Area", track, new Color(0f, 0f, 0f, 0f));
        Stretch(handleArea, Vector2.zero, Vector2.one, new Vector2(7f, 0f), new Vector2(-7f, 0f));
        handleArea.GetComponent<Image>().raycastTarget = false;
        var handle = PanelObject("Handle", handleArea, new Color32(230, 240, 248, 255));
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);   // Slider drives anchor x = value
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(14f, 5f);  // width 14, 5px taller than the track
        handle.anchoredPosition = Vector2.zero;
        RoundCircle(handle);

        var slider = track.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = SfxVolume;
        slider.onValueChanged.AddListener(v =>
        {
            SfxVolume = v;
            if (percentReadout != null) percentReadout.text = Mathf.RoundToInt(v * 100f) + "%";
            PlayCardDrawSfx();   // instant feedback at the new volume
        });
    }

    // Tears down the board and rebuilds the main menu in this same scene
    // (single-scene design — mirror of MainMenuManager.StartVersusSelfFlow).
    public void ReturnToMenu()
    {
        // Leaving a live networked match counts as a SURRENDER: concede first (while still connected)
        // so it's recorded as a loss for us and — via the concede message, or the opponent's own
        // disconnect fallback — a win for them. Skipped once the match is already finished (normal end,
        // or we surrendered from the result screen) so it never double-fires or turns a win into a loss.
        if (isNetworked && !isReplayMode && state != null && state.Status != "finished" && !opponentLeft)
            Dispatch(new GameCommand { Type = "concede", Seat = localSeat });
        // Custom versus-self decks only apply to the match they were picked for;
        // leaving the board falls back to the ST01-vs-ST02 default next time.
        PendingSouthDeckId = null;
        PendingNorthDeckId = null;
        // Release any lingering matchmaking proposal so a fast requeue starts a FRESH search
        // instead of being handed this finished match's dead session id ("stuck on connecting").
        // Server-side handleQueueJoin also clears it on re-join, but doing it here cleans up even
        // when the player just returns to the menu without queuing again. Ranked/casual only —
        // custom lobby matches never touch the queue.
        if (isNetworked && (networkedMode == "ranked" || networkedMode == "casual"))
            _ = RankedStore.QueueCancelAsync();
        // Tear down the networked match so the NEXT one can start a fresh Relay. Netcode's
        // NetworkManager stays connected after a match otherwise, and the Sessions SDK
        // refuses to start a new session on an already-connected NetworkManager, leaving
        // every 2nd match per app run stuck on "Connecting to your opponent…".
        LobbyManager.ShutdownNetwork();       // immediate, synchronous
        _ = LobbyManager.LeaveCurrentAsync(); // remove us from the UGS session (fire-and-forget)
        if (canvas != null) canvas.gameObject.SetActive(false);
        MainMenuManager.EnsureMenu();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // ── End-of-match rematch / deck-swap (result screen; see DrawMatchResultOverlay) ──

    // Custom online "Change Deck": return to the lobby waiting room WITHOUT tearing down —
    // the session/Netcode connection stays open so the host's next Start Match reuses it,
    // and each player can re-pick their deck there (the existing lobby flow handles both).
    // EnsureMenu shows the waiting room while LobbyManager.CurrentSession is non-null.
    // NOTE: networked path — needs live 2-client testing.
    public void ReturnToLobby()
    {
        PendingSouthDeckId = null;
        PendingNorthDeckId = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        MainMenuManager.EnsureMenu();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // Custom online one-click rematch. Both players must ask; the host (south) then picks a
    // shared seed and both restart in place. Mirrors the initial launch (host owns the seed),
    // but reuses the open session — no re-queue, no teardown.
    private void RequestRematch()
    {
        if (rematchLocalRequested || rematchStarting) return;
        rematchLocalRequested = true;
        MatchNetworkSync.SendRematchRequest();
        MaybeStartHostRematch();
        Render();
    }

    private void OnRematchRequested()
    {
        if (this == null) return;
        rematchPeerRequested = true;
        MaybeStartHostRematch();
        if (this != null && state != null) Render();
    }

    // Only the host (south) generates + broadcasts the seed, so both sides agree on one deal.
    private void MaybeStartHostRematch()
    {
        if (rematchStarting || localSeat != "south") return;
        if (!(rematchLocalRequested && rematchPeerRequested)) return;
        rematchStarting = true;
        string seed = System.Guid.NewGuid().ToString("N");
        MatchNetworkSync.SendRematchStart(seed);
        RestartNetworkedMatch(seed);
    }

    private void OnRematchStartReceived(string seed)
    {
        if (this == null || rematchStarting || string.IsNullOrEmpty(seed)) return;
        rematchStarting = true;
        RestartNetworkedMatch(seed);
    }

    // Rebuild a fresh networked match from the same decks (currentMatchConfig) + a new seed,
    // keeping the live session/connection. Mirrors the match-scoped resets in NewMatch /
    // EnterNetworkedMatch but does NOT touch isNetworked/localSeat/names/subscriptions.
    private void RestartNetworkedMatch(string seed)
    {
        var config = new MatchConfig { Seed = seed };
        if (currentMatchConfig?.SouthDeckDef != null) config.SouthDeckDef = currentMatchConfig.SouthDeckDef;
        if (currentMatchConfig?.NorthDeckDef != null) config.NorthDeckDef = currentMatchConfig.NorthDeckDef;
        currentMatchConfig = config;

        replaySaved = false;
        matchStartRealtime = Time.realtimeSinceStartup;
        commandElapsedSeconds.Clear();
        mulliganAnimShownKey = null;
        mulliganRedrawSeat = null;
        handDealAnimating = false;
        mulliganDealAnimating = 0;
        lastCardPoses.Clear();
        suppressMoveAnim.Clear();

        state = GameEngine.CreateMatch(config);
        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);

        finishedResultText = null;
        matchResultHidden = false;
        opponentLeft = false;
        rematchLocalRequested = false;
        rematchPeerRequested = false;
        rematchStarting = false;
        Render();
    }

    private void AddMenuItem(RectTransform parent, string label, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
    {
        var item = PanelObject(label + " Item", parent, new Color32(34, 58, 78, 235));
        Stretch(item, min, max, Vector2.zero, Vector2.zero);
        Round(item);
        AddRoundedCardBorder(item, MenuB, 1.1f);
        var d = PanelObject("Dot", item, (Color)Accent);
        d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
        d.pivot = new Vector2(0f, 0.5f);
        d.sizeDelta = new Vector2(6f, 6f);
        d.anchoredPosition = new Vector2(12f, 0f);
        RoundCircle(d);
        var text = TextObject("Text", item, label, 11, Ink, TextAnchor.MiddleLeft, monoFont);
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(26, 0), new Vector2(-8, 0));
        var button = item.gameObject.AddComponent<Button>();
        button.onClick.AddListener(action);
    }

    private void DrawPhaseTracker()
    {
        var row = RowObject("Phase Tracker", sideRoot, 3, TextAnchor.MiddleCenter);
        Stretch(row, new Vector2(0.06f, 0.905f), new Vector2(0.94f, 0.93f), Vector2.zero, Vector2.zero);
        string ph = state.Phase ?? "";
        // Live state mapping: MAIN ↔ ATTACK alternate with each declared/resolved battle;
        // the turn-start override sequences REFRESH → DRAW → DON while those animations play.
        string live = state.Battle != null || ph == "battle" ? "ATTACK"
            : ph == "main" ? "MAIN"
            : ph == "refresh" ? "REFRESH"
            : ph == "draw" ? "DRAW"
            : ph == "don" ? "DON"
            : "END";
        string shown = phaseDisplayOverride ?? live;
        var phases = new (string label, bool active)[] {
            ("REFRESH", shown == "REFRESH"),
            ("DRAW",    shown == "DRAW"),
            ("DON",     shown == "DON"),
            ("MAIN",    shown == "MAIN"),
            ("ATTACK",  shown == "ATTACK"),
            ("END",     shown == "END"),
        };
        foreach (var (label, active) in phases)
        {
            var pill = PanelObject(label + " Phase", row, active ? Accent : new Color(1f, 1f, 1f, 0.04f));
            pill.sizeDelta = new Vector2(36f, 18f);   // RowObject's HLG sizes by sizeDelta, not LayoutElement
            SetPreferred(pill, new Vector2(36f, 18f));
            Round(pill);
            if (!active) AddRoundedCardBorder(pill, ZoneBorder, 1f);
            var t = TextObject("t", pill, label, 8, active ? BadgeInk : Ink, TextAnchor.MiddleCenter, monoFont);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }
    }

    // A player plate (avatar dot + name + LIFE/HAND/DON), used for both seats.
    private void DrawPlayerPlate(RectTransform parent, PlayerState p, string seat, bool highlight, Vector2 min, Vector2 max)
    {
        // When it's this seat's turn, fill the plate with the same dark blue used inside
        // the actions block and turn just the border cyan; text stays light on the dark fill.
        var plate = PanelObject(seat + " Plate", parent, highlight ? LogBgDark : new Color(1f, 1f, 1f, 0.03f));
        Stretch(plate, min, max, Vector2.zero, Vector2.zero);
        RoundBig(plate);
        AddRoundedCardBorder(plate, highlight ? Accent : MenuB, highlight ? 1.5f : 1f);

        // South sits on the player's side: mirror the avatar to the right edge and
        // right-align the name so the plate reads as a reflection of the opponent's.
        bool mirror = seat == BottomSeat;

        float dotX = mirror ? 0.945f : 0.055f;
        // Show the leader's color-identity hex icon; fall back to a plain dot if unavailable.
        string leaderColor = null;
        if (p != null && p.Leader != null)
        {
            var ldef = GameEngine.GetCard(p.Leader);
            if (ldef != null) leaderColor = ldef.Color;
        }
        var iconTex = LoadColorIcon(leaderColor);
        if (iconTex != null)
        {
            var pivot = new Vector2(mirror ? 1f : 0f, 0.5f);
            // On the active player's turn the plate fills dark, so use the variant with a cyan rim
            // baked in (concentric by construction); otherwise the plain icon.
            var drawTex = highlight ? (GetIconWithOutline(iconTex, Accent) ?? iconTex) : iconTex;
            var iconGO = new GameObject("Avatar Icon").AddComponent<UnityEngine.UI.RawImage>();
            iconGO.transform.SetParent(plate, false);
            iconGO.texture = drawTex;
            iconGO.raycastTarget = false;
            var rt = iconGO.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(dotX, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(30f, 30f);
            rt.anchoredPosition = Vector2.zero;
        }
        else
        {
            var dot = PanelObject("Avatar", plate, highlight ? Accent : Accent2);
            dot.anchorMin = new Vector2(dotX, 0.5f); dot.anchorMax = new Vector2(dotX, 0.5f);
            dot.pivot = new Vector2(mirror ? 1f : 0f, 0.5f); dot.sizeDelta = new Vector2(30f, 30f); dot.anchoredPosition = Vector2.zero;
            RoundCircle(dot);
        }

        // Player plate: show the human display name, never the engine seat identifier
        // ("South"/"North" is a stable internal id — see DisplayName()). Falls back to
        // "Player 1/2" when no name was supplied, never to the raw seat.
        string name = DisplayName(seat);
        int life = p != null ? p.Life.Count : 0;
        int hand = p != null ? p.Hand.Count : 0;
        int don = p != null ? p.CostArea.Count : 0;
        Vector2 textMin = mirror ? new Vector2(0.03f, 0f) : new Vector2(0.24f, 0f);
        Vector2 textMax = mirror ? new Vector2(0.76f, 0f) : new Vector2(0.97f, 0f);
        var nm = TextObject("Plate Name", plate, name, 13, Ink, mirror ? TextAnchor.LowerRight : TextAnchor.LowerLeft, titleFont);
        Stretch(nm.rectTransform, new Vector2(textMin.x, 0.54f), new Vector2(textMax.x, 0.92f), Vector2.zero, Vector2.zero);
        var stats = TextObject("Plate Stats", plate, $"LIFE {life}   ·   HAND {hand}   ·   DON {don}", 9, Ink, mirror ? TextAnchor.UpperRight : TextAnchor.UpperLeft, monoFont);
        stats.fontStyle = FontStyle.Bold;
        Stretch(stats.rectTransform, new Vector2(textMin.x, 0.10f), new Vector2(textMax.x, 0.44f), Vector2.zero, Vector2.zero);
    }

    private sealed class ChatMessage { public string Sender; public string Text; public bool Mine; }
    private readonly List<ChatMessage> chatMessages = new List<ChatMessage>();
    private const int MaxChatHistory = 50;

    private void SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.Length > MatchNetworkSync.MaxChatLength) text = text.Substring(0, MatchNetworkSync.MaxChatLength);
        string sender = isNetworked ? DisplayName(localSeat) : "You";
        chatMessages.Add(new ChatMessage { Sender = sender, Text = text, Mine = true });
        TrimChatHistory();
        if (isNetworked) MatchNetworkSync.SendChat(text);
        chatDraft = "";
        Render();
    }

    private void OnNetworkChatReceived(string text)
    {
        if (!isNetworked || string.IsNullOrEmpty(text)) return;
        string opponentSeat = localSeat == "south" ? "north" : "south";
        chatMessages.Add(new ChatMessage { Sender = DisplayName(opponentSeat), Text = text, Mine = false });
        TrimChatHistory();
        if (!chatOpen) chatUnread = true;
        // Never rebuild mid-drag (Render() would destroy the dragged object under the
        // EventSystem) — the coalesced refresh in Update() picks it up instead.
        if (isDraggingHandCard || isDraggingAttack) _artRefreshQueued = true;
        else Render();
    }

    private void TrimChatHistory()
    {
        while (chatMessages.Count > MaxChatHistory) chatMessages.RemoveAt(0);
    }

    // Presence OUT bookkeeping: called by CardHover on enter/exit. Board hovers publish the
    // hovered instance id; hovering (lifting) one of OUR OWN hand cards publishes its hand
    // index. Update() streams the current state to the peer (rate-limited in the transport).
    private void SetPresenceHover(CardInstance card, bool isHandCard, bool entered)
    {
        if (!isNetworked || card == null || state == null) return;
        if (isHandCard)
        {
            if (entered)
            {
                int idx = -1;
                if (card.Owner == localSeat && state.Players.TryGetValue(localSeat, out var lp) && lp != null)
                    idx = lp.Hand.FindIndex(c => c.InstanceId == card.InstanceId);
                presenceRaisedHandIndex = idx;
            }
            else
            {
                presenceRaisedHandIndex = -1;
            }
        }
        else
        {
            if (entered) presenceHoverCardId = card.InstanceId;
            else if (presenceHoverCardId == card.InstanceId) presenceHoverCardId = null;
        }
    }

    // Presence IN: store the opponent's latest payload (each one is a full replacement) and
    // re-render its cosmetic effects. NEVER dispatches commands or touches game state.
    private void OnNetworkPresenceReceived(PresencePayload payload)
    {
        if (!isNetworked || isReplayMode) return;
        opponentPresence = payload;
        // Presence streams at ~10 packets/sec with the FULL state each time. Re-applying an
        // unchanged packet destroys and recreates the glow 10x a second — visible blinking.
        // Only re-apply when something actually changed.
        string hoverNow = payload != null ? (payload.hoverCardId ?? "") : "";
        string raisedNow = payload != null && payload.raisedHandIndexes != null ? string.Join(",", System.Array.ConvertAll(payload.raisedHandIndexes, x => x.ToString())) : "";
        string groupsNow = payload != null && payload.donGroups != null ? string.Join(",", System.Array.ConvertAll(payload.donGroups, x => x.ToString())) : "";
        bool groupsChanged = groupsNow != lastPresenceGroups;
        if (hoverNow == lastPresenceHover && raisedNow == lastPresenceRaised && !groupsChanged) return;
        lastPresenceHover = hoverNow;
        lastPresenceRaised = raisedNow;
        lastPresenceGroups = groupsNow;
        if (groupsChanged)
        {
            Render();   // opponent re-partitioned their DON!! — redraw their cost row grouped
            return;     // Render() re-applies presence at the end
        }
        ApplyOpponentPresence();
    }

    private void ApplyOpponentPresence()
    {
        // Clear previous cosmetic state first (also handles the empty-payload case).
        if (opponentPresenceGlow != null) { Destroy(opponentPresenceGlow.gameObject); opponentPresenceGlow = null; }
        foreach (var g in opponentPresenceHandGlows) if (g != null) Destroy(g.gameObject);
        opponentPresenceHandGlows.Clear();
        for (int i = 0; i < opponentHandSlots.Count; i++)
            if (opponentHandSlots[i] != null) opponentHandSlots[i].anchoredPosition = opponentHandSlotHomes[i];

        if (!isNetworked || opponentPresence == null) return;

        // (i) Gold hover glow on whatever the opponent is pointing at — board cards first,
        // then the extended registry (cost-area DON!!, deck / DON!! deck / trash piles, Life).
        if (!string.IsNullOrEmpty(opponentPresence.hoverCardId))
        {
            RectTransform glowRect = null;
            if (cardTargetRects.TryGetValue(opponentPresence.hoverCardId, out var cardRect) && cardRect != null)
                glowRect = (cardRect.Find("Card Face") as RectTransform) ?? cardRect;
            else if (presenceGlowRects.TryGetValue(opponentPresence.hoverCardId, out var zoneRect) && zoneRect != null)
                glowRect = zoneRect;
            if (glowRect != null)
                opponentPresenceGlow = AddMysticalCardOutline(glowRect, false);
        }

        // (ii) The opponent's inspected hand cards lift ~12px AND glow (the lift alone was
        // easy to miss; the glow matches every other "opponent is looking here" cue).
        if (opponentPresence.raisedHandIndexes != null)
        {
            foreach (var idx in opponentPresence.raisedHandIndexes)
            {
                if (idx < 0 || idx >= opponentHandSlots.Count) continue;
                var slot = opponentHandSlots[idx];
                if (slot == null) continue;
                slot.anchoredPosition = opponentHandSlotHomes[idx] + new Vector2(0f, -12f);
                opponentPresenceHandGlows.Add(AddMysticalCardOutline(slot, false));
            }
        }

        // (iii) A card the opponent is mid-reorder within their hand: lift it and slide it toward its
        // target slot, so you see them repositioning their hand in real time.
        int dFrom = opponentPresence.handDragFrom, dTo = opponentPresence.handDragTo;
        if (dFrom >= 0 && dFrom < opponentHandSlots.Count && dTo >= 0 && dTo < opponentHandSlotHomes.Count)
        {
            var dslot = opponentHandSlots[dFrom];
            if (dslot != null)
            {
                float tx = opponentHandSlotHomes[dTo].x;
                dslot.anchoredPosition = new Vector2(tx, opponentHandSlotHomes[dFrom].y - 16f);  // lifted + slid to target
                dslot.SetAsLastSibling();
                opponentPresenceHandGlows.Add(AddMysticalCardOutline(dslot, false));
            }
        }
    }

    // Publishes hover on non-card zones (DON!!, deck/DON!!-deck/trash piles, Life stacks) so
    // the opponent sees a glow on EVERYTHING the player inspects, not just board cards.
    private void SetZonePresenceHover(string token, bool entered)
    {
        if (!isNetworked || string.IsNullOrEmpty(token)) return;
        if (entered) presenceHoverCardId = token;
        else if (presenceHoverCardId == token) presenceHoverCardId = null;
    }

    private sealed class ZoneHoverPresence : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private string token;
        public void Init(GameManager m, string t) { manager = m; token = t; }
        public void OnPointerEnter(PointerEventData e) { if (manager != null) manager.SetZonePresenceHover(token, true); }
        public void OnPointerExit(PointerEventData e) { if (manager != null) manager.SetZonePresenceHover(token, false); }
    }

    // Networked-match chat: a collapsed tab on the LEFT screen edge; clicking it expands a
    // ~300px panel with the scrollable message history + an input field (Enter or Send to
    // send). Presence/game hotkeys are guarded while the input is focused (ChatInputFocused).
    private void DrawMatchChatPanel()
    {
        // Collapsed tab (always present so the panel can be re-opened).
        var tab = PanelObject("Chat Tab", boardRoot, chatOpen ? Accent : (Color)new Color32(34, 58, 78, 235));
        tab.anchorMin = new Vector2(0f, 0.5f);
        tab.anchorMax = new Vector2(0f, 0.5f);
        tab.pivot = new Vector2(0f, 0.5f);
        tab.sizeDelta = new Vector2(26f, 92f);
        tab.anchoredPosition = new Vector2(0f, 0f);
        Round(tab);
        AddRoundedCardBorder(tab, chatOpen ? Accent2 : MenuB, 1f);
        var tabText = TextObject("Chat Tab Text", tab, "C\nH\nA\nT", 10, chatOpen ? BadgeInk : Ink, TextAnchor.MiddleCenter, monoFont);
        tabText.fontStyle = FontStyle.Bold;
        Stretch(tabText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (chatUnread && !chatOpen)
        {
            var dot = PanelObject("Chat Unread Dot", tab, (Color)RedAccent);
            dot.anchorMin = dot.anchorMax = new Vector2(0.82f, 0.92f);
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            dot.anchoredPosition = Vector2.zero;
            RoundCircle(dot);
        }
        var tabButton = tab.gameObject.AddComponent<Button>();
        tabButton.onClick.AddListener(() => { chatOpen = !chatOpen; if (chatOpen) chatUnread = false; Render(); });

        if (!chatOpen) return;

        var panel = PanelObject("Match Chat Panel", boardRoot, (Color)new Color32(14, 30, 46, 246));
        panel.anchorMin = new Vector2(0f, 0.5f);
        panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.sizeDelta = new Vector2(300f, 420f);
        panel.anchoredPosition = new Vector2(30f, 0f);
        RoundBig(panel);
        AddRoundedCardBorder(panel, MenuB, 1.2f);

        var title = TextObject("Match Chat Title", panel, "MATCH CHAT", 10, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(title.rectTransform, new Vector2(0.05f, 0.93f), new Vector2(0.95f, 0.99f), Vector2.zero, Vector2.zero);
        // Copy the whole conversation to the clipboard (the input itself already supports
        // native Ctrl+C/V paste; this covers copying the received messages/history).
        AddCopyChip(panel, "Copy", BuildChatText, new Vector2(0.70f, 0.925f), new Vector2(0.95f, 0.99f));

        // Scrollable message list (same viewport/ScrollRect pattern as the combat log).
        var viewport = PanelObject("Match Chat Viewport", panel, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("Match Chat Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 18f;
        AttachScrollbar(scroll);

        foreach (var m in chatMessages)
        {
            var line = TextObject("Chat Line", content, $"{m.Sender}: {m.Text}", 12,
                m.Mine ? Accent2 : Ink, TextAnchor.UpperLeft);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            line.verticalOverflow = VerticalWrapMode.Overflow;
            line.raycastTarget = false;
        }
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;

        // Input field (left) + Send (right).
        var fieldGo = new GameObject("Match Chat Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        var fieldRt = fieldGo.GetComponent<RectTransform>();
        fieldRt.SetParent(panel, false);
        Stretch(fieldRt, new Vector2(0.04f, 0.015f), new Vector2(0.74f, 0.105f), Vector2.zero, Vector2.zero);
        fieldGo.GetComponent<Image>().color = new Color32(20, 34, 50, 235);
        Round(fieldRt);
        AddRoundedCardBorder(fieldRt, MenuB, 1f);
        var field = fieldGo.GetComponent<InputField>();
        var ph = TextObject("Match Chat Placeholder", fieldRt, "Type message...", 11, Muted, TextAnchor.MiddleLeft);
        var txt = TextObject("Match Chat Field Text", fieldRt, "", 11, Ink, TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, new Vector2(0.06f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero);
        Stretch(txt.rectTransform, new Vector2(0.06f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero);
        field.textComponent = txt;
        field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        field.characterLimit = MatchNetworkSync.MaxChatLength;
        // Renders happen on every incoming command; keep the half-typed draft alive across them.
        field.text = chatDraft ?? "";
        field.onValueChanged.AddListener(v => chatDraft = v);
        field.onEndEdit.AddListener(v =>
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SendChat(v);
        });
        chatInputField = field;

        var sendGo = PanelObject("Match Chat Send", panel, Accent);
        Stretch(sendGo, new Vector2(0.77f, 0.015f), new Vector2(0.96f, 0.105f), Vector2.zero, Vector2.zero);
        Round(sendGo);
        var sendTxt = TextObject("Match Chat Send Text", sendGo, "Send", 11, BadgeInk, TextAnchor.MiddleCenter, titleFont);
        Stretch(sendTxt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var sendBtn = sendGo.gameObject.AddComponent<Button>();
        sendBtn.onClick.AddListener(() => { var t = field.text; field.text = ""; SendChat(t); });
    }

    private void DrawChatPanel()
    {
        var panel = PanelObject("Chat Panel", sideRoot, new Color(0, 0, 0, 0));
        Stretch(panel, new Vector2(0.06f, 0.15f), new Vector2(0.94f, 0.325f), Vector2.zero, Vector2.zero);

        var title = TextObject("Chat Title", panel, "CHAT", 9, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(title.rectTransform, new Vector2(0.0f, 0.94f), new Vector2(0.95f, 1.0f), Vector2.zero, Vector2.zero);

        // Bubble behind the message list - a small gap under the CHAT label (matching the combat-log
        // label spacing), filling down to the input.
        var chatBubble = PanelObject("Chat Bubble", panel, LogBgDark);
        Stretch(chatBubble, new Vector2(0.0f, 0.22f), new Vector2(1.0f, 0.90f), Vector2.zero, Vector2.zero);
        RoundBig(chatBubble);
        AddRoundedCardBorder(chatBubble, MenuB, 1f);

        // Scrollable message list (same viewport/ScrollRect pattern as the combat log).
        var viewport = PanelObject("Chat Viewport", panel, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.05f, 0.25f), new Vector2(0.95f, 0.87f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("Chat Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 18f;
        AttachScrollbar(scroll);

        foreach (var m in chatMessages)
        {
            var row = new GameObject("Chat Msg").AddComponent<RectTransform>();
            row.SetParent(content, false);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 24f;

            var bubble = PanelObject("Bubble", row, m.Mine ? Accent : new Color(1f, 1f, 1f, 0.06f));
            bubble.anchorMin = new Vector2(m.Mine ? 0.16f : 0f, 0f);
            bubble.anchorMax = new Vector2(m.Mine ? 1f : 0.84f, 1f);
            bubble.offsetMin = Vector2.zero; bubble.offsetMax = Vector2.zero;
            Round(bubble);
            var bt = TextObject("Bubble Text", bubble, m.Text, 12, m.Mine ? BadgeInk : Ink,
                m.Mine ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            bt.horizontalOverflow = HorizontalWrapMode.Wrap;
            bt.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch(bt.rectTransform, new Vector2(0.06f, 0f), new Vector2(0.94f, 1f), Vector2.zero, Vector2.zero);
        }
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;

        // Input field (left) + Send (right) - explicit anchors so they stay compact (a layout group
        // here ignored sizing and ballooned the field).
        var fieldGo = new GameObject("Chat Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        var fieldRt = fieldGo.GetComponent<RectTransform>();
        fieldRt.SetParent(panel, false);
        Stretch(fieldRt, new Vector2(0.0f, 0.02f), new Vector2(0.69f, 0.17f), Vector2.zero, Vector2.zero);
        fieldGo.GetComponent<Image>().color = new Color32(20, 34, 50, 235);
        Round(fieldRt);
        AddRoundedCardBorder(fieldRt, MenuB, 1f);
        var field = fieldGo.GetComponent<InputField>();
        var ph = TextObject("Chat Placeholder", fieldRt, "Type message...", 11, Muted, TextAnchor.MiddleLeft);
        var txt = TextObject("Chat Field Text", fieldRt, "", 11, Ink, TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, new Vector2(0.06f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero);
        Stretch(txt.rectTransform, new Vector2(0.06f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero);
        field.textComponent = txt;
        field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        field.onEndEdit.AddListener(v => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SendChat(v); });

        var sendGo = PanelObject("Send Button", panel, Accent);
        Stretch(sendGo, new Vector2(0.72f, 0.02f), new Vector2(1.0f, 0.17f), Vector2.zero, Vector2.zero);
        Round(sendGo);
        var sendTxt = TextObject("Send Text", sendGo, "Send", 11, BadgeInk, TextAnchor.MiddleCenter, titleFont);
        Stretch(sendTxt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var sendBtn = sendGo.gameObject.AddComponent<Button>();
        sendBtn.onClick.AddListener(() => { var t = field.text; field.text = ""; SendChat(t); });
    }

    private void AddEndTurnPanel()
    {
        var enabled = CanEndTurn() && (aiSeat == null || state.ActiveSeat != aiSeat);
        var root = PanelObject("Large End Turn", sideRoot, enabled ? Accent : (Color)new Color32(40, 60, 72, 190));
        Stretch(root, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.085f), Vector2.zero, Vector2.zero);
        RoundBig(root);
        AddRoundedCardBorder(root, enabled ? Accent2 : (Color)new Color32(70, 90, 104, 150), 2.2f);

        var text = TextObject("End Turn Text", root, "END TURN", 24, enabled ? BadgeInk : new Color32(150, 165, 180, 180), TextAnchor.MiddleCenter, titleFont);
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var button = root.gameObject.AddComponent<Button>();
        button.interactable = enabled;
        button.onClick.AddListener(() => Dispatch(new GameCommand { Type = "endTurn", Seat = state.ActiveSeat }));
    }

    private void DrawContextActions(RectTransform panel)
    {
        // Counter step gets a FIXED footer (outside the scroll) holding Resolve Attack, so a big hand
        // of counter cards doesn't force scrolling down to resolve. Reserve space at the bottom. Hidden
        // while a counter EVENT's effect is resolving (a pending effect/choice/look pivots the panel).
        bool counterFooter = CounterIsMine(state.Battle)
            && state.DeckLook == null && state.PendingEffects.Count == 0 && state.ActiveChoice == null;
        const float counterFooterH = 42f;

        var viewport = PanelObject("Action Viewport", panel, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f),
            new Vector2(0f, counterFooter ? counterFooterH + 4f : 0f), Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        var body = new GameObject("Action Content").AddComponent<RectTransform>();
        body.SetParent(viewport, false);
        Stretch(body, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        body.pivot = new Vector2(0.5f, 1f);
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4;
        layout.padding = new RectOffset(0, 8, 0, 0);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = body;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;
        AttachScrollbar(scroll);

        // Fixed Resolve-Attack footer for the counter step (outside the scrolling card grid). It passes
        // counters and resolves in one step, so the separate "damage" window never appears.
        if (counterFooter)
        {
            var footer = PanelObject("Counter Footer", panel, new Color(0, 0, 0, 0));
            Stretch(footer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-8f, counterFooterH));
            AddButton(footer, "Resolve Attack", ResolveCounterStep, true, false);
        }

        if (state.DeckLook != null)
        {
            selectedId = null;
            selectedSeat = null;
            var dl = state.DeckLook;
            var selecting = dl.Step == "select";
            // Buttons only for the seat doing the look/search; the other client just waits.
            if (isNetworked && dl.Seat != localSeat)
            {
                AddInfo(body, $"{dl.SourceName}: opponent is looking at cards — waiting on opponent...");
                return;
            }
            if (dl.Step == "scry")
            {
                AddInfo(body, $"{dl.SourceName}: click cards to keep on top of the deck; the rest go to the bottom.");
                AddButton(body, "Confirm Placement", () => Dispatch(new GameCommand { Type = "deckLookScryConfirm", Seat = dl.Seat, OrderedInstanceIds = new List<string>(deckLookScryTopIds ?? new List<string>()) }));
            }
            else if (dl.SearchMode)
            {
                string costHint = dl.MaxCost >= 0 ? $" (cost ≤ {dl.MaxCost})" : "";
                AddInfo(body, $"{dl.SourceName}: search the deck — click a card to add to hand{costHint}.");
                AddButton(body, "Take None / Shuffle", () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
            }
            else if (dl.FromTrash && selecting)
            {
                // Trash-play (OP13-082 Five Elders, Sengoku, …): pick up to N Characters from the trash to
                // play; green = a valid pick (a name already played greys out under "different card names").
                string feat = string.IsNullOrEmpty(dl.FeatureFilter) ? "" : $"{{{dl.FeatureFilter}}} ";
                string names = dl.DifferentNames ? " (different names)" : "";
                AddInfo(body, $"{dl.SourceName}: play up to {dl.SelectCount} {feat}Character(s) from your trash{names} — click a highlighted card, or take none.");
                AddButton(body, "Take None / Done", () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
            }
            else
            {
                AddInfo(body, selecting
                    ? $"{dl.SourceName}: choose up to 1 {{{dl.FeatureFilter}}} card to add to your hand."
                    : "Drag to set the order these return to the bottom of the deck, then confirm.");
                if (selecting)
                    AddButton(body, "Take None", () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
                else
                    AddButton(body, deckLookAnimating ? "Placing..." : "Confirm Order", ConfirmDeckLookOrder, !deckLookAnimating);
            }
            return;
        }

        // 6th-character replacement: an effect wants to play a Character but the board is full, so the
        // player picks which of their own Characters to play OVER — or skips. The board itself is the
        // affordance (own Characters glow green); a single Skip declines, returning the card to its source.
        if (state.PendingCharReplace != null)
        {
            selectedId = null;
            selectedSeat = null;
            var cr = state.PendingCharReplace;
            bool notMine = (aiSeat != null && cr.Seat == aiSeat) || (isNetworked && cr.Seat != localSeat);
            string heldName = cr.Held != null ? (GameEngine.GetCard(cr.Held)?.Name ?? "a Character") : "a Character";
            if (notMine)
            {
                AddInfo(body, $"◦  {cr.SourceName}: opponent is choosing which Character to play over…");
                return;
            }
            AddInfo(body, $"{cr.SourceName}: your board is full — click one of your Characters to play {heldName} over it (that Character is trashed), or skip.");
            AddButton(body, "Skip", () => Dispatch(new GameCommand { Type = "charReplace", Seat = cr.Seat, Target = "" }), true);
            return;
        }

        // "Choose one" modal — a card effect is waiting for the player to pick option A or B.
        if (state.ActiveChoice != null)
        {
            selectedId = null;
            selectedSeat = null;
            DrawChoiceActions(body);
            return;
        }

        // Pending effects (e.g. a [When Attacking] buff queued mid-battle) must resolve before
        // battle actions are shown, since the battle UI would otherwise hide the only way to
        // pick a target for them.
        if (state.PendingEffects.Count > 0)
        {
            selectedId = null;
            selectedSeat = null;
            DrawPendingEffectActions(body);
            return;
        }

        if (state.Battle != null)
        {
            selectedId = null;
            selectedSeat = null;
            DrawBattleActions(body);
            return;
        }

        // ---- Trash viewer --------------------------------------------------------
        // The cards themselves render in the trash popup (DrawTrashOverlay) over the play
        // area; the side panel just offers the close action.
        if (trashViewSeat != null)
        {
            state.Players.TryGetValue(trashViewSeat, out var tvp);
            int tvCount = tvp?.Trash.Count ?? 0;
            AddInfo(body, $"Viewing {trashViewSeat} trash ({tvCount} cards). Hover a card for a preview.");
            AddButton(body, "Close Trash View", () => { trashViewSeat = null; Render(); });
            return;
        }

        // ---- DON selected -------------------------------------------------------
        if (selectedDonIds.Count > 0 && selectedDonSeat == state.ActiveSeat)
        {
            AddInfo(body, $"{selectedDonIds.Count} DON!! selected. Click or drag onto your leader or a character.");
            AddButton(body, "Cancel DON", () => ClearDonSelection());
            return;
        }

        // ---- Hand card selected --------------------------------------------------
        if (!string.IsNullOrEmpty(selectedId) && selectedSeat != null && selectedSeat.EndsWith("-hand")
            && (!isNetworked || selectedSeat == localSeat + "-hand"))
        {
            var hSeat = selectedSeat.Replace("-hand", "");
            state.Players.TryGetValue(hSeat, out var hp2);
            var hCard = hp2?.Hand.Find(c => c.InstanceId == selectedId);
            if (hCard != null)
            {
                var hDef = GameEngine.GetCard(hCard);
                string powerPart  = hDef.Power   > 0 ? $"  Power {hDef.Power}"    : "";
                string counterPart = hDef.Counter > 0 ? $"  Counter +{hDef.Counter}" : "";
                AddInfo(body, $"Hand: {hDef.Name}  [{hDef.Type}  Cost {hDef.Cost}{powerPart}{counterPart}]");
                if (!string.IsNullOrEmpty(hDef.Effect))  AddInfo(body, hDef.Effect);
                if (!string.IsNullOrEmpty(hDef.Trigger)) AddInfo(body, $"[Trigger] {hDef.Trigger}");

                int freeDon2 = hp2 != null ? GameEngine.ActiveDonCount(hp2) : 0;
                int effCost = GameEngine.GetCost(state, hCard);   // effective cost (printed + CostDelta modifiers)
                bool canAfford = hp2 != null && freeDon2 >= effCost;
                string costHint = canAfford ? "" : $"  (need {effCost} DON!!, have {freeDon2})";

                bool counterOnly = hDef.Type == "event" && hDef.Power == 0 && hDef.Counter > 0
                    && string.IsNullOrEmpty(hDef.Effect);
                if (counterOnly)
                    AddInfo(body, "Counter card — play only during an opponent's attack at the counter step.");
                else
                {
                    var burnCard = hCard;
                    bool burn = hDef.Type == "event";
                    AddButton(body, $"Play{costHint}",
                        () =>
                        {
                            if (burn) StartEventBurn(burnCard);
                            Dispatch(new GameCommand { Type = "playCard", Seat = hSeat, InstanceId = selectedId });
                        },
                        canAfford);
                }

                AddButton(body, "Deselect", () => { selectedId = null; selectedSeat = null; Render(); });
                return;
            }
            selectedId = null;
            selectedSeat = null;
        }

        // ---- Board card selected -------------------------------------------------
        if (!string.IsNullOrEmpty(selectedId) && !string.IsNullOrEmpty(selectedSeat) && selectedSeat == state.ActiveSeat
            && (!isNetworked || selectedSeat == localSeat))
        {
            var selected = FindAny(selectedSeat, selectedId);
            if (selected != null)
            {
                var selDef = GameEngine.GetCard(selected);
                int livePow = GameEngine.GetPower(state, selected);
                AddInfo(body, $"Selected: {selDef.Name}  [Power {livePow}]");

                var badges = new System.Text.StringBuilder();
                if (GameEngine.HasRush(state, selected))                        badges.Append("[Rush] ");
                if (GameEngine.HasDoubleAttack(state, selected))                badges.Append("[Double Attack] ");
                if (GameEngine.HasModifier(state, selected, "cannotAttack"))    badges.Append("[Cannot Attack] ");
                if (GameEngine.HasModifier(state, selected, "cannotBeKod"))     badges.Append("[Cannot Be K.O.'d] ");
                if (GameEngine.HasModifier(state, selected, "canAttackActive")) badges.Append("[Can Attack Active] ");
                if (GameEngine.HasModifier(state, selected, "freeze"))          badges.Append("[Frozen] ");
                if (badges.Length > 0) AddInfo(body, badges.ToString().Trim());

                if (selDef.Effect.Contains("[Activate: Main]"))
                {
                    bool abilUsed = state.Players[selectedSeat].AbilityUsedThisTurn.Contains(selected.InstanceId);
                    // Deciding whether to use the ability: show the glowing card preview + write the
                    // [Activate: Main] ability out, so it reads as a real pending decision (not a bare
                    // button), OPTCGSim-style.
                    AddEffectCardVisual(body, selected.CardId);
                    string mainClause = CleanEffectText(ExtractActivateMainClause(selDef.Effect));
                    if (!string.IsNullOrEmpty(mainClause)) AddScaledInfo(body, mainClause);
                    AddButton(body, abilUsed ? "Activate: Main (used)" : "Activate: Main",
                        () => Dispatch(new GameCommand { Type = "activateMain", Seat = selectedSeat, Target = selectedId }),
                        !abilUsed);
                }

                // DON!! are attached by DRAGGING them onto a card, not from the action window — the
                // "Attach 1 / Attach All DON!!" buttons were removed here per design.

                // No manual rest/un-rest: a character only becomes rested as a consequence of a card
                // effect (or its own attack), never by the player freely toggling it. Removed the
                // debug-era "Rest / Set Active" button here.

                bool hasRush = GameEngine.HasRush(state, selected);
                if (hasRush && selected.PlayedOnTurn == state.TurnNumber)
                    AddInfo(body, "[Rush] — this card can attack the turn it was played.");

                bool canHitActive = GameEngine.HasModifier(state, selected, "canAttackActive");
                bool hasDblAtk = GameEngine.HasDoubleAttack(state, selected);
                int atkCount = state.AttackCountThisTurn.TryGetValue(selected.InstanceId, out int ac) ? ac : 0;
                if (hasDblAtk && atkCount == 1)
                    AddInfo(body, "Double Attack: can attack once more this turn.");
                else if (canHitActive)
                    AddInfo(body, "Click any opponent character (rested or active) or their leader to attack.");
                else
                    AddInfo(body, "Click an opponent leader or rested character to attack.");
                return;
            }
        }

        // ---- Default -------------------------------------------------------------
        AddInfo(body, "Click one of your board cards to select it, or play cards from your hand. Click a trash pile to browse it.");
    }

    private void DrawPendingEffectActions(RectTransform body)
    {
        var effect = state.PendingEffects[0];
        var source = CardData.GetCard(effect.SourceCardId);

        // Glowing pending card + timing header + the effect text written out — shown to BOTH
        // players, so the waiting side can read exactly what decision is being made (and, once
        // task B lands, watch the green progress fill). The card is the anchor; the panel just
        // explains it, OPTCGSim-style.
        AddEffectCardVisual(body, effect.SourceCardId);
        AddInfo(body, source.Name + " — " + EffectTimingLabel(effect.Timing));
        AddEffectProgressText(body, effect);

        // Not my decision — the opponent's (networked) or the bot's (solo vs AI). Passive pending
        // state, no action bubbles; the deciding side resolves it (AiTick drives the bot).
        bool notMyDecision = (aiSeat != null && effect.Seat == aiSeat) || (isNetworked && effect.Seat != localSeat);
        if (notMyDecision)
        {
            AddInfo(body, "◦  Pending opponent's decision…");
            return;
        }

        // Multi-pick progress ("choose up to N").
        // Show the remaining-pick count whenever a multi-select still has picks left (was `> 1`,
        // which made the hint vanish at the LAST pick instead of showing "1 more").
        if (effect.SelectionsRemaining > 0)
            AddInfo(body, $"Choose up to {effect.SelectionsRemaining} more, or Skip to stop early.");

        // Does the CURRENT clause need a board/hand/trash pick? If so, the board itself is the
        // affordance (OPTCGSim-style): a short prompt + a single Skip, and clicking a highlighted
        // target resolves the step — no separate wall-of-text "Resolve" bubble. If not, it's a
        // plain use/skip decision.
        bool donGive = DonGivePickActive(effect.Seat);
        if (EffectHasValidTarget(effect) || donGive)
        {
            AddInfo(body, donGive ? "Click a rested DON!! to give to your Leader." : EffectTargetPrompt(effect));
            if (effect.Optional)
                AddButton(body, "Skip", () => Dispatch(new GameCommand { Type = "passEffect", Seat = effect.Seat, EffectId = effect.EffectId }), true);
        }
        else
        {
            AddButton(body, "Use Effect", () => Dispatch(new GameCommand { Type = "resolveEffect", Seat = effect.Seat, EffectId = effect.EffectId }));
            AddButton(body, "Skip", () => Dispatch(new GameCommand { Type = "passEffect", Seat = effect.Seat, EffectId = effect.EffectId }), effect.Optional);
        }
    }

    // True if any card in play/hand/trash is a legal target for the effect's CURRENT clause.
    // Drives whether the pending-effect panel shows a board prompt (pick on the board) or a
    // simple Use/Skip decision. IsValidEffectTarget already enforces zone/ownership/clause
    // rules, so scanning a superset of zones is safe (invalid cards return false).
    private bool EffectHasValidTarget(PendingEffect effect)
    {
        if (state?.Players == null || effect == null) return false;
        foreach (var kv in state.Players)
        {
            var p = kv.Value;
            if (p == null) continue;
            if (p.Leader != null && GameEngine.IsValidEffectTarget(state, effect, p.Leader)) return true;
            if (p.CharacterArea != null)
                foreach (var c in p.CharacterArea)
                    if (c != null && GameEngine.IsValidEffectTarget(state, effect, c)) return true;
            if (p.Hand != null)
                foreach (var c in p.Hand)
                    if (c != null && GameEngine.IsValidEffectTarget(state, effect, c)) return true;
            if (p.Trash != null)
                foreach (var c in p.Trash)
                    if (c != null && GameEngine.IsValidEffectTarget(state, effect, c)) return true;
            if (p.Life != null)
                foreach (var c in p.Life)
                    if (c != null && GameEngine.IsValidEffectTarget(state, effect, c)) return true;
        }
        return false;
    }

    // Short, board-anchored prompt for a targeting step (no verbatim card text).
    private string EffectTargetPrompt(PendingEffect effect)
    {
        if ((effect.Text ?? "").IndexOf("top or bottom of your Life", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "Click the top or bottom of your Life pile.";
        switch (effect.TargetZone)
        {
            case OnePieceTcg.Engine.EffectTargetZone.Hand:  return "Select a card in your hand.";
            case OnePieceTcg.Engine.EffectTargetZone.Trash: return "Select a card in your trash.";
            case OnePieceTcg.Engine.EffectTargetZone.Any:   return "Select a highlighted target on the board or in your hand.";
            default:                                        return "Select a highlighted target on the board.";
        }
    }

    // Button label for the resolve button: the card's effect text VERBATIM (players
    // know how to read card text — paraphrasing loses context). Only the leading
    // timing tags (already shown in the panel header) and the standard DON!!-return
    // reminder parenthetical are stripped, and whitespace is collapsed.
    private static string ResolveEffectLabel(PendingEffect effect)
    {
        string text = effect.Text ?? "";
        // Strip leading timing tags like [Activate: Main] / [On Play]/[On K.O.].
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]*\]\s*/?\s*)+", "").Trim();
        // Strip the boilerplate DON!!-return reminder text.
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"\(You may return the specified number of DON!! cards from your field to your DON!! deck\.?\)", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(text) ? "Resolve effect" : UpperFirst(text);
    }

    private static string UpperFirst(string s)
    {
        return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    private static string NormalizeSign(string value)
    {
        return value.Replace(" ", "").Replace('−', '-').Replace('–', '-');
    }

    private void DrawChoiceActions(RectTransform body)
    {
        var ch = state.ActiveChoice;
        var source = CardData.GetCard(ch.SourceCardId);
        AddEffectCardVisual(body, ch.SourceCardId);
        // Buttons only for the seat whose choice this is; the other client just waits.
        if (isNetworked && ch.Seat != localSeat)
        {
            AddInfo(body, source.Name + " — opponent is choosing...");
            return;
        }
        AddInfo(body, source.Name + " — Choose One:");
        AddInfo(body, $"A: {ch.OptionA}");
        AddInfo(body, $"B: {ch.OptionB}");
        AddButton(body, "Choose A", () => Dispatch(new GameCommand { Type = "resolveChoice", Seat = ch.Seat, Target = "A" }));
        AddButton(body, "Choose B", () => Dispatch(new GameCommand { Type = "resolveChoice", Seat = ch.Seat, Target = "B" }));
    }

    private static string EffectTimingLabel(string timing)
    {
        switch (timing)
        {
            case "onPlay":              return "On Play";
            case "trigger":             return "Trigger";
            case "main":                return "Main";
            case "whenAttacking":       return "When Attacking";
            case "activateMain":        return "Activate: Main";
            case "onKo":                return "On KO";
            case "endOfYourTurn":       return "End of Your Turn";
            case "endOfOpponentsTurn":  return "End of Opponent's Turn";
            case "startOfYourTurn":     return "Start of Your Turn";
            case "counter":             return "Counter";
            case "onBlock":             return "On Block";
            case "onOpponentsAttack":   return "On Your Opponent's Attack";
            case "reactive":            return "Triggered Effect";
            default: return string.IsNullOrEmpty(timing) ? "Effect" : timing;
        }
    }

    private void DrawBattleActions(RectTransform body)
    {
        var b = state.Battle;
        AddInfo(body, StepLabel(b.Step));

        // Defender priority: every post-declaration decision (block, counter, final resolve,
        // trigger) belongs to the DEFENDER (BattleState.PrioritySeat — always TargetSeat; the
        // engine ignores resolveAttack from the attacker). In a networked match the attacker's
        // client gets a passive status line and NO buttons; hotseat shows the buttons since
        // both seats are local.
        string prioritySeat = string.IsNullOrEmpty(b.PrioritySeat) ? b.TargetSeat : b.PrioritySeat;
        bool myDecision = !isNetworked || prioritySeat == localSeat;
        if (!myDecision)
        {
            if (b.Step == "trigger")
                AddInfo(body, "Opponent is checking their Life card for a Trigger — waiting on opponent...");
            else
                AddInfo(body, "Waiting on opponent...");
            return;
        }

        if (b.Step == "block")
        {
            AddInfo(body, "Click an active blocker on the defending board, or pass blockers.");
            AddButton(body, "Pass Blockers", () => Dispatch(new GameCommand { Type = "passBlock", Seat = b.TargetSeat }));
        }
        else if (b.Step == "counter")
        {
            DrawCounterStep(body, b);
        }
        else if (b.Step == "damage")
        {
            // Resolve Attack is folded into the counter window's footer now, so this step never gets its
            // own window: auto-resolve it (deferred, once per battle, to avoid re-entrant dispatch during
            // Render). The AI resolves its own damage via AiTick; the attacker's client just waits.
            bool mineToResolve = (aiSeat == null || b.TargetSeat != aiSeat) && (!isNetworked || b.TargetSeat == localSeat);
            if (mineToResolve)
            {
                AddInfo(body, "Resolving…");
                if (autoResolvedDamageBattleId != b.Id)
                {
                    autoResolvedDamageBattleId = b.Id;
                    Invoke(nameof(AutoResolveDamage), 0f);
                }
            }
            else AddInfo(body, "Waiting on opponent…");
        }
        else if (b.Step == "trigger")
        {
            // Only the DEFENDER sees the revealed life card and the activate/pass choice;
            // the attacker's client returned above with a passive note. They get the card
            // preview only AFTER the defender activates it (see OnNetworkCommandReceived).
            if (aiSeat != null && b.TargetSeat == aiSeat)
            {
                AddInfo(body, "Basic Bot is checking its Life card for a Trigger...");
                return;
            }
            var revealed = b.RevealedLife != null ? GameEngine.GetCard(b.RevealedLife) : null;
            // The deciding player sees the actual card glowing (same treatment as pending
            // effects) with its [Trigger] text — not just bare buttons.
            if (b.RevealedLife != null) AddEffectCardVisual(body, b.RevealedLife.CardId);
            AddInfo(body, "Revealed: " + (revealed != null ? revealed.Name : "?"));
            if (revealed != null && !string.IsNullOrWhiteSpace(revealed.Trigger))
            {
                AddInfo(body, "[Trigger] pending: " + revealed.Trigger);
                // A "[Trigger] Activate this card's [Main]/[Counter]/… effect" just references another clause
                // on the same card — spell that clause out so the player knows what the trigger actually does.
                var selfAct = System.Text.RegularExpressions.Regex.Match(revealed.Trigger,
                    @"Activate this card's \[(Main|Counter|On Play|On K\.O\.)\] effect",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (selfAct.Success && !string.IsNullOrWhiteSpace(revealed.Effect))
                {
                    string tag = "[" + selfAct.Groups[1].Value + "]";
                    foreach (var line in revealed.Effect.Split('\n'))
                        if (line.IndexOf(tag, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        { AddInfo(body, "  ↳ " + line.Trim()); break; }
                }
            }
            AddButton(body, "Resolve Trigger", () => Dispatch(new GameCommand { Type = "useTrigger", Seat = b.TargetSeat }));
            AddButton(body, "Pass Trigger", () => Dispatch(new GameCommand { Type = "passTrigger", Seat = b.TargetSeat }));
        }
    }

    // Guards the damage-step auto-resolve so it fires once per battle (see the "damage" case above).
    private string autoResolvedDamageBattleId;
    private void AutoResolveDamage()
    {
        var b = state?.Battle;
        if (b != null && b.Step == "damage")
            Dispatch(new GameCommand { Type = "resolveAttack", Seat = b.TargetSeat });
    }

    // True when the local human is the one deciding this counter step (so it's safe to show the hand
    // grid). Never reveal an AI defender's hand, and in a networked match only the priority seat.
    private bool CounterIsMine(BattleState b) =>
        b != null && b.Step == "counter"
        && (aiSeat == null || b.TargetSeat != aiSeat)
        && (!isNetworked || b.TargetSeat == localSeat);

    // The fixed-footer button: pass any remaining counters and resolve in one go, so the separate
    // "damage / Resolve Attack" window never appears.
    private void ResolveCounterStep()
    {
        var b = state?.Battle;
        if (b == null) return;
        string seat = b.TargetSeat;
        Dispatch(new GameCommand { Type = "passCounter", Seat = seat });
        if (state?.Battle != null && state.Battle.Step == "damage")
            Dispatch(new GameCommand { Type = "resolveAttack", Seat = state.Battle.TargetSeat });
    }

    // Counter step rendered right in the actions panel: a matchup line with mini card art + a compact
    // grid of the defender's hand (counter-value cards highlighted + clickable, the rest dimmed).
    // Hovering a tile floats the real card up in your hand and previews it. The Resolve Attack button
    // is a fixed footer (see DrawContextActions), not in this scroll.
    private void DrawCounterStep(RectTransform body, BattleState b)
    {
        if (!CounterIsMine(b))
        {
            AddInfo(body, aiSeat != null && b.TargetSeat == aiSeat ? "Opponent is countering…" : "Waiting on opponent…");
            return;
        }

        string attackerSeat = OtherSeatLocal(b.TargetSeat);
        var atkInst = FindAny(attackerSeat, b.AttackerId);
        var tgtInst = FindAny(b.TargetSeat, b.TargetId);
        var atkDef = atkInst != null ? GameEngine.GetCard(atkInst) : null;
        var tgtDef = tgtInst != null ? GameEngine.GetCard(tgtInst) : null;
        int atkPow = b.AttackPower;
        int tgtBase = tgtInst != null ? GameEngine.GetPower(state, tgtInst) : b.DefensePower;
        int total = tgtBase + b.CounterPower;   // defender's current effective power (grows as counters play)
        bool hits = atkPow >= total;
        bool leaderHit = tgtDef != null && tgtDef.Type == "leader";

        AddCounterMatchup(body, atkInst, atkPow, tgtInst, total);
        if (hits)
        {
            int need = (atkPow - total) / 1000 * 1000 + 1000;   // smallest 1000-step counter that exceeds the attack
            string consequence = leaderHit ? "you'd lose 1 Life" : $"{(tgtDef != null ? tgtDef.Name : "your character")} would be K.O.'d";
            AddCounterVerdict(body, false, $"Hits now — {consequence}. +{need} more counter to stop it.");
        }
        else AddCounterVerdict(body, true, "Safe — this attack is stopped.");

        DrawCounterHand(body, b);
    }

    // "attacker × defender" matchup: a mini card with its power below (no name). Hovering a mini shows
    // a big character preview just left of the panel.
    private void AddCounterMatchup(RectTransform body, CardInstance atk, int atkPow, CardInstance tgt, int tgtPow)
    {
        var row = PanelObject("Matchup", body, new Color(0, 0, 0, 0));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
        AddMatchupSide(row, atk, atkPow, new Color32(240, 150, 150, 255), 0.00f, 0.45f);
        var x = TextObject("x", row, "×", 16, Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(x.rectTransform, new Vector2(0.45f, 0f), new Vector2(0.55f, 1f), Vector2.zero, Vector2.zero);
        AddMatchupSide(row, tgt, tgtPow, new Color32(150, 215, 150, 255), 0.55f, 1.00f);
    }

    private void AddMatchupSide(RectTransform row, CardInstance ci, int pow, Color powCol, float xMin, float xMax)
    {
        var holder = PanelObject("side", row, new Color(0, 0, 0, 0));
        Stretch(holder, new Vector2(xMin, 0f), new Vector2(xMax, 1f), Vector2.zero, Vector2.zero);
        float ch = 52f, cw = ch * 0.714f;
        if (ci != null)
        {
            var mini = PanelObject("mini", holder, new Color(0, 0, 0, 0));
            mini.anchorMin = mini.anchorMax = new Vector2(0.5f, 1f);
            mini.pivot = new Vector2(0.5f, 1f);
            mini.sizeDelta = new Vector2(cw, ch);
            mini.anchoredPosition = new Vector2(0f, -2f);
            RoundedCardVisual("art", mini, GetCardSprite(ci.CardId), out _);
            AddEffectGlow(mini);   // the "in action" glow hugs the mini card
            mini.gameObject.AddComponent<CounterPreviewHover>().Init(this, ci);
        }
        var pt = TextObject("p", holder, pow.ToString(), 13, powCol, TextAnchor.UpperCenter, titleFont);
        pt.fontStyle = FontStyle.Bold;
        Stretch(pt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f), new Vector2(0f, 20f));
    }

    // Centered verdict line with a safe/danger icon.
    private void AddCounterVerdict(RectTransform body, bool safe, string text)
    {
        var row = PanelObject("Verdict", body, new Color(0, 0, 0, 0));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        Color col = safe ? (Color)new Color32(120, 235, 155, 255) : (Color)new Color32(240, 150, 150, 255);
        var t = TextObject("t", row, (safe ? "✓  " : "⚠  ") + text, 11, col, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(6f, 0f), new Vector2(-6f, 0f));
    }

    // Big character preview parked just LEFT of the actions/side panel, shown while hovering a matchup mini.
    internal void CounterPreviewEnter(CardInstance ci) { ShowCounterSidePreview(ci); }
    internal void CounterPreviewExit() { HideHandHoverPreview(); }
    private void ShowCounterSidePreview(CardInstance ci)
    {
        HideHandHoverPreview();
        if (ci == null || canvas == null) return;
        handHoverRoot = PanelObject("Counter Side Preview", canvas.transform, new Color(0, 0, 0, 0));
        handHoverRoot.anchorMin = handHoverRoot.anchorMax = new Vector2(0.82f, 0.5f);
        handHoverRoot.pivot = new Vector2(1f, 0.5f);
        handHoverRoot.sizeDelta = new Vector2(288f, 402f);   // same size as the normal hand hover preview
        handHoverRoot.anchoredPosition = new Vector2(-8f, 0f);
        var g = handHoverRoot.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false; g.interactable = false;
        // Green when this card is a valid counter/target, else the neutral gold preview glow (consistency).
        if (IsGreenTargetNow(ci)) AddUsableGlow(handHoverRoot); else AddMysticalCardOutline(handHoverRoot, true);
        RoundedCardVisual("art", handHoverRoot, GetCardSprite(ci.CardId), out var img);
        if (img != null) img.raycastTarget = false;
        handHoverRoot.SetAsLastSibling();
    }

    // Drive the real hand card's hover (lift + in-hand preview) from a counter tile in the actions panel.
    private CardHover FindHandCardHover(string instanceId)
    {
        if (instanceId == null || !handCardRects.TryGetValue(instanceId, out var rt) || rt == null) return null;
        return rt.GetComponentInChildren<CardHover>();
    }

    internal void CounterTileHoverEnter(CardInstance card)
    {
        if (card == null || isDraggingHandCard || isDraggingAttack) return;
        var ch = FindHandCardHover(card.InstanceId);
        if (ch != null) ch.OnPointerEnter(new PointerEventData(EventSystem.current));
    }

    internal void CounterTileHoverExit(CardInstance card)
    {
        if (card == null) return;
        var ch = FindHandCardHover(card.InstanceId);
        if (ch != null) ch.OnPointerExit(new PointerEventData(EventSystem.current));
    }

    // Grid of the defender's hand as small card tiles for the counter step.
    private void DrawCounterHand(RectTransform body, BattleState b)
    {
        var p = state.Players.ContainsKey(b.TargetSeat) ? state.Players[b.TargetSeat] : null;
        if (p == null || p.Hand == null || p.Hand.Count == 0)
        {
            AddInfo(body, "Your hand is empty — nothing to counter with.");
            return;
        }

        const int cols = 3;
        const float tileH = 92f, vSpacing = 8f, hSpacing = 6f;
        float tileW = tileH * 0.714f;
        int rows = (p.Hand.Count + cols - 1) / cols;

        var container = PanelObject("Counter Hand", body, new Color(0, 0, 0, 0));
        var le = container.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rows * tileH + (rows - 1) * vSpacing + 12f;

        var grid = container.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(tileW, tileH);
        grid.spacing = new Vector2(hSpacing, vSpacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.padding = new RectOffset(2, 2, 4, 4);

        foreach (var card in p.Hand)
        {
            var def = GameEngine.GetCard(card);
            // Usable = engine-validated for the counter step: a counter-value card, OR an affordable
            // [Counter] event (DON check inside). Unaffordable events / non-counters stay dimmed.
            bool usable = GameEngine.CanCounterFromHand(state, b.TargetSeat, card);
            // The "+X" power badge is only for counter-VALUE cards; counter EVENTS show green (usable)
            // but no flat +power indicator (their boost is an effect, not the corner value).
            bool showValueBadge = def != null && def.Type != "event" && def.Counter > 0;

            var cell = PanelObject("Counter Tile " + card.InstanceId, container, new Color(0, 0, 0, 0));
            RoundedCardVisual("art", cell, GetCardSprite(card.CardId), out var artImg);
            if (artImg != null) artImg.color = usable ? Color.white : new Color(1f, 1f, 1f, 0.42f);   // dim unusable

            if (usable) AddRoundedCardBorder(cell, new Color32(70, 220, 140, 255), 1.6f);
            if (showValueBadge)
            {
                var badge = PanelObject("badge", cell, new Color32(16, 60, 40, 235));
                badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0f);
                badge.pivot = new Vector2(0.5f, 0f);
                badge.sizeDelta = new Vector2(tileW - 6f, 16f);
                badge.anchoredPosition = new Vector2(0f, 4f);
                Round(badge);
                var bt = TextObject("t", badge, "+" + def.Counter, 9, new Color32(120, 245, 175, 255), TextAnchor.MiddleCenter, monoFont);
                bt.fontStyle = FontStyle.Bold;
                bt.raycastTarget = false;
                Stretch(bt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }

            cell.gameObject.AddComponent<PreviewOnHover>().Init(this, card);
            if (usable)
            {
                var cardRef = card;
                cell.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    CounterTileHoverExit(cardRef);
                    Dispatch(new GameCommand { Type = "counterWithCard", Seat = b.TargetSeat, InstanceId = cardRef.InstanceId });
                });
            }
        }
    }

    // ── Difficulty ladder: one strong core, degraded per tier ────────────────
    // All three tiers share Engine/Bot/IntermediateBot.cs (which carries every validated playtest win). This
    // sets that core's per-seat degradation knobs for the CURRENT difficulty, and MUST run before every AI
    // tick (Advanced included, since AdvancedContractBot also drives the IntermediateBot core internally) so a
    // Beginner match never leaks its sloppiness into a later Intermediate/Advanced match.
    //   Beginner  → revert the resource-DISCIPLINE wins (DON!! sequencing, hold-counter, no-over-counter,
    //               smart mulligan): it still pressures Life (the headline lesson) but wastes DON!! and
    //               over-counters, so it is clearly beatable without ever looking brain-dead.
    //   Intermediate / Advanced → all knobs null = full-strength core (Advanced adds search on top).
    // Static fields are safe here: one match at a time on Unity's main thread. Tune the gap by adding/removing
    // reverts (e.g. also set AltFloorSeat/AltFloor to partially undo Life-pressure for an even weaker Beginner).
    private void ApplyBotDifficultyKnobs()
    {
        string s = aiSeat != null && aiDifficulty == "beginner" ? aiSeat : null;
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyDonSeat = s;               // front-load DON!! (waste it)
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterVariantSeat = s;      // don't hold big counters
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackCharCounterSeat = s;  // over-counter Characters
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackLeaderCounterSeat = s;// over-counter the Leader
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyMulliganSeat = s;          // cruder mulligan
    }

    // ── Intermediate Bot decision core ───────────────────────────────────────
    // Thin adapter around Engine/Bot/IntermediateBot.cs: one decision per tick,
    // applied through this class's own Dispatch() so UI/animation/network sync
    // behave identically to a human or the Basic Bot. Shares the same early-exit
    // guards and aiTriedThisTurn blacklist as AiTick() below — IntermediateBot
    // exposes SnapshotFor/Succeeded/Signature specifically so this method doesn't
    // need to reimplement its "did that actually work" check (see the robustness
    // note at the top of IntermediateBot.cs: rejections still Log(), so a naive
    // EventLog-changed check isn't a reliable success signal).
    private bool IntermediateAiTick()
    {
        var p = state.Players.ContainsKey(aiSeat) ? state.Players[aiSeat] : null;
        if (p == null) return false;
        if (activeMoveGhosts > 0) return false;
        if (handDealAnimating) return false;
        if (mulliganDealAnimating > 0) return false;
        if (state.TurnNumber != aiLastTurnSeen)
        {
            aiLastTurnSeen = state.TurnNumber;
            aiTriedThisTurn.Clear();
            if (state.Status == "active" && state.ActiveSeat == aiSeat)
            {
                aiNextActionAt = Time.unscaledTime + 2.9f;
                return false;
            }
        }

        var cmd = IntermediateBot.DecideOneCommand(state, aiSeat, aiTriedThisTurn);
        if (cmd == null) return false;
        object before = IntermediateBot.SnapshotFor(state, cmd);
        Dispatch(cmd);
        if (!IntermediateBot.Succeeded(state, cmd, before))
            aiTriedThisTurn.Add(IntermediateBot.Signature(cmd));
        return true;
    }

    // ── Intermediate (Champion) Bot decision core ────────────────────────────
    // Same per-tick adapter as IntermediateAiTick, but drives Engine/Bot/ChampionBot.cs —
    // the style evolved by the out-of-ship Elo tournament (beats the plain IntermediateBot
    // ~54% head-to-head). ChampionBot returns the same command shapes, so IntermediateBot's
    // SnapshotFor/Succeeded/Signature still key the no-op blacklist correctly.
    private bool ChampionAiTick()
    {
        var p = state.Players.ContainsKey(aiSeat) ? state.Players[aiSeat] : null;
        if (p == null) return false;
        if (activeMoveGhosts > 0) return false;
        if (handDealAnimating) return false;
        if (mulliganDealAnimating > 0) return false;
        if (state.TurnNumber != aiLastTurnSeen)
        {
            aiLastTurnSeen = state.TurnNumber;
            aiTriedThisTurn.Clear();
            if (state.Status == "active" && state.ActiveSeat == aiSeat)
            {
                aiNextActionAt = Time.unscaledTime + 2.9f;
                return false;
            }
        }

        var cmd = ChampionBot.DecideOneCommand(state, aiSeat, aiTriedThisTurn);
        if (cmd == null) return false;
        object before = IntermediateBot.SnapshotFor(state, cmd);
        Dispatch(cmd);
        if (!IntermediateBot.Succeeded(state, cmd, before))
            aiTriedThisTurn.Add(IntermediateBot.Signature(cmd));
        return true;
    }

    // ── Advanced (Search) Bot decision core ──────────────────────────────────
    // Drives Engine/Bot/Search/SearchBot.cs — the every-legal-action rollout search bot (beats the
    // Champion ~87% head-to-head). Same per-tick adapter; SearchBot returns the same command shapes
    // so IntermediateBot's SnapshotFor/Succeeded/Signature key the no-op blacklist correctly.
    // NOTE: a single SearchBot decision runs several full playouts on cloned state (~hundreds of ms)
    // on this (main) thread. The tick loop pauses between actions so it fires at most once per tick;
    // if it visibly hitches the UI, move the SearchBot.DecideOneCommand call onto a worker thread and
    // Dispatch its result on the next tick.
    private bool AdvancedAiTick()
    {
        var p = state.Players.ContainsKey(aiSeat) ? state.Players[aiSeat] : null;
        if (p == null) return false;
        if (activeMoveGhosts > 0) return false;
        if (handDealAnimating) return false;
        if (mulliganDealAnimating > 0) return false;
        if (state.TurnNumber != aiLastTurnSeen)
        {
            aiLastTurnSeen = state.TurnNumber;
            aiTriedThisTurn.Clear();
            aiActivatedThisTurn.Clear();
            if (state.Status == "active" && state.ActiveSeat == aiSeat)
            {
                aiNextActionAt = Time.unscaledTime + 2.9f;
                return false;
            }
        }

        // Advanced tier = the full validated contract (contract-v2, ported from Tools/Sim): IntermediateBot
        // base, with the activation module on midrange main turns, the pressure module on control main turns,
        // SearchBot's rollout on tactical/resolution branches, and greedy everywhere else. aiArchetype gates it.
        var cmd = OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.Decide(
            state, aiSeat, aiTriedThisTurn, aiActivatedThisTurn, aiArchetype);
        if (cmd == null) return false;
        object before = IntermediateBot.SnapshotFor(state, cmd);
        Dispatch(cmd);
        if (!IntermediateBot.Succeeded(state, cmd, before))
            aiTriedThisTurn.Add(IntermediateBot.Signature(cmd));
        return true;
    }

    // ── Basic Bot decision core ──────────────────────────────────────────────
    // Heuristics modelled on Strawtable's greedy AI: mulligan on curveless hands,
    // play the biggest affordable character, dump spare DON onto the leader, attack
    // with everything (leader by default, profitable rested trades first), counter
    // only when it flips a battle worth saving, always use triggers, resolve effect
    // prompts with the first valid target, take choice option A.
    private bool AiTick()
    {
        var p = state.Players.ContainsKey(aiSeat) ? state.Players[aiSeat] : null;
        if (p == null) return false;
        // Never act while card-movement ghosts are still flying — acting mid-animation
        // forces re-renders that cut every animation short (the "sped up" feel).
        if (activeMoveGhosts > 0) return false;
        if (handDealAnimating) return false;
        if (mulliganDealAnimating > 0) return false;   // don't cut the player's opening-hand deal short
        if (state.TurnNumber != aiLastTurnSeen)
        {
            aiLastTurnSeen = state.TurnNumber;
            aiTriedThisTurn.Clear();
            if (state.Status == "active" && state.ActiveSeat == aiSeat)
            {
                // Let the "Opponent's Turn" banner play out (+~1s) before the first move.
                aiNextActionAt = Time.unscaledTime + 2.9f;
                return false;
            }
        }

        // 1. Mulligan: keep only hands with an early play (a character costing ≤3).
        if (state.Status == "mulligan" && !p.MulliganDecided)
        {
            bool hasEarly = p.Hand.Exists(c =>
            {
                var d = GameEngine.GetCard(c);
                return d.Type == "character" && d.Cost <= 3;
            });
            Dispatch(new GameCommand { Type = "mulliganDecision", Seat = aiSeat, Mulligan = !hasEarly });
            return true;
        }
        if (state.Status != "active") return false;

        // 2. "Choose one" prompts: take option A.
        if (state.ActiveChoice != null)
        {
            if (state.ActiveChoice.Seat != aiSeat) return false;
            Dispatch(new GameCommand { Type = "resolveChoice", Seat = aiSeat, Target = "A" });
            return true;
        }

        // 2b. 6th-character replace owned by the bot: play over its weakest Character (or skip if none).
        if (state.PendingCharReplace != null)
        {
            if (state.PendingCharReplace.Seat != aiSeat) return false;
            string victim = null; int worst = int.MaxValue;
            foreach (var c in p.CharacterArea)
            {
                if (c == null) continue;
                int pw = GameEngine.GetPower(state, c);
                if (pw < worst) { worst = pw; victim = c.InstanceId; }
            }
            Dispatch(new GameCommand { Type = "charReplace", Seat = aiSeat, Target = victim ?? "" });
            return true;
        }

        // 3. Deck-look / search: pick the first selectable card(s); confirm order as-is.
        if (state.DeckLook != null)
        {
            if (state.DeckLook.Seat != aiSeat) return false;
            var dl = state.DeckLook;
            if (dl.Step == "select")
            {
                var pick = dl.Cards.Find(IsDeckLookSelectable);
                if (pick != null)
                {
                    Dispatch(new GameCommand { Type = "deckLookSelect", Seat = aiSeat, Target = pick.InstanceId });
                    return true;
                }
                Dispatch(new GameCommand { Type = "deckLookSelect", Seat = aiSeat, Target = null });
                return true;
            }
            var order = new List<string>();
            foreach (var c in dl.Cards) order.Add(c.InstanceId);
            Dispatch(new GameCommand
            {
                Type = dl.Step == "scry" ? "deckLookScryConfirm" : "deckLookConfirmOrder",
                Seat = aiSeat,
                OrderedInstanceIds = order,
            });
            return true;
        }

        // 4. Pending effects owned by the bot: pay DON!! costs, then first valid target,
        //    then a bare resolve; skip when nothing is legal.
        if (state.PendingEffects.Count > 0)
        {
            var pe = state.PendingEffects[0];
            if (pe.Seat != aiSeat) return false;
            if (pe.DonPaymentRemaining > 0)
            {
                // Pay a DON!! −N by returning cost-area DON!! first (preserves board power), then falling
                // back to ATTACHED DON!! — both are valid on-field targets for the return.
                string donToReturn = p.CostArea.Count > 0 ? p.CostArea[0].InstanceId : FirstAttachedDonId(p);
                if (!string.IsNullOrEmpty(donToReturn))
                {
                    Dispatch(new GameCommand { Type = "resolveEffect", Seat = aiSeat, EffectId = pe.EffectId, Target = donToReturn });
                    return true;
                }
                Dispatch(new GameCommand { Type = "passEffect", Seat = aiSeat, EffectId = pe.EffectId });
                return true;
            }
            string tgt = AiFindEffectTarget(pe);
            string attemptKey = "fx:" + pe.EffectId + ":" + (tgt ?? "bare");
            if (!aiTriedThisTurn.Add(attemptKey))
            {
                Dispatch(new GameCommand { Type = "passEffect", Seat = aiSeat, EffectId = pe.EffectId });
                return true;
            }
            Dispatch(tgt != null
                ? new GameCommand { Type = "resolveEffect", Seat = aiSeat, EffectId = pe.EffectId, Target = tgt }
                : new GameCommand { Type = "resolveEffect", Seat = aiSeat, EffectId = pe.EffectId });
            return true;
        }

        // 5. Battle responses while defending.
        if (state.Battle != null)
        {
            var b = state.Battle;
            if (b.PrioritySeat != aiSeat) return false;
            var atkInst = FindAny(b.AttackerSeat, b.AttackerId);
            int atkPow = atkInst != null ? GameEngine.GetPower(state, atkInst) : b.AttackPower;
            if (b.Step == "block")
            {
                // Block only when the leader is hit at low life and a blocker exists.
                var target = FindAny(b.TargetSeat, b.TargetId);
                bool leaderHit = target != null && GameEngine.GetCard(target).Type == "leader";
                if (leaderHit && p.Life.Count <= 2)
                {
                    foreach (var c in p.CharacterArea)
                    {
                        if (c == null || c.Rested) continue;
                        var d = GameEngine.GetCard(c);
                        if (d.Keywords.Contains("Blocker") && aiTriedThisTurn.Add("blk:" + c.InstanceId))
                        {
                            Dispatch(new GameCommand { Type = "blockAttack", Seat = aiSeat, Blocker = c.InstanceId });
                            return true;
                        }
                    }
                }
                Dispatch(new GameCommand { Type = "passBlock", Seat = aiSeat });
                return true;
            }
            if (b.Step == "counter")
            {
                var target = FindAny(b.TargetSeat, b.TargetId);
                bool leaderHit = target != null && GameEngine.GetCard(target).Type == "leader";
                int defPow = (target != null ? GameEngine.GetPower(state, target) : b.DefensePower) + b.CounterPower;
                int deficit = atkPow - defPow;
                bool worthSaving = (leaderHit && p.Life.Count <= 3)
                    || (!leaderHit && target != null && GameEngine.GetPower(state, target) >= 6000);
                if (deficit >= 0 && worthSaving)
                {
                    // Cheapest sufficient counter first; keep expensive cards.
                    CardInstance best = null;
                    int bestVal = int.MaxValue;
                    foreach (var c in p.Hand)
                    {
                        var d = GameEngine.GetCard(c);
                        if (d.Counter <= 0) continue;
                        if (d.Counter < bestVal) { best = c; bestVal = d.Counter; }
                    }
                    if (best != null && aiTriedThisTurn.Add("ctr:" + best.InstanceId + ":" + b.Id))
                    {
                        Dispatch(new GameCommand { Type = "counterWithCard", Seat = aiSeat, InstanceId = best.InstanceId });
                        return true;
                    }
                }
                Dispatch(new GameCommand { Type = "passCounter", Seat = aiSeat });
                return true;
            }
            if (b.Step == "damage")
            {
                Dispatch(new GameCommand { Type = "resolveAttack", Seat = aiSeat });
                return true;
            }
            if (b.Step == "trigger")
            {
                Dispatch(new GameCommand { Type = "useTrigger", Seat = aiSeat });
                return true;
            }
            return false;
        }

        // 6. The bot's own turn. The engine already auto-refreshed, auto-drew and
        //    auto-added DON!! in ApplyStartOfTurn (the manual DRAW/DON buttons are hotseat
        //    conveniences — dispatching them would ILLEGALLY draw extra resources), so the
        //    bot only ever acts during its MAIN phase.
        if (state.ActiveSeat != aiSeat) return false;
        if (state.Phase == "main")
        {
            int activeDon = GameEngine.ActiveDonCount(p);

            // a. Play the biggest affordable character (into a free slot, or — on a full board — over the
            //    weakest existing character if the new body is bigger, per the 6th-character rule), then stage.
            CardInstance bestPlay = null;
            int bestCost = -1;
            int bestReplaceSlot = -1;   // >=0 => play over this occupied slot (full board)
            bool haveSlot = p.CharacterArea.Exists(c => c == null);
            int weakestSlot = -1, weakestPow = int.MaxValue;
            for (int s = 0; s < p.CharacterArea.Count; s++)
            {
                var occ = p.CharacterArea[s];
                if (occ == null) continue;
                int pw = GameEngine.GetPower(state, occ);
                if (pw < weakestPow) { weakestPow = pw; weakestSlot = s; }
            }
            foreach (var c in p.Hand)
            {
                if (aiTriedThisTurn.Contains("play:" + c.InstanceId)) continue;
                var d = GameEngine.GetCard(c);
                int cost = GameEngine.GetCost(state, c);
                if (cost > activeDon) continue;
                if (d.Type == "character")
                {
                    if (haveSlot)
                    {
                        if (cost > bestCost) { bestPlay = c; bestCost = cost; bestReplaceSlot = -1; }
                    }
                    else if (weakestSlot >= 0 && d.Power > weakestPow && cost > bestCost)
                    {
                        bestPlay = c; bestCost = cost; bestReplaceSlot = weakestSlot;
                    }
                }
                else if (d.Type == "stage" && p.Stage == null && cost > bestCost) { bestPlay = c; bestCost = cost; bestReplaceSlot = -1; }
            }
            if (bestPlay != null)
            {
                aiTriedThisTurn.Add("play:" + bestPlay.InstanceId);
                var playCmd = new GameCommand { Type = "playCard", Seat = aiSeat, InstanceId = bestPlay.InstanceId };
                if (bestReplaceSlot >= 0) playCmd.SlotIndex = bestReplaceSlot;
                Dispatch(playCmd);
                return true;
            }

            // b. Spare DON!! → leader BEFORE attacking (attacks then swing with the buff;
            //    holding DON back is only correct when saving for counter events, which the
            //    basic bot never plays). Only once plays are exhausted (the play branch
            //    above returns first while anything is affordable).
            if (activeDon > 0 && p.Leader != null && aiTriedThisTurn.Add("dump-don:" + state.TurnNumber + ":" + activeDon))
            {
                Dispatch(new GameCommand { Type = "attachDon", Seat = aiSeat, Target = p.Leader.InstanceId, Amount = activeDon });
                return true;
            }

            // c. Attacks: every ready attacker swings — a profitable rested trade if one
            //    exists, otherwise the enemy leader.
            var opp = state.Players[GameEngine.OtherSeat(aiSeat)];
            var attackers = new List<CardInstance>();
            if (p.Leader != null) attackers.Add(p.Leader);
            foreach (var c in p.CharacterArea) if (c != null) attackers.Add(c);
            bool aiCanBattle = p.TurnsStarted > 1;
            foreach (var atk in attackers)
            {
                if (!aiCanBattle) break;
                if (atk.Rested || aiTriedThisTurn.Contains("atk:" + atk.InstanceId)) continue;
                int myPow = GameEngine.GetPower(state, atk);
                // A [When Attacking] effect makes even a losing swing worthwhile (the
                // effect still fires); otherwise only attack targets we can actually beat.
                var atkDef = GameEngine.GetCard(atk);
                bool hasWhenAttacking = (atkDef.Effect ?? "").IndexOf("[When Attacking]", System.StringComparison.OrdinalIgnoreCase) >= 0;
                string targetId = null;
                foreach (var oc in opp.CharacterArea)
                {
                    if (oc == null || !oc.Rested) continue;
                    if (GameEngine.GetPower(state, oc) <= myPow && GameEngine.GetCard(oc).Cost >= 3) { targetId = oc.InstanceId; break; }
                }
                if (targetId == null && opp.Leader != null)
                {
                    int leaderPow = GameEngine.GetPower(state, opp.Leader);
                    if (myPow >= leaderPow || hasWhenAttacking) targetId = opp.Leader.InstanceId;
                }
                if (targetId == null) continue;
                aiTriedThisTurn.Add("atk:" + atk.InstanceId);
                Dispatch(new GameCommand { Type = "declareAttack", Seat = aiSeat, Attacker = atk.InstanceId, Target = targetId });
                return true;
            }

            // d. Nothing left — end the turn.
            if (aiTriedThisTurn.Add("end:" + state.TurnNumber))
            {
                Dispatch(new GameCommand { Type = "endTurn", Seat = aiSeat });
                return true;
            }
        }
        return false;
    }

    // First valid target for a pending effect, scanning board (both sides), the bot's
    // hand + trash, then the opponent's hand (blind picks) — mirrors IsValidEffectTarget.
    private string AiFindEffectTarget(OnePieceTcg.Engine.PendingEffect pe)
    {
        var candidates = new List<CardInstance>();
        foreach (var seatKey in state.Players.Keys)
        {
            var pl = state.Players[seatKey];
            if (pl.Leader != null) candidates.Add(pl.Leader);
            foreach (var c in pl.CharacterArea) if (c != null) candidates.Add(c);
            if (pl.Stage != null) candidates.Add(pl.Stage);
        }
        var self = state.Players[aiSeat];
        candidates.AddRange(self.Hand);
        candidates.AddRange(self.Trash);
        var oppP = state.Players[GameEngine.OtherSeat(aiSeat)];
        candidates.AddRange(oppP.Hand);
        candidates.AddRange(oppP.Trash);

        // Rank valid targets instead of grabbing the first legal one by slot order (which happily K.O.'d a
        // 1-cost body over the 4-cost Blocker that was actually stopping lethal). A removal/debuff wants the
        // opponent's BIGGEST threat — Blockers first, then cost+power; a buff wants your own biggest body.
        string oppSeat = GameEngine.OtherSeat(aiSeat);
        bool negative = AiEffectIsNegative(pe.Text);
        CardInstance best = null;
        double bestScore = double.NegativeInfinity;
        foreach (var c in candidates)
        {
            if (!GameEngine.IsValidEffectTarget(state, pe, c)) continue;
            bool opp = c.Owner == oppSeat;
            double val = GameEngine.GetCost(state, c) * 1000.0 + GameEngine.GetPower(state, c);
            if (opp && GameEngine.HasBlocker(state, c)) val += 4000.0;   // walls are the priority removal
            // Want the biggest OPPONENT body for a removal; the biggest OWN body for a buff. An
            // ownership mismatch (e.g. a removal that can only hit your own board) is worst.
            double score = negative
                ? (opp ? val : -val - 1e6)
                : (opp ? -val - 1e6 : val);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best?.InstanceId;
    }

    // Cheap "is this a removal/debuff" test for the basic bot's target ranking (KO/trash/rest/bounce/−stat).
    private static bool AiEffectIsNegative(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string t = text.ToLowerInvariant();
        return t.Contains("k.o.") || t.Contains("trash") || t.Contains("rest ") || t.Contains("cannot")
            || t.Contains("return") || t.Contains("discard")
            || System.Text.RegularExpressions.Regex.IsMatch(t, @"[-−]\d");
    }

    private void OnCardClick(CardInstance card, string seat)
    {
        // Lock the docked left card-preview onto whatever card was just clicked.
        if (card != null) previewLockCard = card;
        // Sandbox edit mode: a click SELECTS the card for the editor's ops section rather than
        // driving normal play (attach DON / declare attack / target an effect).
        if (isSandbox && sandboxEditMode && card != null)
        {
            sandboxSelectedCard = card;
            Render();
            return;
        }
        if (state.DeckLook != null)
        {
            // "select" is click-driven; "scry" clicks toggle a card in/out of the TOP set;
            // "rearrange" is drag-to-reorder plus an explicit Confirm button, not handled here.
            if (state.DeckLook.Step == "select")
                SelectDeckLookCard(card.InstanceId);
            else if (state.DeckLook.Step == "scry")
            {
                if (deckLookScryTopIds == null) deckLookScryTopIds = new List<string>();
                if (!deckLookScryTopIds.Remove(card.InstanceId)) deckLookScryTopIds.Add(card.InstanceId);
                Render();
            }
            return;
        }

        // 6th-character replace: clicking one of my own board Characters plays the held card over it.
        if (state.PendingCharReplace != null && card != null && IsCharReplaceTarget(card))
        {
            Dispatch(new GameCommand { Type = "charReplace", Seat = state.PendingCharReplace.Seat, Target = card.InstanceId });
            return;
        }

        if (selectedDonIds.Count > 0)
        {
            AttachSelectedDonTo(seat, card);
            return;
        }

        // Clicking the trash pile's top card toggles the trash viewer, same as clicking
        // the pile zone itself. (Overlay cards pass seat == null, so they don't re-toggle;
        // and while an effect is picking from the trash, clicks keep routing to targeting.)
        if (card != null && card.Zone == "trash" && seat != null)
        {
            bool trashTargeting = state.PendingEffects.Count > 0
                && state.PendingEffects[0].TargetZone == OnePieceTcg.Engine.EffectTargetZone.Trash;
            if (!trashTargeting)
            {
                trashViewSeat = trashViewSeat == card.Owner ? null : card.Owner;
                Render();
                return;
            }
        }

        if (seat != null && seat.EndsWith("-hand"))
        {
            var handSeat = seat.Replace("-hand", "");

            // Pending effects that want a HAND target take priority even mid-battle —
            // reactions like Nami's [On Your Opponent's Attack] "trash 1 card: …" queue
            // during the block step, and their cost is paid by clicking a hand card.
            if (state.PendingEffects.Count > 0)
            {
                var peBattle = state.PendingEffects[0];
                if ((peBattle.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Hand ||
                     peBattle.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Any)
                    && peBattle.Seat == handSeat)
                {
                    Dispatch(new GameCommand
                    {
                        Type = "resolveEffect",
                        Seat = peBattle.Seat,
                        EffectId = peBattle.EffectId,
                        Target = card.InstanceId,
                    });
                    return;
                }
            }

            if (state.Battle != null)
            {
                if (state.Battle.Step == "counter" && state.Battle.TargetSeat == handSeat)
                    Dispatch(new GameCommand { Type = "counterWithCard", Seat = handSeat, InstanceId = card.InstanceId });
                else
                    Render();
                return;
            }

            // If a pending effect wants the player to choose a hand card as its target
            // (e.g. Leader Kid discarding, Straw Sword trigger playing from hand),
            // route the click to resolveEffect instead of the normal play-card flow.
            if (state.PendingEffects.Count > 0)
            {
                var pe = state.PendingEffects[0];
                if (pe.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Hand ||
                    pe.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Any)
                {
                    Dispatch(new GameCommand
                    {
                        Type = "resolveEffect",
                        Seat = pe.Seat,
                        EffectId = pe.EffectId,
                        Target = card.InstanceId,
                    });
                }
                else
                {
                    Render();
                }
                return;
            }

            if (state.ActiveSeat == handSeat)
            {
                var def = GameEngine.GetCard(card);
                if (state.Phase == "main" && (def.Type == "character" || def.Type == "stage" || def.Type == "event"))
                {
                    // Select the hand card so the side panel can show a Play button.
                    selectedId = card.InstanceId;
                    selectedSeat = handSeat + "-hand";
                    trashViewSeat = null;
                    Render();
                    return;
                }
                Dispatch(new GameCommand { Type = "playCard", Seat = handSeat, InstanceId = card.InstanceId });
            }
            else
            {
                Render();
            }
            return;
        }

        if (state.PendingEffects.Count > 0)
        {
            var effect = state.PendingEffects[0];
            Dispatch(new GameCommand { Type = "resolveEffect", Seat = effect.Seat, EffectId = effect.EffectId, Target = card.InstanceId });
            return;
        }

        if (state.Battle != null)
        {
            if (state.Battle.Step == "block" && state.Battle.TargetSeat == seat)
                Dispatch(new GameCommand { Type = "blockAttack", Seat = seat, Blocker = card.InstanceId });
            else
                Render();
            return;
        }

        if (seat == state.ActiveSeat)
        {
            // Full-board replacement: with a hand Character selected and all 5 slots filled, clicking
            // one of your own Characters plays the selected card into that slot, trashing the one there.
            var ap = state.Players[state.ActiveSeat];
            int clickedSlot = ap.CharacterArea.FindIndex(c => c != null && c.InstanceId == card.InstanceId);
            bool boardFull = ap.CharacterArea.All(c => c != null);
            if (boardFull && clickedSlot >= 0 && state.Phase == "main"
                && !string.IsNullOrEmpty(selectedId) && selectedSeat == state.ActiveSeat + "-hand")
            {
                var selCard = ap.Hand.Find(c => c.InstanceId == selectedId);
                if (selCard != null && GameEngine.GetCard(selCard).Type == "character")
                {
                    Dispatch(new GameCommand { Type = "playCard", Seat = state.ActiveSeat, InstanceId = selectedId, SlotIndex = clickedSlot });
                    selectedId = null;
                    selectedSeat = null;
                    return;
                }
            }
            selectedId = card.InstanceId;
            selectedSeat = seat;
            Render();
            return;
        }

        if (selectedSeat == state.ActiveSeat && seat == GameEngine.OtherSeat(state.ActiveSeat) && !string.IsNullOrEmpty(selectedId))
        {
            Dispatch(new GameCommand { Type = "declareAttack", Seat = selectedSeat, Attacker = selectedId, Target = card.InstanceId });
            selectedId = null;
            selectedSeat = null;
        }
        else
        {
            // No actionable click (e.g. inspecting an opponent card or a board card off-turn):
            // still refresh so the docked preview reflects the newly locked card.
            Render();
        }
    }

    // True for a card the active player could drag an attack FROM (their own leader or a character
    // on the field, during their main phase with no battle/effect/search in progress). The engine
    // still validates the attack itself on declare (rested target, summoning sickness, etc.).
    private bool IsAttackableBoardCard(CardInstance card, string seat)
    {
        if (card == null || string.IsNullOrEmpty(seat) || seat.EndsWith("-hand")) return false;
        if (state == null || state.Status != "active" || state.Phase != "main") return false;
        if (state.Battle != null || state.DeckLook != null || state.PendingEffects.Count > 0) return false;
        if (seat != state.ActiveSeat || !state.Players.ContainsKey(seat)) return false;
        var p = state.Players[seat];
        bool isLeader = p.Leader != null && p.Leader.InstanceId == card.InstanceId;
        bool isChar = p.CharacterArea.Any(c => c != null && c.InstanceId == card.InstanceId);
        return isLeader || isChar;
    }

    private void DeclareAttackFromDrag(string attackerSeat, string attackerId, string targetId)
    {
        if (state == null || state.Battle != null) return;
        Dispatch(new GameCommand { Type = "declareAttack", Seat = attackerSeat, Attacker = attackerId, Target = targetId });
        selectedId = null;
        selectedSeat = null;
    }

    // Mirrors the engine's DeclareAttack target rule: the opponent's leader is always valid; an
    // opponent character is valid only if it is rested (or the attacker can hit active characters).
    private bool IsValidAttackTarget(string attackerSeat, CardInstance attacker, CardInstance target)
    {
        if (state == null || attacker == null || target == null) return false;
        if (string.IsNullOrEmpty(target.Owner) || target.Owner == attackerSeat) return false;
        if (!state.Players.TryGetValue(target.Owner, out var opp) || opp == null) return false;
        if (opp.Leader != null && opp.Leader.InstanceId == target.InstanceId) return true;
        bool isChar = opp.CharacterArea.Any(c => c != null && c.InstanceId == target.InstanceId);
        if (!isChar) return false;
        if (target.Rested) return true;
        return GameEngine.HasModifier(state, attacker, "canAttackActive");
    }

    // A summoning-sick Character with a partial "Rush vs Characters" (EB02-019 Zoro) can still attack
    // opponent Characters this turn — but only when a LEGAL target actually exists (a rested Character,
    // or any Character if it can hit active ones). Uses the same engine gate DeclareAttack does, so the
    // board visual matches what an attack would allow. True → don't dim it as summoning-sick.
    private bool CanAttackCharactersNow(CardInstance card, string seat)
    {
        if (state == null || card == null) return false;
        if (!GameEngine.CanAttackCharactersOnPlayTurn(state, seat, card)) return false;
        string oppSeat = OtherSeatLocal(seat);
        if (!state.Players.TryGetValue(oppSeat, out var opp) || opp == null) return false;
        return opp.CharacterArea.Any(c => c != null && IsValidAttackTarget(seat, card, c));
    }

    private RectTransform GetCardRect(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return null;
        return cardTargetRects.TryGetValue(instanceId, out var r) ? r : null;
    }

    // On-card overlay shown while dragging an attack: green ring for a legal target, red ring + X
    // for an illegal one. Parented to the card so it tracks it; raycast-transparent.
    // Attack-target feedback while dragging: a green rim glow for a legal target, a red rim glow
    // for one that can't be attacked. Parented to the "Card Face" (which carries the rested 90deg
    // rotation) so the glow turns WITH the card instead of staying upright.
    private RectTransform ShowAttackTargetIndicator(RectTransform cardRect, bool valid)
    {
        if (cardRect == null) return null;
        var host = (cardRect.Find("Card Face") as RectTransform) ?? cardRect;
        var glow = valid ? AddUsableGlow(host) : AddInvalidGlow(host);
        if (glow == null) return null;

        // The glow overflows the card, and a leader sits in a snug zone hemmed in by other zones that
        // are drawn LATER in the same half (stage, deck, trash, ...). Those later siblings paint over
        // the overflow, so the ring looks clipped/masked by the board. Lift the target's zone (and its
        // half) to the top of the board draw order while the indicator is shown; a SiblingRestore on
        // the glow puts them back the instant the indicator is destroyed.
        var restore = glow.gameObject.AddComponent<SiblingRestore>();
        var path = new System.Collections.Generic.List<Transform>();
        var t = (Transform)cardRect;
        while (t != null && t != boardRoot) { path.Add(t); t = t.parent; }
        if (path.Count >= 3)
        {
            restore.Remember(path[path.Count - 3]); // zone (child of half)
            restore.Remember(path[path.Count - 2]); // half (child of mat)
            restore.RaiseAll();
        }
        return glow;
    }

    private bool CanStartHandDrag(CardInstance card, string handSeat)
    {
        if (state == null || card == null || state.Battle != null || state.PendingEffects.Count > 0) return false;
        if (state.Status != "active" || !state.Players.ContainsKey(handSeat)) return false;
        return state.Players[handSeat].Hand.Any(c => c.InstanceId == card.InstanceId);
    }

    private bool CanDragHandCard(CardInstance card, string handSeat)
    {
        if (!CanStartHandDrag(card, handSeat)) return false;
        if (state.ActiveSeat != handSeat || state.Phase != "main") return false;
        var player = state.Players[handSeat];
        var def = GameEngine.GetCard(card);
        if (def.Type != "character" && def.Type != "stage" && def.Type != "event") return false;
        if (GameEngine.ActiveDonCount(player) < GameEngine.GetCost(state, card)) return false;
        if (def.Type == "event") return HasMainEffect(def);
        if (def.Type == "stage") return true;
        // A full board is still draggable: drop onto an occupied slot to replace it (the
        // 6th-character rule; CanAcceptCharacterDrop only permits the drop when the board is full).
        return true;
    }

    private bool CanAcceptCharacterDrop(string seat, int slotIndex)
    {
        if (state == null || !state.Players.ContainsKey(seat)) return false;
        if (state.Status != "active" || state.ActiveSeat != seat || state.Phase != "main" || state.Battle != null || state.PendingEffects.Count > 0) return false;
        var area = state.Players[seat].CharacterArea;
        if (slotIndex < 0 || slotIndex >= area.Count) return false;
        if (area[slotIndex] == null) return true;        // empty slot - normal play
        return area.All(e => e != null);                 // occupied slot is droppable only when the board is full (replace)
    }

    private bool CanReorderHandDrop(CardInstance card, string handSeat, string dropSeat)
    {
        return dropSeat == handSeat && CanStartHandDrag(card, handSeat);
    }

    private bool CanAcceptStageDrop(string seat)
    {
        if (state == null || !state.Players.ContainsKey(seat)) return false;
        if (state.Status != "active" || state.ActiveSeat != seat || state.Phase != "main" || state.Battle != null || state.PendingEffects.Count > 0) return false;
        return state.Players[seat].Hand.Any(card => GameEngine.GetCard(card).Type == "stage" && GameEngine.ActiveDonCount(state.Players[seat]) >= GameEngine.GetCard(card).Cost);
    }

    private bool CanActivateEventFromHand(CardInstance card, string handSeat)
    {
        if (state == null || card == null || !state.Players.ContainsKey(handSeat)) return false;
        if (state.Status != "active" || state.ActiveSeat != handSeat || state.Phase != "main" || state.Battle != null || state.PendingEffects.Count > 0) return false;
        var player = state.Players[handSeat];
        if (!player.Hand.Any(c => c.InstanceId == card.InstanceId)) return false;
        var def = GameEngine.GetCard(card);
        return def.Type == "event" && HasMainEffect(def) && GameEngine.ActiveDonCount(player) >= GameEngine.GetCost(state, card);
    }

    private static bool HasMainEffect(CardDef def)
    {
        return def != null && def.Type == "event" && !string.IsNullOrWhiteSpace(def.Effect) && def.Effect.IndexOf("[Main]", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void PlayCharacterInSlot(CardInstance card, string handSeat, int slotIndex)
    {
        if (GameEngine.GetCard(card).Type != "character") return;
        if (!CanDragHandCard(card, handSeat) || !CanAcceptCharacterDrop(handSeat, slotIndex)) return;
        suppressMoveAnim.Add(card.InstanceId);   // the player's own drag WAS the movement
        Dispatch(new GameCommand { Type = "playCard", Seat = handSeat, InstanceId = card.InstanceId, SlotIndex = slotIndex });
    }

    private void PlayStage(CardInstance card, string handSeat)
    {
        if (GameEngine.GetCard(card).Type != "stage") return;
        if (!CanDragHandCard(card, handSeat) || !CanAcceptStageDrop(handSeat)) return;
        suppressMoveAnim.Add(card.InstanceId);   // the player's own drag WAS the movement
        Dispatch(new GameCommand { Type = "playCard", Seat = handSeat, InstanceId = card.InstanceId });
    }

    private void PlayEventFromHand(CardInstance card, string handSeat)
    {
        if (!CanActivateEventFromHand(card, handSeat)) return;
        StartEventBurn(card);
        Dispatch(new GameCommand { Type = "playCard", Seat = handSeat, InstanceId = card.InstanceId });
    }

    // ---- Experimental: event cards "incinerate" with a green burn when played ------------------
    private static Shader _dissolveShader;
    private static Texture2D _burnNoiseTex;

    private static Texture2D GetBurnNoiseTexture()
    {
        if (_burnNoiseTex != null) return _burnNoiseTex;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        const float scale = 5f;
        for (int y = 0; y < S; y++)
        {
            float v = (float)y / (S - 1);                 // 0 bottom, 1 top
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / (S - 1);
                float p = Mathf.PerlinNoise(u * scale, v * scale);
                float p2 = Mathf.PerlinNoise(u * scale * 2.3f + 11f, v * scale * 2.3f + 7f) * 0.5f;
                float noise = Mathf.Clamp01((p + p2) / 1.5f);
                // Bias so the TOP of the card has the lowest values - it burns away first.
                float val = Mathf.Clamp01(noise * 0.55f + (1f - v) * 0.45f);
                byte b = (byte)(val * 255f);
                px[y * S + x] = new Color32(b, b, b, 255);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        _burnNoiseTex = tex;
        return tex;
    }

    private Material CreateDissolveMaterial()
    {
        if (_dissolveShader == null) _dissolveShader = Shader.Find("UI/CardDissolve");
        if (_dissolveShader == null) return null;          // shader not imported yet - skip the effect
        var m = new Material(_dissolveShader);
        m.SetTexture("_NoiseTex", GetBurnNoiseTexture());
        m.SetFloat("_Cutoff", 0f);
        m.SetFloat("_EdgeWidth", 0.14f);
        // Match the green of the "usable" hover glow.
        m.SetColor("_EdgeColor", new Color(0.16f, 1f, 0.36f, 1f) * 1.25f);
        m.SetColor("_EmberColor", new Color(0.85f, 1f, 0.55f, 1f) * 1.2f);
        return m;
    }

    private void StartEventBurn(CardInstance card)
    {
        if (card == null || canvas == null) return;
        var sprite = GetCardSprite(card.CardId);
        if (sprite == null || sprite.texture == null) return;
        var mat = CreateDissolveMaterial();
        if (mat == null) return;

        Vector2 size = new Vector2(150f, 210f);
        Vector3 pos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        if (handCardRects.TryGetValue(card.InstanceId, out var rect) && rect != null)
        {
            size = rect.rect.size * 1.2f;
            pos = rect.position;
        }

        var go = new GameObject("Event Burn");
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.position = pos;
        var img = go.AddComponent<RawImage>();
        img.texture = sprite.texture;
        img.material = mat;
        img.raycastTarget = false;
        go.AddComponent<EventBurn>().Init(img, mat, 0.8f);
    }

    private sealed class EventBurn : MonoBehaviour
    {
        private RawImage img;
        private Material mat;
        private float dur;
        private float t;

        public void Init(RawImage image, Material material, float duration)
        {
            img = image; mat = material; dur = Mathf.Max(0.1f, duration);
        }

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            if (mat != null) mat.SetFloat("_Cutoff", p * 1.18f);
            if (img != null)
            {
                var c = img.color;
                c.a = 1f - Mathf.Clamp01((p - 0.72f) / 0.28f);   // fade the last embers out
                img.color = c;
            }
            if (p >= 1f)
            {
                if (mat != null) Destroy(mat);
                Destroy(gameObject);
            }
        }
    }

    private bool CanActivateEventDragRelease(PointerEventData eventData, CardInstance card, string handSeat)
    {
        if (!CanActivateEventFromHand(card, handSeat) || eventData == null || eventData.pointerCurrentRaycast.gameObject == null) return false;
        var eventDrop = eventData.pointerCurrentRaycast.gameObject.GetComponentInParent<EventDrop>();
        return eventDrop != null && eventDrop.Seat == handSeat;
    }

    private bool TryReorderHandDrag(PointerEventData eventData, CardInstance card, string handSeat)
    {
        if (!CanStartHandDrag(card, handSeat) || eventData == null || eventData.pointerCurrentRaycast.gameObject == null) return false;
        var handDrop = eventData.pointerCurrentRaycast.gameObject.GetComponentInParent<HandDrop>();
        if (handDrop == null || handDrop.Seat != handSeat) return false;
        int targetIndex = HandInsertIndex(handDrop.TargetRect, eventData.position, handSeat);
        Dispatch(new GameCommand { Type = "reorderHand", Seat = handSeat, InstanceId = card.InstanceId, SlotIndex = targetIndex });
        return true;
    }

    private int HandInsertIndex(RectTransform handRect, Vector2 screenPosition, string handSeat)
    {
        if (!state.Players.ContainsKey(handSeat)) return 0;
        var hand = state.Players[handSeat].Hand;
        var count = hand.Count;
        if (count == 0) return 0;

        // Compare against the actual on-screen X position of each current hand card rather than
        // a linear map across the full hand panel's width. The panel is much wider than the
        // fanned cards themselves (especially with few cards in hand), so the old approach made
        // dropping near the front/back of the hand require dragging out to the panel's edges
        // instead of just past the first/last card.
        var centerXs = new List<float>(count);
        int resolved = 0;
        foreach (var card in hand)
        {
            if (handCardRects.TryGetValue(card.InstanceId, out var cardRect) && cardRect != null)
            {
                var screenPoint = RectTransformUtility.WorldToScreenPoint(null, cardRect.position);
                centerXs.Add(screenPoint.x);
                resolved++;
            }
            else
            {
                centerXs.Add(float.NaN);
            }
        }

        if (resolved == 0)
        {
            if (handRect == null) return 0;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(handRect, screenPosition, null, out var localPoint);
            var rect = handRect.rect;
            var normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            return Mathf.Clamp(Mathf.RoundToInt(normalized * count), 0, count);
        }

        for (int i = 0; i < count; i++)
        {
            if (!float.IsNaN(centerXs[i]) && screenPosition.x < centerXs[i]) return i;
        }
        return count;
    }

    private RectTransform GetDragTargetRect(PointerEventData eventData, CardInstance card, string handSeat, out bool valid)
    {
        valid = false;
        if (eventData == null || eventData.pointerCurrentRaycast.gameObject == null || card == null) return null;

        var def = GameEngine.GetCard(card);
        var target = eventData.pointerCurrentRaycast.gameObject;
        var handDrop = target.GetComponentInParent<HandDrop>();
        if (handDrop != null)
        {
            valid = CanReorderHandDrop(card, handSeat, handDrop.Seat);
            return handDrop.TargetRect;
        }

        var characterDrop = target.GetComponentInParent<CharacterSlotDrop>();
        if (characterDrop != null)
        {
            valid = def.Type == "character" && characterDrop.Seat == handSeat && CanAcceptCharacterDrop(characterDrop.Seat, characterDrop.SlotIndex);
            return characterDrop.TargetRect;
        }

        var stageDrop = target.GetComponentInParent<StageDrop>();
        if (stageDrop != null)
        {
            valid = def.Type == "stage" && stageDrop.Seat == handSeat && CanAcceptStageDrop(stageDrop.Seat);
            return stageDrop.TargetRect;
        }

        var eventDrop = target.GetComponentInParent<EventDrop>();
        if (eventDrop != null)
        {
            valid = def.Type == "event" && eventDrop.Seat == handSeat && CanActivateEventFromHand(card, handSeat);
            return eventDrop.TargetRect;
        }

        return null;
    }

    private CardInstance FindAny(string seat, string instanceId)
    {
        if (seat == null || !state.Players.ContainsKey(seat)) return null;
        var p = state.Players[seat];
        if (p.Leader.InstanceId == instanceId) return p.Leader;
        if (p.Stage != null && p.Stage.InstanceId == instanceId) return p.Stage;
        return p.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == instanceId);
    }

    private void ShowPreview(CardInstance card, bool faceUp)
    {
        if (!faceUp || card == null)
        {
            HidePreview();
            return;
        }
        previewRoot.gameObject.SetActive(true);
        previewImage.sprite = GetCardSprite(card.CardId);
        previewTitle.text = GameEngine.GetCard(card).Name;
        previewRoot.SetAsLastSibling();
    }

    private void ShowDonPreview()
    {
        previewRoot.gameObject.SetActive(true);
        previewImage.sprite = GetDonSprite();
        previewTitle.text = "DON!! Card";
        previewRoot.SetAsLastSibling();
    }

    private void ShowBackPreview()
    {
        previewRoot.gameObject.SetActive(true);
        previewImage.sprite = GetBackSprite();
        previewTitle.text = "";
        previewRoot.SetAsLastSibling();
    }

    private void ShowDonBackPreview()
    {
        previewRoot.gameObject.SetActive(true);
        previewImage.sprite = GetDonBackSprite();
        previewTitle.text = "";
        previewRoot.SetAsLastSibling();
    }

    private void HidePreview()
    {
        if (previewRoot != null) previewRoot.gameObject.SetActive(false);
    }

    // Shown while the cursor is over a [Blocker] shield; hidden on exit / board rebuild.
    public void ShowBlockerPreview()
    {
        if (blockerPreviewRoot == null) return;
        HidePreview();                          // don't fight the card preview for the right slot
        blockerPreviewRoot.gameObject.SetActive(true);
        blockerPreviewRoot.SetAsLastSibling();
    }

    public void HideBlockerPreview()
    {
        if (blockerPreviewRoot != null) blockerPreviewRoot.gameObject.SetActive(false);
    }

    // Preview a card by its id (used by the clickable card links in the combat log).
    public void ShowPreviewById(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || previewRoot == null) { HidePreview(); return; }
        var sprite = GetCardSprite(cardId);
        if (sprite == null) return;
        previewRoot.gameObject.SetActive(true);
        previewImage.sprite = sprite;
        var cd = CardData.GetCard(cardId);
        previewTitle.text = cd != null ? cd.Name : cardId;
        previewRoot.SetAsLastSibling();
    }

    private void ShowHandHoverPreview(CardInstance card, RectTransform source)
    {
        HideHandHoverPreview();
        if (card == null || source == null || canvas == null) return;

        var canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        var size = new Vector2(288f, 402f);
        var screenPoint = RectTransformUtility.WorldToScreenPoint(null, source.TransformPoint(source.rect.center));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out var localPoint)) return;

        var liftDirection = card.Owner == TopSeat ? -1f : 1f;
        var target = localPoint + new Vector2(0, (size.y * 0.5f + 70f) * liftDirection);
        target = ClampToCanvas(target, size, canvasRect.rect, 12f);

        handHoverRoot = PanelObject("Hand Hover Preview", canvas.transform, new Color(0, 0, 0, 0));
        handHoverRoot.anchorMin = new Vector2(0.5f, 0.5f);
        handHoverRoot.anchorMax = new Vector2(0.5f, 0.5f);
        handHoverRoot.pivot = new Vector2(0.5f, 0.5f);
        handHoverRoot.sizeDelta = size;
        handHoverRoot.anchoredPosition = target;
        var group = handHoverRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        // A valid deck-look/search pick keeps the GREEN glow on its hover preview too (it IS a valid
        // target) rather than flipping to the neutral gold outline used for non-actionable cards.
        if (IsGreenTargetNow(card))
            AddUsableGlow(handHoverRoot);
        else AddMysticalCardOutline(handHoverRoot, true);
        RoundedCardVisual("Hand Hover Art", handHoverRoot, GetCardSprite(card.CardId), out var image);
        image.raycastTarget = false;
        handHoverRoot.SetAsLastSibling();
    }

    private void HideHandHoverPreview()
    {
        if (handHoverRoot == null) return;
        Destroy(handHoverRoot.gameObject);
        handHoverRoot = null;
    }

    private static Vector2 ClampToCanvas(Vector2 position, Vector2 size, Rect canvasRect, float padding)
    {
        var halfWidth = size.x * 0.5f + padding;
        var halfHeight = size.y * 0.5f + padding;
        return new Vector2(
            Mathf.Clamp(position.x, canvasRect.xMin + halfWidth, canvasRect.xMax - halfWidth),
            Mathf.Clamp(position.y, canvasRect.yMin + halfHeight, canvasRect.yMax - halfHeight));
    }

    private void AddCardToZone(RectTransform zone, CardInstance card, string seat, bool faceUp, bool inverted = false)
    {
        var holder = new GameObject("Card Holder").AddComponent<RectTransform>();
        holder.SetParent(zone, false);
        FitCardAspect(zone, holder);
        if (inverted) holder.localRotation = Quaternion.Euler(0, 0, 180f);
        // holder is rotated for the opponent side, so tell AddCard to counter-rotate its text
        // overlays (status tags / DON count) back upright — otherwise they read upside-down.
        AddCard(holder, card, seat, faceUp, Vector2.zero, true, flipOverlays: inverted);
    }

    private void AddCard(RectTransform parent, CardInstance card, string seat, bool faceUp, Vector2 size, bool stretch = false, bool inverted = false, int handHomeSiblingIndex = -1, bool suppressPreview = false, bool flipOverlays = false)
    {
        // The card reads upside-down (opponent side) when either this call rotates the root 180°
        // (inverted) or the parent holder was already rotated (flipOverlays) — text overlays must be
        // counter-rotated so tags/DON counts stay readable.
        bool overlaysUpsideDown = inverted || flipOverlays;
        var attachedDon = faceUp ? card.AttachedDonIds.Count : 0;
        var root = PanelObject(GameEngine.GetCard(card).Name, parent, new Color(0, 0, 0, 0));
        root.GetComponent<Image>().raycastTarget = false;
        if (stretch) Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        else SetPreferred(root, size);
        if (inverted) root.localRotation = Quaternion.Euler(0, 0, 180f);
        if (card != null && !string.IsNullOrEmpty(card.InstanceId) && seat != null)
            cardTargetRects[card.InstanceId] = root;

        // Card always renders full-size; attached DON are tucked behind it (see the loop below).
        var cardBody = PanelObject("Card Face", root, new Color(0, 0, 0, 0));
        Stretch(cardBody, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Soft drop shadow for depth (Rift-Atlas style): a blurred rounded silhouette slightly larger
        // than the card and nudged down, behind the art so it peeks out around the edges. First child of
        // cardBody, so it rides along when the card rests/taps.
        var shadow = ImageObject("Card Shadow", cardBody, GetCardShadowSprite());
        shadow.type = Image.Type.Sliced;
        shadow.color = new Color(0f, 0f, 0f, 0.52f);
        shadow.raycastTarget = false;
        var shRt = shadow.rectTransform;
        shRt.anchorMin = Vector2.zero; shRt.anchorMax = Vector2.one;
        shRt.offsetMin = new Vector2(-9f, -17f);   // expand sideways, extend + drop below
        shRt.offsetMax = new Vector2(9f, -3f);
        shRt.SetAsFirstSibling();

        RoundedCardVisual("Art", cardBody, faceUp ? GetCardSprite(card.CardId) : GetBackSprite(), out var image);

        if (card.Rested)
        {
            // Rested cards turn 90deg (the standard cue) but stay fully opaque - dimming them let the
            // DON tucked behind the character show through, which read as a visual bug. Negative Z = clockwise,
            // so the card tips onto its RIGHT side.
            cardBody.localRotation = Quaternion.Euler(0, 0, -90f);
        }

        if (faceUp)
        {
            // Attached DON!! shown the OP way: ドン!!×N (katakana for DON!!), so it reads distinctly
            // from stat/cost pills (e.g. "+1 COST").
            if (card.AttachedDonIds.Count > 0) { var db = AddBadge(cardBody, "ドン!!×" + card.AttachedDonIds.Count, new Vector2(0.40f, 0.80f), new Vector2(0.98f, 0.99f), new Color32(120, 235, 155, 255), jpFont); if (overlaysUpsideDown) FlipBadgeUpright(db); }

            // Status indicator chips — everything currently affecting this card (OPTCGSim-style
            // insight): power/cost changes, base overrides, keyword grants, restrictions
            // (can't attack / frozen / negated / ...), protections. Engine-computed, so the
            // chips can never disagree with the rules. Stacked from the bottom-left; the
            // hover preview lists the full detail for each.
            if (state != null && (card.Zone == "character" || card.Zone == "leader" || card.Zone == "stage"))
            {
                var statusBadges = GameEngine.GetStatusBadges(state, card);
                float badgeY = 0.02f;
                for (int sbI = 0; sbI < statusBadges.Count && sbI < 5; sbI++)
                {
                    var sb = statusBadges[sbI];
                    var badgeRt = AddBadge(cardBody, sb.Label,
                        new Vector2(0.02f, badgeY), new Vector2(0.80f, badgeY + 0.125f),
                        StatusBadgeColor(sb.Kind));
                    if (overlaysUpsideDown) FlipBadgeUpright(badgeRt);
                    badgeY += 0.138f;
                }
                if (statusBadges.Count > 5)
                {
                    var moreRt = AddBadge(cardBody, "+" + (statusBadges.Count - 5) + " MORE",
                        new Vector2(0.02f, badgeY), new Vector2(0.80f, badgeY + 0.125f),
                        new Color32(170, 182, 200, 255));
                    if (overlaysUpsideDown) FlipBadgeUpright(moreRt);
                }
            }

            // Persistent GREEN glow on every card that is a VALID target for the pending effect
            // (engine-validated via IsValidEffectTarget); invalid in-zone cards keep the red
            // hover treatment (CardHover). Only shown to the deciding seat's client.
            if (state != null && state.PendingEffects.Count > 0)
            {
                var pending = state.PendingEffects[0];
                if ((!isNetworked || pending.Seat == localSeat) && GameEngine.IsValidEffectTarget(state, pending, card))
                    AddUsableGlow(cardBody);
            }

            // Persistent GREEN glow on my own Characters while the 6th-character replace prompt is up —
            // they are the "play over this one" picks.
            if (state != null && state.PendingCharReplace != null && IsCharReplaceTarget(card))
                AddUsableGlow(cardBody);
        }

        if (faceUp && IsDonAttachTarget(seat, card))
        {
            cardBody.gameObject.AddComponent<DonAttachTarget>().Init(this, seat, card);
            if (selectedDonIds.Count > 0 && selectedDonSeat == seat)
                AddOutline(cardBody.gameObject, new Color32(255, 214, 112, 210), 1.6f);
        }

        // The green "usable" glow appears only when the mouse hovers a valid card (handled by
        // CardHover), not persistently on every legal pick.

        // Replay is observation-only (spec: "just watching, no interaction") — no clicking to
        // select/target, no drag-to-attack, no hand reordering. Hover/preview still works
        // (CardHover below is unconditional) so you can still inspect any card at rest.
        if (!isReplayMode)
        {
            var button = cardBody.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() => OnCardClick(card, seat));
        }

        var hover = cardBody.gameObject.AddComponent<CardHover>();
        hover.Init(this, card, faceUp, seat != null && seat.EndsWith("-hand"), handHomeSiblingIndex, suppressPreview);

        // Right-click any card (even in replay) to file a bug report that captures the exact game state
        // (BugReportStore) — a durable, scrapeable record with an exact-repro command log.
        cardBody.gameObject.AddComponent<CardContextClick>().Init(this, card);

        // Drag-to-attack: drag from your own leader/character to an opponent card; the arrow
        // follows the cursor and the attack is declared on release. (Click-to-attack still works.)
        if (!isReplayMode && faceUp && IsAttackableBoardCard(card, seat))
            cardBody.gameObject.AddComponent<AttackDrag>().Init(this, canvas, card, seat);

        if (!isReplayMode && faceUp && seat != null && seat.EndsWith("-hand"))
        {
            var drag = cardBody.gameObject.AddComponent<CardDrag>();
            drag.Init(this, canvas, card, seat.Replace("-hand", ""), inverted);
            // `parent` here is the fan-positioned "Fanned Hand Card" holder (root's container),
            // not root itself - that's the object whose anchoredPosition/rotation the fan layout
            // (and the live drag-reorder preview) actually manipulates.
            handCardRects[card.InstanceId] = parent;
        }

        // Created after cardBody so attached-DON visuals are later siblings (on top), meaning
        // hovering them anywhere they overlap the card art reliably shows the DON preview
        // instead of being shadowed by the card's own raycast target underneath.
        // Attached DON: a tight horizontal row (cost-area style) tucked under the character. The row
        // starts at the character's vertical middle and is centred; its left-to-right span is capped
        // to the character's width, so each added DON packs TIGHTER instead of spilling past the card.
        int attachedShown = Mathf.Min(attachedDon, 10);
        if (attachedShown > 0)
        {
            // Attached DON render at the SAME size as a leader/character card (they're normal cards,
            // just tucked behind). The fan NEVER exceeds the character's ON-SCREEN width: a rested
            // character is rotated 90°, so its on-screen width is the card's HEIGHT — the DON get that
            // much room and spread wider. A few DON stay bunched at a comfortable spacing; as the count
            // climbs the spacing shrinks so they pack exactly to the edge instead of spilling past it.
            float donW = boardCardSize.x;
            float donH = boardCardSize.y;
            // Fan the attached DON out behind the card, up to the card's RESTED horizontal extent
            // (its portrait HEIGHT), so several DON spread into a fan instead of stacking on one spot
            // (availWidth == donW made the step 0 → every DON overlapped, looking like a single one).
            // Applies to both active and rested characters.
            float availWidth = Mathf.Max(donW, donH);
            float preferredStep = donW * 0.16f;                   // bunched look while there's room
            float maxStep = attachedShown > 1 ? Mathf.Max(0f, (availWidth - donW) / (attachedShown - 1)) : 0f;
            float step = Mathf.Min(preferredStep, maxStep);
            float total = donW + step * (attachedShown - 1);      // guaranteed <= availWidth
            float startX = -total * 0.5f + donW * 0.5f;
            float centerY = donH * -0.30f;                        // ~30% of the DON peeks past the card
            bool donMinusPick = faceUp && DonMinusPaymentActive(seat);
            for (int i = 0; i < attachedShown; i++)
            {
                var don = new GameObject("Attached DON").AddComponent<RectTransform>();
                don.SetParent(root, false);
                don.SetAsFirstSibling();   // render BEHIND the character so it tucks under, not over
                don.anchorMin = don.anchorMax = new Vector2(0.5f, 0.5f);
                don.pivot = new Vector2(0.5f, 0.5f);
                don.sizeDelta = new Vector2(donW, donH);
                don.anchoredPosition = new Vector2(startX + i * step, centerY);
                AddDonCardVisual(don, false, true);

                // During a "DON!! −N" payment, attached DON!! are returnable — bring them to the front,
                // highlight them, and let a click return that specific DON!! (engine detaches it).
                if (donMinusPick && i < card.AttachedDonIds.Count)
                {
                    string donId = card.AttachedDonIds[i];
                    don.SetAsLastSibling();
                    AddUsableGlow(don);
                    don.gameObject.AddComponent<Button>().onClick.AddListener(() => ReturnAttachedDon(seat, donId));
                }
            }
        }
    }

    private void AddDon(RectTransform parent, DonInstance don, bool inverted = false)
    {
        var holder = new GameObject("DON!!").AddComponent<RectTransform>();
        holder.SetParent(parent, false);
        SetPreferred(holder, new Vector2(34, 48));
        AddDonCardVisual(holder, don.Rested, true);
        if (don.Rested) holder.localRotation = Quaternion.Euler(0, 0, inverted ? 270f : 90f);
        else if (inverted) holder.localRotation = Quaternion.Euler(0, 0, 180f);
    }

    private void DrawFittedCardRow(RectTransform parent, IList<CardInstance> cards, string seat, bool faceUp, bool inverted,
                                   float maxCardWidth, float cardHeight, float idealGap, float minStep)
    {
        if (cards == null || cards.Count == 0) return;

        var layout = FittedRow(cards.Count, maxCardWidth, idealGap, minStep, false);
        var yMin = Mathf.Clamp01((1f - cardHeight) * 0.5f);
        var yMax = Mathf.Clamp01(yMin + cardHeight);

        for (int i = 0; i < cards.Count; i++)
        {
            var holder = new GameObject("Fitted Card").AddComponent<RectTransform>();
            holder.SetParent(parent, false);
            var x = layout.start + i * layout.step;
            Stretch(holder, new Vector2(x, yMin), new Vector2(x + layout.cardWidth, yMax), Vector2.zero, Vector2.zero);
            AddCard(holder, cards[i], seat, faceUp, Vector2.zero, true, inverted);
        }
    }

    private struct FanGeometry
    {
        public float CardWidth;
        public float CardHeight;
        public float MaxSpread;
        public float ArcDepth;
        public float MaxAngle;
    }

    private FanGeometry ComputeFanGeometry(RectTransform parent, int count)
    {
        var width = parent.rect.width;
        if (width <= 0f && boardRoot != null) width = boardRoot.rect.width * 0.76f;
        if (width <= 0f) width = Screen.width * 0.58f;
        var height = parent.rect.height;
        if (height <= 0f) height = Screen.height * 0.12f;

        var cardWidth = Mathf.Clamp(width * 0.10f, 68f, 108f);
        cardWidth = Mathf.Min(cardWidth, Mathf.Max(60f, height * 0.82f / 1.40f));
        // Flatten the fan as the hand grows: small hands keep a gentle arc/tilt, large hands go nearly
        // flat so the edge cards don't tilt up and poke above the row.
        float fan = Mathf.Lerp(1f, 0.2f, Mathf.Clamp01((count - 5f) / 8f));
        return new FanGeometry
        {
            CardWidth = cardWidth,
            CardHeight = cardWidth * 1.40f,
            MaxSpread = Mathf.Min(width * 0.82f, Mathf.Max(0f, (count - 1) * cardWidth * 0.70f)),
            ArcDepth = Mathf.Clamp(height * 0.12f, 3f, 14f) * fan,
            MaxAngle = Mathf.Clamp((count - 1) * 1.1f, 3f, 10f) * fan,
        };
    }

    // Positions/rotates holder for slot `index` of `count` within a fan using the given geometry.
    // Shared by the normal layout pass and CardDrag's live in-hand reorder preview, so dragging
    // always slots cards into exactly the same positions the fan would render them at at rest.
    private void ApplyFanSlot(RectTransform holder, FanGeometry geo, int index, int count, bool top)
    {
        var pose = FanSlotPose(geo, index, count, top);
        ApplyRectPose(holder, pose.position, pose.rotation, Vector3.one);
    }

    private void SlideFanSlot(RectTransform holder, FanGeometry geo, int index, int count, bool top, float lift = 0f, float scale = 1f)
    {
        var pose = FanSlotPose(geo, index, count, top);
        var liftDirection = top ? -1f : 1f;
        AnimateRectPose(holder, pose.position + new Vector2(0f, lift * liftDirection), pose.rotation, Vector3.one * scale, 0.10f);
    }

    private static (Vector2 position, Quaternion rotation) FanSlotPose(FanGeometry geo, int index, int count, bool top)
    {
        var normalized = HandFanPosition(index, count);
        var x = normalized * geo.MaxSpread * 0.5f;
        var curve = (1f - normalized * normalized) * geo.ArcDepth;
        var y = top ? -curve : curve;
        return (new Vector2(x, y), Quaternion.Euler(0, 0, top ? normalized * geo.MaxAngle : -normalized * geo.MaxAngle));
    }

    private RectTransform GetHandRow(string seat) => seat == "south" ? southHandRow : northHandRow;

    private List<CardInstance> GetHandSnapshot(string seat) => new List<CardInstance>(state.Players[seat].Hand);

    private void DrawFannedHandRow(RectTransform parent, IList<CardInstance> cards, string seat, bool top)
    {
        if (seat == "south-hand") southHandRow = parent; else if (seat == "north-hand") northHandRow = parent;
        if (cards == null || cards.Count == 0) return;

        int count = cards.Count;
        var geo = ComputeFanGeometry(parent, count);

        var holders = new RectTransform[count];
        foreach (var i in Enumerable.Range(0, count).OrderByDescending(index => Mathf.Abs(HandFanPosition(index, count))).ThenBy(index => index))
        {
            var holder = new GameObject("Fanned Hand Card").AddComponent<RectTransform>();
            holder.SetParent(parent, false);
            holder.anchorMin = new Vector2(0.5f, 0.5f);
            holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(geo.CardWidth, geo.CardHeight);

            ApplyFanSlot(holder, geo, i, count, top);
            bool handFaceUp;
            if (isReplayMode)
            {
                // Independent per-seat toggle (replayHandVisibility) rather than the live-match
                // "hide the opponent's hand" rule below — a replay has no real "opponent" to hide
                // information from, just a spectator choosing what to look at.
                string realSeat = seat.EndsWith("-hand") ? seat.Substring(0, seat.Length - 5) : seat;
                handFaceUp = replayHandVisibility == "both" || replayHandVisibility == realSeat;
            }
            else
            {
                // In a networked match the top-rendered hand is always the remote opponent's (see
                // BottomSeat/TopSeat) - hide it as card backs, same as any other face-down pile.
                // Hotseat/Versus Self is unaffected (isNetworked is false there).
                // At the end of the match we reveal the opponent's hand (RevealFinishedInfo, win or
                // lose) — the local hand is already face-up, so this only flips the top one.
                handFaceUp = RevealFinishedInfo() || !(top && (isNetworked || aiSeat == TopSeat));
            }
            AddCard(holder, cards[i], seat, handFaceUp, Vector2.zero, true, top && !IsReplayRotated, count - 1 - i);
            holders[i] = holder;
            // Presence IN: remember the opponent's face-down hand holders (by hand index) so
            // PresenceReceived can lift the cards they're currently inspecting.
            if (isNetworked && top)
            {
                while (opponentHandSlots.Count <= i) { opponentHandSlots.Add(null); opponentHandSlotHomes.Add(Vector2.zero); }
                opponentHandSlots[i] = holder;
                opponentHandSlotHomes[i] = holder.anchoredPosition;
            }
        }

        // Push siblings to the front from rightmost to leftmost, so the leftmost card ends up
        // frontmost. Done after every holder exists so each SetAsLastSibling call is unambiguous.
        for (var i = count - 1; i >= 0; i--) holders[i].SetAsLastSibling();
    }

    private static float HandFanPosition(int index, int count)
    {
        return count <= 1 ? 0f : Mathf.Lerp(-1f, 1f, index / (float)(count - 1));
    }

    private void DrawFittedDonRow(RectTransform parent, IList<DonInstance> donCards, string seat, bool inverted)
    {
        if (donCards == null || donCards.Count == 0) return;

        // DON tuck under each other in a condensed, centred row (matches the reference mat): uniform
        // card size, overlapping, staying tucked even when rested/used. Step shrinks to fit the band.
        int n = donCards.Count;
        // DON render at the SAME size as board cards (they ARE cards) — no height clamp to the (inset)
        // DON-row band, which was shrinking them ~12%. Full-size DON still fit within the cost ZONE.
        float cardW = boardCardSize.x > 1f ? boardCardSize.x : 70f;
        float cardH = boardCardSize.y > 1f ? boardCardSize.y : cardW / CardAspect;
        float availW = parent.rect.width > 1f ? parent.rect.width : (boardRoot != null ? boardRoot.rect.width * 0.55f : 600f);
        float donStep = cardW * 0.34f;
        if (cardW + (n - 1) * donStep > availW) donStep = (availW - cardW) / Mathf.Max(1, n - 1);
        float donStart = -((n - 1) * donStep) * 0.5f;
        // Draw rested DON first (left) then active DON (right), so DON that refresh/restand back to
        // active always regroup to one side instead of appearing interleaved with rested ones (the
        // raw CostArea order can mix them after a restand/return-active). Engine state is untouched —
        // only the DRAW order changes, and each DON keeps its ORIGINAL CostArea index so click/drag
        // still resolve correctly (SelectDon validates CostArea[index] == the clicked DON). This
        // mirrors DrawGroupedDonRow, which already clusters rested-then-active.
        var order = new List<int>(n);
        for (int i = 0; i < n; i++) if (donCards[i].Rested) order.Add(i);
        for (int i = 0; i < n; i++) if (!donCards[i].Rested) order.Add(i);
        for (int pos = 0; pos < n; pos++)
        {
            int oi = order[pos];
            AddCostDon(parent, donCards[oi], seat, oi, donStart + pos * donStep, cardW, cardH, inverted, -1);
        }
    }

    private static (float cardWidth, float step, float start) FittedRow(int count, float maxCardWidth, float idealGap, float minStep, bool center)
    {
        if (count <= 1) return (Mathf.Min(maxCardWidth, 1f), 0f, center ? (1f - Mathf.Min(maxCardWidth, 1f)) * 0.5f : 0f);

        var cardWidth = Mathf.Min(maxCardWidth, 1f);
        var noOverlapStep = cardWidth + idealGap;
        var fitStep = (1f - cardWidth) / (count - 1);
        var step = Mathf.Min(noOverlapStep, fitStep);

        if (step < minStep)
        {
            cardWidth = Mathf.Max(0.045f, 1f - minStep * (count - 1));
            step = Mathf.Min(minStep, (1f - cardWidth) / (count - 1));
        }

        var total = cardWidth + step * (count - 1);
        var start = center ? Mathf.Max(0f, (1f - total) * 0.5f) : 0f;
        return (cardWidth, step, start);
    }

    // Visual depth (in px) a face-down pile rises by, saturating with card count so a full deck is a
    // modest stack and a near-empty one a sliver. Shared by AddBackStack and the snug-zone sizing.
    private float StackDepth(int count)
    {
        int c = Mathf.Clamp(count, 1, 60);
        return boardCardSize.y * 0.13f * (1f - Mathf.Exp(-c / 18f));
    }

    // Resize a mat zone so its colored region hugs the card(s) inside it instead of leaving slack.
    // Centered on the same spot the min/max rect would occupy; pxW/pxH are the content's pixel size.
    private void SnugZone(RectTransform zone, Vector2 min, Vector2 max, float pxW, float pxH)
    {
        float cx = (min.x + max.x) * 0.5f;
        float cy = (min.y + max.y) * 0.5f;
        zone.anchorMin = zone.anchorMax = new Vector2(cx, cy);
        zone.pivot = new Vector2(0.5f, 0.5f);
        zone.sizeDelta = new Vector2(pxW, pxH);
        zone.anchoredPosition = Vector2.zero;
    }

    // The actual pixel size a single board card renders at inside this zone - i.e. boardCardSize after
    // FitCardAspect's clamp to the zone rect. Used so SnugZone wraps the REAL card, not the ideal size.
    private Vector2 FittedCardSize(RectTransform zone)
    {
        float cw = boardCardSize.x, chh = boardCardSize.y;
        float maxW = zone.rect.width * 0.92f, maxH = zone.rect.height * 0.92f;
        if (maxW > 1f && cw > maxW) { cw = maxW; chh = cw / CardAspect; }
        if (maxH > 1f && chh > maxH) { chh = maxH; cw = chh * CardAspect; }
        return new Vector2(cw, chh);
    }

    private void AddBackStack(RectTransform holder, int visibleCount, Sprite back = null, bool donBack = false)
    {
        // Show pile depth as a compact 3D stack whose edges fall FORWARD (down-and-right toward the
        // viewer), like the reference. The total depth SATURATES with card count, so a near-empty pile
        // is a sliver and a full 50-card deck is only a modest stack - never massive.
        int count = Mathf.Clamp(visibleCount, 1, 60);
        var stackSprite = back != null ? back : GetBackSprite();
        float total = StackDepth(count);
        for (int j = 0; j < count; j++)
        {
            // j = 0 is the deepest layer (furthest forward/down); j = count-1 is the front card, flush
            // with the holder and drawn last so it sits on top. Offset is purely VERTICAL so the pile
            // stacks straight down toward the viewer with no diagonal lean.
            float t = count > 1 ? (float)(count - 1 - j) / (count - 1) : 0f; // 1 at back, 0 at front
            var card = PanelObject("Stacked Card Back", holder, new Color(0, 0, 0, 0));
            Stretch(card, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            card.anchoredPosition += new Vector2(0f, -total * t);
            AddOutline(card.gameObject, new Color32(0, 0, 0, 55), 0.7f);
            RoundedCardVisual("Back", card, stackSprite, out var img);
            img.raycastTarget = false;
            card.gameObject.AddComponent<BackHover>().Init(this, donBack);
        }
    }

    // A face-up pile (trash): depth layers fall forward like a deck, with the most-recent card face-up
    // on top. The depth layers reuse the top card's art so only their thin edges show beneath it.
    private void AddFaceUpStack(RectTransform holder, CardInstance topCard, int count)
    {
        int n = Mathf.Clamp(count, 1, 60);
        float total = StackDepth(count);
        var sprite = GetCardSprite(topCard.CardId);
        for (int j = 0; j < n - 1; j++)
        {
            float t = (float)(n - 1 - j) / (n - 1); // 1 at back -> >0; the front card is added last
            var layer = PanelObject("Trash Layer", holder, new Color(0, 0, 0, 0));
            Stretch(layer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            layer.anchoredPosition += new Vector2(0f, -total * t);
            AddOutline(layer.gameObject, new Color32(0, 0, 0, 55), 0.7f);
            RoundedCardVisual("Edge", layer, sprite, out var img);
            img.raycastTarget = false;
        }
        // Front = the real most-recent card, flush on top, with hover/preview/badges.
        AddCard(holder, topCard, topCard.Owner, true, Vector2.zero, true);
    }

    // When an "add … from the top or bottom of your Life …" effect is pending for this local seat,
    // overlay the Life pile with two glowing, clickable halves (TOP / BOTTOM) so the player picks
    // the Life card right on the board (no A/B modal). Each dispatches resolveEffect with the top /
    // bottom Life instance id — validated by the engine's IsValidEffectTarget.
    private void MaybeAddLifeTargetPicker(RectTransform lifeZone, PlayerState p, string seat)
    {
        if (lifeZone == null || state == null || state.PendingEffects.Count == 0 || p.Life.Count == 0) return;
        var pe = state.PendingEffects[0];
        string peText = pe.Text ?? "";
        // The effect's controller (pe.Seat) makes the pick — never the AI or a remote player.
        string controller = pe.Seat;
        if (isNetworked && controller != localSeat) return;
        if (aiSeat != null && controller == aiSeat) return;
        // Which Life pile does this effect want to pick from?
        //   "top or bottom of your Life"            → the CONTROLLER's own Life  (seat == controller)
        //   "top or bottom of your opponent's Life" → the OPPONENT's Life        (seat == other seat)
        bool wantsOpp = peText.IndexOf("top or bottom of your opponent's Life", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool wantsOwn = !wantsOpp && peText.IndexOf("top or bottom of your Life", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool zoneIsOwn = seat == controller;
        bool zoneIsOpp = seat == OtherSeatLocal(controller);
        if (!((wantsOwn && zoneIsOwn) || (wantsOpp && zoneIsOpp))) return;

        // Snap the glow to the ACTUAL top and bottom Life card rects (AddLifeStackToZone stores
        // them in lifeMoveRects[seat], card index i == Life[i]), rather than splitting the zone in
        // half. So the rim glow hugs the real card, like any other targetable card.
        if (!lifeMoveRects.TryGetValue(seat, out var lifeRects) || lifeRects == null || lifeRects.Count == 0) return;
        int visible = Mathf.Min(p.Life.Count, lifeRects.Count);
        string topId = p.Life[p.Life.Count - 1].InstanceId;   // engine top = end of the list
        string botId = p.Life[0].InstanceId;                  // engine bottom = index 0

        void GlowCard(int idx, string lifeId)
        {
            if (idx < 0 || idx >= lifeRects.Count || lifeRects[idx] == null) return;
            var card = lifeRects[idx];
            AddUsableGlow(card);
            // Transparent, card-sized click catcher on top (the Life card art itself is not
            // raycastable), routing the pick to resolveEffect.
            var pick = PanelObject("Life Pick", card, new Color(0f, 0f, 0f, 0f));
            Stretch(pick, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var pimg = pick.GetComponent<Image>(); if (pimg != null) pimg.raycastTarget = true;
            pick.SetAsLastSibling();
            pick.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                Dispatch(new GameCommand { Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId, Target = lifeId }));
        }

        GlowCard(visible - 1, topId);                     // top-most visible card = top of Life
        if (p.Life.Count > 1) GlowCard(0, botId);         // index 0 = bottom of Life
    }

    private RectTransform AddPile(RectTransform root, string label, int count, Vector2 min, Vector2 max, bool cardBack)
    {
        var zone = ZonePanel(root, label, min, max);
        var pile = PanelObject(label + " Pile", zone, new Color(0, 0, 0, 0));
        Stretch(pile, new Vector2(0.15f, 0.18f), new Vector2(0.85f, 0.72f), Vector2.zero, Vector2.zero);
        if (cardBack && count > 0)
        {
            RoundedCardVisual("Back", pile, GetBackSprite(), out var img);
        }
        var countText = TextObject("Count", zone, count.ToString(), 18, Ink, TextAnchor.MiddleCenter);
        Stretch(countText.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.18f), Vector2.zero, Vector2.zero);
        return pile;
    }

    private RectTransform ZonePanel(RectTransform parent, string label, Vector2 min, Vector2 max)
    {
        var zone = PanelObject(label + " Zone", parent, ZoneFill);
        Stretch(zone, min, max, Vector2.zero, Vector2.zero);
        AddOutline(zone.gameObject, new Color32(70, 82, 104, 95), 0.6f);
        var text = TextObject(label + " Label", zone, label, 9, new Color32(139, 150, 168, 190), TextAnchor.UpperLeft);
        Stretch(text.rectTransform, new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero);
        return zone;
    }

    private RectTransform RowObject(string name, RectTransform parent, float spacing, TextAnchor alignment)
    {
        var row = new GameObject(name).AddComponent<RectTransform>();
        row.SetParent(parent, false);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = alignment;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        return row;
    }

    private bool CanEndTurn()
    {
        return state.Status == "active" && state.Phase == "main" && state.Battle == null && state.PendingEffects.Count == 0
            && (!isNetworked || state.ActiveSeat == localSeat);   // no dead END TURN button off-turn in networked play
    }

    private void AddButton(RectTransform parent, string label, UnityEngine.Events.UnityAction action, bool enabled = true, bool dot = true)
    {
        // Long labels (rethought effect descriptions) wrap onto extra lines: grow the
        // button instead of clipping, and let the text best-fit down a notch if needed.
        int estLines = 1 + (label != null ? label.Length : 0) / 30;
        float btnH = 34f + Mathf.Clamp(estLines - 1, 0, 2) * 13f;
        var root = PanelObject(label + " Button", parent, enabled ? new Color32(34, 58, 78, 235) : new Color32(24, 34, 44, 170));
        SetPreferred(root, new Vector2(118, btnH));
        root.sizeDelta = new Vector2(118, btnH);
        Round(root);
        AddRoundedCardBorder(root, enabled ? MenuB : (Color)new Color32(50, 58, 74, 80), 1.1f);
        Color textColor = enabled ? Ink : (Color)new Color32(120, 130, 146, 160);
        if (dot)
        {
            var d = PanelObject("Dot", root, enabled ? Accent : (Color)new Color32(90, 100, 116, 160));
            d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
            d.pivot = new Vector2(0f, 0.5f);
            d.sizeDelta = new Vector2(6f, 6f);
            d.anchoredPosition = new Vector2(12f, 0f);
            RoundCircle(d);
            var text = TextObject("Text", root, label, 11, textColor, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = 11;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(26, 3), new Vector2(-8, -3));
        }
        else
        {
            var text = TextObject("Text", root, label, 11, textColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(6, 0), new Vector2(-6, 0));
        }
        var button = root.gameObject.AddComponent<Button>();
        button.interactable = enabled;
        button.onClick.AddListener(action);
    }

    // Small square glyph-only button (rewind/play/pause/etc.) for compact transport rows —
    // a League-of-Legends-style condensed playback bar, rather than AddButton's fixed
    // 118px-wide labeled buttons. Sized via LayoutElement only (no explicit sizeDelta), so a
    // parent HorizontalLayoutGroup with childControlWidth/Height=true can still stretch its
    // height to fill a thin row while keeping width fixed (see DrawReplayControlBar).
    private void AddIconButton(RectTransform parent, string glyph, UnityEngine.Events.UnityAction action, bool enabled = true, float width = 28f)
    {
        var root = PanelObject(glyph + " Icon Button", parent, enabled ? new Color32(34, 58, 78, 235) : new Color32(24, 34, 44, 170));
        var le = root.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width;
        Round(root);
        AddRoundedCardBorder(root, enabled ? MenuB : (Color)new Color32(50, 58, 74, 80), 1f);
        var text = TextObject("Glyph", root, glyph, 13, enabled ? Ink : (Color)new Color32(120, 130, 146, 160), TextAnchor.MiddleCenter, monoFont);
        text.fontStyle = FontStyle.Bold;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 8;
        text.resizeTextMaxSize = 13;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 0f), new Vector2(-2f, 0f));
        var button = root.gameObject.AddComponent<Button>();
        button.interactable = enabled;
        button.onClick.AddListener(action);
    }

    // Small text pill (mode/speed/desc toggles) — same idea as AddIconButton but for short
    // text instead of a single glyph, sized to fit the label rather than AddButton's fixed
    // 118px. Used by the condensed replay control bar's top strip.
    private void AddCompactPill(RectTransform parent, string label, UnityEngine.Events.UnityAction action, bool enabled = true)
    {
        float w = Mathf.Clamp(label.Length * 6.2f + 16f, 40f, 220f);
        var root = PanelObject(label + " Pill", parent, enabled ? new Color32(34, 58, 78, 235) : new Color32(24, 34, 44, 170));
        SetPreferred(root, new Vector2(w, 22f));
        root.sizeDelta = new Vector2(w, 22f);
        Round(root);
        AddRoundedCardBorder(root, enabled ? MenuB : (Color)new Color32(50, 58, 74, 80), 1f);
        var text = TextObject("Text", root, label, 9, enabled ? Ink : (Color)new Color32(120, 130, 146, 160), TextAnchor.MiddleCenter, monoFont);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var button = root.gameObject.AddComponent<Button>();
        button.interactable = enabled;
        button.onClick.AddListener(action);
    }

    // Thematic slim scrollbar (cyan rounded handle on a faint navy track) overlaid
    // on the right edge of a scroll viewport. Auto-hides when the content fits.
    private void AttachScrollbar(ScrollRect scroll)
    {
        if (scroll == null || scroll.viewport == null) return;
        var vp = scroll.viewport;

        var track = PanelObject("Scrollbar", vp, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.12f));
        track.anchorMin = new Vector2(1f, 0f);
        track.anchorMax = new Vector2(1f, 1f);
        track.pivot     = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(6f, -4f);
        track.anchoredPosition = new Vector2(-1f, 0f);
        Round(track);

        var slide = PanelObject("Sliding Area", track, new Color(0, 0, 0, 0));
        Stretch(slide, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));

        var handle = PanelObject("Handle", slide, new Color(Accent.r, Accent.g, Accent.b, 0.8f));
        Stretch(handle, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Round(handle);

        var sb = track.gameObject.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.handleRect = handle;
        sb.targetGraphic = handle.GetComponent<Image>();

        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void AddInfo(RectTransform parent, string message)
    {
        message ??= "";
        var text = TextObject("Info", parent, message, 10, Muted, TextAnchor.UpperLeft);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.lineSpacing = 0.92f;
        // No fixed/min width: let the vertical layout control the width (it force-expands to fill the
        // panel) so the text wraps to the real panel width instead of a 260px guess that clips.
        // Height auto-sizes from the wrapped Text's preferred height.
    }

    // Info text that shrinks (and finally truncates) with length, so even wordy card
    // texts fit in the action panel without scrolling — the full text is always readable
    // on the card preview itself.
    private void AddScaledInfo(RectTransform parent, string message)
    {
        message ??= "";
        int size = message.Length <= 140 ? 10 : message.Length <= 240 ? 9 : 8;
        if (message.Length > 340) message = message.Substring(0, 337).TrimEnd() + "…";
        var text = TextObject("Info", parent, message, size, Muted, TextAnchor.UpperLeft);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.lineSpacing = 0.9f;
    }

    private void AddCenteredText(RectTransform parent, string message)
    {
        var text = TextObject("Empty", parent, message, 13, Muted, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.72f), Vector2.zero, Vector2.zero);
    }

    // Per-(source card + timing) animated fill fraction, so the green/red sweep continues smoothly
    // across re-renders and across the "Then," continuation chain (EffectId changes; source+timing
    // stay stable). Keyed so only the currently-shown effect's phase is retained.
    private readonly Dictionary<string, float> _effectProgressShown = new Dictionary<string, float>();

    // The effect text written out under the glowing card, with resolved clauses filled GREEN and
    // skipped / criteria-not-met clauses filled RED, sweeping in TypeRacer-style (task B). Shown to
    // BOTH players — the ledger rides the networked state, so the watcher sees the same fill as the
    // deciding player. Pending text stays the neutral colour.
    private void AddEffectProgressText(RectTransform parent, PendingEffect effect)
    {
        string full = CleanEffectText(!string.IsNullOrEmpty(effect.OriginalText) ? effect.OriginalText : effect.Text);
        if (string.IsNullOrEmpty(full)) return;
        int size = full.Length <= 140 ? 10 : full.Length <= 240 ? 9 : 8;
        if (full.Length > 340) full = full.Substring(0, 337).TrimEnd() + "…";

        // Locate each resolved (green=1) / skipped (red=2) clause in the full text; both resolve
        // front-to-back, so sort by position and paint a per-character colour map.
        var located = new List<(int idx, int len, int code)>();
        if (effect.DoneParts != null)
            foreach (var d in effect.DoneParts) AddLocatedPart(full, d, 1, located);
        if (effect.SkippedParts != null)
            foreach (var sp in effect.SkippedParts) AddLocatedPart(full, sp, 2, located);
        located.Sort((a, b) => a.idx.CompareTo(b.idx));

        int[] colorIdx = new int[full.Length];
        int completedEnd = 0;
        foreach (var p in located)
        {
            for (int i = p.idx; i < p.idx + p.len && i < full.Length; i++) colorIdx[i] = p.code;
            if (p.idx + p.len > completedEnd) completedEnd = Mathf.Min(p.idx + p.len, full.Length);
        }
        float target = full.Length > 0 ? (float)completedEnd / full.Length : 0f;

        var text = TextObject("Progress", parent, full, size, Muted, TextAnchor.UpperLeft);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.lineSpacing = 0.9f;
        text.supportRichText = true;

        string key = (effect.SourceInstanceId ?? "") + "|" + (effect.Timing ?? "");
        // Keep only the current key so a card re-triggering the same timing later re-reveals fresh.
        if (_effectProgressShown.Count > 0)
        {
            var stale = new List<string>();
            foreach (var k in _effectProgressShown.Keys) if (k != key) stale.Add(k);
            foreach (var k in stale) _effectProgressShown.Remove(k);
        }
        float start = _effectProgressShown.TryGetValue(key, out var prev) ? Mathf.Min(prev, target) : 0f;
        _effectProgressShown[key] = start;

        var comp = text.gameObject.AddComponent<EffectProgressText>();
        comp.Init(text, full, colorIdx, target, start, v => _effectProgressShown[key] = v);
    }

    private void AddLocatedPart(string full, string rawPart, int code, List<(int idx, int len, int code)> into)
    {
        string part = CleanEffectText(rawPart);
        if (string.IsNullOrEmpty(part)) return;
        int idx = full.IndexOf(part, System.StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) into.Add((idx, part.Length, code));
    }

    // Strip leading timing tags (already in the panel header) and the boilerplate DON!!-return
    // reminder, and collapse whitespace — so the written-out text is clean and clause lookups line
    // up. Mirrors ResolveEffectLabel's cleaning (kept separate so it does not upper-case).
    // The single ability line containing "[Activate: Main]" (whole text if not found), so the
    // selected-card panel can write out just that ability.
    private static string ExtractActivateMainClause(string effect)
    {
        if (string.IsNullOrEmpty(effect)) return "";
        foreach (var line in effect.Split('\n'))
            if (line.IndexOf("[Activate: Main]", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return line;
        return effect;
    }

    private static string CleanEffectText(string text)
    {
        text = text ?? "";
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]*\]\s*/?\s*)+", "").Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"\(You may return the specified number of DON!! cards from your field to your DON!! deck\.?\)", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    // Drives the TypeRacer-style fill: a shown fraction eases toward the resolved fraction each
    // frame, and characters below the cutoff render in their clause colour (green = done, red =
    // skipped) while the rest stay the base colour. Re-seeded from GameManager's per-key store so
    // the sweep resumes smoothly when the panel is rebuilt.
    private sealed class EffectProgressText : MonoBehaviour
    {
        private const string Green = "<color=#4FD08A>";
        private const string Red   = "<color=#E65454>";
        private const float  Speed = 2.4f;   // fraction per second

        private Text label;
        private string raw;
        private int[] colorIdx;   // per char: 0 base, 1 green, 2 red
        private float target;
        private float shown;
        private System.Action<float> writeBack;

        public void Init(Text label, string raw, int[] colorIdx, float target, float start, System.Action<float> writeBack)
        {
            this.label = label; this.raw = raw ?? ""; this.colorIdx = colorIdx;
            this.target = Mathf.Clamp01(target);
            this.shown = Mathf.Clamp01(start);
            this.writeBack = writeBack;
            if (label != null) label.supportRichText = true;
            Apply();
        }

        private void Update()
        {
            if (Mathf.Approximately(shown, target)) return;
            shown = Mathf.MoveTowards(shown, target, Speed * Time.deltaTime);
            writeBack?.Invoke(shown);
            Apply();
        }

        private void Apply()
        {
            if (label == null || raw.Length == 0) return;
            int cut = Mathf.Clamp(Mathf.RoundToInt(shown * raw.Length), 0, raw.Length);
            var sb = new System.Text.StringBuilder(raw.Length + 32);
            int cur = 0;   // 0 base, 1 green, 2 red
            for (int i = 0; i < raw.Length; i++)
            {
                int c = (i < cut && colorIdx != null && i < colorIdx.Length) ? colorIdx[i] : 0;
                if (c != cur)
                {
                    if (cur != 0) sb.Append("</color>");
                    if (c == 1) sb.Append(Green);
                    else if (c == 2) sb.Append(Red);
                    cur = c;
                }
                sb.Append(raw[i]);
            }
            if (cur != 0) sb.Append("</color>");
            label.text = sb.ToString();
        }
    }

    private void AddEmptySlot(RectTransform parent, string label, Vector2 size)
    {
        var slot = PanelObject(string.IsNullOrEmpty(label) ? "Empty Slot" : label, parent, new Color32(10, 14, 22, 74));
        SetPreferred(slot, size);
        AddOutline(slot.gameObject, new Color32(80, 91, 112, 70), 0.6f);
        AddBoardCancel(slot);
        if (!string.IsNullOrEmpty(label))
        {
            var text = TextObject("Label", slot, label, 11, new Color32(153, 163, 181, 190), TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }
    }

    // Places one character cell occupying [xMin, xMin+widthFrac] of the fitted character row.
    // Cell is the transparent drop target; card is aspect-fitted inside. Empty cells read as an
    // open band (faint card-shaped hint, gold only where a card can drop).
    private void AddCharacterSlot(RectTransform parent, PlayerState player, string seat, int slotIndex, bool inverted, float xMin, float widthFrac)
    {
        var slot = PanelObject($"Character Slot {slotIndex + 1}", parent, new Color(0, 0, 0, 0));
        Stretch(slot, new Vector2(xMin, 0f), new Vector2(xMin + widthFrac, 1f), Vector2.zero, Vector2.zero);

        var drop = slot.gameObject.AddComponent<CharacterSlotDrop>();
        drop.Init(this, seat, slotIndex);

        var holder = new GameObject("Character Holder").AddComponent<RectTransform>();
        holder.SetParent(slot, false);
        FitCardAspect(slot, holder, 0.98f);

        var card = player.CharacterArea[slotIndex];
        var bg = PanelObject("Slot BG", holder, new Color(0, 0, 0, 0));
        Stretch(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        bg.GetComponent<Image>().raycastTarget = false;
        // Placement indicators only appear on the active player's board - they double as the
        // whose-turn cue, so the opponent's empty slots stay blank.
        if (card == null && seat == state.ActiveSeat)
        {
            // Subtle empty-slot box (matches the mock): a faint fill, cyan-tinted when you can play here.
            // NB: AddOutline uses useGraphicAlpha=false, which fills a solid quad - so we tint the fill
            // directly instead of outlining, to avoid the old loud gold block.
            bool canDrop = CanAcceptCharacterDrop(seat, slotIndex);
            bg.GetComponent<Image>().color = canDrop
                ? new Color(Accent.r, Accent.g, Accent.b, 0.035f)
                : new Color(1f, 1f, 1f, 0.0f);   // essentially no fill until you can play here
            // Very faded, finely-broken dashed outline, like the mock's empty character slots.
            AddDashedBorder(bg, canDrop
                ? new Color(Accent.r, Accent.g, Accent.b, 0.30f)
                : new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.16f));
        }

        if (card != null)
        {
            AddCard(holder, card, seat, true, Vector2.zero, true, inverted);

            // QoL: dim a summoning-sick Character (played this turn, no [Rush]) on the turn
            // player's board — a clear cue that it can't attack yet. A partial "Rush vs Characters"
            // (EB02-019 Zoro, when its condition holds and a legal Character target exists) can still
            // attack this turn, so it must NOT be dimmed — it should read as live/clickable.
            if (!isReplayMode && seat == state.ActiveSeat && !card.Rested
                && GameEngine.IsSummoningSick(state, card)
                && !CanAttackCharactersNow(card, seat))
            {
                // Official-app style: KEEP the card's colours but DARKEN it. A near-black veil at ~0.70
                // alpha is a pure multiply (card * ~0.30), so hue/saturation are preserved — it just dims,
                // rather than draining to greyscale.
                var dim = PanelObject("Summoning Sick Dim", holder, new Color(0.02f, 0.03f, 0.05f, 0.70f));
                Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Round(dim);
                dim.GetComponent<Image>().raycastTarget = false;
                dim.SetAsLastSibling();
            }

            // A [Blocker] shield, centred on and straddling the card's top edge (half on / half off).
            // Live blockers get a faint grey glow (like the soft card glow); a blocker whose ability
            // is CANCELLED (Limejuice's cannotBlock) or that faces an incoming UNBLOCKABLE attack gets
            // a red X and no glow instead. A fully effect-negated card (Teach) keeps its own "negated"
            // treatment and shows no shield here.
            if (!card.Rested && GameEngine.HasBlocker(state, card) && !GameEngine.IsEffectNegated(state, card))
            {
                bool blockDisabled = GameEngine.IsBlockCancelled(state, card)
                    || GameEngine.BlockBarredByCurrentAttack(state, card);
                const float shieldSize = 34f;
                // Put the shield on the CENTER-FACING edge of the card for both players: the near
                // player's cards straddle their top edge, the far (opponent/bot) player's cards
                // straddle their bottom edge — so every shield sits toward the middle of the board.
                float edgeY = seat != BottomSeat ? 0f : 1f;
                // Nudge the badge INWARD only enough that the ring glow doesn't cross the row divider into the
                // other player's region — but keep it riding HIGH on the card's edge (smaller offset ⇒ higher /
                // less art covered; raise toward 0.5 to tuck it fully onto the card).
                float inwardY = (edgeY < 0.5f ? 1f : -1f) * shieldSize * 0.12f;
                // Ring glow: steel grey at rest, GOLD while the shield is hovered (like the card hover glow).
                Color shieldGlowIdle  = new Color(0.62f, 0.66f, 0.71f, 0.90f);
                Color shieldGlowHover = new Color(0.98f, 0.74f, 0.28f, 0.95f);
                RawImage shieldGlow = null;

                // Ring glow that circularly HUGS the shield's rim (like the card rim glow hugs a card border)
                // — a thin ring, transparent at the core, not a solid square. Live blockers only.
                if (!blockDisabled)
                {
                    var sglow = new GameObject("Blocker Shield Glow").AddComponent<RectTransform>();
                    sglow.SetParent(holder, false);
                    sglow.anchorMin = sglow.anchorMax = new Vector2(0.5f, edgeY);
                    sglow.pivot = new Vector2(0.5f, 0.5f);
                    sglow.sizeDelta = new Vector2(shieldSize * 1.5f, shieldSize * 1.5f); // tight to the rim
                    sglow.anchoredPosition = new Vector2(0f, inwardY);
                    shieldGlow = sglow.gameObject.AddComponent<RawImage>();
                    shieldGlow.texture = GetRingGlowTexture(0.62f, 0.10f);       // thin ring hugging the shield rim
                    shieldGlow.color = shieldGlowIdle;                           // steel grey (turns gold on hover)
                    shieldGlow.raycastTarget = false;
                    sglow.SetAsLastSibling();
                }

                var art = LoadBlockerShieldSprite();
                var shield = PanelObject("Blocker Shield", holder,
                    art != null ? Color.white : new Color(0.16f, 0.52f, 0.85f, 0.95f));
                shield.anchorMin = shield.anchorMax = new Vector2(0.5f, edgeY);
                shield.pivot = new Vector2(0.5f, 0.5f);
                shield.sizeDelta = new Vector2(shieldSize, shieldSize);
                shield.anchoredPosition = new Vector2(0f, inwardY);   // tucked onto the card edge, not past it
                // Far (north) side: flip the shield vertically so its TOP points toward the middle of the
                // board (matching the near side) instead of pointing away.
                if (seat != BottomSeat) shield.localScale = new Vector3(1f, -1f, 1f);
                var simg = shield.GetComponent<Image>();
                // Hoverable + clickable: hover shows the blocker preview and turns the glow gold; a click
                // forwards to the card's click so the shield icon can also declare a block.
                simg.raycastTarget = true;
                shield.gameObject.AddComponent<BlockerShieldHover>().Init(this, shieldGlow, shieldGlowIdle, shieldGlowHover, card, seat);
                if (art != null)
                {
                    // Use the shield art directly (square, preserve aspect); no panel/glyph.
                    simg.sprite = art;
                    simg.preserveAspect = true;
                }
                else
                {
                    // Fallback until Assets/Resources/blocker_shield.png is present: drawn glyph badge.
                    Round(shield);
                    AddOutline(shield.gameObject, new Color(1f, 1f, 1f, 0.9f), 1f);
                    var sg = TextObject("Shield Glyph", shield, "⛨", 18, Color.white, TextAnchor.MiddleCenter, titleFont);
                    Stretch(sg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    sg.raycastTarget = false;
                }
                if (blockDisabled) simg.color = new Color(simg.color.r, simg.color.g, simg.color.b, simg.color.a * 0.55f);
                shield.SetAsLastSibling();

                // Red X over the shield when its block is cancelled / an unblockable attack is incoming.
                // Drawn as two crossed bars (no font-glyph dependency), each with a dark outline.
                if (blockDisabled)
                {
                    foreach (float ang in new[] { 45f, -45f })
                    {
                        var bar = PanelObject("Shield X Bar", shield, new Color(0.96f, 0.24f, 0.24f, 1f));
                        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                        bar.pivot = new Vector2(0.5f, 0.5f);
                        bar.sizeDelta = new Vector2(shieldSize * 0.92f, shieldSize * 0.17f);
                        bar.anchoredPosition = Vector2.zero;
                        bar.localRotation = Quaternion.Euler(0f, 0f, ang);
                        Round(bar);
                        bar.GetComponent<Image>().raycastTarget = false;
                        AddOutline(bar.gameObject, new Color(0f, 0f, 0f, 0.75f), 1f);
                    }
                }
            }
        }
    }

    // Blocker shield art: drop a PNG at Assets/Resources/blocker_shield.png and it replaces the
    // drawn glyph badge on active [Blocker]s. Cached (loaded once); works whether the PNG imports
    // as a Sprite or a plain Texture2D.
    private Sprite _blockerShieldSprite;
    private bool _blockerShieldLoaded;
    private Sprite LoadBlockerShieldSprite()
    {
        if (_blockerShieldLoaded) return _blockerShieldSprite;
        _blockerShieldLoaded = true;
        _blockerShieldSprite = Resources.Load<Sprite>("blocker_shield");
        if (_blockerShieldSprite == null)
        {
            var tex = Resources.Load<Texture2D>("blocker_shield");
            if (tex != null)
                _blockerShieldSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        return _blockerShieldSprite;
    }

    // `accent` is the BRIGHT badge colour; the fill is a dark tint of it and the text is the accent —
    // exactly the counter-step "+1000" pill language (dark green pill + bright green text).
    private RectTransform AddBadge(RectTransform parent, string label, Vector2 min, Vector2 max, Color accent, Font labelFont = null)
    {
        var badge = PanelObject("Badge", parent, new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, 0.92f));
        Stretch(badge, min, max, Vector2.zero, Vector2.zero);
        Round(badge);
        badge.GetComponent<Image>().raycastTarget = false;
        var text = TextObject("Badge Text", badge, label, 11, accent, TextAnchor.MiddleCenter, labelFont);
        text.fontStyle = FontStyle.Bold;
        text.raycastTarget = false;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, 0f), new Vector2(-3f, 0f));
        return badge;
    }

    // Opponent board cards render inside a holder rotated 180°, so text overlays (status tags, DON
    // count) inherit the flip and read upside-down. Counter-rotate the badge 180° about its own centre
    // so the text is upright for the viewer while the badge stays put. Call only for inverted cards.
    private void FlipBadgeUpright(RectTransform badge)
    {
        if (badge != null) badge.localRotation = Quaternion.Euler(0, 0, 180f);
    }

    private RectTransform PanelObject(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        return rt;
    }

    private void AddBoardCancel(RectTransform rect)
    {
        if (rect == null) return;
        rect.gameObject.AddComponent<BoardCancelClick>().Init(this);
    }

    private Image ImageObject(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = Color.white;
        return img;
    }

    // Corner radius as a fraction of card width, measured from the official card PNGs
    // (~21.5px on 600px wide = 3.58%).
    private const float RoundedCornerFraction = 0.0400f;
    private static Material _roundedCardBaseMaterial;

    private Material GetRoundedCardBaseMaterial()
    {
        if (_roundedCardBaseMaterial == null)
            _roundedCardBaseMaterial = new Material(Shader.Find("UI/RoundedCard"));
        return _roundedCardBaseMaterial;
    }

    private RectTransform RoundedCardVisual(string name, Transform parent, Sprite sprite, out Image image)
    {
        // Transparent root container (kept so callers can size/anchor/aspect-fit this).
        var root = PanelObject(name + " Clip", parent, new Color(0, 0, 0, 0));
        Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Card art clipped to a rounded rect by the UI/RoundedCard shader (smooth, anti-aliased
        // edge) instead of a binary stencil Mask. The art fills the rect (no preserveAspect) so
        // the rounded corners land exactly on the card edges; callers feed a card-aspect rect.
        image = ImageObject(name, root, sprite);
        image.raycastTarget = false;
        image.preserveAspect = false;
        image.type = Image.Type.Simple;
        image.material = GetRoundedCardBaseMaterial();
        Stretch(image.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        image.gameObject.AddComponent<RoundedCardClip>().Init(image, RoundedCornerFraction);
        return root;
    }

    // Feeds the UI/RoundedCard shader the card's pixel size + radius each frame, and gives each
    // card its own material instance so radii are independent. Anti-aliased, RectMask2D-safe.
    private sealed class RoundedCardClip : MonoBehaviour
    {
        private static readonly int SizeID = Shader.PropertyToID("_Size");
        private static readonly int RadiusID = Shader.PropertyToID("_Radius");
        private static readonly int SaturationID = Shader.PropertyToID("_Saturation");
        private Material mat;
        private RectTransform rt;
        private float fraction;

        public void Init(Graphic graphic, float radiusFraction)
        {
            rt = graphic.rectTransform;
            mat = Instantiate(graphic.material);   // per-card instance
            graphic.material = mat;
            fraction = radiusFraction;
        }

        // 1 = full colour, 0 = greyscale (used to grey out summoning-sick cards).
        public void SetSaturation(float s) { if (mat != null) mat.SetFloat(SaturationID, s); }

        private void Update()
        {
            if (mat == null || rt == null) return;
            Vector2 s = rt.rect.size;
            if (s.x <= 1f || s.y <= 1f) return;
            mat.SetVector(SizeID, new Vector4(s.x, s.y, 0f, 0f));
            mat.SetFloat(RadiusID, Mathf.Min(s.x, s.y) * fraction);
        }

        private void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }

    private Image AddRoundedCardImage(Transform parent, string name, Sprite sprite)
    {
        RoundedCardVisual(name, parent, sprite, out var image);
        return image;
    }

    private RectTransform AddMysticalCardOutline(RectTransform parent, bool strong)
    {
        if (parent == null) return null;
        var glowRoot = PanelObject("Hover Glow Root", parent, new Color(0, 0, 0, 0));
        Stretch(glowRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        glowRoot.SetAsFirstSibling();
        var group = glowRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Gold "ethereal mist" rim glow (shader: UI/CardHoverGlow). The shader now uses premultiplied
        // alpha, so it composites over bright zones instead of washing out - no backing halo needed.
        // A single expanded
        // quad; the shader confines the glow to a band hugging the card's rounded-rect
        // edge and animates flowing noise so the gold mist licks around the border.
        const float glowExpand = 0.28f;
        var rimGo = new GameObject("RimGlow");
        rimGo.transform.SetParent(glowRoot, false);
        var rimRt = rimGo.AddComponent<RectTransform>();
        rimRt.anchorMin = new Vector2(-glowExpand, -glowExpand);
        rimRt.anchorMax = new Vector2(1f + glowExpand, 1f + glowExpand);
        rimRt.offsetMin = rimRt.offsetMax = Vector2.zero;
        var rimImg = rimGo.AddComponent<RawImage>();
        rimImg.texture = Texture2D.whiteTexture;
        rimImg.raycastTarget = false;
        rimImg.material = CreateRimGlowMaterial();

        rimGo.AddComponent<CardRimGlow>().Init(rimImg, glowExpand, strong);
        return glowRoot;
    }

    private static Shader _rimGlowShader;
    private Material CreateRimGlowMaterial()
    {
        if (_rimGlowShader == null) _rimGlowShader = Shader.Find("UI/CardHoverGlow");
        var m = new Material(_rimGlowShader);
        // HDR gold; brighter than 1.0 so the additive glow has punch.
        m.SetColor("_GlowColor", new Color(1.00f, 0.59f, 0.10f, 1f) * 1.28f);  // gold mid leaning a touch more orange
        m.SetColor("_CoreColor", new Color(1.00f, 0.80f, 0.38f, 1f) * 1.12f);  // warm gold center
        m.SetColor("_OuterColor", new Color(0.82f, 0.29f, 0.04f, 1f) * 1.05f); // deeper amber-orange fringe
        m.SetFloat("_Speed", 0.55f);
        m.SetFloat("_NoiseScale", 3.0f);
        m.SetFloat("_Pulse", 0.22f);
        return m;
    }

    // GREEN "usable" rim glow - same UI/CardHoverGlow shader & CardRimGlow driver as the gold hover
    // glow (so the STYLE matches), but a deep emerald colour that flags a card the player can use
    // right now. Driven by the engine's real rules (GameEngine.IsPlayableNow / IsDeckLookSelectable),
    // so it only lights when the card is actually playable: enough active DON for its cost in main
    // phase, a valid counter during the counter step, or a card meeting the search criteria.
    private RectTransform AddUsableGlow(RectTransform parent)
    {
        if (parent == null) return null;
        var glowRoot = PanelObject("Usable Glow Root", parent, new Color(0, 0, 0, 0));
        Stretch(glowRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        glowRoot.SetAsFirstSibling();
        var group = glowRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        const float glowExpand = 0.28f;
        var rimGo = new GameObject("UsableRimGlow");
        rimGo.transform.SetParent(glowRoot, false);
        var rimRt = rimGo.AddComponent<RectTransform>();
        rimRt.anchorMin = new Vector2(-glowExpand, -glowExpand);
        rimRt.anchorMax = new Vector2(1f + glowExpand, 1f + glowExpand);
        rimRt.offsetMin = rimRt.offsetMax = Vector2.zero;
        var rimImg = rimGo.AddComponent<RawImage>();
        rimImg.texture = Texture2D.whiteTexture;
        rimImg.raycastTarget = false;
        rimImg.material = CreateUsableGlowMaterial();

        rimGo.AddComponent<CardRimGlow>().Init(rimImg, glowExpand, true);
        return glowRoot;
    }

    // Blue shimmering glow used behind a card that's waiting on effect resolution in the actions box.
    private RectTransform AddEffectGlow(RectTransform parent)
    {
        if (parent == null) return null;
        var glowRoot = PanelObject("Effect Glow Root", parent, new Color(0, 0, 0, 0));
        Stretch(glowRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        glowRoot.SetAsFirstSibling();
        var group = glowRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        const float glowExpand = 0.26f;
        var rimGo = new GameObject("EffectRimGlow");
        rimGo.transform.SetParent(glowRoot, false);
        var rimRt = rimGo.AddComponent<RectTransform>();
        rimRt.anchorMin = new Vector2(-glowExpand, -glowExpand);
        rimRt.anchorMax = new Vector2(1f + glowExpand, 1f + glowExpand);
        rimRt.offsetMin = rimRt.offsetMax = Vector2.zero;
        var rimImg = rimGo.AddComponent<RawImage>();
        rimImg.texture = Texture2D.whiteTexture;
        rimImg.raycastTarget = false;
        rimImg.material = CreateEffectGlowMaterial();

        rimGo.AddComponent<CardRimGlow>().Init(rimImg, glowExpand, true);
        return glowRoot;
    }

    // A fitted, semi-transparent card (with the blue shimmer) shown at the top of the actions box
    // while its effect waits to resolve. Resolve bubbles are added below it by the caller.
    private void AddEffectCardVisual(RectTransform body, string cardId)
    {
        var sprite = GetCardSprite(cardId);
        if (sprite == null) return;
        const float cardH = 205f;
        float cardW = cardH * 0.714f;

        var container = PanelObject("Effect Card", body, new Color(0, 0, 0, 0));
        var le = container.gameObject.AddComponent<LayoutElement>();
        // Extra height adds breathing room below the card before the text/bubbles begin.
        le.preferredHeight = cardH + 34f;

        var holder = new GameObject("Effect Card Holder").AddComponent<RectTransform>();
        holder.SetParent(container, false);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = new Vector2(cardW, cardH);
        holder.anchoredPosition = Vector2.zero;

        AddEffectGlow(holder);
        RoundedCardVisual("Effect Art", holder, sprite, out var img);
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0.52f);   // more transparent while pending
    }

    // "Cannot attack" flag: a deep-red ALPHA-blended soft halo behind the card. The additive rim
    // shader (green/gold glows) only ADDS light, so red washes to pink on the light mat; a normal-
    // blended halo darkens toward red instead, reading as an obvious dark-red ring around the card.
    // The opaque centre is hidden behind the card, leaving only the red halo around its edge.
    private RectTransform AddInvalidGlow(RectTransform parent)
    {
        if (parent == null) return null;
        var glowRoot = PanelObject("Invalid Glow Root", parent, new Color(0, 0, 0, 0));
        Stretch(glowRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        glowRoot.SetAsFirstSibling();
        var group = glowRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Three translucent red auras (alpha-blended so they DARKEN the light mat toward red instead of
        // washing to pink like the additive rim glow). Each is brightest right at the card edge and blooms
        // outward with a soft convex falloff, so together they read as a glowy red rim that matches the
        // radiance of the green/gold outlines. The opaque centres sit hidden behind the card.
        AddMistRawImage(glowRoot, "Invalid Aura Wide", GetGlowAuraTexture(0.30f, 1.9f), 0.30f, new Color(0.46f, 0.0f, 0.0f, 0.55f), 0f, 0f);
        AddMistRawImage(glowRoot, "Invalid Aura Mid", GetGlowAuraTexture(0.20f, 2.1f), 0.20f, new Color(0.70f, 0.02f, 0.02f, 0.80f), 0f, 0f);
        AddMistRawImage(glowRoot, "Invalid Aura Edge", GetGlowAuraTexture(0.11f, 2.4f), 0.11f, new Color(0.92f, 0.10f, 0.06f, 0.95f), 0f, 0f);
        return glowRoot;
    }

    private static Shader _usableGlowShader;
    private Material CreateUsableGlowMaterial()
    {
        if (_usableGlowShader == null) _usableGlowShader = Shader.Find("UI/CardHoverGlow");
        var m = new Material(_usableGlowShader);
        // Radiant emerald - mirrors the gold glow's HDR brightness & liveliness so it reads just as
        // rich, only green. (Additive shader: values >1 are what give the glow its punch.)
        m.SetColor("_GlowColor", new Color(0.12f, 0.95f, 0.32f, 1f) * 1.32f);   // vivid emerald mid
        m.SetColor("_CoreColor", new Color(0.65f, 1.18f, 0.62f, 1f) * 1.12f);   // luminous green-white core
        m.SetColor("_OuterColor", new Color(0.05f, 0.50f, 0.14f, 1f) * 1.06f);  // deep green fringe
        m.SetFloat("_Speed", 0.55f);
        m.SetFloat("_NoiseScale", 3.0f);
        m.SetFloat("_Pulse", 0.22f);
        return m;
    }

    private Material CreateEffectGlowMaterial()
    {
        if (_usableGlowShader == null) _usableGlowShader = Shader.Find("UI/CardHoverGlow");
        var m = new Material(_usableGlowShader);
        // Cobalt/cyan to match the client accent, with a stronger pulse so it reads as a "waiting" shimmer.
        m.SetColor("_GlowColor", new Color(0.18f, 0.66f, 0.92f, 1f) * 1.42f);   // accent-blue mid
        m.SetColor("_CoreColor", new Color(0.58f, 0.88f, 1.00f, 1f) * 1.16f);   // luminous blue-white core
        m.SetColor("_OuterColor", new Color(0.04f, 0.34f, 0.60f, 1f) * 1.06f);  // deep blue fringe
        m.SetFloat("_Speed", 0.7f);
        m.SetFloat("_NoiseScale", 3.0f);
        m.SetFloat("_Pulse", 0.38f);
        return m;
    }

    private static Shader _invalidGlowShader;
    private Material CreateInvalidGlowMaterial()
    {
        if (_invalidGlowShader == null) _invalidGlowShader = Shader.Find("UI/CardHoverGlow");
        var m = new Material(_invalidGlowShader);
        // Vivid red - "cannot be attacked" flag; mirrors the green glow's brightness, opposite hue.
        m.SetColor("_GlowColor", new Color(0.75f, 0.015f, 0.015f, 1f) * 1.70f);  // deep saturated red mid
        m.SetColor("_CoreColor", new Color(1.00f, 0.05f, 0.05f, 1f) * 1.50f);    // pure red core (no pink wash)
        m.SetColor("_OuterColor", new Color(0.22f, 0.0f, 0.0f, 1f) * 1.20f);     // near-black blood-red fringe
        m.SetFloat("_Speed", 0.55f);
        m.SetFloat("_NoiseScale", 3.0f);
        m.SetFloat("_Pulse", 0.22f);
        return m;
    }

    // Per-frame driver for the gold rim glow: feeds the shader exact pixel sizes so the
    // band lands on the card edge, and fades _Intensity in smoothly when shown.
    private sealed class CardRimGlow : MonoBehaviour
    {
        private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        private Material mat;
        private RectTransform rt;
        private float expand;
        private float target;
        private float current;

        public void Init(RawImage image, float expandFraction, bool strong)
        {
            rt = image.rectTransform;
            mat = image.material;   // per-instance material from CreateRimGlowMaterial
            expand = expandFraction;
            target = strong ? 1f : 0.82f;
            current = 0f;
            if (mat != null) mat.SetFloat(IntensityID, 0f);
        }

        private void Update()
        {
            if (mat == null || rt == null) return;
            Vector2 g = rt.rect.size;
            if (g.x > 1f && g.y > 1f)
            {
                Vector2 card = g / (1f + 2f * expand);
                float mn = Mathf.Min(card.x, card.y);
                mat.SetVector("_GlowSize", new Vector4(g.x, g.y, 0f, 0f));
                mat.SetVector("_CardSize", new Vector4(card.x, card.y, 0f, 0f));
                mat.SetFloat("_CornerPx", mn * 0.06f);
                mat.SetFloat("_BleedPx", mn * 0.05f);
                mat.SetFloat("_GlowWidthPx", mn * 0.075f);  // finite glow reach
                mat.SetFloat("_CoreWidthPx", Mathf.Max(1.5f, mn * 0.02f));
                mat.SetFloat("_WispPx", mn * 0.06f);         // tendril length
            }
            current = Mathf.MoveTowards(current, target, 6.5f * Time.unscaledDeltaTime);
            mat.SetFloat(IntensityID, current);
        }

        private void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }

    private void AddMistRawImage(RectTransform parent, string layerName, Texture2D tex,
        float anchorExpand, Color col, float uvOffsetX, float uvOffsetY)
    {
        var go = new GameObject(layerName);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(-anchorExpand, -anchorExpand);
        rt.anchorMax = new Vector2(1f + anchorExpand, 1f + anchorExpand);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.color = col;
        img.raycastTarget = false;
        img.uvRect = new Rect(uvOffsetX, uvOffsetY, 1f, 1f);
    }

    private void AddRoundedCardBorder(RectTransform parent, Color color, float thickness)
    {
        if (parent == null || color.a <= 0f || thickness <= 0f) return;
        var border = ImageObject("Rounded Card Border", parent, GetRoundedCardBorderSprite(thickness));
        border.color = color;
        border.raycastTarget = false;
        border.type = Image.Type.Sliced;
        Stretch(border.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private Sprite _roundedRectSprite;
    // A generic 9-sliced filled rounded rectangle, so any panel (pill, badge, bubble, plate) can be
    // rounded without distortion regardless of its aspect.
    private Sprite GetRoundedRectSprite()
    {
        if (_roundedRectSprite != null) return _roundedRectSprite;
        const int S = 24; const float r = 5f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Min(x + 0.5f, S - x - 0.5f);
                float cy = Mathf.Min(y + 0.5f, S - y - 0.5f);
                float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r + 0.75f - dist);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return _roundedRectSprite;
    }

    // Round an existing panel's Image (9-sliced fill), for the cobalt rounded-pill look.
    private void Round(RectTransform rt)
    {
        if (rt == null) return;
        var img = rt.GetComponent<Image>();
        if (img == null) return;
        img.sprite = GetRoundedRectSprite();
        img.type = Image.Type.Sliced;
    }

    private Sprite _circleSprite;
    private Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int S = 48; const float r = S / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _circleSprite;
    }

    // Make a small panel a true circle (count badges, avatar dots, bullet dots).
    private void RoundCircle(RectTransform rt)
    {
        if (rt == null) return;
        var img = rt.GetComponent<Image>();
        if (img == null) return;
        img.sprite = GetCircleSprite();
        img.type = Image.Type.Simple;
    }

    private Sprite _roundedRectSpriteBig;
    private Sprite GetRoundedRectSpriteBig()
    {
        if (_roundedRectSpriteBig != null) return _roundedRectSpriteBig;
        const int S = 44; const float r = 12f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Min(x + 0.5f, S - x - 0.5f);
                float cy = Mathf.Min(y + 0.5f, S - y - 0.5f);
                float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
                float a = Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _roundedRectSpriteBig = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return _roundedRectSpriteBig;
    }

    // Round a LARGE panel (END TURN, plates, info boxes) with a bigger radius than the small pills.
    private void RoundBig(RectTransform rt)
    {
        if (rt == null) return;
        var img = rt.GetComponent<Image>();
        if (img == null) return;
        img.sprite = GetRoundedRectSpriteBig();
        img.type = Image.Type.Sliced;
    }

    private Sprite _dashedBorderSprite;
    // A faded ROUNDED dashed outline (card aspect) for empty character slots: solid rounded corners,
    // dashes along the straight edges - matching the designer's mock.
    private Sprite GetDashedBorderSprite()
    {
        if (_dashedBorderSprite != null) return _dashedBorderSprite;
        const int W = 168, H = 234; const float th = 1.6f, radius = 16f; const int dash = 5, gap = 8;
        int period = dash + gap;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float outer = RoundedRectAlpha(x + 0.5f, y + 0.5f, W, H, radius);
                float inner = RoundedRectAlpha(x + 0.5f - th, y + 0.5f - th, W - th * 2f, H - th * 2f, Mathf.Max(0f, radius - th));
                float b = Mathf.Clamp01(outer * (1f - inner));   // hollow rounded border alpha

                // Dash the WHOLE outline, corners included (no solid corner arcs). Pick the dash axis
                // from whichever edge the pixel is nearest: dash along x near the top/bottom edges,
                // along y near the left/right edges; they swap across each corner's 45-degree diagonal.
                float fx = x + 0.5f, fy = y + 0.5f;
                float dTB = Mathf.Min(fy, H - fy);   // distance to nearest horizontal edge
                float dLR = Mathf.Min(fx, W - fx);   // distance to nearest vertical edge
                bool useX = dTB <= dLR;
                bool gapHere = useX ? ((x % period) >= dash) : ((y % period) >= dash);

                float a = gapHere ? 0f : b;
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _dashedBorderSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
        return _dashedBorderSprite;
    }

    private void AddDashedBorder(RectTransform parent, Color color)
    {
        var img = ImageObject("Dashed Border", parent, GetDashedBorderSprite());
        img.color = color;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        Stretch(img.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private Sprite GetRoundedCardMaskSprite()
    {
        if (roundedCardMaskSprite != null) return roundedCardMaskSprite;

        // High-res mask so it's always scaled DOWN to the card (smooth), never up (which
        // stair-steps the stencil edge). Drawn as Image.Type.Simple so the radius stays a
        // fixed fraction of the card on every card. radius/width = 18/512 = 3.5%, matching
        // the measured corner radius of the official card PNGs (~3.58% of width) so masked
        // cards round identically to natively-rounded ones.
        const int width = 512;
        const int height = 720;
        const float radius = 18f;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var px = Mathf.Min(x + 0.5f, width - x - 0.5f);
                var py = Mathf.Min(y + 0.5f, height - y - 0.5f);
                var dx = Mathf.Max(0f, radius - px);
                var dy = Mathf.Max(0f, radius - py);
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01(radius + 0.75f - distance);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        roundedCardMaskSprite = Sprite.Create(
            tex,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        return roundedCardMaskSprite;
    }

    private Sprite GetArrowHeadSprite()
    {
        if (arrowHeadSprite != null) return arrowHeadSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var clear = new Color32(255, 255, 255, 0);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var nx = (x + 0.5f) / size;
                var ny = (y + 0.5f) / size;
                var halfWidth = Mathf.Lerp(0.42f, 0.02f, ny);
                var inside = ny >= 0.08f && ny <= 0.96f && Mathf.Abs(nx - 0.5f) <= halfWidth;
                pixels[y * size + x] = inside ? Color.white : clear;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        arrowHeadSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return arrowHeadSprite;
    }

    // Tileable Perlin-noise mist texture for the card hover effect. Generated once at runtime.
    // Sampled at integer multiples of the frequency (0..4) so the texture wraps seamlessly —
    // Perlin's lattice repeats at integer boundaries, making RawImage.uvRect scrolling seam-free.
    private static Texture2D _mistTex;

    private static Texture2D GetMistTexture()
    {
        if (_mistTex != null) return _mistTex;
        const int S = 256;
        _mistTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        _mistTex.wrapMode = TextureWrapMode.Repeat;
        _mistTex.filterMode = FilterMode.Bilinear;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float nx = x / (float)S * 4f;
                float ny = y / (float)S * 4f;
                float v = Mathf.PerlinNoise(nx,          ny         ) * 0.50f
                        + Mathf.PerlinNoise(nx * 2f + 5f, ny * 2f + 3f) * 0.32f
                        + Mathf.PerlinNoise(nx * 4f + 1f, ny * 4f + 8f) * 0.18f;
                v = Mathf.Clamp01(v * 1.4f - 0.12f);
                v = Mathf.Pow(v, 1.6f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(v * 225f));
            }
        }
        _mistTex.SetPixels32(px);
        _mistTex.Apply(false);
        return _mistTex;
    }

    // Soft ambient glow texture — bright where the card silhouette is, smoothly fading to
    // transparent outward. The RawImage using this texture is anchorExpand × larger than the card
    // so the card art covers the bright center; only the outer halo ring is visible.
    // Horizontal alpha gradient (transparent at the left edge -> opaque at the right edge), white RGB.
    // Used for the tapered centre-seam segments; flip via uvRect for the mirrored side.
    private static Texture2D _hGradTex;

    private static Texture2D GetHGradientTexture()
    {
        if (_hGradTex != null) return _hGradTex;
        const int W = 96;
        _hGradTex = new Texture2D(W, 1, TextureFormat.RGBA32, false);
        _hGradTex.filterMode = FilterMode.Bilinear;
        _hGradTex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W];
        for (int i = 0; i < W; i++)
        {
            // Ease toward the bright end so the line reads stronger near the pill and fades softly out.
            float t = i / (float)(W - 1);
            float a = t * t;   // quadratic falloff
            px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        _hGradTex.SetPixels32(px);
        _hGradTex.Apply(false, true);
        return _hGradTex;
    }

    // Vertical alpha gradient (transparent at the bottom -> opaque at the top), white RGB. Used for the
    // vertical corner-bracket arms; flip via uvRect for the bottom corners.
    private static Texture2D _vGradTex;

    private static Texture2D GetVGradientTexture()
    {
        if (_vGradTex != null) return _vGradTex;
        const int H = 96;
        _vGradTex = new Texture2D(1, H, TextureFormat.RGBA32, false);
        _vGradTex.filterMode = FilterMode.Bilinear;
        _vGradTex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[H];
        for (int i = 0; i < H; i++)
        {
            float t = i / (float)(H - 1);
            float a = t * t;   // quadratic falloff, matching the horizontal gradient
            px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        _vGradTex.SetPixels32(px);
        _vGradTex.Apply(false, true);
        return _vGradTex;
    }

    private static Texture2D _softGlowTex;

    private static Texture2D GetSoftGlowTexture(float anchorExpand)
    {
        if (_softGlowTex != null) return _softGlowTex;
        const int W = 128, H = 128;
        _softGlowTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _softGlowTex.filterMode = FilterMode.Bilinear;
        _softGlowTex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        // The glow image is (1 + 2×expand) times the card size.
        // The card occupies [e, 1-e] in each texture axis where e = expand / (1 + 2×expand).
        float total = 1f + 2f * anchorExpand;
        float e = anchorExpand / total;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = (x + 0.5f) / W;
                float ny = (y + 0.5f) / H;
                // Distance from the card's inner rect boundary, normalised to [0..1].
                float dx = Mathf.Max(0f, e - nx, nx - (1f - e));
                float dy = Mathf.Max(0f, e - ny, ny - (1f - e));
                float dist = Mathf.Sqrt(dx * dx + dy * dy) / e;
                // SmoothStep: alpha = 1 at card edge, falls to 0 at the glow image perimeter.
                float a = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(dist));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        _softGlowTex.SetPixels32(px);
        _softGlowTex.Apply(false, true);
        return _softGlowTex;
    }

    private static readonly Dictionary<int, Texture2D> _glowAuraTexCache = new Dictionary<int, Texture2D>();
    // Soft-edged aura: fully opaque over the card interior (hidden behind the card) and falling off
    // with a convex (1-dist)^power curve through the border band, so it blooms outward like a glow
    // rather than ending in the hard panel edge GetSoftGlowTexture (smoothstep) produces.
    private static Texture2D GetGlowAuraTexture(float anchorExpand, float power)
    {
        int key = Mathf.RoundToInt(anchorExpand * 100f) * 1000 + Mathf.RoundToInt(power * 100f);
        if (_glowAuraTexCache.TryGetValue(key, out var cached) && cached != null) return cached;
        const int W = 128, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        float total = 1f + 2f * anchorExpand;
        float e = anchorExpand / total;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = (x + 0.5f) / W;
                float ny = (y + 0.5f) / H;
                float dx = Mathf.Max(0f, e - nx, nx - (1f - e));
                float dy = Mathf.Max(0f, e - ny, ny - (1f - e));
                float dist = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / e);
                float a = Mathf.Pow(1f - dist, power);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _glowAuraTexCache[key] = tex;
        return tex;
    }

    private static readonly Dictionary<int, Texture2D> _ringGlowTexCache = new Dictionary<int, Texture2D>();
    // Radial RING halo: alpha peaks at normalized radius `peak` (0 = center, 1 = mid-edge) and falls off
    // both inward and outward with a Gaussian of width `thickness`, transparent at the core. So it HUGS the
    // rim of a circular/badge element (like the card rim glow hugs a card border) instead of filling a
    // solid square behind it.
    private static Texture2D GetRingGlowTexture(float peak, float thickness)
    {
        int key = Mathf.RoundToInt(peak * 1000f) * 1000 + Mathf.RoundToInt(thickness * 1000f);
        if (_ringGlowTexCache.TryGetValue(key, out var cached) && cached != null) return cached;
        const int W = 128, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        float twoTSq = 2f * thickness * thickness;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = (x + 0.5f) / W - 0.5f;
                float ny = (y + 0.5f) / H - 0.5f;
                float r = Mathf.Sqrt(nx * nx + ny * ny) * 2f;   // 0 at center, 1 at mid-edge
                float d = r - peak;
                float a = Mathf.Clamp01(Mathf.Exp(-(d * d) / twoTSq));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _ringGlowTexCache[key] = tex;
        return tex;
    }

    private static readonly Dictionary<int, Texture2D> _softHaloTexCache = new Dictionary<int, Texture2D>();
    // Card-shaped halo with a GAUSSIAN falloff. Unlike GetGlowAuraTexture's (1-dist)^power curve - which
    // is fairly uniform then ends at a contour the eye reads as an "edge" - a Gaussian decays smoothly
    // and asymptotically to ~0, so the halo melts into the background with no perceptible boundary.
    private static Texture2D GetSoftHaloTexture(float anchorExpand, float sigma)
    {
        int key = Mathf.RoundToInt(anchorExpand * 100f) * 1000 + Mathf.RoundToInt(sigma * 1000f);
        if (_softHaloTexCache.TryGetValue(key, out var cached) && cached != null) return cached;
        const int W = 128, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        float total = 1f + 2f * anchorExpand;
        float e = anchorExpand / total;
        float twoSigSq = 2f * sigma * sigma;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = (x + 0.5f) / W;
                float ny = (y + 0.5f) / H;
                float dx = Mathf.Max(0f, e - nx, nx - (1f - e));
                float dy = Mathf.Max(0f, e - ny, ny - (1f - e));
                float d = Mathf.Sqrt(dx * dx + dy * dy) / e;
                float a = Mathf.Exp(-(d * d) / twoSigSq);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _softHaloTexCache[key] = tex;
        return tex;
    }

    private Sprite GetRoundedCardBorderSprite(float thickness)
    {
        var key = Mathf.Clamp(Mathf.RoundToInt(thickness * 10f), 1, 80);
        if (roundedCardBorderSprites.TryGetValue(key, out var cached)) return cached;

        const int width = 64;
        const int height = 90;
        const float radius = 6.5f;
        var borderThickness = Mathf.Clamp(key / 10f, 0.75f, 8f);
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var outer = RoundedRectAlpha(x + 0.5f, y + 0.5f, width, height, radius);
                var inner = RoundedRectAlpha(
                    x + 0.5f - borderThickness,
                    y + 0.5f - borderThickness,
                    width - borderThickness * 2f,
                    height - borderThickness * 2f,
                    Mathf.Max(0f, radius - borderThickness));
                var alpha = Mathf.Clamp01(outer * (1f - inner));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        var sprite = Sprite.Create(
            tex,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        roundedCardBorderSprites[key] = sprite;
        return sprite;
    }

    private static float RoundedRectAlpha(float x, float y, float width, float height, float radius)
    {
        if (width <= 0f || height <= 0f) return 0f;
        var px = Mathf.Min(x, width - x);
        var py = Mathf.Min(y, height - y);
        var dx = Mathf.Max(0f, radius - px);
        var dy = Mathf.Max(0f, radius - py);
        var distance = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius + 0.75f - distance);
    }

    private Text TextObject(string name, Transform parent, string value, int size, Color color, TextAnchor alignment, Font fontOverride = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var text = go.AddComponent<Text>();
        text.font = fontOverride != null ? fontOverride : font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private void AddOutline(GameObject go, Color color, float distance)
    {
        var outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
        // Several panels this is attached to (e.g. cardBody's "Card Face") are themselves
        // fully transparent backgrounds; Outline's default useGraphicAlpha multiplies its own
        // rendered alpha by the host graphic's alpha, which would make it invisible at alpha 0.
        outline.useGraphicAlpha = false;
    }

    private void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // Real card/DON art is ~600x838 (width/height ~0.714), same ratio the hand fan already sizes
    // to. Zones like Life/Deck/DON Deck/Trash/Cost Area used to Stretch the card holder to an
    // arbitrary fraction of the zone's own (mismatched) shape, then preserveAspect-fit the art
    // inside it - leaving dead space and a wrong-shaped hover outline around the actual card.
    // This sizes the holder itself to the correct aspect, centered in the available area, so the
    // holder's bounds (and thus its outline/hover) match the visible card exactly.
    private const float CardAspect = 1f / 1.40f;

    private void FitCardAspect(RectTransform parent, RectTransform holder, float fillFrac = 0.92f, bool uniform = true)
    {
        float cardW, cardH;
        if (uniform && boardCardSize.x > 0f)
        {
            cardW = boardCardSize.x;
            cardH = boardCardSize.y;
            var maxW = parent.rect.width * fillFrac;
            var maxH = parent.rect.height * fillFrac;
            if (maxW > 1f && cardW > maxW) { cardW = maxW; cardH = cardW / CardAspect; }
            if (maxH > 1f && cardH > maxH) { cardH = maxH; cardW = cardH * CardAspect; }
        }
        else
        {
            var availW = parent.rect.width * fillFrac;
            var availH = parent.rect.height * fillFrac;
            cardW = Mathf.Min(availW, availH * CardAspect);
            cardH = cardW / CardAspect;
        }
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = new Vector2(cardW, cardH);
        holder.anchoredPosition = Vector2.zero;
    }

    private void SetPreferred(RectTransform rt, Vector2 size)
    {
        if (size == Vector2.zero) return;
        var layout = rt.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = size.x;
        layout.preferredHeight = size.y;
        layout.minWidth = size.x;
        layout.minHeight = size.y;
    }

    private void Clear(RectTransform root)
    {
        if (root == null) return;
        // Destroy() is deferred to end-of-frame, so a rebuilt row (e.g. the phase-pill tracker) would
        // render on top of the OLD one for a frame — showing two active pills. SetActive(false) hides
        // the old children immediately, before the rebuild renders.
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var go = root.GetChild(i).gameObject;
            go.SetActive(false);
            Destroy(go);
        }
    }

    private void ClearDragGhosts()
    {
        if (canvas == null) return;
        for (int i = canvas.transform.childCount - 1; i >= 0; i--)
        {
            var child = canvas.transform.GetChild(i);
            if (child.name == "Dragging Card" ||
                child.name == "Drag Arrow" ||
                child.name == "Attack Drag Arrow" ||
                child.name == "Selected DON Drag Ghost" ||
                child.name == "Deck Look Drag Ghost")
                Destroy(child.gameObject);
        }
    }

    private void ApplyRectPose(RectTransform rect, Vector2 position, Quaternion rotation, Vector3 scale)
    {
        if (rect == null) return;
        var tween = rect.GetComponent<RectTransformSlideTween>();
        if (tween != null)
        {
            tween.enabled = false;
            Destroy(tween);
        }
        rect.anchoredPosition = position;
        rect.localRotation = rotation;
        rect.localScale = scale;
    }

    private void AnimateRectPose(RectTransform rect, Vector2 position, Quaternion rotation, Vector3 scale, float duration)
    {
        if (rect == null) return;
        var tween = rect.GetComponent<RectTransformSlideTween>();
        if (tween == null) tween = rect.gameObject.AddComponent<RectTransformSlideTween>();
        tween.Init(position, rotation, scale, duration);
    }

    private Sprite GetCardSprite(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        if (spriteCache.TryGetValue(cardId, out var cached)) return cached;

        if (CardAssets.UseCdn)
        {
            // No sprite this frame; fetch in the background and re-render on arrival.
            KickCardSpriteLoad(cardId);
            return null;
        }

        var tex = LoadFront(cardId);
        if (tex == null) return null;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        spriteCache[cardId] = sprite;
        return sprite;
    }

    // ── WebGL/CDN async art pipeline ─────────────────────────────────────────
    // One in-flight fetch per key; results (including misses, cached as null so
    // they are never re-requested) land in the same caches the sync path uses,
    // then a single coalesced Render() picks them up (see Update).
    private readonly HashSet<string> _artPending = new HashSet<string>();
    private bool _artRefreshQueued;

    private async void KickCardSpriteLoad(string cardId)
    {
        if (!_artPending.Add(cardId)) return;
        try
        {
            while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
            var rel = CardAssets.FirstExisting(CardAssets.ArtCandidates(cardId));
            var tex = rel != null ? await CardAssets.LoadTextureAsync(rel) : null;
            Sprite sprite = null;
            if (tex != null)
            {
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 8;
                tex.mipMapBias = -0.75f;
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            texCache[cardId] = tex;
            spriteCache[cardId] = sprite;
            if (sprite != null) _artRefreshQueued = true;
        }
        finally { _artPending.Remove(cardId); }
    }

    // Shared-texture fetch for backs/don/icons: writes into `assign` via the
    // supplied setter, queues the coalesced re-render.
    private async void KickSharedTexLoad(string relPath, System.Action<Texture2D> assign)
    {
        if (!_artPending.Add(relPath)) return;
        try
        {
            while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
            var tex = CardAssets.Exists(relPath) ? await CardAssets.LoadTextureAsync(relPath) : null;
            if (tex != null)
            {
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 8;
                tex.mipMapBias = -0.75f;
                assign(tex);
                _artRefreshQueued = true;
            }
        }
        finally { _artPending.Remove(relPath); }
    }

    private Sprite GetBackSprite()
    {
        if (backSprite != null) return backSprite;
        if (backTex == null)
        {
            if (CardAssets.UseCdn)
            {
                KickSharedTexLoad("optcg_card_back.jpg", t => backTex = t);
                return null;
            }
            var projectPath = CardAssets.LocalPath("optcg_card_back.jpg");
            backTex = LoadFile(File.Exists(projectPath) ? projectPath : BackImageFallbackPath);
            if (backTex == null) backTex = LoadFile(Path.Combine(AssetBase, "backs", "CardBackRegular.png"));
        }
        if (backTex == null) return null;
        backSprite = Sprite.Create(backTex, new Rect(0, 0, backTex.width, backTex.height), new Vector2(0.5f, 0.5f), 100f);
        return backSprite;
    }

    private Sprite GetDonSprite()
    {
        if (donSprite != null) return donSprite;
        if (donTex == null)
        {
            if (CardAssets.UseCdn)
            {
                KickSharedTexLoad("donCardAltArt.png", t => donTex = t);
                return null;
            }
            var projectPath = CardAssets.LocalPath("donCardAltArt.png");
            donTex = LoadFile(File.Exists(projectPath) ? projectPath : DonImageFallbackPath);
        }
        if (donTex == null) return null;
        donSprite = Sprite.Create(donTex, new Rect(0, 0, donTex.width, donTex.height), new Vector2(0.5f, 0.5f), 100f);
        return donSprite;
    }

    // White/teal ONE PIECE DON!! card back (from the reference mat) for the DON!! deck pile.
    private Sprite GetDonBackSprite()
    {
        if (donBackSprite != null) return donBackSprite;
        if (donBackTex == null)
        {
            if (CardAssets.UseCdn)
            {
                if (CardAssets.Ready && !CardAssets.Exists("CardBackDon.png")) return GetBackSprite();
                KickSharedTexLoad("CardBackDon.png", t => donBackTex = t);
                return GetBackSprite();   // regular back until (unless) the DON back arrives
            }
            var projectPath = CardAssets.LocalPath("CardBackDon.png");
            if (!File.Exists(projectPath)) return GetBackSprite();
            donBackTex = LoadFile(projectPath);
            if (donBackTex == null) return GetBackSprite();
        }
        donBackSprite = Sprite.Create(donBackTex, new Rect(0, 0, donBackTex.width, donBackTex.height), new Vector2(0.5f, 0.5f), 100f);
        return donBackSprite;
    }

    private Texture2D LoadFront(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        if (texCache.TryGetValue(cardId, out var cached)) return cached;

        var safeId = SafePathPart(cardId.Trim());
        if (string.IsNullOrWhiteSpace(safeId)) return null;
        string set = safeId.Contains("-") ? safeId.Split('-')[0] : "";

        // Desktop/editor path only — the CDN build resolves art in KickCardSpriteLoad.
        var candidates = new List<string>();
        foreach (var rel in CardAssets.ArtCandidates(safeId))
            AddPathCandidate(candidates, CardAssets.LocalPath(rel));
        AddPathCandidate(candidates, AssetBase, set, safeId + "_small.jpg");

        Texture2D tex = null;
        foreach (var path in candidates)
        {
            tex = LoadFile(path);
            if (tex != null) break;
        }
        texCache[cardId] = tex;
        return tex;
    }

    // Loads the One Piece hex color-identity icon for a leader's color string (e.g. "Red/Green").
    // Filenames in StreamingAssets/Cards/ColorIcons use underscores instead of slashes (Red_Green.png).
    private Texture2D LoadColorIcon(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var key = color.Trim();
        if (colorIconCache.TryGetValue(key, out var cached)) return cached;

        var fileName = SafePathPart(key.Replace('/', '_').Replace(' ', '_')) + ".png";

        if (CardAssets.UseCdn)
        {
            KickSharedTexLoad($"ColorIcons/{fileName}", t => colorIconCache[key] = t);
            return null;   // icon pops in on the coalesced re-render
        }

        var candidates = new List<string>();
        AddPathCandidate(candidates, CardAssets.LocalPath($"ColorIcons/{fileName}"));

        Texture2D tex = null;
        foreach (var path in candidates)
        {
            tex = LoadFile(path);
            if (tex != null) break;
        }
        colorIconCache[key] = tex;
        return tex;
    }

    // Bakes a cyan rim directly into a copy of the icon: any transparent texel within `radius` of an
    // opaque one becomes the outline color. Doing it in one texture (vs. a second scaled graphic)
    // guarantees the rim is perfectly concentric - no subpixel drift from pixel-perfect snapping.
    private Texture2D GetIconWithOutline(Texture2D src, Color outline)
    {
        if (src == null) return null;
        if (iconOutlinedCache.TryGetValue(src, out var cached)) return cached;

        Texture2D outTex = null;
        try
        {
            int w = src.width, h = src.height;
            var sp = src.GetPixels32();
            var dst = new Color32[sp.Length];
            Color32 oc = outline;
            // ~2px rim at the 30px display size: 128/30 ≈ 4.3 texels/px, so ~9 texels.
            int radius = Mathf.Max(1, Mathf.RoundToInt(w / 128f * 9f));
            int r2 = radius * radius;
            const byte cut = 60;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (sp[i].a > cut) { dst[i] = sp[i]; continue; }
                    bool near = false;
                    for (int dy = -radius; dy <= radius && !near; dy++)
                    {
                        int ny = y + dy; if (ny < 0 || ny >= h) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx; if (nx < 0 || nx >= w) continue;
                            if (dx * dx + dy * dy <= r2 && sp[ny * w + nx].a > cut) { near = true; break; }
                        }
                    }
                    dst[i] = near ? oc : sp[i];
                }
            }
            outTex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            outTex.SetPixels32(dst);
            outTex.filterMode = FilterMode.Trilinear;
            outTex.anisoLevel = 8;
            outTex.Apply(true, false);
        }
        catch { outTex = null; }
        iconOutlinedCache[src] = outTex;
        return outTex;
    }

    private static void AddPathCandidate(List<string> candidates, params string[] parts)
    {
        try
        {
            candidates.Add(Path.Combine(parts));
        }
        catch (System.ArgumentException)
        {
            // Card ids from outside data should never stop the board from rendering.
        }
    }

    private static string SafePathPart(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Where(c => c != '/' && c != '\\' && !invalid.Contains(c)).ToArray();
        return new string(chars);
    }

    private static Texture2D LoadFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // Create WITH a mip chain so LoadImage generates mipmaps. Without mips, a large card
                // texture drawn small on the board aliases badly (shimmery, jagged text). Trilinear +
                // anisotropic filtering keeps it crisp when downscaled.
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                tex.LoadImage(File.ReadAllBytes(path), false);
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 8;
                // Bias sampling toward the sharper (larger) mip so cards stay crisp instead of mushy.
                // Mipmaps kill the jagged aliasing; the negative bias claws back the lost sharpness.
                tex.mipMapBias = -0.75f;
                tex.Apply(true, false);
                return tex;
            }
        }
        catch { }
        return null;
    }

    private static string StepLabel(string step)
    {
        switch (step)
        {
            case "block": return "Block Step";
            case "counter": return "Counter Step";
            case "damage": return "Damage Step";
            case "trigger": return "Trigger Step";
            default: return "Battle";
        }
    }

    private sealed class CharacterSlotDrop : MonoBehaviour, IDropHandler
    {
        private GameManager manager;
        private string seat;
        private int slotIndex;

        public string Seat => seat;
        public int SlotIndex => slotIndex;
        public RectTransform TargetRect => transform as RectTransform;

        public void Init(GameManager owner, string playerSeat, int characterSlot)
        {
            manager = owner;
            seat = playerSeat;
            slotIndex = characterSlot;
        }

        public void OnDrop(PointerEventData eventData)
        {
            var drag = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CardDrag>() : null;
            if (drag == null || drag.HandSeat != seat) return;
            manager.PlayCharacterInSlot(drag.Card, drag.HandSeat, slotIndex);
        }
    }

    private sealed class StageDrop : MonoBehaviour, IDropHandler
    {
        private GameManager manager;
        private string seat;

        public string Seat => seat;
        public RectTransform TargetRect => transform as RectTransform;

        public void Init(GameManager owner, string playerSeat)
        {
            manager = owner;
            seat = playerSeat;
        }

        public void OnDrop(PointerEventData eventData)
        {
            var drag = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CardDrag>() : null;
            if (drag == null || drag.HandSeat != seat) return;
            manager.PlayStage(drag.Card, drag.HandSeat);
        }
    }

    private sealed class EventDrop : MonoBehaviour
    {
        private GameManager manager;
        private string seat;

        public string Seat => seat;
        public RectTransform TargetRect => transform as RectTransform;

        public void Init(GameManager owner, string playerSeat)
        {
            manager = owner;
            seat = playerSeat;
        }
    }

    private sealed class RectTransformSlideTween : MonoBehaviour
    {
        private RectTransform rect;
        private Vector2 startPosition;
        private Vector2 targetPosition;
        private Quaternion startRotation;
        private Quaternion targetRotation;
        private Vector3 startScale;
        private Vector3 targetScale;
        private float duration;
        private float elapsed;

        public void Init(Vector2 position, Quaternion rotation, Vector3 scale, float seconds)
        {
            rect = transform as RectTransform;
            if (rect == null) { Destroy(this); return; }
            startPosition = rect.anchoredPosition;
            targetPosition = position;
            startRotation = rect.localRotation;
            targetRotation = rotation;
            startScale = rect.localScale;
            targetScale = scale;
            duration = Mathf.Max(0.01f, seconds);
            elapsed = 0f;
        }

        private void Update()
        {
            if (rect == null) { Destroy(this); return; }
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var eased = t * t * (3f - 2f * t);
            rect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, eased);
            rect.localRotation = Quaternion.Slerp(startRotation, targetRotation, eased);
            rect.localScale = Vector3.Lerp(startScale, targetScale, eased);
            if (t >= 1f) Destroy(this);
        }
    }

    private sealed class BoardCancelClick : MonoBehaviour, IPointerClickHandler
    {
        private GameManager manager;

        public void Init(GameManager owner)
        {
            manager = owner;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left ||
                eventData.button == PointerEventData.InputButton.Right)
                manager.CancelDonSelectionFromBoardClick();
        }
    }

    private sealed class DonAttachTarget : MonoBehaviour
    {
        private GameManager manager;
        private string seat;
        private CardInstance card;

        public string Seat => seat;
        public CardInstance Card => card;

        public void Init(GameManager owner, string playerSeat, CardInstance targetCard)
        {
            manager = owner;
            seat = playerSeat;
            card = targetCard;
        }

        public bool AttachSelectedDon()
        {
            return manager != null && manager.AttachSelectedDonTo(seat, card);
        }
    }

    private sealed class DonSelector : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GameManager manager;
        private Canvas ownerCanvas;
        private string seat;
        private string instanceId;
        private int index;
        private GameObject ghost;

        public void Init(GameManager owner, Canvas canvas, string playerSeat, string donInstanceId, int donIndex)
        {
            manager = owner;
            ownerCanvas = canvas;
            seat = playerSeat;
            instanceId = donInstanceId;
            index = donIndex;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) { manager.RightClickDon(seat, instanceId); return; }
            if (eventData.button != PointerEventData.InputButton.Left) return;
            manager.SelectDon(seat, instanceId, index, IsShiftPressed());
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            manager.EnsureDonSelectedForDrag(seat, instanceId, index);
            CreateGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            MoveGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            DestroyGhost();

            var targetGo = eventData.pointerCurrentRaycast.gameObject;
            var groupTag = targetGo != null ? targetGo.GetComponentInParent<DonGroupTag>() : null;
            if (groupTag != null) { manager.MoveDonToGroup(seat, instanceId, groupTag.GroupIndex); return; }
            var target = targetGo != null ? targetGo.GetComponentInParent<DonAttachTarget>() : null;
            if (target != null) target.AttachSelectedDon();
        }

        private void OnDisable()
        {
            DestroyGhost();
        }

        private void OnDestroy()
        {
            DestroyGhost();
        }

        private void CreateGhost(PointerEventData eventData)
        {
            if (ownerCanvas == null || manager == null) return;
            ghost = new GameObject("Selected DON Drag Ghost");
            ghost.transform.SetParent(ownerCanvas.transform, false);
            ghost.transform.SetAsLastSibling();
            var rect = ghost.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(76f, 106f);
            var group = ghost.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var card = manager.PanelObject("DON Ghost Card", rect, new Color(0, 0, 0, 0));
            manager.Stretch(card, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            manager.AddDonCardVisual(card, false, true);
            manager.AddMysticalCardOutline(card, true);

            int count = manager.selectedDonIds.Count;
            if (count > 1)
                manager.AddBadge(card, "x" + count, new Vector2(0.60f, 0.78f), new Vector2(1.02f, 1.00f), new Color32(24, 33, 46, 240));

            MoveGhost(eventData);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (ghost != null) ghost.transform.position = eventData.position;
        }

        private void DestroyGhost()
        {
            if (ghost != null) Destroy(ghost);
            ghost = null;
        }

        private static bool IsShiftPressed()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }

    // Marks a cost-area DON with its group index so a dragged DON can be dropped onto it to move groups.
    private sealed class DonGroupTag : MonoBehaviour { public int GroupIndex; }

    private void SetHandDropRaycastActive(string seat, bool active)
    {
        var img = seat == "south" ? southHandPanelImage : northHandPanelImage;
        if (img != null) img.raycastTarget = active;
    }

    private sealed class HandDrop : MonoBehaviour
    {
        private GameManager manager;
        private string seat;

        public string Seat => seat;
        public RectTransform TargetRect => transform as RectTransform;

        public void Init(GameManager owner, string playerSeat)
        {
            manager = owner;
            seat = playerSeat;
        }
    }

    private sealed class CardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GameManager manager;
        private Canvas ownerCanvas;
        private CardInstance card;
        private string handSeat;
        private GameObject ghost;
        private GameObject arrowRoot;
        private RectTransform sourceRect;
        private bool inverted;

        // In-hand live reorder state. While this is active there's no ghost/arrow - the real
        // card itself floats above the fan at a fixed lift, and the OTHER cards slide live to
        // show the resulting order. Dragging out of the hand bounds switches to the existing
        // ghost+arrow "play this card" behavior instead.
        private bool inHandReorder;
        private List<CardInstance> previewOrder;
        private int originalIndex;
        private int previewIndex;
        private bool previewOrderChangedThisFrame;
        private RectTransform liftHolder;
        private Vector2 homeAnchoredPosition;
        private Quaternion homeRotation;
        private int homeSiblingIndex;
        private CanvasGroup liftGroup;
        private bool sourceHiddenForGhost;

        public CardInstance Card => card;
        public string HandSeat => handSeat;

        public void Init(GameManager owner, Canvas canvas, CardInstance cardInstance, string seat, bool invertedHand = false)
        {
            manager = owner;
            ownerCanvas = canvas;
            card = cardInstance;
            handSeat = seat;
            inverted = invertedHand;
            sourceRect = GetComponent<RectTransform>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!manager.CanStartHandDrag(card, handSeat)) return;
            var hover = GetComponent<CardHover>();
            if (hover != null) hover.ResetVisual();
            manager.HidePreview();
            manager.HideHandHoverPreview();
            manager.SetHandDropRaycastActive(handSeat, true);
            manager.isDraggingHandCard = true;

            // cardBody (this) -> root (transform.parent) -> "Fanned Hand Card" holder.
            liftHolder = transform.parent != null ? transform.parent.parent as RectTransform : null;
            inHandReorder = liftHolder != null && manager.GetHandRow(handSeat) != null;

            if (inHandReorder)
            {
                previewOrder = manager.GetHandSnapshot(handSeat);
                originalIndex = previewOrder.FindIndex(c => c.InstanceId == card.InstanceId);
                previewIndex = originalIndex;
                homeAnchoredPosition = liftHolder.anchoredPosition;
                homeRotation = liftHolder.localRotation;
                homeSiblingIndex = liftHolder.GetSiblingIndex();
                liftHolder.SetAsLastSibling();
                // The lifted card stays fully rendered (no separate ghost) but must not block
                // raycasts itself - otherwise it's always the topmost hit under the cursor,
                // which breaks both StillWithinHand's detection and any drop target underneath
                // (e.g. a character slot) once the card is dragged out over it.
                liftGroup = liftHolder.gameObject.GetComponent<CanvasGroup>();
                if (liftGroup == null) liftGroup = liftHolder.gameObject.AddComponent<CanvasGroup>();
                liftGroup.alpha = 1f;
                liftGroup.blocksRaycasts = false;
                SnapHeldCardToPreviewSlot();
                return;
            }

            CreateGhostAndArrow(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (inHandReorder)
            {
                bool within = StillWithinHand(eventData);
                if (within)
                {
                    UpdateLivePreview(eventData);
                    SnapHeldCardToPreviewSlot();
                    // Stream the live reorder so the opponent sees this card lift and slide in our hand.
                    manager.presenceHandDragFrom = originalIndex;
                    manager.presenceHandDragTo = previewIndex;
                }
                else
                {
                    ExitInHandReorder(true);
                    CreateGhostAndArrow(eventData);
                }
                return;
            }
            MoveGhost(eventData);
            UpdateArrow(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            manager.isDraggingHandCard = false;
            manager.presenceHandDragFrom = manager.presenceHandDragTo = -1;   // stop streaming the reorder

            if (inHandReorder)
            {
                manager.SetHandDropRaycastActive(handSeat, false);
                inHandReorder = false;
                if (liftGroup != null) liftGroup.blocksRaycasts = true;
                manager.Dispatch(new GameCommand { Type = "reorderHand", Seat = handSeat, InstanceId = card.InstanceId, SlotIndex = EngineHandTargetIndex(previewIndex) });
                previewOrder = null;
                liftHolder = null;
                liftGroup = null;
                sourceHiddenForGhost = false;
                return;
            }

            // Event cards: dragging up out of the hand activates them. Accept either a release over the
            // half's EventDrop target OR (fallback) any release outside the hand bounds, so the gesture
            // is reliable even if the drop raycast lands on a card/zone instead of the half itself.
            if (!manager.TryReorderHandDrag(eventData, card, handSeat)
                && (manager.CanActivateEventDragRelease(eventData, card, handSeat)
                    || (manager.CanActivateEventFromHand(card, handSeat) && !StillWithinHand(eventData))))
                manager.PlayEventFromHand(card, handSeat);
            manager.SetHandDropRaycastActive(handSeat, false);
            RestoreLiftedSource();
            if (ghost != null) Destroy(ghost);
            if (arrowRoot != null) Destroy(arrowRoot);
            ghost = null;
            arrowRoot = null;
            // Releasing over a non-playable region dispatches nothing, so nothing re-renders —
            // any cards slid/lifted during the drag stay stuck mid-air. Always rebuild: if a
            // play DID happen this render is redundant but harmless; if not, it snaps every
            // hand card cleanly back to its fan slot.
            manager.Render();
        }

        // Pure geometric check (no raycasting) - relying on Unity's raycast/EventSystem for this
        // proved fragile across multiple bugs (the dragged card itself blocking its own raycast,
        // CanvasGroup timing). Just test the cursor's screen position against the hand panel's
        // actual rect directly.
        private bool StillWithinHand(PointerEventData eventData)
        {
            var panelImage = handSeat == "south" ? manager.southHandPanelImage : manager.northHandPanelImage;
            if (panelImage == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(panelImage.rectTransform, eventData.position, null);
        }

        private void SnapHeldCardToPreviewSlot()
        {
            if (liftHolder == null) return;
            var row = manager.GetHandRow(handSeat);
            if (row == null || previewOrder == null) return;

            var count = previewOrder.Count;
            var slot = Mathf.Clamp(previewIndex, 0, Mathf.Max(0, count - 1));
            var geo = manager.ComputeFanGeometry(row, count);

            // While dragging within hand, the card should feel like it has snapped into the
            // candidate slot, but be lifted toward center like the normal hand-hover treatment.
            const float lift = 42f;
            if (previewOrderChangedThisFrame)
            {
                manager.SlideFanSlot(liftHolder, geo, slot, count, inverted, lift, 1.08f);
                previewOrderChangedThisFrame = false;
            }
            else if (liftHolder.GetComponent<RectTransformSlideTween>() == null)
            {
                manager.ApplyFanSlot(liftHolder, geo, slot, count, inverted);
                var liftDirection = inverted ? -1f : 1f;
                liftHolder.anchoredPosition += new Vector2(0f, lift * liftDirection);
                liftHolder.localScale = Vector3.one * 1.08f;
            }
            ApplyPreviewStackOrder();
        }

        private void ApplyPreviewStackOrder()
        {
            if (liftHolder == null || previewOrder == null) return;

            // Match the normal hand fan's sibling order every frame: rightmost/back cards first,
            // leftmost/front cards last. Rebuilding the complete order avoids one-frame index
            // oscillation around the card in front of the dragged card.
            for (int i = previewOrder.Count - 1; i >= 0; i--)
            {
                var c = previewOrder[i];
                if (c.InstanceId == card.InstanceId)
                {
                    liftHolder.SetAsLastSibling();
                }
                else if (manager.handCardRects.TryGetValue(c.InstanceId, out var holder) && holder != null)
                {
                    holder.SetAsLastSibling();
                }
            }
        }

        // Recomputes where the dragged card would land and live-repositions every other card into
        // the full hand fan, leaving the dragged card's destination slot empty.
        private void UpdateLivePreview(PointerEventData eventData)
        {
            var row = manager.GetHandRow(handSeat);
            if (row == null || previewOrder == null) return;

            int count = previewOrder.Count;
            var geo = manager.ComputeFanGeometry(row, count);
            previewOrderChangedThisFrame = false;
            // Each slot's screen-space centre X (local slot X -> world -> screen, the same direction
            // MoveGhost/CreateArrow already use successfully).
            var slotScreenX = new float[count];
            for (int i = 0; i < count; i++)
            {
                var normalized = HandFanPosition(i, count);
                var slotX = normalized * geo.MaxSpread * 0.5f;
                var slotWorld = row.TransformPoint(new Vector3(slotX, 0f, 0f));
                slotScreenX[i] = RectTransformUtility.WorldToScreenPoint(null, slotWorld).x;
            }
            // Shift at the BOUNDARY between adjacent slots (midpoint of their centres = the shared
            // edge), not when the cursor reaches the next slot's centre - so a card moves as soon as
            // you cross the edge between cards instead of dragging all the way over the neighbour.
            int newIndex = count - 1;
            for (int i = 0; i < count - 1; i++)
            {
                var boundary = (slotScreenX[i] + slotScreenX[i + 1]) * 0.5f;
                if (eventData.position.x < boundary) { newIndex = i; break; }
            }

            if (newIndex != previewIndex)
            {
                var oldIndex = previewOrder.FindIndex(c => c.InstanceId == card.InstanceId);
                if (oldIndex < 0) return;
                previewOrder.RemoveAt(oldIndex);
                newIndex = Mathf.Clamp(newIndex, 0, previewOrder.Count);
                previewOrder.Insert(newIndex, card);
                previewIndex = newIndex;
                previewOrderChangedThisFrame = true;
            }

            // Leave the dragged card's destination slot empty while positioning every other card
            // in the full hand fan. This makes the reorder target visible without compressing the
            // hand into a different N-1-card geometry during the drag.
            if (!previewOrderChangedThisFrame) return;
            for (int slot = 0; slot < previewOrder.Count; slot++)
            {
                var c = previewOrder[slot];
                if (c.InstanceId == card.InstanceId) continue;
                if (manager.handCardRects.TryGetValue(c.InstanceId, out var holder) && holder != null)
                    manager.SlideFanSlot(holder, geo, slot, count, inverted);
            }
        }

        private int EngineHandTargetIndex(int finalIndex)
        {
            return originalIndex >= 0 && originalIndex < finalIndex ? finalIndex + 1 : finalIndex;
        }

        // Switches from in-hand reorder to the out-of-hand "play this card" flow: undo the live
        // preview shifts and optionally hide the source card while the ghost/arrow takes over.
        private void ExitInHandReorder(bool hideSourceForGhost = false)
        {
            inHandReorder = false;
            manager.presenceHandDragFrom = manager.presenceHandDragTo = -1;   // no longer reordering in-hand
            var row = manager.GetHandRow(handSeat);
            if (row != null)
            {
                var trueHand = manager.GetHandSnapshot(handSeat);
                var geo = manager.ComputeFanGeometry(row, trueHand.Count);
                for (int i = 0; i < trueHand.Count; i++)
                {
                    if (trueHand[i].InstanceId == card.InstanceId) continue;
                    if (manager.handCardRects.TryGetValue(trueHand[i].InstanceId, out var holder) && holder != null)
                        manager.ApplyFanSlot(holder, geo, i, trueHand.Count, inverted);
                }
            }
            if (liftHolder != null)
            {
                liftHolder.anchoredPosition = homeAnchoredPosition;
                liftHolder.localRotation = homeRotation;
                liftHolder.localScale = Vector3.one;
                liftHolder.SetSiblingIndex(homeSiblingIndex);
            }
            if (liftGroup != null)
            {
                liftGroup.alpha = hideSourceForGhost ? 0f : 1f;
                liftGroup.blocksRaycasts = !hideSourceForGhost;
            }
            sourceHiddenForGhost = hideSourceForGhost;
            previewOrder = null;
            if (!hideSourceForGhost)
            {
                liftHolder = null;
                liftGroup = null;
            }
        }

        private void RestoreLiftedSource()
        {
            if (sourceHiddenForGhost && liftGroup != null)
            {
                liftGroup.alpha = 1f;
                liftGroup.blocksRaycasts = true;
            }
            sourceHiddenForGhost = false;
            liftHolder = null;
            liftGroup = null;
        }

        private void CreateGhostAndArrow(PointerEventData eventData)
        {
            ghost = new GameObject("Dragging Card");
            ghost.transform.SetParent(ownerCanvas.transform, false);
            ghost.transform.SetAsLastSibling();
            var rect = ghost.AddComponent<RectTransform>();
            var sourceSize = sourceRect != null ? sourceRect.rect.size : new Vector2(80, 112);
            rect.sizeDelta = sourceSize * 1.25f;
            var image = manager.AddRoundedCardImage(ghost.transform, "Dragging Card Art", manager.GetCardSprite(card.CardId));
            image.raycastTarget = false;
            var group = ghost.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            // Free drag: the card follows the cursor 1:1; it only snaps into place while
            // hovering a character slot it can legally drop into. The targeting arrow
            // appears from the card's hand slot to the drop zone while over one.
            CreateArrow();
            MoveGhost(eventData);
            UpdateArrow(eventData);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (ghost == null) return;
            var snapRect = manager.GetDragTargetRect(eventData, card, handSeat, out var snapValid);
            if (snapValid && snapRect != null
                && GameEngine.GetCard(card).Type == "character"
                && snapRect.GetComponentInParent<CharacterSlotDrop>() != null)
            {
                // Hovering a valid character slot: snap the card onto the slot.
                ghost.transform.position = snapRect.position;
                ghost.transform.localScale = Vector3.one;
            }
            else
            {
                ghost.transform.position = eventData.position;
                ghost.transform.localScale = Vector3.one;
            }
        }

        private void CreateArrow()
        {
            var arrowRect = new GameObject("Drag Arrow").AddComponent<RectTransform>();
            arrowRoot = arrowRect.gameObject;
            arrowRoot.transform.SetParent(ownerCanvas.transform, false);
            arrowRoot.transform.SetAsLastSibling();
            var group = arrowRoot.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            arrowRoot.SetActive(false);
        }

        private void UpdateArrow(PointerEventData eventData)
        {
            if (arrowRoot == null || sourceRect == null) return;
            var root = arrowRoot.transform as RectTransform;
            manager.Clear(root);
            var target = manager.GetDragTargetRect(eventData, card, handSeat, out var valid);
            if (target == null)
            {
                arrowRoot.SetActive(false);
                return;
            }

            var end = RectTransformUtility.WorldToScreenPoint(null, target.position);
            var start = RectTransformUtility.WorldToScreenPoint(null, sourceRect.position);
            var delta = end - start;
            var distance = delta.magnitude;
            if (distance < 8f)
            {
                arrowRoot.SetActive(false);
                return;
            }

            arrowRoot.SetActive(true);
            arrowRoot.transform.SetAsLastSibling();

            var pulse = 0.38f + 0.62f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7.5f));
            var color = valid ? Gold : RedAccent;
            color.a = valid ? pulse : 0.28f + 0.22f * pulse;
            manager.DrawCurvedTargetingArrow(root, sourceRect, target, color, valid ? 12f : 8f);
        }
    }

    private sealed class DonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private Outline outline;
        private Color originalOutlineColor;
        private Vector2 originalOutlineDistance;
        private RectTransform hoverGlow;

        public void Init(GameManager owner)
        {
            manager = owner;
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                originalOutlineColor = outline.effectColor;
                originalOutlineDistance = outline.effectDistance;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            manager.ShowDonPreview();
            ShowHoverGlow();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideHoverGlow();
            manager.HidePreview();
            if (outline != null)
            {
                outline.effectColor = originalOutlineColor;
                outline.effectDistance = originalOutlineDistance;
            }
        }

        private void ShowHoverGlow()
        {
            HideHoverGlow();
            // Parent the glow to the DON holder (this catcher's parent), NOT the catcher itself.
            // The catcher sits on TOP of the card art, so glowing it rendered the glow in front of
            // the card. Using the holder lets AddMysticalCardOutline place it BEHIND the art, like
            // every other card.
            var target = (transform.parent as RectTransform) ?? (transform as RectTransform);
            hoverGlow = manager.AddMysticalCardOutline(target, true);
        }

        private void HideHoverGlow()
        {
            if (hoverGlow == null) return;
            Destroy(hoverGlow.gameObject);
            hoverGlow = null;
        }
    }

    private sealed class MysticCardAura : MonoBehaviour
    {
        private struct MistLayer
        {
            public RawImage Img;
            public Color Base;
            public float VelX, VelY, U, V, Phase;
        }

        private struct BorderLayer
        {
            public Image Img;
            public Color Base;
            public float Phase;
        }

        // The ambient glow blob (SoftGlow) just breathes — it does NOT scroll.
        private RawImage softGlow;
        private Color    softGlowBase;

        private readonly List<MistLayer>   mist   = new List<MistLayer>();
        private readonly List<BorderLayer> border = new List<BorderLayer>();

        private static readonly (float vx, float vy)[] MistVelocities =
        {
            (-0.022f,  0.013f),
            ( 0.030f, -0.017f),
        };

        public void Init(bool strong)
        {
            softGlow = null;
            mist.Clear();
            border.Clear();
            int mi = 0;
            foreach (var raw in GetComponentsInChildren<RawImage>(true))
            {
                if (raw == null) continue;
                if (raw.gameObject.name.StartsWith("Mist_"))
                {
                    var (vx, vy) = mi < MistVelocities.Length ? MistVelocities[mi] : (0.02f, 0.01f);
                    mist.Add(new MistLayer
                    {
                        Img = raw, Base = raw.color,
                        VelX = vx, VelY = vy,
                        U = raw.uvRect.x, V = raw.uvRect.y,
                        Phase = mi * 1.47f,
                    });
                    mi++;
                }
                else
                {
                    softGlow = raw;
                    softGlowBase = raw.color;
                }
            }
            int bi = 0;
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (img == null) continue;
                border.Add(new BorderLayer { Img = img, Base = img.color, Phase = bi * 0.83f });
                bi++;
            }
        }

        private void Update()
        {
            float t  = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            // Ambient glow — slow, subtle breathing; period ~5.7 s.
            if (softGlow != null)
            {
                float pulse = 0.82f + 0.18f * Mathf.Sin(t * 1.1f);
                var c = softGlowBase;
                c.a = Mathf.Clamp01(softGlowBase.a * pulse);
                softGlow.color = c;
            }

            // Mist rings — UV scroll in opposite diagonals, gentle alpha flicker.
            for (int i = 0; i < mist.Count; i++)
            {
                var m = mist[i];
                if (m.Img == null) continue;
                m.U += m.VelX * dt;
                m.V += m.VelY * dt;
                m.Img.uvRect = new Rect(m.U, m.V, 1f, 1f);
                float pulse = 0.75f + 0.25f * Mathf.Sin(t * 1.7f + m.Phase);
                var c = m.Base;
                c.a = Mathf.Clamp01(m.Base.a * pulse);
                m.Img.color = c;
                mist[i] = m;
            }

            // Crisp edge line — slightly faster flicker for a shimmer quality.
            for (int i = 0; i < border.Count; i++)
            {
                var b = border[i];
                if (b.Img == null) continue;
                float pulse = 0.85f + 0.15f * Mathf.Sin(t * 2.8f + b.Phase);
                var c = b.Base;
                c.a = Mathf.Clamp01(b.Base.a * pulse);
                b.Img.color = c;
                border[i] = b;
            }
        }
    }

    private sealed class BackHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private Outline outline;
        private Color originalOutlineColor;
        private Vector2 originalOutlineDistance;
        private RectTransform hoverGlow;
        private bool donBack;

        public void Init(GameManager owner, bool isDonBack = false)
        {
            manager = owner;
            donBack = isDonBack;
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                originalOutlineColor = outline.effectColor;
                originalOutlineDistance = outline.effectDistance;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (donBack) manager.ShowDonBackPreview();
            else manager.ShowBackPreview();
            ShowHoverGlow();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideHoverGlow();
            manager.HidePreview();
            if (outline != null)
            {
                outline.effectColor = originalOutlineColor;
                outline.effectDistance = originalOutlineDistance;
            }
        }

        private void ShowHoverGlow()
        {
            HideHoverGlow();
            hoverGlow = manager.AddMysticalCardOutline(transform as RectTransform, true);
        }

        private void HideHoverGlow()
        {
            if (hoverGlow == null) return;
            Destroy(hoverGlow.gameObject);
            hoverGlow = null;
        }
    }

    // Temporarily lifts transforms to the top of their parent's draw order, restoring their original
    // sibling indices when this component is destroyed. Used so an attack-target glow that overflows
    // its card renders above neighbouring board zones instead of being painted over by them.
    private sealed class SiblingRestore : MonoBehaviour
    {
        private readonly System.Collections.Generic.List<Transform> targets = new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<int> indices = new System.Collections.Generic.List<int>();

        public void Remember(Transform t)
        {
            if (t == null) return;
            targets.Add(t);
            indices.Add(t.GetSiblingIndex());
        }

        public void RaiseAll()
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) targets[i].SetAsLastSibling();
        }

        private void OnDestroy()
        {
            for (int i = targets.Count - 1; i >= 0; i--)
                if (targets[i] != null) targets[i].SetSiblingIndex(indices[i]);
        }
    }

    private sealed class AttackDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GameManager manager;
        private Canvas ownerCanvas;
        private CardInstance card;
        private string seat;
        private RectTransform sourceRect;
        private GameObject arrowRoot;
        private GameObject indicator;
        private string hoverTargetId;
        private bool dragging;
        private static readonly Color AttackColor = new Color(1f, 0.30f, 0.10f, 0.98f);
        private static readonly Color ValidColor = new Color(1f, 0.42f, 0.12f, 0.98f);
        private static readonly Color InvalidColor = new Color(0.55f, 0.58f, 0.62f, 0.95f);

        public void Init(GameManager owner, Canvas canvas, CardInstance attacker, string attackerSeat)
        {
            manager = owner;
            ownerCanvas = canvas;
            card = attacker;
            seat = attackerSeat;
            sourceRect = transform as RectTransform;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (manager == null || card == null || !manager.IsAttackableBoardCard(card, seat)) return;
            dragging = true;
            manager.isDraggingAttack = true;
            manager.HidePreview();
            manager.HideHandHoverPreview();
            var hover = GetComponent<CardHover>();
            if (hover != null) hover.ResetVisual();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging) return;

            // Which opponent card (if any) is under the cursor right now?
            CardInstance hovered = null;
            var go = eventData.pointerCurrentRaycast.gameObject;
            if (go != null)
            {
                var th = go.GetComponentInParent<CardHover>();
                if (th != null) hovered = th.Card;
            }
            bool overOpp = hovered != null && !string.IsNullOrEmpty(hovered.Owner) && hovered.Owner != seat;
            bool valid = overOpp && manager.IsValidAttackTarget(seat, card, hovered);
            string newHoverId = overOpp ? hovered.InstanceId : null;

            // Refresh the on-card valid/invalid indicator only when the hovered card changes.
            if (newHoverId != hoverTargetId)
            {
                if (indicator != null) { Destroy(indicator); indicator = null; }
                hoverTargetId = newHoverId;
                if (overOpp)
                {
                    var tr = manager.GetCardRect(hovered.InstanceId);
                    if (tr != null)
                    {
                        var ind = manager.ShowAttackTargetIndicator(tr, valid);
                        if (ind != null) indicator = ind.gameObject;
                    }
                }
            }

            // Arrow snaps to the hovered card (coloured by validity), else trails the cursor.
            if (arrowRoot != null) Destroy(arrowRoot);
            arrowRoot = manager.NewArrowRoot("Attack Drag Arrow", ownerCanvas.transform).gameObject;
            var root = arrowRoot.transform as RectTransform;
            var snapRect = overOpp ? manager.GetCardRect(hovered.InstanceId) : null;
            if (snapRect != null)
                manager.DrawCurvedTargetingArrow(root, sourceRect, snapRect, valid ? ValidColor : InvalidColor, 14f);
            else
                manager.DrawCurvedTargetingArrowToPoint(root, sourceRect, eventData.position, AttackColor, 14f);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Cleanup();
            if (!dragging) return;
            dragging = false;
            manager.isDraggingAttack = false;

            CardInstance targetCard = null;
            var go = eventData.pointerCurrentRaycast.gameObject;
            if (go != null)
            {
                var th = go.GetComponentInParent<CardHover>();
                if (th != null) targetCard = th.Card;
            }
            // Only a legal target attacks; an invalid drop does nothing (the red X already warned).
            if (targetCard != null && manager.IsValidAttackTarget(seat, card, targetCard))
                manager.DeclareAttackFromDrag(seat, card.InstanceId, targetCard.InstanceId);
        }

        private void Cleanup()
        {
            if (arrowRoot != null) { Destroy(arrowRoot); arrowRoot = null; }
            if (indicator != null) { Destroy(indicator); indicator = null; }
            hoverTargetId = null;
        }

        private void OnDisable()
        {
            Cleanup();
            if (manager != null) manager.isDraggingAttack = false;
        }
    }

    private sealed class LogCardLink : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private string cardId;
        public void Init(GameManager owner, string id) { manager = owner; cardId = id; }
        public void OnPointerEnter(PointerEventData eventData) { manager.ShowPreviewById(cardId); }
        public void OnPointerExit(PointerEventData eventData) { manager.HidePreview(); }
    }

    // Hover a [Blocker] shield → show the blocker info preview on the right and turn the ring glow gold;
    // revert on exit. CLICKING the shield forwards to the card's own click, so during a block you can
    // declare the block by clicking either the card or its shield icon (the icon sits on top and would
    // otherwise absorb the click).
    private sealed class BlockerShieldHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private GameManager manager;
        private RawImage glow;
        private Color idleColor, hoverColor;
        private CardInstance card;
        private string seat;
        public void Init(GameManager owner, RawImage shieldGlow, Color idle, Color hover, CardInstance c, string s)
        { manager = owner; glow = shieldGlow; idleColor = idle; hoverColor = hover; card = c; seat = s; }
        public void OnPointerEnter(PointerEventData eventData)
        { if (glow != null) glow.color = hoverColor; if (manager != null) manager.ShowBlockerPreview(); }
        public void OnPointerExit(PointerEventData eventData)
        { if (glow != null) glow.color = idleColor; if (manager != null) manager.HideBlockerPreview(); }
        public void OnPointerClick(PointerEventData eventData)
        { if (manager != null && card != null) manager.OnCardClick(card, seat); }
    }

    // Hover handler for counter-step tiles: instead of a popup beside the tile, it floats the ACTUAL
    // card up in your hand and previews it there (exactly like hovering the card in hand). Nested so it
    // can reach the private hover API.
    private sealed class PreviewOnHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private CardInstance card;
        public void Init(GameManager m, CardInstance c) { manager = m; card = c; }
        public void OnPointerEnter(PointerEventData e) { if (manager != null) manager.CounterTileHoverEnter(card); }
        public void OnPointerExit(PointerEventData e) { if (manager != null) manager.CounterTileHoverExit(card); }
    }

    // Hover on a counter-step matchup mini card → big character preview left of the panel.
    private sealed class CounterPreviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private CardInstance card;
        public void Init(GameManager m, CardInstance c) { manager = m; card = c; }
        public void OnPointerEnter(PointerEventData e) { if (manager != null) manager.CounterPreviewEnter(card); }
        public void OnPointerExit(PointerEventData e) { if (manager != null) manager.CounterPreviewExit(); }
    }

    private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameManager manager;
        private CardInstance card;
        private bool faceUp;
        private bool isHandCard;
        private Transform visualRoot;
        private Transform stackRoot;
        private Vector3 originalScale;
        private Vector3 originalLocalPosition;
        private Outline outline;
        private Color originalOutlineColor;
        private Vector2 originalOutlineDistance;
        private int homeSiblingIndex;
        private bool suppressPreview;
        private RectTransform hoverGlow;

        public CardInstance Card => card;

        public void Init(GameManager owner, CardInstance cardInstance, bool showFace, bool handCard, int handHomeSiblingIndex = -1, bool suppressPreviewPopup = false)
        {
            manager = owner;
            card = cardInstance;
            faceUp = showFace;
            isHandCard = handCard;
            homeSiblingIndex = handHomeSiblingIndex;
            suppressPreview = suppressPreviewPopup;
            visualRoot = transform.parent != null ? transform.parent : transform;
            stackRoot = visualRoot.parent != null ? visualRoot.parent : visualRoot;
            originalScale = visualRoot.localScale;
            originalLocalPosition = visualRoot.localPosition;
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                originalOutlineColor = outline.effectColor;
                originalOutlineDistance = outline.effectDistance;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // While any card is mid-drag, the cursor sweeping over other hand cards shouldn't
            // pop their preview/glow - it's incidental, not an intentional hover.
            if (manager.isDraggingHandCard || manager.isDraggingAttack) return;

            ShowHoverGlow();
            manager.SetPresenceHover(card, isHandCard, true);
            manager.ShowContextTargetArrow(card);

            if (suppressPreview) return;

            if (!isHandCard || !faceUp)
            {
                manager.ShowPreview(card, faceUp);
                return;
            }

            manager.HidePreview();
            manager.ShowHandHoverPreview(card, transform as RectTransform);
            stackRoot.SetAsLastSibling();
            var liftDirection = card != null && card.Owner == manager.TopSeat ? -1f : 1f;
            visualRoot.localScale = originalScale * 1.08f;
            visualRoot.localPosition = originalLocalPosition + new Vector3(0, 10f * liftDirection, 0);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ResetVisual();
            manager.SetPresenceHover(card, isHandCard, false);
            manager.HideHoverTargetArrow();
            if (homeSiblingIndex >= 0) stackRoot.SetSiblingIndex(homeSiblingIndex);
            if (isHandCard) manager.HideHandHoverPreview();
            else manager.HidePreview();
        }

        public void ResetVisual()
        {
            HideHoverGlow();
            visualRoot.localScale = originalScale;
            visualRoot.localPosition = originalLocalPosition;
            if (outline != null)
            {
                outline.effectColor = originalOutlineColor;
                outline.effectDistance = originalOutlineDistance;
            }
        }

        private void ShowHoverGlow()
        {
            HideHoverGlow();
            var target = transform as RectTransform;
            // Green rim when usable OR a valid deck-look/search pick (e.g. Imu's Stage choices), RED rim
            // when it's an in-zone-but-illegal effect target, otherwise the normal gold hover glow.
            if (manager.IsGreenTargetNow(card))
                hoverGlow = manager.AddUsableGlow(target);
            else if (manager.IsInvalidEffectTarget(card))
                hoverGlow = manager.AddInvalidGlow(target);
            else
                hoverGlow = manager.AddMysticalCardOutline(target, true);
        }

        private void HideHoverGlow()
        {
            if (hoverGlow == null) return;
            Destroy(hoverGlow.gameObject);
            hoverGlow = null;
        }
    }

}






















