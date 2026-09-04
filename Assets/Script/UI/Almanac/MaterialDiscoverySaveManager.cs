using System.Collections.Generic;

/// <summary>
/// Almanac-facing facade for bridge-material discovery. PlayerDataManager remains
/// the only owner of the JSON save file.
/// </summary>
public static class MaterialDiscoverySaveManager
{
    public static bool Discover(BridgeMaterialSO material)
    {
        if (material == null || PlayerDataManager.Instance == null) return false;

        bool discovered = PlayerDataManager.Instance.DiscoverMaterial(material.Id);
        if (discovered)
            AchievementPopupNotification.NotifyAlmanacEntry(
                material.GetDisplayName(),
                "Material",
                material.materialIcon);

        return discovered;
    }

    public static bool IsDiscovered(BridgeMaterialSO material)
    {
        return material != null && PlayerDataManager.Instance != null &&
               PlayerDataManager.Instance.IsMaterialDiscovered(material.Id);
    }

    public static IReadOnlyList<string> GetDiscoveredIds()
    {
        PlayerData data = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.CurrentData
            : null;

        return data != null && data.discoveredMaterialIds != null
            ? data.discoveredMaterialIds
            : (IReadOnlyList<string>)System.Array.Empty<string>();
    }
}
