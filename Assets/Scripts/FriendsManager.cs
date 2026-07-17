// One Piece TCG - Friend requests/list/block, built on Unity Gaming Services' Friends
// service (relationship graph + real-time presence) layered with our own username system
// for display. Sibling to AccountManager/LobbyManager, same static-class + EnsureReadyAsync
// idiom.
//
// Deliberately does NOT read the Friends service's own Relationship.Member.Profile.Name -
// that field is tied exclusively to Unity's separate Player Name service (the Name#1234
// discriminator system the account work rejected for uniqueness reasons). Every name shown
// here is resolved through our own claimed-username system instead, via the
// GetUsernamesForPlayers Cloud Code script and the playerUsernames reverse index
// ClaimUsername.js writes at claim time.
//
// API surface confirmed against the actual installed package (com.unity.services.friends
// 1.1.1, via IFriendsService's public members): AddFriendAsync (NOT SendFriendRequestAsync -
// an earlier draft of this file assumed that name from docs alone and it didn't compile),
// DeleteIncomingFriendRequestAsync, DeleteOutgoingFriendRequestAsync, DeleteFriendAsync,
// AddBlockAsync, DeleteBlockAsync, the Friends/IncomingFriendRequests/OutgoingFriendRequests
// properties, the RelationshipAdded/RelationshipDeleted/PresenceUpdated events, and
// Relationship.Member (singular - already resolved to "the other person", never yourself).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;
using UnityEngine;

public enum FriendActionFailureReason
{
    None,
    UsernameNotFound,
    SelfAdd,
    NoNetwork,
    Unknown,
}

public readonly struct FriendActionResult
{
    public bool Ok { get; }
    public FriendActionFailureReason Reason { get; }
    public string Message { get; }

    private FriendActionResult(bool ok, FriendActionFailureReason reason, string message)
    {
        Ok = ok;
        Reason = reason;
        Message = message;
    }

    public static FriendActionResult Success() => new FriendActionResult(true, FriendActionFailureReason.None, null);
    public static FriendActionResult Fail(FriendActionFailureReason reason, string message) => new FriendActionResult(false, reason, message);
}

public readonly struct FriendEntry
{
    public string PlayerId { get; }
    public string Username { get; }
    public bool Online { get; }

    public FriendEntry(string playerId, string username, bool online)
    {
        PlayerId = playerId;
        Username = username;
        Online = online;
    }
}

public static class FriendsManager
{
    // Raised on any relationship or presence change (ours or a friend's), so the UI can
    // repaint live instead of only refreshing on manual navigation - mirrors how
    // MainMenuManager already reacts to MatchNetworkSync.MatchStartReceived.
    public static event Action FriendsChanged;

    private static Task _friendsInitTask;
    private static string _friendsInitPlayerId;   // the player FriendsService was initialized FOR
    private static bool _friendsHandlersHooked;   // subscribe the relationship/presence events only once

    public static async Task EnsureReadyAsync()
    {
        await AccountManager.EnsureReadyAsync();

        // FriendsService binds to the auth player at init time. If the signed-in player has changed since
        // we last initialized (anonymous → account, a re-login, or an account switch), re-initialize for the
        // NEW player — otherwise we keep querying the previous player's (usually empty) friends/requests,
        // which reads to the user as "my friends vanished after a game".
        string current = CurrentPlayerId();
        if (_friendsInitTask != null && !string.Equals(_friendsInitPlayerId, current, StringComparison.Ordinal))
            _friendsInitTask = null;
        if (_friendsInitTask == null) _friendsInitPlayerId = current;

        _friendsInitTask ??= InitializeAndSubscribeAsync();
        await _friendsInitTask;
    }

    private static string CurrentPlayerId()
    {
        try { return AuthenticationService.Instance.PlayerId; } catch { return null; }
    }

    private static async Task InitializeAndSubscribeAsync()
    {
        // InitializeAsync is safe to call again after an auth player change; it rebinds the service to the
        // current player. Tolerate an "already initialized" style throw so a re-init for the new player
        // doesn't abort the whole flow.
        try { await FriendsService.Instance.InitializeAsync(); }
        catch (Exception ex) { Debug.LogWarning($"FriendsManager: FriendsService.InitializeAsync ({ex.Message}) — continuing."); }
        if (_friendsHandlersHooked) { await PublishOnlineAsync(); return; }
        _friendsHandlersHooked = true;
        FriendsService.Instance.RelationshipAdded += _ => FriendsChanged?.Invoke();
        FriendsService.Instance.RelationshipDeleted += _ => FriendsChanged?.Invoke();
        // PresenceUpdated fires whenever a friend's Availability changes (client opened/
        // closed) - routed into the same FriendsChanged repaint the relationship events use,
        // so the menu's online/offline dots update live without any extra plumbing.
        FriendsService.Instance.PresenceUpdated += _ => FriendsChanged?.Invoke();
        await PublishOnlineAsync();
    }

