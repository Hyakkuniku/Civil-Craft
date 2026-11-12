using UnityEngine;

public partial class BridgePhysicsManager
{
    private BridgeEnvironmentalAudio environmentalAudio;
    private bool environmentalAudioApplicationPaused;
    private bool environmentalAudioApplicationFocused = true;

    private void PrepareEnvironmentalAudio()
    {
        var settings = Resources.Load<BridgeEnvironmentalAudioSettings>("BridgeEnvironmentalAudioSettings");
        environmentalAudio = new BridgeEnvironmentalAudio(transform, settings);
    }

    private void RefreshEnvironmentalAudio()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !EnvironmentalEventVisible)
        {
            environmentalAudio?.Stop();
            return;
        }
        bool paused = environmentalAudioApplicationPaused || !environmentalAudioApplicationFocused ||
            !Application.isFocused || AudioListener.pause || Time.timeScale <= 0f ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused);
        environmentalAudio?.Tick(environmentalTest, environmentalEventTime, environmentalLoad, paused);
    }

    private void OnApplicationPause(bool paused)
    {
        environmentalAudioApplicationPaused = paused;
        if (paused) environmentalAudio?.Suspend();
        else RefreshEnvironmentalAudio();
    }

    private void OnApplicationFocus(bool focused)
    {
        environmentalAudioApplicationFocused = focused;
        if (!focused) environmentalAudio?.Suspend();
        else RefreshEnvironmentalAudio();
    }
}
