using UnityEngine;

public partial class LiveLoadVehicle
{
    [Header("Simulation Audio")]
    [SerializeField] private VehicleSimulationSound simulationSound = new VehicleSimulationSound();
    private VehicleSimulationLoop simulationAudio;
    private bool audioApplicationPaused;
    private bool audioApplicationFocused = true;
    private bool simulationAudioRequested;

    /// <summary>Reuses authored audio for multiplayer's mesh-only presentation.</summary>
    internal VehicleSimulationSound SimulationSound => simulationSound;
    internal void StopSimulationSound() => StopSimulationAudio();

    private void LateUpdate() => RefreshSimulationAudio();

    private void RefreshSimulationAudio()
    {
        bool driving = simulationAudioRequested && isActiveAndEnabled && !isRemoteMultiplayerRepresentation &&
            participatingInCurrentSimulation && isDriving && physicsManager != null &&
            physicsManager.IsSimulationActive && CanParticipateInCurrentSimulation();
        if (!driving || simulationSound == null || simulationSound.clip == null)
        { StopSimulationAudio(); return; }
        if (simulationAudio == null) simulationAudio = new VehicleSimulationLoop(transform);
        simulationAudio.Tick(simulationSound, true, audioApplicationPaused || !audioApplicationFocused ||
            !Application.isFocused || AudioListener.pause || Time.timeScale <= 0f);
    }

    private void BeginSimulationAudio() { simulationAudioRequested = true; RefreshSimulationAudio(); }
    private void StopSimulationAudio() { simulationAudioRequested = false; simulationAudio?.Stop(); }
    private void OnApplicationPause(bool paused)
    {
        audioApplicationPaused = paused;
        if (paused) simulationAudio?.Suspend(); else RefreshSimulationAudio();
    }
    private void OnApplicationFocus(bool focused)
    {
        audioApplicationFocused = focused;
        if (!focused) simulationAudio?.Suspend(); else RefreshSimulationAudio();
    }
}
