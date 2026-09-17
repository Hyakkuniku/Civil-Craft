using System;
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
public class CosmeticLoadoutData
{
    public string accessoriesID = string.Empty;
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

    public CosmeticLoadoutData Clone() =>
        JsonUtility.FromJson<CosmeticLoadoutData>(JsonUtility.ToJson(this));

    public string GetID(CosmeticCategory category)
    {
        switch (category)
        {
            case CosmeticCategory.Accessories: return accessoriesID;
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
            case CosmeticCategory.Accessories: accessoriesID = value; break;
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
