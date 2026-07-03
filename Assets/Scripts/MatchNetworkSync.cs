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
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using OnePieceTcg.Engine;

public static class MatchNetworkSync
{
    private const string MatchStartMessage = "OptcgMatchStart";
    private const string GameCommandMessage = "OptcgGameCmd";

    private static bool handlersRegistered;

    public static event Action<string> MatchStartReceived;   // payload: match seed
    public static event Action<GameCommand> CommandReceived;

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

    public static void SendMatchStart(string seed)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || string.IsNullOrEmpty(seed)) return;
        var target = GetPeerClientId();
        if (target == null) { Debug.LogWarning("MatchNetworkSync.SendMatchStart: no connected peer yet - not sent."); return; }
        using var writer = new FastBufferWriter(seed.Length * 2 + 32, Allocator.Temp);
        writer.WriteValueSafe(seed);
        nm.CustomMessagingManager.SendNamedMessage(MatchStartMessage, target.Value, writer, NetworkDelivery.ReliableSequenced);
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
        reader.ReadValueSafe(out string seed);
        MatchStartReceived?.Invoke(seed);
    }

    private static void OnGameCommandMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        var serializable = JsonUtility.FromJson<SerializableCommand>(json);
        if (serializable != null) CommandReceived?.Invoke(serializable.ToCommand());
    }
}
