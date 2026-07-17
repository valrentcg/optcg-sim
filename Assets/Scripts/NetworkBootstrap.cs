// One Piece TCG - Creates the persistent NetworkManager + UnityTransport that
// com.unity.services.multiplayer's Sessions API wires up automatically once
// SessionOptions.WithRelayNetwork() is used (see LobbyManager.CreateLobbyAsync).
// NetworkManager.Singleton must exist BEFORE any session create/join call - the
// SDK's built-in Netcode-for-GameObjects handler throws otherwise.

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class NetworkBootstrap
{
    public static void EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null)
        {
            // Already created (e.g. a prior match that ShutdownNetwork tore down but left the
            // GameObject). Re-arm the connect callback so the NEXT session re-registers its named-
            // message handlers on the fresh CustomMessagingManager (idempotent -= / +=).
            MatchNetworkSync.EnsureHandlersRegistered();
            return;
        }

        var go = new GameObject("NetworkManager");
        Object.DontDestroyOnLoad(go);
        var transport = go.AddComponent<UnityTransport>();
#if UNITY_WEBGL && !UNITY_EDITOR
        // Browsers cannot speak UDP/DTLS — Relay traffic must go over WebSockets.
        // Desktop clients keep the default UDP path; Relay bridges the two, so
        // web and desktop players can still share a session as long as both
        // builds run the same protocol version (guarded by version.json's
        // minSupportedBuildNumber).
        transport.UseWebSockets = true;
#endif
        var manager = go.AddComponent<NetworkManager>();
        // NetworkConfig is a plain field with no inline initializer, so a NetworkManager
        // added purely at runtime (no scene/prefab data backing it) can come up with it
        // still null, unlike one placed in the Editor where serialization fills it in.
        if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
        manager.NetworkConfig.NetworkTransport = transport;
        // Scene management MUST be off. This project never syncs scenes or
        // NetworkObjects (gameplay is a deterministic GameCommand log over custom
        // messages), and NGO's automatic scene-sync handshake actively breaks the
        // connection here: on client connect, NetworkSceneManager tries to resolve
        // the host's scene hash to a local scene path, throws
        // ArgumentOutOfRangeException (GetSceneNameFromPath), and the handshake
        // never completes — the lobby then sits on "Finishing connection..."
        // forever because IsPeerConnected never turns true.
        manager.NetworkConfig.EnableSceneManagement = false;

        MatchNetworkSync.EnsureHandlersRegistered();
    }
}
