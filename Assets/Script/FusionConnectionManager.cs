using System;
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

    private NetworkRunner runner;
    private int operationVersion;

    public static FusionConnectionManager Instance { get; private set; }
    public NetworkRunner Runner => runner;
    public string HostJoinCode { get; private set; }
    public bool IsHosting => runner != null && runner.IsRunning && runner.IsServer;
    public bool IsClientConnected => runner != null && runner.IsRunning && runner.IsClient && !runner.IsServer;
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
        if (previous.name == "Multiplayer" && next.name != "Multiplayer")
            StopSession();
    }

    public async Task<string> StartHostAsync()
    {
        if (IsHosting) return HostJoinCode;
        if (runner != null) throw new InvalidOperationException("A Fusion session is already starting or running.");

        int version = ++operationVersion;
        string roomCode = CreateRoomCode();
        NetworkRunner newRunner = CreateRunner();
        try
        {
            StartGameResult result = await newRunner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = roomCode,
                PlayerCount = 2,
                IsOpen = true,
                IsVisible = false,
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
            await ShutdownRunnerAsync(newRunner);
            throw;
        }
    }

    public async Task JoinAsync(string roomCode)
    {
        if (runner != null) throw new InvalidOperationException("A Fusion session is already starting or running.");
        if (string.IsNullOrWhiteSpace(roomCode)) throw new ArgumentException("Enter a room code.", nameof(roomCode));

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

        runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
        return true;
    }

    public void StopSession()
    {
        ++operationVersion;
        HostJoinCode = null;
        NetworkRunner oldRunner = runner;
        runner = null;
        if (oldRunner != null) _ = ShutdownRunnerAsync(oldRunner);
    }

    private NetworkRunner CreateRunner()
    {
        // A Fusion runner is single-use. Create a fresh one after each disconnect.
        GameObject runnerObject = new GameObject("Fusion Network Runner");
        DontDestroyOnLoad(runnerObject);
        runner = runnerObject.AddComponent<NetworkRunner>();
        runnerObject.AddComponent<NetworkSceneManagerDefault>();
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
