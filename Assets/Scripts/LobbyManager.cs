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
        _initTask ??= InitializeIfNeeded();
        await _initTask;

        if (AuthenticationService.Instance.IsSignedIn) return;
        _signInTask ??= AuthenticationService.Instance.SignInAnonymouslyAsync();
        await _signInTask;
    }

    private static async Task InitializeIfNeeded()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();
    }

    public static async Task<IHostSession> CreateLobbyAsync(string lobbyName, bool isPrivate, string ownerDisplayName)
    {
        await EnsureSignedInAsync();
        // No .WithRelayNetwork() yet: that requires a registered INetworkHandler (we're
        // deliberately not using Netcode for GameObjects/Entities - see file header), which
        // is the separate match-sync follow-up. Plain sessions are enough for lobby
        // create/browse/join/leave; the relay connection gets added alongside that handler.
        var options = new SessionOptions
        {
            Name = string.IsNullOrWhiteSpace(lobbyName) ? "Untitled Lobby" : lobbyName.Trim(),
            MaxPlayers = DefaultMaxPlayers,
            IsPrivate = isPrivate,
        };
        options.SessionProperties[OwnerNameKey] = new SessionProperty(
            string.IsNullOrWhiteSpace(ownerDisplayName) ? "Captain" : ownerDisplayName.Trim());

        var session = await MultiplayerService.Instance.CreateSessionAsync(options);
        CurrentSession = session;
        return session;
    }

    public static async Task<ISession> JoinByCodeAsync(string joinCode)
    {
        await EnsureSignedInAsync();
        var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim());
        CurrentSession = session;
        return session;
    }

    public static async Task<ISession> JoinByIdAsync(string sessionId)
    {
        await EnsureSignedInAsync();
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

    public static async Task LeaveCurrentAsync()
    {
        if (CurrentSession == null) return;
        try { await CurrentSession.LeaveAsync(); }
        catch (Exception ex) { Debug.LogWarning($"Leave lobby failed: {ex.Message}"); }
        CurrentSession = null;
    }
}
