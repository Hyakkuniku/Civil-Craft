using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Photon Fusion room lifecycle for the existing two-player Host/Join UI.
/// Kept separate from the Relay manager until the avatar migration is verified.
/// </summary>
public sealed class FusionConnectionManager : MonoBehaviour
{
    private const string RoomAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int RoomCodeLength = 6;
    private const string FixedRegion = "asia";
    private const string ModeSelectionSceneName = "Mode Selection";
    public const string HostWorldSceneName = "CanyonCrossing";

    private NetworkRunner runner;
    private int operationVersion;
    private bool clientSessionEstablished;
    private bool guestVisitedHostWorld;
    private bool guestSaveProtected;
    private bool networkSceneLoading;
    private Task pendingShutdown = Task.CompletedTask;
    private static bool pendingHostLeftNotice;

    public static FusionConnectionManager Instance { get; private set; }
    public NetworkRunner Runner => runner;
    public bool IsNetworkSceneLoading => networkSceneLoading;
    public bool IsSessionStopping => !pendingShutdown.IsCompleted;
    public string HostJoinCode { get; private set; }
    public bool IsHosting => runner != null && runner.IsRunning && runner.IsServer;
    public bool IsClientConnected => runner != null && runner.IsRunning && runner.IsClient && !runner.IsServer;
    public bool IsGuestInHostWorld =>
        SceneManager.GetActiveScene().name == HostWorldSceneName &&
        (IsClientConnected || guestSaveProtected);
    public bool IsGuestSaveProtected => guestSaveProtected || IsGuestInHostWorld;
    public bool IsHostWorldSession => runner != null && runner.IsRunning &&
        SceneManager.GetActiveScene().name == HostWorldSceneName;
    public bool IsAvatarScene => runner != null && runner.IsRunning &&
        (SceneManager.GetActiveScene().name == HostWorldSceneName ||
         SceneManager.GetActiveScene().name == "Multiplayer");
    public int ConnectedPlayerCount
    {
        get
        {
            if (runner == null || !runner.IsRunning) return 0;
            int count = 0;
            foreach (PlayerRef player in runner.ActivePlayers) count++;
            return count;
        }
    }

