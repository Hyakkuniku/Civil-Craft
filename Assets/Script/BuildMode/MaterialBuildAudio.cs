using UnityEngine;

/// <summary>Material handling and contact sounds at accepted player actions.</summary>
public static class MaterialBuildAudio
{
    private const float PlacementBurstSeconds = 0.10f;
    private const int MaximumPlacementVoices = 4;
    private static BridgeMaterialSO lastEquippedMaterial;
    private static int lastEquipFrame = -1;
    private static float placementBurstStarted = float.NegativeInfinity;
    private static int placementVoices;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        lastEquippedMaterial = null;
        lastEquipFrame = -1;
        placementBurstStarted = float.NegativeInfinity;
        placementVoices = 0;
    }

    public static void PlayEquip(BridgeMaterialSO material, string fallbackSfxId = "Click")
    {
        if (!CanPlay(material)) return;
        string id = string.IsNullOrWhiteSpace(material.equipSfxId) ? fallbackSfxId : material.equipSfxId;
        if (string.IsNullOrWhiteSpace(id)) return;
        // A toolbar Button and its pointer handler can both select on one click.
        if (lastEquippedMaterial == material && lastEquipFrame == Time.frameCount) return;
        lastEquippedMaterial = material;
        lastEquipFrame = Time.frameCount;
        AudioManager.Instance.PlaySFX(id);
    }

    public static void PlayPlace(BridgeMaterialSO material, string fallbackSfxId = "PlaceBar")
    {
        if (!CanPlay(material)) return;
        string id = string.IsNullOrWhiteSpace(material.placeSfxId) ? fallbackSfxId : material.placeSfxId;
        if (string.IsNullOrWhiteSpace(id)) return;
        float now = Time.unscaledTime;
        if (now < placementBurstStarted || now - placementBurstStarted >= PlacementBurstSeconds)
        {
            placementBurstStarted = now;
            placementVoices = 0;
        }
        // Large blueprint pastes must not stack hundreds of contact sounds.
        if (placementVoices >= MaximumPlacementVoices) return;
        placementVoices++;
        AudioManager.Instance.PlaySFX(id);
    }

    private static bool CanPlay(BridgeMaterialSO material)
    {
        return Application.isPlaying && material != null && AudioManager.Instance != null &&
               Time.timeScale > 0f && !AudioListener.pause;
    }
}
