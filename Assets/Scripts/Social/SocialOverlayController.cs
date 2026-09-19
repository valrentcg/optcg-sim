// One Piece TCG - persistent social drawer.
//
// Unlike the original MainMenuManager-owned chat dock, this object survives every
// menu/deck-builder/match hand-off. It owns the inbox, conversation drafts, unread
// state and invite notifications, while the match's opponent chat remains separate.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum SocialSurfaceContext
{
    MainMenu,
    DeckBuilder,
    Sealed,
    Match,
    Replay,
}

public sealed class SocialOverlayController : MonoBehaviour
{
    private static readonly Color Ink = new Color32(238, 242, 247, 255);
    private static readonly Color Muted = new Color32(151, 166, 188, 255);
    private static readonly Color Accent = new Color32(79, 195, 224, 255);
    private static readonly Color AccentSoft = new Color32(38, 92, 111, 255);
    private static readonly Color Green = new Color32(75, 221, 150, 255);
    private static readonly Color Gold = new Color32(226, 190, 102, 255);
    private static readonly Color Danger = new Color32(230, 84, 84, 255);
    private static readonly Color PanelBg = new Color32(10, 22, 35, 252);
    private static readonly Color Panel2 = new Color32(15, 31, 47, 255);
    private static readonly Color Panel3 = new Color32(21, 42, 59, 255);
    private static readonly Color Border = new Color32(91, 151, 185, 92);

    private sealed class ThreadState
    {
        public string PlayerId;
        public string Username;
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
        public long LastId;
        public long OldestId;
        public string Draft = "";
        public string Error;
        public bool Loading;
        public bool Loaded;
        public bool LoadingOlder;
        public bool HasOlder;
        public float ScrollPosition;
        public bool StickToBottom = true;
    }

    public static SocialOverlayController Instance { get; private set; }
    public static event Action StateChanged;

    public static int UnreadTotal => Instance?._unreadTotal ?? 0;
    public static int OnlineCount => Instance?._friends.Count(f => f.Online) ?? 0;
    public static int UnreadFor(string playerId)
    {
        if (Instance == null || string.IsNullOrEmpty(playerId)) return 0;
        return Instance._unread.TryGetValue(playerId, out var count) ? count : 0;
    }

    public static IReadOnlyList<ChatConversationSummary> ConversationSummaries =>
        Instance?._conversations ?? (IReadOnlyList<ChatConversationSummary>)Array.Empty<ChatConversationSummary>();

    public static bool InputFocused
    {
        get
        {
            if (Instance == null || EventSystem.current == null) return false;
            var selected = EventSystem.current.currentSelectedGameObject;
            var field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && Instance._root != null && field.transform.IsChildOf(Instance._root);
        }
    }

    public static void EnsureCreated()
    {
        if (Instance != null) return;
        var found = UnityEngine.Object.FindAnyObjectByType<SocialOverlayController>();
        if (found != null) { Instance = found; return; }
        new GameObject("Persistent Social Overlay").AddComponent<SocialOverlayController>();
    }

    public static void SetContext(SocialSurfaceContext context)
    {
        EnsureCreated();
        if (Instance == null || Instance._context == context) return;
        Instance._context = context;
        Instance.Render();
    }

    public static void OpenConversation(string playerId, string username)
    {
        EnsureCreated();
        Instance?.OpenThread(playerId, username);
    }

    public static void OpenDrawer(string tab = "messages")
    {
        EnsureCreated();
        if (Instance == null) return;
        Instance._drawerOpen = true;
        Instance._tab = tab;
        Instance.Render();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBoot() => EnsureCreated();

    private Canvas _canvas;
    private RectTransform _root;
    private Font _font;
    private Font _mono;
    private Sprite _rounded;
    private Sprite _circle;
    private SocialSurfaceContext _context = SocialSurfaceContext.MainMenu;
    private bool _drawerOpen;
    private string _tab = "messages";
    private string _search = "";
    private string _addName = "";
    private string _error;
    private string _selectedId;
    private readonly List<FriendEntry> _friends = new List<FriendEntry>();
    private readonly List<FriendEntry> _incoming = new List<FriendEntry>();
    private readonly List<FriendEntry> _outgoing = new List<FriendEntry>();
    private readonly List<ChatConversationSummary> _conversations = new List<ChatConversationSummary>();
    private readonly Dictionary<string, int> _unread = new Dictionary<string, int>();
    private readonly Dictionary<string, string> _knownNames = new Dictionary<string, string>();
    private readonly Dictionary<string, ThreadState> _threads = new Dictionary<string, ThreadState>();
    private int _unreadTotal;
    private bool _busy;
    private bool _polling;
    private int _pollFailures;
    private string _identity;
    private readonly List<GameInvite> _invites = new List<GameInvite>();
    private string _toastPeerId;
    private string _toastBody;
    private float _toastUntil;
    private bool _inviteBusy;
    private Vector2 _lastScreen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        try { _mono = Font.CreateDynamicFontFromOSFont(new[] { "JetBrains Mono", "Consolas", "Cascadia Mono", "Courier New" }, 14); }
        catch { _mono = _font; }
        BuildCanvas();
        FriendsManager.FriendsChanged += OnFriendsChanged;
        Render();
        StartPolling();
    }

    private void OnDestroy()
    {
        FriendsManager.FriendsChanged -= OnFriendsChanged;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var size = new Vector2(Screen.width, Screen.height);
        if (_lastScreen != size) { _lastScreen = size; Render(); }
        if (_toastUntil > 0f && Time.unscaledTime > _toastUntil)
        {
            _toastUntil = 0f;
            Render();
        }
    }

