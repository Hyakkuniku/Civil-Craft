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

    // Headwear remains compatible with the selected hairstyle. It is fitted a
    // little farther around the head so the hair can stay visible without most
    // of it cutting through the hat or cap.
    private static readonly HashSet<string> HeadwearIDs = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "EngineeringHardHat",
        "Accessory_SmallCap",
        "Accessory_LargeCap"
    };

    private static readonly Dictionary<GameObject, Vector3> OriginalHeadwearScales =
        new Dictionary<GameObject, Vector3>();

    private static readonly Vector3 HeadwearFitMultiplier = new Vector3(1.1f, 1.04f, 1.1f);

    public static void Apply(IList<CosmeticModelBinding> bindings, CosmeticLoadoutData loadout)
    {
        if (bindings == null || loadout == null) return;

        // Resolve one binding for each clothing category. Accessories are
        // intentionally multi-select and are applied separately below.
        Dictionary<CosmeticCategory, CosmeticModelBinding> selected =
            new Dictionary<CosmeticCategory, CosmeticModelBinding>();
        foreach (CosmeticCategory category in Enum.GetValues(typeof(CosmeticCategory)))
        {
            if (category == CosmeticCategory.Accessories) continue;
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

        HashSet<string> selectedAccessoryIDs = new HashSet<string>(
            loadout.GetAccessoryIDs(), StringComparer.OrdinalIgnoreCase);

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
                if (HeadwearIDs.Contains(binding.cosmeticID?.Trim() ?? string.Empty))
                    RestoreHeadwearScale(model);
            }
        }

        Color accessoryColor = loadout.GetColor(CosmeticCategory.Accessories);
        for (int i = 0; i < bindings.Count; i++)
        {
            CosmeticModelBinding binding = bindings[i];
            if (binding == null || binding.category != CosmeticCategory.Accessories ||
                binding.models == null ||
                !selectedAccessoryIDs.Contains(binding.cosmeticID?.Trim() ?? string.Empty))
                continue;

            foreach (GameObject model in binding.models)
            {
                if (model == null) continue;
                model.SetActive(true);
                if (HeadwearIDs.Contains(binding.cosmeticID?.Trim() ?? string.Empty))
                    FitHeadwearOverHair(model);
                ApplyColor(model, accessoryColor);
            }
        }

        foreach (KeyValuePair<CosmeticCategory, CosmeticModelBinding> choice in selected)
        {
            CosmeticModelBinding binding = choice.Value;
            if (binding == null || binding.models == null) continue;

            Color color = loadout.GetColor(choice.Key);
            foreach (GameObject model in binding.models)
            {
                if (model == null) continue;
                model.SetActive(true);
                ApplyColor(model, color);
            }
        }
    }

    private static void FitHeadwearOverHair(GameObject model)
    {
        if (model == null) return;
        if (!OriginalHeadwearScales.TryGetValue(model, out Vector3 originalScale))
        {
            originalScale = model.transform.localScale;
            OriginalHeadwearScales[model] = originalScale;
        }

        model.transform.localScale = Vector3.Scale(originalScale, HeadwearFitMultiplier);
    }

    private static void RestoreHeadwearScale(GameObject model)
    {
        if (model != null && OriginalHeadwearScales.TryGetValue(model, out Vector3 originalScale))
            model.transform.localScale = originalScale;
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
    private const string WardrobePlayerResource = "Loading/NewCharacterPreview";

    public static PlayerCosmetics Instance { get; private set; }
    public static event Action<string> EquippedHatChanged;
    public static event Action<CosmeticLoadoutData> LoadoutChanged;

    [Header("Legacy Hat Library")]
    public List<CosmeticItem> hats = new List<CosmeticItem>();

    [Header("Categorized Model Bindings")]
    public List<CosmeticModelBinding> cosmeticBindings = new List<CosmeticModelBinding>();

    private void Awake()
    {
        Instance = this;
        UpgradeLegacyPlayerVisual();
    }
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
            bool visible = loadout.IsAccessoryEquipped(hat.cosmeticID);
            hat.cosmeticModel.SetActive(visible);
        }
    }

    public void UnlockAndEquipHat(string hatID)
    {
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.UnlockCosmeticReward(hatID, true);
        RefreshCosmetics();
    }

    private void UpgradeLegacyPlayerVisual()
    {
        // Older scenes such as Bhan House have the wardrobe IDs but no model
        // references. Their old character mesh cannot display the saved outfit.
        if (cosmeticBindings != null)
        {
            foreach (CosmeticModelBinding binding in cosmeticBindings)
                if (binding != null && binding.models != null)
                    foreach (GameObject model in binding.models)
                        if (model != null) return;
        }

        PlayerMotor motor = GetComponent<PlayerMotor>();
        Animator oldAnimator = motor != null ? motor.playerAnimator : null;
        if (oldAnimator == null) return;

        Transform oldVisual = oldAnimator.transform;
        while (oldVisual.parent != null && oldVisual.parent != transform)
            oldVisual = oldVisual.parent;
        if (oldVisual.parent != transform ||
            !oldVisual.name.StartsWith("Character_w_Clothes_v4", StringComparison.Ordinal)) return;

        GameObject wardrobePrefab = Resources.Load<GameObject>(WardrobePlayerResource);
        if (wardrobePrefab == null)
        {
            Debug.LogWarning("[PlayerCosmetics] Full wardrobe player prefab is missing; keeping the legacy model.", this);
            return;
        }

        GameObject replacement = Instantiate(wardrobePrefab, transform, false);
        replacement.name = "NewCharacterModel (Player)";
        replacement.transform.localPosition = oldVisual.localPosition;
        replacement.transform.localRotation = oldVisual.localRotation;
        replacement.transform.localScale = oldVisual.localScale;

        PlayerCosmeticMirror wardrobe = replacement.GetComponent<PlayerCosmeticMirror>();
        Animator newAnimator = replacement.GetComponentInChildren<Animator>(true);
        if (wardrobe == null || newAnimator == null || wardrobe.cosmeticBindings == null ||
            wardrobe.cosmeticBindings.Count == 0)
        {
            Destroy(replacement);
            Debug.LogWarning("[PlayerCosmetics] Full wardrobe player is incomplete; keeping the legacy model.", this);
            return;
        }

        hats = wardrobe.hats;
        cosmeticBindings = wardrobe.cosmeticBindings;
        wardrobe.enabled = false; // The PlayerCosmetics component owns this loadout.
        motor.playerAnimator = newAnimator;
        oldVisual.gameObject.SetActive(false);
    }
}
