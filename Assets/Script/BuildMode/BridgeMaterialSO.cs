using UnityEngine;

[CreateAssetMenu(fileName = "NewBridgeMaterial", menuName = "Bridge/Material")]
public class BridgeMaterialSO : ScriptableObject
{
    [Header("Player-Facing Details")]
    [Tooltip("Permanent save ID. Existing materials safely fall back to the asset name when this is blank.")]
    [SerializeField] private string materialId;

    [Tooltip("Friendly name shown in material introduction panels. The asset name is used when left blank.")]
    public string displayName;

    [TextArea(3, 8)]
    [Tooltip("Short explanation of what this material is and when the player should use it.")]
    public string introductionDescription;

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

    // --- NEW: Unlock System ---
    [Header("Unlock System")]
    [Tooltip("How much Gold it costs to unlock this material if a contract restricts it.")]
    public int unlockCost = 500; 

    [Header("Visuals")]
    [Tooltip("The 2D sprite icon shown in the UI and Receipt.")]
    public Sprite materialIcon; 
    
    [Tooltip("For Piers: This is the bottom Pillar that stretches up.")]
    public GameObject segmentPrefab;
    [Tooltip("For Piers: This is the T-Shaped Cap that sits at the top.")]
    public GameObject pierCapPrefab; 
    
    public Color gizmoColor = Color.white;
    public bool isDualBeam = false;
    public float zOffset = 0.5f;

    public string Id => string.IsNullOrWhiteSpace(materialId) ? name.Trim() : materialId.Trim();

    public float GetPlacedMassPerMeter()
    {
        return massPerMeter * (isDualBeam ? 2f : 1f);
    }

    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        return name.Replace("Material", string.Empty).Trim();
    }
}
