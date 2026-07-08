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

public static class MatchNetworkSync
{
    private const string MatchStartMessage = "OptcgMatchStart";
    private const string GameCommandMessage = "OptcgGameCmd";
    private const string DeckShareMessage = "OptcgDeckShare";

    private static bool handlersRegistered;

    public static event Action<MatchStartPayload> MatchStartReceived;
    public static event Action<GameCommand> CommandReceived;
    public static event Action<NetworkDeck> DeckShareReceived;   // peer told us which deck they'll play

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

    private static void OnGameCommandMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        var serializable = JsonUtility.FromJson<SerializableCommand>(json);
        if (serializable != null) CommandReceived?.Invoke(serializable.ToCommand());
    }
}
