using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Spawns one Fusion avatar proxy per connected player in a live world.</summary>
public sealed class FusionSessionCallbacks : MonoBehaviour, INetworkRunnerCallbacks
{
    private NetworkObject avatarPrefab;

    private void Awake()
    {
        avatarPrefab = Resources.Load<NetworkObject>("FusionMultiplayerAvatar");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        EnsureAvatars(runner);
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        FusionConnectionManager.Instance?.HandleSceneLoadCompleted(runner);
        EnsureAvatars(runner);
    }

    private void EnsureAvatars(NetworkRunner runner)
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (runner == null || !runner.IsServer || connection == null ||
            connection.Runner != runner || connection.IsNetworkSceneLoading ||
            !connection.IsAvatarScene) return;
        if (avatarPrefab == null)
        {
            Debug.LogError("[Fusion] FusionMultiplayerAvatar prefab is missing from Resources.", this);
            return;
        }

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (runner.TryGetPlayerObject(player, out NetworkObject existing) && existing != null)
                continue;

            NetworkObject avatar = runner.Spawn(avatarPrefab, Vector3.zero,
                Quaternion.identity, player);
            if (avatar != null) runner.SetPlayerObject(player, avatar);
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer && runner.TryGetPlayerObject(player, out NetworkObject avatar) &&
            avatar != null)
            runner.Despawn(avatar);

        FusionConnectionManager.Instance?.HandleGuestDisconnected(runner, player);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LogGuestDisconnect(runner, $"shutdown={shutdownReason}");
#endif
        FusionConnectionManager.Instance?.HandleHostDisconnected(runner, true);
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LogGuestDisconnect(runner, $"reason={reason}");
#endif
        FusionConnectionManager.Instance?.HandleHostDisconnected(runner, false);
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void LogGuestDisconnect(NetworkRunner runner, string reason)
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (connection == null || connection.Runner != runner || runner.IsServer) return;
        Debug.LogWarning($"[Fusion session] Guest disconnected: {reason} " +
            $"scene={SceneManager.GetActiveScene().name} loading={connection.IsNetworkSceneLoading}", this);
    }
#endif
    public void OnConnectRequest(NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress,
        NetConnectFailedReason reason) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner,
        Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player,
        ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player,
        ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        FusionConnectionManager.Instance?.HandleSceneLoadStarted(runner);
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
