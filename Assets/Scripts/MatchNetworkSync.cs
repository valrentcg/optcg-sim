// One Piece TCG - Thin match-command transport over Netcode for GameObjects'
// CustomMessagingManager (named byte messages sent directly through NetworkManager,
// no NetworkObject/prefab/NetworkVariable needed).
//
// Deliberately not using NetworkObjects: there's nothing to spawn or sync as a
// GameObject - the two players share one GameState by replaying the same
// deterministic GameCommand log on both sides (see GameEngine.CreateMatch/
// ApplyCommand and ReplayStore.cs, which already relies on that determinism).
// NGO exists purely to reuse Unity's tested Relay/connection handling, which
// com.unity.services.multiplayer wires up automatically for us via
// SessionOptions.WithRelayNetwork() once this package is present.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using OnePieceTcg.Engine;

// Wire-format snapshot of one player's deck (leader + main-deck counts). Sent by
// the guest to the host when they pick a deck in the lobby, and embedded (both
// seats) in the match-start payload so each client can build an identical match
// without either side needing the other's deck to exist in its local DeckStore.
[Serializable]
public class NetworkDeckEntry { public string id; public int count; }

[Serializable]
public class NetworkDeck
{
    public string id;
    public string name;
    public string leader;
    public NetworkDeckEntry[] cards;

    public static NetworkDeck From(DeckData d)
    {
        if (d == null) return null;
        var entries = new List<NetworkDeckEntry>();
        if (d.cards != null)
            foreach (var e in d.cards)
                if (e != null && e.count > 0) entries.Add(new NetworkDeckEntry { id = e.id, count = e.count });
        return new NetworkDeck { id = d.id, name = d.name, leader = d.leaderId, cards = entries.ToArray() };
    }

    public DeckDef ToDeckDef()
    {
        if (string.IsNullOrEmpty(leader)) return null;
        var list = new List<(string cardId, int qty)> { (leader, 1) };
        if (cards != null)
            foreach (var e in cards)
                if (e != null && e.count > 0) list.Add((e.id, e.count));
        return new DeckDef
        {
            Id = string.IsNullOrEmpty(id) ? "net" : id,
            Name = string.IsNullOrEmpty(name) ? "Custom Deck" : name,
            Leader = leader,
            List = list,
        };
    }
}

// Everything both clients need to start the same match: the shared seed plus both
// seats' decks. Serialized with JsonUtility over the existing match-start message.
[Serializable]
public class MatchStartPayload
{
    public string seed;
    public NetworkDeck south;   // host's deck
    public NetworkDeck north;   // guest's deck
    public bool ranked;         // true = a Ranked-queue match that counts toward the bounty ladder
    public string mode;         // "ranked" | "casual" | "custom" — the game type for stats/history
    public bool forgiveness;    // custom lobby: enable the in-match rewind toggle (opponent-approved)
    public string format;       // "standard" | "extra" — card-format legality (custom lobby; casual/ranked = standard)
    public BlitzConfig blitz;   // custom lobby: timed-match settings (null/Standard = untimed)
    // Host's build number (UpdateChecker.CurrentBuildNumber). The guest aborts if it differs from
    // its own — two builds can have divergent engine logic, and both clients replay the SAME
    // GameCommand log, so a version mismatch would silently desync into different boards. Absent
    // field (old client) deserializes to 0, which correctly mismatches any real build.
    public int build;
}

/// <summary>Host -> guest broadcast of a custom lobby's rules, so the guest can SEE what they're joining
/// (format, forgiveness, timing) and apply the format to their own deck grey-out before picking.</summary>
[Serializable]
public class LobbySettingsPayload
{
    public string format = "standard";   // "standard" | "extra"
    public bool forgiveness;             // rewind toggle enabled
    public string timing = "Untimed";    // human-readable timing summary (per-player clocks etc.)
}

// Lightweight "what am I looking at" state each client streams to its opponent while
// playing: which board card is being hovered (opponent renders a gold glow on it) and
// which face-down hand cards are being lifted/inspected. Sent over an Unreliable
// channel - it's pure fire-and-forget cosmetic state, so lost or stale packets are fine
// (the next send overwrites). Serialized with JsonUtility like everything else here.
// Forgiveness-mode rewind negotiation. The requester proposes rewinding to command index `cursor`
// (kind = "action" | "turn", for the prompt wording); the opponent replies accept/decline. On
// accept BOTH clients truncate their (identical) CommandHistory to `cursor` and resimulate.
[Serializable]
public class RewindRequestPayload
{
    public int cursor;
    public string kind = "action";
}

[Serializable]
public class RewindResponsePayload
{
    public bool accept;
    public int cursor;
}

