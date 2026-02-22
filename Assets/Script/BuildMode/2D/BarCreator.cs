using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BarCreator : MonoBehaviour, IPointerDownHandler
{
    bool barCreationStarted = false;
    public float canvasPlaneDistance = 10f; 
    public float detectionRadius = 0.5f; 

    public Bar currentBar;
    public GameObject barToInstantiate;
    public Transform barParent;

    [Header("3D Material Data")]
    public BridgeMaterialSO activeMaterial;

    public Point currentStartPoint;
    public Point currentEndPoint;
    public GameObject pointToInstantiate;
    public Transform pointParent;

    [Header("Grid Settings")]
    public bool isGridSnappingEnabled = true;
    public Image gridVisual; 

    [Header("Visual Aids")]
    public LineRenderer radiusIndicator; // The visual circle
    public int circleResolution = 50;    // How smooth the circle is
    public float circleLineWidth = 0.05f;

    public void SetActiveMaterial(BridgeMaterialSO newMaterial)
    {
        if (newMaterial != null)
        {
            activeMaterial = newMaterial;
            Debug.Log($"Switched material to: {activeMaterial.displayName}");
            
            // If we swap materials mid-build, instantly update the circle size!
            if (barCreationStarted) DrawRadiusCircle(); 
        }
    }

    public void ToggleGrid()
    {
        isGridSnappingEnabled = !isGridSnappingEnabled;
        if (gridVisual != null) gridVisual.canvasRenderer.SetAlpha(isGridSnappingEnabled ? 1f : 0f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Vector2 worldPos = GetWorldMousePosition(eventData.position);

        if (!barCreationStarted)
        {
            Point startingNode = CheckForExistingPoint(worldPos);
            
            if (startingNode != null) 
            {
                currentStartPoint = startingNode;
                barCreationStarted = true;
                StartBarCreation(startingNode.transform.position);
            }
            else 
            {
                Debug.Log("Cannot start here! You must click an existing node.");
            }
        }
        else
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                FinishBarCreation(worldPos);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                CancelCreation();
            }
        }
    }

    private Point CheckForExistingPoint(Vector2 position)
    {
        Point[] allPoints = FindObjectsByType<Point>(FindObjectsSortMode.None);
        foreach (Point p in allPoints)
        {
            if (p == currentEndPoint) continue;

            if (Vector2.Distance(p.transform.position, position) < detectionRadius)
            {
                return p;
            }
        }
        return null;
    }

    Vector3 GetWorldMousePosition(Vector2 screenPos)
    {
        Vector3 mousePosWithDepth = new Vector3(screenPos.x, screenPos.y, canvasPlaneDistance);
        return Camera.main.ScreenToWorldPoint(mousePosWithDepth);
    }

    void StartBarCreation(Vector2 startPosition)
    {
        if (activeMaterial == null)
        {
            Debug.LogWarning("No active material selected! Please assign one in the inspector.");
            return;
        }

        GameObject newBar = Instantiate(barToInstantiate, barParent);
        newBar.name = "Bar";
        currentBar = newBar.GetComponent<Bar>();
        
        currentBar.Initialize(activeMaterial);
        currentBar.StartPosition = startPosition;

        GameObject endObj = Instantiate(pointToInstantiate, startPosition, Quaternion.identity, pointParent);
        endObj.name = "GhostPoint";
        currentEndPoint = endObj.GetComponent<Point>();

        // NEW: Draw the circle the moment we start building
        DrawRadiusCircle();
    }

    void FinishBarCreation(Vector2 rawWorldPos)
    {
        Point existingEndPoint = CheckForExistingPoint(rawWorldPos);
        Vector2 finalPosition;

        if (existingEndPoint != null)
        {
            finalPosition = existingEndPoint.transform.position; 
        }
        else
        {
            finalPosition = isGridSnappingEnabled ? (Vector2)Vector2Int.RoundToInt(rawWorldPos) : rawWorldPos;
        }

        float limit = activeMaterial != null ? activeMaterial.maxLength : 5f;
        if (Vector2.Distance(currentStartPoint.transform.position, finalPosition) > limit) return;

        if (Vector2.Distance(currentStartPoint.transform.position, finalPosition) < 0.1f) return;

        if (existingEndPoint != null)
        {
            Destroy(currentEndPoint.gameObject);
            currentEndPoint = existingEndPoint;
        }
        else
        {
            currentEndPoint.name = "Point";
            currentEndPoint.transform.position = finalPosition;
        }

        currentBar.UpdateCreatingBar(finalPosition);

        currentStartPoint.ConnectedBars.Add(currentBar);
        currentEndPoint.ConnectedBars.Add(currentBar);
        
        currentStartPoint = currentEndPoint;
        currentEndPoint = null; 
        
        StartBarCreation(currentStartPoint.transform.position);
    }

    void CancelCreation()
    {
        barCreationStarted = false;
        if(currentBar != null) Destroy(currentBar.gameObject);
        if(currentEndPoint != null) Destroy(currentEndPoint.gameObject);
        
        currentStartPoint = null;
        currentEndPoint = null;

        // NEW: Hide the circle when we cancel building
        if (radiusIndicator != null) radiusIndicator.enabled = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G)) ToggleGrid();

        if (barCreationStarted && currentEndPoint != null)
        {
            Vector2 worldMousePos = GetWorldMousePosition(Input.mousePosition);
            
            Point hoveredNode = CheckForExistingPoint(worldMousePos);
            Vector2 targetPos;

            if (hoveredNode != null)
            {
                targetPos = hoveredNode.transform.position; 
            }
            else
            {
                targetPos = isGridSnappingEnabled ? (Vector2)Vector2Int.RoundToInt(worldMousePos) : worldMousePos;
            }

            currentEndPoint.transform.position = targetPos;
            currentBar.UpdateCreatingBar(targetPos);
        }
    }

    private void OnDrawGizmos()
    {
        if (barCreationStarted && currentStartPoint != null && activeMaterial != null)
        {
            Gizmos.color = activeMaterial.gizmoColor;
            Gizmos.DrawWireSphere(currentStartPoint.transform.position, activeMaterial.maxLength);
        }
    }

    // NEW: Method to handle generating the LineRenderer points
    private void DrawRadiusCircle()
    {
        if (radiusIndicator == null || currentStartPoint == null || activeMaterial == null) return;

        radiusIndicator.enabled = true;
        radiusIndicator.useWorldSpace = true;
        radiusIndicator.positionCount = circleResolution + 1;

        // Apply visual settings
        radiusIndicator.startColor = activeMaterial.gizmoColor;
        radiusIndicator.endColor = activeMaterial.gizmoColor;
        radiusIndicator.startWidth = circleLineWidth;
        radiusIndicator.endWidth = circleLineWidth;

        Vector3 center = currentStartPoint.transform.position;
        float radius = activeMaterial.maxLength;
        float angleStep = 360f / circleResolution;

        for (int i = 0; i <= circleResolution; i++)
        {
            float currentAngle = i * angleStep * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(
                center.x + Mathf.Cos(currentAngle) * radius,
                center.y + Mathf.Sin(currentAngle) * radius,
                center.z 
            );
            radiusIndicator.SetPosition(i, pos);
        }
    }
}