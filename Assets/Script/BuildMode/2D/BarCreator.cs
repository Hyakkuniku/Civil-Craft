using System; // <-- ADDED for the Action Event
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class BarCreator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler 
{
    // --- ADDED THIS LINE: Broadcasts when the material changes so UI can update efficiently ---
    public event Action<BridgeMaterialSO> OnActiveMaterialChanged; 

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
    
    [Header("Selection & Move Tools")]
    public bool isSelectMode = false;
    public bool isMoveMode = false; 
    public RectTransform selectionBoxUI; 
    private Vector2 selectionStartPos;
    private List<Point> selectedPoints = new List<Point>();
    
    private bool isDraggingSelection = false;
    private bool isDraggingSelectionBox = false; 
    private Vector3 dragStartMouseWorld;
    private Vector3 dragLastValidDelta;
    private HistoryAction currentMoveAction;

    [HideInInspector] public bool isSimulating = false; 

    public bool IsCreating => barCreationStarted;
    public bool IsErasing => isDeleteMode && currentSwipeDeleteAction != null;
    public bool IsSelecting => isSelectMode; 
    public bool IsMoving => isMoveMode; 
    public bool IsPasting => ClipboardManager.Instance != null && ClipboardManager.Instance.isPasteMode; 

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
        if (selectionBoxUI != null) selectionBoxUI.gameObject.SetActive(false); 
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
        CancelAllModes();
        isSimulating = false; 
        if (ghostPierBar != null) Destroy(ghostPierBar.gameObject);
    }

    public void CancelAllModes()
    {
        isSelectMode = false;
        isMoveMode = false;
        isDeleteMode = false;
        if (ClipboardManager.Instance != null) ClipboardManager.Instance.CancelPasteMode();
        CancelCreation();
        ClearSelection();
    }

    public Camera GetActiveCamera()
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

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = GetPointerPosition();
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        return results.Count > 0 && results[0].gameObject != this.gameObject;
    }

    public List<Point> GetSelectedPoints()
    {
        return selectedPoints;
    }
    
    public void ClearSelectionPublic()
    {
        ClearSelection();
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating) return;

        if (Touch.activeTouches.Count > 1)
        {
            if (barCreationStarted) CancelCreation();
            if (selectionBoxUI != null) selectionBoxUI.gameObject.SetActive(false); 
            if (ClipboardManager.Instance != null && ClipboardManager.Instance.isPasteMode) ClipboardManager.Instance.isDraggingSelection = false; 
            return;
        }

        if (isMoveMode && isDraggingSelection && selectedPoints.Count > 0)
        {
            Vector3 worldMousePos = GetWorldMousePosition(GetPointerPosition());
            Vector3 mouseDelta = worldMousePos - dragStartMouseWorld;
            
            Point primaryNode = selectedPoints[0];
            Vector3 originalPrimaryPos = currentMoveAction.originalPositions[primaryNode];
            Vector3 targetPrimaryPos = originalPrimaryPos + mouseDelta;
            
            if (isGridSnappingEnabled)
            {
                targetPrimaryPos = new Vector3(Mathf.Round(targetPrimaryPos.x), Mathf.Round(targetPrimaryPos.y), targetPrimaryPos.z);
            }

            Vector3 finalDelta = targetPrimaryPos - originalPrimaryPos;
            Point constraintCenter = null;
            float constraintRadius = 0f;
            Color constraintColor = Color.white;
            
            for (int iter = 0; iter < 15; iter++) 
            {
                bool constraintHit = false;
                foreach (Point p in selectedPoints)
                {
                    if (p.originalIsAnchor) continue; 
                    
                    foreach (Bar b in p.ConnectedBars)
                    {
                        if (b == null || !b.gameObject.activeSelf || b.materialData.isPier) continue;
                        
                        Point otherPoint = (b.startPoint == p) ? b.endPoint : b.startPoint;
                        if (selectedPoints.Contains(otherPoint) && !otherPoint.originalIsAnchor) continue; 
                        
                        float maxLen = b.materialData.maxLength;
                        Vector3 movingNodeOriginalPos = currentMoveAction.originalPositions[p];
                        Vector3 proposedPos = movingNodeOriginalPos + finalDelta;
                        Vector3 staticPos = otherPoint.transform.position; 
                        
                        if (Vector3.Distance(staticPos, proposedPos) > maxLen + 0.001f) 
                        {
                            Vector3 dir = (proposedPos - staticPos).normalized;
                            if (dir == Vector3.zero) dir = Vector3.up; 
                            
                            Vector3 clampedPos = staticPos + (dir * maxLen);
                            Vector3 correction = clampedPos - movingNodeOriginalPos;
                            finalDelta = Vector3.Lerp(finalDelta, correction, 0.8f); 
                            
                            constraintHit = true;
                            constraintCenter = otherPoint;
                            constraintRadius = maxLen;
                            constraintColor = b.materialData.gizmoColor;
                        }
                    }
                }
                if (!constraintHit) break; 
            }

            bool isSafe = true;
            foreach (Point p in selectedPoints)
            {
                if (p.originalIsAnchor) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b == null || !b.gameObject.activeSelf || b.materialData.isPier) continue; 
                    Point otherPoint = (b.startPoint == p) ? b.endPoint : b.startPoint;
                    if (selectedPoints.Contains(otherPoint) && !otherPoint.originalIsAnchor) continue; 
                    
                    Vector3 proposedPos = currentMoveAction.originalPositions[p] + finalDelta;
                    if (Vector3.Distance(otherPoint.transform.position, proposedPos) > b.materialData.maxLength + 0.05f) 
                    {
                        isSafe = false;
                        break;
                    }
                }
                if (!isSafe) break;
            }

            if (isSafe) dragLastValidDelta = finalDelta;
            else finalDelta = dragLastValidDelta; 

            foreach (Point p in selectedPoints)
            {
                if (!p.originalIsAnchor) p.transform.position = currentMoveAction.originalPositions[p] + finalDelta;
            }
            
            // --- BUG FIX: Enforce Pier rules dynamically during movement ---
            foreach (Point p in selectedPoints)
            {
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.materialData.isPier)
                    {
                        Point pBot = b.startPoint.transform.position.y < b.endPoint.transform.position.y ? b.startPoint : b.endPoint;
                        Point pTop = b.startPoint.transform.position.y > b.endPoint.transform.position.y ? b.startPoint : b.endPoint;
                        
                        Vector3 botPos = pBot.transform.position;
                        Vector3 topPos = pTop.transform.position;
                        
                        // Force X alignment (if top moves, bottom follows. If bottom moves alone, top follows)
                        if (selectedPoints.Contains(pBot) && !selectedPoints.Contains(pTop)) topPos.x = botPos.x;
                        else botPos.x = topPos.x; 
                        
                        // Force Bottom Y to be locked to the floor
                        botPos.y = pierBaseY; 
                        
                        // Force Y limits
                        if (topPos.y < botPos.y + 1f) topPos.y = botPos.y + 1f;
                        if (topPos.y > botPos.y + b.materialData.maxLength) topPos.y = botPos.y + b.materialData.maxLength;
                        
                        pBot.transform.position = botPos;
                        pTop.transform.position = topPos;
                    }
                }
            }

            UpdateBarsForSelectedPoints();

            if (constraintCenter != null) DrawMoveRadius(constraintCenter.transform.position, constraintRadius, constraintColor);
            else if (radiusIndicator != null) radiusIndicator.enabled = false;

            return; 
        }

        if (activeMaterial != null && activeMaterial.isPier && !barCreationStarted && !isDeleteMode && !isSelectMode && !isMoveMode && !IsPasting)
        {
            if (IsPointerOverUI())
            {
                if (ghostPierBar != null) ghostPierBar.gameObject.SetActive(false);
            }
            else
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

                if (Vector3.Distance(floorPos, targetPos) > maxLen) targetPos = floorPos + Vector3.up * maxLen;

                ghostPierBar.gameObject.SetActive(true);
                ghostPierBar.StartPosition = floorPos;
                ghostPierBar.UpdateCreatingBar(targetPos);
            }
        }
        else if (ghostPierBar != null)
        {
            ghostPierBar.gameObject.SetActive(false);
        }

        if (barCreationStarted && currentEndPoint != null && !isDeleteMode && !isSelectMode && !isMoveMode && !IsPasting)
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

                if (isGridSnappingEnabled && snapThreshold == 1.5f) alignedX = Mathf.Round(alignedX);

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

    public void DeleteSelected()
    {
        if (selectedPoints.Count == 0) return;

        HistoryAction deleteAction = new HistoryAction { isBuildEvent = false };
        List<Point> pointsToProcess = new List<Point>(selectedPoints);
        
        foreach (Point p in pointsToProcess)
        {
            DeletePoint(p, deleteAction);
        }

        if (deleteAction.affectedObjects.Count > 0)
        {
            if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(deleteAction);
        }

        ClearSelection();

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.SetSelectionPanelActive(false);
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Bulk Selection Deleted");
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

        if (eventData.button != PointerEventData.InputButton.Left) return;

        Vector2 screenPos = eventData.position;

        if (IsPasting && eventData.button == PointerEventData.InputButton.Left)
        {
            ClipboardManager.Instance.HandlePointerDown(eventData);
            return;
        }

        if (isMoveMode && eventData.button == PointerEventData.InputButton.Left)
        {
            Point hoveredNode = CheckForExistingPoint(screenPos);
            if (hoveredNode != null)
            {
                if (!selectedPoints.Contains(hoveredNode))
                {
                    ClearSelection();
                    hoveredNode.isSelected = true;
                    hoveredNode.UpdateMaterial();
                    selectedPoints.Add(hoveredNode);
                }
                else
                {
                    selectedPoints.Remove(hoveredNode);
                    selectedPoints.Insert(0, hoveredNode);
                }

                isDraggingSelection = true;
                dragStartMouseWorld = GetWorldMousePosition(screenPos);
                dragLastValidDelta = Vector3.zero; 
                
                // --- BUG FIX: Populate original positions for ALL points just in case Pier nodes are implicitly moved ---
                currentMoveAction = new HistoryAction { isMoveEvent = true };
                foreach (Point p in Point.AllPoints) 
                {
                    if (p.gameObject.activeSelf) currentMoveAction.originalPositions[p] = p.transform.position;
                }
            }
            return;
        }

        if (isSelectMode && eventData.button == PointerEventData.InputButton.Left)
        {
            selectionStartPos = screenPos;
            isDraggingSelectionBox = false; 
            return;
        }

        if (isDeleteMode && eventData.button == PointerEventData.InputButton.Left)
        {
            currentSwipeDeleteAction = new HistoryAction { isBuildEvent = false };
            PerformSwipeDelete(screenPos);
            return; 
        }

        Point existingNode = CheckForExistingPoint(screenPos);
        
        if (!barCreationStarted && eventData.button == PointerEventData.InputButton.Left && activeMaterial != null && !isSelectMode && !isMoveMode && !IsPasting && !isDeleteMode)
        {
            if (activeMaterial.isPier)
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

                if (isGridSnappingEnabled && snapThreshold == 1.5f) alignedX = Mathf.Round(alignedX);
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
            else if (existingNode != null) 
            {
                currentStartPoint = existingNode;
                createdStartPoint = false;
                barCreationStarted = true;
                StartBarCreation(existingNode.transform.position);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (isDeleteMode && currentSwipeDeleteAction != null) PerformSwipeDelete(eventData.position);
        
        if (isSelectMode)
        {
            if (!isDraggingSelectionBox && Vector2.Distance(selectionStartPos, eventData.position) > 15f)
            {
                isDraggingSelectionBox = true;
                ClearSelection(); 
                if (selectionBoxUI != null) selectionBoxUI.gameObject.SetActive(true);
            }

            if (isDraggingSelectionBox) UpdateSelectionBox(eventData.position);
        }

        if (IsPasting)
        {
            ClipboardManager.Instance.HandleDrag(eventData);
            return;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating) return; 

        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (IsPasting)
        {
            ClipboardManager.Instance.HandlePointerUp(eventData);
            return;
        }

        if (isMoveMode)
        {
            if (isDraggingSelection)
            {
                isDraggingSelection = false;
                if (radiusIndicator != null) radiusIndicator.enabled = false; 
                
                if (currentMoveAction != null)
                {
                    // --- BUG FIX: Finalize all moved points for Undo/Redo ---
                    foreach (Point p in Point.AllPoints) 
                    {
                        if (p.gameObject.activeSelf) currentMoveAction.newPositions[p] = p.transform.position;
                    }

                    if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(currentMoveAction);
                    currentMoveAction = null;
                    if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Selection Moved");
                }
            }
            return; 
        }

        if (isSelectMode)
        {
            if (isDraggingSelectionBox)
            {
                isDraggingSelectionBox = false;
                if (selectionBoxUI != null) selectionBoxUI.gameObject.SetActive(false);
                SelectPointsInBox(selectionStartPos, eventData.position);
            }
            else
            {
                Point tappedPoint = CheckForExistingPoint(eventData.position);
                if (tappedPoint != null) TogglePointSelection(tappedPoint);
                else
                {
                    Bar tappedBar = CheckForExistingBar(eventData.position);
                    if (tappedBar != null) ToggleBarSelection(tappedBar);
                    else ClearSelection(); 
                }

                if (BuildUIController.Instance != null && selectedPoints.Count > 0)
                {
                    BuildUIController.Instance.SetSelectionPanelActive(true);
                }
            }
            return;
        }

        if (isDeleteMode)
        {
            if (currentSwipeDeleteAction != null && currentSwipeDeleteAction.affectedObjects.Count > 0) 
            {
                if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(currentSwipeDeleteAction);
            }
            currentSwipeDeleteAction = null;
            return;
        }

        if (barCreationStarted && activeMaterial != null && eventData.button == PointerEventData.InputButton.Left && !isDeleteMode && !isSelectMode && !isMoveMode && !IsPasting)
        {
            Vector2 screenPos = GetPointerPosition();
            Point hoveredNode = CheckForExistingPoint(screenPos);
            Vector3 worldPos = GetWorldMousePosition(screenPos);
            FinishBarCreation(worldPos, hoveredNode);
        }
        else
        {
            CancelCreation(); 
        }
    }

    private void TogglePointSelection(Point p)
    {
        if (selectedPoints.Contains(p))
        {
            p.isSelected = false;
            p.UpdateMaterial();
            selectedPoints.Remove(p);
        }
        else
        {
            p.isSelected = true;
            p.UpdateMaterial();
            selectedPoints.Add(p);
        }
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction(p.isSelected ? "Node Selected" : "Node Deselected");
    }

    private void ToggleBarSelection(Bar bar)
    {
        Point p1 = bar.startPoint;
        Point p2 = bar.endPoint;
        bool p1Selected = selectedPoints.Contains(p1);
        bool p2Selected = selectedPoints.Contains(p2);
        
        if (p1Selected && p2Selected)
        {
            p1.isSelected = false; p1.UpdateMaterial(); selectedPoints.Remove(p1);
            p2.isSelected = false; p2.UpdateMaterial(); selectedPoints.Remove(p2);
        }
        else
        {
            if (!p1Selected) { p1.isSelected = true; p1.UpdateMaterial(); selectedPoints.Add(p1); }
            if (!p2Selected) { p2.isSelected = true; p2.UpdateMaterial(); selectedPoints.Add(p2); }
        }
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Beam Selected");
    }

    public void ToggleSelectMode()
    {
        if (isSimulating) return;
        isSelectMode = !isSelectMode;
        if (isSelectMode) 
        { 
            isMoveMode = false; 
            isDeleteMode = false; 
            if (ClipboardManager.Instance != null) ClipboardManager.Instance.CancelPasteMode(); 
            CancelCreation(); 
            SetActiveMaterial(null); 
        }
        else ClearSelection();
        
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Select Mode: " + (isSelectMode ? "ON" : "OFF"));
    }

    public void ToggleMoveMode()
    {
        if (isSimulating) return;
        isMoveMode = !isMoveMode;
        if (isMoveMode) 
        { 
            isSelectMode = false; 
            isDeleteMode = false; 
            if (ClipboardManager.Instance != null) ClipboardManager.Instance.CancelPasteMode(); 
            CancelCreation(); 
            SetActiveMaterial(null); 
        }
        else if (radiusIndicator != null) radiusIndicator.enabled = false;

        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Move Mode: " + (isMoveMode ? "ON" : "OFF"));
    }

    public void ToggleDeleteMode() 
    { 
        if (isSimulating) return; 
        isDeleteMode = !isDeleteMode; 
        if (isDeleteMode) 
        { 
            CancelAllModes(); 
            isDeleteMode = true; 
            SetActiveMaterial(null);
            
            // --- ADDED THIS LINE: Tells UI buttons to deselect because we are erasing! ---
            OnActiveMaterialChanged?.Invoke(null);
        } 
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Delete Mode: " + (isDeleteMode ? "ON" : "OFF"));
    }

    public void SetActiveMaterial(BridgeMaterialSO newMaterial)
    {
        if (newMaterial != null)
        {
            if (activeMaterial != null && !activeMaterial.isPier) previousNonPierMaterial = activeMaterial;
            else if (previousNonPierMaterial == null && !newMaterial.isPier) previousNonPierMaterial = newMaterial;
        }

        activeMaterial = newMaterial;
        
        // --- ADDED THIS LINE: Broadcasts to the UI that the material changed! ---
        OnActiveMaterialChanged?.Invoke(activeMaterial);
        
        if (activeMaterial != null)
        {
            if (barCreationStarted) DrawRadiusVisual(); 
            
            if (!activeMaterial.isPier && ghostPierBar != null) { Destroy(ghostPierBar.gameObject); ghostPierBar = null; }
            else if (activeMaterial.isPier && ghostPierBar != null) ghostPierBar.Initialize(activeMaterial);
            
            CancelAllModes();
        }
        else
        {
            if (ghostPierBar != null) ghostPierBar.gameObject.SetActive(false);
        }
    }

    private void ClearSelection()
    {
        foreach (Point p in selectedPoints) if (p != null) { p.isSelected = false; p.UpdateMaterial(); }
        selectedPoints.Clear();
        if (BuildUIController.Instance != null) BuildUIController.Instance.SetSelectionPanelActive(false);
    }

    private void UpdateSelectionBox(Vector2 currentScreenPos)
    {
        if (selectionBoxUI == null || selectionBoxUI.parent == null) return;
        RectTransform parentRect = selectionBoxUI.parent as RectTransform;
        Camera uiCam = GetActiveCamera();
        Canvas canvas = selectionBoxUI.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) uiCam = null;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, selectionStartPos, uiCam, out Vector2 localStart);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, currentScreenPos, uiCam, out Vector2 localEnd);

        float width = localEnd.x - localStart.x;
        float height = localEnd.y - localStart.y;
        selectionBoxUI.anchorMin = new Vector2(0.5f, 0.5f);
        selectionBoxUI.anchorMax = new Vector2(0.5f, 0.5f);
        selectionBoxUI.pivot = new Vector2(0.5f, 0.5f);
        selectionBoxUI.sizeDelta = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
        selectionBoxUI.localPosition = localStart + new Vector2(width / 2, height / 2);
    }

    private void SelectPointsInBox(Vector2 startPos, Vector2 endPos)
    {
        ClearSelection(); 
        Camera cam = GetActiveCamera();
        Rect selectionRect = new Rect(Mathf.Min(startPos.x, endPos.x), Mathf.Min(startPos.y, endPos.y), Mathf.Abs(startPos.x - endPos.x), Mathf.Abs(startPos.y - endPos.y));

        foreach (Point p in Point.AllPoints)
        {
            if (p.gameObject.activeSelf) 
            {
                Vector2 screenPos = cam.WorldToScreenPoint(p.transform.position);
                if (selectionRect.Contains(screenPos)) { p.isSelected = true; p.UpdateMaterial(); selectedPoints.Add(p); }
            }
        }
        if (selectedPoints.Count > 0 && BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.SetSelectionPanelActive(true);
            BuildUIController.Instance.LogAction("Box Selection Applied");
        }
    }

    private void UpdateBarsForSelectedPoints()
    {
        HashSet<Bar> affectedBars = new HashSet<Bar>();
        foreach (Point p in selectedPoints) 
        {
            foreach (Bar b in p.ConnectedBars) 
            {
                if (b != null && b.gameObject.activeSelf) 
                {
                    affectedBars.Add(b);
                    
                    // --- BUG FIX: Also update the other end of any attached piers since we auto-align them!
                    if (b.materialData.isPier)
                    {
                        foreach(Bar botBar in b.startPoint.ConnectedBars) if (botBar.gameObject.activeSelf) affectedBars.Add(botBar);
                        foreach(Bar topBar in b.endPoint.ConnectedBars) if (topBar.gameObject.activeSelf) affectedBars.Add(topBar);
                    }
                }
            }
        }
        foreach (Bar b in affectedBars) { b.StartPosition = b.startPoint.transform.position; b.UpdateCreatingBar(b.endPoint.transform.position); }
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    private void PerformSwipeDelete(Vector2 screenPos)
    {
        Point hoveredPoint = CheckForExistingPoint(screenPos);
        if (hoveredPoint != null) { 
            DeletePoint(hoveredPoint, currentSwipeDeleteAction); 
            if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Deleted Node");
            return; 
        }
        Bar hoveredBar = CheckForExistingBar(screenPos);
        if (hoveredBar != null) {
            DeleteBar(hoveredBar, currentSwipeDeleteAction);
            if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Deleted Beam");
        }
    }

    public void DeleteBar(Bar bar, HistoryAction currentAction)
    {
        if (bar == null || !bar.gameObject.activeSelf) return;
        currentAction.affectedObjects.Add(bar.gameObject);
        bar.gameObject.SetActive(false); 
        Point p1 = bar.startPoint;
        Point p2 = bar.endPoint;
        if (p1 != null && p1.ConnectedBars.Count == 0 && p1.Runtime && p1.gameObject.activeSelf) { currentAction.affectedObjects.Add(p1.gameObject); p1.gameObject.SetActive(false); }
        if (p2 != null && p2.ConnectedBars.Count == 0 && p2.Runtime && p2.gameObject.activeSelf) { currentAction.affectedObjects.Add(p2.gameObject); p2.gameObject.SetActive(false); }
        if (p1 != null && p1.gameObject.activeSelf) p1.EvaluateAnchorState();
        if (p2 != null && p2.gameObject.activeSelf) p2.EvaluateAnchorState();
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    public void DeletePoint(Point p, HistoryAction currentAction)
    {
        if (p == null || !p.Runtime || !p.gameObject.activeSelf) return; 
        List<Bar> barsToDelete = new List<Bar>(p.ConnectedBars);
        foreach (Bar b in barsToDelete) DeleteBar(b, currentAction);
        if (p.gameObject.activeSelf) { currentAction.affectedObjects.Add(p.gameObject); p.gameObject.SetActive(false); }
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
                    if (sqrDist < minSqrDist) { minSqrDist = sqrDist; closestBar = b; }
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

    public void ToggleGrid() 
    { 
        isGridSnappingEnabled = !isGridSnappingEnabled; 
        if (gridVisual != null) gridVisual.canvasRenderer.SetAlpha(isGridSnappingEnabled ? 1f : 0f); 
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Grid Snapping: " + (isGridSnappingEnabled ? "ON" : "OFF"));
    }

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
            if (distToRay < minRayDist) { minRayDist = distToRay; closest = p; }
        }
        return closest;
    }

    public Vector3 GetWorldMousePosition(Vector2 screenPos)
    {
        Camera cam = GetActiveCamera();
        if (currentStartPoint == null)
        {
            float bridgeZ = Point.AllPoints.Count > 0 ? Point.AllPoints[0].transform.position.z : 0f;
            Plane flatWorldPlane = new Plane(Vector3.back, new Vector3(0, 0, bridgeZ));
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (flatWorldPlane.Raycast(ray, out float distance)) return ray.GetPoint(distance);
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
        DrawRadiusVisual();
    }

    private void FinishBarCreation(Vector3 rawWorldPos, Point existingEndPoint)
    {
        if (currentBar == null || currentStartPoint == null || currentEndPoint == null) 
        {
            CancelCreation();
            return;
        }

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
            if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Drawing Canceled");
            return; 
        }

        bool createdNewEndPoint = (existingEndPoint == null);
        bool createdNewStartPoint = createdStartPoint;
        if (existingEndPoint != null) { Destroy(currentEndPoint.gameObject); currentEndPoint = existingEndPoint; }
        else { currentEndPoint.name = "Point"; currentEndPoint.transform.position = finalPosition; }

        if (activeMaterial != null && activeMaterial.isPier) { currentStartPoint.isAnchor = true; currentStartPoint.UpdateMaterial(); currentEndPoint.isAnchor = true; currentEndPoint.UpdateMaterial(); }
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
        if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(buildAction);

        currentStartPoint = null;
        currentEndPoint = null;
        currentBar = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;
        
        if (BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.MarkBridgeDirty();
            if (createdNewEndPoint)
                BuildUIController.Instance.LogAction("Point created");
            else
                BuildUIController.Instance.LogAction("Point connected");
        }

        if (activeMaterial != null && activeMaterial.isPier && previousNonPierMaterial != null) SetActiveMaterial(previousNonPierMaterial);
    }

    public void CancelCreation()
    {
        barCreationStarted = false;
        if (currentBar != null) Destroy(currentBar.gameObject);
        if (currentEndPoint != null) Destroy(currentEndPoint.gameObject);
        if (createdStartPoint && currentStartPoint != null) Destroy(currentStartPoint.gameObject);
        createdStartPoint = false;
        currentStartPoint = null;
        currentEndPoint = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    private void DrawRadiusVisual()
    {
        if (radiusIndicator == null || currentStartPoint == null || activeMaterial == null) return;
        radiusIndicator.enabled = true;
        radiusIndicator.useWorldSpace = true;
        
        Vector3 center = currentStartPoint.transform.position;
        float limit = activeMaterial.maxLength;
        if (BuildUIController.Instance != null)
        {
            float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
            float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
            float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
            if (maxAffordable < limit) limit = maxAffordable;
        }

        if (activeMaterial.isPier)
        {
            radiusIndicator.positionCount = 2;
            radiusIndicator.startColor = activeMaterial.gizmoColor;
            radiusIndicator.endColor = activeMaterial.gizmoColor;
            radiusIndicator.startWidth = circleLineWidth * 1.5f; 
            radiusIndicator.endWidth = circleLineWidth * 1.5f;
            
            radiusIndicator.SetPosition(0, center + new Vector3(-200f, limit, 0));
            radiusIndicator.SetPosition(1, center + new Vector3(200f, limit, 0));
        }
        else
        {
            radiusIndicator.positionCount = circleResolution + 1;
            radiusIndicator.startColor = activeMaterial.gizmoColor;
            radiusIndicator.endColor = activeMaterial.gizmoColor;
            radiusIndicator.startWidth = circleLineWidth;
            radiusIndicator.endWidth = circleLineWidth;

            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;
            float angleStep = 360f / circleResolution;
            for (int i = 0; i <= circleResolution; i++)
            {
                float currentAngle = i * angleStep * Mathf.Deg2Rad;
                Vector3 pos = center + (right * Mathf.Cos(currentAngle) * limit) + (up * Mathf.Sin(currentAngle) * limit);
                radiusIndicator.SetPosition(i, pos);
            }
        }
    }

    private void DrawMoveRadius(Vector3 center, float radius, Color color)
    {
        if (radiusIndicator == null) return;
        radiusIndicator.enabled = true;
        radiusIndicator.useWorldSpace = true;
        radiusIndicator.positionCount = circleResolution + 1;
        radiusIndicator.startColor = color;
        radiusIndicator.endColor = color;
        radiusIndicator.startWidth = circleLineWidth;
        radiusIndicator.endWidth = circleLineWidth;
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
}