using UnityEngine;

/// <summary>Shared material fracture playback for simulations and mesh-only spectators.</summary>
public static class MaterialBreakAudio
{
    private const float BurstSeconds = 0.10f;
    private const int MaximumVoicesPerBurst = 4;
    private static float burstStarted = float.NegativeInfinity;
    private static int burstVoices;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        burstStarted = float.NegativeInfinity;
        burstVoices = 0;
    }

    public static void Play(BridgeMaterialSO material)
    {
        if (!Application.isPlaying || material == null || AudioManager.Instance == null ||
            string.IsNullOrWhiteSpace(material.breakSfxId) || Time.timeScale <= 0f || AudioListener.pause) return;

        float now = Time.unscaledTime;
        if (now < burstStarted || now - burstStarted >= BurstSeconds)
        {
            burstStarted = now;
            burstVoices = 0;
        }
        // One structural collapse can detach hundreds of members together.
        // Keep its sound bounded while retaining the first distinct fractures.
        if (burstVoices >= MaximumVoicesPerBurst) return;
        burstVoices++;
        AudioManager.Instance.PlaySFX(material.breakSfxId);
    }
}
