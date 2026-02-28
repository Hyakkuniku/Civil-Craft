using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 1. ADDED IPointerUpHandler to detect when the finger is lifted
public class BarCreator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler 
{
    [Header("References")]
    public Bar currentBar;
    public GameObject barToInstantiate;
    public Transform barParent;
    public Point currentStartPoint;
    public Point currentEndPoint;
    public GameObject pointToInstantiate;
    public Transform pointParent;

    [Header("3D Material Data")]
    public BridgeMaterialSO activeMaterial;

    [Header("Grid Settings")]
    public bool isGridSnappingEnabled = true;
    public Image gridVisual; 

    [Header("Visual Aids")]
    public LineRenderer radiusIndicator; 
    public int circleResolution = 50;    
    public float circleLineWidth = 0.05f;

    private bool barCreationStarted = false;

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);

            bool isBuilding = GameManager.Instance.CurrentState == GameManager.GameState.Building;
            if (pointParent != null) pointParent.gameObject.SetActive(isBuilding);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void HandleEnterBuildMode()
    {
        if (pointParent != null) pointParent.gameObject.SetActive(true);
    }

    private void HandleExitBuildMode()
    {
        CancelCreation(); 
        if (pointParent != null) pointParent.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)
            return;

        if (Input.GetKeyDown(KeyCode.G)) ToggleGrid();

        if (barCreationStarted && currentEndPoint != null)
        {
            Vector2 screenPos = Input.mousePosition;
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldMousePos = GetWorldMousePosition(screenPos, hoveredNode);
            
            Vector3 targetPos = CalculateTargetPosition(worldMousePos, hoveredNode);

            float maxLen = activeMaterial != null ? activeMaterial.maxLength : 5f;
            Vector3 startPos = currentStartPoint.transform.position;
            
            if (Vector3.Distance(startPos, targetPos) > maxLen)
            {
                Vector3 direction = (targetPos - startPos).normalized;
                targetPos = startPos + (direction * maxLen);

                if (isGridSnappingEnabled && hoveredNode == null)
                {
                    targetPos = new Vector3(Mathf.RoundToInt(targetPos.x), Mathf.RoundToInt(targetPos.y), Mathf.RoundToInt(targetPos.z));
                    if (Vector3.Distance(startPos, targetPos) > maxLen)
                    {
                        targetPos = startPos + (direction * maxLen); 
                    }
                }
            }

            currentEndPoint.transform.position = targetPos;
            currentBar.UpdateCreatingBar(targetPos);
        }
    }

    // --- CHANGED: Now only handles the initial touch/click ---
    public void OnPointerDown(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)
            return;

        // Right click to cancel (PC only)
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            CancelCreation();
            return;
        }

        Vector2 screenPos = eventData.position;
        Point hoveredNode = CheckForExistingPoint(screenPos);

        // Start drawing only if we click a valid node with the left mouse button (or tap)
        if (!barCreationStarted && eventData.button == PointerEventData.InputButton.Left)
        {
            if (hoveredNode != null) 
            {
                currentStartPoint = hoveredNode;
                barCreationStarted = true;
                StartBarCreation(hoveredNode.transform.position);
            }
        }
    }

    // --- NEW: Detects when you lift your finger off the screen ---
    public void OnPointerUp(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)
            return;

        if (barCreationStarted && eventData.button == PointerEventData.InputButton.Left)
        {
            Vector2 screenPos = eventData.position;
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldPos = GetWorldMousePosition(screenPos, hoveredNode);

            FinishBarCreation(worldPos, hoveredNode);
        }
    }

    public void SetActiveMaterial(BridgeMaterialSO newMaterial)
    {
        if (newMaterial != null)
        {
            activeMaterial = newMaterial;
            if (barCreationStarted) DrawRadiusCircle(); 
        }
    }

    public void ToggleGrid()
    {
        isGridSnappingEnabled = !isGridSnappingEnabled;
        if (gridVisual != null) gridVisual.canvasRenderer.SetAlpha(isGridSnappingEnabled ? 1f : 0f);
    }

    private Vector3 CalculateTargetPosition(Vector3 rawPos, Point hoveredNode)
    {
        if (hoveredNode != null) return hoveredNode.transform.position;
        
        if (isGridSnappingEnabled)
        {
            return new Vector3(Mathf.RoundToInt(rawPos.x), Mathf.RoundToInt(rawPos.y), Mathf.RoundToInt(rawPos.z));
        }

        return rawPos;
    }

    private Point CheckForExistingPoint(Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        Point closest = null;
        float minDist = 0.5f; 

        foreach (Point p in Point.AllPoints)
        {
            if (p == currentEndPoint) continue;
            
            float dist = Vector3.Cross(ray.direction, p.transform.position - ray.origin).magnitude;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }
        return closest;
    }

    private Vector3 GetWorldMousePosition(Vector2 screenPos, Point snappedPoint)
    {
        if (snappedPoint != null) return snappedPoint.transform.position;
        
        float zDepth = currentStartPoint != null ? currentStartPoint.transform.position.z : 0f;
        Plane plane = new Plane(Vector3.forward, new Vector3(0, 0, zDepth));
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        
        if (plane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }
        return Vector3.zero;
    }

    private void StartBarCreation(Vector3 startPosition)
    {
        if (activeMaterial == null) return;

        GameObject newBar = Instantiate(barToInstantiate, barParent);
        newBar.name = "Bar";
        currentBar = newBar.GetComponent<Bar>();
        
        currentBar.Initialize(activeMaterial);
        currentBar.StartPosition = startPosition;

        GameObject endObj = Instantiate(pointToInstantiate, startPosition, Quaternion.identity, pointParent);
        endObj.name = "GhostPoint";
        currentEndPoint = endObj.GetComponent<Point>();

        DrawRadiusCircle();
    }

    private void FinishBarCreation(Vector3 rawWorldPos, Point existingEndPoint)
    {
        Vector3 finalPosition = CalculateTargetPosition(rawWorldPos, existingEndPoint);
        float limit = activeMaterial != null ? activeMaterial.maxLength : 5f;
        Vector3 startPos = currentStartPoint.transform.position;

        if (Vector3.Distance(startPos, finalPosition) > limit)
        {
            Vector3 direction = (finalPosition - startPos).normalized;
            finalPosition = startPos + (direction * limit);
            
            if (isGridSnappingEnabled && existingEndPoint == null)
            {
                finalPosition = new Vector3(Mathf.RoundToInt(finalPosition.x), Mathf.RoundToInt(finalPosition.y), Mathf.RoundToInt(finalPosition.z));
                if (Vector3.Distance(startPos, finalPosition) > limit) finalPosition = startPos + (direction * limit);
            }

            existingEndPoint = null; 
        }

        // Check if the user barely moved their finger (tapped instead of dragged)
        // If the bar is too short, we cancel it to prevent glitchy invisible beams
        if (Vector3.Distance(startPos, finalPosition) < 0.1f) 
        {
            CancelCreation();
            return;
        }

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
        
        // --- CHANGED: End the building phase entirely here ---
        barCreationStarted = false;
        currentStartPoint = null;
        currentEndPoint = null;
        currentBar = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;
    }

    private void CancelCreation()
    {
        barCreationStarted = false;
        if (currentBar != null) Destroy(currentBar.gameObject);
        if (currentEndPoint != null) Destroy(currentEndPoint.gameObject);
        
        currentStartPoint = null;
        currentEndPoint = null;

        if (radiusIndicator != null) radiusIndicator.enabled = false;
    }

    private void DrawRadiusCircle()
    {
        if (radiusIndicator == null || currentStartPoint == null || activeMaterial == null) return;

        radiusIndicator.enabled = true;
        radiusIndicator.useWorldSpace = true;
        radiusIndicator.positionCount = circleResolution + 1;

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

    private void OnDrawGizmos()
    {
        if (barCreationStarted && currentStartPoint != null && activeMaterial != null)
        {
            Gizmos.color = activeMaterial.gizmoColor;
            Gizmos.DrawWireSphere(currentStartPoint.transform.position, activeMaterial.maxLength);
        }
    }
}