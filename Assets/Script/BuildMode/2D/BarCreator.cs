using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

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

public class BarCreator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler 
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
    private BridgeMaterialSO previousNonPierMaterial;

    [Header("Modes & Settings")]
    public bool isGridSnappingEnabled = true;
    public bool isDeleteMode = false;
    
    [HideInInspector] public bool isSimulating = false; 

    public bool IsCreating => barCreationStarted;
    public bool IsErasing => isDeleteMode && currentSwipeDeleteAction != null;

    [Header("Pier Settings")]
    public float pierBaseY = -10f; 
    private Bar ghostPierBar; 

    [Header("Snapping Sensitivity")]
    public float deleteSnapRadiusPixels = 50f; 
    public float nodeSnapRadiusWorld = 1.2f;

    [Header("Visual Aids")]
    public Image gridVisual; 
    public LineRenderer radiusIndicator; 
    public int circleResolution = 50;    
    public float circleLineWidth = 0.05f;

    private bool barCreationStarted = false;
    private bool createdStartPoint = false; 

    private Stack<HistoryAction> undoStack = new Stack<HistoryAction>();
    private Stack<HistoryAction> redoStack = new Stack<HistoryAction>();
    private HistoryAction currentSwipeDeleteAction;

    private void OnEnable() { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

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

    private void HandleEnterBuildMode() { isSimulating = false; }
    private void HandleExitBuildMode() 
    { 
        CancelCreation(); 
        isDeleteMode = false; 
        isSimulating = false; 
        if (ghostPierBar != null) Destroy(ghostPierBar.gameObject);
    }

    private Camera GetActiveCamera()
    {
        if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null && GameManager.Instance.ActiveBuildLocation.locationCamera != null)
            return GameManager.Instance.ActiveBuildLocation.locationCamera;
        return Camera.main;
    }

    private Vector2 GetPointerPosition()
    {
        if (Touch.activeTouches.Count > 0) return Touch.activeTouches[0].screenPosition;
        if (Pointer.current != null) return Pointer.current.position.ReadValue();
        return Input.mousePosition;
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating) return;

        if (Touch.activeTouches.Count > 1)
        {
            if (barCreationStarted) CancelCreation();
            return;
        }

        if (activeMaterial != null && activeMaterial.isPier && !barCreationStarted && !isDeleteMode)
        {
            if (ghostPierBar == null) CreateGhostPierBar();

            Vector2 screenPos = GetPointerPosition();
            Vector3 worldPos = GetWorldMousePosition(screenPos);
            
            float snapThreshold = 1.5f; 
            float alignedX = worldPos.x;
            float bridgeZ = Point.AllPoints.Count > 0 ? Point.AllPoints[0].transform.position.z : 0f;

            foreach (Point p in Point.AllPoints)
            {
                if (p.gameObject.activeSelf)
                {
                    float xDiff = Mathf.Abs(p.transform.position.x - worldPos.x);
                    if (xDiff < snapThreshold)
                    {
                        snapThreshold = xDiff;
                        alignedX = p.transform.position.x;
                    }
                }
            }

            Vector3 floorPos = new Vector3(alignedX, pierBaseY, bridgeZ);
            
            float targetY = Mathf.Max(worldPos.y, pierBaseY + 0.5f); 
            if (isGridSnappingEnabled)
            {
                targetY = Mathf.Round(targetY);
                if (targetY <= pierBaseY) targetY = pierBaseY + 1f;
            }

            Vector3 targetPos = new Vector3(alignedX, targetY, bridgeZ);

            float maxLen = activeMaterial.maxLength;
            if (BuildUIController.Instance != null)
            {
                float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
                float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
                float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
                if (maxAffordable < maxLen) maxLen = maxAffordable;
            }

            if (Vector3.Distance(floorPos, targetPos) > maxLen)
            {
                targetPos = floorPos + Vector3.up * maxLen;
            }

            ghostPierBar.gameObject.SetActive(true);
            ghostPierBar.StartPosition = floorPos;
            ghostPierBar.UpdateCreatingBar(targetPos);
        }
        else if (ghostPierBar != null)
        {
            ghostPierBar.gameObject.SetActive(false);
        }

        if (barCreationStarted && currentEndPoint != null && !isDeleteMode)
        {
            Vector2 screenPos = GetPointerPosition();
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldMousePos = GetWorldMousePosition(screenPos);
            Vector3 targetPos = CalculateTargetPosition(worldMousePos, hoveredNode);

            if (activeMaterial != null && activeMaterial.isPier)
            {
                float snapThreshold = 1.5f; 
                float alignedX = worldMousePos.x;

                foreach (Point p in Point.AllPoints)
                {
                    if (p.gameObject.activeSelf && p != currentStartPoint && p != currentEndPoint)
                    {
                        float xDiff = Mathf.Abs(p.transform.position.x - worldMousePos.x);
                        if (xDiff < snapThreshold)
                        {
                            snapThreshold = xDiff;
                            alignedX = p.transform.position.x;
                        }
                    }
                }

                if (isGridSnappingEnabled && snapThreshold == 1.5f) 
                {
                    alignedX = Mathf.Round(alignedX);
                }

                Vector3 newStartPos = currentStartPoint.transform.position;
                newStartPos.x = alignedX;
                currentStartPoint.transform.position = newStartPos;
                currentBar.StartPosition = newStartPos;
                
                targetPos.x = alignedX;
                
                if (targetPos.y <= pierBaseY + 0.5f) targetPos.y = pierBaseY + 1f;
            }

            float maxLen = activeMaterial != null ? activeMaterial.maxLength : 5f;
            
            if (BuildUIController.Instance != null && activeMaterial != null)
            {
                float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
                float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
                float maxAffordableLength = Mathf.Max(0f, remainingBudget / costPerMeter);
                if (maxAffordableLength < maxLen) maxLen = maxAffordableLength;
            }

            Vector3 startPos = currentStartPoint.transform.position;
            
            if (Vector3.Distance(startPos, targetPos) > maxLen)
            {
                Vector3 direction = (targetPos - startPos).normalized;
                targetPos = startPos + (direction * maxLen);

                if (isGridSnappingEnabled && hoveredNode == null)
                {
                    targetPos = new Vector3(Mathf.RoundToInt(targetPos.x), Mathf.RoundToInt(targetPos.y), targetPos.z);
                    if (activeMaterial != null && activeMaterial.isPier) targetPos.x = startPos.x;
                    if (Vector3.Distance(startPos, targetPos) > maxLen) targetPos = startPos + (direction * maxLen); 
                }
            }

            currentEndPoint.transform.position = targetPos;
            currentBar.UpdateCreatingBar(targetPos);
        }
    }

    private void CreateGhostPierBar()
    {
        GameObject obj = Instantiate(barToInstantiate, barParent);
        obj.name = "GhostPierBar";
        ghostPierBar = obj.GetComponent<Bar>();
        ghostPierBar.Initialize(activeMaterial);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating || Touch.activeTouches.Count > 1) return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            CancelCreation();
            return;
        }

        Vector2 screenPos = eventData.position;

        if (isDeleteMode && eventData.button == PointerEventData.InputButton.Left)
        {
            currentSwipeDeleteAction = new HistoryAction { isBuildEvent = false };
            PerformSwipeDelete(screenPos);
            return; 
        }

        Point hoveredNode = CheckForExistingPoint(screenPos);
        if (!barCreationStarted && eventData.button == PointerEventData.InputButton.Left)
        {
            if (activeMaterial != null && activeMaterial.isPier)
            {
                Vector3 worldPos = GetWorldMousePosition(screenPos);
                float alignedX = worldPos.x;
                float snapThreshold = 1.5f; 
                float bridgeZ = Point.AllPoints.Count > 0 ? Point.AllPoints[0].transform.position.z : 0f;

                foreach (Point p in Point.AllPoints)
                {
                    if (p.gameObject.activeSelf)
                    {
                        float xDiff = Mathf.Abs(p.transform.position.x - worldPos.x);
                        if (xDiff < snapThreshold)
                        {
                            snapThreshold = xDiff;
                            alignedX = p.transform.position.x;
                        }
                    }
                }

                if (isGridSnappingEnabled && snapThreshold == 1.5f)
                {
                    alignedX = Mathf.Round(alignedX);
                }

                Vector3 startPos = new Vector3(alignedX, pierBaseY, bridgeZ);

                GameObject startObj = Instantiate(pointToInstantiate, startPos, Quaternion.identity, pointParent);
                startObj.name = "PierTip";
                currentStartPoint = startObj.GetComponent<Point>();
                
                currentStartPoint.originalIsAnchor = true; 
                currentStartPoint.isAnchor = true; 
                currentStartPoint.UpdateMaterial();
                
                createdStartPoint = true;
                barCreationStarted = true; 
                
                if (ghostPierBar != null) ghostPierBar.gameObject.SetActive(false); 
                
                StartBarCreation(startPos);
            }
            else if (hoveredNode != null) 
            {
                currentStartPoint = hoveredNode;
                createdStartPoint = false;
                barCreationStarted = true;
                StartBarCreation(hoveredNode.transform.position);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (isDeleteMode && currentSwipeDeleteAction != null) PerformSwipeDelete(eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating) return; 

        if (isDeleteMode)
        {
            if (currentSwipeDeleteAction != null && currentSwipeDeleteAction.affectedObjects.Count > 0) RecordAction(currentSwipeDeleteAction);
            currentSwipeDeleteAction = null;
            return;
        }

        if (barCreationStarted && eventData.button == PointerEventData.InputButton.Left && !isDeleteMode)
        {
            Vector2 screenPos = GetPointerPosition();
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldPos = GetWorldMousePosition(screenPos);
            FinishBarCreation(worldPos, hoveredNode);
        }
    }

    private void PerformSwipeDelete(Vector2 screenPos)
    {
        Point hoveredPoint = CheckForExistingPoint(screenPos);
        if (hoveredPoint != null) { DeletePoint(hoveredPoint, currentSwipeDeleteAction); return; }

        Bar hoveredBar = CheckForExistingBar(screenPos);
        if (hoveredBar != null) DeleteBar(hoveredBar, currentSwipeDeleteAction);
    }

    public void Undo()
    {
        if (isSimulating || undoStack.Count == 0 || (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)) return;
        CancelCreation(); 
        HistoryAction action = undoStack.Pop();
        action.Undo();
        redoStack.Push(action);
        RefreshAllPoints(); 

        // --- OPTIMIZATION HOOK ---
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    public void Redo()
    {
        if (isSimulating || redoStack.Count == 0 || (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building)) return;
        CancelCreation(); 
        HistoryAction action = redoStack.Pop();
        action.Redo();
        undoStack.Push(action);
        RefreshAllPoints(); 

        // --- OPTIMIZATION HOOK ---
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
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

    public void ToggleDeleteMode() { if (isSimulating) return; isDeleteMode = !isDeleteMode; if (isDeleteMode) CancelCreation(); }

    public void DeleteBar(Bar bar, HistoryAction currentAction)
    {
        if (bar == null || !bar.gameObject.activeSelf) return;

        currentAction.affectedObjects.Add(bar.gameObject);
        bar.gameObject.SetActive(false); 

        Point p1 = bar.startPoint;
        Point p2 = bar.endPoint;

        if (p1 != null && p1.ConnectedBars.Count == 0 && p1.Runtime && p1.gameObject.activeSelf)
        {
            currentAction.affectedObjects.Add(p1.gameObject);
            p1.gameObject.SetActive(false);
        }

        if (p2 != null && p2.ConnectedBars.Count == 0 && p2.Runtime && p2.gameObject.activeSelf)
        {
            currentAction.affectedObjects.Add(p2.gameObject);
            p2.gameObject.SetActive(false);
        }

        if (p1 != null && p1.gameObject.activeSelf) p1.EvaluateAnchorState();
        if (p2 != null && p2.gameObject.activeSelf) p2.EvaluateAnchorState();

        // --- OPTIMIZATION HOOK ---
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
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
        Camera cam = GetActiveCamera();
        Bar closestBar = null;
        float minSqrDist = deleteSnapRadiusPixels * deleteSnapRadiusPixels;

        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b == null || !b.gameObject.activeSelf || b.startPoint == null || b.endPoint == null) continue;

                Vector3 startScreenPos = cam.WorldToScreenPoint(b.startPoint.transform.position);
                Vector3 endScreenPos = cam.WorldToScreenPoint(b.endPoint.transform.position);

                if (startScreenPos.z > 0 && endScreenPos.z > 0)
                {
                    float sqrDist = SqrDistancePointToLineSegment(screenPos, startScreenPos, endScreenPos);
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        closestBar = b;
                    }
                }
            }
        }
        return closestBar;
    }

    private float SqrDistancePointToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 lineDir = lineEnd - lineStart;
        float sqrLength = lineDir.sqrMagnitude;
        if (sqrLength == 0) return (point - lineStart).sqrMagnitude;
        
        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, lineDir) / sqrLength);
        Vector2 projection = lineStart + t * lineDir;
        return (point - projection).sqrMagnitude;
    }

    public void SetActiveMaterial(BridgeMaterialSO newMaterial)
    {
        if (newMaterial != null)
        {
            if (activeMaterial != null && !activeMaterial.isPier)
            {
                previousNonPierMaterial = activeMaterial;
            }
            else if (previousNonPierMaterial == null && !newMaterial.isPier)
            {
                previousNonPierMaterial = newMaterial;
            }

            activeMaterial = newMaterial;
            if (barCreationStarted) DrawRadiusCircle(); 
            
            if (!activeMaterial.isPier && ghostPierBar != null)
            {
                Destroy(ghostPierBar.gameObject);
                ghostPierBar = null;
            }
            else if (activeMaterial.isPier && ghostPierBar != null)
            {
                ghostPierBar.Initialize(activeMaterial);
            }
        }
    }

    public void ToggleGrid() { isGridSnappingEnabled = !isGridSnappingEnabled; if (gridVisual != null) gridVisual.canvasRenderer.SetAlpha(isGridSnappingEnabled ? 1f : 0f); }

    private Vector3 CalculateTargetPosition(Vector3 rawPos, Point hoveredNode)
    {
        if (hoveredNode != null) return hoveredNode.transform.position;
        float lockedZ = currentStartPoint != null ? currentStartPoint.transform.position.z : 0f;
        if (isGridSnappingEnabled) return new Vector3(Mathf.RoundToInt(rawPos.x), Mathf.RoundToInt(rawPos.y), lockedZ);
        return new Vector3(rawPos.x, rawPos.y, lockedZ);
    }

    private Point CheckForExistingPoint(Vector2 screenPos)
    {
        Camera cam = GetActiveCamera();
        Ray ray = cam.ScreenPointToRay(screenPos);
        
        Point closest = null;
        float minRayDist = nodeSnapRadiusWorld; 
        
        foreach (Point p in Point.AllPoints)
        {
            if (p == currentEndPoint || !p.gameObject.activeSelf) continue;

            float distToRay = Vector3.Cross(ray.direction, p.transform.position - ray.origin).magnitude;

            if (distToRay < minRayDist)
            {
                minRayDist = distToRay;
                closest = p;
            }
        }
        return closest;
    }

    private Vector3 GetWorldMousePosition(Vector2 screenPos)
    {
        Camera cam = GetActiveCamera();

        if (currentStartPoint == null)
        {
            float bridgeZ = 0f;
            if (Point.AllPoints.Count > 0) 
            {
                bridgeZ = Point.AllPoints[0].transform.position.z;
            }

            Plane flatWorldPlane = new Plane(Vector3.back, new Vector3(0, 0, bridgeZ));
            Ray ray = cam.ScreenPointToRay(screenPos);
            
            if (flatWorldPlane.Raycast(ray, out float distance)) 
            {
                return ray.GetPoint(distance);
            }
            return Vector3.zero; 
        }
        else
        {
            Vector3 referencePoint = currentStartPoint.transform.position;
            Plane cameraPlane = new Plane(-cam.transform.forward, referencePoint);
            Ray ray = cam.ScreenPointToRay(screenPos);
            
            if (cameraPlane.Raycast(ray, out float distance))
            {
                Vector3 hitPoint = ray.GetPoint(distance);
                Vector3 localOffset = hitPoint - referencePoint;
                return referencePoint + new Vector3(Vector3.Dot(localOffset, cam.transform.right), Vector3.Dot(localOffset, cam.transform.up), 0);
            }
            return referencePoint;
        }
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
        
        if (activeMaterial != null && activeMaterial.isPier) finalPosition.x = currentStartPoint.transform.position.x;

        float limit = activeMaterial != null ? activeMaterial.maxLength : 5f;
        
        if (BuildUIController.Instance != null && activeMaterial != null)
        {
            float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
            float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
            float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
            if (maxAffordable < limit) limit = maxAffordable;
        }

        Vector3 startPos = currentStartPoint.transform.position;
        float distanceToTarget = Vector3.Distance(startPos, finalPosition);
        
        if (existingEndPoint != null && distanceToTarget > limit && distanceToTarget <= limit + 0.2f) limit = distanceToTarget; 

        if (Vector3.Distance(startPos, finalPosition) > limit)
        {
            Vector3 direction = (finalPosition - startPos).normalized;
            finalPosition = startPos + (direction * limit);
            
            if (isGridSnappingEnabled && existingEndPoint == null)
            {
                finalPosition = new Vector3(Mathf.RoundToInt(finalPosition.x), Mathf.RoundToInt(finalPosition.y), finalPosition.z);
                if (activeMaterial != null && activeMaterial.isPier) finalPosition.x = startPos.x;
                if (Vector3.Distance(startPos, finalPosition) > limit) finalPosition = startPos + (direction * limit);
            }
            existingEndPoint = null; 
        }

        if (existingEndPoint == null)
        {
            foreach (Point p in Point.AllPoints)
            {
                if (p != currentStartPoint && p != currentEndPoint && p.gameObject.activeSelf && Vector3.Distance(p.transform.position, finalPosition) < 0.05f)
                {
                    existingEndPoint = p;
                    finalPosition = p.transform.position; 
                    break;
                }
            }
        }

        if (Vector3.Distance(startPos, finalPosition) < 0.1f) 
        {
            CancelCreation();
            return;
        }

        bool createdNewEndPoint = (existingEndPoint == null);
        bool createdNewStartPoint = createdStartPoint;

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

        if (activeMaterial != null && activeMaterial.isPier)
        {
            currentStartPoint.isAnchor = true;
            currentStartPoint.UpdateMaterial();
            currentEndPoint.isAnchor = true;
            currentEndPoint.UpdateMaterial();
        }

        currentBar.UpdateCreatingBar(finalPosition);
        currentBar.startPoint = currentStartPoint;
        currentBar.endPoint = currentEndPoint;

        if (!currentStartPoint.ConnectedBars.Contains(currentBar)) currentStartPoint.ConnectedBars.Add(currentBar);
        if (!currentEndPoint.ConnectedBars.Contains(currentBar)) currentEndPoint.ConnectedBars.Add(currentBar);
        
        currentStartPoint.EvaluateAnchorState();
        currentEndPoint.EvaluateAnchorState();

        barCreationStarted = false;
        createdStartPoint = false; 

        HistoryAction buildAction = new HistoryAction { isBuildEvent = true };
        buildAction.affectedObjects.Add(currentBar.gameObject);
        if (createdNewStartPoint) buildAction.affectedObjects.Add(currentStartPoint.gameObject); 
        if (createdNewEndPoint) buildAction.affectedObjects.Add(currentEndPoint.gameObject);
        RecordAction(buildAction);

        currentStartPoint = null;
        currentEndPoint = null;
        currentBar = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;

        // --- OPTIMIZATION HOOK ---
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();

        if (activeMaterial != null && activeMaterial.isPier && previousNonPierMaterial != null)
        {
            SetActiveMaterial(previousNonPierMaterial);
        }
    }

    public void CancelCreation()
    {
        barCreationStarted = false;
        if (currentBar != null) Destroy(currentBar.gameObject);
        if (currentEndPoint != null) Destroy(currentEndPoint.gameObject);
        
        if (createdStartPoint && currentStartPoint != null) 
        {
            Destroy(currentStartPoint.gameObject);
        }

        createdStartPoint = false;
        currentStartPoint = null;
        currentEndPoint = null;

        if (radiusIndicator != null) radiusIndicator.enabled = false;
        
        // --- OPTIMIZATION HOOK ---
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    private void RefreshAllPoints()
    {
        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf)
            {
                p.EvaluateAnchorState();
            }
        }
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

        Vector3 right = Vector3.right;
        Vector3 up = Vector3.up;
        float angleStep = 360f / circleResolution;

        for (int i = 0; i <= circleResolution; i++)
        {
            float currentAngle = i * angleStep * Mathf.Deg2Rad;
            Vector3 pos = center + (right * Mathf.Cos(currentAngle) * radius) + (up * Mathf.Sin(currentAngle) * radius);
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