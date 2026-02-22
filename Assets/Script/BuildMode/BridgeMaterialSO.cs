using UnityEngine;

[CreateAssetMenu(menuName = "Bridge/Bridge Material", fileName = "BridgeMaterial")]
public class BridgeMaterialSO : ScriptableObject
{
    public string displayName = "Wood";

    [Header("Visual")]
    public GameObject segmentPrefab;        
    public Material overrideMaterial;       
    public Color gizmoColor = Color.white;

    [Header("Side Beams (Optional)")]
    public GameObject sideBeamPrefab;       // The beam to spawn on the sides
    public Vector3 sideBeamOffset = new Vector3(0, 0, 1f); // Offset distance (Z=1 pushes it back 1 unit)

    [Header("Physics (future)")]
    public float massPerMeter      = 2f;
    public float breakForce        = 800f;
    public float breakTorque       = 600f;
    public bool  useSpring         = false;
    public float spring            = 1000f;
    public float damper            = 50f;

    [Header("Cost / Gameplay")]
    public int   woodCost          = 5;
    public int   metalCost         = 0;
    public float maxLength         = 6f;
}