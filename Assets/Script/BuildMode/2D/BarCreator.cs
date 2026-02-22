using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BarCreator : MonoBehaviour, IPointerDownHandler
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
            // Listen to GameManager events
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);

            // Set the initial visibility based on the starting state
            bool isBuilding = GameManager.Instance.CurrentState == GameManager.GameState.Building;
            if (pointParent != null) pointParent.gameObject.SetActive(isBuilding);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            // Always clean up listeners to prevent memory leaks!
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
        CancelCreation(); // Stop dragging if the player exits mid-build
        if (pointParent != null) pointParent.gameObject.SetActive(false);
    }

    private void Update()
    {
        // GUARD: Stop processing if we are NOT in build mode
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)
            return;

        if (Input.GetKeyDown(KeyCode.G)) ToggleGrid();

        if (barCreationStarted && currentEndPoint != null)
        {
            Vector2 screenPos = Input.mousePosition;
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldMousePos = GetWorldMousePosition(screenPos, hoveredNode);
            Vector3 targetPos = CalculateTargetPosition(worldMousePos, hoveredNode);

            // Z is dynamically preserved based on snap logic
            currentEndPoint.transform.position = targetPos;
            currentBar.UpdateCreatingBar(targetPos);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // GUARD: Ignore all clicks if we are NOT in build mode
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)
            return;

        Vector2 screenPos = eventData.position;
        Point hoveredNode = CheckForExistingPoint(screenPos);
        Vector3 worldPos = GetWorldMousePosition(screenPos, hoveredNode);

        if (!barCreationStarted)
        {
            if (hoveredNode != null) 
            {
                currentStartPoint = hoveredNode;
                barCreationStarted = true;
                StartBarCreation(hoveredNode.transform.position);
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
                FinishBarCreation(worldPos, hoveredNode);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                CancelCreation();
            }
        }
    }

    public void SetActiveMaterial(BridgeMaterialSO newMaterial)
    {
        if (newMaterial != null)
        {
            activeMaterial = newMaterial;
            Debug.Log($"Switched material to: {activeMaterial.displayName}");
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

        if (Vector3.Distance(currentStartPoint.transform.position, finalPosition) > limit) return;
        if (Vector3.Distance(currentStartPoint.transform.position, finalPosition) < 0.1f) return;

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