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

public class GameManager : MonoBehaviour
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
    private bool isReplayMode;
    private ReplayRecord loadedReplay;
    private int replayCursor;
    // Set by MainMenuManager's replay browser before EnsureBoard(); when populated,
    // Start() enters read-only replay playback instead of starting a new match.
    public static ReplayRecord PendingReplayLoad;
    // Set by MainMenuManager's lobby hub before EnsureBoard(); when populated, Start()
    // enters a networked match instead of local hotseat play. LocalSeat restricts
    // interaction to the seat this client actually controls (see Dispatch()).
    public static string PendingNetworkedSeed;
    public static string PendingNetworkedSeat;
    private bool isNetworked;
    private string localSeat;
    // Cached label for the coin-flip "waiting on opponent" state (networked only); Update()
    // mutates its text directly for the blinking-dots animation instead of re-rendering the
    // whole board every frame. Unity's fake-null makes the null-check safe even after the
    // overlay's GameObject gets torn down by the next Render() pass.
    private Text coinFlipWaitingText;
    private string coinFlipWaitingBaseMessage;
    // Which seat renders at the bottom/top of THIS client's screen. "south" for hotseat and
    // Versus Self (unchanged, matches every existing hardcoded assumption in this file) -
    // for a networked match, the locally-controlled seat is always drawn at the bottom
    // regardless of whether that's "south" or "north" in the shared GameState, since the
    // underlying seat identity must stay the same on both clients for the deterministic
    // command-replay sync to work (see MatchNetworkSync.cs) - only the rendering flips.
    private string BottomSeat => isNetworked ? localSeat : "south";
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
    private CardInstance previewLockCard;   // last left-clicked card, shown in the docked left preview
    private bool menuOpen;                   // game menu (upper-right) open/closed
    private RectTransform deckLookOverlay;    // full-screen search/look overlay (lives on the canvas)
    private bool deckLookPeeking;              // true while the player has toggled the overlay away to check the board/hand
    private DeckLookState deckLookRevealSession; // which DeckLookState (by reference) we've already started/finished revealing
    private bool deckLookRevealing;              // true while cards are still being drawn in one at a time; selection is disabled meanwhile
    private RectTransform handHoverRoot;
    private Image previewImage;
    private Text previewTitle;
    private Font font;
    private Font monoFont;
    private Font titleFont;
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
    }
    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        // Techy/mono faces to approximate the design mock (Chakra Petch / JetBrains Mono). Falls back
        // through common Windows fonts; if none exist these stay null and TextObject uses the default.
        try { monoFont = Font.CreateDynamicFontFromOSFont(new[] { "JetBrains Mono", "Consolas", "Cascadia Mono", "Courier New" }, 14); } catch { monoFont = null; }
        try { titleFont = Font.CreateDynamicFontFromOSFont(new[] { "Chakra Petch", "Bahnschrift", "Eurostile", "Segoe UI Semibold", "Segoe UI" }, 16); } catch { titleFont = null; }
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
        else
        {
            NewMatch();
        }
    }

    private void OnDestroy()
    {
        MatchNetworkSync.CommandReceived -= OnNetworkCommandReceived;
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
                    card.effect,
                    card.trigger,
                    NormalizeFeatures(card));
            }
            Debug.Log($"Loaded {seen.Count} official One Piece card definitions.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Official card library failed to load: {ex.Message}");
        }
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
        state = GameEngine.CreateMatch(config);
        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);
        Render();
    }

    private void Dispatch(GameCommand command)
    {
        if (isReplayMode) return; // read-only playback: ignore stray interactive input
        // Networked match: block acting as the other seat (e.g. a stray click reaching a
        // handler before UI catches up) rather than auditing every call site individually.
        if (isNetworked && !string.IsNullOrEmpty(command.Seat) && command.Seat != localSeat) return;
        state = GameEngine.ApplyCommand(state, command);
        NormalizeSelection();
        if (state.Status == "finished" && !replaySaved)
        {
            replaySaved = true;
            SaveFinishedMatchRecords();
        }
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
        ResimulateReplayTo(record.CommandHistory.Count);
    }

    private void ResimulateReplayTo(int index)
    {
        if (loadedReplay == null) return;
        replayCursor = Mathf.Clamp(index, 0, loadedReplay.CommandHistory.Count);
        state = GameEngine.CreateMatch(currentMatchConfig);
        for (int i = 0; i < replayCursor; i++)
            state = GameEngine.ApplyCommand(state, loadedReplay.CommandHistory[i].ToCommand());
        selectedId = null;
        selectedSeat = null;
        Render();
    }

    private void ExitReplayToMenu()
    {
        isReplayMode = false;
        loadedReplay = null;
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
        isNetworked = true;
        localSeat = seat;

        var config = new MatchConfig { Seed = seed };
        currentMatchConfig = config;
        state = GameEngine.CreateMatch(config);
        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);

        MatchNetworkSync.CommandReceived -= OnNetworkCommandReceived;
        MatchNetworkSync.CommandReceived += OnNetworkCommandReceived;
        Render();
    }

    private void OnNetworkCommandReceived(GameCommand command)
    {
        if (!isNetworked || state == null) return;
        state = GameEngine.ApplyCommand(state, command);
        NormalizeSelection();
        if (state.Status == "finished" && !replaySaved)
        {
            replaySaved = true;
            SaveFinishedMatchRecords();
        }
        Render();
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
        var record = ReplayStore.Save(state, currentMatchConfig, durationSeconds);
        if (record == null) return;

        var summary = MatchHistoryStore.BuildSummary(state, record,
            isNetworked && !string.IsNullOrEmpty(localSeat) ? localSeat : "south");
        // Fire-and-forget: SaveMatchAsync catches + logs its own failures (and
        // skips guests entirely), so match end can never be blocked by network.
        if (summary != null)
        {
            _ = MatchHistoryStore.SaveMatchAsync(summary);
            // Lifetime + seasonal aggregates (StatsStore.cs). Also fire-and-forget:
            // stats failures log and skip, they never block or break match end.
            _ = StatsStore.RecordMatchAsync(summary);
        }
    }

    private void NormalizeSelection()
    {
        if (state == null) return;
        ReconcileDonGroups();
        NormalizeDonSelection();

        // Clear trash viewer when a blocking state appears.
        if (trashViewSeat != null && (state.Battle != null || state.PendingEffects.Count > 0 || state.DeckLook != null || state.ActiveChoice != null))
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

    private void SelectDon(string seat, string instanceId, int index, bool shift, bool render = true)
    {
        if (state == null || string.IsNullOrEmpty(seat) || !state.Players.TryGetValue(seat, out var player)) return;
        if (index < 0 || index >= player.CostArea.Count) return;
        var don = player.CostArea[index];
        if (don == null || don.InstanceId != instanceId || !CanSelectDon(seat, don)) return;

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
        if (selectedDonIds.Count > 0) ClearDonSelection();
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
        if (don.Rested) holder.localRotation = Quaternion.Euler(0, 0, inverted ? 270f : 90f);
        else if (inverted) holder.localRotation = Quaternion.Euler(0, 0, 180f);
        var catcher = PanelObject("DON Hover Catcher", holder, new Color(0, 0, 0, 0));
        Stretch(catcher, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        catcher.gameObject.AddComponent<DonHover>().Init(this);
        catcher.gameObject.AddComponent<DonSelector>().Init(this, canvas, seat, don.InstanceId, origIndex);
        if (groupTag >= 0) catcher.gameObject.AddComponent<DonGroupTag>().GroupIndex = groupTag;
    }

    // Grouped cost-area display from donGroupSizes: rested DON cluster, then one cluster per group,
    // gaps auto-justified so the groups fill the cost region (and shrink to fit when crowded).
    private void DrawGroupedDonRow(RectTransform parent, IList<DonInstance> donCards, string seat, bool inverted)
    {
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
        for (int g = 0; g < donGroupSizes.Count && pos < active.Count; g++)
        {
            int take = Mathf.Min(donGroupSizes[g], active.Count - pos);
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
            clusterTags.Add(donGroupSizes.Count);
        }
        if (clusterCards.Count == 0) return;

        float cardW = boardCardSize.x > 1f ? boardCardSize.x : 70f;
        float cardH = boardCardSize.y > 1f ? boardCardSize.y : cardW / CardAspect;
        float maxH = parent.rect.height > 1f ? parent.rect.height * 0.96f : cardH;
        if (cardH > maxH) { cardH = maxH; cardW = cardH * CardAspect; }
        float availW = parent.rect.width > 1f ? parent.rect.width : (boardRoot != null ? boardRoot.rect.width * 0.55f : 600f);
        float intra = cardW * 0.34f;
        int nClusters = clusterCards.Count;
        float SumSpans()
        {
            float w = 0f;
            for (int c = 0; c < nClusters; c++) w += cardW + (clusterCards[c].Count - 1) * intra;
            return w;
        }
        float minGap = cardW * 0.18f;
        float maxGap = cardW * 1.1f;
        float minimal = SumSpans() + minGap * Mathf.Max(0, nClusters - 1);
        if (minimal > availW && minimal > 0f)
        {
            float scale = availW / minimal;
            cardW *= scale; cardH *= scale; intra *= scale; minGap *= scale; maxGap *= scale;
        }
        float sumSpans = SumSpans();
        float gap = nClusters > 1 ? Mathf.Clamp((availW - sumSpans) / (nClusters - 1), minGap, maxGap) : 0f;
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
            state.ActiveSeat != seat) return false;
        return true;
    }

    private bool AttachSelectedDonTo(string seat, CardInstance card)
    {
        if (!CanAttachSelectedDonTo(seat, card)) return false;
        var donIds = selectedDonIds.ToList();
        ClearDonSelection(false);
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
    }

    private void Render()
    {
        ClearDragGhosts();
        HideHandHoverPreview();
        // Also drop the floating board preview: a click that plays/moves a card rebuilds the board and
        // destroys the hovered card's CardHover before its OnPointerExit fires, which would otherwise
        // leave the preview stuck up. Re-hovering re-shows it.
        HidePreview();
        Clear(boardRoot);
        Clear(sideRoot);
        Clear(leftRoot);
        handCardRects.Clear();
        boardDeckPileRects.Clear();
        cardTargetRects.Clear();
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
        Stretch(playRoot, new Vector2(0.17f, 0f), new Vector2(0.83f, 1f), Vector2.zero, Vector2.zero);
        var _praw = playRoot.GetComponent<Image>();
        if (_praw != null) _praw.raycastTarget = false;
        DrawReferencePlaymat();
        DrawExternalHand(state.Players[TopSeat], TopSeat, true);
        DrawExternalHand(state.Players[BottomSeat], BottomSeat, false);
        // Battle status banner removed - the live power badges on the cards convey the same info now.
        DrawCoinFlipOverlay();
        DrawMulliganOverlay();
        DrawDeckLookOverlay();
        DrawResolvedTargetingArrows();
        DrawSidePanel();
        DrawLeftPanel();
        if (isReplayMode) DrawReplayBar();
    }

    // Thin control strip across the top of the board, only shown during replay playback.
    private void DrawReplayBar()
    {
        var bar = PanelObject("Replay Bar", boardRoot, new Color32(8, 14, 21, 220));
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 40f);
        bar.anchoredPosition = Vector2.zero;

        int total = loadedReplay?.CommandHistory.Count ?? 0;
        var label = TextObject("Replay Label", bar,
            $"MATCH HISTORY  ·  step {replayCursor}/{total}  ·  turn {state.TurnNumber}",
            12, Ink, TextAnchor.MiddleLeft, monoFont);
        Stretch(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(16f, 0f), Vector2.zero);

        var controls = PanelObject("Replay Controls", bar, new Color(0, 0, 0, 0));
        controls.anchorMin = new Vector2(1f, 0f);
        controls.anchorMax = new Vector2(1f, 1f);
        controls.pivot = new Vector2(1f, 0.5f);
        controls.sizeDelta = new Vector2(420f, 0f);
        controls.anchoredPosition = new Vector2(-12f, 0f);
        var hlg = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        AddButton(controls, "|< Start", () => ResimulateReplayTo(0), replayCursor > 0, false);
        AddButton(controls, "< Prev", () => ResimulateReplayTo(replayCursor - 1), replayCursor > 0, false);
        AddButton(controls, "Next >", () => ResimulateReplayTo(replayCursor + 1), replayCursor < total, false);
        AddButton(controls, "End >|", () => ResimulateReplayTo(total), replayCursor < total, false);
        AddButton(controls, "Exit", () => ExitReplayToMenu(), true, false);
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
            return;
        }
        var dl = state.DeckLook;
        bool selecting = dl.Step == "select";

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

        if (!selecting)
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
        else
        {
            titleText = selecting
                ? $"{dl.SourceName}: choose up to 1 {DeckLookFilterDesc(dl)}card to add to your hand"
                : "Drag to set the order these return to the bottom of the deck, then Confirm";
        }
        var title = TextObject("Deck Look Title", dim, titleText, 25, Ink, TextAnchor.MiddleCenter);
        Stretch(title.rectTransform, new Vector2(0.06f, 0.90f), new Vector2(0.78f, 0.985f), Vector2.zero, Vector2.zero);

        // Lets the player dismiss the overlay to check the board/hand before deciding; reopens
        // via the small "Show Cards" button drawn above when deckLookPeeking is true. Hidden while
        // cards are still being drawn in, since toggling it would call Render() and tear down the
        // in-flight animation (see DrawDeckLookOverlay's teardown at the very top of this method).
        if (!revealingNow)
        {
            AddOverlayButton(dim, "View Board / Hand", new Vector2(0.80f, 0.918f), new Vector2(0.935f, 0.965f),
                () => { deckLookPeeking = true; Render(); });
        }

        deckLookCardRects.Clear();

        var row = new GameObject("Deck Look Row").AddComponent<RectTransform>();
        row.SetParent(dim, false);
        // In search mode always show all (revealed) cards in select step; rearrange never happens.
        var displayCards = (selecting || dl.SearchMode) ? dl.Cards : deckLookWorkingOrder;
        var cardSize = DeckLookCardSize(displayCards?.Count ?? 0, selecting || dl.SearchMode);
        Stretch(row, new Vector2(0.03f, 0.07f), new Vector2(0.97f, 0.875f), Vector2.zero, Vector2.zero);
        DrawDeckLookCards(row, displayCards, selecting || dl.SearchMode, cardSize);

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
        if (dl.SearchMode)
            AddOverlayButton(dim, "Take None / Shuffle", new Vector2(0.40f, 0.012f), new Vector2(0.60f, 0.058f),
                () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
        else if (selecting)
            AddOverlayButton(dim, "Take None", new Vector2(0.43f, 0.012f), new Vector2(0.57f, 0.058f),
                () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
        else
            AddOverlayButton(dim, deckLookAnimating ? "Placing..." : "Confirm Order", new Vector2(0.42f, 0.205f), new Vector2(0.58f, 0.252f),
                ConfirmDeckLookOrder, !deckLookAnimating);
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

    private Vector2 DeckLookCardSize(int count, bool selecting)
    {
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

        if (selecting && cards != null && cards.Count == 5)
        {
            int topCount = cards.Count == 5 ? 3 : 2;
            var top = DeckLookChoiceRow(row, "Deck Look Top Row", new Vector2(0f, 0.54f), new Vector2(1f, 1f), selecting ? 24f : 18f);
            var bottom = DeckLookChoiceRow(row, "Deck Look Bottom Row", new Vector2(0f, 0f), new Vector2(1f, 0.46f), selecting ? 24f : 18f);
            for (int i = 0; i < cards.Count; i++) AddDeckLookChoiceCard(i < topCount ? top : bottom, cards[i], selecting, cardSize);
            return;
        }

        if (cards != null && cards.Count > 5)
        {
            var grid = row.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cardSize;
            grid.spacing = selecting ? new Vector2(34f, 24f) : new Vector2(20f, 18f);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Min(3, Mathf.Max(1, Mathf.CeilToInt(cards.Count / 2f)));
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
        var step = count <= 1 ? 0f : Mathf.Min(cardSize.x + 18f, Mathf.Max(1f, (width - cardSize.x) / (count - 1)));
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
        if (dl == null || dl.Step != "select" || card == null) return false;
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
        if (dl.MaxCost >= 0 && def.Cost > dl.MaxCost) return false;
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
        return !string.IsNullOrEmpty(card.Owner) && GameEngine.IsPlayableNow(state, card.Owner, card);
    }

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
        Dispatch(new GameCommand { Type = "deckLookSelect", Seat = seat, Target = instanceId });
        var afterHand = state.Players.TryGetValue(seat, out var afterPlayer) ? afterPlayer.Hand.Count : -1;
        var afterStep = state.DeckLook != null ? state.DeckLook.Step : "(closed)";
        Debug.Log($"DeckLook select result: {selectedName} / {instanceId}; hand after {afterHand}; step {afterStep}.");
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
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (rect == null) yield break;

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

    private void DrawMulliganOverlay()
    {
        if (state.Status != "mulligan") return;

        var dim = PanelObject("Mulligan Dim", boardRoot, new Color32(8, 10, 14, 160));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var panel = PanelObject("Mulligan Panel", boardRoot, (Color)new Color32(14, 30, 46, 250));
        Stretch(panel, new Vector2(0.24f, 0.34f), new Vector2(0.76f, 0.66f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 2f);

        var label = TextObject("Mulligan Text", panel, "Look at your hand. Each player may mulligan once for a fresh 5.", 15, Ink, TextAnchor.MiddleCenter, titleFont);
        Stretch(label.rectTransform, new Vector2(0.04f, 0.72f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero);

        // Networked PvP: each client only sees/controls their own mulligan choice - showing
        // the opponent's here would leak whether they kept or mulliganed before it matters.
        // Hotseat/Versus Self still shows both side by side, unchanged.
        var mulliganSeats = isNetworked ? new[] { BottomSeat } : new[] { "south", "north" };
        foreach (var seat in mulliganSeats)
        {
            var p = state.Players[seat];
            var half = mulliganSeats.Length == 1
                ? new Vector2(0.06f, 0.94f)
                : (seat == BottomSeat ? new Vector2(0.06f, 0.54f) : new Vector2(0.54f, 0.94f));

            var name = TextObject(seat + " Mulligan Name", panel, p.MulliganDecided ? $"{p.Name}: ready" : $"{p.Name}: deciding...", 13, Muted, TextAnchor.MiddleCenter, monoFont);
            Stretch(name.rectTransform, new Vector2(half.x, 0.42f), new Vector2(half.y, 0.68f), Vector2.zero, Vector2.zero);

            if (p.MulliganDecided) continue;
            var row = RowObject(seat + " Mulligan Buttons", panel, 10, TextAnchor.MiddleCenter);
            Stretch(row, new Vector2(half.x, 0.08f), new Vector2(half.y, 0.40f), Vector2.zero, Vector2.zero);
            AddButton(row, "Mulligan", () => Dispatch(new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = true }));
            AddButton(row, "Keep Hand", () => Dispatch(new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = false }));
        }
    }

    private void DrawCoinFlipOverlay()
    {
        if (state.Status != "coinflip") { coinFlipWaitingText = null; return; }

        var dim = PanelObject("Coin Flip Dim", boardRoot, new Color32(8, 10, 14, 200));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var panel = PanelObject("Coin Flip Panel", boardRoot, (Color)new Color32(14, 30, 46, 250));
        Stretch(panel, new Vector2(0.30f, 0.38f), new Vector2(0.70f, 0.62f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 2f);

        var winner = state.Players[state.CoinFlipWinner];

        // Networked PvP: only the coin-flip winner sees the Go First/Second choice - the other
        // client gets a waiting message (with an animated ellipsis, see Update()) instead of a
        // second copy of buttons they have no business clicking. Hotseat/Versus Self is
        // unaffected (isNetworked is false there), matching how mulligan was scoped earlier.
        if (isNetworked && state.CoinFlipWinner != localSeat)
        {
            coinFlipWaitingBaseMessage = $"Waiting for {winner.Name} to decide going first or second";
            var waitLabel = TextObject("Coin Flip Text", panel, coinFlipWaitingBaseMessage,
                16, Ink, TextAnchor.MiddleCenter, titleFont);
            waitLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(waitLabel.rectTransform, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
            coinFlipWaitingText = waitLabel;
            return;
        }

        coinFlipWaitingText = null;
        var label = TextObject("Coin Flip Text", panel, $"{winner.Name} won the coin flip!\nGoing first or second?", 16, Ink, TextAnchor.MiddleCenter, titleFont);
        Stretch(label.rectTransform, new Vector2(0.04f, 0.55f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero);

        var buttons = RowObject("Coin Flip Buttons", panel, 14, TextAnchor.MiddleCenter);
        Stretch(buttons, new Vector2(0.10f, 0.12f), new Vector2(0.90f, 0.48f), Vector2.zero, Vector2.zero);
        AddButton(buttons, "Go First", () => Dispatch(new GameCommand { Type = "chooseTurnOrder", Seat = state.CoinFlipWinner, GoingFirst = true }));
        AddButton(buttons, "Go Second", () => Dispatch(new GameCommand { Type = "chooseTurnOrder", Seat = state.CoinFlipWinner, GoingFirst = false }));
    }

    private void DrawTableSurface()
    {
        // Full-bleed coloured playmat background spanning the ENTIRE window: north (blue) fills the top
        // half, south (green) the bottom half, with a thin centre divide. The play space (zones, cards)
        // is drawn at full size on top of this in playRoot; the colour bleeds out to every edge.
        // Cobalt felt: dark navy (north) over dark teal (south), full-bleed behind the opaque menus.
        var north = PanelObject("North Table Wash", boardRoot, MatTop);
        Stretch(north, new Vector2(0f, 0.5f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        AddBoardCancel(north);

        var south = PanelObject("South Table Wash", boardRoot, MatBottom);
        Stretch(south, Vector2.zero, new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        AddBoardCancel(south);

        // Everything below is confined to the centered play column (x 0.17-0.83 of the window).
        // Soft center brightening (radial felt lift).
        var glow = new GameObject("Table Center Glow").AddComponent<RectTransform>();
        glow.SetParent(boardRoot, false);
        glow.anchorMin = new Vector2(0.20f, 0.06f);
        glow.anchorMax = new Vector2(0.80f, 0.94f);
        glow.offsetMin = Vector2.zero; glow.offsetMax = Vector2.zero;
        var glowImg = glow.gameObject.AddComponent<RawImage>();
        glowImg.texture = GetSoftGlowTexture(0.5f);
        glowImg.color = new Color(0.35f, 0.55f, 0.66f, 0.12f);
        glowImg.raycastTarget = false;

        // (Active-side glow removed - the active player's empty-slot placement indicators are the
        // whose-turn cue now.)

        // Soft cyan glow behind the centre pill (the mock's box-shadow halo).
        var crestGlow = new GameObject("Crest Glow").AddComponent<RectTransform>();
        crestGlow.SetParent(boardRoot, false);
        crestGlow.anchorMin = crestGlow.anchorMax = new Vector2(0.5f, 0.5f);
        crestGlow.pivot = new Vector2(0.5f, 0.5f);
        crestGlow.sizeDelta = new Vector2(250f, 64f);
        crestGlow.anchoredPosition = Vector2.zero;
        var crestGlowImg = crestGlow.gameObject.AddComponent<RawImage>();
        crestGlowImg.texture = GetSoftGlowTexture(0.5f);
        crestGlowImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.10f);
        crestGlowImg.raycastTarget = false;

        // Center crest pill: dark, rounded, cyan-bordered (not a filled cyan bar).
        // Match the other text-block backgrounds (LogBgDark rgb) but keep full alpha so the seam
        // behind the pill stays masked.
        var crest = PanelObject("Center Crest", boardRoot, (Color)new Color32(14, 30, 46, 255));
        crest.anchorMin = crest.anchorMax = new Vector2(0.5f, 0.5f);
        crest.pivot = new Vector2(0.5f, 0.5f);
        crest.sizeDelta = new Vector2(196f, 24f);
        crest.anchoredPosition = Vector2.zero;
        Round(crest);
        AddRoundedCardBorder(crest, Accent, 1.2f);
        string activeName = state.Players.TryGetValue(state.ActiveSeat, out var activePlayer) && activePlayer != null
            ? activePlayer.Name : state.ActiveSeat;
        var crestText = TextObject("Center Crest Text", crest,
            $"TURN {state.TurnNumber}    ·    {activeName.ToUpper()}'S TURN", 10, Ink, TextAnchor.MiddleCenter, monoFont);
        Stretch(crestText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Scale the pill (and its glow) to the player-name length; the centre pivot keeps it
        // locked to the middle of the playmat regardless of width.
        float crestW = Mathf.Max(160f, crestText.preferredWidth + 34f);
        crest.sizeDelta = new Vector2(crestW, 24f);
        crestGlow.sizeDelta = new Vector2(crestW + 54f, 64f);

        // Center seam: two cyan segments that taper to transparent toward the outer ends and are
        // brightest toward the centre. Each runs ALL the way to the board centre (behind the pill);
        // the opaque pill is then drawn on top and masks the inner overlap, so the visible line
        // always meets the box edge exactly - no fixed offset to tune, snaps to any pill width.
        var seamCol = new Color(Accent.r, Accent.g, Accent.b, 0.55f);

        var seamL = new GameObject("Center Seam L").AddComponent<RectTransform>();
        seamL.SetParent(boardRoot, false);
        seamL.anchorMin = new Vector2(0.208f, 0.4988f);
        seamL.anchorMax = new Vector2(0.5f, 0.5012f);
        seamL.offsetMin = Vector2.zero; seamL.offsetMax = Vector2.zero;
        var seamLImg = seamL.gameObject.AddComponent<RawImage>();
        seamLImg.texture = GetHGradientTexture();        // alpha 0 (left/outer) -> 1 (right/centre)
        seamLImg.color = seamCol;
        seamLImg.raycastTarget = false;

        var seamR = new GameObject("Center Seam R").AddComponent<RectTransform>();
        seamR.SetParent(boardRoot, false);
        seamR.anchorMin = new Vector2(0.5f, 0.4988f);
        seamR.anchorMax = new Vector2(0.792f, 0.5012f);
        seamR.offsetMin = Vector2.zero; seamR.offsetMax = Vector2.zero;
        var seamRImg = seamR.gameObject.AddComponent<RawImage>();
        seamRImg.texture = GetHGradientTexture();
        seamRImg.uvRect = new Rect(1f, 0f, -1f, 1f);     // flip: alpha 1 (left/centre) -> 0 (right/outer)
        seamRImg.color = seamCol;
        seamRImg.raycastTarget = false;

        // Pill (and its label) render above the seam and mask the inner overlap so the line meets the
        // box edge precisely at any width. The glow is intentionally NOT raised - it stays in creation
        // order (before playRoot) so it sits underneath the character areas.
        crest.SetAsLastSibling();

        // Field corner brackets (cyan L's) at the four extreme corners of the whole play space. The arms
        // hug the outer perimeter (top/bottom + side edges) rather than cutting through the middle of the
        // board, and taper inward like the centre seam.
        // (Corner vertical lines removed - visual clutter.)
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
        Stretch(topHalf, new Vector2(0, 0.50f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        AddBoardCancel(topHalf);
        var topEventDrop = topHalf.gameObject.AddComponent<EventDrop>();
        topEventDrop.Init(this, TopSeat);
        var bottomHalf = PanelObject("South Playmat Half", mat, new Color(0, 0, 0, 0));
        Stretch(bottomHalf, Vector2.zero, new Vector2(1, 0.50f), Vector2.zero, Vector2.zero);
        AddBoardCancel(bottomHalf);
        var bottomEventDrop = bottomHalf.gameObject.AddComponent<EventDrop>();
        bottomEventDrop.Init(this, BottomSeat);

        DrawMatHalf(topHalf, state.Players[TopSeat], TopSeat, true);
        DrawMatHalf(bottomHalf, state.Players[BottomSeat], BottomSeat, false);
    }

    private void DrawMatHalf(RectTransform half, PlayerState p, string seat, bool top)
    {
        // (Removed the gold active-side highlight per design — both halves stay the same felt.)

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
        var donRow = new GameObject("Cost DON Cards").AddComponent<RectTransform>();
        donRow.SetParent(cost, false);
        Stretch(donRow, new Vector2(0.02f, 0.06f), new Vector2(0.98f, 0.94f), Vector2.zero, Vector2.zero);
        if (state != null && seat == state.ActiveSeat && donGroupSizes.Count > 1)
            DrawGroupedDonRow(donRow, p.CostArea, seat, top);
        else
            DrawFittedDonRow(donRow, p.CostArea, seat, top);
        // ("No DON!!" placeholder text removed per design.)

        var leaderMin = top ? new Vector2(0.445f, 0.36f) : new Vector2(0.445f, 0.34f);
        var leaderMax = top ? new Vector2(0.555f, 0.66f) : new Vector2(0.555f, 0.64f);
        var leader = MatZone(half, "LEADER", leaderMin, leaderMax, new Color32(217, 224, 210, 235), top);
        AddCardToZone(leader, p.Leader, seat, true, top);
        var leaderSz = FittedCardSize(leader);
        SnugZone(leader, leaderMin, leaderMax, leaderSz.x * 1.06f, leaderSz.y * 1.06f);

        var stageMin = top ? new Vector2(0.31f, 0.36f) : new Vector2(0.58f, 0.34f);
        var stageMax = top ? new Vector2(0.42f, 0.66f) : new Vector2(0.69f, 0.64f);
        var stage = MatZone(half, "STAGE", stageMin, stageMax, new Color32(151, 179, 92, 220), top);
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
        var deckSz = FittedCardSize(deck);
        float deckDepth = p.Deck.Count > 0 ? StackDepth(p.Deck.Count) : 0f;
        SnugZone(deck, deckMin, deckMax, deckSz.x * 1.06f, deckSz.y * 1.06f + deckDepth);

        // DON!! deck mid-side beside the leader row (reference mat).
        var donDeckMin = top ? new Vector2(0.87f, 0.69f) : new Vector2(0.00f, 0.02f);
        var donDeckMax = top ? new Vector2(1.00f, 0.98f) : new Vector2(0.13f, 0.31f);
        var donDeck = MatZone(half, "DON!! DECK", donDeckMin, donDeckMax, new Color32(221, 226, 211, 225), top);
        AddDonDeckPileToZone(donDeck, p.DonDeck, top);
        var donSz = FittedCardSize(donDeck);
        float donDepth = p.DonDeck > 0 ? StackDepth(p.DonDeck) : 0f;
        SnugZone(donDeck, donDeckMin, donDeckMax, donSz.x * 1.06f, donSz.y * 1.06f + donDepth);

        var trashMin = top ? new Vector2(0.00f, 0.69f) : new Vector2(0.87f, 0.02f);
        var trashMax = top ? new Vector2(0.13f, 0.98f) : new Vector2(1.00f, 0.31f);
        var trash = MatZone(half, "TRASH", trashMin, trashMax, new Color32(172, 169, 73, 220), top);
        AddPileCardToZone(trash, "Trash", p.Trash.Count, false, p.Trash.LastOrDefault(), top);
        var trashSz = FittedCardSize(trash);
        float trashDepth = p.Trash.Count > 1 ? StackDepth(p.Trash.Count) : 0f;
        SnugZone(trash, trashMin, trashMax, trashSz.x * 1.06f, trashSz.y * 1.06f + trashDepth);

        // Life in the player-edge corner (reference mat).
        // Life: tall column up the left side, flanking the character row.
        var lifeMin = top ? new Vector2(0.85f, 0.03f) : new Vector2(0.02f, 0.40f);
        var lifeMax = top ? new Vector2(0.98f, 0.60f) : new Vector2(0.15f, 0.97f);
        var life = MatZone(half, "LIFE", lifeMin, lifeMax, new Color32(226, 230, 216, 235), top);
        AddLifeStackToZone(life, p.Life.Count, top);
    }

    private void DrawExternalHand(PlayerState p, string seat, bool top)
    {
        var panel = PanelObject(seat + " External Hand", playRoot, new Color(0, 0, 0, 0));
        Stretch(panel, top ? new Vector2(0.10f, 0.885f) : new Vector2(0.10f, -0.080f), top ? new Vector2(0.90f, 1.080f) : new Vector2(0.90f, 0.115f), Vector2.zero, Vector2.zero);
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
        Stretch(row, new Vector2(0.02f, top ? 0.10f : 0.02f), new Vector2(0.98f, top ? 0.98f : 0.90f), Vector2.zero, Vector2.zero);
        DrawFannedHandRow(row, p.Hand, seat + "-hand", top);
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

        var nm = TextObject("Preview Name", leftRoot, def != null ? def.Name : "-", 16, Ink, TextAnchor.LowerLeft, titleFont);
        Stretch(nm.rectTransform, new Vector2(0.06f, 0.602f), new Vector2(0.94f, 0.628f), Vector2.zero, Vector2.zero);

        var chips = RowObject("Preview Chips", leftRoot, 5, TextAnchor.MiddleLeft);
        Stretch(chips, new Vector2(0.06f, 0.566f), new Vector2(0.94f, 0.594f), Vector2.zero, Vector2.zero);
        if (def != null)
        {
            AddPreviewChip(chips, def.Type != null ? def.Type.ToUpper() : "CARD", true);
            if (!string.IsNullOrEmpty(def.Color)) AddPreviewChip(chips, def.Color.ToUpper(), false);
            if (def.Life.HasValue) AddPreviewChip(chips, "LIFE " + def.Life.Value, false);
            else AddPreviewChip(chips, (def.Type == "leader" ? "" : "COST " + def.Cost), false);
            // Power last - not every card has it.
            if (showPower) AddPreviewChip(chips, "POWER " + GameEngine.GetPower(state, focus), false);
        }

        var effBox = PanelObject("Preview Effect", leftRoot, LogBgDark);
        Stretch(effBox, new Vector2(0.06f, 0.45f), new Vector2(0.94f, 0.552f), Vector2.zero, Vector2.zero);
        RoundBig(effBox);
        AddRoundedCardBorder(effBox, MenuB, 1f);
        var eff = TextObject("Preview Effect Text", effBox, def != null && !string.IsNullOrEmpty(def.Effect) ? def.Effect : "", 11, Ink, TextAnchor.UpperLeft);
        eff.horizontalOverflow = HorizontalWrapMode.Wrap; eff.verticalOverflow = VerticalWrapMode.Truncate;
        Stretch(eff.rectTransform, new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f), Vector2.zero, Vector2.zero);

        DrawCombatLogPanel();
    }

    private void AddPreviewChip(RectTransform parent, string label, bool primary)
    {
        if (string.IsNullOrEmpty(label)) return;
        var chip = PanelObject(label + " Chip", parent, primary ? Accent : new Color(1f, 1f, 1f, 0.04f));
        float chipW = 12f + label.Length * 6.6f;
        chip.sizeDelta = new Vector2(chipW, 18f);   // RowObject's HLG sizes by sizeDelta, not LayoutElement
        SetPreferred(chip, new Vector2(chipW, 18f));
        Round(chip);
        if (!primary) AddRoundedCardBorder(chip, ZoneBorder, 1f);
        var t = TextObject("t", chip, label, 9, primary ? BadgeInk : Ink, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
    }

    private static readonly System.Text.RegularExpressions.Regex LogCardIdRx =
        new System.Text.RegularExpressions.Regex(@"\[[A-Za-z0-9]{1,6}-\d{1,4}\]");

    private void DrawCombatLogPanel()
    {
        var logLabel = TextObject("Combat Log Label", leftRoot, "COMBAT LOG", 9, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(logLabel.rectTransform, new Vector2(0.06f, 0.425f), new Vector2(0.94f, 0.443f), Vector2.zero, Vector2.zero);

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
            string actorName = null;
            if (!string.IsNullOrEmpty(e.Actor) && e.Actor != "system" &&
                state.Players.TryGetValue(e.Actor, out var actorP) && actorP != null)
                actorName = actorP.Name;
            BuildLogEntry(content, actorName, e.Message, width);
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
        var start = sourceCenter + dir * RectScreenRadius(source, 0.42f);
        var end = targetCenter - dir * RectScreenRadius(target, 0.50f);
        // Arc bows toward whichever side the target sits on, scaled by the horizontal offset - so a
        // dead-ahead (vertical) shot draws as a straight arrow, while shots to the left or right sweep
        // out to their respective side.
        var bow = Mathf.Clamp((end.x - start.x) * 0.42f, -130f, 130f);
        var control = (start + end) * 0.5f + new Vector2(bow, 0f);

        const int segments = 18;
        var last = start;
        for (int i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var next = Bezier(start, control, end, t);
            var width = Mathf.Lerp(thickness * 1.25f, thickness * 0.72f, t);
            AddArrowSegment(root, last, next, new Color(color.r, color.g, color.b, 0.20f), width + 11f);
            AddArrowSegment(root, last, next, color, width);
            last = next;
        }

        var tipDir = (end - Bezier(start, control, end, 0.94f)).normalized;
        AddArrowHead(root, end, tipDir, color, thickness * 3.1f);
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
        var start = sourceCenter + dir * RectScreenRadius(source, 0.42f);
        var end = screenPoint;
        // Arc bows toward whichever side the target sits on, scaled by the horizontal offset - so a
        // dead-ahead (vertical) shot draws as a straight arrow, while shots to the left or right sweep
        // out to their respective side.
        var bow = Mathf.Clamp((end.x - start.x) * 0.42f, -130f, 130f);
        var control = (start + end) * 0.5f + new Vector2(bow, 0f);
        const int segments = 18;
        var last = start;
        for (int i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var next = Bezier(start, control, end, t);
            var width = Mathf.Lerp(thickness * 1.25f, thickness * 0.72f, t);
            AddArrowSegment(root, last, next, new Color(color.r, color.g, color.b, 0.20f), width + 11f);
            AddArrowSegment(root, last, next, color, width);
            last = next;
        }
        var tipDir = (end - Bezier(start, control, end, 0.94f)).normalized;
        AddArrowHead(root, end, tipDir, color, thickness * 3.1f);
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

    private void AddLifeStackToZone(RectTransform zone, int count, bool top)
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
            RoundedCardVisual("Life Back", card, GetBackSprite(), out var img);
            img.raycastTarget = false;
            card.gameObject.AddComponent<BackHover>().Init(this);
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
        if (p.Stage != null) AddCardToZone(stage, p.Stage, seat, true);
        else AddCenteredText(stage, "Stage");
        x += step;

        boardDeckPileRects[seat] = AddPile(root, "Deck", p.Deck.Count, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), true);
        x += step;
        AddPile(root, "DON!! Deck", p.DonDeck, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), false);
        x += step;
        AddPile(root, "Life", p.Life.Count, new Vector2(x, yMin), new Vector2(x + 0.076f, yMax), true);
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

        DrawMenuButton();

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

        DrawChatPanel();
        DrawPlayerPlate(sideRoot, state.Players.ContainsKey(BottomSeat) ? state.Players[BottomSeat] : null, BottomSeat, state.ActiveSeat == BottomSeat, new Vector2(0.06f, 0.094f), new Vector2(0.94f, 0.138f));
        AddEndTurnPanel();

        // Built last so the dropdown overlays everything else in the side panel.
        if (menuOpen) DrawGameMenu();
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
        // Taller now that there are three items (New Match / Main Menu / Close).
        Stretch(menu, new Vector2(0.52f, 0.74f), new Vector2(0.965f, 0.95f), Vector2.zero, Vector2.zero);
        RoundBig(menu);
        AddRoundedCardBorder(menu, Accent, 1.3f);
        menu.SetAsLastSibling();

        AddMenuItem(menu, "New Match", new Vector2(0.07f, 0.70f), new Vector2(0.93f, 0.95f),
            () => { menuOpen = false; NewMatch(); });
        AddMenuItem(menu, "Main Menu", new Vector2(0.07f, 0.385f), new Vector2(0.93f, 0.635f),
            () => { menuOpen = false; ReturnToMenu(); });
        AddMenuItem(menu, "Close", new Vector2(0.07f, 0.05f), new Vector2(0.93f, 0.30f),
            () => { menuOpen = false; Render(); });
    }

    // Tears down the board and rebuilds the main menu in this same scene
    // (single-scene design — mirror of MainMenuManager.StartVersusSelfFlow).
    public void ReturnToMenu()
    {
        // Custom versus-self decks only apply to the match they were picked for;
        // leaving the board falls back to the ST01-vs-ST02 default next time.
        PendingSouthDeckId = null;
        PendingNorthDeckId = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        MainMenuManager.EnsureMenu();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
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
        var phases = new (string label, bool active)[] {
            ("REFRESH", ph == "refresh"),
            ("DRAW",    ph == "draw"),
            ("DON",     ph == "don"),
            ("MAIN",    ph == "main"),
            ("ATTACK",  ph == "battle"),
            ("END",     ph == "end" || ph == "endTurn"),
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

        string name = p != null ? p.Name : seat;
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

    private void SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        chatMessages.Add(new ChatMessage { Sender = "You", Text = text.Trim(), Mine = true });
        Render();
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
        var enabled = CanEndTurn();
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
        var viewport = PanelObject("Action Viewport", panel, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f), Vector2.zero, Vector2.zero);
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

        if (state.DeckLook != null)
        {
            selectedId = null;
            selectedSeat = null;
            var dl = state.DeckLook;
            var selecting = dl.Step == "select";
            if (dl.SearchMode)
            {
                string costHint = dl.MaxCost >= 0 ? $" (cost ≤ {dl.MaxCost})" : "";
                AddInfo(body, $"{dl.SourceName}: search the deck — click a card to add to hand{costHint}.");
                AddButton(body, "Take None / Shuffle", () => Dispatch(new GameCommand { Type = "deckLookSelect", Seat = dl.Seat, Target = null }));
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
        if (trashViewSeat != null)
        {
            state.Players.TryGetValue(trashViewSeat, out var tvp);
            var trash = tvp?.Trash ?? new List<CardInstance>();
            AddInfo(body, $"{trashViewSeat} trash ({trash.Count} cards):");
            for (int ti = trash.Count - 1; ti >= 0; ti--)
            {
                var tc = trash[ti];
                var td = GameEngine.GetCard(tc);
                AddInfo(body, $"  [{td.Cost}] {td.Name} ({td.Type})");
            }
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
        if (!string.IsNullOrEmpty(selectedId) && selectedSeat != null && selectedSeat.EndsWith("-hand"))
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
                bool canAfford = hp2 != null && freeDon2 >= hDef.Cost;
                string costHint = canAfford ? "" : $"  (need {hDef.Cost} DON!!, have {freeDon2})";

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
        if (!string.IsNullOrEmpty(selectedId) && !string.IsNullOrEmpty(selectedSeat) && selectedSeat == state.ActiveSeat)
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
                    AddButton(body, abilUsed ? "Activate: Main (used)" : "Activate: Main",
                        () => Dispatch(new GameCommand { Type = "activateMain", Seat = selectedSeat, Target = selectedId }),
                        !abilUsed);
                }

                int freeDon = GameEngine.ActiveDonCount(state.Players[selectedSeat]);
                if (freeDon > 0)
                {
                    AddButton(body, "Attach 1 DON!!",
                        () => Dispatch(new GameCommand { Type = "attachDon", Seat = selectedSeat, Target = selectedId, Amount = 1 }));
                    AddButton(body, $"Attach All DON!! ({freeDon})",
                        () => Dispatch(new GameCommand { Type = "attachDon", Seat = selectedSeat, Target = selectedId, Amount = freeDon }));
                }

                if (selDef.Type != "leader")
                    AddButton(body, selected.Rested ? "Set Active" : "Rest",
                        () => Dispatch(new GameCommand { Type = selected.Rested ? "unrest" : "rest", Seat = selectedSeat, Target = selectedId }));

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
        AddInfo(body, "Click one of your board cards to select it, or play cards from your hand.");
        foreach (var kvp in state.Players)
        {
            string seat = kvp.Key;
            int tCount = kvp.Value.Trash.Count;
            var captSeat = seat;
            AddButton(body, $"View {seat} Trash ({tCount})", () => { trashViewSeat = captSeat; Render(); });
        }
    }

    private void DrawPendingEffectActions(RectTransform body)
    {
        var effect = state.PendingEffects[0];
        var source = CardData.GetCard(effect.SourceCardId);
        AddEffectCardVisual(body, effect.SourceCardId);
        AddInfo(body, source.Name + " - " + EffectTimingLabel(effect.Timing));
        AddInfo(body, effect.Text);

        string targetHint;
        if (effect.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Hand)
            targetHint = "Click a card in your hand as the target.";
        else if (effect.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Trash)
            targetHint = "Click a card in your trash as the target.";
        else if (effect.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Any)
            targetHint = "Click a target card on the board or in your hand.";
        else
            targetHint = "Click a valid target on the board if the effect needs one.";
        AddInfo(body, targetHint);

        // For trash-targeting effects: show each trash card as a clickable button so the player
        // doesn't have to navigate a pile.  This mirrors how hand cards are routed above.
        if (effect.TargetZone == OnePieceTcg.Engine.EffectTargetZone.Trash
            && state.Players.TryGetValue(effect.Seat, out var trashOwner) && trashOwner.Trash.Count > 0)
        {
            AddInfo(body, "Select from your trash:");
            foreach (var tc in trashOwner.Trash)
            {
                var captured = tc;
                var tcDef = GameEngine.GetCard(captured);
                AddButton(body, $"{tcDef.Name} (Cost {tcDef.Cost})", () => Dispatch(new GameCommand
                {
                    Type = "resolveEffect",
                    Seat = effect.Seat,
                    EffectId = effect.EffectId,
                    Target = captured.InstanceId,
                }));
            }
        }

        AddButton(body, "Resolve / Manual", () => Dispatch(new GameCommand { Type = "resolveEffect", Seat = effect.Seat, EffectId = effect.EffectId }));
        AddButton(body, "Skip Effect", () => Dispatch(new GameCommand { Type = "passEffect", Seat = effect.Seat, EffectId = effect.EffectId }), effect.Optional);
    }

    private void DrawChoiceActions(RectTransform body)
    {
        var ch = state.ActiveChoice;
        var source = CardData.GetCard(ch.SourceCardId);
        AddEffectCardVisual(body, ch.SourceCardId);
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
            default: return string.IsNullOrEmpty(timing) ? "Effect" : timing;
        }
    }

    private void DrawBattleActions(RectTransform body)
    {
        var b = state.Battle;
        var defender = state.Players[b.TargetSeat];
        AddInfo(body, StepLabel(b.Step));

        if (b.Step == "block")
        {
            AddInfo(body, "Click an active blocker on the defending board, or pass blockers.");
            AddButton(body, "Pass Blockers", () => Dispatch(new GameCommand { Type = "passBlock", Seat = b.TargetSeat }));
        }
        else if (b.Step == "counter")
        {
            AddInfo(body, $"Counter total: +{b.CounterPower}");
            AddInfo(body, "Click counter cards in the defending hand. Each click adds that card's counter value.");
            AddButton(body, "Done Countering", () => Dispatch(new GameCommand { Type = "passCounter", Seat = b.TargetSeat }));
        }
        else if (b.Step == "damage")
        {
            AddInfo(body, "Counter window closed. Resolve damage or battle result.");
            AddButton(body, "Resolve Attack", () => Dispatch(new GameCommand { Type = "resolveAttack", Seat = b.AttackerSeat }));
        }
        else if (b.Step == "trigger")
        {
            var revealed = b.RevealedLife != null ? GameEngine.GetCard(b.RevealedLife) : null;
            AddInfo(body, "Revealed: " + (revealed != null ? revealed.Name : "?"));
            if (revealed != null && !string.IsNullOrWhiteSpace(revealed.Trigger)) AddInfo(body, revealed.Trigger);
            AddButton(body, "Resolve Trigger", () => Dispatch(new GameCommand { Type = "useTrigger", Seat = b.TargetSeat }));
            AddButton(body, "Pass Trigger", () => Dispatch(new GameCommand { Type = "passTrigger", Seat = b.TargetSeat }));
        }
    }

    private void OnCardClick(CardInstance card, string seat)
    {
        // Lock the docked left card-preview onto whatever card was just clicked.
        if (card != null) previewLockCard = card;
        if (state.DeckLook != null)
        {
            // Only the "select" step is click-driven; "rearrange" is drag-to-reorder (see
            // DeckLookCardDrag) plus an explicit Confirm button, not handled here.
            if (state.DeckLook.Step == "select")
                SelectDeckLookCard(card.InstanceId);
            return;
        }

        if (selectedDonIds.Count > 0)
        {
            AttachSelectedDonTo(seat, card);
            return;
        }

        if (seat != null && seat.EndsWith("-hand"))
        {
            var handSeat = seat.Replace("-hand", "");
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
        if (GameEngine.ActiveDonCount(player) < def.Cost) return false;
        if (def.Type == "event") return HasMainEffect(def);
        if (def.Type == "stage") return true;
        return player.CharacterArea.Any(slot => slot == null);
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
        return def.Type == "event" && HasMainEffect(def) && GameEngine.ActiveDonCount(player) >= def.Cost;
    }

    private static bool HasMainEffect(CardDef def)
    {
        return def != null && def.Type == "event" && !string.IsNullOrWhiteSpace(def.Effect) && def.Effect.IndexOf("[Main]", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void PlayCharacterInSlot(CardInstance card, string handSeat, int slotIndex)
    {
        if (GameEngine.GetCard(card).Type != "character") return;
        if (!CanDragHandCard(card, handSeat) || !CanAcceptCharacterDrop(handSeat, slotIndex)) return;
        Dispatch(new GameCommand { Type = "playCard", Seat = handSeat, InstanceId = card.InstanceId, SlotIndex = slotIndex });
    }

    private void PlayStage(CardInstance card, string handSeat)
    {
        if (GameEngine.GetCard(card).Type != "stage") return;
        if (!CanDragHandCard(card, handSeat) || !CanAcceptStageDrop(handSeat)) return;
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
        if (IsCardUsableNow(card)) AddUsableGlow(handHoverRoot); else AddMysticalCardOutline(handHoverRoot, true);
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
        AddCard(holder, card, seat, faceUp, Vector2.zero, true);
    }

    private void AddCard(RectTransform parent, CardInstance card, string seat, bool faceUp, Vector2 size, bool stretch = false, bool inverted = false, int handHomeSiblingIndex = -1, bool suppressPreview = false)
    {
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

        RoundedCardVisual("Art", cardBody, faceUp ? GetCardSprite(card.CardId) : GetBackSprite(), out var image);

        if (card.Rested)
        {
            // Rested cards turn 90deg (the standard cue) but stay fully opaque - dimming them let the
            // DON tucked behind the character show through, which read as a visual bug.
            cardBody.localRotation = Quaternion.Euler(0, 0, 90f);
        }

        if (faceUp)
        {
            if (card.AttachedDonIds.Count > 0) AddBadge(cardBody, "+" + card.AttachedDonIds.Count, new Vector2(0.62f, 0.82f), new Vector2(0.98f, 0.98f), new Color32(13, 68, 34, 230));
        }

        if (faceUp && IsDonAttachTarget(seat, card))
        {
            cardBody.gameObject.AddComponent<DonAttachTarget>().Init(this, seat, card);
            if (selectedDonIds.Count > 0 && selectedDonSeat == seat)
                AddOutline(cardBody.gameObject, new Color32(255, 214, 112, 210), 1.6f);
        }

        // The green "usable" glow appears only when the mouse hovers a valid card (handled by
        // CardHover), not persistently on every legal pick.

        var button = cardBody.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => OnCardClick(card, seat));

        var hover = cardBody.gameObject.AddComponent<CardHover>();
        hover.Init(this, card, faceUp, seat != null && seat.EndsWith("-hand"), handHomeSiblingIndex, suppressPreview);

        // Drag-to-attack: drag from your own leader/character to an opponent card; the arrow
        // follows the cursor and the attack is declared on release. (Click-to-attack still works.)
        if (faceUp && IsAttackableBoardCard(card, seat))
            cardBody.gameObject.AddComponent<AttackDrag>().Init(this, canvas, card, seat);

        if (faceUp && seat != null && seat.EndsWith("-hand"))
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
        int attachedShown = Mathf.Min(attachedDon, 8);
        if (attachedShown > 0)
        {
            float donW = boardCardSize.x * 0.66f;
            float donH = boardCardSize.y * 0.66f;
            float span = boardCardSize.x;                         // never exceed the character's width
            float step = attachedShown > 1 ? (span - donW) / (attachedShown - 1) : 0f;
            float total = donW + step * (attachedShown - 1);
            float startX = -total * 0.5f + donW * 0.5f;
            float centerY = -donH * 0.5f;                         // top edge sits at the card's middle
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
            // In a networked match the top-rendered hand is always the remote opponent's (see
            // BottomSeat/TopSeat) - hide it as card backs, same as any other face-down pile.
            // Hotseat/Versus Self is unaffected (isNetworked is false there).
            bool handFaceUp = !(isNetworked && top);
            AddCard(holder, cards[i], seat, handFaceUp, Vector2.zero, true, top, count - 1 - i);
            holders[i] = holder;
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
        float cardW = boardCardSize.x > 1f ? boardCardSize.x : 70f;
        float cardH = boardCardSize.y > 1f ? boardCardSize.y : cardW / CardAspect;
        float availW = parent.rect.width > 1f ? parent.rect.width : (boardRoot != null ? boardRoot.rect.width * 0.55f : 600f);
        float maxH = parent.rect.height > 1f ? parent.rect.height * 0.96f : cardH;
        if (cardH > maxH) { cardH = maxH; cardW = cardH * CardAspect; }
        float donStep = cardW * 0.34f;
        if (cardW + (n - 1) * donStep > availW) donStep = (availW - cardW) / Mathf.Max(1, n - 1);
        float donStart = -((n - 1) * donStep) * 0.5f;
        for (int i = 0; i < n; i++)
            AddCostDon(parent, donCards[i], seat, i, donStart + i * donStep, cardW, cardH, inverted, -1);
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
        return state.Status == "active" && state.Phase == "main" && state.Battle == null && state.PendingEffects.Count == 0;
    }

    private void AddButton(RectTransform parent, string label, UnityEngine.Events.UnityAction action, bool enabled = true, bool dot = true)
    {
        var root = PanelObject(label + " Button", parent, enabled ? new Color32(34, 58, 78, 235) : new Color32(24, 34, 44, 170));
        SetPreferred(root, new Vector2(118, 34));
        root.sizeDelta = new Vector2(118, 34);
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
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(26, 0), new Vector2(-8, 0));
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

    private void AddCenteredText(RectTransform parent, string message)
    {
        var text = TextObject("Empty", parent, message, 13, Muted, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.72f), Vector2.zero, Vector2.zero);
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
            AddCard(holder, card, seat, true, Vector2.zero, true, inverted);
    }

    private void AddBadge(RectTransform parent, string label, Vector2 min, Vector2 max, Color bg)
    {
        var badge = PanelObject("Badge", parent, bg);
        Stretch(badge, min, max, Vector2.zero, Vector2.zero);
        var text = TextObject("Badge Text", badge, label, 11, Color.white, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
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
                }
                else
                {
                    ExitInHandReorder(true);
                    CreateGhostAndArrow(eventData);
                }
                return;
            }
            MoveGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            manager.isDraggingHandCard = false;

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
            // Event cards just drag up to play - no targeting arrow at characters/stage. Their effect's
            // targeting arrows appear later (once the card is in trash and resolving).
            if (GameEngine.GetCard(card).Type != "event") CreateArrow();
            MoveGhost(eventData);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (ghost == null) return;
            ghost.transform.position = eventData.position;
            UpdateArrow(eventData);
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
            // Green rim when usable, RED rim when it's an in-zone-but-illegal effect target, otherwise
            // the normal gold hover glow.
            if (manager.IsCardUsableNow(card))
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






















