// MainMenuManager.cs
// Main Menu screen for One Piece TCG Simulator.
// Pure procedural uGUI — same helpers, palette, and rebuild pattern as GameManager.cs.
// No prefabs, no UI Toolkit, no external packages.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NOTE: declared partial — the My Profile stage (player-scouting handoff) lives in
// MainMenuManager.Profile.cs to keep this file from growing further.
public partial class MainMenuManager : MonoBehaviour
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
    private const string DefaultPlayerName = "Legendary Captain Usopp";

    // Versus-self deck picks. Static so they survive this MonoBehaviour being
    // torn down and recreated (single-scene design) whenever a picker round-trip
    // rebuilds the menu — mirrors DeckStore.ActiveDeckId's lifetime.
    private static string p1DeckId;
    private static string p2DeckId;

    // ── Lobby (networked match) deck selection ──────────────────────────────
    // Static because the deck picker tears down this MainMenuManager instance and
    // a new one is built when the picker closes — same pattern as p1/p2DeckId.
    private static string lobbyDeckId;            // our pick: DeckStore id or "starter:stXX"; null = seat default
    private static NetworkDeck lobbyPeerDeck;     // the peer's shared pick (via OptcgDeckShare); null = their default
    private static bool reopenLobbyAfterPicker;   // restore the waiting room after the picker closes
    // Set when ENTER is pressed without both decks chosen; cleared automatically
    // once both are valid (recomputed fresh every BuildLaunchBar), so it doesn't
    // need its own timer — it just flags whichever slot(s) are still empty.
    private bool enterAlert;

    // When true, the stage shows the replay browser instead of the game-mode portals.
    private bool showingReplays;

    // ── Match History sub-state (only meaningful while showingReplays) ────────
    // selectedMatchId: null = the scannable 20-row list, set = the turn-by-turn
    // detail view for that match. matchDetailTab picks the detail tab, and
    // matchFilter re-filters the list. All reset when the nav row is clicked.
    private string selectedMatchId;
    private string matchDetailTab = "log";     // "log" | "decks"
    private string matchFilter = "all";        // "all" | "win" | "loss"
    // Merged data source: account summaries (MatchHistoryStore) + local replays
    // (ReplayStore — watch-ability + guest fallback), deduped by id, newest
    // first, capped at 20. null = not loaded yet (kicks off LoadMatchHistory).
    private List<MatchSummary> matchHistory;
    private bool matchHistoryLoading;
    // Which of the merged matches still have a full local replay on disk —
    // only those rows get a Watch button (cloud summaries can't be re-simmed).
    private readonly Dictionary<string, ReplayRecord> localReplaysById = new Dictionary<string, ReplayRecord>();

    // Card library + face-crop data for the Match History banners/decklists —
    // the same official-card-library.json / face-data.json files DeckBuilder
    // loads, parsed lazily the first time the stage is shown.
    private CardRec[] menuCardLibrary;
    private Dictionary<string, CardRec> menuCardsById;
    private Dictionary<string, float> menuFaceY, menuFaceX;
    private readonly Dictionary<string, Sprite> _thumbArtCache = new Dictionary<string, Sprite>();

    // One Piece colour swatches (mirror of DeckBuilderManager.ColorSwatch, which
    // is private there) — leader colour dots, life pips, deck-row dots.
    private static readonly Dictionary<string, Color> MenuColorSwatch = new Dictionary<string, Color>
    {
        { "Red",    new Color32(214,  68,  68, 255) },
        { "Green",  new Color32( 70, 180, 110, 255) },
        { "Blue",   new Color32( 70, 140, 220, 255) },
        { "Purple", new Color32(160, 110, 210, 255) },
        { "Black",  new Color32( 90, 100, 120, 255) },
        { "Yellow", new Color32(230, 200,  90, 255) },
    };
    private static readonly Color GoodGreen = new Color32( 79, 208, 138, 255);   // WIN / win-rate
    private static readonly Color RowBg     = new Color32( 17,  32,  47, 255);   // match row / panel fill

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

    // Account gate: shown automatically once signed in if no username has been claimed
    // yet (required to play - it's the name shown as lobby owner), before any other
    // stage is reachable. Also offers switching to "sign in with an existing account"
    // for players recovering onto a new device instead of claiming a fresh name.
    private bool showingAccountGate;
    private bool accountGateSignInMode;
    // Post-claim "secure your account" step: shown right after a successful name claim
    // so linking a recovery email is offered up front instead of buried in settings.
    private bool accountGatePostClaimMode;
    // Two-click arm for signing out with no recovery email linked (account would be lost).
    private bool signOutArmed;
    // Show/hide state for the settings password field ("SHOW"/"HIDE" toggle).
    private bool settingsPasswordVisible;
    // Registration-only: retype-your-password confirmation field.
    private string accountPasswordConfirmInput = "";
    // SHOW/HIDE toggles for the registration password fields.
    private bool regPasswordVisible;
    private bool regConfirmVisible;
    // Settings sub-view: add/change the secondary recovery email.
    private bool accountSettingsRecoveryMode;
    private string recoveryEmailInput = "";
    // Account & Recovery: opened from the gear icon / Settings nav row. Lets an
    // already-playing account link email+password (for recovery) and request/confirm
    // a password reset. Not required to play, unlike the gate above.
    private bool showingAccountSettings;
    private bool accountSettingsResetMode;
    private string usernameInput = "";
    private string accountEmailInput = "";
    private string accountPasswordInput = "";
    private string resetTokenInput = "";
    private string resetNewPasswordInput = "";
    private bool accountBusy;
    private string accountError;

    // Friends stage: shown for the "Friends" nav row. Relationship graph + presence come
    // from FriendsManager.cs (Unity Friends service); lists are cached here and refreshed
    // on navigation and on FriendsManager.FriendsChanged (real-time relationship/presence
    // updates), the same way browsedLobbies caches LobbyManager's results.
    private bool showingFriends;
    private string addFriendUsernameInput = "";
    private bool friendsBusy;
    private string friendsError;
    private List<FriendEntry> friendsList = new List<FriendEntry>();
    private List<FriendEntry> incomingRequests = new List<FriendEntry>();
    private List<FriendEntry> outgoingRequests = new List<FriendEntry>();

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

        // Coming back from the lobby's deck picker: reopen the waiting room (the
        // session itself survived in LobbyManager.CurrentSession; only the UI died).
        if (reopenLobbyAfterPicker)
        {
            reopenLobbyAfterPicker = false;
            showingLobbyHub = LobbyManager.CurrentSession != null;
        }

        EnsureEventSystem();
        BuildCanvas();
        RenderMenu();

        // Subscribed for this object's whole lifetime (not just while the lobby hub is
        // open) so the guest still gets launched into the match if they've wandered
        // elsewhere in the menu when the host clicks Start Match.
        MatchNetworkSync.MatchStartReceived -= OnNetworkMatchStartReceived;
        MatchNetworkSync.MatchStartReceived += OnNetworkMatchStartReceived;

        MatchNetworkSync.DeckShareReceived -= OnPeerDeckShared;
        MatchNetworkSync.DeckShareReceived += OnPeerDeckShared;

        FriendsManager.FriendsChanged -= OnFriendsChanged;
        FriendsManager.FriendsChanged += OnFriendsChanged;

        CheckAccountGateOnBoot();
    }

    // Repaints live on any relationship/presence change (ours or a friend's) rather than
    // only refreshing when the player happens to navigate back to the Friends stage.
    private void OnFriendsChanged()
    {
        if (this == null || menuRoot == null) return;
        RefreshFriendsLists();
    }

    private async void CheckAccountGateOnBoot()
    {
        try
        {
            await AccountManager.EnsureReadyAsync();
            var existing = await AccountManager.LoadOwnUsernameAsync();
            if (this == null || menuRoot == null) return;
            if (string.IsNullOrEmpty(existing))
            {
                // Returning guests skip the welcome gate - their choice was saved.
                // The gate comes back via Settings > Create Account / Sign In.
                if (AccountManager.TryRestoreGuestSession())
                {
                    RenderMenu(); // top bar picks up the restored guest name
                    return;
                }
                showingAccountGate = true;
                RenderMenu();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Account gate check failed: {ex.Message}");
        }
    }

    // ── Leader art (versus-self deck-slot thumbnails) ───────────────────────────
    // All card art/data IO goes through CardAssets: direct file IO in the Editor
    // and desktop builds (UseCdn=false — behavior identical to the old inline
    // File.* code), HTTP from the R2 CDN on WebGL. Paths are Cards-relative.
    private static string LeaderArtPath(string id)
        => CardAssets.FirstExisting(CardAssets.ArtCandidates(id));

    // Decode + the sharpness settings previously duplicated in LoadArt /
    // LoadThumbSprite (same fix as GameManager.LoadFile / DeckBuilder's queue).
    private static Sprite SpriteFromBytes(byte[] bytes)
    {
        if (bytes == null) return null;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!tex.LoadImage(bytes)) return null;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            tex.mipMapBias = -0.75f;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch { return null; }
    }

    private Sprite LoadArt(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_leaderArtCache.TryGetValue(id, out var cached)) return cached;

        if (!CardAssets.UseCdn)
        {
            // Desktop/Editor: synchronous, same behavior as always.
            Sprite sprite = null;
            string rel = LeaderArtPath(id);
            if (rel != null)
            {
                try { sprite = SpriteFromBytes(File.ReadAllBytes(CardAssets.LocalPath(rel))); }
                catch { /* ignore */ }
            }
            _leaderArtCache[id] = sprite;
            return sprite;
        }

        // WebGL/CDN: no result this frame — kick an async fetch; the menu
        // re-renders (coalesced, in Update) when art arrives and finds it cached.
        KickMenuArtLoad(id, thumbFirst: false);
        return null;
    }

    // One in-flight fetch per id; caches the result (null included, so missing
    // art is not re-requested) and queues a single re-render.
    private async void KickMenuArtLoad(string id, bool thumbFirst)
    {
        if (!_menuArtPending.Add(id)) return;
        try
        {
            while (!CardAssets.Ready) await Task.Yield();   // index.json still downloading

            string rel = null;
            if (thumbFirst)
            {
                var t = CardAssets.ThumbCandidate(id);
                if (CardAssets.Exists(t)) rel = t;
            }
            rel ??= CardAssets.FirstExisting(CardAssets.ArtCandidates(id));

            var sprite = rel != null ? SpriteFromBytes(await CardAssets.ReadBytesAsync(rel)) : null;
            if (thumbFirst) _thumbArtCache[id] = sprite; else _leaderArtCache[id] = sprite;
            if (sprite != null) _menuArtRefreshQueued = true;
        }
        finally { _menuArtPending.Remove(id); }
    }

    private readonly HashSet<string> _menuArtPending = new HashSet<string>();
    private bool _menuArtRefreshQueued;

    private void Start()
    {
        // Update clock every 30 seconds (no need for every-second polling)
        InvokeRepeating(nameof(UpdateClock), 30f, 30f);
        BootUpdateAndAssetsOnce();
    }

    // Launch-time update check + CDN asset index, once per app run. On WebGL a
    // newer deployed build hard-reloads the page inside CheckAsync; on desktop
    // it raises UpdateChecker.OnUpdateAvailable (hook a prompt to it later).
    // CheckAndApplyDesktopUpdateAsync is the real exe auto-updater (Velopack):
    // it silently downloads + restarts into a newer build if one exists on
    // GitHub Releases, independently of the buildNumber/manifest check above.
    private static bool _bootRan;
    private static async void BootUpdateAndAssetsOnce()
    {
        if (_bootRan) return;
        _bootRan = true;

        // Desktop: the Velopack check runs FIRST, behind a full-screen splash,
        // so an update installs before the player ever sees the menu — launch
        // reads as "checking… downloading… restarting" instead of the game
        // opening, closing, and reopening. Restarting is unavoidable (Windows
        // can't patch a running exe) but this way it happens seconds into boot,
        // behind the splash, before any interaction.
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        ShowUpdateSplash();
        bool restarting = await UpdateChecker.CheckAndApplyDesktopUpdateAsync(s => _updateStatus = s);
        if (restarting) return;          // keep the splash up; Velopack relaunches us momentarily
        HideUpdateSplash();
#endif
        await UpdateChecker.CheckAsync();
        await CardAssets.InitAsync();
    }

    // ── Update splash ─────────────────────────────────────────────────────────
    // Minimal self-contained canvas (sorted above everything) shown during the
    // launch-time Velopack check. Status text arrives from background threads via
    // _updateStatus; Update() applies it on the main thread.
    private static GameObject _updateSplash;
    private static Text _updateSplashText;
    private static volatile string _updateStatus;

    private static void ShowUpdateSplash()
    {
        if (_updateSplash != null) return;
        _updateSplash = new GameObject("Update Splash");
        UnityEngine.Object.DontDestroyOnLoad(_updateSplash);
        var canvas = _updateSplash.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above every menu canvas

        var bg = new GameObject("BG").AddComponent<Image>();
        bg.transform.SetParent(_updateSplash.transform, false);
        bg.color = new Color32(7, 13, 22, 255);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

        _updateSplashText = new GameObject("Status").AddComponent<Text>();
        _updateSplashText.transform.SetParent(_updateSplash.transform, false);
        _updateSplashText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _updateSplashText.fontSize = 18;
        _updateSplashText.alignment = TextAnchor.MiddleCenter;
        _updateSplashText.color = new Color32(126, 200, 227, 255);
        _updateSplashText.text = "CHECKING FOR UPDATES...";
        var txtRt = _updateSplashText.rectTransform;
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
    }

    private static void HideUpdateSplash()
    {
        if (_updateSplash != null) UnityEngine.Object.Destroy(_updateSplash);
        _updateSplash = null;
        _updateSplashText = null;
        _updateStatus = null;
    }

    // ── Tab navigation between input fields ──────────────────────────────────
    // uGUI InputField has no built-in tab support; MakeInput registers each field
    // in creation order (== visual order) and this walks the list. Shift+Tab
    // goes backwards. Applies to every menu screen with inputs, not just the gate.

    private readonly List<InputField> tabOrder = new List<InputField>();

    private void Update()
    {
        // Update-splash status arrives from Velopack's background threads; apply
        // it to the UI here, on the main thread.
        if (_updateSplashText != null && _updateStatus != null)
            _updateSplashText.text = _updateStatus;

        // Coalesced re-render when async card art/library data arrives (CDN
        // builds): many loads can complete in one frame; rebuild the menu once.
        if (_menuArtRefreshQueued && menuRoot != null && !showingAccountGate)
        {
            _menuArtRefreshQueued = false;   // stays queued while the gate is up
            RenderMenu();
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        // Enter submits whichever account form is showing (uGUI single-line
        // InputFields end their edit on Enter but don't consume the key).
        if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            && showingAccountGate && !accountBusy)
        {
            if (accountGatePostClaimMode) LinkEmailClicked();
            else if (accountGateSignInMode) SignInWithEmailClicked();
            else ClaimUsernameClicked();
            return;
        }

        if (!kb.tabKey.wasPressedThisFrame) return;

        tabOrder.RemoveAll(f => f == null); // destroy-and-rebuild leaves stale entries
        if (tabOrder.Count == 0) return;

        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        int idx = -1;
        for (int i = 0; i < tabOrder.Count; i++)
            if (selected != null && tabOrder[i].gameObject == selected) { idx = i; break; }

        bool backwards = kb.shiftKey.isPressed;
        int next = idx < 0
            ? (backwards ? tabOrder.Count - 1 : 0)          // nothing focused: start at an end
            : (idx + (backwards ? -1 : 1) + tabOrder.Count) % tabOrder.Count;

        var target = tabOrder[next];
        target.Select();
        target.ActivateInputField();
        target.MoveTextEnd(false);
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
        tabOrder.Clear();

        BuildBackground();
        BuildTopBar();
        BuildBody();
        // Account gate renders as a modal over the whole menu (top bar included) so the
        // menu stays visible-but-locked behind it instead of the stage being hijacked.
        if (showingAccountGate) BuildAccountGateModal(menuRoot);
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

        // ── Left — player identity (click → My Profile stage) ───────────────
        var identity = PanelObject("Identity", bar,
            showingProfile ? new Color(Accent.r, Accent.g, Accent.b, 0.12f) : new Color(0, 0, 0, 0));
        Stretch(identity, Vector2.zero, new Vector2(0.32f, 1f), new Vector2(12f, 6f), new Vector2(0f, -6f));
        Round(identity);
        if (showingProfile)
            AddRoundedCardBorder(identity, new Color(Accent.r, Accent.g, Accent.b, 0.40f), 1.5f);
        var identityBtn = identity.gameObject.AddComponent<Button>();
        identityBtn.onClick.AddListener(OpenMyProfile);

        // Circular avatar showing the player's chosen profile icon (face-crop of
        // card art; picker lives in MainMenuManager.IconPicker.cs). Falls back to
        // a steel circle with the player's initial when no icon is set.
        BuildCircleFaceIcon(identity, EffectiveProfileIconId(), 34f, new Vector2(6f, 0f));
        KickProfileIconCloudRefresh();

        var nameText = TextObject("PlayerName", identity, AccountManager.CurrentUsername ?? AccountManager.CachedUsername ?? AccountManager.GuestDisplayName ?? DefaultPlayerName, 15, Ink, TextAnchor.LowerLeft);
        nameText.fontStyle = FontStyle.Bold;
        nameText.raycastTarget = false;
        Stretch(nameText.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(52f, 2f), Vector2.zero);

        var subText = TextObject("PlayerSub", identity, "CAPTAIN  ·  VIEW PROFILE", 10, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(subText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(52f, 0f), new Vector2(0f, -2f));

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
        Stretch(clockText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-120f, 0f));

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
        gearBtn.onClick.AddListener(OpenAccountSettings);

        // ── Exit game button, left of the gear. Plain "EXIT" text — the ⏻ power
        // glyph isn't in the runtime fonts and renders as a blank box. ─────────
        var exit = PanelObject("Exit Btn", rightGroup, LogBgDark);
        exit.anchorMin = new Vector2(1f, 0.5f);
        exit.anchorMax = new Vector2(1f, 0.5f);
        exit.pivot     = new Vector2(1f, 0.5f);
        exit.sizeDelta = new Vector2(56f, 40f);
        exit.anchoredPosition = new Vector2(-48f, 0f);
        Round(exit);
        AddRoundedCardBorder(exit, MenuB, 1f);

        var exitLabel = TextObject("Exit Label", exit, "EXIT", 11, Muted, TextAnchor.MiddleCenter, monoFont);
        exitLabel.fontStyle = FontStyle.Bold;
        Stretch(exitLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var exitBtn = exit.gameObject.AddComponent<Button>();
        exitBtn.onClick.AddListener(ShowExitConfirm);
    }

    // ── Exit confirmation ─────────────────────────────────────────────────────
    // Quitting shouldn't be one accidental click away: EXIT opens a modal that
    // dims the menu and asks first. CANCEL (or clicking the dim backdrop) closes
    // it; EXIT GAME actually quits.
    private RectTransform exitConfirmOverlay;

    private void ShowExitConfirm()
    {
        if (exitConfirmOverlay != null) return;   // already open

        // Full-screen dim backdrop — parented to the canvas root so it sits on
        // top of everything, and raycast-blocking so the menu behind is inert.
        var overlay = PanelObject("Exit Confirm Overlay", canvas.transform, new Color(0f, 0f, 0f, 0.62f));
        Stretch(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        exitConfirmOverlay = overlay;
        overlay.gameObject.AddComponent<Button>().onClick.AddListener(HideExitConfirm); // click-away = cancel

        // Dialog panel
        var panel = PanelObject("Exit Dialog", overlay, LogBgDark);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(380f, 170f);
        RoundBig(panel);
        AddRoundedCardBorder(panel, MenuB, 1f);
        // Swallow clicks so tapping the panel body doesn't trigger the backdrop's cancel.
        panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

        var title = TextObject("Title", panel, "EXIT GAME?", 16, Ink, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0.62f), Vector2.one, new Vector2(16f, 0f), new Vector2(-16f, -14f));

        var sub = TextObject("Sub", panel, "Close the client and return to desktop?", 12, Muted, TextAnchor.UpperCenter);
        Stretch(sub.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 0.62f), new Vector2(16f, 0f), new Vector2(-16f, 0f));

        // CANCEL — safe default on the left
        var cancel = PanelObject("Cancel Btn", panel, new Color32(24, 38, 52, 220));
        Stretch(cancel, new Vector2(0.06f, 0.10f), new Vector2(0.47f, 0.34f), Vector2.zero, Vector2.zero);
        Round(cancel);
        AddRoundedCardBorder(cancel, MenuB, 1f);
        var cancelT = TextObject("t", cancel, "CANCEL", 12, Ink, TextAnchor.MiddleCenter, monoFont);
        Stretch(cancelT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cancel.gameObject.AddComponent<Button>().onClick.AddListener(HideExitConfirm);

        // EXIT GAME — destructive action on the right, red accent
        var confirm = PanelObject("Confirm Btn", panel, new Color32(170, 56, 56, 235));
        Stretch(confirm, new Vector2(0.53f, 0.10f), new Vector2(0.94f, 0.34f), Vector2.zero, Vector2.zero);
        Round(confirm);
        var confirmT = TextObject("t", confirm, "EXIT GAME", 12, Ink, TextAnchor.MiddleCenter, monoFont);
        confirmT.fontStyle = FontStyle.Bold;
        Stretch(confirmT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        confirm.gameObject.AddComponent<Button>().onClick.AddListener(QuitGame);
    }

    private void HideExitConfirm()
    {
        if (exitConfirmOverlay != null) Destroy(exitConfirmOverlay.gameObject);
        exitConfirmOverlay = null;
    }

    // Closes the application. Application.Quit() is a no-op inside the editor,
    // so stop play mode there instead — same behavior players get from the .exe.
    private static void QuitGame()
    {
        // Tear down Netcode BEFORE quitting. NGO has a known teardown bug where
        // app quit runs its shutdown twice (OnApplicationQuit -> OnDestroy) and
        // the second pass NREs in NetworkSceneManager.Dispose (SceneEventDataStore
        // already nulled). Shutting down + destroying the NetworkManager here,
        // while the app is still alive, means the quit path finds nothing to
        // double-dispose.
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            try { nm.Shutdown(); } catch (Exception e) { Debug.LogWarning("Netcode shutdown on quit: " + e.Message); }
            try { DestroyImmediate(nm.gameObject); } catch (Exception e) { Debug.LogWarning("NetworkManager teardown on quit: " + e.Message); }
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
        if (showingAccountSettings) BuildAccountSettingsStage(stage);
        else if (showingFriends) BuildFriendsStage(stage);
        else if (showingProfileIcon) BuildProfileIconStage(stage);
        else if (showingProfile) BuildProfileStage(stage);
        else if (showingReplays) BuildReplayStage(stage);
        else if (showingLobbyHub) BuildLobbyStage(stage);
        else BuildStage(stage);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Replay browser (stage swapped in for "Replays" nav row)
    // ══════════════════════════════════════════════════════════════════════════

    // Entry point kept as BuildReplayStage so the nav wiring above is untouched.
    // Routes between the two Match History views: the scannable 20-row list, and
    // the turn-by-turn detail (selectedMatchId set by clicking a row).
    private void BuildReplayStage(RectTransform stage)
    {
        EnsureMenuCardLibrary();
        EnsureMenuFaceData();

        // Data not loaded yet: kick off the merge (cloud summaries + local
        // replays) and show the loading state until it lands.
        if (matchHistory == null)
        {
            if (!matchHistoryLoading) LoadMatchHistory();
            var loading = TextObject("Loading", stage, "Loading match history...",
                13, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(loading.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(4f, -60f), Vector2.zero);
            return;
        }

        if (selectedMatchId != null)
        {
            var match = matchHistory.Find(m => m != null && m.id == selectedMatchId);
            if (match != null) { BuildMatchDetail(stage, match); return; }
            selectedMatchId = null;   // stale id (match aged out) — fall back to the list
        }
        BuildMatchList(stage);
    }

    // ── Data source: cloud summaries merged with local replays ────────────────

    private async void LoadMatchHistory()
    {
        matchHistoryLoading = true;
        List<MatchSummary> cloud = null;
        try { cloud = await MatchHistoryStore.LoadAsync(); }
        catch (Exception ex) { Debug.LogWarning($"Match history load failed: {ex.Message}"); }
        if (this == null || menuRoot == null) return;
        matchHistoryLoading = false;
        matchHistory = MergeMatchHistory(cloud);
        if (showingReplays) RenderMenu();
    }

    // Account summaries win on dedupe (they carry the turn log); local replays
    // fill in anything the cloud doesn't have (guest play, offline matches) and
    // decide which rows are watchable. Ids are timestamp-sortable on both sides
    // (MatchSummary.id == ReplayRecord.Id), so an ordinal sort = newest first.
    private List<MatchSummary> MergeMatchHistory(List<MatchSummary> cloud)
    {
        localReplaysById.Clear();
        var merged = new List<MatchSummary>();
        var seen = new HashSet<string>();
        if (cloud != null)
            foreach (var m in cloud)
                if (m != null && !string.IsNullOrEmpty(m.id) && seen.Add(m.id)) merged.Add(m);
        foreach (var r in ReplayStore.ListAll())
        {
            if (r == null || string.IsNullOrEmpty(r.Id)) continue;
            localReplaysById[r.Id] = r;
            if (seen.Add(r.Id)) merged.Add(SummaryFromLocalRecord(r));
        }
        merged.Sort((a, b) => string.CompareOrdinal(b.id, a.id));
        if (merged.Count > 20) merged.RemoveRange(20, merged.Count - 20);
        return merged;
    }

    // Shallow summary for a replay that never reached the cloud (guest/offline).
    // A ReplayRecord has no EventLog (only the re-simmable CommandHistory), so
    // turns stay empty and the detail view shows a friendly note instead.
    private MatchSummary SummaryFromLocalRecord(ReplayRecord r)
    {
        // Replays are saved from the south (local) perspective, and WinnerName is
        // the "{name} wins." name — same string ParseMatchup stored per side. So:
        // south won unless the winner matches the north name.
        bool win = !string.IsNullOrEmpty(r.WinnerName) && r.WinnerName != r.NorthDeckName;
        var s = new MatchSummary
        {
            id = r.Id,
            savedAtIso = r.SavedAtIso,
            result = win ? "win" : "loss",
            youLeaderId = r.SouthLeaderId,
            oppLeaderId = r.NorthLeaderId,
            oppName = string.IsNullOrEmpty(r.NorthDeckName) ? "Opponent" : r.NorthDeckName,
            youFirst = r.FirstPlayer != "north",
            youFinalLife = r.SouthFinalLife,
            oppFinalLife = r.NorthFinalLife,
            youMaxLife = MenuLeaderBaseLife(r.SouthLeaderId),
            oppMaxLife = MenuLeaderBaseLife(r.NorthLeaderId),
            turnCount = r.TurnCount,
            durationSeconds = r.DurationSeconds,
        };
        // Decklists resolve the same way the live save site does — from the
        // deck-builder store, if those decks still exist locally.
        var southDeck = string.IsNullOrEmpty(r.SouthDeckId) ? null : DeckStore.Get(r.SouthDeckId);
        var northDeck = string.IsNullOrEmpty(r.NorthDeckId) ? null : DeckStore.Get(r.NorthDeckId);
        if (southDeck?.cards != null)
            foreach (var e in southDeck.cards)
                if (e != null && e.count > 0) s.youDeck.Add(new MatchDeckCount { cardId = e.id, count = e.count });
        if (northDeck?.cards != null)
            foreach (var e in northDeck.cards)
                if (e != null && e.count > 0) s.oppDeck.Add(new MatchDeckCount { cardId = e.id, count = e.count });
        return s;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Match History — LIST VIEW
    // ══════════════════════════════════════════════════════════════════════════

    // Stage pixel width at the 1920x1080 reference: 1920 - 2*22 body pad - 234
    // rail - 20 gap. Rows are laid out with absolute offsets against this, the
    // same hand-anchored style as the rest of the file.
    private const float MatchStageW = 1622f;

    private void BuildMatchList(RectTransform stage)
    {
        const float headerH = 78f;

        // ── Header row: title + sub (left), win-rate card + filter chips (right)
        var header = PanelObject("MH Header", stage, new Color(0, 0, 0, 0));
        Stretch(header, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -headerH), Vector2.zero);

        var title = TextObject("Title", header, "Match History", 27, Ink, TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(4f, 22f), Vector2.zero);

        var sub = TextObject("Sub", header, "LAST 20 MATCHES  ·  SOUTH SEAT", 11, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(sub.rectTransform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(4f, 4f), new Vector2(0f, 26f));

        int wins = 0, total = 0;
        foreach (var m in matchHistory) if (m != null) { total++; if (m.result == "win") wins++; }
        int losses = total - wins;
        int pct = total > 0 ? Mathf.RoundToInt(wins * 100f / total) : 0;

        // Win-rate card. The HTML mock's conic-gradient ring has no uGUI
        // equivalent without a custom shader, so this is the sanctioned
        // approximation: "12–8" record, a horizontal win/loss bar, and the
        // percentage — same information, same palette.
        var wr = PanelObject("WinRate", header, RowBg);
        wr.anchorMin = new Vector2(1f, 1f); wr.anchorMax = new Vector2(1f, 1f);
        wr.pivot = new Vector2(1f, 1f);
        wr.sizeDelta = new Vector2(236f, 62f);
        wr.anchoredPosition = new Vector2(0f, 0f);
        RoundBig(wr);
        AddRoundedCardBorder(wr, MenuB, 1f);

        var pctText = TextObject("Pct", wr, pct + "%", 19, GoodGreen, TextAnchor.MiddleCenter, monoFont);
        pctText.fontStyle = FontStyle.Bold;
        Stretch(pctText.rectTransform, new Vector2(0f, 0f), new Vector2(0.32f, 1f), new Vector2(8f, 0f), Vector2.zero);

        var record = TextObject("Record", wr, $"{wins}–{losses}", 17, Ink, TextAnchor.UpperLeft);
        record.fontStyle = FontStyle.Bold;
        Stretch(record.rectTransform, new Vector2(0.34f, 0.45f), Vector2.one, new Vector2(4f, 0f), new Vector2(-10f, -8f));

        var recordLabel = TextObject("RecordLabel", wr, "WIN / LOSS", 10, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(recordLabel.rectTransform, new Vector2(0.34f, 0.18f), new Vector2(1f, 0.45f), new Vector2(4f, 0f), new Vector2(-10f, 0f));

        // Win/loss bar along the card's bottom: green fill = win share.
        var barBg = PanelObject("BarBg", wr, new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.35f));
        Stretch(barBg, new Vector2(0.34f, 0f), new Vector2(1f, 0f), new Vector2(4f, 8f), new Vector2(-10f, 13f));
        Round(barBg);
        if (total > 0)
        {
            var barFill = PanelObject("BarFill", barBg, GoodGreen);
            Stretch(barFill, Vector2.zero, new Vector2(Mathf.Clamp01(wins / (float)total), 1f), Vector2.zero, Vector2.zero);
            Round(barFill);
        }

        // Filter chips: All / Wins / Losses (pill group left of the win-rate card).
        var chips = PanelObject("Filters", header, new Color32(14, 28, 43, 255));
        chips.anchorMin = new Vector2(1f, 1f); chips.anchorMax = new Vector2(1f, 1f);
        chips.pivot = new Vector2(1f, 1f);
        chips.sizeDelta = new Vector2(248f, 34f);
        chips.anchoredPosition = new Vector2(-252f, -14f);
        Round(chips);
        AddRoundedCardBorder(chips, MenuB, 1f);
        BuildFilterChip(chips, "All", "all", 0);
        BuildFilterChip(chips, "Wins", "win", 1);
        BuildFilterChip(chips, "Losses", "loss", 2);

        // ── Filtered rows in a vertical scroll (20 rows won't all fit 1080) ──
        var filtered = new List<MatchSummary>();
        foreach (var m in matchHistory)
            if (m != null && (matchFilter == "all" || m.result == matchFilter)) filtered.Add(m);

        var listArea = PanelObject("MH List", stage, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -headerH - 8f));

        if (filtered.Count == 0)
        {
            string msg = matchHistory.Count == 0
                ? "No matches yet — finish a match and it'll show up here."
                : "No matches for this filter.";
            var empty = TextObject("Empty", listArea, msg, 13, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, -34f), Vector2.zero);
            return;
        }

        const float rowH = 94f, gap = 9f;
        var content = MakeMenuScroll(listArea, filtered.Count * (rowH + gap) - gap);
        for (int i = 0; i < filtered.Count; i++)
        {
            var row = PanelObject("Match Row " + i, content, RowBg);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowH);
            row.anchoredPosition = new Vector2(0f, -(i * (rowH + gap)));
            BuildMatchRow(row, filtered[i]);
        }
    }

    private void BuildFilterChip(RectTransform group, string label, string value, int index)
    {
        bool active = matchFilter == value;
        var chip = PanelObject(label + " Chip", group, active ? Accent : new Color(0, 0, 0, 0));
        chip.anchorMin = new Vector2(index / 3f, 0f);
        chip.anchorMax = new Vector2((index + 1) / 3f, 1f);
        chip.offsetMin = new Vector2(4f, 4f);
        chip.offsetMax = new Vector2(-4f, -4f);
        Round(chip);
        var t = TextObject("t", chip, label, 11, active ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = chip.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { matchFilter = value; RenderMenu(); });
    }

    // One 94px match row: stripe · result column · YOU banner · final-life
    // center · OPP banner · date/watch/chevron meta.
    private void BuildMatchRow(RectTransform row, MatchSummary m)
    {
        Round(row);
        AddRoundedCardBorder(row, MenuB, 1f);

        bool win = m.result == "win";
        Color resColor = win ? GoodGreen : RedAccent;
        ReplayRecord localReplay;
        localReplaysById.TryGetValue(m.id, out localReplay);

        // Whole row opens the detail view (Button color-tint gives the hover lift).
        var rowBtn = row.gameObject.AddComponent<Button>();
        string capturedId = m.id;
        rowBtn.onClick.AddListener(() => { selectedMatchId = capturedId; matchDetailTab = "log"; RenderMenu(); });

        // 1. Result stripe (4px, colored).
        var stripe = PanelObject("Stripe", row, resColor);
        Stretch(stripe, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 3f), new Vector2(4f, -3f));
        stripe.GetComponent<Image>().raycastTarget = false;

        // 2. Result column: WIN/LOSS badge over "T{n} · {m}m{ss}s".
        var badge = PanelObject("Badge", row, new Color(resColor.r, resColor.g, resColor.b, 0.13f));
        badge.anchorMin = new Vector2(0f, 1f); badge.anchorMax = new Vector2(0f, 1f);
        badge.pivot = new Vector2(0f, 1f);
        badge.sizeDelta = new Vector2(74f, 27f);
        badge.anchoredPosition = new Vector2(18f, -18f);
        Round(badge);
        AddRoundedCardBorder(badge, new Color(resColor.r, resColor.g, resColor.b, 0.55f), 1f);
        var badgeT = TextObject("t", badge, win ? "WIN" : "LOSS", 14, resColor, TextAnchor.MiddleCenter, monoFont);
        badgeT.fontStyle = FontStyle.Bold;
        Stretch(badgeT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var meta = TextObject("TurnDur", row, $"T{m.turnCount} · {FormatDuration(m.durationSeconds)}",
            10, new Color32(133, 152, 168, 255), TextAnchor.UpperLeft, monoFont);
        Stretch(meta.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(18f, 12f), new Vector2(118f, -52f));

        // 3/5. Leader banners (eye-cropped art + darkening gradient + overlay).
        const float bannerL = 124f, centerHalf = 63f, metaW = 166f;
        float bannerW = MatchStageW / 2f - bannerL - centerHalf;   // approx px width for cover math

        var youBanner = PanelObject("You Banner", row, RowBg);
        Stretch(youBanner, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(bannerL, 6f), new Vector2(-centerHalf, -6f));
        BuildLeaderBanner(youBanner, m.youLeaderId, bannerW, 82f, true);
        BuildBannerOverlay(youBanner, m, true, false);

        var oppBanner = PanelObject("Opp Banner", row, RowBg);
        Stretch(oppBanner, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(centerHalf, 6f), new Vector2(-metaW, -6f));
        BuildLeaderBanner(oppBanner, m.oppLeaderId, bannerW, 82f, false);
        BuildBannerOverlay(oppBanner, m, false, false);

        // 4. Center score: FINAL LIFE / "{a} – {b}" / VS.
        var center = PanelObject("Center", row, new Color(0, 0, 0, 0));
        center.anchorMin = new Vector2(0.5f, 0f); center.anchorMax = new Vector2(0.5f, 1f);
        center.pivot = new Vector2(0.5f, 0.5f);
        center.sizeDelta = new Vector2(118f, 0f);
        center.anchoredPosition = Vector2.zero;   // banners end symmetrically at ±centerHalf around the row middle
        center.GetComponent<Image>().raycastTarget = false;
        var flLabel = TextObject("FL", center, "FINAL LIFE", 9, new Color32(124, 146, 162, 255), TextAnchor.LowerCenter, monoFont);
        Stretch(flLabel.rectTransform, new Vector2(0f, 0.66f), Vector2.one, Vector2.zero, new Vector2(0f, -8f));
        var score = TextObject("Score", center, $"{m.youFinalLife} – {m.oppFinalLife}", 23, Ink, TextAnchor.MiddleCenter);
        score.fontStyle = FontStyle.Bold;
        Stretch(score.rectTransform, new Vector2(0f, 0.32f), new Vector2(1f, 0.66f), Vector2.zero, Vector2.zero);
        var vs = TextObject("VS", center, "VS", 10, new Color32(92, 112, 128, 255), TextAnchor.UpperCenter, monoFont);
        Stretch(vs.rectTransform, Vector2.zero, new Vector2(1f, 0.32f), new Vector2(0f, 8f), Vector2.zero);

        // 6. Right meta: date + "Xh ago", Watch ▷ (only when the full replay is
        // still on this machine), and the "open detail" chevron.
        var when = TextObject("When", row, FormatWhen(m.savedAtIso), 11, new Color32(198, 211, 220, 255), TextAnchor.UpperRight, monoFont);
        Stretch(when.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-metaW + 4f, 0f), new Vector2(-70f, -20f));
        var ago = TextObject("Ago", row, FormatAgo(m.savedAtIso), 10, new Color32(111, 134, 150, 255), TextAnchor.UpperRight, monoFont);
        Stretch(ago.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-metaW + 4f, 0f), new Vector2(-70f, -42f));

        if (localReplay != null)
        {
            var watch = PanelObject("Watch", row, new Color(Accent.r, Accent.g, Accent.b, 0.12f));
            watch.anchorMin = new Vector2(1f, 0.5f); watch.anchorMax = new Vector2(1f, 0.5f);
            watch.pivot = new Vector2(1f, 0.5f);
            watch.sizeDelta = new Vector2(34f, 34f);
            watch.anchoredPosition = new Vector2(-30f, 0f);
            Round(watch);
            AddRoundedCardBorder(watch, new Color(Accent.r, Accent.g, Accent.b, 0.35f), 1f);
            var wIcon = TextObject("t", watch, "▷", 14, Accent, TextAnchor.MiddleCenter);
            Stretch(wIcon.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var capturedReplay = localReplay;
            var wBtn = watch.gameObject.AddComponent<Button>();
            wBtn.onClick.AddListener(() => WatchReplay(capturedReplay));
        }

        var chevron = TextObject("Chevron", row, "›", 18, new Color32(92, 112, 128, 255), TextAnchor.MiddleRight);
        Stretch(chevron.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-24f, 0f), new Vector2(-10f, 0f));
    }

    // Text overlay on a leader banner. youSide = left-aligned "YOU" block;
    // otherwise right-aligned "@opp". `hero` switches to the bigger detail sizes.
    private void BuildBannerOverlay(RectTransform banner, MatchSummary m, bool youSide, bool hero)
    {
        string leaderId = youSide ? m.youLeaderId : m.oppLeaderId;
        var rec = MenuCard(leaderId);
        string leaderName = rec != null ? rec.name : (string.IsNullOrEmpty(leaderId) ? "Unknown Leader" : leaderId);
        Color leaderColor = MenuLeaderColor(leaderId);
        int life = youSide ? m.youFinalLife : m.oppFinalLife;
        int maxLife = Mathf.Max(youSide ? m.youMaxLife : m.oppMaxLife, life);
        bool first = youSide ? m.youFirst : !m.youFirst;
        var anchor = youSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        float pad = hero ? 22f : 16f;

        var holder = PanelObject("Overlay", banner, new Color(0, 0, 0, 0));
        Stretch(holder, Vector2.zero, Vector2.one, new Vector2(pad, 6f), new Vector2(-pad, -6f));
        holder.GetComponent<Image>().raycastTarget = false;

        // Row 1: colour diamond + side label (+ gold 1ST pill when they opened).
        var top = PanelObject("Top", holder, new Color(0, 0, 0, 0));
        Stretch(top, new Vector2(0f, 0.68f), Vector2.one, Vector2.zero, Vector2.zero);
        top.GetComponent<Image>().raycastTarget = false;
        var hlg = top.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 7f;
        hlg.childAlignment = youSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        hlg.childControlWidth = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var dot = PanelObject("Dot", top, leaderColor);
        dot.sizeDelta = new Vector2(9f, 9f);
        SetPreferred(dot, new Vector2(9f, 9f));
        dot.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Round(dot);

        string sideLabel = youSide
            ? (hero ? "YOU · " + (AccountManager.CurrentUsername ?? AccountManager.CachedUsername ?? AccountManager.GuestDisplayName ?? DefaultPlayerName).ToUpperInvariant() : "YOU")
            : "@" + m.oppName;
        var side = TextObject("Side", top, sideLabel, 10, Ink, TextAnchor.MiddleLeft, monoFont);
        side.fontStyle = FontStyle.Bold;
        SetPreferred(side.rectTransform, new Vector2(12f + sideLabel.Length * 6.6f, 16f));

        if (first)
        {
            var pill = PanelObject("First Pill", top, new Color(Gold.r, Gold.g, Gold.b, 0.18f));
            var pillW = hero ? 74f : 36f;
            pill.sizeDelta = new Vector2(pillW, 16f);
            SetPreferred(pill, new Vector2(pillW, 16f));
            Round(pill);
            AddRoundedCardBorder(pill, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 1f);
            var pt = TextObject("t", pill, hero ? "WENT 1ST" : "1ST", 9, Gold, TextAnchor.MiddleCenter, monoFont);
            pt.fontStyle = FontStyle.Bold;
            Stretch(pt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // Row 2: leader name.
        var nameT = TextObject("Leader", holder, leaderName, hero ? 27 : 17, Ink, anchor);
        nameT.fontStyle = FontStyle.Bold;
        Stretch(nameT.rectTransform, new Vector2(0f, 0.30f), new Vector2(1f, 0.68f), Vector2.zero, Vector2.zero);

        // Row 3: life pips + "{N} LIFE" (or "{n}/{max} LIFE" on the hero).
        var pipRow = PanelObject("Pips", holder, new Color(0, 0, 0, 0));
        Stretch(pipRow, Vector2.zero, new Vector2(1f, 0.30f), Vector2.zero, Vector2.zero);
        pipRow.GetComponent<Image>().raycastTarget = false;
        var pipHlg = pipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        pipHlg.spacing = 3f;
        pipHlg.childAlignment = youSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        pipHlg.childControlWidth = false; pipHlg.childControlHeight = false;
        pipHlg.childForceExpandWidth = false; pipHlg.childForceExpandHeight = false;
        float pipW = hero ? 9f : 7f, pipH = hero ? 15f : 12f;
        for (int i = 0; i < maxLife; i++)
        {
            bool filled = i < life;
            var pip = PanelObject("Pip", pipRow, filled ? leaderColor : new Color(0, 0, 0, 0));
            pip.sizeDelta = new Vector2(pipW, pipH);
            SetPreferred(pip, new Vector2(pipW, pipH));
            Round(pip);
            if (!filled) AddRoundedCardBorder(pip, new Color(Muted.r, Muted.g, Muted.b, 0.5f), 1f);
        }
        string lifeLabel = hero ? $"  {life} / {maxLife} LIFE" : $"  {life} LIFE";
        var lifeT = TextObject("LifeLabel", pipRow, lifeLabel, 11, Muted, TextAnchor.MiddleLeft, monoFont);
        SetPreferred(lifeT.rectTransform, new Vector2(12f + lifeLabel.Length * 6.6f, 16f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Match History — DETAIL VIEW (‹ BACK · hero · meta chips · log / decklists)
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildMatchDetail(RectTransform stage, MatchSummary m)
    {
        bool win = m.result == "win";
        Color resColor = win ? GoodGreen : RedAccent;
        ReplayRecord localReplay;
        localReplaysById.TryGetValue(m.id, out localReplay);

        // ── Header bar: BACK + title + date, WATCH REPLAY right ─────────────
        var header = PanelObject("MD Header", stage, new Color(0, 0, 0, 0));
        Stretch(header, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -44f), Vector2.zero);

        var back = PanelObject("Back", header, new Color32(34, 58, 78, 230));
        back.anchorMin = new Vector2(0f, 0.5f); back.anchorMax = new Vector2(0f, 0.5f);
        back.pivot = new Vector2(0f, 0.5f);
        back.sizeDelta = new Vector2(84f, 32f);
        back.anchoredPosition = new Vector2(0f, 0f);
        Round(back);
        AddRoundedCardBorder(back, ZoneBorder, 1f);
        var backT = TextObject("t", back, "‹ BACK", 11, Ink, TextAnchor.MiddleCenter, monoFont);
        backT.fontStyle = FontStyle.Bold;
        Stretch(backT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var backBtn = back.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(() => { selectedMatchId = null; RenderMenu(); });

        var title = TextObject("Title", header, "Match Detail", 20, Ink, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(100f, 0f), Vector2.zero);

        var date = TextObject("Date", header, FormatWhen(m.savedAtIso), 11, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(date.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(240f, 0f), Vector2.zero);

        if (localReplay != null)
        {
            var watch = PanelObject("Watch Replay", header, Accent);
            watch.anchorMin = new Vector2(1f, 0.5f); watch.anchorMax = new Vector2(1f, 0.5f);
            watch.pivot = new Vector2(1f, 0.5f);
            watch.sizeDelta = new Vector2(158f, 32f);
            watch.anchoredPosition = Vector2.zero;
            Round(watch);
            var wT = TextObject("t", watch, "▷ WATCH REPLAY", 11, BadgeInk, TextAnchor.MiddleCenter, monoFont);
            wT.fontStyle = FontStyle.Bold;
            Stretch(wT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var capturedReplay = localReplay;
            var wBtn = watch.gameObject.AddComponent<Button>();
            wBtn.onClick.AddListener(() => WatchReplay(capturedReplay));
        }

        // ── Hero panel (158px): stripe, YOU banner, VICTORY/DEFEAT, OPP banner ─
        var hero = PanelObject("MD Hero", stage, RowBg);
        Stretch(hero, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -44f - 12f - 158f), new Vector2(0f, -44f - 12f));
        RoundBig(hero);
        AddRoundedCardBorder(hero, MenuB, 1f);

        var stripe = PanelObject("Stripe", hero, resColor);
        Stretch(stripe, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 4f), new Vector2(4f, -4f));
        stripe.GetComponent<Image>().raycastTarget = false;

        float heroBannerW = (MatchStageW - 300f) / 2f - 8f;
        var youHero = PanelObject("You Hero", hero, RowBg);
        Stretch(youHero, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(8f, 6f), new Vector2(-150f, -6f));
        BuildLeaderBanner(youHero, m.youLeaderId, heroBannerW, 146f, true);
        BuildBannerOverlay(youHero, m, true, true);

        var oppHero = PanelObject("Opp Hero", hero, RowBg);
        Stretch(oppHero, new Vector2(0.5f, 0f), Vector2.one, new Vector2(150f, 6f), new Vector2(-8f, -6f));
        BuildLeaderBanner(oppHero, m.oppLeaderId, heroBannerW, 146f, false);
        BuildBannerOverlay(oppHero, m, false, true);

        var centerPanel = PanelObject("Hero Center", hero, new Color32(9, 17, 26, 140));
        centerPanel.anchorMin = new Vector2(0.5f, 0f); centerPanel.anchorMax = new Vector2(0.5f, 1f);
        centerPanel.pivot = new Vector2(0.5f, 0.5f);
        centerPanel.sizeDelta = new Vector2(300f, 0f);
        centerPanel.anchoredPosition = Vector2.zero;
        Round(centerPanel);
        var verdict = TextObject("Verdict", centerPanel, win ? "VICTORY" : "DEFEAT", 22, resColor, TextAnchor.LowerCenter, monoFont);
        verdict.fontStyle = FontStyle.Bold;
        Stretch(verdict.rectTransform, new Vector2(0f, 0.62f), Vector2.one, Vector2.zero, new Vector2(0f, -18f));
        var bigScore = TextObject("Score", centerPanel, $"{m.youFinalLife} – {m.oppFinalLife}", 34, Ink, TextAnchor.MiddleCenter);
        bigScore.fontStyle = FontStyle.Bold;
        Stretch(bigScore.rectTransform, new Vector2(0f, 0.26f), new Vector2(1f, 0.62f), Vector2.zero, Vector2.zero);
        var flLabel = TextObject("FL", centerPanel, "FINAL LIFE", 9, new Color32(124, 146, 162, 255), TextAnchor.UpperCenter, monoFont);
        Stretch(flLabel.rectTransform, Vector2.zero, new Vector2(1f, 0.26f), new Vector2(0f, 12f), Vector2.zero);

        // ── Meta chips + tab switch row ──────────────────────────────────────
        const float chipsTop = 44f + 12f + 158f + 12f;
        var chipRow = PanelObject("MD Chips", stage, new Color(0, 0, 0, 0));
        Stretch(chipRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -chipsTop - 48f), new Vector2(0f, -chipsTop));

        var chipData = new (string label, string value)[]
        {
            ("RESULT",     win ? "Victory" : "Defeat"),
            ("TURNS",      m.turnCount.ToString()),
            ("FIRST MOVE", m.youFirst ? "You" : "@" + m.oppName),
            ("DURATION",   FormatDuration(m.durationSeconds)),
            ("PLAYED",     FormatAgo(m.savedAtIso)),
        };
        float cx = 0f;
        foreach (var (label, value) in chipData)
        {
            float w = Mathf.Max(96f, 34f + Mathf.Max(label.Length * 7f, value.Length * 8f));
            var chip = PanelObject(label + " Chip", chipRow, new Color32(14, 28, 43, 255));
            chip.anchorMin = new Vector2(0f, 0f); chip.anchorMax = new Vector2(0f, 1f);
            chip.pivot = new Vector2(0f, 0.5f);
            chip.sizeDelta = new Vector2(w, 0f);
            chip.anchoredPosition = new Vector2(cx, 0f);
            Round(chip);
            AddRoundedCardBorder(chip, MenuB, 1f);
            var lt = TextObject("l", chip, label, 9, new Color32(124, 146, 162, 255), TextAnchor.UpperLeft, monoFont);
            Stretch(lt.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(12f, 0f), new Vector2(-8f, -7f));
            var vt = TextObject("v", chip, value, 14, Ink, TextAnchor.LowerLeft);
            vt.fontStyle = FontStyle.Bold;
            Stretch(vt.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(12f, 7f), new Vector2(-8f, 0f));
            cx += w + 8f;
        }

        // Segmented tabs (right): ACTION LOG / DECKLISTS.
        var tabs = PanelObject("Tabs", chipRow, new Color32(14, 28, 43, 255));
        tabs.anchorMin = new Vector2(1f, 0f); tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(1f, 0.5f);
        tabs.sizeDelta = new Vector2(280f, 0f);
        tabs.anchoredPosition = Vector2.zero;
        Round(tabs);
        AddRoundedCardBorder(tabs, MenuB, 1f);
        BuildDetailTab(tabs, "▤ ACTION LOG", "log", 0);
        BuildDetailTab(tabs, "▦ DECKLISTS", "decks", 1);

        // ── Tab content (vertical scroll fills the rest of the stage) ────────
        var contentArea = PanelObject("MD Content", stage, new Color(0, 0, 0, 0));
        Stretch(contentArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -chipsTop - 48f - 12f));

        if (matchDetailTab == "decks") BuildDecklistsTab(contentArea, m);
        else BuildActionLogTab(contentArea, m);
    }

    private void BuildDetailTab(RectTransform group, string label, string value, int index)
    {
        bool active = matchDetailTab == value;
        var tab = PanelObject(value + " Tab", group, active ? Accent : new Color(0, 0, 0, 0));
        tab.anchorMin = new Vector2(index / 2f, 0f);
        tab.anchorMax = new Vector2((index + 1) / 2f, 1f);
        tab.offsetMin = new Vector2(4f, 4f);
        tab.offsetMax = new Vector2(-4f, -4f);
        Round(tab);
        var t = TextObject("t", tab, label, 11, active ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = tab.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { matchDetailTab = value; RenderMenu(); });
    }

    // ── ACTION LOG tab: per-turn headers + chat-style you/opp bubbles ─────────

    private static readonly Color TagPurple = new Color32(160, 110, 210, 255);
    private static readonly Color TagBlue   = new Color32( 70, 140, 220, 255);

    private (string label, Color color) TagStyle(string k)
    {
        switch (k)
        {
            case "don":     return ("DON",   Gold);
            case "play":    return ("PLAY",  Accent);
            case "event":   return ("EVENT", TagPurple);
            case "attack":  return ("ATK",   Ink);
            case "block":   return ("BLOCK", TagBlue);
            case "counter": return ("CNTR",  Gold);
            case "life":    return ("DMG",   RedAccent);
            case "ko":      return ("KO",    RedAccent);
            default:        return ("DRAW",  Muted);
        }
    }

    private void BuildActionLogTab(RectTransform area, MatchSummary m)
    {
        if (m.turns == null || m.turns.Count == 0)
        {
            var empty = TextObject("Empty", area,
                "No action log for this match — it was played before cloud match history\n(or as a guest). Watch the replay to relive it.",
                13, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, -60f), Vector2.zero);
            return;
        }

        const float turnHeaderH = 34f, actH = 34f, turnGap = 8f;
        float total = 0f;
        foreach (var t in m.turns) total += turnHeaderH + (t.acts?.Count ?? 0) * actH + turnGap;

        var content = MakeMenuScroll(area, total);
        float y = 0f;
        foreach (var t in m.turns)
        {
            bool yours = t.active == "you";
            Color turnColor = yours ? Accent : RedAccent;

            // "TURN {n}" badge + owner label + fading hairline.
            var head = PanelObject("Turn Head " + t.turn, content, new Color(0, 0, 0, 0));
            head.anchorMin = new Vector2(0f, 1f); head.anchorMax = new Vector2(1f, 1f);
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(0f, turnHeaderH);
            head.anchoredPosition = new Vector2(0f, -y);
            head.GetComponent<Image>().raycastTarget = false;

            var badge = PanelObject("Badge", head, yours ? Accent : new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.9f));
            badge.anchorMin = new Vector2(0f, 0.5f); badge.anchorMax = new Vector2(0f, 0.5f);
            badge.pivot = new Vector2(0f, 0.5f);
            badge.sizeDelta = new Vector2(74f, 22f);
            badge.anchoredPosition = new Vector2(2f, 0f);
            Round(badge);
            var bt = TextObject("t", badge, "TURN " + t.turn, 11, BadgeInk, TextAnchor.MiddleCenter, monoFont);
            bt.fontStyle = FontStyle.Bold;
            Stretch(bt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var owner = TextObject("Owner", head, yours ? "Your turn" : $"@{m.oppName}'s turn", 11, Muted, TextAnchor.MiddleLeft, monoFont);
            Stretch(owner.rectTransform, Vector2.zero, Vector2.one, new Vector2(88f, 0f), new Vector2(-260f, 0f));

            var hair = PanelObject("Hairline", head, new Color(turnColor.r, turnColor.g, turnColor.b, 0.25f));
            Stretch(hair, new Vector2(0.2f, 0.5f), new Vector2(1f, 0.5f), new Vector2(140f, 0f), new Vector2(-8f, 1f));
            hair.GetComponent<Image>().raycastTarget = false;
            y += turnHeaderH;

            if (t.acts == null) { y += turnGap; continue; }
            foreach (var a in t.acts)
            {
                bool mine = a.s == "you";
                var (tagLabel, tagColor) = TagStyle(a.k);

                // Bubble: left-aligned + cyan tint for you, right-aligned + red
                // tint for the opponent, inner-edge accent bar, max ~62% width.
                var line = PanelObject("Act", content, new Color(0, 0, 0, 0));
                line.anchorMin = new Vector2(0f, 1f); line.anchorMax = new Vector2(1f, 1f);
                line.pivot = new Vector2(0.5f, 1f);
                line.sizeDelta = new Vector2(0f, actH);
                line.anchoredPosition = new Vector2(0f, -y);
                line.GetComponent<Image>().raycastTarget = false;

                var bubble = PanelObject("Bubble", line,
                    mine ? new Color(Accent.r, Accent.g, Accent.b, 0.07f)
                         : new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.06f));
                Stretch(bubble, mine ? Vector2.zero : new Vector2(0.38f, 0f),
                    mine ? new Vector2(0.62f, 1f) : Vector2.one,
                    new Vector2(mine ? 2f : 0f, 3f), new Vector2(mine ? 0f : -10f, -3f));
                Round(bubble);

                var accentEdge = PanelObject("Edge", bubble, mine ? Accent : RedAccent);
                Stretch(accentEdge, mine ? Vector2.zero : new Vector2(1f, 0f),
                    mine ? new Vector2(0f, 1f) : Vector2.one,
                    mine ? new Vector2(0f, 2f) : new Vector2(-3f, 2f),
                    mine ? new Vector2(3f, -2f) : new Vector2(0f, -2f));
                accentEdge.GetComponent<Image>().raycastTarget = false;

                var tag = PanelObject("Tag", bubble, new Color(0, 0, 0, 0));
                tag.anchorMin = new Vector2(0f, 0.5f); tag.anchorMax = new Vector2(0f, 0.5f);
                tag.pivot = new Vector2(0f, 0.5f);
                tag.sizeDelta = new Vector2(48f, 16f);
                tag.anchoredPosition = new Vector2(10f, 0f);
                AddRoundedCardBorder(tag, new Color(tagColor.r, tagColor.g, tagColor.b, 0.55f), 1f);
                var tagT = TextObject("t", tag, tagLabel, 9, tagColor, TextAnchor.MiddleCenter, monoFont);
                tagT.fontStyle = FontStyle.Bold;
                Stretch(tagT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                var actT = TextObject("Text", bubble, a.t, 13, new Color32(219, 229, 236, 255), TextAnchor.MiddleLeft);
                Stretch(actT.rectTransform, Vector2.zero, Vector2.one, new Vector2(66f, 0f), new Vector2(-10f, 0f));
                y += actH;
            }
            y += turnGap;
        }
    }

    // ── DECKLISTS tab: two columns, leader-banner headers, card rows ──────────

    private void BuildDecklistsTab(RectTransform area, MatchSummary m)
    {
        BuildDeckColumn(area, m, true);
        BuildDeckColumn(area, m, false);
    }

    private void BuildDeckColumn(RectTransform area, MatchSummary m, bool youSide)
    {
        var deck = youSide ? m.youDeck : m.oppDeck;
        string leaderId = youSide ? m.youLeaderId : m.oppLeaderId;
        var leadRec = MenuCard(leaderId);
        Color leaderColor = MenuLeaderColor(leaderId);

        var col = PanelObject(youSide ? "You Deck" : "Opp Deck", area, new Color32(14, 28, 43, 255));
        Stretch(col, youSide ? Vector2.zero : new Vector2(0.5f, 0f),
            youSide ? new Vector2(0.5f, 1f) : Vector2.one,
            new Vector2(youSide ? 0f : 8f, 0f), new Vector2(youSide ? -8f : 0f, 0f));
        RoundBig(col);
        AddRoundedCardBorder(col, MenuB, 1f);

        // Header: 96px eye-cropped leader banner + side label + LEADER chip.
        float colW = MatchStageW / 2f - 8f;
        var bannerHolder = PanelObject("Banner", col, RowBg);
        Stretch(bannerHolder, new Vector2(0f, 1f), Vector2.one, new Vector2(6f, -102f), new Vector2(-6f, -6f));
        BuildLeaderBanner(bannerHolder, leaderId, colW - 12f, 96f, true);

        var sideT = TextObject("Side", bannerHolder, youSide ? "YOU" : "@" + m.oppName, 10, Muted, TextAnchor.UpperLeft, monoFont);
        sideT.fontStyle = FontStyle.Bold;
        Stretch(sideT.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -30f), new Vector2(-16f, -12f));

        var nameT = TextObject("Leader", bannerHolder,
            leadRec != null ? leadRec.name : (string.IsNullOrEmpty(leaderId) ? "Unknown Leader" : leaderId),
            21, Ink, TextAnchor.LowerLeft);
        nameT.fontStyle = FontStyle.Bold;
        Stretch(nameT.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 10f), new Vector2(-120f, -34f));

        var leaderChip = PanelObject("Leader Chip", bannerHolder, new Color(leaderColor.r, leaderColor.g, leaderColor.b, 0.16f));
        leaderChip.anchorMin = new Vector2(1f, 0f); leaderChip.anchorMax = new Vector2(1f, 0f);
        leaderChip.pivot = new Vector2(1f, 0f);
        leaderChip.sizeDelta = new Vector2(74f, 20f);
        leaderChip.anchoredPosition = new Vector2(-12f, 10f);
        Round(leaderChip);
        AddRoundedCardBorder(leaderChip, new Color(leaderColor.r, leaderColor.g, leaderColor.b, 0.6f), 1f);
        var lcT = TextObject("t", leaderChip, "LEADER", 9, leaderColor, TextAnchor.MiddleCenter, monoFont);
        lcT.fontStyle = FontStyle.Bold;
        Stretch(lcT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Sub-header: MAIN DECK · counts.
        int totalCards = 0;
        if (deck != null) foreach (var e in deck) totalCards += e?.count ?? 0;
        var subHead = PanelObject("SubHead", col, new Color(0, 0, 0, 0));
        Stretch(subHead, new Vector2(0f, 1f), Vector2.one, new Vector2(14f, -132f), new Vector2(-14f, -104f));
        subHead.GetComponent<Image>().raycastTarget = false;
        var mdT = TextObject("md", subHead, "MAIN DECK", 11, Muted, TextAnchor.MiddleLeft, monoFont);
        mdT.fontStyle = FontStyle.Bold;
        Stretch(mdT.rectTransform, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        var cntT = TextObject("cnt", subHead,
            $"{totalCards} CARDS · {(deck?.Count ?? 0)} UNIQUE", 10, new Color32(111, 134, 150, 255), TextAnchor.MiddleRight, monoFont);
        Stretch(cntT.rectTransform, new Vector2(0.5f, 0f), Vector2.one, Vector2.zero, Vector2.zero);

        // Card rows in their own scroll (each column scrolls independently).
        var rowsArea = PanelObject("Rows", col, new Color(0, 0, 0, 0));
        Stretch(rowsArea, Vector2.zero, Vector2.one, new Vector2(6f, 8f), new Vector2(-6f, -136f));

        if (deck == null || deck.Count == 0)
        {
            var empty = TextObject("Empty", rowsArea, "No decklist recorded for this match.",
                12, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -30f), Vector2.zero);
            return;
        }

        // Sort by cost then name for a readable curve, resolving from the library.
        var sorted = new List<MatchDeckCount>(deck);
        sorted.Sort((a, b) =>
        {
            var ra = MenuCard(a.cardId); var rb = MenuCard(b.cardId);
            int ca = ra?.cost ?? 0, cb = rb?.cost ?? 0;
            if (ca != cb) return ca.CompareTo(cb);
            return string.CompareOrdinal(ra?.name ?? a.cardId, rb?.name ?? b.cardId);
        });

        const float rowH = 30f;
        var content = MakeMenuScroll(rowsArea, sorted.Count * rowH);
        for (int i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            var rec = MenuCard(e.cardId);
            var rowRt = PanelObject("Card " + i, content, i % 2 == 1 ? new Color(1f, 1f, 1f, 0.02f) : new Color(0, 0, 0, 0));
            rowRt.anchorMin = new Vector2(0f, 1f); rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, rowH);
            rowRt.anchoredPosition = new Vector2(0f, -(i * rowH));

            var count = TextObject("Count", rowRt, e.count + "×", 12, Ink, TextAnchor.MiddleLeft, monoFont);
            count.fontStyle = FontStyle.Bold;
            Stretch(count.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 0f), new Vector2(42f, 0f));

            var cost = PanelObject("Cost", rowRt, Accent);
            cost.anchorMin = new Vector2(0f, 0.5f); cost.anchorMax = new Vector2(0f, 0.5f);
            cost.pivot = new Vector2(0f, 0.5f);
            cost.sizeDelta = new Vector2(20f, 20f);
            cost.anchoredPosition = new Vector2(44f, 0f);
            Round(cost);
            var costT = TextObject("t", cost, (rec?.cost ?? 0).ToString(), 11, BadgeInk, TextAnchor.MiddleCenter, monoFont);
            costT.fontStyle = FontStyle.Bold;
            Stretch(costT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var dot = PanelObject("Dot", rowRt, MenuLeaderColor(e.cardId));
            dot.anchorMin = new Vector2(0f, 0.5f); dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            dot.anchoredPosition = new Vector2(74f, 0f);
            dot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Round(dot);

            var name = TextObject("Name", rowRt, rec?.name ?? e.cardId, 13, Ink, TextAnchor.MiddleLeft);
            Stretch(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(92f, 0f), new Vector2(-116f, 0f));

            string type = (rec?.type ?? "").ToUpperInvariant();
            string typeLabel = type.StartsWith("CHAR") ? "CHAR" : type.StartsWith("EVEN") ? "EVEN" : type.StartsWith("STAG") ? "STAG" : type;
            var typeT = TextObject("Type", rowRt, typeLabel, 9, new Color32(111, 134, 150, 255), TextAnchor.MiddleRight, monoFont);
            Stretch(typeT.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-112f, 0f), new Vector2(-62f, 0f));

            string counter = rec != null && rec.counter > 0 ? "+" + rec.counter : "—";
            var counterT = TextObject("Counter", rowRt, counter, 10, rec != null && rec.counter > 0 ? Gold : Muted, TextAnchor.MiddleRight, monoFont);
            Stretch(counterT.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-58f, 0f), new Vector2(-10f, 0f));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Match History — shared pieces (banners, scroll, card data, formatting)
    // ══════════════════════════════════════════════════════════════════════════

    // Eye-cropped leader banner: the full card thumb on an Image inside a
    // RectMask2D holder, scaled to cover and offset so the face-data eye point
    // sits in view (same math as DeckBuilderManager's hex cells / _faceMap),
    // then a darkening horizontal gradient toward the text side.
    private void BuildLeaderBanner(RectTransform holder, string leaderId, float approxW, float approxH, bool darkLeft)
    {
        holder.gameObject.AddComponent<RectMask2D>();
        Round(holder);
        var holderImg = holder.GetComponent<Image>();
        holderImg.raycastTarget = false;

        var sprite = LoadThumbSprite(leaderId);
        if (sprite != null)
        {
            const float BLEED = 1.04f;
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 0.716f;
            // Cover: scale so both dimensions overfill the banner.
            float artW = approxW * BLEED;
            float artH = artW / aspect;
            if (artH < approxH * BLEED) { artH = approxH * BLEED; artW = artH * aspect; }

            float fy = 0.22f, fx = 0.5f;
            if (menuFaceY != null && menuFaceY.TryGetValue(leaderId, out var my)) fy = Mathf.Clamp(my, 0.08f, 0.5f);
            if (menuFaceX != null && menuFaceX.TryGetValue(leaderId, out var mx)) fx = Mathf.Clamp(mx, 0.1f, 0.9f);

            // Offset so the eye point heads for the banner centre, clamped so the
            // art never slides off an edge (fy is measured from the card's top).
            float maxX = Mathf.Max(0f, (artW - approxW) / 2f);
            float maxY = Mathf.Max(0f, (artH - approxH) / 2f);
            float offX = Mathf.Clamp(-(fx - 0.5f) * artW, -maxX, maxX);
            float offY = Mathf.Clamp((fy - 0.5f) * artH, -maxY, maxY);

            var art = PanelObject("Art", holder, Color.white);
            var ai = art.GetComponent<Image>();
            ai.sprite = sprite; ai.type = Image.Type.Simple; ai.preserveAspect = false;
            ai.raycastTarget = false;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(artW, artH);
            art.anchoredPosition = new Vector2(offX, offY);
        }

        // Left-dark (or right-dark, mirrored via scale) readability gradient.
        var grad = PanelObject("Gradient", holder, new Color32(9, 17, 26, 232));
        var gi = grad.GetComponent<Image>();
        gi.sprite = GetHGradientSprite();
        gi.type = Image.Type.Simple;
        gi.raycastTarget = false;
        Stretch(grad, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (!darkLeft) grad.localScale = new Vector3(-1f, 1f, 1f);
    }

    // Minimal vertical scroll (viewport + RectMask2D + fixed-height content) —
    // the menu variant of DeckBuilderManager.MakeScroll, without the scrollbar.
    private RectTransform MakeMenuScroll(RectTransform area, float contentHeight)
    {
        var viewport = PanelObject("Viewport", area, new Color(0f, 0f, 0f, 0.001f));
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.sizeDelta = new Vector2(0f, Mathf.Max(contentHeight, 0f));

        var sr = area.gameObject.AddComponent<ScrollRect>();
        sr.viewport = viewport;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 26f;
        return content;
    }

    // ── Card library / face data / thumbs (same files DeckBuilder reads) ──────

    private void EnsureMenuCardLibrary()
    {
        if (menuCardsById != null) return;
        menuCardsById = new Dictionary<string, CardRec>();
        if (CardAssets.UseCdn) { FillMenuCardLibraryAsync(); return; }
        try
        {
            string path = CardAssets.LocalPath("official-card-library.json");
            if (!File.Exists(path)) return;
            ParseMenuCardLibrary(File.ReadAllText(path));
        }
        catch (Exception e) { Debug.LogWarning("Menu card library parse failed: " + e.Message); }
    }

    private void ParseMenuCardLibrary(string json)
    {
        var wrap = JsonUtility.FromJson<CardLibFile>("{\"cards\":" + json + "}");
        menuCardLibrary = wrap?.cards ?? new CardRec[0];
        foreach (var c in menuCardLibrary)
            if (c != null && !string.IsNullOrEmpty(c.id) && !menuCardsById.ContainsKey(c.id))
                menuCardsById[c.id] = c;
    }

    private async void FillMenuCardLibraryAsync()
    {
        try
        {
            while (!CardAssets.Ready) await Task.Yield();
            var json = await CardAssets.ReadTextAsync("official-card-library.json");
            if (string.IsNullOrEmpty(json)) return;
            ParseMenuCardLibrary(json);
            _menuArtRefreshQueued = true; // colors/life labels can now resolve
        }
        catch (Exception e) { Debug.LogWarning("Menu card library parse failed: " + e.Message); }
    }

    private void EnsureMenuFaceData()
    {
        if (menuFaceY != null) return;
        menuFaceY = new Dictionary<string, float>();
        menuFaceX = new Dictionary<string, float>();
        if (CardAssets.UseCdn) { FillMenuFaceDataAsync(); return; }
        try
        {
            string p = CardAssets.LocalPath("face-data.json");
            if (!File.Exists(p)) return;
            ParseMenuFaceData(File.ReadAllText(p));
        }
        catch (Exception e) { Debug.LogWarning("Menu face-data load failed: " + e.Message); }
    }

    private void ParseMenuFaceData(string json)
    {
        var f = JsonUtility.FromJson<FaceMapFile>(json);
        if (f?.ids == null || f.y == null) return;
        for (int i = 0; i < f.ids.Length && i < f.y.Length; i++) menuFaceY[f.ids[i]] = f.y[i];
        if (f.x != null && f.x.Length == f.ids.Length)
            for (int i = 0; i < f.ids.Length; i++) menuFaceX[f.ids[i]] = f.x[i];
    }

    private async void FillMenuFaceDataAsync()
    {
        try
        {
            while (!CardAssets.Ready) await Task.Yield();
            var json = await CardAssets.ReadTextAsync("face-data.json");
            if (!string.IsNullOrEmpty(json)) ParseMenuFaceData(json);
        }
        catch (Exception e) { Debug.LogWarning("Menu face-data load failed: " + e.Message); }
    }

    private CardRec MenuCard(string id)
        => id != null && menuCardsById != null && menuCardsById.TryGetValue(id, out var c) ? c : null;

    // First colour of the card's "Red/Green"-style colour string → swatch.
    private Color MenuLeaderColor(string cardId)
    {
        var rec = MenuCard(cardId);
        var colors = rec?.Colors();
        if (colors != null && colors.Length > 0 && MenuColorSwatch.TryGetValue(colors[0], out var c)) return c;
        return Muted;
    }

    private int MenuLeaderBaseLife(string leaderId)
    {
        var rec = MenuCard(leaderId);
        return rec != null && rec.life > 0 ? rec.life : 5;
    }

    // Leader card thumbnail — the same Thumbs path scheme DeckBuilder uses,
    // falling back to the full card art LoadArt already resolves.
    private Sprite LoadThumbSprite(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_thumbArtCache.TryGetValue(id, out var cached)) return cached;

        if (!CardAssets.UseCdn)
        {
            // Desktop/Editor: synchronous, same behavior as always.
            Sprite sprite = null;
            string rel = CardAssets.ThumbCandidate(id);
            if (rel != null && CardAssets.Exists(rel))
            {
                try { sprite = SpriteFromBytes(File.ReadAllBytes(CardAssets.LocalPath(rel))); }
                catch { /* fall through to full art */ }
            }
            if (sprite == null) sprite = LoadArt(id);
            _thumbArtCache[id] = sprite;
            return sprite;
        }

        // WebGL/CDN: async fetch (thumb first, full art fallback), re-render on arrival.
        KickMenuArtLoad(id, thumbFirst: true);
        return null;
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    private string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "—";
        return $"{seconds / 60}m{seconds % 60:00}s";
    }

    private string FormatAgo(string iso)
    {
        if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return "";
        var span = DateTime.UtcNow - dt.ToUniversalTime();
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
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
    // Account gate (required-to-play) + Account & Recovery settings (optional).
    // Identity/uniqueness/profanity logic lives in AccountManager.cs + Cloud Code;
    // this is just the uGUI wiring, following the same panel/busy/error idiom as
    // the lobby hub below.
    // ══════════════════════════════════════════════════════════════════════════

    private void OpenAccountSettings()
    {
        showingProfile = false;
        showingAccountSettings = true;
        accountSettingsResetMode = false;
        accountSettingsRecoveryMode = false;
        accountError = null;
        signOutArmed = false;
        RenderMenu();
    }

    private void CloseAccountSettings()
    {
        showingAccountSettings = false;
        accountError = null;
        signOutArmed = false;
        RenderMenu();
    }

    // Honors the "Stay signed in" preference: when off, drop the cached session token on
    // quit so the next launch starts at the sign-in screen instead of silently resuming.
    private void OnApplicationQuit()
    {
        if (!AccountManager.StaySignedIn) AccountManager.SignOut();
    }

    private void BuildAccountGateModal(RectTransform root)
    {
        // Full-screen dim layer whose Image IS a raycast target, so everything
        // behind it (nav rail, top bar, gear) is unclickable while the gate is up.
        var blocker = PanelObject("Account Modal Blocker", root, new Color(0f, 0f, 0f, 0.62f));
        Stretch(blocker, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        blocker.GetComponent<Image>().raycastTarget = true;

        var window = PanelObject("Account Modal Window", blocker, new Color32(10, 19, 28, 250));
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot     = new Vector2(0.5f, 0.5f);
        window.sizeDelta = new Vector2(560f,
            accountGatePostClaimMode ? 470f : accountGateSignInMode ? 460f : 630f);
        window.anchoredPosition = Vector2.zero;
        RoundBig(window);
        AddRoundedCardBorder(window, MenuB, 1f);

        string title = accountGatePostClaimMode ? "Secure Your Account"
            : accountGateSignInMode ? "Welcome Back!" : "Welcome to One Piece TCG Simulator!";
        var titleText = TextObject("Modal Title", window, title, 21, Ink, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -60f), new Vector2(-24f, -14f));

        string subtitle = accountGatePostClaimMode
            ? "One more step - add your login details."
            : accountGateSignInMode ? "Sign in with your username or email."
            : "Set up your username to start playing.";
        var sub = TextObject("Modal Sub", window, subtitle, 12, Muted, TextAnchor.MiddleCenter, monoFont);
        Stretch(sub.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -90f), new Vector2(-24f, -62f));

        // Content sits above a reserved bottom strip inside the window where the
        // guest option lives, separated by a hairline so it reads as "or, just
        // look around" rather than a third equal choice.
        var content = PanelObject("Modal Content", window, new Color(0, 0, 0, 0));
        Stretch(content, Vector2.zero, Vector2.one,
            new Vector2(0f, accountGatePostClaimMode ? 16f : 60f), new Vector2(0f, -96f));

        if (accountGatePostClaimMode) BuildPostClaimEmailFields(content);
        else if (accountGateSignInMode) BuildAccountSignInFields(content);
        else BuildAccountClaimFields(content);

        // Not offered on the retry step (the name is already claimed there -
        // finishing login setup is the only path).
        if (!accountGatePostClaimMode)
        {
            var guestDivider = PanelObject("Guest Divider", window, new Color32(255, 255, 255, 20));
            Stretch(guestDivider, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 59f), new Vector2(-24f, 60f));

            var guestHolder = PanelObject("Guest Holder", window, new Color(0, 0, 0, 0));
            guestHolder.anchorMin = new Vector2(0.5f, 0f);
            guestHolder.anchorMax = new Vector2(0.5f, 0f);
            guestHolder.pivot     = new Vector2(0.5f, 0f);
            guestHolder.sizeDelta = new Vector2(190f, 34f);
            guestHolder.anchoredPosition = new Vector2(0f, 14f);
            AddButton(guestHolder, "Continue as guest", ContinueAsGuestClicked, true, false, true);
        }
    }

    private void BuildPostClaimEmailFields(RectTransform panel)
    {
        // Reached only when the name claim succeeded but the email link failed -
        // the retry screen for finishing login setup without losing the name.
        var header = TextObject("Header", panel, "FINISH LOGIN SETUP", 12, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -26f), new Vector2(-24f, -6f));

        var hint = TextObject("Hint", panel,
            $"Your name \"{AccountManager.CurrentUsername}\" is claimed, but the email link didn't go through. Fix the details below and retry, or skip and add it later in Settings.",
            11, Muted, TextAnchor.UpperLeft, monoFont);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(hint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -92f), new Vector2(-24f, -30f));

        var emailField = MakeInput(panel, "Email", accountEmailInput, s => accountEmailInput = s, null);
        Stretch(emailField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -132f), new Vector2(-24f, -98f));

        var passwordField = MakeInput(panel, "Password", accountPasswordInput, s => accountPasswordInput = s, null,
            InputField.ContentType.Password);
        Stretch(passwordField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -172f), new Vector2(-24f, -138f));

        var pwHint = TextObject("Password Hint", panel,
            "8-30 characters with an uppercase letter, lowercase letter, number, and symbol.",
            10, Muted, TextAnchor.UpperLeft, monoFont);
        pwHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(pwHint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -210f), new Vector2(-24f, -176f));

        AddCenteredButton(panel, accountBusy ? "Working..." : "Link Email", LinkEmailClicked,
            -222f, 180f, 38f, !accountBusy);
        AddCenteredButton(panel, "Skip for now",
            () => { accountGatePostClaimMode = false; showingAccountGate = false; accountError = null; RenderMenu(); },
            -268f, 160f, 34f, !accountBusy);

        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("Error", panel, accountError, 11, RedAccent, TextAnchor.UpperCenter, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -350f), new Vector2(-24f, -310f));
        }
    }

    private void BuildAccountClaimFields(RectTransform panel)
    {
        var header = TextObject("Header", panel, "PICK A NAME", 12, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -26f), new Vector2(-24f, -6f));

        var hint = TextObject("Hint", panel, "3-16 characters, letters/numbers/underscore. This can't be changed later.",
            11, Muted, TextAnchor.UpperLeft, monoFont);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(hint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -52f), new Vector2(-24f, -30f));

        var nameField = MakeInput(panel, "e.g. StrawHat99", usernameInput,
            s => { usernameInput = s.Length > 16 ? s.Substring(0, 16) : s; }, null);
        Stretch(nameField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -92f), new Vector2(-24f, -58f));

        var divider = PanelObject("Divider", panel, new Color32(255, 255, 255, 20));
        Stretch(divider, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -113f), new Vector2(-24f, -112f));

        var acctHeader = TextObject("Account Header", panel, "ACCOUNT LOGIN", 12, Muted, TextAnchor.UpperLeft, monoFont);
        acctHeader.fontStyle = FontStyle.Bold;
        Stretch(acctHeader.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -140f), new Vector2(-24f, -120f));

        var acctHint = TextObject("Account Hint", panel,
            "Sign in later with your name or this email, on any device.",
            11, Muted, TextAnchor.UpperLeft, monoFont);
        acctHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(acctHint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -166f), new Vector2(-24f, -144f));

        var emailField = MakeInput(panel, "Email", accountEmailInput, s => accountEmailInput = s, null);
        Stretch(emailField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -206f), new Vector2(-24f, -172f));

        var passwordField = MakeInput(panel, "Password", accountPasswordInput, s => accountPasswordInput = s, null,
            regPasswordVisible ? InputField.ContentType.Standard : InputField.ContentType.Password);
        Stretch(passwordField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -246f), new Vector2(-82f, -212f));

        var pwEyeHolder = PanelObject("PW Eye Holder", panel, new Color(0, 0, 0, 0));
        Stretch(pwEyeHolder, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-78f, -246f), new Vector2(-24f, -212f));
        AddButton(pwEyeHolder, regPasswordVisible ? "HIDE" : "SHOW",
            () => { regPasswordVisible = !regPasswordVisible; RenderMenu(); }, true, false, true);

        var confirmField = MakeInput(panel, "Confirm password", accountPasswordConfirmInput,
            s => accountPasswordConfirmInput = s, null,
            regConfirmVisible ? InputField.ContentType.Standard : InputField.ContentType.Password);
        Stretch(confirmField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -286f), new Vector2(-82f, -252f));

        var cfEyeHolder = PanelObject("CF Eye Holder", panel, new Color(0, 0, 0, 0));
        Stretch(cfEyeHolder, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-78f, -286f), new Vector2(-24f, -252f));
        AddButton(cfEyeHolder, regConfirmVisible ? "HIDE" : "SHOW",
            () => { regConfirmVisible = !regConfirmVisible; RenderMenu(); }, true, false, true);

        var pwHint = TextObject("Password Hint", panel,
            "8-30 characters with an uppercase letter, lowercase letter, number, and symbol.",
            10, Muted, TextAnchor.UpperLeft, monoFont);
        pwHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(pwHint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -324f), new Vector2(-24f, -290f));

        AddCenteredButton(panel, accountBusy ? "Working..." : "Create Account", ClaimUsernameClicked,
            -336f, 200f, 38f, !accountBusy);
        AddCenteredButton(panel, "Already have an account?\nSign in",
            () => { accountGateSignInMode = true; accountError = null; RenderMenu(); },
            -382f, 230f, 42f);

        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("Error", panel, accountError, 11, RedAccent, TextAnchor.UpperCenter, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -466f), new Vector2(-24f, -428f));
        }
    }

    // Centered fixed-size button anchored below the fields (top-anchored so the
    // stack hugs the content instead of floating at the window's bottom edge).
    private void AddCenteredButton(RectTransform panel, string label, UnityEngine.Events.UnityAction action,
        float top, float width, float height, bool enabled = true)
    {
        var holder = PanelObject(label + " Holder", panel, new Color(0, 0, 0, 0));
        holder.anchorMin = new Vector2(0.5f, 1f);
        holder.anchorMax = new Vector2(0.5f, 1f);
        holder.pivot     = new Vector2(0.5f, 1f);
        holder.sizeDelta = new Vector2(width, height);
        holder.anchoredPosition = new Vector2(0f, top);
        AddButton(holder, label, action, enabled, false, true);
    }

    private void BuildAccountSignInFields(RectTransform panel)
    {
        var header = TextObject("Header", panel, "SIGN IN", 12, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -26f), new Vector2(-24f, -6f));

        var idField = MakeInput(panel, "Username or email", accountEmailInput, s => accountEmailInput = s, null);
        Stretch(idField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -72f), new Vector2(-24f, -38f));

        var passwordField = MakeInput(panel, "Password", accountPasswordInput, s => accountPasswordInput = s, null,
            InputField.ContentType.Password);
        Stretch(passwordField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -112f), new Vector2(-24f, -78f));

        // Sign In and the stay-signed-in checkbox share one centered row.
        var signRow = PanelObject("Sign In Row", panel, new Color(0, 0, 0, 0));
        signRow.anchorMin = new Vector2(0.5f, 1f);
        signRow.anchorMax = new Vector2(0.5f, 1f);
        signRow.pivot     = new Vector2(0.5f, 1f);
        signRow.sizeDelta = new Vector2(400f, 38f);
        signRow.anchoredPosition = new Vector2(0f, -124f);

        var signInHolder = PanelObject("Sign In Holder", signRow, new Color(0, 0, 0, 0));
        Stretch(signInHolder, Vector2.zero, new Vector2(0.44f, 1f), Vector2.zero, Vector2.zero);
        AddButton(signInHolder, accountBusy ? "Working..." : "Sign In", SignInWithEmailClicked, !accountBusy, false, true);

        var stayHolder = PanelObject("Stay Holder", signRow, new Color(0, 0, 0, 0));
        Stretch(stayHolder, new Vector2(0.5f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        AddButton(stayHolder, (AccountManager.StaySignedIn ? "[x] " : "[ ] ") + "Stay signed in",
            () => { AccountManager.StaySignedIn = !AccountManager.StaySignedIn; RenderMenu(); }, true, false, true);

        AddCenteredButton(panel, "New here? Create an account",
            () => { accountGateSignInMode = false; accountError = null; RenderMenu(); },
            -174f, 240f, 36f);

        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("Error", panel, accountError, 11, RedAccent, TextAnchor.UpperCenter, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -266f), new Vector2(-24f, -218f));
        }
    }

    private void BuildAccountSettingsStage(RectTransform stage)
    {
        const float titleH = 60f;

        var titleRow = PanelObject("Account Settings Title Row", stage, new Color(0, 0, 0, 0));
        Stretch(titleRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -titleH), Vector2.zero);
        var titleText = TextObject("Title", titleRow, "Account & Recovery", 26, Ink, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(4f, 0f), Vector2.zero);

        var backHolder = PanelObject("Back Holder", titleRow, new Color(0, 0, 0, 0));
        Stretch(backHolder, new Vector2(0.8f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        var backHlg = backHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        backHlg.childAlignment = TextAnchor.MiddleRight;
        backHlg.childControlWidth = false;
        backHlg.childControlHeight = false;
        AddButton(backHolder, "< Back", CloseAccountSettings, true, false);

        // Guest view spreads across the full stage width; the account forms keep
        // the narrower half-width column that suits stacked input fields.
        var panel = PanelObject("Account Settings Panel", stage, new Color32(8, 16, 24, 153));
        // Full-width for the "browse" views (guest info, linked-account summary);
        // half-width column only for the stacked-input forms (link email, reset).
        bool wideSettings = AccountManager.IsGuest
            || (AccountManager.HasEmailLinked && !accountSettingsResetMode && !accountSettingsRecoveryMode);
        Stretch(panel, Vector2.zero, new Vector2(wideSettings ? 1f : 0.5f, 1f), Vector2.zero, new Vector2(0f, -titleH));
        Round(panel);
        AddRoundedCardBorder(panel, MenuB, 1f);

        if (AccountManager.IsGuest) BuildGuestSettingsFields(panel);
        else if (accountSettingsResetMode) BuildPasswordResetFields(panel);
        else if (accountSettingsRecoveryMode) BuildRecoveryEmailFields(panel);
        else if (AccountManager.HasEmailLinked) BuildEmailLinkedSummary(panel);
        else BuildLinkEmailFields(panel);
    }

    private void BuildGuestSettingsFields(RectTransform panel)
    {
        var header = TextObject("Header", panel, "GUEST MODE", 13, Muted, TextAnchor.UpperCenter, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -48f), new Vector2(-24f, -26f));

        var playingAs = TextObject("Playing As Label", panel, "PLAYING AS", 10, Muted, TextAnchor.UpperCenter, monoFont);
        Stretch(playingAs.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -96f), new Vector2(-24f, -78f));

        var nameText = TextObject("Guest Name", panel, AccountManager.GuestDisplayName, 28, Ink, TextAnchor.UpperCenter);
        nameText.fontStyle = FontStyle.Bold;
        Stretch(nameText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -138f), new Vector2(-24f, -98f));

        var tempNote = TextObject("Temp Note", panel,
            "Opponents will see this name with the [Guest] tag. It lasts only for this session - guest progress isn't tied to an account and won't follow you to another device.",
            11, Muted, TextAnchor.UpperCenter, monoFont);
        tempNote.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(tempNote.rectTransform, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -196f), new Vector2(0f, -146f));

        var divider = PanelObject("Divider", panel, new Color32(255, 255, 255, 20));
        Stretch(divider, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -219f), new Vector2(0f, -218f));

        var lockedHeader = TextObject("Locked Header", panel, "LOCKED IN GUEST MODE", 10, Muted, TextAnchor.UpperCenter, monoFont);
        lockedHeader.fontStyle = FontStyle.Bold;
        Stretch(lockedHeader.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -258f), new Vector2(-24f, -240f));

        // Three side-by-side cards - the page is full-width now, so spread out
        // instead of stacking.
        (string title, string desc)[] locked =
        {
            ("Friends", "Add friends, see who's online, send invites"),
            ("Ranked play", "Climb the ladder with a permanent name"),
            ("Account recovery", "Keep your name and progress on any device"),
        };
        const float cardTop = -288f, cardBottom = -392f;
        for (int i = 0; i < locked.Length; i++)
        {
            float xMin = 0.06f + i * 0.30f;
            float xMax = xMin + 0.26f;
            var card = PanelObject($"Locked Card {i}", panel, new Color32(8, 16, 26, 200));
            Stretch(card, new Vector2(xMin, 1f), new Vector2(xMax, 1f), new Vector2(0f, cardBottom), new Vector2(0f, cardTop));
            Round(card);
            AddRoundedCardBorder(card, MenuB, 1f);

            var cardTitle = TextObject($"Locked Title {i}", card, locked[i].title, 13, Ink, TextAnchor.UpperCenter);
            cardTitle.fontStyle = FontStyle.Bold;
            Stretch(cardTitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -38f), new Vector2(-10f, -14f));

            var cardDesc = TextObject($"Locked Desc {i}", card, locked[i].desc, 10, Muted, TextAnchor.UpperCenter, monoFont);
            cardDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(cardDesc.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -42f));
        }

        var unlockNote = TextObject("Unlock Note", panel,
            "Creating an account keeps the deck and match history from this session.",
            11, Muted, TextAnchor.MiddleCenter, monoFont);
        unlockNote.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(unlockNote.rectTransform, new Vector2(0.15f, 0f), new Vector2(0.85f, 0f), new Vector2(0f, 84f), new Vector2(0f, 118f));

        // Fixed-width centered button, wide enough that the label stays on one line.
        var createHolder = PanelObject("Create Holder", panel, new Color(0, 0, 0, 0));
        createHolder.anchorMin = new Vector2(0.5f, 0f);
        createHolder.anchorMax = new Vector2(0.5f, 0f);
        createHolder.pivot     = new Vector2(0.5f, 0f);
        createHolder.sizeDelta = new Vector2(340f, 46f);
        createHolder.anchoredPosition = new Vector2(0f, 24f);
        AddButton(createHolder, "Create Account / Sign In", () =>
        {
            AccountManager.EndGuestSession();
            showingAccountSettings = false;
            showingAccountGate = true;
            accountGateSignInMode = false;
            accountError = null;
            RenderMenu();
        }, true, false, true);
    }

    private void BuildEmailLinkedSummary(RectTransform panel)
    {
        // Mirrors the guest-mode page's full-width, center-aligned layout: identity
        // block up top, a row of status/action cards, sign-out anchored at the bottom.
        var header = TextObject("Header", panel, "ACCOUNT", 13, Muted, TextAnchor.UpperCenter, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -48f), new Vector2(-24f, -26f));

        var signedAs = TextObject("Signed In Label", panel, "SIGNED IN AS", 10, Muted, TextAnchor.UpperCenter, monoFont);
        Stretch(signedAs.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -96f), new Vector2(-24f, -78f));

        var nameText = TextObject("Account Name", panel, AccountManager.CurrentUsername ?? DefaultPlayerName, 28, Ink, TextAnchor.UpperCenter);
        nameText.fontStyle = FontStyle.Bold;
        Stretch(nameText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -138f), new Vector2(-24f, -98f));

        var note = TextObject("Account Note", panel,
            "A recovery email is linked - you can sign in with your name or email on any device, and your match history follows your account.",
            11, Muted, TextAnchor.UpperCenter, monoFont);
        note.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(note.rectTransform, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -196f), new Vector2(0f, -146f));

        var divider = PanelObject("Divider", panel, new Color32(255, 255, 255, 20));
        Stretch(divider, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -219f), new Vector2(0f, -218f));

        // Status/action cards, same geometry as the guest page's locked-feature row.
        const float cardTop = -248f, cardBottom = -372f;
        string emailsDesc =
            $"Main: {AccountManager.PrimaryEmail ?? "on file"}\nRecovery: {AccountManager.RecoveryEmail ?? "not set"}";
        (string title, string desc, string action)[] cards =
        {
            ("Emails", emailsDesc + "\nEither address works for sign-in and reset codes.",
                AccountManager.RecoveryEmail == null ? "Add Recovery Email" : "Change Recovery Email"),
            ("Password", "Change it any time - a reset code is emailed to your addresses.", "Reset Password"),
            ("Session", "Stay signed in between launches, or require a sign-in every time.", "toggle"),
        };
        for (int i = 0; i < cards.Length; i++)
        {
            float xMin = 0.06f + i * 0.30f;
            float xMax = xMin + 0.26f;
            var card = PanelObject($"Account Card {i}", panel, new Color32(8, 16, 26, 200));
            Stretch(card, new Vector2(xMin, 1f), new Vector2(xMax, 1f), new Vector2(0f, cardBottom), new Vector2(0f, cardTop));
            Round(card);
            AddRoundedCardBorder(card, MenuB, 1f);

            var cardTitle = TextObject($"Card Title {i}", card, cards[i].title, 13, Ink, TextAnchor.UpperCenter);
            cardTitle.fontStyle = FontStyle.Bold;
            Stretch(cardTitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -36f), new Vector2(-10f, -12f));

            var cardDesc = TextObject($"Card Desc {i}", card, cards[i].desc, 10, Muted, TextAnchor.UpperCenter, monoFont);
            cardDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(cardDesc.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -84f), new Vector2(-10f, -38f));

            var btnHolder = PanelObject($"Card Btn {i}", card, new Color(0, 0, 0, 0));
            Stretch(btnHolder, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-12f, 44f));
            if (cards[i].action == "toggle")
                AddButton(btnHolder, (AccountManager.StaySignedIn ? "[x] " : "[ ] ") + "Stay signed in",
                    () => { AccountManager.StaySignedIn = !AccountManager.StaySignedIn; RenderMenu(); }, true, false, true);
            else if (cards[i].action == "Reset Password")
                AddButton(btnHolder, "Reset Password",
                    () => { accountSettingsResetMode = true; accountError = null; RenderMenu(); }, !accountBusy, false, true);
            else
                AddButton(btnHolder, cards[i].action,
                    () => { accountSettingsRecoveryMode = true; recoveryEmailInput = ""; accountError = null; RenderMenu(); },
                    !accountBusy, false, true);
        }

        AddSignOutRow(panel);
    }

    private void AddSignOutRow(RectTransform panel)
    {
        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("SignOut Error", panel, accountError, 11, RedAccent, TextAnchor.MiddleCenter, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0.15f, 0f), new Vector2(0.85f, 0f), new Vector2(0f, 76f), new Vector2(0f, 122f));
        }

        var signOutHolder = PanelObject("Sign Out Holder", panel, new Color(0, 0, 0, 0));
        signOutHolder.anchorMin = new Vector2(0.5f, 0f);
        signOutHolder.anchorMax = new Vector2(0.5f, 0f);
        signOutHolder.pivot     = new Vector2(0.5f, 0f);
        signOutHolder.sizeDelta = new Vector2(220f, 40f);
        signOutHolder.anchoredPosition = new Vector2(0f, 24f);
        AddButton(signOutHolder, signOutArmed ? "Sign out anyway" : "Sign Out", SignOutClicked, !accountBusy, false, true);
    }

    private void SignOutClicked()
    {
        // An anonymous account with no recovery email is gone forever once signed out,
        // so require a deliberate second click in that case.
        if (!AccountManager.HasEmailLinked && !signOutArmed)
        {
            signOutArmed = true;
            accountError = "This account has no email & password yet, so there'd be no way to sign back in - the account and its name would be lost for good. Link an email above first, or click 'Sign out anyway' if you're sure.";
            RenderMenu();
            return;
        }

        signOutArmed = false;
        AccountManager.SignOut();
        accountError = null;
        accountEmailInput = "";
        accountPasswordInput = "";
        showingAccountSettings = false;
        showingAccountGate = true;
        accountGateSignInMode = true;
        accountGatePostClaimMode = false;
        RenderMenu();
    }

    private void BuildLinkEmailFields(RectTransform panel)
    {
        var header = TextObject("Header", panel, "LINK RECOVERY EMAIL", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var emailField = MakeInput(panel, "Email", accountEmailInput, s => accountEmailInput = s, null);
        Stretch(emailField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -70f), new Vector2(-16f, -46f));

        var passwordField = MakeInput(panel, "Password", accountPasswordInput, s => accountPasswordInput = s, null,
            settingsPasswordVisible ? InputField.ContentType.Standard : InputField.ContentType.Password);
        Stretch(passwordField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -106f), new Vector2(-70f, -82f));

        var eyeHolder = PanelObject("Eye Holder", panel, new Color(0, 0, 0, 0));
        Stretch(eyeHolder, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-66f, -106f), new Vector2(-16f, -82f));
        AddButton(eyeHolder, settingsPasswordVisible ? "HIDE" : "SHOW",
            () => { settingsPasswordVisible = !settingsPasswordVisible; RenderMenu(); }, true, false, true);

        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("Error", panel, accountError, 11, RedAccent, TextAnchor.UpperLeft, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 54f), new Vector2(-16f, 100f));
        }

        var linkHolder = PanelObject("Link Holder", panel, new Color(0, 0, 0, 0));
        Stretch(linkHolder, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(-16f, 50f));
        AddButton(linkHolder, accountBusy ? "Working..." : "Link Email", LinkEmailClicked, !accountBusy, false, true);

        var signOutHolder = PanelObject("Sign Out Holder", panel, new Color(0, 0, 0, 0));
        Stretch(signOutHolder, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, -14f), new Vector2(-16f, 16f));
        AddButton(signOutHolder, signOutArmed ? "Sign out anyway" : "Sign Out", SignOutClicked, !accountBusy, false);
    }

    private void BuildRecoveryEmailFields(RectTransform panel)
    {
        var header = TextObject("Header", panel, "RECOVERY EMAIL", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var hint = TextObject("Hint", panel,
            "A second address for this account. Reset codes can be sent to it, and it works for email sign-in - useful if you ever lose access to your main inbox.",
            11, Muted, TextAnchor.UpperLeft, monoFont);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(hint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -96f), new Vector2(-16f, -42f));

        var emailField = MakeInput(panel, "recovery@example.com", recoveryEmailInput, s2 => recoveryEmailInput = s2, null);
        Stretch(emailField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -138f), new Vector2(-16f, -104f));

        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("Error", panel, accountError, 11, RedAccent, TextAnchor.UpperLeft, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -196f), new Vector2(-16f, -148f));
        }

        AddCenteredButton(panel, accountBusy ? "Working..." : "Save Recovery Email", SetRecoveryEmailClicked,
            -204f, 220f, 38f, !accountBusy);
        AddCenteredButton(panel, "< Back",
            () => { accountSettingsRecoveryMode = false; accountError = null; RenderMenu(); },
            -250f, 120f, 34f);
    }

    private async void SetRecoveryEmailClicked()
    {
        if (string.IsNullOrWhiteSpace(recoveryEmailInput) || !recoveryEmailInput.Contains("@"))
        {
            accountError = "Enter a valid email address.";
            RenderMenu();
            return;
        }
        accountBusy = true;
        accountError = null;
        RenderMenu();
        try
        {
            var result = await AccountManager.SetRecoveryEmailAsync(recoveryEmailInput.Trim());
            if (this == null || menuRoot == null) return;
            if (result.Ok) accountSettingsRecoveryMode = false;
            else accountError = result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            accountError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                accountBusy = false;
                RenderMenu();
            }
        }
    }

    private void BuildPasswordResetFields(RectTransform panel)
    {
        var header = TextObject("Header", panel, "RESET PASSWORD", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var emailField = MakeInput(panel, "Email", accountEmailInput, s => accountEmailInput = s, null);
        Stretch(emailField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -70f), new Vector2(-16f, -46f));

        var sendHolder = PanelObject("Send Holder", panel, new Color(0, 0, 0, 0));
        Stretch(sendHolder, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -104f), new Vector2(-16f, -74f));
        AddButton(sendHolder, accountBusy ? "Working..." : "Email Me a Code", RequestPasswordResetClicked, !accountBusy, false);

        var tokenField = MakeInput(panel, "Code from email", resetTokenInput, s => resetTokenInput = s, null);
        Stretch(tokenField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -142f), new Vector2(-16f, -112f));

        var newPasswordField = MakeInput(panel, "New password", resetNewPasswordInput, s => resetNewPasswordInput = s, null,
            InputField.ContentType.Password);
        Stretch(newPasswordField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -178f), new Vector2(-16f, -148f));

        if (!string.IsNullOrEmpty(accountError))
        {
            var err = TextObject("Error", panel, accountError, 11, RedAccent, TextAnchor.UpperLeft, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 54f), new Vector2(-16f, 100f));
        }

        var confirmHolder = PanelObject("Confirm Holder", panel, new Color(0, 0, 0, 0));
        Stretch(confirmHolder, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(-16f, 50f));
        AddButton(confirmHolder, accountBusy ? "Working..." : "Set New Password", ConfirmPasswordResetClicked, !accountBusy, false);

        var switchHolder = PanelObject("Switch Holder", panel, new Color(0, 0, 0, 0));
        Stretch(switchHolder, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -14f), new Vector2(-16f, 16f));
        AddButton(switchHolder, "< Back", () => { accountSettingsResetMode = false; accountError = null; RenderMenu(); }, true, false);
    }

    private async void ClaimUsernameClicked()
    {
        if (string.IsNullOrWhiteSpace(usernameInput)) { accountError = "Enter a name first."; RenderMenu(); return; }
        if (usernameInput.Trim().Length < 3)
        {
            // Unity's username/password login requires 3+ chars, and the claimed name
            // doubles as the login username.
            accountError = "Names must be at least 3 characters.";
            RenderMenu();
            return;
        }
        if (string.IsNullOrWhiteSpace(accountEmailInput) || !accountEmailInput.Contains("@"))
        {
            accountError = "Enter a valid email address.";
            RenderMenu();
            return;
        }
        string pwProblem = PasswordProblem(accountPasswordInput);
        if (pwProblem != null)
        {
            accountError = pwProblem;
            RenderMenu();
            return;
        }
        if (accountPasswordInput != accountPasswordConfirmInput)
        {
            accountError = "Passwords don't match - retype them to make sure.";
            RenderMenu();
            return;
        }
        accountBusy = true;
        accountError = null;
        RenderMenu();
        try
        {
            var result = await AccountManager.ClaimUsernameAsync(usernameInput.Trim());
            if (this == null || menuRoot == null) return;
            if (result.Ok)
            {
                // A signed-in account that ALREADY has credentials (e.g. recovered via
                // sign-in but missing a username) just needed the claim - trying to
                // link again would fail with "already linked". Done.
                if (AccountManager.HasEmailLinked)
                {
                    showingAccountGate = false;
                    accountPasswordInput = "";
                    accountError = null;
                    return;
                }
                var linkResult = await AccountManager.LinkEmailPasswordAsync(accountEmailInput.Trim(), accountPasswordInput);
                if (this == null || menuRoot == null) return;
                if (linkResult.Ok)
                {
                    showingAccountGate = false;
                    accountPasswordInput = "";
                    accountPasswordConfirmInput = "";
                    accountError = null;
                }
                else
                {
                    // Name is claimed; only the email link failed. Land on the retry
                    // screen with the reason so they can fix it or skip.
                    accountGatePostClaimMode = true;
                    accountError = linkResult.Message;
                }
            }
            else
            {
                accountError = DescribeClaimFailure(result.Reason);
            }
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            accountError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                accountBusy = false;
                RenderMenu();
            }
        }
    }

    private async void SignInWithEmailClicked()
    {
        if (string.IsNullOrWhiteSpace(accountEmailInput) || string.IsNullOrWhiteSpace(accountPasswordInput))
        {
            accountError = "Enter your email and password.";
            RenderMenu();
            return;
        }
        accountBusy = true;
        accountError = null;
        RenderMenu();
        try
        {
            var result = await AccountManager.SignInWithEmailPasswordAsync(accountEmailInput.Trim(), accountPasswordInput);
            if (this == null || menuRoot == null) return;
            if (result.Ok)
            {
                if (string.IsNullOrEmpty(AccountManager.CurrentUsername))
                {
                    // Recovered an account that has credentials but no claimed name
                    // (possible after support-side data fixes): send them straight to
                    // the claim screen instead of dropping them into the menu nameless.
                    accountGateSignInMode = false;
                    accountError = "Signed in! This account has no name yet - pick one to finish.";
                }
                else
                {
                    showingAccountGate = false;
                }
            }
            else accountError = result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            accountError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                accountBusy = false;
                RenderMenu();
            }
        }
    }

    private async void LinkEmailClicked()
    {
        if (string.IsNullOrWhiteSpace(accountEmailInput) || string.IsNullOrWhiteSpace(accountPasswordInput))
        {
            accountError = "Enter an email and password.";
            RenderMenu();
            return;
        }
        accountBusy = true;
        accountError = null;
        RenderMenu();
        try
        {
            var result = await AccountManager.LinkEmailPasswordAsync(accountEmailInput.Trim(), accountPasswordInput);
            if (this == null || menuRoot == null) return;
            if (!result.Ok) accountError = result.Message;
            else if (accountGatePostClaimMode) // reached from the claim retry screen
            {
                // Linked from the post-claim step - done with the whole gate.
                accountGatePostClaimMode = false;
                showingAccountGate = false;
                accountPasswordInput = "";
            }
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            accountError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                accountBusy = false;
                RenderMenu();
            }
        }
    }

    private async void RequestPasswordResetClicked()
    {
        if (string.IsNullOrWhiteSpace(accountEmailInput)) { accountError = "Enter your email first."; RenderMenu(); return; }
        accountSettingsResetMode = true;
        accountBusy = true;
        accountError = null;
        RenderMenu();
        try
        {
            await AccountManager.RequestPasswordResetAsync(accountEmailInput.Trim());
            if (this == null || menuRoot == null) return;
            accountError = "If that email has an account, a code is on its way.";
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            accountError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                accountBusy = false;
                RenderMenu();
            }
        }
    }

    private async void ConfirmPasswordResetClicked()
    {
        if (string.IsNullOrWhiteSpace(resetTokenInput) || string.IsNullOrWhiteSpace(resetNewPasswordInput))
        {
            accountError = "Enter the code from your email and a new password.";
            RenderMenu();
            return;
        }
        accountBusy = true;
        accountError = null;
        RenderMenu();
        try
        {
            var result = await AccountManager.ConfirmPasswordResetAsync(resetTokenInput.Trim(), resetNewPasswordInput);
            if (this == null || menuRoot == null) return;
            accountError = result.Ok ? "Password updated - you can sign in with it now." : result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            accountError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                accountBusy = false;
                RenderMenu();
            }
        }
    }

    // Mirrors Unity Authentication's password policy so failures are caught with a
    // friendly message before the round-trip instead of an opaque SDK error after it.
    private static string PasswordProblem(string pw)
    {
        if (string.IsNullOrEmpty(pw) || pw.Length < 8 || pw.Length > 30)
            return "Password must be 8-30 characters.";
        bool upper = false, lower = false, digit = false, symbol = false;
        foreach (char c in pw)
        {
            if (char.IsUpper(c)) upper = true;
            else if (char.IsLower(c)) lower = true;
            else if (char.IsDigit(c)) digit = true;
            else symbol = true;
        }
        if (!(upper && lower && digit && symbol))
            return "Password needs an uppercase letter, a lowercase letter, a number, and a symbol.";
        return null;
    }

    // ── Guest mode ────────────────────────────────────────────────────────────
    // "Guest <title> <character>" - display-only, never claimed server-side, so
    // guests skip the account flow entirely. Online-identity features (friends,
    // ranked) check AccountManager.IsGuest and gate themselves off.

    private static readonly string[] GuestTitles =
    {
        "Captain", "Admiral", "Vice Admiral", "Warlord", "Supernova", "First Mate",
        "Navigator", "Shipwright", "Cabin Boy", "Bounty Hunter", "Revolutionary", "Yonko",
    };

    private static readonly string[] GuestCharacters =
    {
        "Luffy", "Zoro", "Nami", "Usopp", "Sanji", "Chopper", "Robin", "Franky",
        "Brook", "Jinbe", "Ace", "Sabo", "Shanks", "Buggy", "Law", "Kid",
        "Hancock", "Crocodile", "Doflamingo", "Katakuri", "Yamato", "Carrot",
        "Vivi", "Rebecca", "Perona", "Marco", "Izo", "Denjiro", "Kinemon", "Oden",
    };

    private void ContinueAsGuestClicked()
    {
        var rng = new System.Random();
        string guestName = $"[Guest] {GuestTitles[rng.Next(GuestTitles.Length)]} {GuestCharacters[rng.Next(GuestCharacters.Length)]}";
        AccountManager.StartGuestSession(guestName);
        showingAccountGate = false;
        accountGateSignInMode = false;
        accountGatePostClaimMode = false;
        accountError = null;
        RenderMenu();
    }

    private string DescribeClaimFailure(AccountFailureReason reason)
    {
        switch (reason)
        {
            case AccountFailureReason.TooLong: return "That name's too long (max 16 characters).";
            case AccountFailureReason.BadChars: return "Letters, numbers, and underscore only.";
            case AccountFailureReason.Profanity: return "That name isn't allowed.";
            case AccountFailureReason.NameTaken: return "That name's taken - try another.";
            case AccountFailureReason.AlreadyHasUsername: return "You already have a name.";
            case AccountFailureReason.NoNetwork: return "Couldn't reach the server - check your connection.";
            default: return "Enter a name first.";
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Friends (stage swapped in for the "Friends" nav row). Relationship graph +
    // presence live in FriendsManager.cs (Unity Friends service); this is just the
    // uGUI wiring, following the same panel/busy/error idiom as everything above.
    // ══════════════════════════════════════════════════════════════════════════

    private string FriendsOnlineSubtitle()
    {
        int online = friendsList.Count(f => f.Online);
        return online == 1 ? "1 online" : $"{online} online";
    }

    private void OpenFriends()
    {
        showingAccountSettings = false;
        showingReplays = false;
        showingProfile = false;
        showingFriends = true;
        friendsError = null;
        RenderMenu();
        RefreshFriendsLists();
    }

    private void CloseFriends()
    {
        showingFriends = false;
        friendsError = null;
        RenderMenu();
    }

    private async void RefreshFriendsLists()
    {
        if (AccountManager.IsGuest) return;
        try
        {
            var friends = await FriendsManager.GetFriendsAsync();
            var incoming = await FriendsManager.GetIncomingRequestsAsync();
            var outgoing = await FriendsManager.GetOutgoingRequestsAsync();
            if (this == null || menuRoot == null) return;
            friendsList = friends;
            incomingRequests = incoming;
            outgoingRequests = outgoing;
            RenderMenu();
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            friendsError = $"Couldn't load friends: {ex.Message}";
            RenderMenu();
        }
    }

    private void BuildFriendsStage(RectTransform stage)
    {
        const float titleH = 60f;

        var titleRow = PanelObject("Friends Title Row", stage, new Color(0, 0, 0, 0));
        Stretch(titleRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -titleH), Vector2.zero);
        var titleText = TextObject("Title", titleRow, "Friends", 26, Ink, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(4f, 0f), Vector2.zero);

        var backHolder = PanelObject("Back Holder", titleRow, new Color(0, 0, 0, 0));
        Stretch(backHolder, new Vector2(0.8f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        var backHlg = backHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        backHlg.childAlignment = TextAnchor.MiddleRight;
        backHlg.childControlWidth = false;
        backHlg.childControlHeight = false;
        AddButton(backHolder, "< Back", CloseFriends, true, false);

        // Guests have no account identity - no relationships to load or invite.
        if (AccountManager.IsGuest)
        {
            var panelG = PanelObject("Friends Guest Panel", stage, new Color32(8, 16, 24, 153));
            Stretch(panelG, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, -titleH));
            Round(panelG);
            AddRoundedCardBorder(panelG, MenuB, 1f);
            var msg = TextObject("Guest Msg", panelG,
                "Friends are unavailable in guest mode.\n\nCreate an account (Settings gear, top right) to add friends, send invites, and play ranked.",
                12, Muted, TextAnchor.UpperLeft, monoFont);
            msg.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(msg.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));
            return;
        }

        var body = PanelObject("Friends Body", stage, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -titleH));

        // Left: add by username + incoming/outgoing requests.
        var requestsPanel = PanelObject("Requests Panel", body, new Color32(8, 16, 24, 153));
        Stretch(requestsPanel, Vector2.zero, new Vector2(0.42f, 1f), Vector2.zero, Vector2.zero);
        Round(requestsPanel);
        AddRoundedCardBorder(requestsPanel, MenuB, 1f);
        BuildFriendRequestsPanel(requestsPanel);

        // Right: friends list.
        var friendsPanel = PanelObject("Friends Panel", body, new Color32(8, 16, 24, 153));
        Stretch(friendsPanel, new Vector2(0.46f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        Round(friendsPanel);
        AddRoundedCardBorder(friendsPanel, MenuB, 1f);
        BuildFriendsListPanel(friendsPanel);
    }

    private void BuildFriendRequestsPanel(RectTransform panel)
    {
        var header = TextObject("Header", panel, "ADD A FRIEND", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var nameField = MakeInput(panel, "Username", addFriendUsernameInput, s => addFriendUsernameInput = s, null);
        Stretch(nameField, new Vector2(0f, 1f), new Vector2(0.68f, 1f), new Vector2(16f, -70f), new Vector2(0f, -46f));

        var addHolder = PanelObject("Add Holder", panel, new Color(0, 0, 0, 0));
        Stretch(addHolder, new Vector2(0.68f, 1f), new Vector2(1f, 1f), new Vector2(0f, -70f), new Vector2(-16f, -46f));
        AddButton(addHolder, friendsBusy ? "..." : "Add", AddFriendClicked, !friendsBusy, false);

        if (!string.IsNullOrEmpty(friendsError))
        {
            var err = TextObject("Error", panel, friendsError, 11, RedAccent, TextAnchor.UpperLeft, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -108f), new Vector2(-16f, -78f));
        }

        var listHeader = TextObject("List Header", panel, "REQUESTS", 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(listHeader.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -136f), new Vector2(-16f, -116f));

        var listArea = PanelObject("Requests List", panel, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -142f));

        var allRequests = new List<(FriendEntry entry, bool incoming)>();
        foreach (var r in incomingRequests) allRequests.Add((r, true));
        foreach (var r in outgoingRequests) allRequests.Add((r, false));

        if (allRequests.Count == 0)
        {
            var empty = TextObject("Empty", listArea, "No pending requests.", 12, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -24f));
            return;
        }

        const float rowH = 48f, gap = 6f;
        int shown = Mathf.Min(allRequests.Count, 8);
        for (int i = 0; i < shown; i++)
        {
            var (entry, incoming) = allRequests[i];
            var row = PanelObject("Request Row " + i, listArea, new Color32(14, 22, 32, 180));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowH);
            row.anchoredPosition = new Vector2(0f, -(i * (rowH + gap)));
            BuildRequestRow(row, entry, incoming);
        }
    }

    private void BuildRequestRow(RectTransform row, FriendEntry entry, bool incoming)
    {
        Round(row);
        AddRoundedCardBorder(row, MenuB, 1f);

        var name = TextObject("Name", row, entry.Username, 13, Ink, TextAnchor.MiddleLeft);
        Stretch(name.rectTransform, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(12f, 0f), Vector2.zero);

        var tag = TextObject("Tag", row, incoming ? "wants to be friends" : "pending", 10, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(tag.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.7f, 1f), Vector2.zero, Vector2.zero);

        var btnHolder = PanelObject("Buttons", row, new Color(0, 0, 0, 0));
        Stretch(btnHolder, new Vector2(0.7f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        var hlg = btnHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;

        if (incoming)
        {
            AddButton(btnHolder, "Accept", () => RespondToRequestClicked(entry.PlayerId, true), !friendsBusy, false);
            AddButton(btnHolder, "Decline", () => RespondToRequestClicked(entry.PlayerId, false), !friendsBusy, false);
        }
        else
        {
            AddButton(btnHolder, "Cancel", () => CancelOutgoingClicked(entry.PlayerId), !friendsBusy, false);
        }
    }

    private void BuildFriendsListPanel(RectTransform panel)
    {
        var header = TextObject("Header", panel, $"FRIENDS ({friendsList.Count})", 13, Muted, TextAnchor.UpperLeft, monoFont);
        header.fontStyle = FontStyle.Bold;
        Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -34f), new Vector2(-16f, -14f));

        var listArea = PanelObject("Friends List Area", panel, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -46f));

        if (friendsList.Count == 0)
        {
            var empty = TextObject("Empty", listArea, "No friends yet - add one by username.", 12, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(empty.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -24f));
            return;
        }

        const float rowH = 48f, gap = 6f;
        int shown = Mathf.Min(friendsList.Count, 10);
        for (int i = 0; i < shown; i++)
        {
            var row = PanelObject("Friend Row " + i, listArea, new Color32(14, 22, 32, 180));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowH);
            row.anchoredPosition = new Vector2(0f, -(i * (rowH + gap)));
            BuildFriendRow(row, friendsList[i]);
        }
    }

    private void BuildFriendRow(RectTransform row, FriendEntry entry)
    {
        Round(row);
        AddRoundedCardBorder(row, MenuB, 1f);

        var dot = PanelObject("Presence Dot", row, entry.Online ? Accent : Muted);
        dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
        dot.pivot = new Vector2(0f, 0.5f);
        dot.sizeDelta = new Vector2(8f, 8f);
        dot.anchoredPosition = new Vector2(12f, 0f);
        RoundCircle(dot);

        var name = TextObject("Name", row, entry.Username, 13, Ink, TextAnchor.MiddleLeft);
        Stretch(name.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(28f, 0f), Vector2.zero);

        var btnHolder = PanelObject("Buttons", row, new Color(0, 0, 0, 0));
        Stretch(btnHolder, new Vector2(0.6f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        var hlg = btnHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        AddButton(btnHolder, "Remove", () => RemoveFriendClicked(entry.PlayerId), !friendsBusy, false);
        AddButton(btnHolder, "Block", () => BlockFriendClicked(entry.PlayerId), !friendsBusy, false);
    }

    private async void AddFriendClicked()
    {
        if (string.IsNullOrWhiteSpace(addFriendUsernameInput)) { friendsError = "Enter a username first."; RenderMenu(); return; }
        friendsBusy = true;
        friendsError = null;
        RenderMenu();
        try
        {
            var result = await FriendsManager.SendFriendRequestByUsernameAsync(addFriendUsernameInput.Trim());
            if (this == null || menuRoot == null) return;
            if (result.Ok)
            {
                addFriendUsernameInput = "";
                await RefreshFriendsListsAwaited();
            }
            else
            {
                friendsError = result.Message;
            }
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            friendsError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                friendsBusy = false;
                RenderMenu();
            }
        }
    }

    private async void RespondToRequestClicked(string requesterPlayerId, bool accept)
    {
        friendsBusy = true;
        friendsError = null;
        RenderMenu();
        try
        {
            var result = accept
                ? await FriendsManager.AcceptRequestAsync(requesterPlayerId)
                : await FriendsManager.DeclineRequestAsync(requesterPlayerId);
            if (this == null || menuRoot == null) return;
            if (result.Ok) await RefreshFriendsListsAwaited();
            else friendsError = result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            friendsError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                friendsBusy = false;
                RenderMenu();
            }
        }
    }

    private async void CancelOutgoingClicked(string targetPlayerId)
    {
        friendsBusy = true;
        friendsError = null;
        RenderMenu();
        try
        {
            var result = await FriendsManager.CancelOutgoingRequestAsync(targetPlayerId);
            if (this == null || menuRoot == null) return;
            if (result.Ok) await RefreshFriendsListsAwaited();
            else friendsError = result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            friendsError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                friendsBusy = false;
                RenderMenu();
            }
        }
    }

    private async void RemoveFriendClicked(string playerId)
    {
        friendsBusy = true;
        friendsError = null;
        RenderMenu();
        try
        {
            var result = await FriendsManager.RemoveFriendAsync(playerId);
            if (this == null || menuRoot == null) return;
            if (result.Ok) await RefreshFriendsListsAwaited();
            else friendsError = result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            friendsError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                friendsBusy = false;
                RenderMenu();
            }
        }
    }

    private async void BlockFriendClicked(string playerId)
    {
        friendsBusy = true;
        friendsError = null;
        RenderMenu();
        try
        {
            var result = await FriendsManager.BlockAsync(playerId);
            if (this == null || menuRoot == null) return;
            if (result.Ok) await RefreshFriendsListsAwaited();
            else friendsError = result.Message;
        }
        catch (Exception ex)
        {
            if (this == null || menuRoot == null) return;
            friendsError = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            if (this != null && menuRoot != null)
            {
                friendsBusy = false;
                RenderMenu();
            }
        }
    }

    // Awaitable twin of RefreshFriendsLists (which is async void, fire-and-forget, for the
    // FriendsChanged event handler) - the click handlers above need to await the reload
    // before flipping friendsBusy back off, otherwise the UI would flash stale data.
    private async Task RefreshFriendsListsAwaited()
    {
        var friends = await FriendsManager.GetFriendsAsync();
        var incoming = await FriendsManager.GetIncomingRequestsAsync();
        var outgoing = await FriendsManager.GetOutgoingRequestsAsync();
        if (this == null || menuRoot == null) return;
        friendsList = friends;
        incomingRequests = incoming;
        outgoingRequests = outgoing;
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
        // ── Deck selection ── each player picks the deck they'll bring; the pick
        // is shared with the peer (OptcgDeckShare) and both decks ride inside the
        // match-start payload. No pick = that seat's starter default.
        SubscribeToSessionEvents(session);   // idempotent; re-attaches after a picker rebuild
        var myDeck = DeckStore.Get(lobbyDeckId);
        string myDeckName = myDeck != null ? myDeck.name
            : (session.IsHost ? "Straw Hat Crew [ST01] (default)" : "Worst Generation [ST02] (default)");
        var myDeckText = TextObject("My Deck", panel, $"YOUR DECK: {myDeckName}", 12, Ink, TextAnchor.UpperLeft, monoFont);
        Stretch(myDeckText.rectTransform, new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(16f, y - 24f), new Vector2(-8f, y));
        var pickHolder = PanelObject("Pick Deck Holder", panel, new Color(0, 0, 0, 0));
        Stretch(pickHolder, new Vector2(0.6f, 1f), Vector2.one, new Vector2(0f, y - 30f), new Vector2(-16f, y + 4f));
        AddButton(pickHolder, "Select Deck", PickLobbyDeck, !lobbyBusy, false);
        y -= 34f;
        string peerDeckName = lobbyPeerDeck != null ? lobbyPeerDeck.name : "not chosen yet (starter default)";
        var peerDeckText = TextObject("Peer Deck", panel, $"OPPONENT DECK: {peerDeckName}", 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(peerDeckText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 22f), new Vector2(-16f, y));
        y -= 28f;

        bool bothPresent = session.PlayerCount >= session.MaxPlayers;
        bool networkReady = MatchNetworkSync.IsPeerConnected;
        string noteMessage;
        if (!bothPresent) noteMessage = "Waiting for another player to join...";
        else if (!networkReady) noteMessage = "Both players are here. Finishing connection...";
        else if (session.IsHost) noteMessage = "Both players are here — pick decks, then click Start Match.";
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

    // Opens the deck-builder hex roster as a one-shot picker for this online match.
    // Same teardown/rebuild pattern as PickPlayerDeck (versus-self): the picker owns
    // the screen, and a fresh MainMenuManager reopens the waiting room on confirm
    // or cancel (reopenLobbyAfterPicker + Awake). Starter decks work here too via
    // their virtual "starter:" ids — nothing is copied into the user's roster.
    private void PickLobbyDeck()
    {
        CancelInvoke();
        UnsubscribeFromSessionEvents();
        if (canvas != null) canvas.gameObject.SetActive(false);
        DeckBuilderManager.OpenPicker("CHOOSE YOUR DECK", "for this online match — pick a deck, then confirm",
            chosenId =>
            {
                lobbyDeckId = chosenId;
                reopenLobbyAfterPicker = true;
                ShareLobbyDeck();
                EnsureMenu();
            },
            () => { reopenLobbyAfterPicker = true; EnsureMenu(); });
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // Tell the peer what we're playing. Safe to call any time: no-ops when no peer
    // is connected yet (OnLobbyNetworkClientConnected re-sends once one is).
    private static void ShareLobbyDeck()
    {
        var deck = DeckStore.Get(lobbyDeckId);
        if (deck != null) MatchNetworkSync.SendDeckShare(NetworkDeck.From(deck));
    }

    private void OnPeerDeckShared(NetworkDeck deck)
    {
        lobbyPeerDeck = deck;
        if (showingLobbyHub) RenderMenu();   // live-update the "OPPONENT DECK" line
    }

    // Host: generates the shared seed and sends it with BOTH deck picks, then each
    // client independently calls GameEngine.CreateMatch with the same payload - no
    // game state itself is transmitted, just what both sides need to build an
    // identical match. Host is always "south", guest always "north".
    private void StartMatchClicked()
    {
        var payload = new MatchStartPayload
        {
            seed = Guid.NewGuid().ToString("N"),
            south = NetworkDeck.From(DeckStore.Get(lobbyDeckId)),   // null → engine default ST01
            north = lobbyPeerDeck,                                  // null → engine default ST02
        };
        MatchNetworkSync.SendMatchStart(payload);
        LaunchNetworkedMatch(payload, "south");
    }

    private void OnNetworkMatchStartReceived(MatchStartPayload payload)
    {
        LaunchNetworkedMatch(payload, "north");
    }

    private void LaunchNetworkedMatch(MatchStartPayload payload, string localSeat)
    {
        CancelInvoke();
        UnsubscribeFromSessionEvents();
        if (canvas != null) canvas.gameObject.SetActive(false);
        GameManager.PendingNetworkedSeed = payload.seed;
        GameManager.PendingNetworkedSeat = localSeat;
        GameManager.PendingNetworkedSouthDeck = payload.south;
        GameManager.PendingNetworkedNorthDeck = payload.north;
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
        // If we picked a deck before the Relay connection finished, the share was
        // a no-op — re-send now that there's actually a peer to receive it.
        ShareLobbyDeck();
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
        MatchNetworkSync.DeckShareReceived -= OnPeerDeckShared;
        FriendsManager.FriendsChanged -= OnFriendsChanged;
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
            var session = await LobbyManager.CreateLobbyAsync(lobbyNameInput, lobbyIsPrivate, AccountManager.CurrentUsername ?? AccountManager.CachedUsername ?? AccountManager.GuestDisplayName ?? DefaultPlayerName);
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
        lobbyPeerDeck = null;   // stale picks shouldn't leak into the next lobby
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
        UnityEngine.Events.UnityAction<string> onChanged, UnityEngine.Events.UnityAction<string> onEnd,
        InputField.ContentType contentType = InputField.ContentType.Standard)
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
        field.contentType = contentType;
        field.caretColor = Accent;
        field.customCaretColor = true;
        if (onChanged != null) field.onValueChanged.AddListener(s => onChanged(s));
        if (onEnd != null) field.onEndEdit.AddListener(s => onEnd(s));
        tabOrder.Add(field); // creation order == visual order == tab order
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
            ("Play",     "Game modes",          null,       !showingReplays && !showingFriends && !showingProfile),
            ("Decks",    "Build & edit",        null,       false),
            ("Match History", "Watch past matches", null,   showingReplays),
            ("Friends",  "Crew & invites",      FriendsOnlineSubtitle(), showingFriends),
            ("Settings", "Preferences & audio", null,       false),
        };

        UnityEngine.Events.UnityAction[] actions =
        {
            () => { showingAccountSettings = false; showingFriends = false; showingReplays = false;
                    showingProfile = false; RenderMenu(); },
            () => OpenDeckBuilder(),
            () => { showingAccountSettings = false; showingFriends = false; showingProfile = false;
                    showingReplays = true; selectedMatchId = null; matchHistory = null; RenderMenu(); },
            OpenFriends,
            OpenAccountSettings,
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
        UnityEngine.Events.UnityAction action, bool enabled = true, bool dot = true, bool fill = false)
    {
        var root = PanelObject(label + " Button", parent,
            enabled ? new Color32(34, 58, 78, 235) : new Color32(24, 34, 44, 170));
        if (fill)
        {
            // Stretch to the holder instead of the default fixed 118x34 chip -
            // used by the modal/wizard screens where buttons span the panel.
            Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }
        else
        {
            SetPreferred(root, new Vector2(118, 34));
            root.sizeDelta = new Vector2(118, 34);
        }
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

    private Sprite _hGradientSprite;
    // Horizontal gradient: opaque white at the LEFT fading to transparent at the
    // right (biased toward the left, mirroring GetVGradientSprite's falloff).
    // Tinted dark it becomes the leader-banner readability scrim; mirror it with
    // localScale.x = -1 for the opponent's right-dark variant.
    private Sprite GetHGradientSprite()
    {
        if (_hGradientSprite != null) return _hGradientSprite;
        const int W = 128, H = 4;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++)
        {
            float t = x / (float)(W - 1);     // 0 at left column, 1 at right column
            float a = Mathf.Clamp01(1f - t);
            a = a * a;                         // bias the solid part toward the left
            byte ab = (byte)Mathf.RoundToInt(a * 255f);
            for (int y = 0; y < H; y++) px[y * W + x] = new Color32(255, 255, 255, ab);
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _hGradientSprite = Sprite.Create(tex, new Rect(0, 0, W, H),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _hGradientSprite;
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
