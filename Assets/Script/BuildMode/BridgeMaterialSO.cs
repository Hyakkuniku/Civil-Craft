using UnityEngine;

[CreateAssetMenu(fileName = "NewBridgeMaterial", menuName = "Bridge/Material")]
public class BridgeMaterialSO : ScriptableObject
{
    [Header("Player-Facing Details")]
    [Tooltip("Permanent save ID. Existing materials safely fall back to the asset name when this is blank.")]
    [SerializeField] private string materialId;

    [Tooltip("Friendly name shown in material introduction panels. The asset name is used when left blank.")]
    public string displayName;

    [Header("Popup")]
    [TextArea(3, 8)]
    [Tooltip("Short explanation of what this material is and when the player should use it.")]
    public string introductionDescription;

    [Tooltip("Optional introduction photo. Leave empty to use Material Icon.")]
    public Sprite introductionImage;

    [Header("Almanac")]
    [TextArea(8, 30)]
    [Tooltip("Full explanation, uses and limitations. Leave blank to reuse Introduction Description.")]
    public string almanacDescription;
    [Tooltip("Optional reference photo/diagram. Leave empty to reuse the popup image.")]
    public Sprite almanacImage;

    public Sprite PopupImage => introductionImage != null ? introductionImage : materialIcon;
    public Sprite AlmanacImage => almanacImage != null ? almanacImage : PopupImage;
    public string PopupDescription => !string.IsNullOrWhiteSpace(introductionDescription)
        ? introductionDescription : "Discover this material and review its properties in the Almanac.";
    public string AlmanacDescription => !string.IsNullOrWhiteSpace(almanacDescription)
        ? almanacDescription : PopupDescription;

    [Header("Base Properties")]
    [Tooltip("Material-only price in Philippine pesos (PHP) per meter. Dual materials are charged twice.")]
    public float costPerMeter = 100f;
    [Min(0f)]
    [Tooltip("Mass in kilograms per meter for one material member. Dual materials are multiplied by two when placed.")]
    public float massPerMeter = 2f;
    public float maxLength = 6f;

    [Header("Axial Force Limits (Newtons)")]
    [Min(0f)]
    [Tooltip("Approximate axial tensile failure load for one simulated member.")]
    public float maxTension = 3000f;
    [Min(0f)]
    [Tooltip("Approximate axial compressive failure load for one simulated member. Use zero for tension-only materials.")]
    public float maxCompression = 3000f;

    [Header("Spring Settings")]
    public bool useSpring = false;
    public float spring = 5000f;
    public float damper = 50f;

    [Header("Special Types")]
    public bool isRope = false; 
    public bool isRoad = false; 
    public bool isPier = false; 

    [Header("Material Audio")]
    [Tooltip("AudioManager SFX ID played when the player equips this material.")]
    public string equipSfxId;
    [Tooltip("AudioManager SFX ID played when the player places this material.")]
    public string placeSfxId;
    [Tooltip("AudioManager SFX ID played when a member made from this material breaks.")]
    public string breakSfxId;

    [Header("Road Bending (Newton-metres)")]
    [Min(0f)]
    [Tooltip("Bending moment that breaks one road panel between its joints. Zero disables the bending check for this material.")]
    public float roadBendingMomentLimit;

    [Header("Pier Buckling")]
    [Min(0.01f)]
    [Tooltip("Pier height that retains the full compression limit. Taller piers lose compression strength according to the buckling exponent.")]
    public float pierBucklingReferenceLength = 10f;
    [Min(0f)]
    [Tooltip("Controls how quickly a tall pier loses compression strength. 2 approximates Euler buckling; 0 disables height scaling.")]
    public float pierBucklingExponent = 2f;
    [Range(0.01f, 1f)]
    [Tooltip("Lowest fraction of max compression retained by very tall piers.")]
    public float pierMinimumCompressionMultiplier = 0.15f;

    // --- NEW: Unlock System ---
    [Header("Unlock System")]
    [Tooltip("Hide this material unless the current contract explicitly lists this exact asset in Allowed Materials. Empty/unrestricted lists and purchased unlocks do not enable it. Off preserves existing material behavior.")]
    public bool requiresExplicitContractAllowance = false;

    [Tooltip("How many Coins it costs to unlock this material if a contract restricts it.")]
    public int unlockCost = 500; 

    [Header("Visuals")]
    [Tooltip("The 2D sprite icon shown in the UI and Receipt.")]
    public Sprite materialIcon; 
    
    [Tooltip("For Piers: This is the bottom Pillar that stretches up.")]
    public GameObject segmentPrefab;
    [Tooltip("For Piers: This is the T-Shaped Cap that sits at the top.")]
    public GameObject pierCapPrefab; 
    [Min(1f)]
    [Tooltip("Visual thickness of the pier head. The top stays fitted to the connected bridge.")]
    public float pierCapVisualThickness = 1f;

    public float PierCapThicknessScale => Mathf.Clamp(pierCapVisualThickness, 1f, 2f);
    
    public Color gizmoColor = Color.white;
    [Tooltip("Optional rope color during simulation. A transparent value uses the standard rope color.")]
    public Color ropeSimulationTint;
    public bool isDualBeam = false;
    public float zOffset = 0.5f;

    public string Id => string.IsNullOrWhiteSpace(materialId) ? name.Trim() : materialId.Trim();

    /// <summary>
    /// Hidden materials are a hard contract restriction, including drawing and
    /// blueprint paste. Other allowance, purchase and quantity checks stay with
    /// their existing owners. Additional opt-in is required only when configured.
    /// Reconstruction and read-only network visuals do not use this build gate.
    /// </summary>
    public bool CanUseInContract(ContractSO contract)
    {
        if (contract != null && contract.hiddenMaterials != null && contract.hiddenMaterials.Contains(this)) return false;
        if (!requiresExplicitContractAllowance) return true;
        if (contract == null) return false;
        if (contract.allowedMaterials == null) return false;
        foreach (MaterialAllowance allowance in contract.allowedMaterials)
            if (allowance != null && allowance.material == this) return true;
        return false;
    }

    public float GetPlacedMassPerMeter()
    {
        return massPerMeter * (isDualBeam ? 2f : 1f);
    }

    public float GetCompressionLimit(float memberLength)
    {
        if (!isPier || pierBucklingExponent <= 0f)
            return maxCompression;

        float safeReferenceLength = Mathf.Max(0.01f, pierBucklingReferenceLength);
        float heightRatio = Mathf.Max(1f, memberLength / safeReferenceLength);
        float multiplier = 1f / Mathf.Pow(heightRatio, pierBucklingExponent);
        multiplier = Mathf.Clamp(multiplier, pierMinimumCompressionMultiplier, 1f);
        return maxCompression * multiplier;
    }

    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        return name.Replace("Material", string.Empty).Trim();
    }
}
