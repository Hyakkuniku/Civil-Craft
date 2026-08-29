using System.Collections.Generic;
using UnityEngine;

public class Bar : MonoBehaviour
{
    public Vector3 StartPosition; 
    public Vector3 EndPosition; // --- NEW: Stores the end coordinate to repair broken prefabs! ---

    public BridgeMaterialSO materialData;

    public Point startPoint;
    public Point endPoint;

    [SerializeField, HideInInspector] private BuildLocation ownerLocation;
    public BuildLocation OwnerLocation => ownerLocation;

    [HideInInspector] public Vector3 preSimPos;
    [HideInInspector] public Quaternion preSimRot;
    [HideInInspector] public Vector3 visualSize = new Vector3(1f, 0.2f, 0.2f);
    [HideInInspector] public float currentLength = 0f;
    [HideInInspector] public float currentAngle = 0f; 
    
    [HideInInspector] public bool isHighlighted = false; 

    private List<GameObject> visualSegments = new List<GameObject>();
    private float baseLength = 1f; 
    private Vector3 originalScale = Vector3.one;
    
    private GameObject pierCapInstance;
    private Vector3 originalCapScale = Vector3.one;
    
    private float capTopOffset = 0f;
    private float capBottomOffset = 0f;

    private Dictionary<Renderer, Material> originalMats = new Dictionary<Renderer, Material>();

    private void Awake()
    {
        if (Application.isPlaying && ownerLocation == null && GameManager.Instance != null)
            ownerLocation = GameManager.Instance.ActiveBuildLocation;
    }

    public void AssignOwner(BuildLocation location, bool overwriteExisting = false)
    {
        if (location != null && (ownerLocation == null || overwriteExisting))
            ownerLocation = location;
    }

    public void InferOwnerFromEndpoints()
    {
        if (ownerLocation != null) return;
        if (startPoint != null && startPoint.OwnerLocation != null)
            ownerLocation = startPoint.OwnerLocation;
        else if (endPoint != null && endPoint.OwnerLocation != null)
            ownerLocation = endPoint.OwnerLocation;
    }

    // --- NEW: Re-knits the graph if this Bar was loaded from a Prefab and lost its scene references ---
    public void AutoRepairEndpoints()
    {
        if (startPoint == null || endPoint == null)
        {
            Point[] allPointsInScene = FindObjectsOfType<Point>();
            foreach (Point p in allPointsInScene)
            {
                if (startPoint == null && Vector3.Distance(StartPosition, p.transform.position) < 0.1f)
                {
                    startPoint = p;
                    if (!p.ConnectedBars.Contains(this)) p.ConnectedBars.Add(this);
                }
                
                if (endPoint == null && Vector3.Distance(EndPosition, p.transform.position) < 0.1f)
                {
                    endPoint = p;
                    if (!p.ConnectedBars.Contains(this)) p.ConnectedBars.Add(this);
                }
            }
        }
    }

    private void OnEnable()
    {
        if (startPoint != null && !startPoint.ConnectedBars.Contains(this)) startPoint.ConnectedBars.Add(this);
        if (endPoint != null && !endPoint.ConnectedBars.Contains(this)) endPoint.ConnectedBars.Add(this);
    }

    private void OnDisable()
    {
        if (!gameObject.activeInHierarchy)
        {
            RemoveConnections();
        }
    }

    private void OnDestroy()
    {
        RemoveConnections();
    }

    private void RemoveConnections()
    {
        if (startPoint != null) startPoint.ConnectedBars.Remove(this);
        if (endPoint != null) endPoint.ConnectedBars.Remove(this);
    }

    public void Initialize(BridgeMaterialSO data)
    {
        materialData = data;
        visualSegments.Clear();
        
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(false); 
            Destroy(child.gameObject);
        }
        
        if (materialData.segmentPrefab != null)
        {
            int spawnCount = materialData.isDualBeam ? 2 : 1;

            for (int i = 0; i < spawnCount; i++)
            {
                GameObject newSegment = Instantiate(materialData.segmentPrefab, transform);
                newSegment.name = materialData.isDualBeam ? $"VisualSegment_{i}" : "VisualSegment";
                
                float offsetValue = 0f;
                if (materialData.isDualBeam)
                {
                    offsetValue = (i == 0) ? materialData.zOffset : -materialData.zOffset;
                }
                
                newSegment.transform.localPosition = new Vector3(0, 0, offsetValue);

                var renderer = newSegment.GetComponentInChildren<Renderer>();
                if (renderer != null && i == 0)
                {
                    originalScale = newSegment.transform.localScale;
                    
                    baseLength = materialData.isPier ? renderer.bounds.size.y : renderer.bounds.size.x;
                    visualSize = renderer.bounds.size; 
                    if (baseLength <= 0f) baseLength = 1f; 
                }
                
                newSegment.transform.localScale = Vector3.zero;
                visualSegments.Add(newSegment);
            }
        }

