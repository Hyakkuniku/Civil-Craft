using UnityEngine;

/// <summary>Scene-owned, explicitly baked coverage; never changes world-object visibility.</summary>
[DisallowMultipleComponent]
public sealed class CanyonOcclusionProfile : MonoBehaviour
{
    public const string TargetSceneName = "CanyonCrossing";

    [Header("Developer A/B Switch")]
    [Tooltip("Off until visual checks and Android frame-time comparisons pass. Does not change player saves.")]
    public bool enableGameplayOcclusion;
    [HideInInspector] public bool bakeVerified;
    [HideInInspector] public Object bakedData;

    [Header("Explicit Scene References")]
    public Camera gameplayCamera;
    public MeshRenderer[] approvedOccluders = new MeshRenderer[0];
    public OcclusionArea[] viewVolumes = new OcclusionArea[0];

    public bool OcclusionRequested
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return DevelopmentPerformanceSettings.OcclusionOverride ?? enableGameplayOcclusion;
#else
            // Production rollout remains off until Android performance is approved,
            // even if the Inspector test switch was saved on during an A/B run.
            return false;
#endif
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnEnable() => DevelopmentPerformanceSettings.Register(this);
    private void OnDisable() => DevelopmentPerformanceSettings.Unregister(this);
#endif

    public bool ContainsCameraPosition(Vector3 worldPosition)
    {
        if (!IsFinite(worldPosition) || viewVolumes == null) return false;
        foreach (OcclusionArea area in viewVolumes)
        {
            // Unity 2022.3 exposes the area bounds but not its Inspector-only
            // Is View Volume switch. The Editor bake validator checks that switch.
            if (area == null || !area.gameObject.activeInHierarchy ||
                area.gameObject.scene != gameObject.scene) continue;
            Vector3 point = area.transform.InverseTransformPoint(worldPosition);
            Vector3 size = area.size;
            if (!IsFinite(point) || !IsFinite(area.center) || !IsFinite(size) ||
                size.x <= 0f || size.y <= 0f || size.z <= 0f) continue;
            Vector3 offset = point - area.center;
            if (Mathf.Abs(offset.x) <= size.x * .5f && Mathf.Abs(offset.y) <= size.y * .5f &&
                Mathf.Abs(offset.z) <= size.z * .5f) return true;
        }
        return false;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
