using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

public class HistoryAction
{
    public bool isBuildEvent; 
    public List<GameObject> affectedObjects = new List<GameObject>();

    public void Undo()
    {
        for (int i = affectedObjects.Count - 1; i >= 0; i--)
            if (affectedObjects[i] != null) affectedObjects[i].SetActive(!isBuildEvent);
    }

    public void Redo()
    {
        for (int i = 0; i < affectedObjects.Count; i++)
            if (affectedObjects[i] != null) affectedObjects[i].SetActive(isBuildEvent);
    }
}

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

    [Header("Modes & Settings")]
    public bool isGridSnappingEnabled = true;
    public bool isDeleteMode = false;
    
    [HideInInspector] public bool isSimulating = false; 

    // ADDED: Let other scripts know if we are actively drawing a bar
    public bool IsCreating => barCreationStarted;

    [Header("Visual Aids")]
    public Image gridVisual; 
    public LineRenderer radiusIndicator; 
    public int circleResolution = 50;    
    public float circleLineWidth = 0.05f;

    private bool barCreationStarted = false;

    private Stack<HistoryAction> undoStack = new Stack<HistoryAction>();
    private Stack<HistoryAction> redoStack = new Stack<HistoryAction>();

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }

        if (pointParent != null) pointParent.gameObject.SetActive(true); 
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
        isSimulating = false; 
    }

    private void HandleExitBuildMode()
    {
        CancelCreation(); 
        isDeleteMode = false; 
        isSimulating = false; 
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)
            return;

        if (isSimulating) return;

        if (barCreationStarted && currentEndPoint != null && !isDeleteMode)
        {
            Vector2 screenPos = Input.mousePosition;
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldMousePos = GetWorldMousePosition(screenPos, hoveredNode);
            
            Vector3 targetPos = CalculateTargetPosition(worldMousePos, hoveredNode);

            float maxLen = activeMaterial != null ? activeMaterial.maxLength : 5f;
            
            // Dynamically restrict max length based on remaining budget!
            if (BuildUIController.Instance != null && activeMaterial != null)
            {
                float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
                float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
                float maxAffordableLength = Mathf.Max(0f, remainingBudget / costPerMeter);
                
                if (maxAffordableLength < maxLen) 
                    maxLen = maxAffordableLength;
            }

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

    public void OnPointerDown(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating) return; 

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            CancelCreation();
            return;
        }

        Vector2 screenPos = eventData.position;

        if (isDeleteMode && eventData.button == PointerEventData.InputButton.Left)
        {
            Point clickedPoint = CheckForExistingPoint(screenPos);
            if (clickedPoint != null)
            {
                HistoryAction deleteAction = new HistoryAction { isBuildEvent = false };
                DeletePoint(clickedPoint, deleteAction);
                if (deleteAction.affectedObjects.Count > 0) RecordAction(deleteAction);
                return;
            }

            Bar clickedBar = CheckForExistingBar(screenPos);
            if (clickedBar != null)
            {
                HistoryAction deleteAction = new HistoryAction { isBuildEvent = false };
                DeleteBar(clickedBar, deleteAction);
                if (deleteAction.affectedObjects.Count > 0) RecordAction(deleteAction);
            }
            return; 
        }

        Point hoveredNode = CheckForExistingPoint(screenPos);
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

    public void OnPointerUp(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating) return; 

        if (barCreationStarted && eventData.button == PointerEventData.InputButton.Left && !isDeleteMode)
        {
            Vector2 screenPos = eventData.position;
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldPos = GetWorldMousePosition(screenPos, hoveredNode);

            FinishBarCreation(worldPos, hoveredNode);
        }
    }

    public void Undo()
    {
        if (isSimulating || undoStack.Count == 0 || (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)) return;
        CancelCreation(); 
        HistoryAction action = undoStack.Pop();
        action.Undo();
        redoStack.Push(action);
    }

    public void Redo()
    {
        if (isSimulating || redoStack.Count == 0 || (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)) return;
        CancelCreation(); 
        HistoryAction action = redoStack.Pop();
        action.Redo();
        undoStack.Push(action);
    }

    private void RecordAction(HistoryAction action)
    {
        undoStack.Push(action);
        foreach (var redoAction in redoStack)
        {
            if (redoAction.isBuildEvent)
            {
                foreach(var obj in redoAction.affectedObjects)
                    if (obj != null) Destroy(obj);
            }
        }
        redoStack.Clear();
    }

    public void ToggleDeleteMode()
    {
        if (isSimulating) return;
        isDeleteMode = !isDeleteMode;
        if (isDeleteMode) CancelCreation(); 
    }

    public void DeleteBar(Bar bar, HistoryAction currentAction)
    {
        if (bar == null || !bar.gameObject.activeSelf) return;

        currentAction.affectedObjects.Add(bar.gameObject);
        bar.gameObject.SetActive(false); 

        if (bar.startPoint != null && bar.startPoint.ConnectedBars.Count == 0 && bar.startPoint.Runtime && bar.startPoint.gameObject.activeSelf)
        {
            currentAction.affectedObjects.Add(bar.startPoint.gameObject);
            bar.startPoint.gameObject.SetActive(false);
        }

        if (bar.endPoint != null && bar.endPoint.ConnectedBars.Count == 0 && bar.endPoint.Runtime && bar.endPoint.gameObject.activeSelf)
        {
            currentAction.affectedObjects.Add(bar.endPoint.gameObject);
            bar.endPoint.gameObject.SetActive(false);
        }
    }

    public void DeletePoint(Point p, HistoryAction currentAction)
    {
        if (p == null || !p.Runtime || !p.gameObject.activeSelf) return; 

        List<Bar> barsToDelete = new List<Bar>(p.ConnectedBars);
        foreach (Bar b in barsToDelete) DeleteBar(b, currentAction);

        if (p.gameObject.activeSelf)
        {
            currentAction.affectedObjects.Add(p.gameObject);
            p.gameObject.SetActive(false);
        }
    }

    private Bar CheckForExistingBar(Vector2 screenPos)
    {
        float zDepth = (Point.AllPoints.Count > 0) ? Point.AllPoints[0].transform.position.z : 0f;
        Plane plane = new Plane(Vector3.forward, new Vector3(0, 0, zDepth));
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        
        if (!plane.Raycast(ray, out float distance)) return null;
        
        Vector3 worldPos = ray.GetPoint(distance);
        Bar closestBar = null;
        float minDist = 0.5f; 

        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b.startPoint == null || b.endPoint == null) continue;

                float dist = DistancePointToLineSegment(worldPos, b.startPoint.transform.position, b.endPoint.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestBar = b;
                }
            }
        }
        return closestBar;
    }

    private float DistancePointToLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 lineDir = lineEnd - lineStart;
        float length = lineDir.magnitude;
        lineDir.Normalize();

        Vector3 pointVector = point - lineStart;
        float dotProduct = Vector3.Dot(pointVector, lineDir);

        if (dotProduct <= 0) return Vector3.Distance(point, lineStart);
        if (dotProduct >= length) return Vector3.Distance(point, lineEnd);

        Vector3 projection = lineStart + lineDir * dotProduct;
        return Vector3.Distance(point, projection);
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
        
        if (plane.Raycast(ray, out float distance)) return ray.GetPoint(distance);
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
        
        // Enforce the budget constraint on the final drop!
        if (BuildUIController.Instance != null && activeMaterial != null)
        {
            float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
            float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
            float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
            if (maxAffordable < limit) limit = maxAffordable;
        }

        Vector3 startPos = currentStartPoint.transform.position;

        // Max Length Leniency 
        float distanceToTarget = Vector3.Distance(startPos, finalPosition);
        if (existingEndPoint != null && distanceToTarget > limit && distanceToTarget <= limit + 0.2f)
        {
            limit = distanceToTarget; 
        }

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

        // --- THE FIX IS HERE ---
        // Prevent Overlapping Grid Nodes, but IGNORE the ghost point (currentEndPoint)
        if (existingEndPoint == null)
        {
            foreach (Point p in Point.AllPoints)
            {
                // We added '&& p != currentEndPoint' so it stops deleting itself!
                if (p != currentStartPoint && p != currentEndPoint && Vector3.Distance(p.transform.position, finalPosition) < 0.05f)
                {
                    existingEndPoint = p;
                    finalPosition = p.transform.position; // Lock perfectly to the existing node
                    break;
                }
            }
        }

        if (Vector3.Distance(startPos, finalPosition) < 0.1f) 
        {
            CancelCreation();
            return;
        }

        bool createdNewPoint = (existingEndPoint == null);

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
        currentBar.startPoint = currentStartPoint;
        currentBar.endPoint = currentEndPoint;

        if (!currentStartPoint.ConnectedBars.Contains(currentBar)) currentStartPoint.ConnectedBars.Add(currentBar);
        if (!currentEndPoint.ConnectedBars.Contains(currentBar)) currentEndPoint.ConnectedBars.Add(currentBar);
        
        barCreationStarted = false;

        HistoryAction buildAction = new HistoryAction { isBuildEvent = true };
        buildAction.affectedObjects.Add(currentBar.gameObject);
        if (createdNewPoint) buildAction.affectedObjects.Add(currentEndPoint.gameObject);
        RecordAction(buildAction);

        currentStartPoint = null;
        currentEndPoint = null;
        currentBar = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;
    }

    public void CancelCreation()
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
        
        if (BuildUIController.Instance != null && activeMaterial != null)
        {
            float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
            float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
            float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
            if (maxAffordable < radius) radius = maxAffordable;
        }

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