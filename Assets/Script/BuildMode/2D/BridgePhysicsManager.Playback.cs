using System.Globalization;
using UnityEngine;

public partial class BridgePhysicsManager
{
    // This owner composes lesson slow motion and the player's temporary speed.
    // Fixed-step size stays unchanged: faster playback runs more of the same steps.
    private readonly SimulationPlaybackState playback = new SimulationPlaybackState();
    private bool playbackOwnsTime;
    private bool playbackDirty;
    private float lastPlaybackScale = 1f;
    private bool playbackRestorePending;
    private float pendingPlaybackRestore = 1f;

    public int SimulationPlaybackStep => playback.Step;
    public float SimulationPlaybackScale => playback.Scale;
    public string SimulationPlaybackLabel => Mathf.Approximately(playback.Scale, 1f)
        ? "0x" : playback.Scale.ToString("0.###", CultureInfo.InvariantCulture) + "x";

    public bool CanAdjustSimulationPlayback => playback.Active && IsSimulationActive &&
        !lockStressTracking && IsSoloSimulationPlayback &&
        GameManager.Instance != null && !GameManager.Instance.IsTransitioning &&
        GameManager.Instance.CurrentState != GameManager.GameState.Normal &&
        Time.timeScale > 0f && (PauseManager.Instance == null || !PauseManager.Instance.isPaused) &&
        !DialogueManager.IsAnyDialogueVisible && !LoadingScreenManager.IsLoading &&
        !UIReservedRegionLayout.IsModalTransitioning &&
        (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel);

    private bool IsSoloSimulationPlayback => !IsSessionChallengeTest &&
        !MultiplayerChallengeLobbyUI.IsChallengeActive &&
        (GameManager.Instance == null || !GameManager.Instance.IsSessionChallengeBuild) &&
        (FusionConnectionManager.Instance == null ||
            (FusionConnectionManager.Instance.Runner == null || !FusionConnectionManager.Instance.Runner.IsRunning) &&
            !FusionConnectionManager.Instance.IsSessionStopping) &&
        (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening);

    private void OnEnable()
    {
        // Also runs when entering Play with domain/scene reload disabled.
        EndSimulationPlayback();
        OnSimulationStopped += EndSimulationPlayback;
        LevelCompleteManager.SimulationSucceeded += FinishSimulationPlayback;
        LevelFailedManager.SimulationFailed += FailSimulationPlayback;
    }

    private void Update()
    {
        if (playbackRestorePending && Time.timeScale > 0f)
        {
            if (Mathf.Approximately(Time.timeScale, lastPlaybackScale)) Time.timeScale = pendingPlaybackRestore;
            playbackRestorePending = false;
        }
        if (!playback.Active) return;
        if (!IsSimulationActive || lockStressTracking || !IsSoloSimulationPlayback ||
            (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Normal))
        {
            EndSimulationPlayback();
            return;
        }
        if (playbackOwnsTime && !playbackDirty && Time.timeScale > 0f &&
            !Mathf.Approximately(Time.timeScale, lastPlaybackScale)) EndSimulationPlayback();
        else if (playbackDirty) ApplySimulationPlayback();
    }

    private void BeginSimulationPlayback()
    {
        EndSimulationPlayback();
        if (!IsSoloSimulationPlayback) return;
        playback.Begin(Time.timeScale);
        lastPlaybackScale = playback.Scale;
        playbackOwnsTime = true;
        playbackDirty = true;
        ApplySimulationPlayback();
    }

    public bool ChangeSimulationPlayback(int direction)
    {
        if (!CanAdjustSimulationPlayback || !playback.TryStep(direction)) return false;
        playbackDirty = true;
        ApplySimulationPlayback();
        return true;
    }

    public bool SetSimulationLessonPlayback(object owner, float multiplier)
    {
        if (!playback.Active || !IsSimulationActive || !IsSoloSimulationPlayback) return false;
        playback.SetLessonMultiplier(owner, multiplier);
        playbackDirty = true;
        ApplySimulationPlayback();
        return true;
    }

    public void ClearSimulationLessonPlayback(object owner)
    {
        if (!playback.ClearLessonMultiplier(owner)) return;
        playbackDirty = true;
        ApplySimulationPlayback();
    }

    private void ApplySimulationPlayback()
    {
        if (!playback.Active || !playbackDirty || Time.timeScale <= 0f ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused)) return;
        // A debug tool or another time owner may have changed the speed. Do not
        // repeatedly fight that owner or overwrite its value during teardown.
        if (playbackOwnsTime && !Mathf.Approximately(Time.timeScale, lastPlaybackScale))
        {
            EndSimulationPlayback();
            return;
        }
        Time.timeScale = lastPlaybackScale = playback.Scale;
        playbackOwnsTime = true;
        playbackDirty = false;
    }

    private void EndSimulationPlayback()
    {
        if (!playback.Active) return;
        float original = playback.BaselineScale;
        if (playbackOwnsTime)
        {
            if (Time.timeScale <= 0f)
            {
                PauseManager.Instance?.ReplacePausedTimeScale(lastPlaybackScale, original);
                PlayerDataManager.Instance?.GetComponent<CloudSaveManager>()?
                    .ReplacePausedTimeScale(lastPlaybackScale, original);
                // A cloud-conflict pause also restores its captured scale later.
                playbackRestorePending = true;
                pendingPlaybackRestore = original;
            }
            else if (Mathf.Approximately(Time.timeScale, lastPlaybackScale)) Time.timeScale = original;
        }
        playback.End();
        playbackDirty = playbackOwnsTime = false;
    }

    private void DisposeSimulationPlayback()
    {
        EndSimulationPlayback();
        OnSimulationStopped -= EndSimulationPlayback;
        LevelCompleteManager.SimulationSucceeded -= FinishSimulationPlayback;
        LevelFailedManager.SimulationFailed -= FailSimulationPlayback;
    }

    private void FinishSimulationPlayback(ContractSO contract, BuildLocation location)
    {
        if (location == SimulationLocation) EndSimulationPlayback();
    }

    private void FailSimulationPlayback(string reason) => EndSimulationPlayback();
}