    private void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20000;
        gameObject.AddComponent<GraphicRaycaster>();
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.dynamicPixelsPerUnit = 4f;
        var rootObject = new GameObject("Social Root", typeof(RectTransform));
        rootObject.transform.SetParent(transform, false);
        _root = rootObject.GetComponent<RectTransform>();
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = _root.offsetMax = Vector2.zero;
    }

    private void OnFriendsChanged() => RefreshFriends();

    private void StartPolling()
    {
        if (_polling) return;
        _polling = true;
        PollLoop();
    }

    private async void PollLoop()
    {
        while (this != null)
        {
            await PollOnce();
            await Task.Delay(3000 * (1 << Mathf.Min(_pollFailures, 4)));
        }
    }

    private async Task PollOnce()
    {
        int failuresBefore = SocialHttp.FailureCount;
        try
        {
            await AccountManager.EnsureReadyAsync();
            if (this == null) return;
            string identity = null;
            try { identity = AuthenticationService.Instance.PlayerId; } catch { }
            if (!string.Equals(identity, _identity, StringComparison.Ordinal))
            {
                ResetForIdentity(identity);
                if (!AccountManager.IsGuest && !string.IsNullOrEmpty(identity)) await RefreshFriendsAwaited();
            }
            if (AccountManager.IsGuest || string.IsNullOrEmpty(identity)) return;

            var (unreadOk, unreadRows, total) = await ChatStore.PollUnreadAsync();
            if (this == null) return;
            bool changed = false;
            int previousUnread = _unreadTotal;
            if (unreadOk)
            {
                var next = new Dictionary<string, int>();
                foreach (var row in unreadRows)
                    if (!string.IsNullOrEmpty(row.fromId) && row.count > 0) next[row.fromId] = row.count;
                changed = total != _unreadTotal || next.Count != _unread.Count ||
                    next.Any(kv => !_unread.TryGetValue(kv.Key, out var old) || old != kv.Value);
                _unread.Clear();
                foreach (var kv in next) _unread[kv.Key] = kv.Value;
                _unreadTotal = total;
            }

            var (conversationOk, summaries) = await ChatStore.ConversationsAsync();
            if (this == null) return;
            if (conversationOk && !SameConversations(_conversations, summaries))
            {
                _conversations.Clear();
                _conversations.AddRange(summaries);
                changed = true;
            }

            if (_unreadTotal > previousUnread && !_drawerOpen)
            {
                var newest = _conversations.FirstOrDefault(c => c.unreadCount > 0);
                if (newest != null)
                {
                    _toastPeerId = newest.peerId;
                    _toastBody = newest.lastBody;
                    _toastUntil = Time.unscaledTime + 7f;
                    changed = true;
                }
            }

            int inviteFailures = SocialHttp.FailureCount;
            var invites = await InviteStore.PollAsync();
            if (this == null) return;
            if (SocialHttp.FailureCount == inviteFailures && !SameInvites(_invites, invites))
            {
                _invites.Clear();
                _invites.AddRange(invites);
                changed = true;
            }

            if (!string.IsNullOrEmpty(_selectedId) && _threads.TryGetValue(_selectedId, out var thread) && thread.Loaded)
            {
                var (tailOk, tail) = await ChatStore.HistoryAsync(thread.PlayerId, thread.LastId);
                if (this == null) return;
                if (tailOk && MergeMessages(thread, tail))
                {
                    changed = true;
                    _ = MarkRead(thread.PlayerId);
                }
            }

            if (changed) NotifyAndRender();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Social overlay poll: " + ex.Message);
        }
        _pollFailures = SocialHttp.FailureCount > failuresBefore ? Mathf.Min(_pollFailures + 1, 4) : 0;
    }

    private static bool SameConversations(List<ChatConversationSummary> a, List<ChatConversationSummary> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].peerId != b[i].peerId || a[i].lastId != b[i].lastId ||
                a[i].unreadCount != b[i].unreadCount || a[i].lastBody != b[i].lastBody) return false;
        }
        return true;
    }

    private static bool SameInvites(List<GameInvite> a, List<GameInvite> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i].id != b[i].id) return false;
        return true;
    }

    private void ResetForIdentity(string identity)
    {
        _identity = identity;
        _friends.Clear(); _incoming.Clear(); _outgoing.Clear(); _conversations.Clear();
        _unread.Clear(); _threads.Clear(); _invites.Clear(); _knownNames.Clear();
        _selectedId = null; _unreadTotal = 0; _drawerOpen = false;
        _toastPeerId = null; _toastBody = null; _toastUntil = 0f; _error = null;
        NotifyAndRender();
    }

    private async void RefreshFriends() => await RefreshFriendsAwaited();

    private async Task RefreshFriendsAwaited()
    {
        if (AccountManager.IsGuest) return;
        try
        {
            var f = await FriendsManager.GetFriendsAsync();
            var inc = await FriendsManager.GetIncomingRequestsAsync();
            var outgoing = await FriendsManager.GetOutgoingRequestsAsync();
            if (this == null) return;
            _friends.Clear(); _friends.AddRange(f);
            _incoming.Clear(); _incoming.AddRange(inc);
            _outgoing.Clear(); _outgoing.AddRange(outgoing);
            foreach (var entry in _friends.Concat(_incoming).Concat(_outgoing))
                if (!string.IsNullOrEmpty(entry.PlayerId)) _knownNames[entry.PlayerId] = entry.Username;
            NotifyAndRender();
        }
        catch (Exception ex) { Debug.LogWarning("Social friends refresh: " + ex.Message); }
    }

    private void NotifyAndRender()
    {
        StateChanged?.Invoke();
        Render();
    }

    private void OpenThread(string playerId, string username)
    {
        if (string.IsNullOrEmpty(playerId) || !AccountManager.HasClaimedIdentity) return;
        _knownNames[playerId] = string.IsNullOrWhiteSpace(username) ? ResolveName(playerId) : username;
        _selectedId = playerId;
        _drawerOpen = true;
        _tab = "messages";
        _toastPeerId = null; _toastBody = null; _toastUntil = 0f;
        var thread = GetThread(playerId);
        thread.Username = ResolveName(playerId);
        Render();
        if (!thread.Loaded) LoadHistory(thread);
        else _ = MarkRead(playerId);
    }

    private ThreadState GetThread(string playerId)
    {
        if (_threads.TryGetValue(playerId, out var state)) return state;
        state = new ThreadState { PlayerId = playerId, Username = ResolveName(playerId) };
        _threads[playerId] = state;
        return state;
    }

    private string ResolveName(string playerId)
    {
        if (_knownNames.TryGetValue(playerId ?? "", out var known) && !string.IsNullOrWhiteSpace(known)) return known;
        var friend = _friends.FirstOrDefault(f => f.PlayerId == playerId);
        if (!string.IsNullOrWhiteSpace(friend.Username)) return friend.Username;
        return string.IsNullOrEmpty(playerId) ? "Captain" : "Captain " + playerId.Substring(0, Math.Min(5, playerId.Length));
    }

    private async void LoadHistory(ThreadState thread)
    {
        if (thread == null || thread.Loading) return;
        thread.Loading = true; thread.Error = null; Render();
        var (ok, messages) = await ChatStore.HistoryAsync(thread.PlayerId);
        if (this == null || !_threads.ContainsKey(thread.PlayerId)) return;
        thread.Loading = false;
        if (!ok) thread.Error = "Messages are unavailable. Check your connection.";
        else
        {
            thread.Loaded = true;
            thread.HasOlder = messages.Count >= 80;
            MergeMessages(thread, messages);
            await MarkRead(thread.PlayerId);
        }
        Render();
    }

    private async void LoadOlder(ThreadState thread)
    {
        if (thread == null || thread.LoadingOlder || !thread.HasOlder || thread.OldestId <= 0) return;
        thread.LoadingOlder = true; Render();
        var (ok, messages) = await ChatStore.HistoryAsync(thread.PlayerId, beforeId: thread.OldestId);
        if (this == null || !_threads.ContainsKey(thread.PlayerId)) return;
        thread.LoadingOlder = false;
        if (!ok) thread.Error = "Earlier messages could not be loaded.";
        else { thread.HasOlder = messages.Count >= 80; MergeMessages(thread, messages); }
        Render();
    }

    private static bool MergeMessages(ThreadState thread, List<ChatMessage> messages)
    {
        bool changed = false;
        foreach (var message in messages)
        {
            if (thread.Messages.Any(m => m.id == message.id)) continue;
            thread.Messages.Add(message); changed = true;
        }
        if (!changed) return false;
        thread.Messages.Sort((a, b) => a.id.CompareTo(b.id));
        thread.OldestId = thread.Messages[0].id;
        thread.LastId = thread.Messages[thread.Messages.Count - 1].id;
        if (thread.StickToBottom) thread.ScrollPosition = 0f;
        return true;
    }

    private async Task MarkRead(string playerId)
    {
        if (!await ChatStore.MarkReadAsync(playerId) || this == null) return;
        if (_unread.Remove(playerId))
        {
            _unreadTotal = _unread.Values.Sum();
            for (int i = 0; i < _conversations.Count; i++)
                if (_conversations[i].peerId == playerId) _conversations[i].unreadCount = 0;
            NotifyAndRender();
        }
    }

    private async void Send(ThreadState thread)
    {
        if (thread == null || _busy || string.IsNullOrWhiteSpace(thread.Draft)) return;
        string body = thread.Draft.Trim();
        if (body.Length > 1000) { thread.Error = "Messages can be at most 1,000 characters."; Render(); return; }
        _busy = true; thread.Error = null; Render();
        var sent = await ChatStore.SendAsync(thread.PlayerId, body);
        if (this == null) return;
        if (sent == null) thread.Error = "Message failed to send. Try again.";
        else { thread.Draft = ""; MergeMessages(thread, new List<ChatMessage> { sent }); }
        _busy = false; Render();
    }

    private async void AddFriend()
    {
        if (_busy || string.IsNullOrWhiteSpace(_addName)) return;
        _busy = true; _error = null; Render();
        var result = await FriendsManager.SendFriendRequestByUsernameAsync(_addName.Trim());
        if (this == null) return;
        if (result.Ok) { _addName = ""; await RefreshFriendsAwaited(); }
        else _error = result.Message;
        _busy = false; Render();
    }

    private async void RespondRequest(string playerId, bool accept)
    {
        if (_busy) return;
        _busy = true; Render();
        var result = accept ? await FriendsManager.AcceptRequestAsync(playerId) : await FriendsManager.DeclineRequestAsync(playerId);
        if (!result.Ok) _error = result.Message;
        await RefreshFriendsAwaited();
        _busy = false; Render();
    }

    private async void CancelRequest(string playerId)
    {
        if (_busy) return;
        _busy = true; Render();
        var result = await FriendsManager.CancelOutgoingRequestAsync(playerId);
        if (!result.Ok) _error = result.Message;
        await RefreshFriendsAwaited();
        _busy = false; Render();
    }

    private bool CanAcceptInvite(out MainMenuManager menu)
    {
        menu = UnityEngine.Object.FindAnyObjectByType<MainMenuManager>();
        return _context == SocialSurfaceContext.MainMenu && menu != null && menu.SocialInviteActionsAvailable;
    }

    private void AcceptInvite(GameInvite invite)
    {
        if (_inviteBusy || invite == null || !CanAcceptInvite(out var menu)) return;
        _inviteBusy = true; Render();
        menu.AcceptInviteFromSocialOverlay(invite);
    }

    public static void InviteActionFinished(string inviteId)
    {
        if (Instance == null) return;
        Instance._inviteBusy = false;
        if (!string.IsNullOrEmpty(inviteId)) Instance._invites.RemoveAll(i => i.id == inviteId);
        Instance.NotifyAndRender();
    }

    private async void DeclineInvite(GameInvite invite)
    {
        if (_inviteBusy || invite == null) return;
        _inviteBusy = true; Render();
        try { await InviteStore.RespondAsync(invite.id, false); }
        finally
        {
            if (this != null)
            {
                _invites.RemoveAll(i => i.id == invite.id);
                _inviteBusy = false;
                NotifyAndRender();
            }
        }
    }

    private void Render()
    {
        if (_root == null) return;
        string focusName = null;
        int caret = 0;
        if (EventSystem.current != null)
        {
            var go = EventSystem.current.currentSelectedGameObject;
            var input = go != null ? go.GetComponent<InputField>() : null;
            if (input != null && input.transform.IsChildOf(_root)) { focusName = input.gameObject.name; caret = input.caretPosition; }
        }
        for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
        if (AccountManager.HasClaimedIdentity)
        {
            if (_drawerOpen) BuildDrawer(); else BuildDockButton();
            BuildNotifications();
        }
        if (!string.IsNullOrEmpty(focusName)) StartCoroutine(RestoreFocus(focusName, caret));
    }

    private IEnumerator RestoreFocus(string objectName, int caret)
    {
        yield return null;
        if (this == null || _root == null || EventSystem.current == null) yield break;
        foreach (var input in _root.GetComponentsInChildren<InputField>(true))
        {
            if (input.gameObject.name != objectName || !input.interactable) continue;
            EventSystem.current.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            input.caretPosition = Mathf.Clamp(caret, 0, input.text.Length);
            yield break;
        }
    }

    private void BuildDockButton()
    {
        bool match = _context == SocialSurfaceContext.Match || _context == SocialSurfaceContext.Replay;
        var dock = Panel("Social Dock", _root, Panel2, true);
        dock.anchorMin = dock.anchorMax = new Vector2(1f, match ? 1f : 0f);
        dock.pivot = new Vector2(1f, match ? 1f : 0f);
        dock.sizeDelta = new Vector2(258f, 46f);
        dock.anchoredPosition = new Vector2(-18f, match ? -18f : 18f);
        AddBorder(dock, _unreadTotal > 0 ? Accent : Border, 1.2f);
        var button = dock.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => { _drawerOpen = true; Render(); });
        var title = Label("Title", dock, _unreadTotal > 0 ? $"SOCIAL   {_unreadTotal}" : "SOCIAL", 13,
            _unreadTotal > 0 ? Accent : Ink, TextAnchor.MiddleLeft, true);
        Stretch(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 0f), new Vector2(-96f, 0f));
        var status = Label("Status", dock, $"{OnlineCount} ONLINE", 9, OnlineCount > 0 ? Green : Muted,
            TextAnchor.MiddleRight, false, _mono);
        Stretch(status.rectTransform, Vector2.zero, Vector2.one, new Vector2(80f, 0f), new Vector2(-15f, 0f));
    }

    private void BuildDrawer()
    {
        bool match = _context == SocialSurfaceContext.Match || _context == SocialSurfaceContext.Replay;
        float availableH = _root.rect.height > 1f ? _root.rect.height : 1080f;
        float h = Mathf.Min(820f, availableH - 92f);
        float w = Mathf.Min(790f, Mathf.Max(620f, (_root.rect.width > 1f ? _root.rect.width : 1920f) * 0.52f));
        var drawer = Panel("Social Drawer", _root, PanelBg, true);
        drawer.anchorMin = drawer.anchorMax = new Vector2(1f, match ? 1f : 0f);
        drawer.pivot = new Vector2(1f, match ? 1f : 0f);
        drawer.sizeDelta = new Vector2(w, h);
        drawer.anchoredPosition = new Vector2(-18f, match ? -72f : 18f);
        AddBorder(drawer, Border, 1.2f);

        var head = Panel("Head", drawer, Panel2, true);
        Stretch(head, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -64f), Vector2.zero);
        var title = Label("Title", head, "SOCIAL", 19, Ink, TextAnchor.LowerLeft, true);
        Stretch(title.rectTransform, new Vector2(0f, 0.35f), new Vector2(0.5f, 1f), new Vector2(18f, 0f), Vector2.zero);
        var subtitle = Label("Subtitle", head, $"{OnlineCount} online  ·  {_friends.Count} friends  ·  {_unreadTotal} unread", 10,
            _unreadTotal > 0 ? Accent : Muted, TextAnchor.UpperLeft, false, _mono);
        Stretch(subtitle.rectTransform, Vector2.zero, new Vector2(0.72f, 0.42f), new Vector2(18f, 2f), Vector2.zero);
        AddButton(head, "—", () => { _drawerOpen = false; Render(); }, true,
            new Vector2(1f, 0.5f), new Vector2(34f, 32f), new Vector2(-52f, 0f), false);
        AddButton(head, "×", () => { _drawerOpen = false; Render(); }, true,
            new Vector2(1f, 0.5f), new Vector2(34f, 32f), new Vector2(-12f, 0f), false);

        var nav = Panel("Navigation", drawer, Panel2, false);
        Stretch(nav, Vector2.zero, new Vector2(0.35f, 1f), new Vector2(0f, 0f), new Vector2(-4f, -68f));
        BuildSocialNavigation(nav);

        var detail = Panel("Conversation", drawer, new Color32(8, 18, 29, 255), false);
        Stretch(detail, new Vector2(0.35f, 0f), Vector2.one, new Vector2(4f, 0f), new Vector2(0f, -68f));
        BuildConversation(detail);
    }

    private void BuildSocialNavigation(RectTransform nav)
    {
        var tabs = Panel("Tabs", nav, Color.clear, false);
        Stretch(tabs, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -48f), new Vector2(-10f, -10f));
        AddTab(tabs, "MESSAGES", "messages", 0f, 0.36f, _unreadTotal);
        AddTab(tabs, "FRIENDS", "friends", 0.37f, 0.69f, 0);
        AddTab(tabs, "REQUESTS", "requests", 0.70f, 1f, _incoming.Count);

        float searchTop = _tab == "friends" ? 94f : 62f;
        if (_tab == "friends")
        {
            var add = Input("Add Friend Input Global", nav, "Add exact username", _addName, s => _addName = s);
            Stretch(add, new Vector2(0f, 1f), new Vector2(0.72f, 1f), new Vector2(12f, -84f), new Vector2(-2f, -52f));
            AddButton(nav, _busy ? "…" : "ADD", AddFriend, !_busy,
                new Vector2(1f, 1f), new Vector2(70f, 32f), new Vector2(-12f, -68f), true);
        }
        if (_tab != "requests")
        {
            var search = Input("Social Search", nav, _tab == "messages" ? "Search messages" : "Search friends", _search,
                s => { if (_search == s) return; _search = s; Render(); });
            Stretch(search, new Vector2(0f, 1f), Vector2.one, new Vector2(12f, -searchTop - 34f), new Vector2(-12f, -searchTop));
        }
        if (!string.IsNullOrEmpty(_error))
        {
            var err = Label("Error", nav, _error, 9, Danger, TextAnchor.UpperLeft, false, _mono);
            Stretch(err.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(13f, -(searchTop + 58f)), new Vector2(-13f, -(searchTop + 36f)));
        }

        float listTop = searchTop + (_tab == "requests" ? 10f : 48f) + (!string.IsNullOrEmpty(_error) ? 28f : 0f);
        var list = Panel("List", nav, Color.clear, false);
        Stretch(list, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -listTop));
        if (_tab == "friends") BuildFriendsList(list);
        else if (_tab == "requests") BuildRequestsList(list);
        else BuildMessageList(list);
    }

    private void AddTab(RectTransform parent, string label, string value, float x0, float x1, int badge)
    {
        var tab = Panel(label, parent, _tab == value ? AccentSoft : Color.clear, true);
        Stretch(tab, new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero);
        if (_tab == value) AddBorder(tab, Accent, 1f);
        tab.gameObject.AddComponent<Button>().onClick.AddListener(() => { _tab = value; _search = ""; Render(); });
        var text = Label("Text", tab, badge > 0 ? $"{label} {badge}" : label, 8, _tab == value ? Ink : Muted,
            TextAnchor.MiddleCenter, true, _mono);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private void BuildMessageList(RectTransform area)
    {
        var visible = _conversations.Where(c => string.IsNullOrWhiteSpace(_search) ||
            ResolveName(c.peerId).IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (visible.Count == 0) { Empty(area, "No conversations yet.\nChoose a friend and say hello."); return; }
        var content = Scroll(area, visible.Count * 78f + 8f);
        for (int i = 0; i < visible.Count; i++)
        {
            var summary = visible[i];
            var row = Row(content, i, 72f, 6f, _selectedId == summary.peerId);
            var peer = summary.peerId;
            row.gameObject.AddComponent<Button>().onClick.AddListener(() => OpenThread(peer, ResolveName(peer)));
            Avatar(row, ResolveName(peer), 38f, new Vector2(9f, -36f), FriendOnline(peer));
            var name = Label("Name", row, ResolveName(peer), 12, Ink, TextAnchor.UpperLeft, true);
            Stretch(name.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(56f, 3f), new Vector2(-52f, -8f));
            string preview = (summary.lastMine ? "You: " : "") + (summary.lastBody ?? "");
            var body = Label("Preview", row, preview, 9, summary.unreadCount > 0 ? Ink : Muted, TextAnchor.UpperLeft, false);
            body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(body.rectTransform, Vector2.zero, new Vector2(1f, 0.53f), new Vector2(56f, 7f), new Vector2(-10f, 0f));
            if (summary.unreadCount > 0) Badge(row, summary.unreadCount.ToString(), new Vector2(-12f, -18f));
        }
    }

    private void BuildFriendsList(RectTransform area)
    {
        var visible = _friends.Where(f => string.IsNullOrWhiteSpace(_search) ||
            f.Username.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(f => f.Online).ThenBy(f => f.Username, StringComparer.OrdinalIgnoreCase).ToList();
        if (visible.Count == 0) { Empty(area, _friends.Count == 0 ? "No friends yet." : "No matching friends."); return; }
        var content = Scroll(area, visible.Count * 68f + 8f);
        for (int i = 0; i < visible.Count; i++)
        {
            var friend = visible[i];
            var row = Row(content, i, 62f, 6f, _selectedId == friend.PlayerId);
            var id = friend.PlayerId; var username = friend.Username;
            row.gameObject.AddComponent<Button>().onClick.AddListener(() => OpenThread(id, username));
            Avatar(row, username, 38f, new Vector2(9f, -31f), friend.Online);
            var name = Label("Name", row, username, 12, Ink, TextAnchor.LowerLeft, true);
            Stretch(name.rectTransform, new Vector2(0f, 0.48f), Vector2.one, new Vector2(56f, 0f), new Vector2(-34f, -4f));
            var status = Label("Status", row, friend.Online ? "ONLINE · AVAILABLE" : "OFFLINE", 8,
                friend.Online ? Green : Muted, TextAnchor.UpperLeft, false, _mono);
            Stretch(status.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(56f, 4f), new Vector2(-10f, 0f));
            int unread = UnreadFor(id); if (unread > 0) Badge(row, unread.ToString(), new Vector2(-10f, -15f));
        }
    }

    private void BuildRequestsList(RectTransform area)
    {
        var rows = new List<(FriendEntry entry, bool incoming)>();
        rows.AddRange(_incoming.Select(x => (x, true)));
        rows.AddRange(_outgoing.Select(x => (x, false)));
        if (rows.Count == 0) { Empty(area, "No pending friend requests."); return; }
        var content = Scroll(area, rows.Count * 88f + 8f);
        for (int i = 0; i < rows.Count; i++)
        {
            var item = rows[i];
            var row = Row(content, i, 82f, 6f, false);
            Avatar(row, item.entry.Username, 34f, new Vector2(10f, -27f), false);
            var name = Label("Name", row, item.entry.Username, 12, Ink, TextAnchor.MiddleLeft, true);
            Stretch(name.rectTransform, new Vector2(0f, 0.45f), Vector2.one, new Vector2(54f, 0f), new Vector2(-8f, -3f));
            var state = Label("State", row, item.incoming ? "WANTS TO JOIN YOUR CREW" : "REQUEST SENT", 8,
                item.incoming ? Accent : Muted, TextAnchor.MiddleLeft, false, _mono);
            Stretch(state.rectTransform, new Vector2(0f, 0.52f), Vector2.one, new Vector2(54f, 0f), new Vector2(-8f, 13f));
            string id = item.entry.PlayerId;
            if (item.incoming)
            {
                AddButton(row, "ACCEPT", () => RespondRequest(id, true), !_busy,
                    new Vector2(0f, 0f), new Vector2(82f, 26f), new Vector2(56f, 8f), true, AccentSoft);
                AddButton(row, "DECLINE", () => RespondRequest(id, false), !_busy,
                    new Vector2(0f, 0f), new Vector2(82f, 26f), new Vector2(144f, 8f), true);
            }
            else AddButton(row, "CANCEL", () => CancelRequest(id), !_busy,
                new Vector2(0f, 0f), new Vector2(82f, 26f), new Vector2(56f, 8f), true);
        }
    }

    private void BuildConversation(RectTransform pane)
    {
        if (string.IsNullOrEmpty(_selectedId))
        {
            var mark = Label("Mark", pane, "◇", 48, Accent, TextAnchor.MiddleCenter, false);
            Stretch(mark.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.68f), Vector2.zero, Vector2.zero);
            var empty = Label("Empty", pane, "Your conversations live here", 18, Ink, TextAnchor.UpperCenter, true);
            Stretch(empty.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.50f), new Vector2(20f, 0f), new Vector2(-20f, 0f));
            var sub = Label("Sub", pane, "Messages and history stay with your account.\nYou can keep chatting while browsing menus or playing a match.", 11,
                Muted, TextAnchor.UpperCenter, false);
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(sub.rectTransform, new Vector2(0f, 0.20f), new Vector2(1f, 0.39f), new Vector2(34f, 0f), new Vector2(-34f, 0f));
            return;
        }
        var thread = GetThread(_selectedId);
        string username = ResolveName(thread.PlayerId);
        var head = Panel("Conversation Head", pane, Panel2, false);
        Stretch(head, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -70f), Vector2.zero);
        Avatar(head, username, 44f, new Vector2(14f, -35f), FriendOnline(thread.PlayerId));
        var name = Label("Name", head, username, 16, Ink, TextAnchor.LowerLeft, true);
        Stretch(name.rectTransform, new Vector2(0f, 0.42f), new Vector2(0.55f, 1f), new Vector2(70f, 0f), Vector2.zero);
        var status = Label("Status", head, FriendOnline(thread.PlayerId) ? "ONLINE · AVAILABLE" : "OFFLINE", 9,
            FriendOnline(thread.PlayerId) ? Green : Muted, TextAnchor.UpperLeft, false, _mono);
        Stretch(status.rectTransform, Vector2.zero, new Vector2(0.55f, 0.46f), new Vector2(70f, 4f), Vector2.zero);
        var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuManager>();
        bool menuActions = menu != null && menu.SocialInviteActionsAvailable;
        AddButton(head, "PROFILE", () => menu?.ShowFriendProfileFromSocial(thread.PlayerId), menu != null,
            new Vector2(1f, 0.5f), new Vector2(82f, 32f), new Vector2(-104f, 0f), true);
        AddButton(head, "INVITE", () => menu?.BeginFriendInviteFromSocial(thread.PlayerId, username),
            menuActions && FriendOnline(thread.PlayerId), new Vector2(1f, 0.5f), new Vector2(82f, 32f), new Vector2(-14f, 0f), true, AccentSoft);

        var history = Panel("History", pane, new Color32(6, 14, 23, 255), false);
        Stretch(history, Vector2.zero, Vector2.one, new Vector2(10f, 70f), new Vector2(-10f, -80f));
        BuildHistory(history, thread);

        if (!string.IsNullOrEmpty(thread.Error))
        {
            var err = Label("Chat Error", pane, thread.Error, 9, Danger, TextAnchor.MiddleLeft, false, _mono);
            Stretch(err.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 57f), new Vector2(-14f, 70f));
        }
        var composer = Input("Social Composer " + thread.PlayerId, pane, "Write a message…", thread.Draft, s => thread.Draft = s);
        composer.GetComponent<InputField>().characterLimit = 1000;
        composer.GetComponent<InputField>().onEndEdit.AddListener(_ =>
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) Send(thread);
        });
        Stretch(composer, new Vector2(0f, 0f), new Vector2(0.78f, 0f), new Vector2(10f, 12f), new Vector2(-2f, 56f));
        AddButton(pane, _busy ? "…" : "SEND", () => Send(thread), !_busy,
            new Vector2(1f, 0f), new Vector2(96f, 44f), new Vector2(-10f, 12f), true, AccentSoft);
    }

    private void BuildHistory(RectTransform area, ThreadState thread)
    {
        if (thread.Loading && thread.Messages.Count == 0) { Empty(area, "Loading messages…"); return; }
        if (thread.Messages.Count == 0) { Empty(area, thread.Loaded ? "No messages yet. Say hi." : "Open this conversation to load history."); return; }
        float top = 10f;
        if (thread.HasOlder) top += 36f;
        float width = Mathf.Max(260f, area.rect.width - 72f);
        var heights = new List<float>();
        foreach (var message in thread.Messages)
        {
            int charsPerLine = Mathf.Max(18, Mathf.FloorToInt(width / 7.1f));
            int lines = Mathf.Max(1, Mathf.CeilToInt((message.body?.Length ?? 0) / (float)charsPerLine));
            heights.Add(47f + (lines - 1) * 17f);
        }
        float total = top + heights.Sum() + thread.Messages.Count * 7f + 12f;
        var content = Scroll(area, total);
        if (thread.HasOlder)
        {
            AddButton(content, thread.LoadingOlder ? "LOADING…" : "LOAD EARLIER MESSAGES", () => LoadOlder(thread), !thread.LoadingOlder,
                new Vector2(0.5f, 1f), new Vector2(190f, 28f), new Vector2(0f, -18f), true);
        }
        for (int i = 0; i < thread.Messages.Count; i++)
        {
            var message = thread.Messages[i];
            float bh = heights[i];
            var bubble = Panel("Message " + message.id, content, message.mine ? new Color32(25, 64, 82, 255) : Panel3, true);
            bubble.anchorMin = bubble.anchorMax = new Vector2(message.mine ? 1f : 0f, 1f);
            bubble.pivot = new Vector2(message.mine ? 1f : 0f, 1f);
            bubble.sizeDelta = new Vector2(width, bh);
            bubble.anchoredPosition = new Vector2(message.mine ? -10f : 10f, -top);
            var meta = Label("Meta", bubble, (message.mine ? "YOU" : thread.Username.ToUpperInvariant()) + "  ·  " + FormatTime(message.createdAt),
                8, message.mine ? Accent : Green, TextAnchor.UpperLeft, true, _mono);
            Stretch(meta.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(12f, -20f), new Vector2(-12f, -5f));
            var body = Label("Body", bubble, message.body, 11, Ink, TextAnchor.UpperLeft, false);
            body.supportRichText = false; body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(body.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 8f), new Vector2(-12f, -25f));
            top += bh + 7f;
        }
        var sr = area.GetComponentInChildren<ScrollRect>();
        if (sr != null)
        {
            sr.verticalNormalizedPosition = thread.ScrollPosition;
            sr.onValueChanged.AddListener(p => { thread.ScrollPosition = p.y; thread.StickToBottom = p.y < 0.08f; });
        }
    }

    private void BuildNotifications()
    {
        float y = -18f;
        if (_invites.Count > 0)
        {
            BuildInviteToast(_invites[0], y);
            y -= 166f;
        }
        if (_toastUntil > Time.unscaledTime && !_drawerOpen && !string.IsNullOrEmpty(_toastPeerId)) BuildMessageToast(y);
    }

    private void BuildInviteToast(GameInvite invite, float y)
    {
        var toast = Panel("Invite Toast", _root, PanelBg, true);
        toast.anchorMin = toast.anchorMax = new Vector2(1f, 1f); toast.pivot = new Vector2(1f, 1f);
        toast.sizeDelta = new Vector2(430f, 150f); toast.anchoredPosition = new Vector2(-18f, y);
        AddBorder(toast, Gold, 1.4f);
        Avatar(toast, string.IsNullOrWhiteSpace(invite.fromName) ? "Friend" : invite.fromName, 48f, new Vector2(16f, -43f), true);
        var kicker = Label("Kicker", toast, "GAME INVITATION", 9, Gold, TextAnchor.MiddleLeft, true, _mono);
        Stretch(kicker.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(80f, -30f), new Vector2(-14f, -10f));
        string who = string.IsNullOrWhiteSpace(invite.fromName) ? "A friend" : invite.fromName;
        var title = Label("Title", toast, who, 16, Ink, TextAnchor.MiddleLeft, true);
        Stretch(title.rectTransform, new Vector2(0f, 0.54f), Vector2.one, new Vector2(80f, 0f), new Vector2(-14f, -34f));
        string lobby = string.IsNullOrWhiteSpace(invite.lobbyName) ? "Custom game" : invite.lobbyName;
        var sub = Label("Sub", toast, lobby, 10, Muted, TextAnchor.MiddleLeft, false);
        Stretch(sub.rectTransform, new Vector2(0f, 0.39f), new Vector2(1f, 0.62f), new Vector2(80f, 0f), new Vector2(-14f, 0f));
        bool canAccept = CanAcceptInvite(out _);
        if (!canAccept)
        {
            var note = Label("Rule", toast, "Return to the main menu to accept", 9, Accent, TextAnchor.MiddleLeft, false, _mono);
            Stretch(note.rectTransform, Vector2.zero, new Vector2(0.54f, 0.32f), new Vector2(16f, 7f), Vector2.zero);
        }
        AddButton(toast, "DECLINE", () => DeclineInvite(invite), !_inviteBusy,
            new Vector2(1f, 0f), new Vector2(90f, 32f), new Vector2(-112f, 12f), true);
        AddButton(toast, _inviteBusy ? "…" : "ACCEPT", () => AcceptInvite(invite), canAccept && !_inviteBusy,
            new Vector2(1f, 0f), new Vector2(90f, 32f), new Vector2(-14f, 12f), true, AccentSoft);
    }

    private void BuildMessageToast(float y)
    {
        var toast = Panel("Message Toast", _root, PanelBg, true);
        toast.anchorMin = toast.anchorMax = new Vector2(1f, 1f); toast.pivot = new Vector2(1f, 1f);
        toast.sizeDelta = new Vector2(390f, 112f); toast.anchoredPosition = new Vector2(-18f, y);
        AddBorder(toast, Accent, 1.2f);
        string name = ResolveName(_toastPeerId);
        Avatar(toast, name, 42f, new Vector2(14f, -40f), FriendOnline(_toastPeerId));
        var title = Label("Title", toast, name, 14, Ink, TextAnchor.MiddleLeft, true);
        Stretch(title.rectTransform, new Vector2(0f, 0.58f), Vector2.one, new Vector2(68f, 0f), new Vector2(-82f, -10f));
        var preview = Label("Preview", toast, _toastBody ?? "New message", 10, Muted, TextAnchor.UpperLeft, false);
        preview.horizontalOverflow = HorizontalWrapMode.Wrap; preview.verticalOverflow = VerticalWrapMode.Truncate;
        Stretch(preview.rectTransform, new Vector2(0f, 0f), new Vector2(0.78f, 0.61f), new Vector2(68f, 8f), Vector2.zero);
        AddButton(toast, "OPEN", () => OpenThread(_toastPeerId, name), true,
            new Vector2(1f, 0.5f), new Vector2(72f, 32f), new Vector2(-12f, 0f), true, AccentSoft);
    }

    private bool FriendOnline(string playerId) => _friends.Any(f => f.PlayerId == playerId && f.Online);

    private static string FormatTime(long ms)
    {
        if (ms <= 0) return "now";
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("MMM d, h:mm tt"); }
        catch { return "now"; }
    }

    // ----- Small uGUI kit ----------------------------------------------------

    private RectTransform Panel(string name, Transform parent, Color color, bool rounded)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        var image = go.GetComponent<Image>(); image.color = color;
        if (rounded) { image.sprite = RoundedSprite(); image.type = Image.Type.Sliced; }
        image.raycastTarget = color.a > 0.001f;
        return rt;
    }

    private void AddBorder(RectTransform rt, Color color, float size)
    {
        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = color; outline.effectDistance = new Vector2(size, -size); outline.useGraphicAlpha = true;
    }

    private Sprite RoundedSprite()
    {
        if (_rounded != null) return _rounded;
        const int n = 32, r = 8;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Social Rounded" };
        tex.hideFlags = HideFlags.HideAndDontSave;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float cx = x < r ? r - 0.5f : x >= n - r ? n - r - 0.5f : x;
            float cy = y < r ? r - 0.5f : y >= n - r ? n - r - 0.5f : y;
            bool inside = (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
            tex.SetPixel(x, y, inside ? Color.white : Color.clear);
        }
        tex.Apply();
        _rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(r, r, r, r));
        _rounded.hideFlags = HideFlags.HideAndDontSave;
        return _rounded;
    }

    private Text Label(string name, Transform parent, string value, int size, Color color,
        TextAnchor alignment, bool bold, Font custom = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>(); text.font = custom ?? _font; text.text = value ?? ""; text.fontSize = size;
        text.color = color; text.alignment = alignment; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private RectTransform Input(string name, Transform parent, string placeholder, string value, Action<string> changed)
    {
        var holder = Panel(name, parent, new Color32(7, 15, 25, 255), true); AddBorder(holder, Border, 1f);
        var text = Label("Text", holder, value, 11, Ink, TextAnchor.MiddleLeft, false);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 4f), new Vector2(-10f, -4f));
        var hint = Label("Placeholder", holder, placeholder, 11, Muted, TextAnchor.MiddleLeft, false);
        hint.fontStyle = FontStyle.Italic;
        Stretch(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 4f), new Vector2(-10f, -4f));
        var input = holder.gameObject.AddComponent<InputField>(); input.textComponent = text; input.placeholder = hint;
        input.text = value ?? ""; input.lineType = InputField.LineType.SingleLine; input.onValueChanged.AddListener(v => changed?.Invoke(v));
        return holder;
    }

    private Button AddButton(Transform parent, string text, Action action, bool enabled, Vector2 anchor,
        Vector2 size, Vector2 pos, bool rounded, Color? fill = null)
    {
        var rt = Panel(text + " Button", parent, enabled ? (fill ?? Panel3) : new Color32(30, 39, 51, 210), rounded);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor.x > 0.9f ? new Vector2(1f, anchor.y) :
            anchor.x < 0.1f ? new Vector2(0f, anchor.y) : new Vector2(0.5f, anchor.y);
        rt.sizeDelta = size; rt.anchoredPosition = pos; AddBorder(rt, enabled ? Border : new Color32(80, 90, 105, 50), 1f);
        var label = Label("Text", rt, text, 9, enabled ? Ink : new Color32(120, 130, 146, 160), TextAnchor.MiddleCenter, true, _mono);
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var button = rt.gameObject.AddComponent<Button>(); button.interactable = enabled;
        if (action != null) button.onClick.AddListener(() => action());
        var cb = button.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        cb.pressedColor = new Color(0.78f, 0.88f, 0.93f, 1f); cb.disabledColor = Color.white; cb.colorMultiplier = 1f; button.colors = cb;
        return button;
    }

    private RectTransform Row(RectTransform content, int index, float height, float gap, bool selected)
    {
        var row = Panel("Row " + index, content, selected ? new Color32(31, 70, 87, 255) : Panel2, true);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(-8f, height); row.anchoredPosition = new Vector2(-2f, -4f - index * (height + gap));
        AddBorder(row, selected ? Accent : Border, selected ? 1.3f : 0.7f); return row;
    }

    private RectTransform Scroll(RectTransform area, float contentHeight)
    {
        var viewport = Panel("Viewport", area, Color.clear, false); Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-7f, 0f));
        // A transparent viewport still needs to receive pointer events so the
        // wheel and drag gestures reach the ScrollRect.
        viewport.GetComponent<Image>().raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Panel("Content", viewport, Color.clear, false);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, Mathf.Max(contentHeight, area.rect.height)); content.anchoredPosition = Vector2.zero;
        var bar = Panel("Scrollbar", area, new Color32(38, 58, 74, 140), true);
        Stretch(bar, new Vector2(1f, 0f), Vector2.one, new Vector2(-5f, 4f), new Vector2(0f, -4f));
        var handle = Panel("Handle", bar, new Color32(104, 150, 178, 210), true); Stretch(handle, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
        var sr = area.gameObject.AddComponent<ScrollRect>(); sr.viewport = viewport; sr.content = content; sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 24f; sr.verticalScrollbar = scrollbar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport; sr.verticalScrollbarSpacing = 2f;
        return content;
    }

    private void Empty(RectTransform area, string message)
    {
        var text = Label("Empty", area, message, 11, Muted, TextAnchor.MiddleCenter, false);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(22f, 22f), new Vector2(-22f, -22f));
    }

    private void Avatar(Transform parent, string username, float size, Vector2 pos, bool online)
    {
        var circle = Panel("Avatar", parent, new Color32(48, 74, 98, 255), true);
        var image = circle.GetComponent<Image>(); image.sprite = CircleSprite(); image.type = Image.Type.Simple;
        circle.anchorMin = circle.anchorMax = new Vector2(0f, 1f); circle.pivot = new Vector2(0f, 1f);
        circle.sizeDelta = new Vector2(size, size); circle.anchoredPosition = pos;
        AddBorder(circle, online ? Green : Border, 1.2f);
        string initial = string.IsNullOrWhiteSpace(username) ? "?" : username.Trim().Substring(0, 1).ToUpperInvariant();
        var text = Label("Initial", circle, initial, Mathf.RoundToInt(size * 0.4f), Ink, TextAnchor.MiddleCenter, true);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var dot = Panel("Presence", circle, online ? Green : Muted, true);
        dot.anchorMin = dot.anchorMax = new Vector2(1f, 0f); dot.pivot = new Vector2(1f, 0f);
        dot.sizeDelta = new Vector2(10f, 10f); dot.anchoredPosition = Vector2.zero;
    }

    private Sprite CircleSprite()
    {
        if (_circle != null) return _circle;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Social Circle" };
        tex.hideFlags = HideFlags.HideAndDontSave;
        float radius = size * 0.5f - 1f;
        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), center);
            float a = Mathf.Clamp01(radius + 1f - d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        _circle = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        _circle.hideFlags = HideFlags.HideAndDontSave;
        return _circle;
    }

    private void Badge(Transform parent, string value, Vector2 pos)
    {
        var badge = Panel("Badge", parent, Accent, true);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, 1f); badge.pivot = new Vector2(1f, 1f);
        badge.sizeDelta = new Vector2(28f, 20f); badge.anchoredPosition = pos;
        var text = Label("Text", badge, value, 9, new Color32(6, 32, 44, 255), TextAnchor.MiddleCenter, true, _mono);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }
}
