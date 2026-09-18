using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>IDs only: the debugger does not need to load every item's models.</summary>
[Serializable]
public sealed class DeveloperItemCatalog
{
    public List<string> shopItemIDs = new List<string>();
    public List<string> cosmeticIDs = new List<string>();
    public const string ResourceName = "DeveloperItemCatalog";

    public static DeveloperItemCatalog Load()
    {
#if UNITY_EDITOR
        return CollectEditorCatalog();
#else
        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        return asset != null ? JsonUtility.FromJson<DeveloperItemCatalog>(asset.text) : null;
#endif
    }

#if UNITY_EDITOR
    public static DeveloperItemCatalog CollectEditorCatalog()
    {
        var catalog = new DeveloperItemCatalog();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ShopItemData", new[] { "Assets" }))
        {
            var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ShopItemData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (item == null || item.cosmeticDefinition == null) continue;
            AddID(catalog.shopItemIDs, item.ItemId);
            if (item.cosmeticDefinition != null)
                AddID(catalog.cosmeticIDs, item.cosmeticDefinition.PermanentID);
        }
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:CosmeticDefinition", new[] { "Assets" }))
        {
            var item = UnityEditor.AssetDatabase.LoadAssetAtPath<CosmeticDefinition>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) AddID(catalog.cosmeticIDs, item.PermanentID);
        }
        catalog.shopItemIDs.Sort(StringComparer.Ordinal);
        catalog.cosmeticIDs.Sort(StringComparer.Ordinal);
        return catalog;
    }

    private static void AddID(List<string> ids, string id)
    {
        if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id.Trim())) ids.Add(id.Trim());
    }
#endif
}