        if (materialData.isPier && materialData.pierCapPrefab != null)
        {
            pierCapInstance = Instantiate(materialData.pierCapPrefab, transform);
            pierCapInstance.name = "PierCap";
            originalCapScale = pierCapInstance.transform.localScale;

            pierCapInstance.transform.position = Vector3.zero;
            pierCapInstance.transform.rotation = Quaternion.identity;
            
            Renderer capRend = pierCapInstance.GetComponentInChildren<Renderer>();
            if (capRend != null)
            {
                capTopOffset = capRend.bounds.max.y;    
                capBottomOffset = capRend.bounds.min.y; 
            }

            pierCapInstance.transform.localScale = Vector3.zero; 
        }

        originalMats.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r != null) originalMats[r] = r.sharedMaterial;
        }
    }

    public void SetHighlight(bool highlight, Material highlightMat)
    {
        isHighlighted = highlight;
        foreach (var kvp in originalMats)
        {
            if (kvp.Key != null)
            {
                kvp.Key.sharedMaterial = (highlight && highlightMat != null) ? highlightMat : kvp.Value;
            }
        }
    }

    public void UpdateCreatingBar(Vector3 ToPosition) 
    {
        EndPosition = ToPosition;
        if (visualSegments.Count == 0 || materialData == null) return;

        Vector3 actualStart = StartPosition;
        Vector3 actualEnd = ToPosition;
        if (ShouldSwapEndpointOrder(actualStart, actualEnd, materialData.isPier))
        {
            actualStart = ToPosition;
            actualEnd = StartPosition;
        }

        // Bars are built on one bridge plane. Keep their length positive and let
        // the rotation carry the full start-to-end direction, including 180 degrees.
        Vector3 direction = actualEnd - actualStart;
        direction.z = 0f;
        float totalDistance = direction.magnitude;
        
        currentLength = totalDistance;
        
        if (totalDistance < 0.01f) 
        {
            foreach (var seg in visualSegments) seg.transform.localScale = Vector3.zero;
            if (pierCapInstance != null) pierCapInstance.transform.localScale = Vector3.zero;
            return;
        }

        if (materialData.isPier)
        {
            currentAngle = 90f; 

            float targetPillarTopY = actualEnd.y;
            if (pierCapInstance != null)
            {
                targetPillarTopY = actualEnd.y - capTopOffset + capBottomOffset; 
            }

            float adjustedDistance = targetPillarTopY - actualStart.y;
            if (adjustedDistance < 0.05f) adjustedDistance = 0.05f; 

            Vector3 midPointAdjusted = actualStart + (Vector3.up * (adjustedDistance / 2f));
            transform.SetPositionAndRotation(midPointAdjusted, Quaternion.identity);

            float scaleMultiplier = adjustedDistance / baseLength;
            Vector3 newScale = new Vector3(
                Mathf.Abs(originalScale.x),
                Mathf.Abs(originalScale.y) * scaleMultiplier,
                Mathf.Abs(originalScale.z));

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }

            if (pierCapInstance != null)
            {
                pierCapInstance.transform.localScale = new Vector3(
                    Mathf.Abs(originalCapScale.x),
                    Mathf.Abs(originalCapScale.y),
                    Mathf.Abs(originalCapScale.z));
                
                Vector3 capPos = actualEnd;
                capPos.y -= capTopOffset; 
                
                pierCapInstance.transform.position = capPos;
                pierCapInstance.transform.rotation = Quaternion.identity;
            }
        }
        else
        {
            Vector3 midPoint = (actualStart + actualEnd) * 0.5f;
            midPoint.z = actualStart.z;

            // actualStart/actualEnd are spatially canonical, so this angle never
            // flips merely because the player dragged in the opposite direction.
            float rotationAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            currentAngle = rotationAngle;

            transform.SetPositionAndRotation(midPoint, Quaternion.Euler(0, 0, rotationAngle));

            float scaleMultiplier = totalDistance / Mathf.Max(Mathf.Abs(baseLength), 0.0001f);
            Vector3 newScale = new Vector3(
                Mathf.Abs(originalScale.x) * scaleMultiplier,
                Mathf.Abs(originalScale.y),
                Mathf.Abs(originalScale.z));

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }
        }
    }

    /// <summary>
    /// Makes endpoint identity independent of drawing direction. Call this after
    /// assigning startPoint/endPoint and before creating physics joints.
    /// </summary>
    public void NormalizeEndpointOrder()
    {
        if (startPoint == null || endPoint == null || materialData == null)
            return;

        if (ShouldSwapEndpointOrder(
            startPoint.transform.position,
            endPoint.transform.position,
            materialData.isPier))
        {
            Point previousStart = startPoint;
            startPoint = endPoint;
            endPoint = previousStart;
        }

        StartPosition = startPoint.transform.position;
        EndPosition = endPoint.transform.position;
        UpdateCreatingBar(EndPosition);
    }

    private static bool ShouldSwapEndpointOrder(Vector3 first, Vector3 second, bool isPier)
    {
        const float epsilon = 0.0001f;

        if (isPier)
            return first.y > second.y + epsilon;

        if (first.x > second.x + epsilon) return true;
        if (first.x < second.x - epsilon) return false;
        if (first.y > second.y + epsilon) return true;
        if (first.y < second.y - epsilon) return false;
        return first.z > second.z;
    }

    public float GetCost()
    {
        if (materialData == null) return 0f;
        int multiplier = materialData.isDualBeam ? 2 : 1;
        return currentLength * materialData.costPerMeter * multiplier;
    }
}
