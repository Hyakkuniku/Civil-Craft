using System.Collections.Generic;
using UnityEngine;

/// <summary>Prices logical construction members, not mesh-prefab Bar components or other sites.</summary>
public sealed class BridgeConstructionReceipt
{
    private readonly Dictionary<BridgeMaterialSO, float> materialMeters = new Dictionary<BridgeMaterialSO, float>();
    public IReadOnlyDictionary<BridgeMaterialSO, float> MaterialMeters => materialMeters;
    public float TotalCost { get; private set; }

    public static bool IsLogicalMember(Bar bar) => bar != null && !bar.IsConstructionPreview &&
        (bar.transform.parent == null || bar.transform.parent.GetComponentInParent<Bar>() == null);

    public static float ConstructionLength(Bar bar) => bar.startPoint != null && bar.endPoint != null
        ? Vector3.Distance(bar.startPoint.transform.position, bar.endPoint.transform.position)
        : Mathf.Max(0f, bar.currentLength);

    public static float ConstructionCost(Bar bar) => bar != null && !bar.IsConstructionPreview && bar.materialData != null
        ? ConstructionLength(bar) * bar.materialData.costPerMeter * (bar.materialData.isDualBeam ? 2 : 1) : 0f;

    public static BridgeConstructionReceipt Capture(BuildLocation location, IEnumerable<Bar> candidates,
        Bar preview = null, bool includeDisabled = false)
    {
        var receipt = new BridgeConstructionReceipt();
        if (location == null || candidates == null) return receipt;
        var counted = new HashSet<Bar>();
        double total = 0;
        foreach (Bar bar in candidates)
        {
            if (!IsLogicalMember(bar) || bar == preview || !bar.gameObject.activeInHierarchy ||
                (!includeDisabled && !bar.enabled) || bar.materialData == null || !location.Owns(bar) || !counted.Add(bar)) continue;
            float meters = ConstructionLength(bar) * (bar.materialData.isDualBeam ? 2 : 1);
            receipt.materialMeters.TryGetValue(bar.materialData, out float previous);
            receipt.materialMeters[bar.materialData] = previous + meters;
            total += (double)ConstructionLength(bar) * bar.materialData.costPerMeter * (bar.materialData.isDualBeam ? 2 : 1);
        }
        receipt.TotalCost = (float)total;
        return receipt;
    }
}
