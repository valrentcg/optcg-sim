// One Piece TCG - Private/public lobby hosting via Unity Gaming Services' unified
// Multiplayer Services SDK (Sessions API: Unity.Services.Multiplayer).
// This is the session/data layer only - it creates, browses, and joins lobbies and
// exposes their metadata (name, owner, player count, join code). It does NOT yet sync
// gameplay between the two connected players; that's a separate follow-up that will
// implement INetworkHandler (see com.unity.services.multiplayer's own interface of that
// name) to ship GameCommand messages over the session's Relay connection.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

public static class LobbyManager
{
    private const string OwnerNameKey = "ownerName";
    private const string CustomRulesKey = "customRules";
    private const string HostLeaderKey = "hostLeaderId";
    // 1v1 for now; spectator slots are a later addition once match networking exists.
    private const int DefaultMaxPlayers = 2;

    public static ISession CurrentSession { get; private set; }

    private static Task _initTask;
    private static Task _signInTask;
    // A finished match can rebuild the menu before the UGS LeaveAsync request completes.
    // Every subsequent create/join/queue entry awaits this shared task so the old leave cannot
    // finish late and shut down the NEW session's NetworkManager underneath it.
    private static Task _leaveTask = Task.CompletedTask;
    private static readonly SemaphoreSlim _rulesSaveGate = new SemaphoreSlim(1, 1);
    private static ISession _rulesPublishedSession;
    private static string _rulesPublishedValue;
    private static string _leaderPublishedValue;

    // AuthenticationService.Instance throws ("Singleton is not initialized") if touched
    // before UnityServices.InitializeAsync() has completed, so that must always run first
    // and be awaited - it cannot be guarded by checking AuthenticationService.Instance itself.
    public static async Task EnsureSignedInAsync()
    {
        await EnsureServicesInitializedAsync();

        if (AuthenticationService.Instance.IsSignedIn) return;
        // NOT ??= : after a SignOut the cached task is completed-but-useless, and
        // reusing it means no re-sign-in ever happens - every Cloud Code call then
        // fails with "Player ID is missing". Start fresh whenever the cached task
        // can no longer produce a signed-in session.
        if (_signInTask == null || _signInTask.IsCompleted)
            _signInTask = AuthenticationService.Instance.SignInAnonymouslyAsync();
        await _signInTask;
    }

    // Split out from EnsureSignedInAsync so AccountManager's email/password sign-in
    // (which requires AuthenticationState.SignedOut, incompatible with an anonymous
    // session already being active) can init services without forcing anonymous sign-in.
    public static async Task EnsureServicesInitializedAsync()
    {
        _initTask ??= InitializeIfNeeded();
        await _initTask;
    }

