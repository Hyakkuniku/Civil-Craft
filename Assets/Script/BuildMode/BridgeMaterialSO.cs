using UnityEngine;

[CreateAssetMenu(menuName = "Bridge/Bridge Material", fileName = "BridgeMaterial")]
public class BridgeMaterialSO : ScriptableObject
{
    public string displayName = "Wood";

    [Header("Visual")]
    public GameObject segmentPrefab;        
    public Material overrideMaterial;       
    public Color gizmoColor = Color.white;

    [Header("Beam Mirroring (3D)")]
    [Tooltip("Check this for wood/steel supports. Uncheck for the central road.")]
    public bool isDualBeam = false; 
    [Tooltip("How far outward from the center the beams should spawn.")]
    public float zOffset = 1.5f;

    [Header("Side Beams (Optional)")]
    public GameObject sideBeamPrefab;       
    public Vector3 sideBeamOffset = new Vector3(0, 0, 1f); 

    [Header("Physics & Structural Integrity")]
    public float massPerMeter      = 2f;
    [Tooltip("Max pulling force before snapping.")]
    public float maxTension        = 1200f; 
    [Tooltip("Max pushing force before buckling.")]
    public float maxCompression    = 800f;  
    [Tooltip("Native Unity joint limit for shear/twisting forces.")]
    public float breakForce        = 800f;
    public float breakTorque       = 600f;

    [Header("Spring Settings")]
    public bool  useSpring         = false;
    public float spring            = 1000f;
    public float damper            = 50f;

    [Header("Cost / Gameplay")]
    public int   woodCost          = 5;
    public int   metalCost         = 0;
    public float maxLength         = 6f;
}