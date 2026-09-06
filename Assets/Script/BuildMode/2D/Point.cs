using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Point : MonoBehaviour
{
    public bool Runtime = true; 
    public bool isAnchor = false;
    
    [HideInInspector] public bool isSelected = false; 
    
    [HideInInspector] public bool originalIsAnchor = false;
    private bool hasInitializedAnchor = false;

    public Material defaultMaterial;
    public Material anchorMaterial;
    public Material selectedMaterial; 

    public List<Bar> ConnectedBars = new List<Bar>();
    public static readonly List<Point> AllPoints = new List<Point>();

    [SerializeField, HideInInspector] private BuildLocation ownerLocation;
    public BuildLocation OwnerLocation => ownerLocation;

    private Renderer pointRenderer;

    [HideInInspector] public Vector3 preSimPos;
    [HideInInspector] public Quaternion preSimRot;
    [HideInInspector] public Transform preSimParent;

    private void Awake()
    {
        pointRenderer = GetComponentInChildren<Renderer>();

        if (Application.isPlaying && ownerLocation == null && GameManager.Instance != null)
            ownerLocation = GameManager.Instance.ActiveBuildLocation;

        if (Application.isPlaying && !hasInitializedAnchor)
        {
            originalIsAnchor = isAnchor;
            hasInitializedAnchor = true;
        }
    }

    public void AssignOwner(BuildLocation location, bool overwriteExisting = false)
    {
        if (location != null && (ownerLocation == null || overwriteExisting))
            ownerLocation = location;
    }

    private void OnEnable()
    {
        if (!AllPoints.Contains(this)) AllPoints.Add(this);
        
        if (pointRenderer == null) pointRenderer = GetComponentInChildren<Renderer>();
        
        if (Application.isPlaying && pointRenderer != null)
        {
            BridgePhysicsManager bpm = FindObjectOfType<BridgePhysicsManager>();
            if (bpm == null || !bpm.isSimulating)
            {
                pointRenderer.enabled = true;
            }
        }
        
        UpdateMaterial();
    }

    private void OnDisable()
    {
        AllPoints.Remove(this);
        
        if (pointRenderer != null)
        {
            pointRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        AllPoints.Remove(this);
    }

    private void Update()
    {
        AnchorEdgeSnap edgeSnap = GetComponent<AnchorEdgeSnap>();
        if (transform.hasChanged && edgeSnap != null)
        {
            if (edgeSnap.ControlsEditorPlacement)
            {
                edgeSnap.HandleEditorTransformChanged();
                return;
            }

            // A Point 1 prefab may contain the optional edge authoring helper.
            // Existing tutorial anchors must not fall through to legacy integer
            // snapping, because their baked ghost coordinates are exact.
            if (edgeSnap.PreservesExistingAnchorPlacement)
            {
                transform.hasChanged = false;
                return;
            }
        }

        if (!Runtime && transform.hasChanged)
        {
            transform.hasChanged = false;
            Vector3Int snapped = Vector3Int.RoundToInt(transform.position);
            transform.position = new Vector3(snapped.x, snapped.y, snapped.z);
        }
    }

    private void OnValidate()
    {
        UpdateMaterial();
    }

    public void UpdateMaterial()
    {
        if (pointRenderer == null) pointRenderer = GetComponentInChildren<Renderer>();
        
        if (pointRenderer != null)
        {
            if (isSelected && selectedMaterial != null)
                pointRenderer.sharedMaterial = selectedMaterial;
            else if (isAnchor && anchorMaterial != null)
                pointRenderer.sharedMaterial = anchorMaterial;
            else if (!isAnchor && defaultMaterial != null)
                pointRenderer.sharedMaterial = defaultMaterial;
        }
    }

    public void EvaluateAnchorState()
    {
        if (!Application.isPlaying) return;
        
        if (!hasInitializedAnchor)
        {
            originalIsAnchor = isAnchor;
            hasInitializedAnchor = true;
        }

        // Only scene/foundation anchors are immovable. A pier transfers load from
        // its dynamic top node to its anchored foot; merely touching a pier must
        // never turn a bridge node into an indestructible anchor.
        isAnchor = originalIsAnchor;
        UpdateMaterial();
    }

    private void OnDrawGizmos()
    {
        if (isAnchor)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawCube(transform.position, Vector3.one * 1f);
        }
        else if (!Runtime)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, 0.2f);
        }
    }
}
