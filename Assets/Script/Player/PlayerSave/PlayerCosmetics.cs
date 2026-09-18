using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CosmeticItem
{
    public string cosmeticID;
    public GameObject cosmeticModel;
}

[Serializable]
public class CosmeticModelBinding
{
    public string cosmeticID;
    public CosmeticCategory category;
    public List<GameObject> models = new List<GameObject>();
    public bool defaultWhenEmpty;
}

public static class CosmeticBindingUtility
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    // These accessories occupy the same volume as the full hair meshes.  Until
    // dedicated "under hat" hair variants exist, hiding hair is the only
    // deterministic way to prevent it from cutting through the headwear.
    private static readonly HashSet<string> HeadwearIDs = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "EngineeringHardHat",
        "Accessory_SmallCap",
        "Accessory_LargeCap"
    };

    public static void Apply(IList<CosmeticModelBinding> bindings, CosmeticLoadoutData loadout)
    {
        if (bindings == null || loadout == null) return;

        // Resolve one binding per category before changing any GameObjects.
        // The previous single-pass implementation could leave authored/default
        // objects visible when a binding was missing or duplicated.
        Dictionary<CosmeticCategory, CosmeticModelBinding> selected =
            new Dictionary<CosmeticCategory, CosmeticModelBinding>();
        foreach (CosmeticCategory category in Enum.GetValues(typeof(CosmeticCategory)))
        {
            string selectedID = loadout.GetID(category)?.Trim();
            CosmeticModelBinding exact = null;
            CosmeticModelBinding fallback = null;

            for (int i = 0; i < bindings.Count; i++)
            {
                CosmeticModelBinding binding = bindings[i];
                if (binding == null || binding.category != category) continue;
                if (fallback == null && binding.defaultWhenEmpty) fallback = binding;
                if (!string.IsNullOrWhiteSpace(selectedID) &&
                    string.Equals(binding.cosmeticID?.Trim(), selectedID,
                        StringComparison.OrdinalIgnoreCase))
                {
                    exact = binding;
                    break;
                }
            }

            selected[category] = exact ?? fallback;
        }

        // Always clear the whole wardrobe first.  This also fixes prefabs whose
        // FBX authored several hair, shirt, or pants variants as active.
        for (int i = 0; i < bindings.Count; i++)
        {
            CosmeticModelBinding binding = bindings[i];
            if (binding == null || binding.models == null) continue;
            foreach (GameObject model in binding.models)
            {
                if (model == null) continue;
                model.SetActive(false);
            }
        }

        string accessoryID = selected.TryGetValue(CosmeticCategory.Accessories,
            out CosmeticModelBinding accessory)
            ? accessory?.cosmeticID?.Trim()
            : loadout.accessoriesID?.Trim();
        bool hideHairForHeadwear = !string.IsNullOrWhiteSpace(accessoryID) &&
                                   HeadwearIDs.Contains(accessoryID);

        foreach (KeyValuePair<CosmeticCategory, CosmeticModelBinding> choice in selected)
        {
            CosmeticModelBinding binding = choice.Value;
            if (binding == null || binding.models == null) continue;
            if (choice.Key == CosmeticCategory.Hair && hideHairForHeadwear) continue;

            Color color = loadout.GetColor(choice.Key);
            foreach (GameObject model in binding.models)
            {
                if (model == null) continue;
                model.SetActive(true);
                ApplyColor(model, color);
            }
        }
    }

    private static void ApplyColor(GameObject model, Color color)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (material == null) continue;
                if (color.a <= 0.001f)
                {
                    // Clear the wardrobe override so the original imported
                    // material colors are used again.
                    renderer.SetPropertyBlock(null, index);
                    continue;
                }
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, index);
                if (material.HasProperty(BaseColor)) block.SetColor(BaseColor, color);
                else if (material.HasProperty(ColorProperty)) block.SetColor(ColorProperty, color);
                renderer.SetPropertyBlock(block, index);
            }
        }
    }
}

public class PlayerCosmetics : MonoBehaviour
{
    public static PlayerCosmetics Instance { get; private set; }
    public static event Action<string> EquippedHatChanged;
    public static event Action<CosmeticLoadoutData> LoadoutChanged;

    [Header("Legacy Hat Library")]
    public List<CosmeticItem> hats = new List<CosmeticItem>();

    [Header("Categorized Model Bindings")]
    public List<CosmeticModelBinding> cosmeticBindings = new List<CosmeticModelBinding>();

    private void Awake() => Instance = this;
    private void Start() => RefreshCosmetics();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RefreshCosmetics()
    {
        if (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null)
            return;

        CosmeticLoadoutData loadout = PlayerDataManager.Instance.GetCosmeticLoadoutCopy();
        ApplyLoadout(loadout);
        EquippedHatChanged?.Invoke(loadout.accessoriesID ?? string.Empty);
        LoadoutChanged?.Invoke(loadout.Clone());
    }

    public void ApplyLoadout(CosmeticLoadoutData loadout)
    {
        if (loadout == null) return;
        CosmeticBindingUtility.Apply(cosmeticBindings, loadout);

        foreach (CosmeticItem hat in hats)
        {
            if (hat == null || hat.cosmeticModel == null) continue;
            bool visible = string.Equals(hat.cosmeticID?.Trim(),
                loadout.accessoriesID?.Trim(), StringComparison.Ordinal);
            hat.cosmeticModel.SetActive(visible);
        }
    }

    public void UnlockAndEquipHat(string hatID)
    {
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.UnlockCosmeticReward(hatID, true);
        RefreshCosmetics();
    }
}