[Serializable]
public class PresencePayload
{
    public string hoverCardId;        // instance/card id being hovered, or null/empty for none
    public int[] raisedHandIndexes;   // indexes of the sender's hand cards currently lifted
    public int[] donGroups;           // sender's DON!! group partition (empty/null = ungrouped)
    // Live hand-reorder: the sender is dragging the card at slot handDragFrom toward slot handDragTo
    // within their own hand. Both -1 when not reordering. (Sent every frame, so idle = -1/-1.)
    public int handDragFrom = -1;
    public int handDragTo = -1;
}

public static class MatchNetworkSync
{
    private const string MatchStartMessage = "OptcgMatchStart";
    private const string GameCommandMessage = "OptcgGameCmd";
    private const string DeckShareMessage = "OptcgDeckShare";
    private const string ChatMessage = "OptcgChat";
    private const string NameShareMessage = "OptcgNameShare";
    private const string PresenceMessage = "OptcgPresence";
    private const string RematchReqMessage = "OptcgRematchReq";   // "I want a rematch"
    private const string RematchGoMessage = "OptcgRematchGo";     // host: "rematch on, here's the seed"
    private const string RewindReqMessage = "OptcgRewindReq";     // "can we rewind to cursor N?"
    private const string RewindRespMessage = "OptcgRewindResp";   // "accept/decline your rewind"
    private const string ReadyMessage = "OptcgReady";             // custom lobby: "I am / am not ready"
    private const string LobbySettingsMessage = "OptcgLobbySet";  // host -> guest: format + custom-rule details

    // Chat messages longer than this are truncated before sending (UI should enforce the
    // same cap on its input field; this is the transport-level backstop).
    public const int MaxChatLength = 500;

    // Presence rate limit: at most ~10 sends/sec. Timestamp is caller-supplied
    // (UnityEngine.Time.unscaledTime from the UI layer) so tests can drive it directly.
    private const float PresenceMinInterval = 0.1f;
    private static float lastPresenceSendTime = float.NegativeInfinity;

    private static bool handlersRegistered;

    public static event Action<MatchStartPayload> MatchStartReceived;
    public static event Action<GameCommand> CommandReceived;
    public static event Action<NetworkDeck> DeckShareReceived;   // peer told us which deck they'll play
    public static event Action<string> ChatReceived;             // peer sent an in-match chat line
    public static event Action<string> PeerNameReceived;         // peer told us their display name
    public static event Action<PresencePayload> PresenceReceived; // peer's hover/raised-hand state
    public static event Action RematchRequested;                 // peer clicked "Rematch" (custom match)
    public static event Action<string> RematchStartReceived;     // host published the rematch seed
    public static event Action<RewindRequestPayload> RewindRequested;   // peer asked to rewind
    public static event Action<RewindResponsePayload> RewindResponded;  // peer answered our rewind ask
    public static event Action<bool> ReadyReceived;                     // peer toggled their lobby Ready state
    public static event Action<LobbySettingsPayload> LobbySettingsReceived; // host told us the lobby's rules