    private static async Task InitializeIfNeeded()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();
        // NOTE: NetworkManager creation deliberately does NOT happen here. A
        // NetworkManager that exists but never starts a session throws a
        // NullReferenceException from NetworkSceneManager.Dispose on app quit
        // (Netcode-for-GameObjects teardown bug), and this method runs on plain
        // boot sign-in - menu-only sessions would pay that cost every run.
        // EnsureNetworkManager() is called just-in-time in CreateLobbyAsync /
        // JoinByCodeAsync / JoinByIdAsync instead, which is still before the
        // Sessions SDK's Netcode handler needs NetworkManager.Singleton.
    }

    public static async Task<IHostSession> CreateLobbyAsync(string lobbyName, bool isPrivate,
        string ownerDisplayName, string publicRulesSummary = null, string publicHostLeaderId = null)
    {
        await LeaveCurrentAsync();
        await EnsureSignedInAsync();
        NetworkBootstrap.EnsureNetworkManager(); // must exist before CreateSessionAsync
        // Netcode for GameObjects is installed, so .WithRelayNetwork() auto-wires
        // NetworkManager/UnityTransport/Relay - no custom INetworkHandler needed.
        var options = new SessionOptions
        {
            Name = string.IsNullOrWhiteSpace(lobbyName) ? "Untitled Lobby" : lobbyName.Trim(),
            MaxPlayers = DefaultMaxPlayers,
            IsPrivate = isPrivate,
        }.WithRelayNetwork();
        options.SessionProperties[OwnerNameKey] = new SessionProperty(
            string.IsNullOrWhiteSpace(ownerDisplayName) ? "Captain" : ownerDisplayName.Trim());
        if (!isPrivate && !string.IsNullOrWhiteSpace(publicRulesSummary))
            options.SessionProperties[CustomRulesKey] = new SessionProperty(publicRulesSummary.Trim());
        if (!isPrivate && !string.IsNullOrWhiteSpace(publicHostLeaderId))
            options.SessionProperties[HostLeaderKey] = new SessionProperty(publicHostLeaderId.Trim());

        var session = await MultiplayerService.Instance.CreateSessionAsync(options);
        CurrentSession = session;
        _rulesPublishedSession = session;
        _rulesPublishedValue = !isPrivate ? publicRulesSummary?.Trim() : null;
        _leaderPublishedValue = !isPrivate ? publicHostLeaderId?.Trim() : null;
        return session;
    }

    public static async Task<ISession> JoinByCodeAsync(string joinCode)
    {
        await LeaveCurrentAsync();
        await EnsureSignedInAsync();
        NetworkBootstrap.EnsureNetworkManager(); // must exist before JoinSessionByCodeAsync
        var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim());
        CurrentSession = session;
        return session;
    }

    public static async Task<ISession> JoinByIdAsync(string sessionId)
    {
        await LeaveCurrentAsync();
        await EnsureSignedInAsync();
        NetworkBootstrap.EnsureNetworkManager(); // must exist before JoinSessionByIdAsync
        var session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
        CurrentSession = session;
        return session;
    }

    public static async Task<List<ISessionInfo>> BrowsePublicLobbiesAsync()
    {
        await EnsureSignedInAsync();
        var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
        return new List<ISessionInfo>(results.Sessions);
    }

    public static string GetOwnerName(ISessionInfo info) =>
        info?.Properties != null && info.Properties.TryGetValue(OwnerNameKey, out var prop) ? prop.Value : "Unknown";

    public static string GetOwnerName(ISession session) =>
        session?.Properties != null && session.Properties.TryGetValue(OwnerNameKey, out var prop) ? prop.Value : "Unknown";

    public static string GetCustomRules(ISessionInfo info) =>
        info?.Properties != null && info.Properties.TryGetValue(CustomRulesKey, out var prop)
            && !string.IsNullOrWhiteSpace(prop?.Value)
            ? prop.Value : "Custom rules set by host";

    public static string GetHostLeaderId(ISessionInfo info) =>
        info?.Properties != null && info.Properties.TryGetValue(HostLeaderKey, out var prop)
            && !string.IsNullOrWhiteSpace(prop?.Value)
            ? prop.Value : null;

    // Public previews expose rules and the host's chosen leader ID, never a deck
    // list. Serialize changes so a fast sequence of host choices cannot save
    // older metadata after newer metadata. Failures never interrupt NGO play.
    public static async Task UpdatePublicPreviewAsync(string publicRulesSummary, string hostLeaderId)
    {
        var session = CurrentSession;
        if (session == null || !session.IsHost || session.IsPrivate) return;
        await _rulesSaveGate.WaitAsync();
        try
        {
            if (CurrentSession != session) return;
            var host = session.AsHost();
            string rules = string.IsNullOrWhiteSpace(publicRulesSummary) ? null : publicRulesSummary.Trim();
            string leader = string.IsNullOrWhiteSpace(hostLeaderId) ? null : hostLeaderId.Trim();
            if (_rulesPublishedSession == session && _rulesPublishedValue == rules
                && _leaderPublishedValue == leader) return;
            host.SetProperty(CustomRulesKey, rules == null ? null : new SessionProperty(rules));
            host.SetProperty(HostLeaderKey, leader == null ? null : new SessionProperty(leader));
            await host.SavePropertiesAsync();
            _rulesPublishedSession = session;
            _rulesPublishedValue = rules;
            _leaderPublishedValue = leader;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not update public lobby preview: {ex.Message}");
        }
        finally { _rulesSaveGate.Release(); }
    }

    /// <summary>The opponent's UGS player id in the current 1v1 session (the player
    /// that isn't us), or null if unavailable. Used to bind a ranked match report to
    /// both sides so the server can cross-check the two halves.</summary>
    public static string OpponentPlayerId()
    {
        try
        {
            var s = CurrentSession;
            if (s?.Players == null) return null;
            string me = AuthenticationService.Instance.PlayerId;
            foreach (var pl in s.Players)
                if (pl != null && !string.IsNullOrEmpty(pl.Id) && pl.Id != me) return pl.Id;
        }
        catch (Exception ex) { Debug.LogWarning($"OpponentPlayerId failed: {ex.Message}"); }
        return null;
    }

    public static Task LeaveCurrentAsync()
    {
        if (_leaveTask != null && !_leaveTask.IsCompleted) return _leaveTask;

        // Detach immediately so freshly rebuilt menu code cannot mistake this for a usable
        // lobby while the service request is still in flight. Capture the exact old session;
        // the continuation must never clear or leave a newer session.
        var leaving = CurrentSession;
        CurrentSession = null;
        ShutdownNetwork();
        _leaveTask = LeaveCapturedSessionAsync(leaving);
        return _leaveTask;
    }

    private static async Task LeaveCapturedSessionAsync(ISession leaving)
    {
        if (leaving == null) return;
        try { await leaving.LeaveAsync(); }
        catch (Exception ex) { Debug.LogWarning($"Leave lobby failed: {ex.Message}"); }
        // Network teardown happened synchronously before the await. Do not repeat it here:
        // by now another guarded create/join may be preparing its own NetworkManager.
    }

    /// <summary>Tear down the Netcode connection left over from a match. Leaving the UGS
    /// session does NOT shut down NetworkManager — it stays connected to the finished
    /// match's Relay, and the Sessions SDK then refuses to start a new one
    /// ("NetworkManager is already connected"), so the 2nd networked match of any app run
    /// silently hangs on "Connecting to your opponent…". Shut it down here so the next
    /// Create/Join starts from a clean transport. Runs on match-end and queue-cancel.</summary>
    public static void ShutdownNetwork()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (!nm.IsListening && !nm.IsConnectedClient && !nm.IsServer) return;
        try { nm.Shutdown(); }
        catch (Exception ex) { Debug.LogWarning($"Netcode shutdown failed: {ex.Message}"); }
        // Shutdown() destroys the CustomMessagingManager; the next match rebuilds it WITHOUT our named-
        // message handlers. Clear the registration guard so OnClientConnected re-registers them next time —
        // otherwise every match after the first drops all messages and hangs on "Connecting…".
        MatchNetworkSync.ResetHandlerRegistration();
    }
}
