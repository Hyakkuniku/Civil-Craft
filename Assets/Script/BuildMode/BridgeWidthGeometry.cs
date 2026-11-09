using System.Collections.Generic;
using UnityEngine;

/// <summary>Resolves per-contract dimensions from the same authored geometry for builds and replicas.</summary>
public static class BridgeWidthGeometry
{
    private static readonly Dictionary<GameObject, Bounds> PrefabBounds = new Dictionary<GameObject, Bounds>();
    private static readonly Dictionary<float, float> PierSupportWidths = new Dictionary<float, float>();
    private static BridgeMaterialSO[] pointMaterials;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        PrefabBounds.Clear();
        PierSupportWidths.Clear();
        pointMaterials = null;
    }

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

    /// <summary>Visual bearing width, including the paired rigid girders outside the roadway.</summary>
    public static float GetPierSupportWidth(float wideWidth)
    {
        if (float.IsNaN(wideWidth) || float.IsInfinity(wideWidth) || wideWidth < 0f) wideWidth = 0f;
        if (Application.isPlaying && PierSupportWidths.TryGetValue(wideWidth, out float cached)) return cached;
        if (pointMaterials == null) pointMaterials = Resources.LoadAll<BridgeMaterialSO>(string.Empty);

        const float bearingEdgeMargin = .075f;
        float width = wideWidth;
        foreach (BridgeMaterialSO material in pointMaterials)
        {
            if (material == null || material.segmentPrefab == null) continue;
            Bounds shape = GetPrefabBounds(material.segmentPrefab);
            if (material.isRoad)
            {
                if (wideWidth <= 0f) width = Mathf.Max(width, shape.size.z);
                continue;
            }
            if (!material.isDualBeam || material.isPier || material.isRope || material.useSpring) continue;
            float halfDepth = Mathf.Max(Mathf.Abs(shape.min.z), Mathf.Abs(shape.max.z));
            width = Mathf.Max(width, 2f * (Mathf.Abs(GetSideOffset(material, wideWidth)) + halfDepth));
        }
        if (width > 0f) width += bearingEdgeMargin * 2f;
        if (Application.isPlaying) PierSupportWidths[wideWidth] = width;
        return width;
    }

    /// <summary>Fits the actual flat cap face without changing road dimensions or material rates.</summary>
    public static float GetPierCapWidthScale(GameObject prefab, float wideWidth)
    {
        if (prefab == null) return 1f;
        float originalWidth = GetPrefabBounds(prefab).size.z;
        if (PierCapSeatingProfiles.TryGetTopFace(prefab, out Bounds face))
        {
            Matrix4x4 source = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale);
            float near = float.PositiveInfinity, far = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = source.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? face.min.x : face.max.x,
                    (corner & 2) == 0 ? face.min.y : face.max.y,
                    (corner & 4) == 0 ? face.min.z : face.max.z));
                near = Mathf.Min(near, point.z); far = Mathf.Max(far, point.z);
            }
            originalWidth = far - near;
        }
        return originalWidth > .001f ? Mathf.Max(1f, GetPierSupportWidth(wideWidth) / originalWidth) : 1f;
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

    public static float GetPointVisualDepth(float wideWidth, BridgeMaterialSO material = null)
    {
        float halfDepth = wideWidth * .5f + .9f;
        if (material != null && material.isDualBeam && !material.isRoad)
        {
            Bounds shape = GetPrefabBounds(material.segmentPrefab);
            float memberDepth = Mathf.Max(Mathf.Abs(shape.min.z), Mathf.Abs(shape.max.z));
            halfDepth = Mathf.Max(halfDepth, GetSideOffset(material, wideWidth) + memberDepth + .2f);
        }
        return halfDepth * 2f;
    }

    public static float GetPointVisualDepth(ContractSO contract)
    {
        float width = contract != null ? contract.GetWideRoadWidth() : 0f;
        if (width <= 0f) return 0f;
        float depth = GetPointVisualDepth(width);
        if (contract.allowedMaterials != null && contract.allowedMaterials.Count > 0)
        {
            foreach (MaterialAllowance allowance in contract.allowedMaterials)
                if (allowance?.material != null && !contract.IsMaterialHidden(allowance.material))
                    depth = Mathf.Max(depth, GetPointVisualDepth(width, allowance.material));
        }
        else
        {
            if (pointMaterials == null) pointMaterials = Resources.LoadAll<BridgeMaterialSO>(string.Empty);
            foreach (BridgeMaterialSO material in pointMaterials)
                if (material != null && !contract.IsMaterialHidden(material))
                    depth = Mathf.Max(depth, GetPointVisualDepth(width, material));
        }
        return depth;
    }
}
