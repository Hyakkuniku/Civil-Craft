using UnityEngine;

public sealed partial class BoatBridgeCrossing
{
    [Header("Simulation Audio")]
    [SerializeField] private VehicleSimulationSound simulationSound = new VehicleSimulationSound();
    private VehicleSimulationLoop boatAudio;
    private bool boatAudioApplicationPaused;
    private bool boatAudioApplicationFocused = true;

    internal VehicleSimulationSound SimulationSound => simulationSound;

    private void LateUpdate() => RefreshBoatAudio();

    private void RefreshBoatAudio()
    {
        bool crossing = moving && delayRemaining <= 0f && isActiveAndEnabled &&
            subscribedManager != null && subscribedManager.isSimulating && activeLocation != null &&
            HasCurrentSimulationContext() && HasTravelDirection;
        if (!crossing || simulationSound == null || simulationSound.clip == null)
        { StopBoatAudio(); return; }
        if (boatAudio == null) boatAudio = new VehicleSimulationLoop(transform);
        boatAudio.Tick(simulationSound, true, boatAudioApplicationPaused || !boatAudioApplicationFocused ||
            !Application.isFocused || AudioListener.pause || Time.timeScale <= 0f);
    }

    private void StopBoatAudio() => boatAudio?.Stop();

    private void OnApplicationPause(bool paused)
    {
        boatAudioApplicationPaused = paused;
        if (paused) boatAudio?.Suspend(); else RefreshBoatAudio();
    }

    private void OnApplicationFocus(bool focused)
    {
        boatAudioApplicationFocused = focused;
        if (!focused) boatAudio?.Suspend(); else RefreshBoatAudio();
    }
}