    // Publish our own availability as ONLINE so we show up green in friends' lists. Re-run after a re-init
    // for a new player so the account (not the old session) is the one shown online. The Friends service
    // flips us to OFFLINE automatically when the client's WebSocket drops (app quit/crash), so no explicit
    // sign-out call is required. Best-effort: in the editor / no-services / offline case this must never
    // throw to the caller — friends lists still work, we just appear offline to others.
    private static async Task PublishOnlineAsync()
    {
        try
        {
            await FriendsService.Instance.SetPresenceAsync(Availability.Online, new PresenceActivity());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"FriendsManager: couldn't set presence to Online ({ex.Message}) - continuing; we'll appear offline to friends.");
        }
    }

    // Minimal activity payload for SetPresenceAsync<T> — the SDK requires one even
    // when we only care about the Availability flag. Serializable so the Friends
    // service can round-trip it; friends' clients ignore it (we only read Availability).
    [Serializable]
    private sealed class PresenceActivity
    {
        public string status = "online";
    }

    public static async Task<FriendActionResult> SendFriendRequestByUsernameAsync(string username)
    {
        await EnsureReadyAsync();

        var (found, ownerId, _) = await AccountManager.LookupPlayerByUsernameAsync(username);
        if (!found) return FriendActionResult.Fail(FriendActionFailureReason.UsernameNotFound, "No player found with that name.");
        if (ownerId == AuthenticationService.Instance.PlayerId)
            return FriendActionResult.Fail(FriendActionFailureReason.SelfAdd, "You can't add yourself.");

        return await AddFriendRawAsync(ownerId);
    }

    // Accepting an incoming request IS adding them back - the Friends service auto-resolves
    // two mutual FRIEND_REQUESTs into a single FRIEND relationship, so there's no separate
    // "accept" call to make.
    public static Task<FriendActionResult> AcceptRequestAsync(string requesterPlayerId) => AddFriendRawAsync(requesterPlayerId);

    private static Task<FriendActionResult> AddFriendRawAsync(string playerId) =>
        RunAsync(() => FriendsService.Instance.AddFriendAsync(playerId));

    public static Task<FriendActionResult> DeclineRequestAsync(string requesterPlayerId) =>
        RunAsync(() => FriendsService.Instance.DeleteIncomingFriendRequestAsync(requesterPlayerId));

    public static Task<FriendActionResult> CancelOutgoingRequestAsync(string targetPlayerId) =>
        RunAsync(() => FriendsService.Instance.DeleteOutgoingFriendRequestAsync(targetPlayerId));

    public static Task<FriendActionResult> RemoveFriendAsync(string playerId) =>
        RunAsync(() => FriendsService.Instance.DeleteFriendAsync(playerId));

    public static Task<FriendActionResult> BlockAsync(string playerId) =>
        RunAsync(() => FriendsService.Instance.AddBlockAsync(playerId));

    public static Task<FriendActionResult> UnblockAsync(string playerId) =>
        RunAsync(() => FriendsService.Instance.DeleteBlockAsync(playerId));

    private static async Task<FriendActionResult> RunAsync(Func<Task> action)
    {
        await EnsureReadyAsync();
        try
        {
            await action();
            FriendsChanged?.Invoke();
            return FriendActionResult.Success();
        }
        catch (RequestFailedException ex)
        {
            // Covers both FriendsServiceException (its base type) and any other UGS
            // transport failure - the Friends service itself enforces the interesting
            // domain rules (max 10 pending requests, max 10 blocks, etc.) server-side,
            // so ex.Message already carries a human-readable reason.
            Debug.LogWarning($"Friends action failed: {ex.Message}");
            return FriendActionResult.Fail(FriendActionFailureReason.NoNetwork, ex.Message);
        }
    }

    public static async Task<List<FriendEntry>> GetFriendsAsync()
    {
        await EnsureReadyAsync();
        return await ResolveEntriesAsync(FriendsService.Instance.Friends);
    }

    public static async Task<List<FriendEntry>> GetIncomingRequestsAsync()
    {
        await EnsureReadyAsync();
        return await ResolveEntriesAsync(FriendsService.Instance.IncomingFriendRequests);
    }

    public static async Task<List<FriendEntry>> GetOutgoingRequestsAsync()
    {
        await EnsureReadyAsync();
        return await ResolveEntriesAsync(FriendsService.Instance.OutgoingFriendRequests);
    }

    private static async Task<List<FriendEntry>> ResolveEntriesAsync(IEnumerable<Relationship> relationships)
    {
        // Relationship.Member is singular - already resolved to "the other person" from the
        // current player's perspective, so no filtering-out-yourself is needed here.
        var others = relationships.Select(rel => rel.Member).ToList();

        var usernames = await LookupUsernamesAsync(others.Select(m => m.Id).ToList());

        var result = new List<FriendEntry>(others.Count);
        foreach (var m in others)
        {
            usernames.TryGetValue(m.Id, out var name);
            // Presence rides along on the Relationship snapshot; anything other than a
            // concrete Online availability (null presence, Offline, Invisible, service
            // hiccup) renders as offline - the safe default for the menu's dot.
            bool online = false;
            try { online = m.Presence != null && m.Presence.Availability == Availability.Online; }
            catch (Exception) { /* degrade to offline */ }
            result.Add(new FriendEntry(m.Id, name ?? "(unknown)", online));
        }
        return result;
    }

    // Not [Serializable] - CloudCodeService's deserializer is Newtonsoft-based, not Unity's
    // JsonUtility, so the attribute isn't needed here and Unity's serialization analyzer
    // otherwise flags the Dictionary field as an unsupported JsonUtility type.
    private class GetUsernamesResponse
    {
        public bool ok;
        public Dictionary<string, string> usernames;
    }

    private static async Task<Dictionary<string, string>> LookupUsernamesAsync(List<string> playerIds)
    {
        if (playerIds.Count == 0) return new Dictionary<string, string>();
        try
        {
            var response = await CloudCodeService.Instance.CallEndpointAsync<GetUsernamesResponse>(
                "GetUsernamesForPlayers", new Dictionary<string, object> { ["playerIds"] = playerIds });
            return response.usernames ?? new Dictionary<string, string>();
        }
        catch (RequestFailedException ex)
        {
            Debug.LogWarning($"GetUsernamesForPlayers failed: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
}