    /// <summary>Call once, right after the NetworkManager singleton is created.</summary>
    public static void EnsureHandlersRegistered()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientConnectedCallback += OnClientConnected;
    }

    /// <summary>Reset after a Netcode shutdown. Shutdown() destroys the CustomMessagingManager, and the
    /// NEXT StartHost/StartClient builds a fresh one WITHOUT our named-message handlers — but the
    /// registration is guarded by the static <c>handlersRegistered</c>, which survives the shutdown. Without
    /// this reset, every match after the first re-uses that stale "true" and OnClientConnected skips
    /// registration, so match-start/deck/name messages are silently dropped and both clients hang on
    /// "Connecting you to your opponent…". Called from LobbyManager.ShutdownNetwork.</summary>
    public static void ResetHandlerRegistration() => handlersRegistered = false;

    // CustomMessagingManager is only populated once NetworkManager finishes starting
    // (StartHost/StartClient), so handler registration has to wait for that - this
    // fires for both the host's own local connection and, later, the joining guest.
    private static void OnClientConnected(ulong clientId)
    {
        if (handlersRegistered) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;

        nm.CustomMessagingManager.RegisterNamedMessageHandler(MatchStartMessage, OnMatchStartMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(GameCommandMessage, OnGameCommandMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(DeckShareMessage, OnDeckShareMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(ChatMessage, OnChatMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(NameShareMessage, OnNameShareMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(PresenceMessage, OnPresenceMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(RematchReqMessage, OnRematchReqMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(RematchGoMessage, OnRematchGoMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(RewindReqMessage, OnRewindReqMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(RewindRespMessage, OnRewindRespMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReadyMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(LobbySettingsMessage, OnLobbySettingsMessage);
        handlersRegistered = true;
    }

    // Returns null (rather than silently falling back to ServerClientId/self) when no peer
    // is actually connected yet, so a send attempted too early fails loudly instead of the
    // host quietly messaging itself - which looks exactly like "the other player never got it."
    private static ulong? GetPeerClientId()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return null;
        if (!nm.IsHost) return NetworkManager.ServerClientId;
        foreach (var id in nm.ConnectedClientsIds)
            if (id != nm.LocalClientId) return id;
        return null;
    }

    /// <summary>True once Netcode itself has a peer connected (not just the Lobby's player count).</summary>
    public static bool IsPeerConnected => GetPeerClientId().HasValue;

    // ---- Sending ----

    public static void SendMatchStart(MatchStartPayload payload)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || payload == null || string.IsNullOrEmpty(payload.seed)) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendMatchStart: no connected peer yet - not sent."); return; }
        string json = JsonUtility.ToJson(payload);
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        // Fragmented: the payload carries two full decklists (~2KB+), well past the
        // ~1264-byte single-packet cap of plain ReliableSequenced.
        nm.CustomMessagingManager.SendNamedMessage(MatchStartMessage, target.Value, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    /// <summary>Tell the peer which deck we'll be playing (lobby deck selection).</summary>
    public static void SendDeckShare(NetworkDeck deck)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || deck == null) return;
        var target = GetPeerClientId();
        if (target == null) return;   // not connected yet - lobby re-sends on peer connect
        string json = JsonUtility.ToJson(deck);
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        // Fragmented for the same reason as SendMatchStart: one decklist can exceed a packet.
        nm.CustomMessagingManager.SendNamedMessage(DeckShareMessage, target.Value, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    public static void SendCommand(GameCommand command)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || command == null) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendCommand: no connected peer yet - not sent."); return; }
        string json = JsonUtility.ToJson(SerializableCommand.From(command));
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        nm.CustomMessagingManager.SendNamedMessage(GameCommandMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>
    /// Send an in-match chat line to the opponent. Text is trimmed to MaxChatLength (500)
    /// chars before sending. Reliable + fragmented so a max-length UTF-16-heavy message
    /// can't exceed a single packet. Silently drops (with a warning) if no peer is
    /// connected yet - chat before the guest joins has nowhere to go.
    /// </summary>
    public static void SendChat(string text)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || string.IsNullOrEmpty(text)) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendChat: no connected peer yet - not sent."); return; }
        if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);
        using var writer = new FastBufferWriter(text.Length * 4 + 32, Allocator.Temp);
        writer.WriteValueSafe(text);
        nm.CustomMessagingManager.SendNamedMessage(ChatMessage, target.Value, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    /// <summary>Tell the peer our display name (for the in-match turn indicator / chat).
    /// Sent on peer connect, like the lobby's deck share. Capped at 40 chars.</summary>
    public static void SendPeerName(string name)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || string.IsNullOrWhiteSpace(name)) return;
        var target = GetPeerClientId();
        if (target == null) return;   // lobby re-sends on peer connect
        name = name.Trim();
        if (name.Length > 40) name = name.Substring(0, 40);
        using var writer = new FastBufferWriter(name.Length * 4 + 32, Allocator.Temp);
        writer.WriteValueSafe(name);
        nm.CustomMessagingManager.SendNamedMessage(NameShareMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>
    /// Stream our hover/raised-hand state to the opponent. Fire-and-forget (Unreliable):
    /// callers may invoke this every frame while state changes; sends are rate-limited to
    /// ~10/sec via the caller-supplied timestamp (pass UnityEngine.Time.unscaledTime -
    /// parameterized so the deterministic engine layer stays Unity-free and tests can
    /// drive the clock). Over-rate calls and calls with no peer are silently dropped.
    /// </summary>
    public static void SendPresence(PresencePayload p, float now)
    {
        if (p == null) return;
        if (now - lastPresenceSendTime < PresenceMinInterval) return;   // rate limit
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        var target = GetPeerClientId();
        if (target == null) return;   // cosmetic state - nothing to warn about pre-connect
        lastPresenceSendTime = now;
        string json = JsonUtility.ToJson(p);
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        // Unreliable: stale/dropped packets are fine, the next send overwrites the state.
        nm.CustomMessagingManager.SendNamedMessage(PresenceMessage, target.Value, writer, NetworkDelivery.Unreliable);
    }

    /// <summary>Convenience overload using UnityEngine.Time.unscaledTime as the clock.</summary>
    public static void SendPresence(PresencePayload p) => SendPresence(p, Time.unscaledTime);

    /// <summary>Tell the peer we want a rematch (custom match). When both sides have asked,
    /// the host publishes a shared seed via SendRematchStart and both restart in place.</summary>
    public static void SendRematchRequest()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendRematchRequest: no peer - not sent."); return; }
        using var writer = new FastBufferWriter(8, Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        nm.CustomMessagingManager.SendNamedMessage(RematchReqMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Tell the peer whether we've readied up in the custom lobby (both must be ready to start).</summary>
    public static void SendReady(bool ready)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        var target = GetPeerClientId();
        if (target == null) return;   // re-sent on peer connect by the lobby
        using var writer = new FastBufferWriter(8, Allocator.Temp);
        writer.WriteValueSafe((byte)(ready ? 1 : 0));
        nm.CustomMessagingManager.SendNamedMessage(ReadyMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Host: broadcast the lobby's rules to the guest so they can see + apply them before picking.</summary>
    public static void SendLobbySettings(LobbySettingsPayload p)
    {
        if (p == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        var target = GetPeerClientId();
        if (target == null) return;
        string json = JsonUtility.ToJson(p);
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        nm.CustomMessagingManager.SendNamedMessage(LobbySettingsMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Host only: publish the agreed rematch seed so both clients rebuild an
    /// identical new match (same decks, new deal) over the still-open session.</summary>
    public static void SendRematchStart(string seed)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || string.IsNullOrEmpty(seed)) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendRematchStart: no peer - not sent."); return; }
        using var writer = new FastBufferWriter(seed.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(seed);
        nm.CustomMessagingManager.SendNamedMessage(RematchGoMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Ask the opponent to rewind to command index `cursor` (kind = "action"|"turn").</summary>
    public static void SendRewindRequest(int cursor, string kind)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendRewindRequest: no peer - not sent."); return; }
        string json = JsonUtility.ToJson(new RewindRequestPayload { cursor = cursor, kind = kind });
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        nm.CustomMessagingManager.SendNamedMessage(RewindReqMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Answer the opponent's rewind ask. `cursor` echoes the request so both truncate to the same point.</summary>
    public static void SendRewindResponse(bool accept, int cursor)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendRewindResponse: no peer - not sent."); return; }
        string json = JsonUtility.ToJson(new RewindResponsePayload { accept = accept, cursor = cursor });
        using var writer = new FastBufferWriter(json.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(json);
        nm.CustomMessagingManager.SendNamedMessage(RewindRespMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
    }

    // ---- Receiving ----

    private static void OnRewindReqMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        RewindRequestPayload payload = null;
        try { payload = JsonUtility.FromJson<RewindRequestPayload>(json); } catch { /* ignore malformed */ }
        if (payload != null) RewindRequested?.Invoke(payload);
    }

    private static void OnRewindRespMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        RewindResponsePayload payload = null;
        try { payload = JsonUtility.FromJson<RewindResponsePayload>(json); } catch { /* ignore malformed */ }
        if (payload != null) RewindResponded?.Invoke(payload);
    }

    private static void OnMatchStartMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        MatchStartPayload payload = null;
        try { payload = JsonUtility.FromJson<MatchStartPayload>(json); } catch { /* fall through */ }
        // Compatibility: a pre-deck-selection client sends the bare seed string
        // instead of a JSON payload. Treat it as a payload with default decks.
        if (payload == null || string.IsNullOrEmpty(payload.seed))
            payload = new MatchStartPayload { seed = json };
        MatchStartReceived?.Invoke(payload);
    }

    private static void OnDeckShareMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        NetworkDeck deck = null;
        try { deck = JsonUtility.FromJson<NetworkDeck>(json); } catch { /* ignore malformed */ }
        if (deck != null && !string.IsNullOrEmpty(deck.leader)) DeckShareReceived?.Invoke(deck);
    }

    private static void OnChatMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string text);
        if (string.IsNullOrEmpty(text)) return;
        // Defensive cap on the receive side too - never trust the peer's client to clamp.
        if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);
        ChatReceived?.Invoke(text);
    }

    private static void OnNameShareMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string name);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Length > 40) name = name.Substring(0, 40);   // defensive receive-side cap
        PeerNameReceived?.Invoke(name);
    }

    private static void OnPresenceMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        PresencePayload payload = null;
        try { payload = JsonUtility.FromJson<PresencePayload>(json); } catch { /* ignore malformed */ }
        if (payload != null) PresenceReceived?.Invoke(payload);
    }

    private static void OnRematchReqMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte _);
        RematchRequested?.Invoke();
    }

    private static void OnReadyMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte b);
        ReadyReceived?.Invoke(b != 0);
    }

    private static void OnLobbySettingsMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        LobbySettingsPayload payload = null;
        try { payload = JsonUtility.FromJson<LobbySettingsPayload>(json); } catch { /* ignore malformed */ }
        if (payload != null) LobbySettingsReceived?.Invoke(payload);
    }

    private static void OnRematchGoMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string seed);
        if (!string.IsNullOrEmpty(seed)) RematchStartReceived?.Invoke(seed);
    }

    private static void OnGameCommandMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        var serializable = JsonUtility.FromJson<SerializableCommand>(json);
        if (serializable != null) CommandReceived?.Invoke(serializable.ToCommand());
    }
}
