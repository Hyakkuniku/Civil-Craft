// BridgeMaterialSO.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Bridge/Bridge Material", fileName = "BridgeMaterial")]
public class BridgeMaterialSO : ScriptableObject
{
    public string displayName = "";

    [Header("Visual")]
    public GameObject segmentPrefab;       // prefab with mesh + collider (can be shared)
    public Material material;              // optional – override material on instance
    public Color gizmoColor = Color.white;

    [Header("Physics")]
    public float massPerMeter      = 2f;
    public float breakForce        = 800f;     // ConfigurableJoint break force
    public float breakTorque       = 600f;
    public bool  useSpring         = false;
    public float spring            = 1000f;
    public float damper            = 50f;

    [Header("Cost / Gameplay")]
    public int   woodCost          = 5;
    public int   metalCost         = 0;
    public float maxLength         = 6f;       // max allowed segment length

    // You can later add: icon, sound, particle on break, etc.
}