using System;
using UnityEngine;

[Serializable]
public sealed class BridgeEnvironmentalTest
{
    [Range(0f, 2f), Tooltip("Peak wind pressure in kPa. Zero disables wind. Gusts push along the span and down on the deck.")]
    public float windPressureKPa;

    [Tooltip("Reverse the gust to blow from right to left in the front build view.")]
    public bool windFromRight;

    [Range(0f, .8f), Tooltip("Peak horizontal shaking acceleration in multiples of gravity. Zero disables earthquakes.")]
    public float earthquakeStrengthG;

    [Range(0f, 120f), Tooltip("Simulation seconds after the bridge settles before the event starts.")]
    public float startDelay = 1f;

    [Range(1f, 30f), Tooltip("How long the gusts and shaking last in simulation seconds.")]
    public float duration = 6f;

    public bool HasWind => float.IsFinite(windPressureKPa) && windPressureKPa > 0f;
    public bool HasEarthquake => float.IsFinite(earthquakeStrengthG) && earthquakeStrengthG > 0f;
    public bool IsEnabled => HasWind || HasEarthquake;

    public BridgeEnvironmentalTest Freeze() => new BridgeEnvironmentalTest
    {
        windPressureKPa = HasWind ? Mathf.Min(windPressureKPa, 2f) : 0f,
        windFromRight = windFromRight,
        earthquakeStrengthG = HasEarthquake ? Mathf.Min(earthquakeStrengthG, .8f) : 0f,
        startDelay = float.IsFinite(startDelay) ? Mathf.Clamp(startDelay, 0f, 120f) : 1f,
        duration = float.IsFinite(duration) ? Mathf.Clamp(duration, 1f, 30f) : 6f
    };

    public DeterministicBridgeStressSolver.EnvironmentalLoad Sample(float eventTime)
    {
        if (eventTime <= 0f || eventTime >= duration) return default;
        float edge = Mathf.Min(.4f, duration * .25f);
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(eventTime, duration - eventTime) / edge));
        float gust = .4f + .6f * Mathf.Pow(.5f + .5f * Mathf.Sin(eventTime * 4.3f), 2f);
        float pressure = windPressureKPa * 1000f * fade * gust * (windFromRight ? -1f : 1f);
        float shake = earthquakeStrengthG * 9.81f * fade;
        // Effective inertial forces in the bridge's XY plane. The acceleration
        // changes sign as the ground shakes in opposite directions.
        float x = shake * (.75f * Mathf.Sin(eventTime * 10.4f) + .25f * Mathf.Sin(eventTime * 17.3f));
        float y = shake * .45f * Mathf.Sin(eventTime * 13.7f + .9f);
        return new DeterministicBridgeStressSolver.EnvironmentalLoad(pressure, x, y);
    }
}
