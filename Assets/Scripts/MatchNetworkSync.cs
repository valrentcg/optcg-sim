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
}

// Lightweight "what am I looking at" state each client streams to its opponent while
// playing: which board card is being hovered (opponent renders a gold glow on it) and
// which face-down hand cards are being lifted/inspected. Sent over an Unreliable
// channel - it's pure fire-and-forget cosmetic state, so lost or stale packets are fine
// (the next send overwrites). Serialized with JsonUtility like everything else here.
[Serializable]
public class PresencePayload
{
    public string hoverCardId;        // instance/card id being hovered, or null/empty for none
    public int[] raisedHandIndexes;   // indexes of the sender's hand cards currently lifted
    public int[] donGroups;           // sender's DON!! group partition (empty/null = ungrouped)
}

public static class MatchNetworkSync
{
    private const string MatchStartMessage = "OptcgMatchStart";
    private const string GameCommandMessage = "OptcgGameCmd";
    private const string DeckShareMessage = "OptcgDeckShare";
    private const string ChatMessage = "OptcgChat";
    private const string NameShareMessage = "OptcgNameShare";
    private const string PresenceMessage = "OptcgPresence";

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

    /// <summary>Call once, right after the NetworkManager singleton is created.</summary>
    public static void EnsureHandlersRegistered()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientConnectedCallback += OnClientConnected;
    }

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

    // ---- Receiving ----

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

    private static void OnGameCommandMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        var serializable = JsonUtility.FromJson<SerializableCommand>(json);
        if (serializable != null) CommandReceived?.Invoke(serializable.ToCommand());
    }
}
