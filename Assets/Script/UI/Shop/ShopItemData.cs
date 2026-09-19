using System;
using UnityEngine;

public enum ShopCategory
{
    Accessory,
    Hairstyle,
    Shirt,
    Pants,
    Shoes
}

[CreateAssetMenu(fileName = "NewShopItem", menuName = "Civil Craft/Shop Item")]
public class ShopItemData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField, Tooltip("Permanent save ID. Do not change this after releasing the item.")]
    private string itemId;

    public string itemName = "New Shop Item";

    [TextArea(2, 5)]
    public string description = "Describe this item.";

    [Header("Presentation")]
    public Sprite icon;
    public ShopCategory category = ShopCategory.Accessory;

    [Header("Optional Wardrobe Unlock")]
    [Tooltip("When assigned, purchasing this item unlocks the linked wardrobe cosmetic.")]
    public CosmeticDefinition cosmeticDefinition;

    [Header("Purchase")]
    [Min(0)] public int price = 100;

    [Tooltip("Enable only for consumables or bundles that may be bought repeatedly.")]
    public bool canPurchaseMultipleTimes;

    public string ItemId => itemId;

#if UNITY_EDITOR
    private void OnValidate()
    {
        price = Mathf.Max(0, price);

        // The wardrobe definition owns the visual/model pairing. Keeping the
        // shop presentation derived from it prevents a card from advertising
        // one item while unlocking a different 3D model.
        if (cosmeticDefinition != null)
        {
            icon = cosmeticDefinition.icon;
            switch (cosmeticDefinition.category)
            {
                case CosmeticCategory.Hair: category = ShopCategory.Hairstyle; break;
                case CosmeticCategory.Shirt: category = ShopCategory.Shirt; break;
                case CosmeticCategory.Pants: category = ShopCategory.Pants; break;
                case CosmeticCategory.Shoes: category = ShopCategory.Shoes; break;
                default: category = ShopCategory.Accessory; break;
            }
        }

        if (string.IsNullOrWhiteSpace(itemId))
        {
            itemId = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
            return;
        }

        // Duplicating a ScriptableObject also duplicates its serialized ID.
        // Regenerate this asset's ID so copied catalog entries remain independent.
        string currentPath = UnityEditor.AssetDatabase.GetAssetPath(this);
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ShopItemData"))
        {
            string otherPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (otherPath == currentPath) continue;

            ShopItemData other = UnityEditor.AssetDatabase.LoadAssetAtPath<ShopItemData>(otherPath);
            if (other == null || other.itemId != itemId) continue;

            itemId = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
            break;
        }
    }
#endif
}
