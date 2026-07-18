// One Piece TCG - quick game invites client (Cloudflare worker + D1, via SocialHttp).
//
// The inviter hosts a UGS custom session (LobbyManager.CreateLobbyAsync), then sends its
// session id here; the invitee polls, accepts, and JoinByIdAsync's it — the same
// "host publishes session id, guest joins by id" handshake the ranked matchmaker uses.
// Poll-based, never throws.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public sealed class GameInvite
{
    public string id;
    public string fromId;
    public string fromName;
    public string sessionId;   // UGS session id to JoinByIdAsync
    public string lobbyName;
    public long createdAt;
}

public static class InviteStore
{
    [Serializable] private sealed class PollResponse { public GameInvite[] invites; }
    [Serializable] private sealed class SendResponse { public bool ok; public string inviteId; }
    [Serializable] private sealed class RespondResponse { public bool ok; public string status; public string sessionId; }
    [Serializable] private sealed class StatusResponse { public string status; }
    [Serializable] private sealed class SendReq { public string toId; public string sessionId; public string lobbyName; public string fromName; }
    [Serializable] private sealed class RespondReq { public string inviteId; public bool accept; }
    [Serializable] private sealed class IdReq { public string inviteId; }

    /// <summary>Invite `toId` into my custom lobby `sessionId`. Returns the invite id
    /// (used to poll its status / cancel) or null on failure.</summary>
    public static async Task<string> SendAsync(string toId, string sessionId, string lobbyName, string fromName)
    {
        if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(sessionId)) return null;
        string json = JsonUtility.ToJson(new SendReq { toId = toId, sessionId = sessionId, lobbyName = lobbyName, fromName = fromName });
        string text = await SocialHttp.PostAsync("/invite/send", json);
        if (text == null) return null;
        try { var wrap = JsonUtility.FromJson<SendResponse>(text); return wrap != null && wrap.ok ? wrap.inviteId : null; }
        catch (Exception ex) { Debug.LogWarning($"Invite send parse failed: {ex.Message}"); return null; }
    }

    /// <summary>Pending invites addressed to me (freshest first). Empty on failure/guest.</summary>
    public static async Task<List<GameInvite>> PollAsync()
    {
        var list = new List<GameInvite>();
        string text = await SocialHttp.GetAsync("/invite/poll");
        if (text == null) return list;
        try { var wrap = JsonUtility.FromJson<PollResponse>(text); if (wrap?.invites != null) list.AddRange(wrap.invites); }
        catch (Exception ex) { Debug.LogWarning($"Invite poll parse failed: {ex.Message}"); }
        return list;
    }

    /// <summary>Accept/decline an invite. On accept, returns the session id to join.</summary>
    public static async Task<(bool ok, string sessionId)> RespondAsync(string inviteId, bool accept)
    {
        if (string.IsNullOrEmpty(inviteId)) return (false, null);
        string json = JsonUtility.ToJson(new RespondReq { inviteId = inviteId, accept = accept });
        string text = await SocialHttp.PostAsync("/invite/respond", json);
        if (text == null) return (false, null);
        try
        {
            var wrap = JsonUtility.FromJson<RespondResponse>(text);
            bool accepted = wrap != null && wrap.status == "accepted";
            return (accepted, accepted ? wrap.sessionId : null);
        }
        catch (Exception ex) { Debug.LogWarning($"Invite respond parse failed: {ex.Message}"); return (false, null); }
    }

    /// <summary>Inviter polls the status of an invite it sent: pending | accepted |
    /// declined | cancelled | expired | unknown.</summary>
    public static async Task<string> StatusAsync(string inviteId)
    {
        if (string.IsNullOrEmpty(inviteId)) return "unknown";
        string text = await SocialHttp.GetAsync($"/invite/status?inviteId={SocialHttp.Esc(inviteId)}");
        if (text == null) return "unknown";
        try { var wrap = JsonUtility.FromJson<StatusResponse>(text); return string.IsNullOrEmpty(wrap?.status) ? "unknown" : wrap.status; }
        catch { return "unknown"; }
    }

    /// <summary>Inviter withdraws a pending invite.</summary>
    public static async Task CancelAsync(string inviteId)
    {
        if (string.IsNullOrEmpty(inviteId)) return;
        await SocialHttp.PostAsync("/invite/cancel", JsonUtility.ToJson(new IdReq { inviteId = inviteId }));
    }
}
