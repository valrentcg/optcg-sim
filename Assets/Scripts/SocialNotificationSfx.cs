using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Session-scoped duplicate suppression for remote social notifications.  The social API is
/// poll-based, so the same unread message/invite is returned repeatedly until it is handled.
/// Keeping the gate independent of audio makes the important "incoming once" rule testable.
/// </summary>
public sealed class SocialNotificationCueGate
{
    private readonly HashSet<string> seenInviteIds = new HashSet<string>();
    private readonly Dictionary<string, long> latestMessageBySender = new Dictionary<string, long>();

    public bool ObserveInvite(string inviteId)
    {
        return !string.IsNullOrWhiteSpace(inviteId) && seenInviteIds.Add(inviteId);
    }

    public bool ObserveMessage(string senderId, long messageId, bool mine)
    {
        if (mine || string.IsNullOrWhiteSpace(senderId) || messageId <= 0) return false;
        if (latestMessageBySender.TryGetValue(senderId, out long latest) && messageId <= latest)
            return false;
        latestMessageBySender[senderId] = messageId;
        return true;
    }
}

/// <summary>
/// Global friend-message and lobby-invite cues.  Uses a dedicated persistent AudioSource, follows
/// the shared in-game SFX preference, and observes server ids so polling/refreshing cannot replay a
/// cue.  Local chat sends never enter the message path because ChatMessage.mine is rejected.
/// </summary>
public sealed class SocialNotificationSfx : MonoBehaviour
{
    private const string LobbyInviteClipName = "social_lobby_invite";
    private const string FriendMessageClipName = "social_friend_message";
    private const float ClipLoadTimeoutSeconds = 2f;

    private static SocialNotificationSfx instance;

    private readonly SocialNotificationCueGate gate = new SocialNotificationCueGate();
    private AudioSource source;
    private AudioClip lobbyInviteClip;
    private AudioClip friendMessageClip;

    public static void ObserveIncomingInvite(GameInvite invite)
    {
        if (invite == null || string.IsNullOrWhiteSpace(invite.id)) return;
        Ensure();
        if (instance != null && instance.gate.ObserveInvite(invite.id))
            instance.PlayWhenReady(() => instance.lobbyInviteClip);
    }

    public static void EnsureRunning() => Ensure();

    /// <summary>Observe the latest unread id from each remote sender. One poll can contain several
    /// new senders/messages, but it produces one notification sound rather than a loud stack.</summary>
    public static void ObserveUnreadSnapshot(IEnumerable<ChatUnreadEntry> unread)
    {
        if (unread == null) return;
        Ensure();
        if (instance == null) return;
        bool anyNew = false;
        foreach (var entry in unread)
            if (entry != null && instance.gate.ObserveMessage(entry.fromId, entry.lastId, mine: false))
                anyNew = true;
        if (anyNew) instance.PlayWhenReady(() => instance.friendMessageClip);
    }

    /// <summary>Observe actual tail-polled conversation messages. This covers an open chat whose
    /// messages are immediately marked read and may therefore never appear in the unread snapshot.</summary>
    public static void ObserveIncomingMessages(IEnumerable<ChatMessage> messages)
    {
        if (messages == null) return;
        Ensure();
        if (instance == null) return;
        bool anyNew = false;
        foreach (var message in messages)
            if (message != null && instance.gate.ObserveMessage(message.fromId, message.id, message.mine))
                anyNew = true;
        if (anyNew) instance.PlayWhenReady(() => instance.friendMessageClip);
    }

    private static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("Social Notification SFX");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<SocialNotificationSfx>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        StartCoroutine(LoadClip(LobbyInviteClipName, clip => lobbyInviteClip = clip));
        StartCoroutine(LoadClip(FriendMessageClipName, clip => friendMessageClip = clip));
        PollUnreadOutsideMainMenu();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private static IEnumerator LoadClip(string clipName, System.Action<AudioClip> assign)
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "sfx", clipName + ".wav");
        if (!System.IO.File.Exists(path)) yield break;
        using (var request = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(
                   new System.Uri(path).AbsoluteUri, AudioType.WAV))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                assign(UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request));
            else
                Debug.LogWarning($"Could not load social SFX '{clipName}': {request.error}");
        }
    }

    private void PlayWhenReady(System.Func<AudioClip> clip)
    {
        var ready = clip();
        if (ready != null)
        {
            source.PlayOneShot(ready, GameManager.SfxVolume);
            return;
        }
        StartCoroutine(PlayWhenReadyRoutine(clip));
    }

    private IEnumerator PlayWhenReadyRoutine(System.Func<AudioClip> clip)
    {
        float deadline = Time.unscaledTime + ClipLoadTimeoutSeconds;
        while (clip() == null && Time.unscaledTime < deadline) yield return null;
        var ready = clip();
        if (ready != null && source != null)
            source.PlayOneShot(ready, GameManager.SfxVolume);
    }

    /// <summary>The menu already owns the full social poll because it also paints badges and
    /// conversations. Once a match replaces that menu, keep only the lightweight unread-message
    /// poll alive so a friend message still produces its cue during play. Lobby invites are
    /// deliberately not polled here: current-match invitations must remain suppressed.</summary>
    private async void PollUnreadOutsideMainMenu()
    {
        while (this != null)
        {
            await Task.Delay(3000);
            if (this == null) return;
            if (FindAnyObjectByType<MainMenuManager>() != null) continue;
            try
            {
                var (ok, unread, _) = await ChatStore.PollUnreadAsync();
                if (this == null) return;
                if (ok) ObserveUnreadSnapshot(unread);
            }
            catch
            {
                // Transient network/auth failures are retried on the next quiet poll.
            }
        }
    }
}
