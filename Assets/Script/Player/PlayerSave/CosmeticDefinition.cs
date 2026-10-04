using System;
using System.Collections.Generic;
using UnityEngine;

public enum CosmeticCategory
{
    Accessories,
    Hair,
    Shirt,
    Pants,
    Shoes
}

[Serializable]
public sealed class CosmeticMaterialColor
{
    public string itemID;
    public string slot;
    // Compact, lossless Color32 RGBA. 00000000 explicitly restores authored color.
    public string rgba;
}

[Serializable]
public class CosmeticLoadoutData
{
    private static readonly HashSet<string> HeadwearAccessoryIDs = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "EngineeringHardHat",
        "Accessory_SmallCap",
        "Accessory_LargeCap"
    };

    // Kept for backward compatibility with existing saves and the original
    // hat-only reward flow. New saves use accessoryIDs for multi-equip.
    public string accessoriesID = string.Empty;
    public List<string> accessoryIDs = new List<string>();
    public string hairID = string.Empty;
    public string shirtID = string.Empty;
    public string pantsID = string.Empty;
    public string shoesID = string.Empty;

    // Alpha zero means "use the model's original authored materials".
    public Color accessoriesColor = Color.clear;
    public Color hairColor = Color.clear;
    public Color shirtColor = Color.clear;
    public Color pantsColor = Color.clear;
    public Color shoesColor = Color.clear;

    // Optional extension: old saves retain their category colors until edited.
    public List<CosmeticMaterialColor> materialColors = new List<CosmeticMaterialColor>();

    public Color GetMaterialColor(string itemID, string slot, CosmeticCategory category)
    {
        CosmeticMaterialColor saved = materialColors?.Find(entry => entry != null &&
            string.Equals(entry.itemID, itemID, StringComparison.OrdinalIgnoreCase) && entry.slot == slot);
        if (saved != null && ColorUtility.TryParseHtmlString("#" + saved.rgba, out Color color))
            return color;
        return GetColor(category);
    }

    public void SetMaterialColor(string itemID, string slot, Color color)
    {
        if (string.IsNullOrWhiteSpace(itemID) || string.IsNullOrWhiteSpace(slot)) return;
        if (materialColors == null) materialColors = new List<CosmeticMaterialColor>();
        CosmeticMaterialColor saved = materialColors.Find(entry => entry != null &&
            string.Equals(entry.itemID, itemID, StringComparison.OrdinalIgnoreCase) && entry.slot == slot);
        if (saved == null)
        {
            saved = new CosmeticMaterialColor { itemID = itemID, slot = slot };
            materialColors.Add(saved);
        }
        saved.rgba = ColorUtility.ToHtmlStringRGBA(color);
    }

    public CosmeticLoadoutData EquippedAppearanceCopy()
    {
        CosmeticLoadoutData copy = Clone();
        if (copy.materialColors != null)
            copy.materialColors.RemoveAll(entry => entry == null ||
                !(copy.IsAccessoryEquipped(entry.itemID) || entry.itemID == copy.hairID ||
                  entry.itemID == copy.shirtID || entry.itemID == copy.pantsID || entry.itemID == copy.shoesID));
        return copy;
    }

    public CosmeticLoadoutData Clone()
    {
        CosmeticLoadoutData clone =
            JsonUtility.FromJson<CosmeticLoadoutData>(JsonUtility.ToJson(this));
        clone.NormalizeAccessories();
        return clone;
    }

    public IReadOnlyList<string> GetAccessoryIDs()
    {
        NormalizeAccessories();
        return accessoryIDs;
    }

    public bool IsAccessoryEquipped(string cosmeticID)
    {
        if (string.IsNullOrWhiteSpace(cosmeticID)) return false;
        string wanted = cosmeticID.Trim();
        if (string.Equals(wanted, "Accessory_None", StringComparison.OrdinalIgnoreCase))
            return GetAccessoryIDs().Count == 0;

        NormalizeAccessories();
        return accessoryIDs.Exists(id =>
            string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase));
    }

    public void SetAccessoryEquipped(string cosmeticID, bool equipped)
    {
        NormalizeAccessories();
        string normalized = cosmeticID != null ? cosmeticID.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)) return;

        if (string.Equals(normalized, "Accessory_None", StringComparison.OrdinalIgnoreCase))
        {
            if (equipped) accessoryIDs.Clear();
            accessoriesID = "Accessory_None";
            return;
        }

        if (equipped && HeadwearAccessoryIDs.Contains(normalized))
            accessoryIDs.RemoveAll(id => HeadwearAccessoryIDs.Contains(id) &&
                                         !string.Equals(id, normalized,
                                             StringComparison.OrdinalIgnoreCase));

        int existingIndex = accessoryIDs.FindIndex(id =>
            string.Equals(id, normalized, StringComparison.OrdinalIgnoreCase));
        if (equipped && existingIndex < 0) accessoryIDs.Add(normalized);
        else if (!equipped && existingIndex >= 0) accessoryIDs.RemoveAt(existingIndex);

        if (equipped)
            accessoriesID = normalized;
        else if (string.Equals(accessoriesID, normalized, StringComparison.OrdinalIgnoreCase))
            accessoriesID = accessoryIDs.Count > 0 ? accessoryIDs[0] : "Accessory_None";
    }

    public void NormalizeAccessories()
    {
        if (accessoryIDs == null) accessoryIDs = new List<string>();

        // A missing/empty list indicates an old save. Import its one equipped
        // accessory once, while treating the old None item as an empty set.
        if (accessoryIDs.Count == 0 && !string.IsNullOrWhiteSpace(accessoriesID) &&
            !string.Equals(accessoriesID.Trim(), "Accessory_None",
                StringComparison.OrdinalIgnoreCase))
            accessoryIDs.Add(accessoriesID.Trim());

        List<string> normalized = new List<string>();
        bool hasHeadwear = false;
        foreach (string savedID in accessoryIDs)
        {
            if (string.IsNullOrWhiteSpace(savedID)) continue;
            string id = savedID.Trim();
            if (string.Equals(id, "Accessory_None", StringComparison.OrdinalIgnoreCase)) continue;
            if (HeadwearAccessoryIDs.Contains(id))
            {
                if (hasHeadwear) continue;
                hasHeadwear = true;
            }
            if (!normalized.Exists(existing =>
                    string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)))
                normalized.Add(id);
        }
        accessoryIDs = normalized;

        if (accessoryIDs.Count == 0)
            accessoriesID = "Accessory_None";
        else if (string.IsNullOrWhiteSpace(accessoriesID) ||
                 !accessoryIDs.Exists(id => string.Equals(id, accessoriesID.Trim(),
                     StringComparison.OrdinalIgnoreCase)))
            accessoriesID = accessoryIDs[0];
    }

    public string GetID(CosmeticCategory category)
    {
        switch (category)
        {
            case CosmeticCategory.Accessories:
                NormalizeAccessories();
                return accessoriesID;
            case CosmeticCategory.Hair: return hairID;
            case CosmeticCategory.Shirt: return shirtID;
            case CosmeticCategory.Pants: return pantsID;
            case CosmeticCategory.Shoes: return shoesID;
            default: return string.Empty;
        }
    }

    public void SetID(CosmeticCategory category, string value)
    {
        value = value != null ? value.Trim() : string.Empty;
        switch (category)
        {
            case CosmeticCategory.Accessories:
                accessoryIDs = new List<string>();
                if (!string.IsNullOrWhiteSpace(value) &&
                    !string.Equals(value, "Accessory_None", StringComparison.OrdinalIgnoreCase))
                    accessoryIDs.Add(value);
                accessoriesID = string.IsNullOrWhiteSpace(value) ? "Accessory_None" : value;
                break;
            case CosmeticCategory.Hair: hairID = value; break;
            case CosmeticCategory.Shirt: shirtID = value; break;
            case CosmeticCategory.Pants: pantsID = value; break;
            case CosmeticCategory.Shoes: shoesID = value; break;
        }
    }

    public Color GetColor(CosmeticCategory category)
    {
        switch (category)
        {
            case CosmeticCategory.Accessories: return accessoriesColor;
            case CosmeticCategory.Hair: return hairColor;
            case CosmeticCategory.Shirt: return shirtColor;
            case CosmeticCategory.Pants: return pantsColor;
            case CosmeticCategory.Shoes: return shoesColor;
            default: return Color.clear;
        }
    }

    public void SetColor(CosmeticCategory category, Color value)
    {
        switch (category)
        {
            case CosmeticCategory.Accessories: accessoriesColor = value; break;
            case CosmeticCategory.Hair: hairColor = value; break;
            case CosmeticCategory.Shirt: shirtColor = value; break;
            case CosmeticCategory.Pants: pantsColor = value; break;
            case CosmeticCategory.Shoes: shoesColor = value; break;
        }
    }
}

