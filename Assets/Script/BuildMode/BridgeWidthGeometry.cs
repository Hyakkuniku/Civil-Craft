using System.Collections.Generic;
using UnityEngine;

/// <summary>Resolves per-contract dimensions from the same authored geometry for builds and replicas.</summary>
public static class BridgeWidthGeometry
{
    private static readonly Dictionary<GameObject, Bounds> PrefabBounds = new Dictionary<GameObject, Bounds>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => PrefabBounds.Clear();

    public static Bounds GetPrefabBounds(GameObject prefab)
    {
        if (prefab == null) return default;
        if (PrefabBounds.TryGetValue(prefab, out Bounds bounds)) return bounds;

        Transform root = prefab.transform;
        Matrix4x4 model = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale) * root.worldToLocalMatrix;
        bool found = false;
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;
            Matrix4x4 toMember = model * filter.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            Vector3 min = mesh.min;
            Vector3 max = mesh.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toMember.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (found) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        if (Application.isPlaying) PrefabBounds.Add(prefab, bounds);
        return bounds;
    }

    public static float GetRoadScale(BridgeMaterialSO material, float wideWidth) =>
        material != null && material.isRoad ? GetPierWidthScale(material.segmentPrefab, wideWidth) : 1f;

    public static float GetCostPerMeter(BridgeMaterialSO material, float wideWidth) =>
        material.costPerMeter * GetRoadScale(material, wideWidth);

    public static float GetPlacedCostPerMeter(BridgeMaterialSO material, float wideWidth) =>
        GetCostPerMeter(material, wideWidth) * (material.isDualBeam ? 2f : 1f);

    public static float GetPlacedMassPerMeter(BridgeMaterialSO material, float wideWidth) =>
        material.GetPlacedMassPerMeter() * GetRoadScale(material, wideWidth);

    public static float GetPierWidthScale(GameObject prefab, float wideWidth)
    {
        if (wideWidth <= 0f) return 1f;
        float originalWidth = GetPrefabBounds(prefab).size.z;
        return originalWidth > .001f ? wideWidth / originalWidth : 1f;
    }

    public static float GetSideOffset(BridgeMaterialSO material, float wideWidth)
    {
        if (material == null || !material.isDualBeam) return 0f;
        if (wideWidth <= 0f || material.isRoad) return material.zOffset;
        Bounds shape = GetPrefabBounds(material.segmentPrefab);
        float halfDepth = Mathf.Max(Mathf.Abs(shape.min.z), Mathf.Abs(shape.max.z));
        return wideWidth * .5f + Mathf.Max(.45f, halfDepth + .025f);
    }

    public static float GetSegmentDepthScale(BridgeMaterialSO material, float wideWidth)
    {
        if (material.isRoad) return GetRoadScale(material, wideWidth);
        return material.isPier && material.pierCapPrefab == null ? GetPierWidthScale(material.segmentPrefab, wideWidth) : 1f;
    }

    public static float GetCenteredDepthOffset(GameObject prefab, float depthScale, float wideWidth) =>
        wideWidth > 0f ? -GetPrefabBounds(prefab).center.z * depthScale : 0f;

    public static float GetSegmentDepthPosition(BridgeMaterialSO material, float wideWidth, int index)
    {
        if (material.isDualBeam) return (index == 0 ? 1f : -1f) * GetSideOffset(material, wideWidth);
        return material.isRoad || (material.isPier && material.pierCapPrefab == null)
            ? GetCenteredDepthOffset(material.segmentPrefab, GetSegmentDepthScale(material, wideWidth), wideWidth) : 0f;
    }
}