    public static FusionConnectionManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        GameObject service = new GameObject("Fusion Connection Manager");
        return service.AddComponent<FusionConnectionManager>();
    }

    public static bool ConsumeHostLeftNotice()
    {
        if (!pendingHostLeftNotice) return false;
        pendingHostLeftNotice = false;
        return true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (Instance == this) Instance = null;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        if (next.name == HostWorldSceneName && IsClientConnected)
            guestVisitedHostWorld = true;
        if ((previous.name == "Multiplayer" || previous.name == HostWorldSceneName) &&
            next.name != previous.name && runner != null && !networkSceneLoading)
            StopSession();
    }

    public void HandleSceneLoadStarted(NetworkRunner loadingRunner)
    {
        if (runner == loadingRunner) networkSceneLoading = true;
    }

    public void HandleSceneLoadCompleted(NetworkRunner loadingRunner)
    {
        if (runner != loadingRunner) return;
        networkSceneLoading = false;
        if (IsGuestInHostWorld) guestVisitedHostWorld = true;
    }

    public Task<string> StartHostAsync() => StartHostSessionAsync(null);

    /// <summary>Publish the already-loaded story world; Fusion takes it over without reloading the host.</summary>
    public Task<string> StartHostFromCurrentWorldAsync()
    {
        Scene world = SceneManager.GetActiveScene();
        if (world.name != HostWorldSceneName || world.buildIndex < 0)
            throw new InvalidOperationException("Open Canyon Crossing before turning on multiplayer.");
        var scenes = new NetworkSceneInfo();
        scenes.AddSceneRef(SceneRef.FromIndex(world.buildIndex), LoadSceneMode.Single);
        return StartHostSessionAsync(scenes);
    }

    private async Task<string> StartHostSessionAsync(NetworkSceneInfo? initialScene)
    {
        if (IsSessionStopping) throw new InvalidOperationException("Wait for the previous session to finish closing.");
        if (IsHosting) return HostJoinCode;
        if (runner != null) throw new InvalidOperationException("A Fusion session is already starting or running.");

        pendingHostLeftNotice = false;
        int version = ++operationVersion;
        string roomCode = CreateRoomCode();
        NetworkRunner newRunner = CreateRunner();
        networkSceneLoading = initialScene.HasValue;
        try
        {
            StartGameResult result = await newRunner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = roomCode,
                PlayerCount = 2,
                IsOpen = true,
                IsVisible = false,
                Scene = initialScene,
                SceneManager = newRunner.GetComponent<NetworkSceneManagerDefault>(),
                CustomPhotonAppSettings = BuildPhotonSettings()
            });

            if (version != operationVersion) throw new OperationCanceledException();
            if (!result.Ok) throw new InvalidOperationException($"Fusion host failed: {result.ShutdownReason}");
            HostJoinCode = roomCode;
            return roomCode;
        }
        catch
        {
            if (runner == newRunner) networkSceneLoading = false;
            await ShutdownRunnerAsync(newRunner);
            throw;
        }
    }

    public bool TryGetGuest(out PlayerRef guest, out NetworkObject avatar)
    {
        guest = PlayerRef.None; avatar = null;
        if (!IsHosting) return false;
        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer) continue;
            guest = player;
            runner.TryGetPlayerObject(player, out avatar);
            return true;
        }
        return false;
    }

    internal static bool CanKickPlayer(bool isHost, PlayerRef host, PlayerRef target, bool connected) =>
        isHost && target != PlayerRef.None && target != host && connected;

    public bool TryKickGuest(PlayerRef target, NetworkObject expectedAvatar)
    {
        if (!IsHostWorldSession || networkSceneLoading || !TryGetGuest(out PlayerRef guest, out NetworkObject avatar) ||
            !CanKickPlayer(IsHosting, runner.LocalPlayer, target, guest == target) ||
            expectedAvatar == null || avatar != expectedAvatar || !expectedAvatar.IsValid) return false;
        // Fusion enforces server authority. This only disconnects this guest;
        // the normal PlayerLeft path restores challenge state and world visuals.
        runner.Disconnect(target);
        return true;
    }

    public async Task JoinAsync(string roomCode)
    {
        if (IsSessionStopping) throw new InvalidOperationException("Wait for the previous session to finish closing.");
        if (runner != null) throw new InvalidOperationException("A Fusion session is already starting or running.");
        if (string.IsNullOrWhiteSpace(roomCode)) throw new ArgumentException("Enter a room code.", nameof(roomCode));

        pendingHostLeftNotice = false;
        int version = ++operationVersion;
        NetworkRunner newRunner = CreateRunner();
        try
        {
            StartGameResult result = await newRunner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = roomCode.ToUpperInvariant(),
                EnableClientSessionCreation = false,
                SceneManager = newRunner.GetComponent<NetworkSceneManagerDefault>(),
                CustomPhotonAppSettings = BuildPhotonSettings()
            });

            if (version != operationVersion) throw new OperationCanceledException();
            if (!result.Ok) throw new InvalidOperationException($"Fusion join failed: {result.ShutdownReason}");
            clientSessionEstablished = true;
        }
        catch
        {
            await ShutdownRunnerAsync(newRunner);
            throw;
        }
    }

    public bool StartMultiplayerScene(string sceneName)
    {
        if (!IsHosting || string.IsNullOrWhiteSpace(sceneName)) return false;
        int sceneIndex = FindBuildSceneIndex(sceneName);
        if (sceneIndex < 0)
        {
            Debug.LogError($"[Fusion] '{sceneName}' is not in Build Settings.", this);
            return false;
        }

        // Protect lobby cleanup before Fusion begins its asynchronous load.
        networkSceneLoading = true;
        try
        {
            runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            networkSceneLoading = false;
            Debug.LogError($"[Fusion] Could not start scene '{sceneName}': {exception.Message}", this);
            return false;
        }
        return true;
    }

    public void StopSession()
    {
        _ = StopSessionAsync();
    }

    /// <summary>Close hosting without loading another scene or writing world progress.</summary>
    public Task StopHostingWorldAsync()
    {
        if (!IsHosting || !IsHostWorldSession || networkSceneLoading)
            throw new InvalidOperationException("Only the active world host can turn off multiplayer.");
        return StopSessionAsync();
    }

    internal static bool CanLeaveHostWorld(bool visiting, bool inHostWorld, bool loading, bool shuttingDown) =>
        visiting && inHostWorld && !loading && !shuttingDown;

    /// <summary>Leave only this guest's visit, preserving the host's room and the guest's story save.</summary>
    public Task LeaveHostWorldAsync()
    {
        bool loading = networkSceneLoading || LoadingScreenManager.IsLoading;
        if (!CanLeaveHostWorld(IsClientConnected, IsHostWorldSession, loading, IsSessionStopping))
            throw new InvalidOperationException("Only a connected guest in the host's world can leave multiplayer.");
        NetworkSceneManagerDefault sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
        // IsBusy accesses Runner.Config. Check initialization before querying it.
        if (sceneManager != null && (sceneManager.Runner != runner || sceneManager.IsBusy))
            throw new InvalidOperationException("Wait for the current multiplayer scene transition to finish.");

        // Intentional leaves must not reuse a stale host-disconnected notification.
        // StopSessionAsync latches guest-save protection before detaching the runner.
        pendingHostLeftNotice = false;
        Task shutdown = StopSessionAsync();
        int exitVersion = operationVersion;
        return ReturnAfterGuestLeaveAsync(shutdown, exitVersion);
    }

    private async Task ReturnAfterGuestLeaveAsync(Task shutdown, int exitVersion)
    {
        await shutdown;
        // A separate scene transition can begin while shutdown is awaiting Fusion.
        // Let it finish rather than competing with the persistent loading screen.
        // Fusion clears its scene manager's Runner during shutdown, so never query
        // that component's IsBusy afterward (it accesses Runner.Config).
        while (this != null && Instance == this && operationVersion == exitVersion && runner == null &&
            LoadingScreenManager.IsLoading)
            await Task.Yield();

        if (this == null || Instance != this || operationVersion != exitVersion || runner != null ||
            SceneManager.GetActiveScene().name != HostWorldSceneName) return;

        LoadingScreenManager.LoadScene(ModeSelectionSceneName);
    }

    private Task StopSessionAsync()
    {
        bool wasHostWorldGuest = guestVisitedHostWorld || IsGuestInHostWorld;
        guestVisitedHostWorld = false;
        if (wasHostWorldGuest) BeginGuestWorldExit();
        ++operationVersion;
        HostJoinCode = null;
        clientSessionEstablished = false;
        networkSceneLoading = false;
        NetworkRunner oldRunner = runner;
        runner = null;
        if (oldRunner != null) pendingShutdown = ShutdownRunnerAsync(oldRunner);
        return pendingShutdown;
    }

    public void HandleHostDisconnected(NetworkRunner disconnectedRunner, bool alreadyShutDown)
    {
        // Local leaves and failed join attempts must not look like a host loss.
        if (runner != disconnectedRunner || !clientSessionEstablished) return;

        NetworkSceneManagerDefault disconnectedSceneManager =
            disconnectedRunner.GetComponent<NetworkSceneManagerDefault>();
        bool wasHostWorldGuest = guestVisitedHostWorld || IsGuestInHostWorld;
        guestVisitedHostWorld = false;
        if (wasHostWorldGuest) BeginGuestWorldExit();
        ++operationVersion;
        clientSessionEstablished = false;
        networkSceneLoading = false;
        HostJoinCode = null;
        runner = null;
        pendingHostLeftNotice = true;

        if (alreadyShutDown)
            Destroy(disconnectedRunner.gameObject);
        else
            StartCoroutine(ShutdownDisconnectedRunnerNextFrame(disconnectedRunner));

        StartCoroutine(ReturnToModeSelectionAfterHostLoss(disconnectedSceneManager));
    }

    private void BeginGuestWorldExit()
    {
        if (guestSaveProtected) return;
        guestSaveProtected = true;
        StartCoroutine(RestoreGuestSaveAfterSceneExit());
    }

    private IEnumerator RestoreGuestSaveAfterSceneExit()
    {
        // Scene objects can save in OnDestroy after activeSceneChanged. Keep
        // writes blocked until that teardown has completed.
        while (SceneManager.GetActiveScene().name == HostWorldSceneName)
            yield return null;
        yield return null;
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.LoadGame(false);
        guestSaveProtected = false;
    }

    public void HandleGuestDisconnected(NetworkRunner activeRunner, PlayerRef player)
    {
        // Only the live host should announce a remote player leaving. In
        // particular, shutting down our own room must not produce a notice.
        if (runner != activeRunner || !IsHosting || player == activeRunner.LocalPlayer)
            return;

        if (SceneManager.GetActiveScene().name == ModeSelectionSceneName)
        {
            ModeSelectionManager selection = FindObjectOfType<ModeSelectionManager>(true);
            if (selection != null) selection.ShowGuestLeftNotice();
        }
        else
        {
            AchievementPopupNotification.NotifyMultiplayerGuestLeft();
        }
    }

    private IEnumerator ShutdownDisconnectedRunnerNextFrame(NetworkRunner disconnectedRunner)
    {
        // Do not re-enter Fusion shutdown while it is dispatching a disconnect.
        yield return null;
        _ = ShutdownRunnerAsync(disconnectedRunner);
    }

    private IEnumerator ReturnToModeSelectionAfterHostLoss(
        NetworkSceneManagerDefault disconnectedSceneManager)
    {
        // A local scene transition may already be in flight when the host drops.
        while (LoadingScreenManager.IsLoading ||
               (disconnectedSceneManager != null && disconnectedSceneManager.IsBusy))
            yield return null;

        if (!pendingHostLeftNotice) yield break;

        if (SceneManager.GetActiveScene().name != ModeSelectionSceneName)
        {
            LoadingScreenManager.LoadScene(ModeSelectionSceneName);
            yield break;
        }

        ModeSelectionManager selection = FindObjectOfType<ModeSelectionManager>(true);
        if (selection != null && ConsumeHostLeftNotice())
            selection.ShowHostLeftNotice();
    }

    private NetworkRunner CreateRunner()
    {
        // A Fusion runner is single-use. Create a fresh one after each disconnect.
        GameObject runnerObject = new GameObject("Fusion Network Runner");
        DontDestroyOnLoad(runnerObject);
        runner = runnerObject.AddComponent<NetworkRunner>();
        runnerObject.AddComponent<NetworkSceneManagerDefault>();
        runnerObject.AddComponent<FusionHostWorldBridgeSync>();
        runnerObject.AddComponent<FusionChallengeSubmissionSync>();
        runnerObject.AddComponent<FusionChallengeTestSync>();
        runnerObject.AddComponent<FusionSessionCallbacks>();
        return runner;
    }

    private static async Task ShutdownRunnerAsync(NetworkRunner oldRunner)
    {
        if (oldRunner == null) return;
        try { await oldRunner.Shutdown(); }
        catch (Exception exception) { Debug.LogWarning($"[Fusion] Shutdown: {exception.Message}"); }
        if (oldRunner != null) Destroy(oldRunner.gameObject);
        if (Instance != null && Instance.runner == oldRunner) Instance.runner = null;
    }

    private static FusionAppSettings BuildPhotonSettings()
    {
        // Room names are regional. Pin both players to one region until the UI
        // can include a region selector alongside the room code.
        FusionAppSettings settings = PhotonAppSettings.Global.AppSettings.GetCopy();
        settings.FixedRegion = FixedRegion;
        return settings;
    }

    private static int FindBuildSceneIndex(string sceneName)
    {
        for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(index);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName,
                    StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }

    private static string CreateRoomCode()
    {
        byte[] randomBytes = new byte[RoomCodeLength];
        using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            generator.GetBytes(randomBytes);

        char[] code = new char[RoomCodeLength];
        for (int index = 0; index < code.Length; index++)
            code[index] = RoomAlphabet[randomBytes[index] % RoomAlphabet.Length];
        return new string(code);
    }
}
