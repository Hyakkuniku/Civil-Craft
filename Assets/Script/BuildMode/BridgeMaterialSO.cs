using UnityEngine;

[CreateAssetMenu(fileName = "NewBridgeMaterial", menuName = "Bridge/Material")]
public class BridgeMaterialSO : ScriptableObject
{
    [Header("Base Properties")]
    public float costPerMeter = 100f;
    public float massPerMeter = 2f;
    public float maxLength = 6f;

    [Header("Stress Limits (Newtons)")]
    public float maxTension = 3000f;
    public float maxCompression = 3000f;

    [Header("Spring Settings")]
    public bool useSpring = false;
    public float spring = 5000f;
    public float damper = 50f;

    [Header("Special Types")]
    public bool isRope = false; 
    public bool isRoad = false; 
    public bool isPier = false; // --- NEW: Check this box in the Inspector for your Pier material! ---

    [Header("Visuals")]
    public GameObject segmentPrefab;
    public Color gizmoColor = Color.white;
    public bool isDualBeam = false;
    public float zOffset = 0.5f;
}