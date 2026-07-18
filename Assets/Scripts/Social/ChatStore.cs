// One Piece TCG - persistent friend chat client (Cloudflare worker + D1, via SocialHttp).
//
// Poll-based DM history + unread badges, matching the ranked queue's request/response
// style. Friendship is enforced by the UI (the graph is UGS Friends); this store just
// sends/reads messages by player id. Never throws — degrades to empty on any failure.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public sealed class ChatMessage
{
    public long id;
    public string fromId;
    public string toId;
    public string body;
    public long createdAt;
    public long readAt;     // 0 when unread
    public bool mine;       // server-computed: this message was sent by me
}

[Serializable]
public sealed class ChatUnreadEntry
{
    public string fromId;
    public int count;
    public long lastId;
}

public static class ChatStore
{
    // ── JsonUtility response wrappers (no top-level arrays/dictionaries) ──
    [Serializable] private sealed class HistoryResponse { public ChatMessage[] messages; }
    [Serializable] private sealed class SendResponse { public bool ok; public long id; public long createdAt; }
    [Serializable] private sealed class PollResponse { public ChatUnreadEntry[] unread; public int total; }
    [Serializable] private sealed class SendReq { public string toId; public string body; }
    [Serializable] private sealed class WithReq { public string withId; }

    /// <summary>Conversation with `withId`, oldest-first, only messages newer than
    /// `sinceId` (pass the highest id you already hold to tail cheaply; 0 for all).</summary>
    public static async Task<List<ChatMessage>> HistoryAsync(string withId, long sinceId = 0)
    {
        var list = new List<ChatMessage>();
        if (string.IsNullOrEmpty(withId)) return list;
        string text = await SocialHttp.GetAsync($"/chat/history?withId={SocialHttp.Esc(withId)}&sinceId={sinceId}");
        if (text == null) return list;
        try
        {
            var wrap = JsonUtility.FromJson<HistoryResponse>(text);
            if (wrap?.messages != null) list.AddRange(wrap.messages);
        }
        catch (Exception ex) { Debug.LogWarning($"Chat history parse failed: {ex.Message}"); }
        return list;
    }

    /// <summary>Send a message. Returns the stored message (with server id/timestamp) or
    /// null on failure, so the caller can append it locally without a full re-fetch.</summary>
    public static async Task<ChatMessage> SendAsync(string toId, string body)
    {
        if (string.IsNullOrEmpty(toId) || string.IsNullOrWhiteSpace(body)) return null;
        string json = JsonUtility.ToJson(new SendReq { toId = toId, body = body });
        string text = await SocialHttp.PostAsync("/chat/send", json);
        if (text == null) return null;
        try
        {
            var wrap = JsonUtility.FromJson<SendResponse>(text);
            if (wrap != null && wrap.ok)
                return new ChatMessage { id = wrap.id, fromId = null, toId = toId, body = body, createdAt = wrap.createdAt, mine = true };
        }
        catch (Exception ex) { Debug.LogWarning($"Chat send parse failed: {ex.Message}"); }
        return null;
    }

    /// <summary>Mark all messages from `withId` as read (clears their unread badge).</summary>
    public static async Task MarkReadAsync(string withId)
    {
        if (string.IsNullOrEmpty(withId)) return;
        await SocialHttp.PostAsync("/chat/read", JsonUtility.ToJson(new WithReq { withId = withId }));
    }

    /// <summary>Unread counts grouped by sender, for badges. Empty on failure/guest.</summary>
    public static async Task<(List<ChatUnreadEntry> unread, int total)> PollUnreadAsync()
    {
        var list = new List<ChatUnreadEntry>();
        string text = await SocialHttp.GetAsync("/chat/poll");
        if (text == null) return (list, 0);
        try
        {
            var wrap = JsonUtility.FromJson<PollResponse>(text);
            if (wrap?.unread != null) list.AddRange(wrap.unread);
            return (list, wrap?.total ?? 0);
        }
        catch (Exception ex) { Debug.LogWarning($"Chat poll parse failed: {ex.Message}"); return (list, 0); }
    }
}
