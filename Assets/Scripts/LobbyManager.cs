// One Piece TCG - Private/public lobby hosting via Unity Gaming Services' unified
// Multiplayer Services SDK (Sessions API: Unity.Services.Multiplayer).
// This is the session/data layer only - it creates, browses, and joins lobbies and
// exposes their metadata (name, owner, player count, join code). It does NOT yet sync
// gameplay between the two connected players; that's a separate follow-up that will
// implement INetworkHandler (see com.unity.services.multiplayer's own interface of that
// name) to ship GameCommand messages over the session's Relay connection.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

public static class LobbyManager
{
    private const string OwnerNameKey = "ownerName";
    // 1v1 for now; spectator slots are a later addition once match networking exists.
    private const int DefaultMaxPlayers = 2;

    public static ISession CurrentSession { get; private set; }

    private static Task _initTask;
    private static Task _signInTask;

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

    public static async Task<IHostSession> CreateLobbyAsync(string lobbyName, bool isPrivate, string ownerDisplayName)
    {
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

        var session = await MultiplayerService.Instance.CreateSessionAsync(options);
        CurrentSession = session;
        return session;
    }

    public static async Task<ISession> JoinByCodeAsync(string joinCode)
    {
        await EnsureSignedInAsync();
        NetworkBootstrap.EnsureNetworkManager(); // must exist before JoinSessionByCodeAsync
        var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim());
        CurrentSession = session;
        return session;
    }

    public static async Task<ISession> JoinByIdAsync(string sessionId)
    {
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

    public static async Task LeaveCurrentAsync()
    {
        if (CurrentSession == null) return;
        try { await CurrentSession.LeaveAsync(); }
        catch (Exception ex) { Debug.LogWarning($"Leave lobby failed: {ex.Message}"); }
        CurrentSession = null;
    }
}
