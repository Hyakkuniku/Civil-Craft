using System;
using UnityEngine;

/// <summary>Two independent loops driven by the same frozen event and load as the simulation.</summary>
public sealed class BridgeEnvironmentalAudio : IDisposable
{
    private readonly BridgeEnvironmentalAudioSettings settings;
    private readonly VehicleSimulationLoop wind;
    private readonly VehicleSimulationLoop earthquake;
    private readonly VehicleSimulationSound windSound = new VehicleSimulationSound();
    private readonly VehicleSimulationSound earthquakeSound = new VehicleSimulationSound();
    private bool finished;

    public BridgeEnvironmentalAudio(Transform owner, BridgeEnvironmentalAudioSettings settings)
    {
        this.settings = settings;
        wind = new VehicleSimulationLoop(owner, "Wind Stress Audio (Runtime)");
        earthquake = new VehicleSimulationLoop(owner, "Earthquake Stress Audio (Runtime)");
    }

    public void Tick(BridgeEnvironmentalTest profile, float eventTime,
        DeterministicBridgeStressSolver.EnvironmentalLoad load, bool paused)
    {
        if (finished || settings == null || profile == null || !profile.IsEnabled ||
            eventTime <= 0f || eventTime >= profile.duration)
        {
            Stop();
            return;
        }

        float edge = Mathf.Min(.4f, profile.duration * .25f);
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(eventTime, profile.duration - eventTime) / edge));
        windSound.clip = settings.windLoop;
        windSound.volume = settings.windVolume * Mathf.Sqrt(Mathf.Clamp01(Mathf.Abs((float)load.WindPressure) / 2000f));
        earthquakeSound.clip = settings.earthquakeLoop;
        earthquakeSound.volume = settings.earthquakeVolume * Mathf.Sqrt(Mathf.Clamp01(profile.earthquakeStrengthG / .8f)) * fade;
        wind.Tick(windSound, profile.HasWind, paused);
        earthquake.Tick(earthquakeSound, profile.HasEarthquake, paused);
    }

    public void Suspend()
    {
        wind.Suspend();
        earthquake.Suspend();
    }

    public void Stop()
    {
        wind.Stop();
        earthquake.Stop();
    }

    public void Finish()
    {
        finished = true;
        Stop();
    }

    public void Dispose()
    {
        finished = true;
        wind.Dispose();
        earthquake.Dispose();
    }
}
