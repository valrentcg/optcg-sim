// MainMenuManager.cs
// Main Menu screen for One Piece TCG Simulator.
// Pure procedural uGUI — same helpers, palette, and rebuild pattern as GameManager.cs.
// No prefabs, no UI Toolkit, no external packages.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    // ── Palette — exact Color32 constants from GameManager ─────────────────────
    private static readonly Color Ink        = new Color32(238, 242, 247, 255);
    private static readonly Color Muted      = new Color32(159, 171, 190, 255);
    private static readonly Color Accent     = new Color32( 79, 195, 224, 255);
    private static readonly Color Accent2    = new Color32(134, 214, 238, 255);
    private static readonly Color BadgeInk   = new Color32(  6,  32,  44, 255);
    private static readonly Color MenuBg     = new Color32( 12,  23,  38, 255);
    private static readonly Color LogBgDark  = new Color32( 14,  30,  46, 184);
    private static readonly Color MenuB      = new Color32(120, 180, 220,  46);
    private static readonly Color ZoneBorder = new Color32(120, 180, 220,  66);
    private static readonly Color Gold       = new Color32(226, 190, 102, 255);
    private static readonly Color RedAccent  = new Color32(230,  84,  84, 255);
    private static readonly Color MatTop     = new Color32( 13,  33,  60, 255);
    private static readonly Color MatBottom  = new Color32( 13,  38,  50, 255);

    // ── Mode data model ────────────────────────────────────────────────────────
    private enum ModeStatus { Ready, Dev, Soon }

    private sealed class MenuMode
    {
        public string Id;
        public string Parent;
        public string Label;
        public string Launch;
        public ModeStatus Status;
    }

    private readonly MenuMode[] modes = new MenuMode[]
    {
        new MenuMode { Id = "soloSelf",    Parent = "SOLO PLAY",   Label = "Versus Self",   Status = ModeStatus.Ready, Launch = "ENTER SANDBOX" },
        new MenuMode { Id = "soloAi",      Parent = "SOLO PLAY",   Label = "Versus A.I.",   Status = ModeStatus.Dev,   Launch = "START MATCH"   },
        new MenuMode { Id = "ranked",      Parent = "MULTIPLAYER", Label = "Ranked Match",  Status = ModeStatus.Soon,  Launch = "FIND MATCH"    },
        new MenuMode { Id = "casual",      Parent = "MULTIPLAYER", Label = "Casual Match",  Status = ModeStatus.Soon,  Launch = "FIND MATCH"    },
        new MenuMode { Id = "privateRoom", Parent = "MULTIPLAYER", Label = "Private Room",  Status = ModeStatus.Ready, Launch = "CREATE ROOM"   },
    };

    // ── Runtime state ──────────────────────────────────────────────────────────
    private string selectedId = "soloSelf";
    private const string DefaultPlayerName = "Captain Vere";

    // Versus-self deck picks. Static so they survive this MonoBehaviour being
    // torn down and recreated (single-scene design) whenever a picker round-trip
    // rebuilds the menu — mirrors DeckStore.ActiveDeckId's lifetime.
    private static string p1DeckId;
    private static string p2DeckId;
    // Set when ENTER is pressed without both decks chosen; cleared automatically
    // once both are valid (recomputed fresh every BuildLaunchBar), so it doesn't
    // need its own timer — it just flags whichever slot(s) are still empty.
    private bool enterAlert;

    // When true, the stage shows the replay browser instead of the game-mode portals.
    private bool showingReplays;

    // When true, the stage shows the private-room lobby hub (create/browse/join, then
    // the in-lobby waiting room) instead of the game-mode portals.
    private bool showingLobbyHub;
    private string lobbyNameInput = "";
    private bool lobbyIsPrivate = true;
    private string joinCodeInput = "";
    private List<ISessionInfo> browsedLobbies = new List<ISessionInfo>();
    private bool lobbyBusy;
    private string lobbyError;
    // Tracks which session we're currently subscribed to for live updates (player joins/
    // leaves, etc.), so the waiting room repaints itself instead of going stale until the
    // local player happens to click something else.
    private ISession subscribedLobbySession;

    private Canvas   canvas;
    private RectTransform menuRoot;
    private Text     clockText;
    private Font     font;
    private Font     monoFont;

    // Leader art cache for the versus-self deck-slot thumbnails — resolves
    // straight from a card id to its art file, no card-library lookup needed.
    private readonly Dictionary<string, Sprite> _leaderArtCache = new Dictionary<string, Sprite>();

    // ── Cached sprites (same generators as GameManager) ───────────────────────
    private Sprite _roundedRectSprite;
    private Sprite _roundedRectSpriteBig;
    private Sprite _circleSprite;
    private Sprite _radialSprite;
    private Sprite _vGradientSprite;
    private readonly Dictionary<int, Sprite> _borderSprites = new Dictionary<int, Sprite>();

    // ══════════════════════════════════════════════════════════════════════════
    // Bootstrap (auto-creates itself in the MainMenu scene, index 0)
    // ══════════════════════════════════════════════════════════════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBoot()
    {
        // The menu is the game's entry point. Create it once at startup — unless
        // a board is already present (e.g. GameManager.BootBoardImmediately was
        // set to launch straight into a match for development).
        if (UnityEngine.Object.FindAnyObjectByType<GameManager>() != null) return;
        EnsureMenu();
    }

    /// <summary>Creates the main menu if one isn't already present.</summary>
    public static void EnsureMenu()
    {
        if (UnityEngine.Object.FindAnyObjectByType<MainMenuManager>() == null)
            new GameObject("MainMenuManager").AddComponent<MainMenuManager>();
    }

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        try
        {
            monoFont = Font.CreateDynamicFontFromOSFont(
                new[] { "JetBrains Mono", "Consolas", "Cascadia Mono", "Courier New" }, 14);
        }
        catch { monoFont = null; }

        EnsureEventSystem();
        BuildCanvas();
        RenderMenu();

        // Subscribed for this object's whole lifetime (not just while the lobby hub is
        // open) so the guest still gets launched into the match if they've wandered
        // elsewhere in the menu when the host clicks Start Match.
        MatchNetworkSync.MatchStartReceived -= OnNetworkMatchStartReceived;
        MatchNetworkSync.MatchStartReceived += OnNetworkMatchStartReceived;
    }

    // ── Leader art (versus-self deck-slot thumbnails) ───────────────────────────
    private static string LeaderArtPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        string safe = id.Trim();
        string set = safe.Contains("-") ? safe.Split('-')[0] : "";
        var candidates = new[]
        {
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "OfficialById", set, safe + ".png"),
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "Official", set, safe + ".png"),
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", set, safe + ".png"),
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", safe + ".png"),
        };
        foreach (var p in candidates) if (File.Exists(p)) return p;
        return null;
    }

    private Sprite LoadArt(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_leaderArtCache.TryGetValue(id, out var cached)) return cached;

        Sprite sprite = null;
        string p = LeaderArtPath(id);
        if (p != null)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (tex.LoadImage(File.ReadAllBytes(p)))
                {
                    tex.filterMode = FilterMode.Trilinear;
                    tex.anisoLevel = 4;
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }
            catch { /* ignore */ }
        }
        _leaderArtCache[id] = sprite;
        return sprite;
    }

    private void Start()
    {
        // Update clock every 30 seconds (no need for every-second polling)
        InvokeRepeating(nameof(UpdateClock), 30f, 30f);
    }

    // ── EventSystem ───────────────────────────────────────────────────────────

    private void EnsureEventSystem()
    {
        foreach (var old in UnityEngine.Object.FindObjectsByType<StandaloneInputModule>(FindObjectsInactive.Include))
            Destroy(old);

        var existing = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }

    // ── Canvas ────────────────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("MainMenu Canvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // above any board canvas that might also exist
        canvasGo.AddComponent<GraphicRaycaster>();

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution  = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight   = 0.5f;
        scaler.dynamicPixelsPerUnit = 4f;

        menuRoot = PanelObject("Menu Root", canvas.transform, MatTop);
        Stretch(menuRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        menuRoot.GetComponent<Image>().raycastTarget = false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Main render — destroy-and-rebuild (matches GameManager's Render() pattern)
    // ══════════════════════════════════════════════════════════════════════════

    private void RenderMenu()
    {
        for (int i = menuRoot.childCount - 1; i >= 0; i--)
            Destroy(menuRoot.GetChild(i).gameObject);
        clockText = null;

        BuildBackground();
        BuildTopBar();
        BuildBody();
        Canvas.ForceUpdateCanvases();
    }

    private void SelectMode(string id)
    {
        selectedId = id;
        RenderMenu();
    }

    private void UpdateClock()
    {
        if (clockText != null)
            clockText.text = DateTime.Now.ToString("HH:mm");
    }

    private MenuMode FindMode(string id)
    {
        foreach (var m in modes)
            if (m.Id == id) return m;
        return modes[0];
    }

    private Color StatusColor(ModeStatus s)
    {
        switch (s)
        {
            case ModeStatus.Ready: return Accent;
            case ModeStatus.Dev:   return Gold;
            default:               return Muted;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Background
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildBackground()
    {
        // One smooth full-height MatBottom→MatTop blend. The old version filled
        // only the lower half with MatBottom, which read as the in-game board's
        // split mat — menus get a single seamless field instead.
        var bottomGrad = PanelObject("BG Gradient", menuRoot, Color.white);
        Stretch(bottomGrad, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var bgImg = bottomGrad.GetComponent<Image>();
        bgImg.sprite = GetBgGradientSprite();
        bgImg.type = Image.Type.Simple;
        bgImg.raycastTarget = false;

        // Soft Accent radial glow behind the portals — a real radial falloff,
        // centred upper-middle, so the field has depth instead of a flat tint.
        AddRadialGlow(menuRoot, new Color(Accent.r, Accent.g, Accent.b, 0.12f),
            new Vector2(0.08f, 0.26f), new Vector2(0.92f, 1.06f));
        // A second, tighter warm-cyan core for a bit of bloom near the top centre.
        AddRadialGlow(menuRoot, new Color(Accent2.r, Accent2.g, Accent2.b, 0.06f),
            new Vector2(0.28f, 0.52f), new Vector2(0.72f, 1.04f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Top bar (h=60, full width, pinned to top)
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildTopBar()
    {
        var bar = PanelObject("Top Bar", menuRoot, new Color32(8, 14, 21, 158));
        Stretch(bar, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -60f), Vector2.zero);

        // Bottom hairline border
        var line = PanelObject("Hairline", bar, MenuB);
        line.anchorMin = Vector2.zero;
        line.anchorMax = new Vector2(1f, 0f);
        line.pivot     = new Vector2(0.5f, 0f);
        line.sizeDelta = new Vector2(0f, 1f);
        line.anchoredPosition = Vector2.zero;
        line.GetComponent<Image>().raycastTarget = false;

        // ── Left — player identity ──────────────────────────────────────────
        var identity = PanelObject("Identity", bar, new Color(0, 0, 0, 0));
        Stretch(identity, Vector2.zero, new Vector2(0.32f, 1f), new Vector2(18f, 6f), new Vector2(0f, -6f));

        // Diamond avatar (rotated 45°, green fill placeholder)
        var avatar = PanelObject("Avatar Diamond", identity, new Color32(31, 138, 91, 255));
        avatar.anchorMin = new Vector2(0f, 0.5f);
        avatar.anchorMax = new Vector2(0f, 0.5f);
        avatar.pivot     = new Vector2(0f, 0.5f);
        avatar.sizeDelta = new Vector2(30f, 30f);
        avatar.anchoredPosition = new Vector2(2f, 0f);
        avatar.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Round(avatar);

        var nameText = TextObject("PlayerName", identity, DefaultPlayerName, 15, Ink, TextAnchor.LowerLeft);
        nameText.fontStyle = FontStyle.Bold;
        Stretch(nameText.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(46f, 2f), Vector2.zero);

        var subText = TextObject("PlayerSub", identity, "CAPTAIN  ·  LV 12", 10, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(subText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(46f, 0f), new Vector2(0f, -2f));

        // ── Center — wordmark (absolutely centered) ─────────────────────────
        var wordmark = PanelObject("Wordmark", bar, new Color(0, 0, 0, 0));
        wordmark.anchorMin = new Vector2(0.5f, 0.5f);
        wordmark.anchorMax = new Vector2(0.5f, 0.5f);
        wordmark.pivot     = new Vector2(0.5f, 0.5f);
        wordmark.sizeDelta = new Vector2(260f, 44f);
        wordmark.anchoredPosition = Vector2.zero;

        var wDiamond = PanelObject("WM Diamond", wordmark, Accent);
        wDiamond.anchorMin = new Vector2(0f, 0.5f);
        wDiamond.anchorMax = new Vector2(0f, 0.5f);
        wDiamond.pivot     = new Vector2(0f, 0.5f);
        wDiamond.sizeDelta = new Vector2(16f, 16f);
        wDiamond.anchoredPosition = new Vector2(6f, 0f);
        wDiamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Round(wDiamond);

        var wmText = TextObject("WM Text", wordmark, "One Piece TCG", 15,
            new Color32(220, 232, 238, 255), TextAnchor.MiddleLeft);
        wmText.fontStyle = FontStyle.Bold;
        Stretch(wmText.rectTransform, Vector2.zero, Vector2.one, new Vector2(34f, 0f), Vector2.zero);

        // ── Right — clock + settings gear ──────────────────────────────────
        var rightGroup = PanelObject("Right Group", bar, new Color(0, 0, 0, 0));
        Stretch(rightGroup, new Vector2(0.68f, 0f), Vector2.one, Vector2.zero, new Vector2(-16f, 0f));

        clockText = TextObject("Clock", rightGroup, DateTime.Now.ToString("HH:mm"),
            14, Ink, TextAnchor.MiddleRight, monoFont);
        Stretch(clockText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-56f, 0f));

        var gear = PanelObject("Gear Btn", rightGroup, LogBgDark);
        gear.anchorMin = new Vector2(1f, 0.5f);
        gear.anchorMax = new Vector2(1f, 0.5f);
        gear.pivot     = new Vector2(1f, 0.5f);
        gear.sizeDelta = new Vector2(40f, 40f);
        gear.anchoredPosition = Vector2.zero;
        Round(gear);
        AddRoundedCardBorder(gear, MenuB, 1f);

        var gearIcon = TextObject("Gear Icon", gear, "⚙", 18, Muted, TextAnchor.MiddleCenter);
        Stretch(gearIcon.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var gearBtn = gear.gameObject.AddComponent<Button>();
        gearBtn.onClick.AddListener(() => { /* TODO: open settings */ });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Body (below top bar): left rail + stage
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildBody()
    {
        var body = PanelObject("Body", menuRoot, new Color(0, 0, 0, 0));
        // Fill from bottom up to where top bar ends, with 22px edge padding
        Stretch(body, Vector2.zero, Vector2.one, new Vector2(22f, 22f), new Vector2(-22f, -60f));

        // ── Left rail (fixed 234px wide) ────────────────────────────────────
        var rail = PanelObject("Left Rail", body, LogBgDark);
        Stretch(rail, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(234f, 0f));
        RoundBig(rail);
        AddRoundedCardBorder(rail, MenuB, 1f);
        BuildLeftRail(rail);

        // ── Stage (fills rest, 20px gap from rail) ──────────────────────────
        var stage = PanelObject("Stage", body, new Color(0, 0, 0, 0));
        Stretch(stage, Vector2.zero, Vector2.one, new Vector2(254f, 0f), Vector2.zero);
        if (showingReplays) BuildReplayStage(stage);
        else if (showingLobbyHub) BuildLobbyStage(stage);
        else BuildStage(stage);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Replay browser (stage swapped in for "Replays" nav row)
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildReplayStage(RectTransform stage)
    {
        const float titleH = 60f;

        var titleRow = PanelObject("Replay Title Row", stage, new Color(0, 0, 0, 0));
        Stretch(titleRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -titleH), Vector2.zero);

        var titleText = TextObject("Title", titleRow, "Match History", 26, Ink, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(4f, 0f), Vector2.zero);

        var backHolder = PanelObject("Back Holder", titleRow, new Color(0, 0, 0, 0));
        Stretch(backHolder, new Vector2(0.8f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        var backHlg = backHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        backHlg.childAlignment = TextAnchor.MiddleRight;
        backHlg.childControlWidth = false;
        backHlg.childControlHeight = false;
        AddButton(backHolder, "< Back", () => { showingReplays = false; RenderMenu(); }, true, false);

        var listArea = PanelObject("Replay List", stage, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -titleH));

        var replays = ReplayStore.ListAll();
        if (replays.Count == 0)
        {
            var empty = TextObject("Empty", listArea, "No match history yet — finish a match to save one.",
                13, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(4f, -34f), Vector2.zero);
            return;
        }

        const float rowH = 56f, gap = 8f;
        int shown = Mathf.Min(replays.Count, 10);
        for (int i = 0; i < shown; i++)
        {
            var row = PanelObject("Replay Row " + i, listArea, new Color32(8, 16, 24, 153));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowH);
            row.anchoredPosition = new Vector2(0f, -(i * (rowH + gap)));
            BuildReplayRow(row, replays[i]);
        }
    }

    private void BuildReplayRow(RectTransform row, ReplayRecord r)
    {
        Round(row);
        AddRoundedCardBorder(row, MenuB, 1f);

        string south = string.IsNullOrEmpty(r.SouthDeckName) ? "South" : r.SouthDeckName;
        string north = string.IsNullOrEmpty(r.NorthDeckName) ? "North" : r.NorthDeckName;
        var title = TextObject("Matchup", row, $"{south}  vs  {north}", 14, Ink, TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.6f, 1f),
            new Vector2(14f, 0f), new Vector2(-4f, -6f));

        string when = FormatWhen(r.SavedAtIso);
        string sub = $"{r.TurnCount} turns"
            + (!string.IsNullOrEmpty(r.WinnerName) ? $"  ·  {r.WinnerName} won" : "")
            + (!string.IsNullOrEmpty(when) ? $"  ·  {when}" : "");
        var subText = TextObject("Sub", row, sub, 11, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(subText.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 0.5f),
            new Vector2(14f, 6f), new Vector2(-4f, 0f));

        var btnHolder = PanelObject("Buttons", row, new Color(0, 0, 0, 0));
        Stretch(btnHolder, new Vector2(0.6f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        var hlg = btnHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        AddButton(btnHolder, "Delete", () => { ReplayStore.Delete(r.Id); RenderMenu(); }, true, false);
        AddButton(btnHolder, "Watch", () => WatchReplay(r), true, false);
    }

    private string FormatWhen(string iso)
    {
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("MMM d, HH:mm");
        return "";
    }

    private void WatchReplay(ReplayRecord r)
    {
        CancelInvoke();
        if (canvas != null) canvas.gameObject.SetActive(false);
        GameManager.PendingReplayLoad = r;
        GameManager.EnsureBoard();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Private-room lobby hub (stage swapped in for "Private Room" mode).
    // Session/data layer lives in LobbyManager.cs (Unity Gaming Services Sessions API).
    // NOTE: this wires up lobby hosting/browsing/joining only — actual in-match command
    // sync between the two connected players is a separate follow-up (INetworkHandler).
    // ══════════════════════════════════════════════════════════════════════════

    private void OpenLobbyHub()
    {
        showingLobbyHub = true;
        lobbyError = null;
        RenderMenu();
        if (LobbyManager.CurrentSession == null) RefreshLobbyBrowser();
    }

    private void CloseLobbyHub()
    {
        showingLobbyHub = false;
        lobbyError = null;
        RenderMenu();
    }

    private void BuildLobbyStage(RectTransform stage)
    {
        if (LobbyManager.CurrentSession != null)
            BuildLobbyWaitingRoom(stage, LobbyManager.CurrentSession);
        else
            BuildLobbyHub(stage);
    }

    private void BuildLobbyHub(RectTransform stage)
    {
        const float titleH = 60f;

        var titleRow = PanelObject("Lobby Title Row", stage, new Color(0, 0, 0, 0));
        Stretch(titleRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -titleH), Vector2.zero);

        var titleText = TextObject("Title", titleRow, "Private Room", 26, Ink, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(4f, 0f), Vector2.zero);

        var backHolder = PanelObject("Back Holder", titleRow, new Color(0, 0, 0, 0));
        Stretch(backHolder, new Vector2(0.8f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        var backHlg = backHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        backHlg.childAlignment = TextAnchor.MiddleRight;
        backHlg.childControlWidth = false;
        backHlg.childControlHeight = false;
        AddButton(backHolder, "< Back", CloseLobbyHub, true, false);

        var body = PanelObject("Lobby Body", stage, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -titleH));

        // Left half: create a lobby.
        var createPanel = PanelObject("Create Panel", body, new Color32(8, 16, 24, 153));
        Stretch(createPanel, Vector2.zero, new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);
        Round(createPanel);
        AddRoundedCardBorder(createPanel, MenuB, 1f);
        BuildCreateLobbyPanel(createPanel);

        // Right half: join a lobby (by code, or browse public ones).
        var joinPanel = PanelObject("Join Panel", body, new Color32(8, 16, 24, 153));
        Stretch(joinPanel, new Vector2(0.52f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        Round(joinPanel);
        AddRoundedCardBorder(joinPanel, MenuB, 1f);
        BuildJoinLobbyPanel(joinPanel);
    }

    private void BuildCreateLobbyPanel(RectTransform panel)
    {
        var header = TextObject("Header", panel, "HOST A LOBBY", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var nameLabel = TextObject("Name Label", panel, "Lobby name", 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(nameLabel.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -62f), new Vector2(-16f, -46f));

        var nameField = MakeInput(panel, "e.g. Vere's Table", lobbyNameInput,
            s => lobbyNameInput = s, null);
        Stretch(nameField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -98f), new Vector2(-16f, -66f));

        var visLabel = TextObject("Vis Label", panel, "Visibility", 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(visLabel.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -128f), new Vector2(-16f, -112f));

        var visRow = PanelObject("Vis Row", panel, new Color(0, 0, 0, 0));
        Stretch(visRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -166f), new Vector2(-16f, -132f));
        var visHlg = visRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        visHlg.spacing = 8f;
        visHlg.childAlignment = TextAnchor.MiddleLeft;
        visHlg.childControlWidth = false;
        visHlg.childControlHeight = false;
        BuildVisibilityOption(visRow, "Private", true);
        BuildVisibilityOption(visRow, "Public", false);

        if (!string.IsNullOrEmpty(lobbyError))
        {
            var err = TextObject("Error", panel, lobbyError, 11, RedAccent, TextAnchor.UpperLeft, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 54f), new Vector2(-16f, 92f));
        }

        var createHolder = PanelObject("Create Holder", panel, new Color(0, 0, 0, 0));
        Stretch(createHolder, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(-16f, 50f));
        AddButton(createHolder, lobbyBusy ? "Working..." : "Create Lobby", CreateLobbyClicked, !lobbyBusy, false);
    }

    private void BuildVisibilityOption(RectTransform parent, string label, bool isPrivateOption)
    {
        bool selected = lobbyIsPrivate == isPrivateOption;
        var tile = PanelObject(label + " Option", parent,
            selected ? new Color(Accent.r, Accent.g, Accent.b, 0.16f) : new Color32(20, 30, 42, 200));
        SetPreferred(tile, new Vector2(110, 30));
        tile.sizeDelta = new Vector2(110, 30);
        Round(tile);
        AddRoundedCardBorder(tile, selected ? Accent : MenuB, selected ? 1.6f : 1f);
        var t = TextObject("Text", tile, label, 11, selected ? Ink : Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = tile.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { lobbyIsPrivate = isPrivateOption; RenderMenu(); });
    }

    private void BuildJoinLobbyPanel(RectTransform panel)
    {
        var header = TextObject("Header", panel, "JOIN A LOBBY", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var codeField = MakeInput(panel, "Enter join code", joinCodeInput, s => joinCodeInput = s, null);
        Stretch(codeField, new Vector2(0f, 1f), new Vector2(0.68f, 1f), new Vector2(16f, -70f), new Vector2(0f, -46f));

        var joinCodeHolder = PanelObject("Join Code Holder", panel, new Color(0, 0, 0, 0));
        Stretch(joinCodeHolder, new Vector2(0.68f, 1f), new Vector2(1f, 1f), new Vector2(0f, -70f), new Vector2(-16f, -46f));
        AddButton(joinCodeHolder, "Join", JoinLobbyByCodeClicked, !lobbyBusy, false);

        var listHeader = PanelObject("List Header Row", panel, new Color(0, 0, 0, 0));
        Stretch(listHeader, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -102f), new Vector2(-16f, -78f));
        var listHeaderText = TextObject("List Header", listHeader, "PUBLIC LOBBIES", 11, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(listHeaderText.rectTransform, Vector2.zero, new Vector2(0.7f, 1f), Vector2.zero, Vector2.zero);
        var refreshHolder = PanelObject("Refresh Holder", listHeader, new Color(0, 0, 0, 0));
        Stretch(refreshHolder, new Vector2(0.7f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        var refreshHlg = refreshHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        refreshHlg.childAlignment = TextAnchor.MiddleRight;
        refreshHlg.childControlWidth = false;
        refreshHlg.childControlHeight = false;
        AddButton(refreshHolder, lobbyBusy ? "..." : "Refresh", RefreshLobbyBrowser, !lobbyBusy, false);

        var listArea = PanelObject("List Area", panel, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -108f));

        if (browsedLobbies.Count == 0)
        {
            var empty = TextObject("Empty", listArea,
                lobbyBusy ? "Searching..." : "No public lobbies right now.",
                12, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -24f));
            return;
        }

        const float rowH = 48f, gap = 6f;
        int shown = Mathf.Min(browsedLobbies.Count, 8);
        for (int i = 0; i < shown; i++)
        {
            var info = browsedLobbies[i];
            var row = PanelObject("Lobby Row " + i, listArea, new Color32(14, 22, 32, 180));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowH);
            row.anchoredPosition = new Vector2(0f, -(i * (rowH + gap)));
            Round(row);

            var name = TextObject("Name", row, string.IsNullOrEmpty(info.Name) ? "Untitled Lobby" : info.Name,
                13, Ink, TextAnchor.UpperLeft);
            name.fontStyle = FontStyle.Bold;
            Stretch(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.62f, 1f), new Vector2(10f, 0f), new Vector2(-4f, -4f));

            int playerCount = Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
            var sub = TextObject("Sub", row, $"{LobbyManager.GetOwnerName(info)}  ·  {playerCount}/{info.MaxPlayers}",
                10, Muted, TextAnchor.LowerLeft, monoFont);
            Stretch(sub.rectTransform, new Vector2(0f, 0f), new Vector2(0.62f, 0.5f), new Vector2(10f, 4f), new Vector2(-4f, 0f));

            bool full = info.AvailableSlots <= 0;
            var joinHolder = PanelObject("Join Holder", row, new Color(0, 0, 0, 0));
            Stretch(joinHolder, new Vector2(0.62f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-8f, 0f));
            var jhlg = joinHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
            jhlg.childAlignment = TextAnchor.MiddleRight;
            jhlg.childControlWidth = false;
            jhlg.childControlHeight = false;
            AddButton(joinHolder, full ? "Full" : "Join", () => JoinLobbyClicked(info), !full && !lobbyBusy, false);
        }
    }

    private void BuildLobbyWaitingRoom(RectTransform stage, ISession session)
    {
        const float titleH = 60f;

        var titleRow = PanelObject("Waiting Title Row", stage, new Color(0, 0, 0, 0));
        Stretch(titleRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -titleH), Vector2.zero);
        var titleText = TextObject("Title", titleRow,
            string.IsNullOrEmpty(session.Name) ? "Lobby" : session.Name, 26, Ink, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(4f, 0f), Vector2.zero);

        var panel = PanelObject("Waiting Panel", stage, new Color32(8, 16, 24, 153));
        Stretch(panel, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -titleH));
        Round(panel);
        AddRoundedCardBorder(panel, MenuB, 1f);

        string vis = session.IsPrivate ? "Private" : "Public";
        string info = $"{vis}  ·  Owner: {LobbyManager.GetOwnerName(session)}  ·  {session.PlayerCount}/{session.MaxPlayers} players";
        var infoText = TextObject("Info", panel, info, 13, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(infoText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -40f), new Vector2(-16f, -16f));

        float y = -70f;
        // Join codes work for any session regardless of public/private - private just
        // means it won't also show up in the public browse list.
        if (session.IsHost && !string.IsNullOrEmpty(session.Code))
        {
            var codeLabel = TextObject("Code Label", panel, $"Join code: {session.Code}", 14, Accent, TextAnchor.UpperLeft, monoFont);
            codeLabel.fontStyle = FontStyle.Bold;
            Stretch(codeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(16f, y - 26f), new Vector2(-16f, y));

            var copyHolder = PanelObject("Copy Holder", panel, new Color(0, 0, 0, 0));
            Stretch(copyHolder, new Vector2(0.7f, 1f), new Vector2(1f, 1f), new Vector2(0f, y - 30f), new Vector2(-16f, y + 4f));
            AddButton(copyHolder, "Copy Code", () => GUIUtility.systemCopyBuffer = session.Code, true, false);
            y -= 40f;
        }

        var playersHeader = TextObject("Players Header", panel, "PLAYERS", 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(playersHeader.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 20f), new Vector2(-16f, y));
        y -= 26f;
        foreach (var p in session.Players)
        {
            string role = p.Id == session.Host ? "Host" : "Guest";
            string who = p.Id == session.CurrentPlayer?.Id ? $"{role} (You)" : role;
            var pText = TextObject("Player " + p.Id, panel, who, 12, Ink, TextAnchor.UpperLeft);
            Stretch(pText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 20f), new Vector2(-16f, y));
            y -= 24f;
        }

        // session.PlayerCount is the LOBBY's view (session/matchmaking membership) - it can say
        // 2/2 before Netcode's own Relay connection between host and guest has actually finished
        // establishing. Gate Start Match on the real Netcode connection too, not just the lobby,
        // so a click can't fire before there's an actual peer to send the match-start message to.
        bool bothPresent = session.PlayerCount >= session.MaxPlayers;
        bool networkReady = MatchNetworkSync.IsPeerConnected;
        string noteMessage;
        if (!bothPresent) noteMessage = "Waiting for another player to join...";
        else if (!networkReady) noteMessage = "Both players are here. Finishing connection...";
        else if (session.IsHost) noteMessage = "Both players are here — click Start Match when ready. (Starter decks only for now.)";
        else noteMessage = "Both players are here. Waiting for the host to start the match...";
        var noteText = TextObject("Note", panel, noteMessage,
            11, Muted, TextAnchor.UpperLeft, monoFont);
        noteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(noteText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 56f), new Vector2(-16f, 96f));

        var actionRow = PanelObject("Action Row", panel, new Color(0, 0, 0, 0));
        Stretch(actionRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(-16f, 50f));
        var ahlg = actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        ahlg.spacing = 8f;
        ahlg.childAlignment = TextAnchor.MiddleLeft;
        ahlg.childControlWidth = false;
        ahlg.childControlHeight = false;
        AddButton(actionRow, "Leave Lobby", LeaveLobbyClicked, !lobbyBusy, false);
        if (session.IsHost)
            AddButton(actionRow, "Start Match", StartMatchClicked, bothPresent && networkReady && !lobbyBusy, false);
    }

    // Host: generates the shared seed, sends it once to the guest, then both clients
    // independently call GameEngine.CreateMatch with that seed - no game state itself
    // is transmitted here, just the seed each side needs to build an identical match.
    private void StartMatchClicked()
    {
        string seed = Guid.NewGuid().ToString("N");
        MatchNetworkSync.SendMatchStart(seed);
        LaunchNetworkedMatch(seed, "south");
    }

    private void OnNetworkMatchStartReceived(string seed)
    {
        LaunchNetworkedMatch(seed, "north");
    }

    // Host is always "south" (ST01), guest is always "north" (ST02) for now - custom
    // deck selection for networked matches is a follow-up; this reuses the same
    // starter-deck defaults hotseat play already falls back to.
    private void LaunchNetworkedMatch(string seed, string localSeat)
    {
        CancelInvoke();
        UnsubscribeFromSessionEvents();
        if (canvas != null) canvas.gameObject.SetActive(false);
        GameManager.PendingNetworkedSeed = seed;
        GameManager.PendingNetworkedSeat = localSeat;
        GameManager.EnsureBoard();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // ISession.Changed fires on any update to the session (players joining/leaving,
    // properties changing, etc.) - both the host's and each guest's local ISession object
    // raise it independently once their client syncs the update, so this is what keeps
    // the waiting room live instead of frozen at whatever it looked like when first drawn.
    private void SubscribeToSessionEvents(ISession session)
    {
        if (session == null || subscribedLobbySession == session) return;
        UnsubscribeFromSessionEvents();
        subscribedLobbySession = session;
        session.Changed += OnLobbySessionChanged;
        session.Deleted += OnLobbySessionChanged;

        // Session membership (session.PlayerCount) and Netcode's actual peer connection can
        // land at slightly different times - repaint again once Netcode itself confirms a
        // peer, so "Start Match" enabling isn't stuck on whichever one lags behind.
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLobbyNetworkClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnLobbyNetworkClientConnected;
        }
    }

    private void UnsubscribeFromSessionEvents()
    {
        if (subscribedLobbySession != null)
        {
            subscribedLobbySession.Changed -= OnLobbySessionChanged;
            subscribedLobbySession.Deleted -= OnLobbySessionChanged;
            subscribedLobbySession = null;
        }
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLobbyNetworkClientConnected;
    }

    private void OnLobbyNetworkClientConnected(ulong clientId)
    {
        if (this == null || menuRoot == null) return;
        RenderMenu();
    }

    private void OnLobbySessionChanged()
    {
        if (this == null || menuRoot == null) return;
        RenderMenu();
    }

    private void OnDestroy()
    {
        UnsubscribeFromSessionEvents();
        MatchNetworkSync.MatchStartReceived -= OnNetworkMatchStartReceived;
    }

    private async void RefreshLobbyBrowser()
    {
        lobbyBusy = true;
        lobbyError = null;
        RenderMenu();
        try
        {
            var results = await LobbyManager.BrowsePublicLobbiesAsync();
            if (this == null || menuRoot == null) return;
            browsedLobbies = results;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            lobbyError = $"Couldn't load lobbies: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                lobbyBusy = false;
                RenderMenu();
            }
        }
    }

    private async void CreateLobbyClicked()
    {
        lobbyBusy = true;
        lobbyError = null;
        RenderMenu();
        try
        {
            var session = await LobbyManager.CreateLobbyAsync(lobbyNameInput, lobbyIsPrivate, DefaultPlayerName);
            if (this == null || menuRoot == null) return;
            SubscribeToSessionEvents(session);
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            lobbyError = $"Couldn't create lobby: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                lobbyBusy = false;
                RenderMenu();
            }
        }
    }

    private async void JoinLobbyByCodeClicked()
    {
        if (string.IsNullOrWhiteSpace(joinCodeInput)) { lobbyError = "Enter a join code first."; RenderMenu(); return; }
        lobbyBusy = true;
        lobbyError = null;
        RenderMenu();
        try
        {
            var session = await LobbyManager.JoinByCodeAsync(joinCodeInput);
            if (this == null || menuRoot == null) return;
            SubscribeToSessionEvents(session);
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            lobbyError = $"Couldn't join lobby: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                lobbyBusy = false;
                RenderMenu();
            }
        }
    }

    private async void JoinLobbyClicked(ISessionInfo info)
    {
        lobbyBusy = true;
        lobbyError = null;
        RenderMenu();
        try
        {
            var session = await LobbyManager.JoinByIdAsync(info.Id);
            if (this == null || menuRoot == null) return;
            SubscribeToSessionEvents(session);
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            lobbyError = $"Couldn't join lobby: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                lobbyBusy = false;
                RenderMenu();
            }
        }
    }

    private async void LeaveLobbyClicked()
    {
        lobbyBusy = true;
        RenderMenu();
        try
        {
            UnsubscribeFromSessionEvents();
            await LobbyManager.LeaveCurrentAsync();
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                lobbyBusy = false;
                RenderMenu();
            }
        }
    }

    // Legacy uGUI InputField with placeholder, themed to match this file's palette
    // (ported from DeckBuilderManager.MakeInput).
    private RectTransform MakeInput(RectTransform parent, string placeholder, string initial,
        UnityEngine.Events.UnityAction<string> onChanged, UnityEngine.Events.UnityAction<string> onEnd)
    {
        var holder = PanelObject("Input", parent, new Color32(8, 16, 26, 255));
        Round(holder);
        AddRoundedCardBorder(holder, MenuB, 1f);

        var textComp = TextObject("Text", holder, "", 13, Ink, TextAnchor.MiddleLeft);
        Stretch(textComp.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-10f, 0f));
        textComp.supportRichText = false;

        var ph = TextObject("Placeholder", holder, placeholder, 13,
            new Color(Muted.r, Muted.g, Muted.b, 0.7f), TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-10f, 0f));
        ph.fontStyle = FontStyle.Italic;

        var field = holder.gameObject.AddComponent<InputField>();
        field.textComponent = textComp;
        field.placeholder = ph;
        field.text = initial ?? "";
        field.lineType = InputField.LineType.SingleLine;
        field.caretColor = Accent;
        field.customCaretColor = true;
        if (onChanged != null) field.onValueChanged.AddListener(s => onChanged(s));
        if (onEnd != null) field.onEndEdit.AddListener(s => onEnd(s));
        return holder;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Left nav rail
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildLeftRail(RectTransform rail)
    {
        // Inner content container with VerticalLayoutGroup
        var content = PanelObject("Rail Content", rail, new Color(0, 0, 0, 0));
        Stretch(content, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -12f));

        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing            = 7f;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // "MENU" section header
        var header = TextObject("Rail Header", content, "MENU", 10, Muted, TextAnchor.LowerLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        var hLE = header.gameObject.AddComponent<LayoutElement>();
        hLE.preferredHeight = 22f;
        hLE.minHeight = 22f;

        // Five nav rows
        var rows = new (string title, string subtitle, string tag, bool active)[]
        {
            ("Play",     "Game modes",          null,       !showingReplays),
            ("Decks",    "Build & edit",        null,       false),
            ("Match History", "Watch past matches", null,   showingReplays),
            ("Friends",  "Crew & invites",      "3 online", false),
            ("Settings", "Preferences & audio", null,       false),
        };

        UnityEngine.Events.UnityAction[] actions =
        {
            () => { showingReplays = false; RenderMenu(); },
            () => OpenDeckBuilder(),
            () => { showingReplays = true; RenderMenu(); },
            () => { /* TODO: open friends */ },
            () => { /* TODO: open settings */ },
        };

        for (int i = 0; i < rows.Length; i++)
            BuildNavRow(content, rows[i].title, rows[i].subtitle, rows[i].tag, rows[i].active, actions[i]);
    }

    private void BuildNavRow(RectTransform parent, string title, string subtitle,
        string tag, bool active, UnityEngine.Events.UnityAction action)
    {
        var rowColor = active
            ? new Color(Accent.r, Accent.g, Accent.b, 0.14f)
            : new Color(0f, 0f, 0f, 0f);

        var row = PanelObject(title + " Row", parent, rowColor);
        var le  = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 64f;
        le.minHeight       = 56f;

        Round(row);
        if (active)
            AddRoundedCardBorder(row, new Color(Accent.r, Accent.g, Accent.b, 0.40f), 1.5f);
        else
            AddRoundedCardBorder(row, MenuB, 1f);

        // 3px Accent left-edge bar (active only)
        if (active)
        {
            var bar = PanelObject("Active Bar", row, Accent);
            bar.anchorMin = new Vector2(0f, 0.22f);
            bar.anchorMax = new Vector2(0f, 0.78f);
            bar.pivot     = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(3f, 0f);
            bar.anchoredPosition = Vector2.zero;
        }

        // Diamond icon (filled+glowing for active, outline for idle)
        const float DS = 11f;
        var diamond = PanelObject("Diamond", row, active ? Accent : new Color(0f, 0f, 0f, 0f));
        diamond.anchorMin = new Vector2(0f, 0.5f);
        diamond.anchorMax = new Vector2(0f, 0.5f);
        diamond.pivot     = new Vector2(0f, 0.5f);
        diamond.sizeDelta = new Vector2(DS, DS);
        diamond.anchoredPosition = new Vector2(16f, 0f);
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Round(diamond);
        if (!active)
            AddRoundedCardBorder(diamond, Muted, 1.2f);

        // Title
        var titleColor = active ? new Color32(241, 247, 250, 255) : new Color32(198, 211, 220, 255);
        var titleText = TextObject("Title", row, title, 15, titleColor, TextAnchor.LowerLeft);
        titleText.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
        Stretch(titleText.rectTransform,
            new Vector2(0f, 0.5f), Vector2.one,
            new Vector2(36f, 2f), new Vector2(tag != null ? -62f : -8f, -4f));

        // Subtitle
        var subColor = active ? Muted : (Color)new Color32(111, 134, 150, 255);
        var subText = TextObject("Subtitle", row, subtitle, 11, subColor, TextAnchor.UpperLeft);
        Stretch(subText.rectTransform,
            Vector2.zero, new Vector2(1f, 0.5f),
            new Vector2(36f, 4f), new Vector2(-8f, -2f));

        // Right tag (e.g. "3 online")
        if (!string.IsNullOrEmpty(tag))
        {
            var tagText = TextObject("Tag", row, tag, 10,
                new Color32(79, 208, 138, 255), TextAnchor.MiddleRight, monoFont);
            Stretch(tagText.rectTransform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-12f, 0f));
        }

        // Button
        var btn = row.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(action);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Stage: title → portals → launch bar
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildStage(RectTransform stage)
    {
        const float titleH   = 60f;
        const float launchH  = 78f;
        const float launchGap = 16f;

        // Title row (pinned to top)
        var titleRow = PanelObject("Title Row", stage, new Color(0, 0, 0, 0));
        Stretch(titleRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -titleH), Vector2.zero);

        var titleText = TextObject("Title", titleRow,
            "Choose your battle", 26, Ink, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.75f, 1f),
            new Vector2(4f, 0f), Vector2.zero);

        // Launch bar (pinned to bottom)
        var launchBar = PanelObject("Launch Bar", stage, new Color32(8, 14, 21, 153));
        Stretch(launchBar, Vector2.zero, new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, launchH));
        Round(launchBar);
        AddRoundedCardBorder(launchBar, MenuB, 1f);
        BuildLaunchBar(launchBar);

        // Portal row (fills middle)
        var portalRow = PanelObject("Portal Row", stage, new Color(0, 0, 0, 0));
        Stretch(portalRow, Vector2.zero, Vector2.one,
            new Vector2(0f, launchH + launchGap), new Vector2(0f, -titleH));
        BuildPortalRow(portalRow);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Portal row: Duel (left) + Solo Play (right)
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildPortalRow(RectTransform row)
    {
        var portalBg = new Color32(8, 14, 24, 200);
        const float halfGap = 8f;

        // Duel portal
        var duel = PanelObject("Duel Portal", row, portalBg);
        Stretch(duel, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-halfGap, 0f));
        RoundBig(duel);
        AddRoundedCardBorder(duel, ZoneBorder, 1f);
        BuildDuelPortal(duel);

        // Solo portal
        var solo = PanelObject("Solo Portal", row, portalBg);
        Stretch(solo, new Vector2(0.5f, 0f), Vector2.one, new Vector2(halfGap, 0f), Vector2.zero);
        RoundBig(solo);
        AddRoundedCardBorder(solo, ZoneBorder, 1f);
        BuildSoloPortal(solo);
    }

    // Soft cyan aura that spills just past a selected tile's edges, rendered
    // behind the tile's content so the ring reads as "lit".
    private void AddSelectionGlow(RectTransform tile)
    {
        var glow = AddRadialGlow(tile, new Color(Accent.r, Accent.g, Accent.b, 0.28f),
            Vector2.zero, Vector2.one);
        glow.offsetMin = new Vector2(-11f, -11f);
        glow.offsetMax = new Vector2(11f, 11f);
        glow.SetAsFirstSibling();   // behind the label/chip/border
    }

    // Thin bright highlight along the top edge of a portal — a subtle "lit from
    // above" rim that lifts the card off the dark field.
    private void AddTopHighlight(RectTransform portal)
    {
        var hi = PanelObject("Top Highlight", portal,
            new Color(Accent2.r, Accent2.g, Accent2.b, 0.22f));
        hi.anchorMin = new Vector2(0.04f, 1f);
        hi.anchorMax = new Vector2(0.96f, 1f);
        hi.pivot     = new Vector2(0.5f, 1f);
        hi.sizeDelta = new Vector2(0f, 2f);
        hi.anchoredPosition = new Vector2(0f, -1f);
        hi.GetComponent<Image>().raycastTarget = false;
    }

    // Bottom scrim built from a vertical gradient so the content area fades up
    // into the art instead of ending in a hard horizontal seam.
    private RectTransform AddScrim(RectTransform portal)
    {
        var scrim = PanelObject("Bottom Scrim", portal, new Color32(6, 10, 16, 255));
        var img   = scrim.GetComponent<Image>();
        img.sprite        = GetVGradientSprite();
        img.type          = Image.Type.Simple;
        img.raycastTarget = false;
        Stretch(scrim, Vector2.zero, new Vector2(1f, 0.52f), Vector2.zero, Vector2.zero);
        return scrim;
    }

    // Fills an empty art slot with a soft glow + a faint concentric-diamond motif
    // and a small caption, so the placeholder reads as intentional, not hollow.
    private void DecorateArtSlot(RectTransform artSlot, string caption)
    {
        AddRadialGlow(artSlot, new Color(Accent.r, Accent.g, Accent.b, 0.08f),
            new Vector2(0.10f, 0.06f), new Vector2(0.90f, 0.98f));

        for (int i = 0; i < 2; i++)
        {
            float s = 138f - i * 48f;
            var dia = PanelObject("Emblem " + i, artSlot, new Color(0f, 0f, 0f, 0f));
            dia.anchorMin = dia.anchorMax = new Vector2(0.5f, 0.56f);
            dia.pivot     = new Vector2(0.5f, 0.5f);
            dia.sizeDelta = new Vector2(s, s);
            dia.localRotation = Quaternion.Euler(0f, 0f, 45f);
            AddRoundedCardBorder(dia,
                new Color(Accent.r, Accent.g, Accent.b, 0.18f - i * 0.06f), 1.4f);
        }

        var cap = TextObject("ArtPlaceholder", artSlot, caption, 11,
            new Color(Accent.r, Accent.g, Accent.b, 0.34f), TextAnchor.LowerCenter, monoFont);
        cap.fontStyle = FontStyle.Bold;
        Stretch(cap.rectTransform, Vector2.zero, new Vector2(1f, 0.16f), Vector2.zero, Vector2.zero);
    }

    // ── Duel portal ───────────────────────────────────────────────────────────

    private void BuildDuelPortal(RectTransform portal)
    {
        // MULTIPLAYER label
        var lbl = TextObject("Label", portal, "MULTIPLAYER", 11,
            new Color32(207, 232, 240, 255), TextAnchor.UpperLeft, monoFont);
        lbl.fontStyle = FontStyle.Bold;
        Stretch(lbl.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(14f, -16f), new Vector2(-14f, -32f));

        // Top-edge highlight (lit-from-above rim)
        AddTopHighlight(portal);

        // Art slot placeholder (Image child named DuelArtSlot for easy replacement)
        var artSlot = PanelObject("DuelArtSlot", portal, new Color(0f, 0f, 0f, 0f));
        Stretch(artSlot, new Vector2(0f, 0.40f), new Vector2(1f, 0.90f),
            new Vector2(18f, 0f), new Vector2(-18f, 0f));
        DecorateArtSlot(artSlot, "[ DUEL ART ]");

        // Bottom content scrim — smooth vertical gradient that melts into the art
        var scrim = AddScrim(portal);

        // Inside the scrim, position content top-down
        var portalTitle = TextObject("Portal Title", scrim, "Duel", 30,
            new Color32(245, 250, 252, 255), TextAnchor.UpperLeft);
        portalTitle.fontStyle = FontStyle.Bold;
        Stretch(portalTitle.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(16f, -42f), new Vector2(-16f, -6f));

        var desc = TextObject("Desc", scrim,
            "Find an opponent and play for rank, or set your own table.",
            13, new Color32(174, 190, 203, 255), TextAnchor.UpperLeft);
        desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(desc.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(16f, -82f), new Vector2(-16f, -46f));

        // Sub-tiles row (Ranked, Casual, Private) — pinned to bottom of scrim
        var subRow = PanelObject("Sub Row", scrim, new Color(0f, 0f, 0f, 0f));
        Stretch(subRow, Vector2.zero, new Vector2(1f, 0f),
            new Vector2(12f, 8f), new Vector2(-12f, 66f));

        var duelSubs = new (string label, string modeId)[]
        {
            ("Ranked",  "ranked"),
            ("Casual",  "casual"),
            ("Private", "privateRoom"),
        };

        for (int i = 0; i < duelSubs.Length; i++)
            BuildMultiSubTile(subRow, duelSubs[i].label, duelSubs[i].modeId,
                FindMode(duelSubs[i].modeId).Status, i, duelSubs.Length, 9f);
    }

    // ── Solo portal ───────────────────────────────────────────────────────────

    private void BuildSoloPortal(RectTransform portal)
    {
        // SOLO label
        var lbl = TextObject("Label", portal, "SOLO", 11,
            new Color32(207, 232, 240, 255), TextAnchor.UpperLeft, monoFont);
        lbl.fontStyle = FontStyle.Bold;
        Stretch(lbl.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(14f, -16f), new Vector2(-14f, -32f));

        // Top-edge highlight (lit-from-above rim)
        AddTopHighlight(portal);

        // Art slot placeholder
        var artSlot = PanelObject("SoloArtSlot", portal, new Color(0f, 0f, 0f, 0f));
        Stretch(artSlot, new Vector2(0f, 0.40f), new Vector2(1f, 0.90f),
            new Vector2(18f, 0f), new Vector2(-18f, 0f));
        DecorateArtSlot(artSlot, "[ SOLO ART ]");

        // Bottom content scrim — smooth vertical gradient that melts into the art
        var scrim = AddScrim(portal);

        var portalTitle = TextObject("Portal Title", scrim, "Solo Play", 30,
            new Color32(245, 250, 252, 255), TextAnchor.UpperLeft);
        portalTitle.fontStyle = FontStyle.Bold;
        Stretch(portalTitle.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(16f, -42f), new Vector2(-16f, -6f));

        var desc = TextObject("Desc", scrim,
            "Practice lines and goldfish new builds at your own pace.",
            13, new Color32(174, 190, 203, 255), TextAnchor.UpperLeft);
        desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(desc.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(16f, -82f), new Vector2(-16f, -46f));

        // Sub-tiles row (Versus Self, Versus A.I.) — left-title / right-chip layout
        var subRow = PanelObject("Sub Row", scrim, new Color(0f, 0f, 0f, 0f));
        Stretch(subRow, Vector2.zero, new Vector2(1f, 0f),
            new Vector2(12f, 8f), new Vector2(-12f, 66f));

        // Versus Self
        BuildSoloSubTile(subRow, "Versus Self", "soloSelf", ModeStatus.Ready,
            0f, 0.5f, 4f, true);
        // Versus A.I.
        BuildSoloSubTile(subRow, "Versus A.I.", "soloAi", ModeStatus.Dev,
            0.5f, 1f, 4f, false);
    }

    // ── Multiplayer sub-tile (centered label + SOON chip below) ──────────────

    private void BuildMultiSubTile(RectTransform parent, string label, string modeId,
        ModeStatus status, int index, int total, float gap)
    {
        float frac = 1f / total;
        float xMin = frac * index;
        float xMax = frac * (index + 1);
        float offLeft  = index == 0        ? 0f : gap * 0.5f;
        float offRight = index == total - 1 ? 0f : gap * 0.5f;

        bool selected = selectedId == modeId;

        var tile = PanelObject(label + " Tile", parent, new Color32(8, 16, 24, 153));
        Stretch(tile, new Vector2(xMin, 0f), new Vector2(xMax, 1f),
            new Vector2(offLeft, 0f), new Vector2(-offRight, 0f));
        Round(tile);

        if (selected)
        {
            tile.GetComponent<Image>().color = new Color(Accent.r, Accent.g, Accent.b, 0.08f);
            AddSelectionGlow(tile);
            AddRoundedCardBorder(tile, Accent, 2f);
        }
        else
        {
            AddRoundedCardBorder(tile, MenuB, 1f);
        }

        var titleText = TextObject("Label", tile, label, 13,
            new Color32(219, 230, 236, 255), TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, new Vector2(0f, 0.45f), Vector2.one,
            new Vector2(6f, 0f), new Vector2(-6f, -4f));

        // SOON chip below label
        var chipAnchor = PanelObject("Chip Anchor", tile, new Color(0f, 0f, 0f, 0f));
        Stretch(chipAnchor, Vector2.zero, new Vector2(1f, 0.45f), new Vector2(6f, 2f), new Vector2(-6f, 0f));
        BuildStatusChip(chipAnchor, status);

        var idCap = modeId;
        var btn = tile.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => SelectMode(idCap));
    }

    // ── Solo sub-tile (title left, chip right) ────────────────────────────────

    private void BuildSoloSubTile(RectTransform parent, string label, string modeId,
        ModeStatus status, float xMin, float xMax, float halfGap, bool isLeft)
    {
        float offLeft  = isLeft  ? 0f : halfGap;
        float offRight = isLeft  ? halfGap : 0f;

        bool selected = selectedId == modeId;

        var tile = PanelObject(label + " Tile", parent, new Color32(8, 16, 24, 153));
        Stretch(tile, new Vector2(xMin, 0f), new Vector2(xMax, 1f),
            new Vector2(offLeft, 0f), new Vector2(-offRight, 0f));
        Round(tile);

        if (selected)
        {
            tile.GetComponent<Image>().color = new Color(Accent.r, Accent.g, Accent.b, 0.08f);
            AddSelectionGlow(tile);
            AddRoundedCardBorder(tile, Accent, 2f);
        }
        else
        {
            AddRoundedCardBorder(tile, MenuB, 1f);
        }

        // Title (left-aligned)
        var titleText = TextObject("Label", tile, label, 13,
            new Color32(219, 230, 236, 255), TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.58f, 1f),
            new Vector2(12f, 0f), Vector2.zero);

        // Status chip (right-aligned)
        var chipAnchor = PanelObject("Chip Anchor", tile, new Color(0f, 0f, 0f, 0f));
        Stretch(chipAnchor, new Vector2(0.55f, 0.18f), new Vector2(1f, 0.82f),
            Vector2.zero, new Vector2(-10f, 0f));
        BuildStatusChipFull(chipAnchor, status);

        var idCap = modeId;
        var btn = tile.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => SelectMode(idCap));
    }

    // Small chip helper for multi-tiles (centered inside chipAnchor)
    private void BuildStatusChip(RectTransform chipAnchor, ModeStatus status)
    {
        string chipLabel;
        bool primary;
        switch (status)
        {
            case ModeStatus.Ready: chipLabel = "READY"; primary = true; break;
            case ModeStatus.Dev:   chipLabel = "DEV";   primary = false; break;
            default:               chipLabel = "SOON";  primary = false; break;
        }

        var chip = PanelObject("Chip", chipAnchor, primary ? Accent : new Color(1f, 1f, 1f, 0.04f));
        Stretch(chip, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.95f), Vector2.zero, Vector2.zero);
        Round(chip);
        if (!primary) AddRoundedCardBorder(chip, status == ModeStatus.Dev ? Gold : ZoneBorder, 1f);

        var ct = TextObject("ChipText", chip, chipLabel, 9,
            primary ? BadgeInk : (status == ModeStatus.Dev ? Gold : Muted),
            TextAnchor.MiddleCenter, monoFont);
        ct.fontStyle = FontStyle.Bold;
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, 0f), new Vector2(-3f, 0f));
    }

    // Larger chip helper for solo sub-tiles (fills chipAnchor)
    private void BuildStatusChipFull(RectTransform chipAnchor, ModeStatus status)
    {
        string chipLabel;
        bool primary;
        switch (status)
        {
            case ModeStatus.Ready: chipLabel = "READY"; primary = true; break;
            case ModeStatus.Dev:   chipLabel = "DEV";   primary = false; break;
            default:               chipLabel = "SOON";  primary = false; break;
        }

        var chip = PanelObject("Chip", chipAnchor, primary ? Accent : new Color(0f, 0f, 0f, 0f));
        Stretch(chip, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Round(chip);
        if (!primary) AddRoundedCardBorder(chip, status == ModeStatus.Dev ? Gold : Muted, 1f);

        var ct = TextObject("ChipText", chip, chipLabel, 9,
            primary ? BadgeInk : (status == ModeStatus.Dev ? Gold : Muted),
            TextAnchor.MiddleCenter, monoFont);
        ct.fontStyle = FontStyle.Bold;
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 0f), new Vector2(-2f, 0f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Launch bar
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildLaunchBar(RectTransform bar)
    {
        var mode = FindMode(selectedId);
        bool ready = mode.Status == ModeStatus.Ready;

        // ── 1. Selected-mode summary (left flex) ────────────────────────────
        var summary = PanelObject("Summary", bar, new Color(0f, 0f, 0f, 0f));
        Stretch(summary, Vector2.zero, new Vector2(0.42f, 1f),
            new Vector2(20f, 8f), new Vector2(0f, -8f));

        var microLbl = TextObject("Micro Label", summary,
            "SELECTED MODE", 9, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(microLbl.rectTransform, new Vector2(0f, 1f), Vector2.one,
            Vector2.zero, new Vector2(0f, -18f));

        // Status dot
        var dot = PanelObject("Status Dot", summary, StatusColor(mode.Status));
        dot.anchorMin = new Vector2(0f, 0.5f);
        dot.anchorMax = new Vector2(0f, 0.5f);
        dot.pivot     = new Vector2(0f, 0.5f);
        dot.sizeDelta = new Vector2(8f, 8f);
        dot.anchoredPosition = new Vector2(0f, -6f);
        RoundCircle(dot);

        var modeNameText = TextObject("Mode Name", summary, mode.Label,
            16, Ink, TextAnchor.MiddleLeft);
        modeNameText.fontStyle = FontStyle.Bold;
        Stretch(modeNameText.rectTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.72f),
            new Vector2(18f, 0f), Vector2.zero);

        var parentLbl = TextObject("Parent", summary, mode.Parent,
            10, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(parentLbl.rectTransform, Vector2.zero, new Vector2(1f, 0.25f),
            new Vector2(18f, 4f), Vector2.zero);

        // ── 2. Deck group ─────────────────────────────────────────────────────
        // Versus Self needs two independent deck picks (one per seat), so it
        // gets its own wider two-slot layout instead of the single active-deck
        // display every other mode still uses.
        if (mode.Id == "soloSelf")
        {
            BuildPlayerDeckSlot(bar, 0.44f, 0.615f, "PLAYER 1 DECK", p1DeckId, () => PickPlayerDeck(1));
            BuildPlayerDeckSlot(bar, 0.615f, 0.79f, "PLAYER 2 DECK", p2DeckId, () => PickPlayerDeck(2));
        }
        else
        {
            var deckGroup = PanelObject("Deck Group", bar, new Color(0f, 0f, 0f, 0f));
            Stretch(deckGroup, new Vector2(0.5f, 0f), new Vector2(0.78f, 1f),
                Vector2.zero, new Vector2(0f, 0f));

            // Card spine (34×48, RedAccent)
            var spine = PanelObject("Card Spine", deckGroup, RedAccent);
            spine.anchorMin = new Vector2(0f, 0.5f);
            spine.anchorMax = new Vector2(0f, 0.5f);
            spine.pivot     = new Vector2(0f, 0.5f);
            spine.sizeDelta = new Vector2(28f, 42f);
            spine.anchoredPosition = new Vector2(0f, 0f);
            Round(spine);
            AddRoundedCardBorder(spine, new Color(1f, 1f, 1f, 0.22f), 1f);

            // Deck info labels
            var deckLblText = TextObject("Active Deck Label", deckGroup,
                "ACTIVE DECK", 9, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(deckLblText.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(36f, -14f), new Vector2(0f, -28f));

            var activeDeck = DeckStore.ActiveOrDefault();
            var deckNameText = TextObject("Deck Name", deckGroup,
                activeDeck != null ? activeDeck.name : "No deck — build one", 13, Ink, TextAnchor.MiddleLeft);
            deckNameText.fontStyle = FontStyle.Bold;
            Stretch(deckNameText.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 0.72f),
                new Vector2(36f, 0f), new Vector2(-4f, 0f));

            // SWAP opens the deck builder (manage / pick a different deck).
            var swap = PanelObject("Swap", deckGroup, new Color(0f, 0f, 0f, 0f));
            Stretch(swap, Vector2.zero, new Vector2(1f, 0.3f), new Vector2(36f, 4f), Vector2.zero);
            var swapText = TextObject("SwapText", swap, "MANAGE DECKS", 10, Accent, TextAnchor.LowerLeft, monoFont);
            Stretch(swapText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            swap.gameObject.AddComponent<Button>().onClick.AddListener(OpenDeckBuilder);
        }

        // ── 3. Divider ───────────────────────────────────────────────────────
        var divider = PanelObject("Divider", bar, MenuB);
        Stretch(divider, new Vector2(0.79f, 0f), new Vector2(0.79f, 1f),
            new Vector2(-1f, 12f), new Vector2(1f, -12f));

        // ── 4. Primary ENTER button ──────────────────────────────────────────
        var enterBg = ready
            ? (Color)new Color32(79, 195, 224, 255)
            : (Color)new Color32(20, 40, 56, 178);

        // Soft cyan glow behind the primary CTA (only when it's live).
        if (ready)
        {
            var enterGlow = AddRadialGlow(bar, new Color(Accent.r, Accent.g, Accent.b, 0.34f),
                new Vector2(0.82f, 0f), Vector2.one);
            enterGlow.offsetMin = new Vector2(-10f, -4f);
            enterGlow.offsetMax = new Vector2(-12f, 4f);
        }

        var enterBtn = PanelObject("Enter Button", bar, enterBg);
        Stretch(enterBtn, new Vector2(0.82f, 0f), Vector2.one,
            new Vector2(0f, 11f), new Vector2(-16f, -11f));
        Round(enterBtn);
        if (!ready) AddRoundedCardBorder(enterBtn, MenuB, 1f);

        string btnLabel = ready
            ? (mode.Launch + " ▸")
            : (mode.Status == ModeStatus.Dev ? "IN DEVELOPMENT" : "COMING SOON");

        Color btnTextColor = ready
            ? BadgeInk
            : new Color32(112, 133, 149, 255);

        var btnText = TextObject("Btn Text", enterBtn, btnLabel, 14,
            btnTextColor, TextAnchor.MiddleCenter, ready ? null : monoFont);
        btnText.fontStyle = ready ? FontStyle.Bold : FontStyle.Normal;
        Stretch(btnText.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(10f, 0f), new Vector2(-10f, 0f));

        if (ready)
        {
            var button = enterBtn.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() =>
            {
                if (mode.Id == "soloSelf")
                    EnterVersusSelf();
                else if (mode.Id == "privateRoom")
                    OpenLobbyHub();
                // TODO: soloAi, ranked, casual when implemented
            });
        }
    }

    // ── Versus Self: per-seat deck picking ──────────────────────────────────────
    // Opens the deck-builder hex roster as a one-shot picker for a single seat;
    // on confirm the choice is stored and the menu rebuilds to show it. Cancel
    // (BACK) just rebuilds the menu unchanged.
    private void PickPlayerDeck(int playerNum)
    {
        CancelInvoke();
        if (canvas != null) canvas.gameObject.SetActive(false);

        string title = playerNum == 1 ? "CHOOSE PLAYER 1 DECK" : "CHOOSE PLAYER 2 DECK";
        string subtitle = playerNum == 1 ? "south seat — pick a deck, then confirm" : "north seat — pick a deck, then confirm";
        DeckBuilderManager.OpenPicker(title, subtitle,
            chosenId =>
            {
                if (playerNum == 1) p1DeckId = chosenId; else p2DeckId = chosenId;
                EnsureMenu();
            },
            EnsureMenu);

        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // Validates both seats have a deck before launching; otherwise flags the
    // empty slot(s) in place rather than silently doing nothing.
    private void EnterVersusSelf()
    {
        if (DeckStore.Get(p1DeckId) == null || DeckStore.Get(p2DeckId) == null)
        {
            enterAlert = true;
            RenderMenu();
            return;
        }

        CancelInvoke();
        if (canvas != null) canvas.gameObject.SetActive(false);
        GameManager.LaunchVersusSelf(p1DeckId, p2DeckId);
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // One player's deck slot in the launch bar: leader thumbnail + deck name,
    // clickable to open that seat's picker. When `enterAlert` is set and this
    // slot has no deck, the border/hint switch to a warning state instead of
    // showing a separate banner — the alert points straight at what's missing.
    private void BuildPlayerDeckSlot(RectTransform bar, float x0, float x1, string microLabel,
        string deckId, UnityEngine.Events.UnityAction onClick)
    {
        var deck = DeckStore.Get(deckId);
        bool missing = deck == null;
        bool flagged = missing && enterAlert;

        var group = PanelObject(microLabel + " Slot", bar, new Color(0f, 0f, 0f, 0f));
        Stretch(group, new Vector2(x0, 0f), new Vector2(x1, 1f),
            new Vector2(6f, 0f), new Vector2(-6f, 0f));

        // Leader thumbnail (28×42, matches the single-deck spine it replaces).
        var thumb = PanelObject("Thumb", group, new Color32(6, 12, 20, 255));
        thumb.anchorMin = new Vector2(0f, 0.5f);
        thumb.anchorMax = new Vector2(0f, 0.5f);
        thumb.pivot     = new Vector2(0f, 0.5f);
        thumb.sizeDelta = new Vector2(28f, 42f);
        thumb.anchoredPosition = new Vector2(0f, 0f);
        Round(thumb);
        AddRoundedCardBorder(thumb, flagged ? RedAccent : new Color(1f, 1f, 1f, 0.22f), flagged ? 1.4f : 1f);

        var art = deck != null ? LoadArt(deck.leaderId) : null;
        if (art != null)
        {
            var im = PanelObject("Art", thumb, Color.white);
            im.GetComponent<Image>().sprite = art;
            im.GetComponent<Image>().preserveAspect = true;
            Stretch(im, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        }

        // Deck info labels
        var microText = TextObject("Micro", group, microLabel, 9, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(microText.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(36f, -14f), new Vector2(0f, -28f));

        var nameText = TextObject("Name", group, missing ? "Tap to select" : deck.name,
            13, missing ? Muted : Ink, TextAnchor.MiddleLeft);
        nameText.fontStyle = FontStyle.Bold;
        Stretch(nameText.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 0.72f),
            new Vector2(36f, 0f), new Vector2(-4f, 0f));

        string hint = flagged ? "⚠ select a deck" : (missing ? "tap to choose a deck" : "tap to change");
        var hintText = TextObject("Hint", group, hint, 10, flagged ? RedAccent : Accent,
            TextAnchor.LowerLeft, monoFont);
        Stretch(hintText.rectTransform, Vector2.zero, new Vector2(1f, 0.3f), new Vector2(36f, 4f), Vector2.zero);

        var hit = group.gameObject.AddComponent<Button>();
        hit.onClick.AddListener(onClick);
    }

    // Tears down the menu and opens the deck builder in this same scene.
    private void OpenDeckBuilder()
    {
        if (canvas != null) canvas.gameObject.SetActive(false);
        DeckBuilderManager.OpenSelect();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers — exact signatures from GameManager so styles match pixel-for-pixel
    // ══════════════════════════════════════════════════════════════════════════

    private RectTransform PanelObject(string name, Transform parent, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        return rt;
    }

    private Text TextObject(string name, Transform parent, string value, int size,
        Color color, TextAnchor alignment, Font fontOverride = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var text = go.AddComponent<Text>();
        text.font               = fontOverride != null ? fontOverride : font;
        text.text               = value;
        text.fontSize           = size;
        text.color              = color;
        text.alignment          = alignment;
        text.raycastTarget      = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow   = VerticalWrapMode.Truncate;
        return text;
    }

    private void Stretch(RectTransform rt, Vector2 min, Vector2 max,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private void Round(RectTransform rt)
    {
        if (rt == null) return;
        var img = rt.GetComponent<Image>();
        if (img == null) return;
        img.sprite = GetRoundedRectSprite();
        img.type   = Image.Type.Sliced;
    }

    private void RoundBig(RectTransform rt)
    {
        if (rt == null) return;
        var img = rt.GetComponent<Image>();
        if (img == null) return;
        img.sprite = GetRoundedRectSpriteBig();
        img.type   = Image.Type.Sliced;
    }

    private void RoundCircle(RectTransform rt)
    {
        if (rt == null) return;
        var img = rt.GetComponent<Image>();
        if (img == null) return;
        img.sprite = GetCircleSprite();
        img.type   = Image.Type.Simple;
    }

    private void SetPreferred(RectTransform rt, Vector2 size)
    {
        if (size == Vector2.zero) return;
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = size.x;
        le.preferredHeight = size.y;
        le.minWidth        = size.x;
        le.minHeight       = size.y;
    }

    private RectTransform RowObject(string name, RectTransform parent,
        float spacing, TextAnchor alignment)
    {
        var row    = new GameObject(name).AddComponent<RectTransform>();
        row.SetParent(parent, false);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing              = spacing;
        layout.childAlignment       = alignment;
        layout.childControlHeight   = false;
        layout.childControlWidth    = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth  = false;
        return row;
    }

    private void AddMenuItem(RectTransform parent, string label, Vector2 min, Vector2 max,
        UnityEngine.Events.UnityAction action)
    {
        var item = PanelObject(label + " Item", parent, new Color32(34, 58, 78, 235));
        Stretch(item, min, max, Vector2.zero, Vector2.zero);
        Round(item);
        AddRoundedCardBorder(item, MenuB, 1.1f);
        var d = PanelObject("Dot", item, Accent);
        d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
        d.pivot     = new Vector2(0f, 0.5f);
        d.sizeDelta = new Vector2(6f, 6f);
        d.anchoredPosition = new Vector2(12f, 0f);
        RoundCircle(d);
        var text = TextObject("Text", item, label, 11, Ink, TextAnchor.MiddleLeft, monoFont);
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(26, 0), new Vector2(-8, 0));
        var button = item.gameObject.AddComponent<Button>();
        button.onClick.AddListener(action);
    }

    private void AddPreviewChip(RectTransform parent, string label, bool primary)
    {
        if (string.IsNullOrEmpty(label)) return;
        var chip   = PanelObject(label + " Chip", parent,
            primary ? Accent : new Color(1f, 1f, 1f, 0.04f));
        float chipW = 12f + label.Length * 6.6f;
        chip.sizeDelta = new Vector2(chipW, 18f);
        SetPreferred(chip, new Vector2(chipW, 18f));
        Round(chip);
        if (!primary) AddRoundedCardBorder(chip, ZoneBorder, 1f);
        var t = TextObject("t", chip, label, 9, primary ? BadgeInk : Ink,
            TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
    }

    private void AddButton(RectTransform parent, string label,
        UnityEngine.Events.UnityAction action, bool enabled = true, bool dot = true)
    {
        var root = PanelObject(label + " Button", parent,
            enabled ? new Color32(34, 58, 78, 235) : new Color32(24, 34, 44, 170));
        SetPreferred(root, new Vector2(118, 34));
        root.sizeDelta = new Vector2(118, 34);
        Round(root);
        AddRoundedCardBorder(root,
            enabled ? MenuB : (Color)new Color32(50, 58, 74, 80), 1.1f);
        Color textColor = enabled ? Ink : (Color)new Color32(120, 130, 146, 160);
        if (dot)
        {
            var d = PanelObject("Dot", root,
                enabled ? Accent : (Color)new Color32(90, 100, 116, 160));
            d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
            d.pivot     = new Vector2(0f, 0.5f);
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

    private void AddRoundedCardBorder(RectTransform parent, Color color, float thickness)
    {
        if (parent == null || color.a <= 0f || thickness <= 0f) return;
        var borderGo = new GameObject("Border");
        borderGo.transform.SetParent(parent, false);
        var rt  = borderGo.AddComponent<RectTransform>();
        var img = borderGo.AddComponent<Image>();
        img.sprite       = GetRoundedCardBorderSprite(thickness);
        img.color        = color;
        img.raycastTarget = false;
        img.type         = Image.Type.Sliced;
        Stretch(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private void AddDashedBorder(RectTransform parent, Color color)
    {
        // Placeholder — dashed borders are used for empty art slots; not required for the menu.
        // Wire up the real implementation from GameManager if needed.
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sprite generators — identical algorithms to GameManager
    // ══════════════════════════════════════════════════════════════════════════

    private Sprite GetRoundedRectSprite()
    {
        if (_roundedRectSprite != null) return _roundedRectSprite;
        const int S = 24; const float r = 5f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Min(x + 0.5f, S - x - 0.5f);
                float cy = Mathf.Min(y + 0.5f, S - y - 0.5f);
                float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
                float a  = Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return _roundedRectSprite;
    }

    private Sprite GetRoundedRectSpriteBig()
    {
        if (_roundedRectSpriteBig != null) return _roundedRectSpriteBig;
        const int S = 44; const float r = 12f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Min(x + 0.5f, S - x - 0.5f);
                float cy = Mathf.Min(y + 0.5f, S - y - 0.5f);
                float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
                float a  = Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _roundedRectSpriteBig = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return _roundedRectSpriteBig;
    }

    private Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int S = 48;
        float rad = S / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - rad, dy = y + 0.5f - rad;
                float a  = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _circleSprite;
    }

    // Matches GameManager's GetRoundedCardBorderSprite exactly (64×90, radius 6.5)
    private Sprite GetRoundedCardBorderSprite(float thickness)
    {
        int key = Mathf.Clamp(Mathf.RoundToInt(thickness * 10f), 1, 80);
        if (_borderSprites.TryGetValue(key, out var cached)) return cached;

        const int W = 64, H = 90;
        const float R = 6.5f;
        float th = Mathf.Clamp(key / 10f, 0.75f, 8f);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float outer = RoundedRectAlpha(x + 0.5f, y + 0.5f, W, H, R);
                float inner = RoundedRectAlpha(
                    x + 0.5f - th, y + 0.5f - th,
                    W - th * 2f, H - th * 2f,
                    Mathf.Max(0f, R - th));
                float a = Mathf.Clamp01(outer * (1f - inner));
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        _borderSprites[key] = sprite;
        return sprite;
    }

    private static float RoundedRectAlpha(float x, float y, float width, float height, float radius)
    {
        if (width <= 0f || height <= 0f) return 0f;
        float px = Mathf.Min(x, width  - x);
        float py = Mathf.Min(y, height - y);
        float dx = Mathf.Max(0f, radius - px);
        float dy = Mathf.Max(0f, radius - py);
        return Mathf.Clamp01(radius + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
    }

    // Soft radial glow: opaque white at the centre fading smoothly to transparent
    // at the edge. Tint via Image.color; alpha in the tint scales the whole glow.
    private Sprite GetRadialSprite()
    {
        if (_radialSprite != null) return _radialSprite;
        const int S = 128; float rad = S / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f - rad) / rad;
                float dy = (y + 0.5f - rad) / rad;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(1f - d);
                a = a * a;                       // ease-in for a soft falloff
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _radialSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _radialSprite;
    }

    // Vertical gradient: opaque white at the bottom fading to transparent at the
    // top (biased toward the bottom). Tint to get a scrim that melts into the art.
    private Sprite GetVGradientSprite()
    {
        if (_vGradientSprite != null) return _vGradientSprite;
        const int W = 4, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);    // 0 at bottom row, 1 at top row
            float a = Mathf.Clamp01(1f - t);
            a = a * a;                        // bias the solid part toward the bottom
            byte ab = (byte)Mathf.RoundToInt(a * 255f);
            for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, ab);
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _vGradientSprite = Sprite.Create(tex, new Rect(0, 0, W, H),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _vGradientSprite;
    }

    private Sprite _bgGradientSprite;
    // Full-screen vertical blend baked as RGB (MatBottom at the bottom →
    // MatTop at the top), LINEAR so there's no perceived seam.
    private Sprite GetBgGradientSprite()
    {
        if (_bgGradientSprite != null) return _bgGradientSprite;
        const int W = 4, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)   // y=0 = bottom row
        {
            Color32 c = Color.Lerp(MatBottom, MatTop, y / (float)(H - 1));
            for (int x = 0; x < W; x++) px[y * W + x] = c;
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _bgGradientSprite = Sprite.Create(tex, new Rect(0, 0, W, H),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _bgGradientSprite;
    }

    // Adds a soft radial glow panel filling the given anchor rect of `parent`.
    private RectTransform AddRadialGlow(RectTransform parent, Color color,
        Vector2 aMin, Vector2 aMax)
    {
        var glow = PanelObject("Glow", parent, color);
        var img  = glow.GetComponent<Image>();
        img.sprite       = GetRadialSprite();
        img.type         = Image.Type.Simple;
        img.raycastTarget = false;
        Stretch(glow, aMin, aMax, Vector2.zero, Vector2.zero);
        return glow;
    }
}
