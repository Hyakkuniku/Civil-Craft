using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Spawns one Fusion avatar proxy per connected player in a live world.</summary>
public sealed class FusionSessionCallbacks : MonoBehaviour, INetworkRunnerCallbacks
{
    private NetworkRunner sessionRunner;
    private NetworkObject avatarPrefab;
    private FusionHostWorldBridgeSync worldBridges;
    private FusionChallengeSubmissionSync challengeSubmissions;
    private FusionChallengeTestSync challengeTests;
    private bool avatarReconciliationPending;
    private float nextAvatarAttemptTime;

    private void Awake()
    {
        sessionRunner = GetComponent<NetworkRunner>();
        avatarPrefab = Resources.Load<NetworkObject>("FusionMultiplayerAvatar");
        worldBridges = GetComponent<FusionHostWorldBridgeSync>();
        challengeSubmissions = GetComponent<FusionChallengeSubmissionSync>();
        challengeTests = GetComponent<FusionChallengeTestSync>();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        RequestAvatarReconciliation();
        worldBridges?.OnPlayerJoined(player);
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        FusionConnectionManager.Instance?.HandleSceneLoadCompleted(runner);
        RequestAvatarReconciliation();
        worldBridges?.OnSceneReady();
    }

    internal void RequestAvatarReconciliation()
    {
        avatarReconciliationPending = true;
        nextAvatarAttemptTime = 0f;
    }

    private void LateUpdate()
    {
        NetworkRunner runner = sessionRunner;
        if (!avatarReconciliationPending || runner == null || !runner.IsRunning ||
            Time.unscaledTime < nextAvatarAttemptTime) return;

        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (!runner.IsServer || connection == null || connection.Runner != runner)
        {
            avatarReconciliationPending = false;
            return;
        }
        // Scene and player callbacks can arrive before the runner can spawn.
        // Keep the request until the active scene and host identity are ready.
        if (connection.IsNetworkSceneLoading || !runner.LocalPlayer.IsRealPlayer) return;
        avatarReconciliationPending = false;
        if (!connection.IsAvatarScene) return;
        if (avatarPrefab == null)
        {
            Debug.LogError("[Fusion] FusionMultiplayerAvatar prefab is missing from Resources.", this);
            return;
        }

        foreach (PlayerRef player in runner.CommittedPlayers)
        {
            if (runner.TryGetPlayerObject(player, out NetworkObject existing) && existing != null && existing.IsValid)
                continue;

            try
            {
                NetworkSpawnStatus status = runner.TrySpawn(avatarPrefab, out NetworkObject avatar,
                    inputAuthority: player);
                if (status == NetworkSpawnStatus.Spawned)
                {
                    runner.SetPlayerObject(player, avatar);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Fusion session] Registered {(player == runner.LocalPlayer ? "host" : "guest")} " +
                        $"avatar in {SceneManager.GetActiveScene().name}.", this);
#endif
                }
                else if (status == NetworkSpawnStatus.FailedLocalPlayerNotYetSet ||
                         status == NetworkSpawnStatus.FailedSimulationNotRunning)
                {
                    avatarReconciliationPending = true;
                    nextAvatarAttemptTime = Time.unscaledTime + 0.25f;
                }
                else
                    Debug.LogError($"[Fusion] Could not spawn the avatar for {player}: {status}.", this);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Fusion] Could not set up the avatar for {player}: {exception.Message}", this);
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        challengeTests?.Clear();
        challengeSubmissions?.Clear();
        worldBridges?.OnPlayerLeft(player);
        if (runner.IsServer && runner.TryGetPlayerObject(player, out NetworkObject avatar) &&
            avatar != null)
            runner.Despawn(avatar);

        FusionConnectionManager.Instance?.HandleGuestDisconnected(runner, player);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        avatarReconciliationPending = false;
        challengeTests?.Clear();
        challengeSubmissions?.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LogGuestDisconnect(runner, $"shutdown={shutdownReason}");
#endif
        FusionConnectionManager.Instance?.HandleHostDisconnected(runner, true);
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        challengeTests?.Clear();
        challengeSubmissions?.Clear();
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
        ReliableKey key, ReadOnlySpan<byte> data)
    {
        // Route by protocol, and resolve on the ACTUAL callback runner. An
        // Awake-only cached reference can be absent after component addition or
        // editor reload; null-conditional forwarding then silently loses every
        // descriptor AND recovery request. Other protocol handlers must never
        // prevent a test packet reaching its receiver.
        key.GetInts(out int tag, out _, out _, out _);
        switch (tag)
        {
            case FusionChallengeTestSync.MessageTag:
                challengeTests = ResolveTestReceiver(runner);
                if (challengeTests != null) challengeTests.OnReliableData(player, key, data);
                else Debug.LogError("[Challenge transfer routing] Test receiver is missing on the callback runner.", this);
                break;
            case FusionChallengeSubmissionSync.MessageTag:
                challengeSubmissions = runner.GetComponent<FusionChallengeSubmissionSync>();
                challengeSubmissions?.OnReliableData(player, key, data);
                break;
            case FusionHostWorldBridgeSync.MessageTag:
                worldBridges = runner.GetComponent<FusionHostWorldBridgeSync>();
                worldBridges?.OnReliableData(player, key, data);
                break;
        }
    }
    internal static FusionChallengeTestSync ResolveTestReceiver(NetworkRunner callbackRunner) =>
        callbackRunner != null ? callbackRunner.GetComponent<FusionChallengeTestSync>() : null;
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player,
        ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        challengeTests?.Clear();
        challengeSubmissions?.Clear();
        FusionConnectionManager.Instance?.HandleSceneLoadStarted(runner);
        worldBridges?.OnSceneLoading();
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