[CreateAssetMenu(fileName = "CosmeticDefinition", menuName = "Civil Craft/Cosmetic Definition")]
public sealed class CosmeticDefinition : ScriptableObject
{
    [Header("Permanent Identity")]
    [SerializeField] private string permanentID;
    public string displayName = "New Cosmetic";
    public Sprite icon;
    public CosmeticCategory category;

    [Header("Character Binding")]
    [Tooltip("Stable model binding used by gameplay, Almanac, and loading-screen characters.")]
    public string modelID;

    [Header("Wardrobe")]
    public Color[] availableColors = { Color.clear };
    public bool unlockedByDefault;

    public string PermanentID => permanentID;

#if UNITY_EDITOR
    public void EditorConfigure(string id, string label, CosmeticCategory newCategory,
        string newModelID, bool defaultUnlocked, Color[] colors)
    {
        permanentID = id;
        displayName = label;
        category = newCategory;
        modelID = newModelID;
        unlockedByDefault = defaultUnlocked;
        availableColors = colors != null && colors.Length > 0 ? colors : new[] { Color.clear };
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void OnValidate()
    {
        permanentID = permanentID != null ? permanentID.Trim() : string.Empty;
        modelID = modelID != null ? modelID.Trim() : string.Empty;
        if (availableColors == null || availableColors.Length == 0)
            availableColors = new[] { Color.clear };
    }
#endif
}
