using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// UI mesh keeps the center transparent and needs no imported sprite or shader.
[RequireComponent(typeof(CanvasRenderer))]
public class AnchorAutoDrawRing : MaskableGraphic
{
    public float Progress;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        AddArc(vh, 1f, new Color(color.r, color.g, color.b, 0.2f));
        AddArc(vh, Mathf.Clamp01(Progress), color);
    }

    private void AddArc(VertexHelper vh, float fraction, Color tint)
    {
        const int resolution = 64;
        float outer = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
        float inner = outer * 0.82f;
        Vector2 center = rectTransform.rect.center;
        int offset = vh.currentVertCount;
        for (int i = 0; i <= resolution; i++)
        {
            float angle = Mathf.PI * 2f * fraction * i / resolution;
            Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            vh.AddVert(center + direction * outer, tint, Vector2.zero);
            vh.AddVert(center + direction * inner, tint, Vector2.zero);
            if (i == 0) continue;
            int v = offset + i * 2;
            vh.AddTriangle(v - 2, v - 1, v);
            vh.AddTriangle(v, v - 1, v + 1);
        }
    }
}

public class BarCreator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler 
{
    public event Action<BridgeMaterialSO> OnActiveMaterialChanged; 

    [Header("References")]
    public Bar currentBar;
    public GameObject barToInstantiate;
    public Transform barParent;
    public Point currentStartPoint;
    public Point currentEndPoint;
    public GameObject pointToInstantiate;
    public Transform pointParent;

    [Header("Build Audio")]
    [Tooltip("SFX ID configured in AudioManager. Played once after a bar placement succeeds.")]
    [SerializeField] private string placeBarSfxId = "PlaceBar";

    [Header("Mobile Build Magnifier")]
    [SerializeField] private MagnifyingGlassController magnifyingGlass;
    [SerializeField] private bool showMagnifierWhileDrawing = true;

    [Header("Node Auto Draw")]
    [Tooltip("Hold on another existing bridge node to confirm auto draw. Supports anchors and regular nodes.")]
    [SerializeField] private bool enableAnchorAutoDraw = true;
    [Tooltip("Delay on the destination node before showing the confirmation ring. Quick drag-and-release places a normal piece.")]
    [SerializeField, Min(0f)] private float autoDrawHoldDelay = 0.45f;
    [Tooltip("Keep holding on the destination node for this duration to confirm auto draw. Moving away resets the ring.")]
    [SerializeField, Min(0.1f)] private float autoDrawLoadingSeconds = 0.65f;
    [SerializeField, Min(0.05f)] private float autoDrawSecondsPerPiece = 0.24f;
    [SerializeField] private Color autoDrawRingColor = new Color(1f, 0.68f, 0.2f);
    public bool IsAutoDrawing => autoDrawRoutine != null;
    private Coroutine autoDrawRoutine;
    private HistoryAction pendingAutoDraw;
    private GameObject autoDrawOverlay;
    private Point autoDrawConfirmationTarget;
    private AnchorAutoDrawRing autoDrawConfirmationRing;
    private float autoDrawConfirmationElapsed;
    private float autoDrawHoverElapsed;

    [Header("3D Material Data")]
    public BridgeMaterialSO activeMaterial;
    private BridgeMaterialSO previousNonPierMaterial;

    [Header("Modes & Settings")]
    public bool isGridSnappingEnabled = true;
    public bool isDeleteMode = false;
    
    [Header("Selection Visuals")]
    [Tooltip("Drag the same highlight material used by Point here!")]
    public Material selectedBarMaterial; 

    [Header("Selection & Move Tools")]
    public bool isSelectMode = false;
    public bool isMoveMode = false; 
    public RectTransform selectionBoxUI; 
    private Vector2 selectionStartPos;
    
    [HideInInspector] public List<Point> selectedPoints = new List<Point>();
    [HideInInspector] public List<Bar> selectedBars = new List<Bar>(); 
    
    public bool isDraggingSelection = false;
    private bool isDraggingSelectionBox = false; 
    private Vector3 dragStartMouseWorld;
    private Vector3 dragLastValidDelta;
    private HistoryAction currentMoveAction;

    [HideInInspector] public bool isSimulating = false; 

    public bool IsCreating => barCreationStarted || IsAutoDrawing;
    public bool IsErasing => isDeleteMode && currentSwipeDeleteAction != null;
    public bool IsSelecting => isSelectMode; 
    public bool IsMoving => isMoveMode; 
    public bool IsPasting => ClipboardManager.Instance != null && ClipboardManager.Instance.isPasteMode; 

    [Header("Pier Settings")]
    public float pierBaseY = -10f; 
    private Bar ghostPierBar; 

    [Header("Snapping Sensitivity")]
    public float deleteSnapRadiusPixels = 50f; 
    [Min(0.01f)]
    public float nodeSnapRadiusWorld = 1.2f;
    [Min(0f)]
    [Tooltip("Prevents nodes from another bridge/build plane being selected when their X/Y positions overlap.")]
    public float nodeSnapDepthTolerance = 1f;

    [Header("Visual Aids")]
    public LineRenderer radiusIndicator; 
    public int circleResolution = 50;    
    public float circleLineWidth = 0.05f;

    private bool barCreationStarted = false;
    private bool createdStartPoint = false; 

    private HistoryAction currentSwipeDeleteAction;

    private Canvas cachedSelectionCanvas;
    private PointerEventData cachedPointerData;
    private List<RaycastResult> cachedRaycastResults = new List<RaycastResult>();
    private HashSet<Bar> cachedAffectedBars = new HashSet<Bar>();
    private List<Point> cachedPointsToProcess = new List<Point>();
    private List<Bar> cachedBarsToTransfer = new List<Bar>();
    private List<Bar> cachedBarsToCollapse = new List<Bar>();

    private void OnEnable() { EnhancedTouchSupport.Enable(); }
    private void OnDisable()
    {
        CancelCreation();
        EnhancedTouchSupport.Disable();
        if (magnifyingGlass != null) magnifyingGlass.HideMagnifier();
    }

    private void Start()
    {
        if (magnifyingGlass == null)
            magnifyingGlass = FindObjectOfType<MagnifyingGlassController>(true);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
        if (pointParent != null) pointParent.gameObject.SetActive(true); 
        
        if (selectionBoxUI != null) 
        {
            cachedSelectionCanvas = selectionBoxUI.GetComponentInParent<Canvas>(true);
            selectionBoxUI.gameObject.SetActive(false); 
        }
        
        if (EventSystem.current != null) cachedPointerData = new PointerEventData(EventSystem.current);
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
        if (UnityEngine.InputSystem.Pointer.current != null) return UnityEngine.InputSystem.Pointer.current.position.ReadValue();
        return Vector2.zero;
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        
        if (cachedPointerData == null) cachedPointerData = new PointerEventData(EventSystem.current);
        cachedPointerData.position = GetPointerPosition();
        
        cachedRaycastResults.Clear();
        EventSystem.current.RaycastAll(cachedPointerData, cachedRaycastResults);

        return cachedRaycastResults.Count > 0 && cachedRaycastResults[0].gameObject != this.gameObject;
    }

    public List<Point> GetSelectedPoints()
    {
        HashSet<Point> allSelected = new HashSet<Point>(selectedPoints);
        foreach(Bar b in selectedBars)
        {
            if (b.startPoint != null) allSelected.Add(b.startPoint);
            if (b.endPoint != null) allSelected.Add(b.endPoint);
        }
        return new List<Point>(allSelected);
    }
    
    public void ClearSelectionPublic()
    {
        ClearSelection();
    }

    private Vector3 GetClosestPointOnLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 lineDir = lineEnd - lineStart;
        float sqrLength = lineDir.sqrMagnitude;
        if (sqrLength == 0) return lineStart;
        float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, lineDir) / sqrLength);
        return lineStart + t * lineDir;
    }

    private Vector3 ClampToEnvironment(Vector3 start, Vector3 end)
    {
        Vector3 dir = (end - start).normalized;
        float dist = Vector3.Distance(start, end);
        int envMask = LayerMask.GetMask("Environment");

        float margin = 1f; 
        
        if (dist <= margin * 2) return end;

        if (Physics.Raycast(start + (dir * margin), dir, out RaycastHit hit, dist - (margin * 2), envMask))
        {
            return hit.point;
        }
        
        return end;
    }

    private void Update()
    {
        if (IsAutoDrawing) return;
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
                        if (b == null || !b.gameObject.activeSelf || b.materialData.isPier || b.startPoint == null || b.endPoint == null) continue;
                        
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
            int envMask = LayerMask.GetMask("Environment"); 

            foreach (Point p in selectedPoints)
            {
                if (p.originalIsAnchor) continue;

                Vector3 proposedPos = currentMoveAction.originalPositions[p] + finalDelta;

                if (Physics.CheckSphere(proposedPos, 0.2f, envMask))
                {
                    isSafe = false;
                    break;
                }

                foreach (Bar b in p.ConnectedBars)
                {
                    if (b == null || !b.gameObject.activeSelf || b.materialData.isPier || b.startPoint == null || b.endPoint == null) continue; 
                    Point otherPoint = (b.startPoint == p) ? b.endPoint : b.startPoint;
                    if (selectedPoints.Contains(otherPoint) && !otherPoint.originalIsAnchor) continue; 
                    
                    if (Vector3.Distance(otherPoint.transform.position, proposedPos) > b.materialData.maxLength + 0.05f) 
                    {
                        isSafe = false;
                        break;
                    }

                    Vector3 dir = (proposedPos - otherPoint.transform.position).normalized;
                    float dist = Vector3.Distance(otherPoint.transform.position, proposedPos);
                    float margin = 0.4f;

                    if (dist > margin * 2 && Physics.Raycast(otherPoint.transform.position + (dir * margin), dir, dist - (margin * 2), envMask))
                    {
                        isSafe = false;
                        break;
                    }
                }
                if (!isSafe) break;
            }

            if (isSafe) dragLastValidDelta = finalDelta;
            else finalDelta = dragLastValidDelta; 

            if (selectedPoints.Count == 1)
            {
                Point movingPoint = selectedPoints[0];
                Vector3 proposedPos = currentMoveAction.originalPositions[movingPoint] + finalDelta;

                foreach (Point p in Point.AllPoints)
                {
                    if (p != movingPoint && p.gameObject.activeSelf)
                    {
                        if (Vector3.Distance(proposedPos, p.transform.position) < 0.3f)
                        {
                            finalDelta = p.transform.position - currentMoveAction.originalPositions[movingPoint];
                            break;
                        }
                    }
                }
            }

            foreach (Point p in selectedPoints)
            {
                if (!p.originalIsAnchor) p.transform.position = currentMoveAction.originalPositions[p] + finalDelta;
            }
            
            foreach (Point p in selectedPoints)
            {
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.materialData.isPier && b.startPoint != null && b.endPoint != null)
                    {
                        Point pBot = b.startPoint.transform.position.y < b.endPoint.transform.position.y ? b.startPoint : b.endPoint;
                        Point pTop = b.startPoint.transform.position.y > b.endPoint.transform.position.y ? b.startPoint : b.endPoint;
                        
                        Vector3 botPos = pBot.transform.position;
                        Vector3 topPos = pTop.transform.position;
                        
                        if (selectedPoints.Contains(pBot) && !selectedPoints.Contains(pTop)) topPos.x = botPos.x;
                        else botPos.x = topPos.x; 
                        
                        botPos.y = pierBaseY; 
                        
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
            CheckForExistingPoint(screenPos, out Point hoveredNode, out Vector3 snapPos);
            if (UpdateAutoDrawConfirmation(hoveredNode)) return;
            Vector3 worldMousePos = GetWorldMousePosition(screenPos);
            
            Vector3 targetPos = CalculateTargetPosition(worldMousePos, hoveredNode, snapPos);

            if (hoveredNode == null && activeMaterial != null && !activeMaterial.isPier)
            {
                float minBarDist = 0.4f;
                foreach (Point p in Point.AllPoints)
                {
                    if (!p.gameObject.activeSelf) continue;
                    foreach (Bar b in p.ConnectedBars)
                    {
                        if (b != null && b.gameObject.activeSelf && b != currentBar && !b.materialData.isPier && b.startPoint != null && b.endPoint != null)
                        {
                            Vector3 closestOnBar = GetClosestPointOnLineSegment(targetPos, b.startPoint.transform.position, b.endPoint.transform.position);
                            float dist = Vector3.Distance(targetPos, closestOnBar);
                            if (dist < minBarDist &&
                                Vector3.Distance(closestOnBar, b.startPoint.transform.position) > 0.2f &&
                                Vector3.Distance(closestOnBar, b.endPoint.transform.position) > 0.2f)
                            {
                                minBarDist = dist;
                                targetPos = closestOnBar;
                            }
                        }
                    }
                }
            }

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

            Vector3 startPos = currentBar.StartPosition;
            
            if (Vector3.Distance(startPos, targetPos) > maxLen)
            {
                Vector3 direction = (targetPos - startPos).normalized;
                targetPos = startPos + (direction * maxLen);

                if (isGridSnappingEnabled && hoveredNode == null)
                {
                    targetPos = SnapToGridFromOrigin(targetPos, startPos);
                    if (activeMaterial != null && activeMaterial.isPier) targetPos.x = startPos.x;
                    if (Vector3.Distance(startPos, targetPos) > maxLen) targetPos = startPos + (direction * maxLen); 
                }
            }

            targetPos = ClampToEnvironment(startPos, targetPos);

            currentEndPoint.transform.position = targetPos;
            currentBar.UpdateCreatingBar(targetPos);

            if (showMagnifierWhileDrawing && magnifyingGlass != null)
                magnifyingGlass.UpdateTrackedPosition(screenPos, targetPos);
        }
    }

    public void DeleteSelected()
    {
        if (selectedPoints.Count == 0 && selectedBars.Count == 0) return;

        HistoryAction deleteAction = new HistoryAction { isBuildEvent = false };
        
        List<Bar> barsToDelete = new List<Bar>(selectedBars);
        foreach (Bar b in barsToDelete) DeleteBar(b, deleteAction);

        cachedPointsToProcess.Clear();
        cachedPointsToProcess.AddRange(selectedPoints);
        foreach (Point p in cachedPointsToProcess) DeletePoint(p, deleteAction);

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

    /// <summary>
    /// Permanently removes the bridge created during the current build attempt while
    /// preserving the Build Location's authored anchors and any already-baked bridge.
    /// This is intentionally not recorded as an Undo action.
    /// </summary>
    public void ClearPlayerPlacedBridge(BuildLocation buildLocation)
    {
        CancelAllModes();

        HashSet<Bar> preservedBars = new HashSet<Bar>();
        HashSet<Point> preservedPoints = new HashSet<Point>();

        // Every completed location shares the same runtime Bar/Point parents.
        // Preserve all baked bridges, not only the location being restarted.
        foreach (BuildLocation location in FindObjectsOfType<BuildLocation>(true))
        {
            if (location == null) continue;

            location.AddProtectedBridgeObjects(preservedBars, preservedPoints);
        }

        Bar[] bars = barParent != null
            ? barParent.GetComponentsInChildren<Bar>(true)
            : FindObjectsOfType<Bar>(true);

        foreach (Bar bar in bars)
        {
            if (bar == null || bar == ghostPierBar || preservedBars.Contains(bar)) continue;
            if (buildLocation != null && bar.OwnerLocation != buildLocation) continue;
            bar.gameObject.SetActive(false);
            Destroy(bar.gameObject);
        }

        Point[] points = pointParent != null
            ? pointParent.GetComponentsInChildren<Point>(true)
            : FindObjectsOfType<Point>(true);

        foreach (Point point in points)
        {
            if (point == null || preservedPoints.Contains(point) || !point.Runtime) continue;
            if (buildLocation != null && point.OwnerLocation != buildLocation) continue;
            point.gameObject.SetActive(false);
            Destroy(point.gameObject);
        }

        foreach (Point point in preservedPoints)
        {
            if (point == null) continue;
            point.ConnectedBars.RemoveAll(bar => bar == null || !bar.gameObject.activeSelf);
            if (point.gameObject.activeInHierarchy) point.EvaluateAnchorState();
        }

        currentBar = null;
        currentStartPoint = null;
        currentEndPoint = null;
        ClearSelection();

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.SetSelectionPanelActive(false);
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Unfinished bridge cleared; completed bridges preserved");
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
        if (IsAutoDrawing) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (isSimulating || Touch.activeTouches.Count > 1) return;
        if (BuildTutorialDirector.Instance != null && !BuildTutorialDirector.Instance.CanPlaceMaterials) return;

        if (eventData.button != PointerEventData.InputButton.Left) return;

        Vector2 screenPos = eventData.position;

        if (IsPasting && eventData.button == PointerEventData.InputButton.Left)
        {
            ClipboardManager.Instance.HandlePointerDown(eventData);
            return;
        }

        if (isMoveMode && eventData.button == PointerEventData.InputButton.Left)
        {
            CheckForExistingPoint(screenPos, out Point hoveredNode, out _);
            if (hoveredNode != null)
            {
                if (!selectedPoints.Contains(hoveredNode))
                {
                    ClearSelection();
                    hoveredNode.isSelected = true;
                    hoveredNode.UpdateMaterial();
                    selectedPoints.Add(hoveredNode);
                    
                    UpdateBarHighlights(); 
                }
                else
                {
                    selectedPoints.Remove(hoveredNode);
                    selectedPoints.Insert(0, hoveredNode);
                }

                isDraggingSelection = true;
                dragStartMouseWorld = GetWorldMousePosition(screenPos);
                dragLastValidDelta = Vector3.zero; 
                
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

        CheckForExistingPoint(screenPos, out Point existingNode, out Vector3 exactSnapPos);
        
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
                Tutorial3DIndicator.NotifyAnchorClicked(existingNode.transform);
                StartBarCreation(exactSnapPos);
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
                TutorialDragSelectionAnim.NotifySelectionDragStarted();
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
        if (IsAutoDrawing) return;
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
                
                bool didMerge = false;

                if (selectedPoints.Count == 1)
                {
                    Point movedPoint = selectedPoints[0];
                    Point targetPoint = null;

                    foreach (Point p in Point.AllPoints)
                    {
                        if (p != movedPoint && p.gameObject.activeSelf)
                        {
                            if (Vector3.Distance(movedPoint.transform.position, p.transform.position) < 0.1f)
                            {
                                targetPoint = p;
                                break;
                            }
                        }
                    }

                    if (targetPoint != null)
                    {
                        didMerge = true;
                        PerformMerge(movedPoint, targetPoint, currentMoveAction.originalPositions[movedPoint]);
                    }
                }

                if (!didMerge && currentMoveAction != null)
                {
                    foreach (Point p in Point.AllPoints) 
                    {
                        if (p.gameObject.activeSelf) currentMoveAction.newPositions[p] = p.transform.position;
                    }

                    if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(currentMoveAction);
                    if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Selection Moved");
                }

                currentMoveAction = null;
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
                CheckForExistingPoint(eventData.position, out Point tappedPoint, out _);
                if (tappedPoint != null) TogglePointSelection(tappedPoint);
                else
                {
                    Bar tappedBar = CheckForExistingBar(eventData.position);
                    if (tappedBar != null) ToggleBarSelection(tappedBar);
                    else ClearSelection(); 
                }

                if (BuildUIController.Instance != null && (selectedPoints.Count > 0 || selectedBars.Count > 0))
                {
                    BuildUIController.Instance.SetSelectionPanelActive(true);
                }
            }

            TutorialDragSelectionAnim.NotifySelectionChanged(this);
            if (BuildTutorialDirector.Instance != null)
                BuildTutorialDirector.Instance.NotifySelectionChanged(this);

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
            Vector2 screenPos = eventData.position;
            CheckForExistingPoint(screenPos, out Point hoveredNode, out Vector3 exactSnapPos);
            Vector3 worldPos = GetWorldMousePosition(screenPos);
            
            // Only a completed hold starts auto draw. An ordinary release still places one piece.
            ResetAutoDrawConfirmation();
            FinishBarCreation(worldPos, hoveredNode, exactSnapPos);
        }
        else
        {
            CancelCreation(); 
        }
    }

    private void PerformMerge(Point movedPoint, Point targetPoint, Vector3 originalPos)
    {
        HistoryAction mergeAction = new HistoryAction { isMergeEvent = true };
        
        mergeAction.originalPositions[movedPoint] = originalPos; 
        mergeAction.newPositions[movedPoint] = targetPoint.transform.position;
        mergeAction.affectedObjects.Add(movedPoint.gameObject); 

        cachedBarsToTransfer.Clear();
        cachedBarsToCollapse.Clear();
        cachedBarsToTransfer.AddRange(movedPoint.ConnectedBars);

        foreach (Bar b in cachedBarsToTransfer)
        {
            bool wasStart = (b.startPoint == movedPoint);
            bool wasEnd = (b.endPoint == movedPoint);

            if (wasStart)
            {
                mergeAction.originalStartPoints[b] = movedPoint;
                mergeAction.mergedStartPoints[b] = targetPoint;
                b.startPoint = targetPoint;
            }
            if (wasEnd)
            {
                mergeAction.originalEndPoints[b] = movedPoint;
                mergeAction.mergedEndPoints[b] = targetPoint;
                b.endPoint = targetPoint;
            }

            movedPoint.ConnectedBars.Remove(b);
            if (!targetPoint.ConnectedBars.Contains(b)) targetPoint.ConnectedBars.Add(b);

            if (b.startPoint == b.endPoint)
            {
                cachedBarsToCollapse.Add(b);
            }
            else
            {
                b.StartPosition = b.startPoint.transform.position;
                b.UpdateCreatingBar(b.endPoint.transform.position);
            }
        }

        foreach (Bar b in cachedBarsToCollapse)
        {
            mergeAction.affectedObjects.Add(b.gameObject);
            targetPoint.ConnectedBars.Remove(b);
            b.gameObject.SetActive(false);
        }

        movedPoint.gameObject.SetActive(false);
        targetPoint.EvaluateAnchorState();
        ClearSelection();

        if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(mergeAction);

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Nodes Merged");
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
        UpdateBarHighlights(); 
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction(p.isSelected ? "Node Selected" : "Node Deselected");
    }

    private void ToggleBarSelection(Bar bar)
    {
        if (selectedBars.Contains(bar))
        {
            selectedBars.Remove(bar);
        }
        else
        {
            selectedBars.Add(bar);
        }

        UpdateBarHighlights(); 
        
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Beam Selected");
    }

    public void UpdateBarHighlights()
    {
        foreach (Point p in Point.AllPoints) 
        {
            foreach (Bar b in p.ConnectedBars) 
            {
                if (b != null) b.SetHighlight(false, selectedBarMaterial);
            }
        }

        if (selectedPoints.Count == 1 && selectedBars.Count == 0) 
        {
            foreach (Bar b in selectedPoints[0].ConnectedBars) 
            {
                if (b != null && b.gameObject.activeSelf) b.SetHighlight(true, selectedBarMaterial);
            }
        } 
        else if (selectedPoints.Count > 1 || selectedBars.Count > 0) 
        {
            foreach (Bar b in selectedBars)
            {
                if (b != null && b.gameObject.activeSelf) b.SetHighlight(true, selectedBarMaterial);
            }

            foreach (Point p in selectedPoints) 
            {
                foreach (Bar b in p.ConnectedBars) 
                {
                    if (b != null && b.gameObject.activeSelf && b.startPoint != null && b.endPoint != null &&
                        selectedPoints.Contains(b.startPoint) && selectedPoints.Contains(b.endPoint)) 
                    {
                        b.SetHighlight(true, selectedBarMaterial);
                    }
                }
            }
        }
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

        selectedBars.Clear();

        UpdateBarHighlights(); 

        if (BuildUIController.Instance != null) BuildUIController.Instance.SetSelectionPanelActive(false);

        if (BuildTutorialDirector.Instance != null)
            BuildTutorialDirector.Instance.NotifySelectionChanged(this);
    }

    private void UpdateSelectionBox(Vector2 currentScreenPos)
    {
        if (selectionBoxUI == null || selectionBoxUI.parent == null) return;
        RectTransform parentRect = selectionBoxUI.parent as RectTransform;
        Camera uiCam = GetActiveCamera();
        
        if (cachedSelectionCanvas != null && cachedSelectionCanvas.renderMode == RenderMode.ScreenSpaceOverlay) uiCam = null;

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

        HashSet<Bar> allActiveBars = new HashSet<Bar>();

        foreach (Point p in Point.AllPoints)
        {
            if (p.gameObject.activeSelf) 
            {
                Vector2 screenPos = cam.WorldToScreenPoint(p.transform.position);
                if (selectionRect.Contains(screenPos)) 
                { 
                    p.isSelected = true; 
                    p.UpdateMaterial(); 
                    selectedPoints.Add(p); 
                }
                
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf) allActiveBars.Add(b);
                }
            }
        }

        foreach (Bar b in allActiveBars)
        {
            if (b.startPoint == null || b.endPoint == null) continue;
            
            Vector2 s1 = cam.WorldToScreenPoint(b.startPoint.transform.position);
            Vector2 s2 = cam.WorldToScreenPoint(b.endPoint.transform.position);
            Vector2 mid = (s1 + s2) / 2f;

            if (selectionRect.Contains(mid) || (selectionRect.Contains(s1) && selectionRect.Contains(s2)))
            {
                selectedBars.Add(b);
            }
        }

        UpdateBarHighlights(); 

        if ((selectedPoints.Count > 0 || selectedBars.Count > 0) && BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.SetSelectionPanelActive(true);
            BuildUIController.Instance.LogAction("Box Selection Applied");
        }
    }

    // --- THE FIX: We rigidly enforce that all bars lock exactly to their node's transform! ---
    private void UpdateBarsForSelectedPoints()
    {
        cachedAffectedBars.Clear();
        foreach (Point p in selectedPoints) 
        {
            foreach (Bar b in p.ConnectedBars) 
            {
                if (b != null && b.gameObject.activeSelf && b.startPoint != null && b.endPoint != null) 
                {
                    cachedAffectedBars.Add(b);
                    if (b.materialData.isPier)
                    {
                        foreach(Bar botBar in b.startPoint.ConnectedBars) if (botBar.gameObject.activeSelf) cachedAffectedBars.Add(botBar);
                        foreach(Bar topBar in b.endPoint.ConnectedBars) if (topBar.gameObject.activeSelf) cachedAffectedBars.Add(topBar);
                    }
                }
            }
        }
        foreach (Bar b in cachedAffectedBars) 
        {
            if (b.startPoint != null && b.endPoint != null) 
            {
                // ALWAYS snap to the exact mathematical center! No sliding!
                b.StartPosition = b.startPoint.transform.position; 
                b.EndPosition = b.endPoint.transform.position; 

                b.UpdateCreatingBar(b.EndPosition); 
            }
        }
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    private void PerformSwipeDelete(Vector2 screenPos)
    {
        CheckForExistingPoint(screenPos, out Point hoveredPoint, out _);
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
        
        cachedBarsToTransfer.Clear();
        cachedBarsToTransfer.AddRange(p.ConnectedBars);
        
        foreach (Bar b in cachedBarsToTransfer) DeleteBar(b, currentAction);
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
        
        if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
        {
            GameManager.Instance.ActiveBuildLocation.SetGridVisualActive(isGridSnappingEnabled);
        }

        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Grid Snapping: " + (isGridSnappingEnabled ? "ON" : "OFF"));
    }

    // --- THE FIX: We added "Straight Line Assist" to automatically enforce perfectly flat roads! ---
    private Vector3 CalculateTargetPosition(Vector3 rawPos, Point hoveredNode, Vector3 snapPos)
    {
        if (hoveredNode != null) return snapPos;
        float lockedZ = currentStartPoint != null ? currentStartPoint.transform.position.z : 0f;
        
        Vector3 result = new Vector3(rawPos.x, rawPos.y, lockedZ);
        
        if (isGridSnappingEnabled)
        {
            // Build locations are not guaranteed to sit on whole-number world
            // coordinates. Snapping to the global origin shortened the first
            // 10 m segment at those sites (for example 232.4727 -> 242.0), so
            // five roads could not cover a measured 50 m span. Use the current
            // node as the grid origin to preserve exact material lengths.
            Vector3 gridOrigin = currentStartPoint != null
                ? currentStartPoint.transform.position
                : Vector3.zero;
            result = SnapToGridFromOrigin(result, gridOrigin);
        }

        // Horizontal & Vertical Straight-Line Assist!
        if (currentStartPoint != null)
        {
            float yDiff = Mathf.Abs(result.y - currentStartPoint.transform.position.y);
            float xDiff = Mathf.Abs(result.x - currentStartPoint.transform.position.x);

            // Force perfectly straight lines if dragging roughly horizontal or vertical
            if (yDiff <= 0.6f && xDiff > yDiff) 
                result.y = currentStartPoint.transform.position.y;
            else if (xDiff <= 0.6f && yDiff > xDiff) 
                result.x = currentStartPoint.transform.position.x;
        }

        return result;
    }

    private static Vector3 SnapToGridFromOrigin(Vector3 position, Vector3 origin)
    {
        return new Vector3(
            origin.x + Mathf.Round(position.x - origin.x),
            origin.y + Mathf.Round(position.y - origin.y),
            position.z);
    }

    // Finds the nearest node center on the active bridge plane. This is independent
    // of drag direction and does not depend on optional point colliders or list order.
    private bool CheckForExistingPoint(Vector2 screenPos, out Point closestPoint, out Vector3 snapPosition)
    {
        closestPoint = null;
        snapPosition = Vector3.zero;

        Vector3 flatMousePos = GetWorldMousePosition(screenPos);
        float closestSqrDistance = nodeSnapRadiusWorld * nodeSnapRadiusWorld;

        foreach (Point p in Point.AllPoints)
        {
            if (p == null
                || p == currentStartPoint
                || p == currentEndPoint
                || !p.gameObject.activeInHierarchy)
                continue;

            Vector3 nodePosition = p.transform.position;
            if (Mathf.Abs(nodePosition.z - flatMousePos.z) > nodeSnapDepthTolerance)
                continue;

            Vector2 offset = new Vector2(
                nodePosition.x - flatMousePos.x,
                nodePosition.y - flatMousePos.y);
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance > closestSqrDistance)
                continue;

            closestSqrDistance = sqrDistance;
            closestPoint = p;
            snapPosition = nodePosition;
        }

        return closestPoint != null;
    }

    public Vector3 GetWorldMousePosition(Vector2 screenPos)
    {
        Camera cam = GetActiveCamera();
        
        // Lock the building to the exact Z depth of the bridge
        float bridgeZ = 0f;
        if (currentStartPoint != null) bridgeZ = currentStartPoint.transform.position.z;
        else if (Point.AllPoints.Count > 0) bridgeZ = Point.AllPoints[0].transform.position.z;

        // A perfectly flat 2D plane facing the Z-axis, regardless of camera tilt
        Plane flatWorldPlane = new Plane(Vector3.back, new Vector3(0, 0, bridgeZ));
        Ray ray = cam.ScreenPointToRay(screenPos);
        
        if (flatWorldPlane.Raycast(ray, out float distance)) 
        {
            return ray.GetPoint(distance);
        }
        
        return currentStartPoint != null ? currentStartPoint.transform.position : Vector3.zero;
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

        if (showMagnifierWhileDrawing && magnifyingGlass != null)
            magnifyingGlass.ShowMagnifier(GetPointerPosition(), startPosition);
    }

    private void FinishBarCreation(Vector3 rawWorldPos, Point existingEndPoint, Vector3 exactSnapPos)
    {
        if (currentBar == null || currentStartPoint == null || currentEndPoint == null) 
        {
            CancelCreation();
            return;
        }

        Vector3 finalPosition = CalculateTargetPosition(rawWorldPos, existingEndPoint, exactSnapPos);
        Vector3 startPos = currentBar.StartPosition;

        BuildTutorialDirector tutorialDirector = BuildTutorialDirector.Instance;
        if (tutorialDirector != null && tutorialDirector.isTracingStep)
        {
            Vector3 snappedPosition;
            if (tutorialDirector.TryGetSnappedEndPosition(activeMaterial, startPos, finalPosition, out snappedPosition))
            {
                finalPosition = snappedPosition;
                if (existingEndPoint != null && Vector3.Distance(existingEndPoint.transform.position, finalPosition) > 0.01f)
                    existingEndPoint = null;
            }
        }
        
        if (activeMaterial != null && activeMaterial.isPier) finalPosition.x = currentStartPoint.transform.position.x;
        float limit = activeMaterial != null ? activeMaterial.maxLength : 5f;
        
        if (BuildUIController.Instance != null && activeMaterial != null)
        {
            float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
            float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
            float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
            if (maxAffordable < limit) limit = maxAffordable;
        }

        float distanceToTarget = Vector3.Distance(startPos, finalPosition);
        if (existingEndPoint != null && distanceToTarget > limit && distanceToTarget <= limit + 0.2f) limit = distanceToTarget; 
        
        if (Vector3.Distance(startPos, finalPosition) > limit)
        {
            Vector3 direction = (finalPosition - startPos).normalized;
            finalPosition = startPos + (direction * limit);
            if (isGridSnappingEnabled && existingEndPoint == null)
            {
                finalPosition = SnapToGridFromOrigin(finalPosition, startPos);
                if (activeMaterial != null && activeMaterial.isPier) finalPosition.x = startPos.x;
                if (Vector3.Distance(startPos, finalPosition) > limit) finalPosition = startPos + (direction * limit); 
            }
            existingEndPoint = null; 
        }

        Vector3 preClampPos = finalPosition;
        finalPosition = ClampToEnvironment(startPos, finalPosition);

        if (Vector3.Distance(preClampPos, finalPosition) > 0.05f && BuildUIController.Instance != null)
        {
            BuildUIController.Instance.LogAction("Beam stopped by terrain");
        }

        if (existingEndPoint == null)
        {
            Vector2 screenPos = GetPointerPosition();
            if (CheckForExistingPoint(screenPos, out Point secondCheckNode, out Vector3 secondCheckSnapPos))
            {
                if (Vector3.Distance(startPos, secondCheckSnapPos) <= limit + 0.05f)
                {
                    existingEndPoint = secondCheckNode;
                    finalPosition = secondCheckSnapPos;
                }
            }
        }
        
        Bar barToSplit = null;
        if (existingEndPoint == null && activeMaterial != null && !activeMaterial.isPier)
        {
            float minBarDist = 0.5f;
            foreach (Point p in Point.AllPoints)
            {
                if (!p.gameObject.activeSelf) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b != currentBar && !b.materialData.isPier && b.startPoint != null && b.endPoint != null)
                    {
                        Vector3 closestOnBar = GetClosestPointOnLineSegment(finalPosition, b.startPoint.transform.position, b.endPoint.transform.position);
                        float dist = Vector3.Distance(finalPosition, closestOnBar);
                        if (dist < minBarDist &&
                            Vector3.Distance(closestOnBar, b.startPoint.transform.position) > 0.2f &&
                            Vector3.Distance(closestOnBar, b.endPoint.transform.position) > 0.2f)
                        {
                            minBarDist = dist;
                            barToSplit = b;
                            finalPosition = closestOnBar;
                        }
                    }
                }
            }
        }

        if (Vector3.Distance(startPos, finalPosition) < 0.1f) 
        { 
            CancelCreation(); 
            if (Vector3.Distance(preClampPos, finalPosition) <= 0.05f && BuildUIController.Instance != null) 
            {
                BuildUIController.Instance.LogAction("Drawing Canceled");
            }
            return; 
        }

        bool createdNewEndPoint = (existingEndPoint == null);
        bool createdNewStartPoint = createdStartPoint;
        
        if (existingEndPoint != null) { Destroy(currentEndPoint.gameObject); currentEndPoint = existingEndPoint; }
        else { currentEndPoint.name = "Point"; currentEndPoint.transform.position = finalPosition; }

        if (activeMaterial != null && activeMaterial.isPier)
        {
            // Pier placement always starts at the configured foundation height.
            // Only that foot is terrain-fixed; the cap remains a normal dynamic
            // bridge node so the pier can carry load and eventually buckle.
            currentStartPoint.originalIsAnchor = true;
            currentStartPoint.isAnchor = true;
            currentStartPoint.UpdateMaterial();

            currentEndPoint.isAnchor = currentEndPoint.originalIsAnchor;
            currentEndPoint.UpdateMaterial();
        }
        
        currentBar.startPoint = currentStartPoint;
        currentBar.endPoint = currentEndPoint;
        currentBar.NormalizeEndpointOrder();
        
        if (!currentStartPoint.ConnectedBars.Contains(currentBar)) currentStartPoint.ConnectedBars.Add(currentBar);
        if (!currentEndPoint.ConnectedBars.Contains(currentBar)) currentEndPoint.ConnectedBars.Add(currentBar);
        currentStartPoint.EvaluateAnchorState();
        currentEndPoint.EvaluateAnchorState();

        HistoryAction buildAction = new HistoryAction { isBuildEvent = true };
        buildAction.affectedObjects.Add(currentBar.gameObject);
        if (createdNewStartPoint) buildAction.affectedObjects.Add(currentStartPoint.gameObject); 
        if (createdNewEndPoint) buildAction.affectedObjects.Add(currentEndPoint.gameObject);

        if (barToSplit != null)
        {
            Point originalStart = barToSplit.startPoint;
            Point originalEnd = barToSplit.endPoint;

            barToSplit.gameObject.SetActive(false);
            buildAction.disabledObjects.Add(barToSplit.gameObject);

            originalStart.ConnectedBars.Remove(barToSplit);
            originalEnd.ConnectedBars.Remove(barToSplit);

            bool startsAtOriginalStart = (currentStartPoint == originalStart);
            bool startsAtOriginalEnd = (currentStartPoint == originalEnd);

            if (startsAtOriginalStart)
            {
                GameObject bObj = Instantiate(barToInstantiate, barParent);
                Bar newBar = bObj.GetComponent<Bar>();
                newBar.Initialize(barToSplit.materialData);
                newBar.StartPosition = finalPosition;
                newBar.EndPosition = originalEnd.transform.position; 
                newBar.UpdateCreatingBar(originalEnd.transform.position);
                newBar.startPoint = currentEndPoint;
                newBar.endPoint = originalEnd;
                currentEndPoint.ConnectedBars.Add(newBar);
                originalEnd.ConnectedBars.Add(newBar);
                
                buildAction.affectedObjects.Add(bObj);
            }
            else if (startsAtOriginalEnd)
            {
                GameObject bObj = Instantiate(barToInstantiate, barParent);
                Bar newBar = bObj.GetComponent<Bar>();
                newBar.Initialize(barToSplit.materialData);
                newBar.StartPosition = finalPosition;
                newBar.EndPosition = originalStart.transform.position; 
                newBar.UpdateCreatingBar(originalStart.transform.position);
                newBar.startPoint = currentEndPoint;
                newBar.endPoint = originalStart;
                currentEndPoint.ConnectedBars.Add(newBar);
                originalStart.ConnectedBars.Add(newBar);
                
                buildAction.affectedObjects.Add(bObj);
            }
            else
            {
                GameObject bObj1 = Instantiate(barToInstantiate, barParent);
                Bar newBar1 = bObj1.GetComponent<Bar>();
                newBar1.Initialize(barToSplit.materialData);
                newBar1.StartPosition = originalStart.transform.position;
                newBar1.EndPosition = finalPosition; 
                newBar1.UpdateCreatingBar(finalPosition);
                newBar1.startPoint = originalStart;
                newBar1.endPoint = currentEndPoint;
                originalStart.ConnectedBars.Add(newBar1);
                currentEndPoint.ConnectedBars.Add(newBar1);

                GameObject bObj2 = Instantiate(barToInstantiate, barParent);
                Bar newBar2 = bObj2.GetComponent<Bar>();
                newBar2.Initialize(barToSplit.materialData);
                newBar2.StartPosition = finalPosition;
                newBar2.EndPosition = originalEnd.transform.position; 
                newBar2.UpdateCreatingBar(originalEnd.transform.position);
                newBar2.startPoint = currentEndPoint;
                newBar2.endPoint = originalEnd;
                currentEndPoint.ConnectedBars.Add(newBar2);
                originalEnd.ConnectedBars.Add(newBar2);

                buildAction.affectedObjects.Add(bObj1);
                buildAction.affectedObjects.Add(bObj2);
            }
            
            if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Beam Sliced");
        }
        else if (BuildUIController.Instance != null && Vector3.Distance(preClampPos, finalPosition) <= 0.05f) 
        {
            if (createdNewEndPoint) BuildUIController.Instance.LogAction("Point created");
            else BuildUIController.Instance.LogAction("Point connected");
        }

        if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(buildAction);

        if (AudioManager.Instance != null && !string.IsNullOrWhiteSpace(placeBarSfxId))
            AudioManager.Instance.PlaySFX(placeBarSfxId);

        if (magnifyingGlass != null) magnifyingGlass.HideMagnifier();

        barCreationStarted = false;
        createdStartPoint = false; 
        currentStartPoint = null;
        currentEndPoint = null;
        currentBar = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;
        
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
        if (activeMaterial != null && activeMaterial.isPier && previousNonPierMaterial != null) SetActiveMaterial(previousNonPierMaterial);
        if (tutorialDirector != null) tutorialDirector.OnBuildActionCompleted(buildAction);
    }

    public void CancelCreation()
    {
        CancelAutoDraw();
        if (magnifyingGlass != null) magnifyingGlass.HideMagnifier();

        barCreationStarted = false;
        if (currentBar != null) Destroy(currentBar.gameObject);
        if (currentEndPoint != null) Destroy(currentEndPoint.gameObject);
        if (createdStartPoint && currentStartPoint != null) Destroy(currentStartPoint.gameObject);
        createdStartPoint = false;
        currentBar = null;
        currentStartPoint = null;
        currentEndPoint = null;
        if (radiusIndicator != null) radiusIndicator.enabled = false;
        if (BuildUIController.Instance != null) BuildUIController.Instance.MarkBridgeDirty();
    }

    private bool IsAutoDrawNodePair(Point destination)
    {
        return enableAnchorAutoDraw && activeMaterial != null && !activeMaterial.isPier &&
            currentStartPoint != null && destination != null && destination != currentStartPoint &&
            destination != currentEndPoint && currentStartPoint.gameObject.activeInHierarchy &&
            destination.gameObject.activeInHierarchy;
    }

    private bool UpdateAutoDrawConfirmation(Point destination)
    {
        bool held = Touch.activeTouches.Count > 0
            ? Touch.activeTouches[0].phase != UnityEngine.InputSystem.TouchPhase.Ended &&
              Touch.activeTouches[0].phase != UnityEngine.InputSystem.TouchPhase.Canceled
            : UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
        if (!held || !IsAutoDrawNodePair(destination))
        {
            ResetAutoDrawConfirmation();
            return false;
        }
        if (autoDrawConfirmationTarget != destination)
        {
            ResetAutoDrawConfirmation();
            autoDrawConfirmationTarget = destination;
        }
        autoDrawHoverElapsed += Time.unscaledDeltaTime;
        if (autoDrawHoverElapsed < Mathf.Max(0f, autoDrawHoldDelay)) return false;
        if (autoDrawConfirmationRing == null)
        {
            autoDrawConfirmationRing = CreateAutoDrawOverlay(false);
            if (magnifyingGlass != null) magnifyingGlass.HideMagnifier();
        }
        // Let the fully filled ring render for a frame before starting construction.
        bool confirmed = autoDrawConfirmationElapsed >= Mathf.Max(0.1f, autoDrawLoadingSeconds);
        autoDrawConfirmationElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(autoDrawConfirmationElapsed / Mathf.Max(0.1f, autoDrawLoadingSeconds));
        RectTransform ringRect = autoDrawConfirmationRing.rectTransform;
        PlaceAutoDrawRing(ringRect, destination.transform.position);
        autoDrawConfirmationRing.Progress = progress;
        ringRect.localRotation = Quaternion.Euler(0f, 0f, -autoDrawConfirmationElapsed * 180f);
        ringRect.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(autoDrawConfirmationElapsed * 9f));
        autoDrawConfirmationRing.SetVerticesDirty();
        if (confirmed) return TryStartNodeAutoDraw(destination);
        return false;
    }

    private void ResetAutoDrawConfirmation()
    {
        autoDrawConfirmationTarget = null;
        autoDrawConfirmationElapsed = 0f;
        autoDrawHoverElapsed = 0f;
        autoDrawConfirmationRing = null;
        if (!IsAutoDrawing && autoDrawOverlay != null)
        {
            autoDrawOverlay.SetActive(false);
            Destroy(autoDrawOverlay);
            autoDrawOverlay = null;
        }
    }

    // Plan the entire gesture first; no partial bridge is committed if it is canceled.
    private bool TryStartNodeAutoDraw(Point destination)
    {
        if (!IsAutoDrawNodePair(destination)) return false;

        Point origin = currentStartPoint;
        BridgeMaterialSO material = activeMaterial;
        Vector3 start = origin.transform.position;
        Vector3 end = destination.transform.position;
        var location = GameManager.Instance != null ? GameManager.Instance.ActiveBuildLocation : null;
        string error = null;
        float length = Vector3.Distance(start, end);
        if (length < 0.1f || material.maxLength < 0.1f ||
            Mathf.Abs(start.z - end.z) > nodeSnapDepthTolerance ||
            (origin.OwnerLocation != null && origin.OwnerLocation != location) ||
            (destination.OwnerLocation != null && destination.OwnerLocation != location))
            error = "Auto draw needs two nodes on the current bridge plane.";

        var positions = new List<Vector3> { start };
        var nodes = new List<Point> { origin };
        // Preserve existing nodes on the span so the result stays connected to supports.
        var stops = new List<Point>();
        foreach (Point p in Point.AllPoints)
        {
            if (p == null || p == origin || p == destination || p == currentEndPoint ||
                !p.gameObject.activeInHierarchy ||
                (p.OwnerLocation != null && p.OwnerLocation != location)) continue;
            Vector3 projected = GetClosestPointOnLineSegment(p.transform.position, start, end);
            if (Vector3.Distance(projected, p.transform.position) < 0.01f &&
                Vector3.Distance(projected, start) > 0.05f && Vector3.Distance(projected, end) > 0.05f)
                stops.Add(p);
        }
        stops.Sort((a, b) => (a.transform.position - start).sqrMagnitude.CompareTo((b.transform.position - start).sqrMagnitude));
        stops.Add(destination);
        foreach (Point stop in stops)
        {
            Vector3 from = positions[positions.Count - 1];
            float distance = Vector3.Distance(from, stop.transform.position);
            int count = Mathf.CeilToInt(distance / Mathf.Max(0.1f, material.maxLength));
            if (positions.Count + count > 129) { error = "Auto draw is limited to 128 pieces per gesture."; break; }
            for (int i = 1; i <= count; i++)
            {
                positions.Add(Vector3.Lerp(from, stop.transform.position, (float)i / count));
                nodes.Add(i == count ? stop : null);
            }
        }

        // Reject overlap, including an existing long beam spanning multiple planned pieces.
        var checkedBars = new HashSet<Bar>();
        foreach (Point p in Point.AllPoints)
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            foreach (Bar b in p.ConnectedBars)
            {
                if (b == null || b == currentBar || !b.gameObject.activeInHierarchy ||
                    b.startPoint == null || b.endPoint == null || !checkedBars.Add(b)) continue;
                Vector3 a = b.startPoint.transform.position;
                Vector3 z = b.endPoint.transform.position;
                Vector3 axis = (end - start).normalized;
                float t0 = Vector3.Dot(a - start, axis);
                float t1 = Vector3.Dot(z - start, axis);
                if (Vector3.Distance(a, start + axis * t0) < 0.02f &&
                    Vector3.Distance(z, start + axis * t1) < 0.02f &&
                    Mathf.Min(length, Mathf.Max(t0, t1)) - Mathf.Max(0f, Mathf.Min(t0, t1)) > 0.05f)
                    error = "This span already contains material. Remove it before auto drawing.";
            }
        }
        for (int i = 1; i < positions.Count; i++)
            if (Vector3.Distance(ClampToEnvironment(positions[i - 1], positions[i]), positions[i]) > 0.05f)
                error = "Auto draw is blocked by terrain.";

        float cost = length * material.costPerMeter * (material.isDualBeam ? 2f : 1f);
        if (BuildUIController.Instance != null &&
            cost > BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost() + 0.01f)
            error = "Not enough bridge budget for this span.";
        var director = BuildTutorialDirector.Instance;
        if (director != null && !director.CanAutoDrawSegments(material, positions))
            error = "Follow the tutorial's individual guide segments for this span.";

        CancelCreation();
        if (error != null)
        {
            if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction(error);
            return true;
        }
        pendingAutoDraw = new HistoryAction { isBuildEvent = true };
        autoDrawRoutine = StartCoroutine(AnimateAnchorSpan(material, positions, nodes, location));
        return true;
    }

    private AnchorAutoDrawRing CreateAutoDrawOverlay(bool blockInput)
    {
        autoDrawOverlay = new GameObject("Auto Draw Loading", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        // Overlay canvases must not inherit the build world's position, scale or masks.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(autoDrawOverlay, gameObject.scene);
        Canvas canvas = autoDrawOverlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 30000;
        Camera buildCamera = GetActiveCamera();
        if (buildCamera != null) canvas.targetDisplay = buildCamera.targetDisplay;
        CanvasScaler scaler = autoDrawOverlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // Prevent material changes and simulation commands during the short transaction.
        var blocker = new GameObject("Input Shield", typeof(RectTransform), typeof(Image));
        blocker.transform.SetParent(autoDrawOverlay.transform, false);
        var shieldRect = (RectTransform)blocker.transform;
        shieldRect.anchorMin = Vector2.zero; shieldRect.anchorMax = Vector2.one;
        shieldRect.offsetMin = shieldRect.offsetMax = Vector2.zero;
        blocker.GetComponent<Image>().color = Color.clear;
        blocker.GetComponent<Image>().raycastTarget = blockInput;
        // Construction only needs the invisible input shield; the ring is for confirmation.
        if (blockInput) return null;
        var ringObject = new GameObject("Hollow Loading Ring", typeof(RectTransform), typeof(CanvasRenderer));
        ringObject.transform.SetParent(autoDrawOverlay.transform, false);
        var ringRect = (RectTransform)ringObject.transform;
        ringRect.sizeDelta = new Vector2(72f, 72f);
        var ring = ringObject.AddComponent<AnchorAutoDrawRing>();
        ring.color = autoDrawRingColor;
        ring.maskable = false;
        ring.raycastTarget = false;
        Canvas.ForceUpdateCanvases();
        return ring;
    }

    private IEnumerator AnimateAnchorSpan(BridgeMaterialSO material, List<Vector3> positions, List<Point> nodes, BuildLocation location)
    {
        CreateAutoDrawOverlay(true);
        // Confirmation is already complete. Start growing the material immediately.
        float elapsed;
        for (int i = 1; i < positions.Count; i++)
        {
            if (!CanContinueAutoDraw(material, nodes, location)) { CancelCreation(); yield break; }
            if (nodes[i] == null)
            {
                GameObject pointObject = Instantiate(pointToInstantiate, positions[i], Quaternion.identity, pointParent);
                pointObject.name = "Point";
                pendingAutoDraw.affectedObjects.Add(pointObject);
                nodes[i] = pointObject.GetComponent<Point>();
                nodes[i].Runtime = true;
                nodes[i].originalIsAnchor = nodes[i].isAnchor = false;
                nodes[i].AssignOwner(location);
                pointObject.transform.localScale = Vector3.zero;
            }
            GameObject barObject = Instantiate(barToInstantiate, barParent);
            barObject.name = "Bar";
            pendingAutoDraw.affectedObjects.Add(barObject);
            Bar bar = barObject.GetComponent<Bar>();
            bar.AssignOwner(location);
            bar.Initialize(material);
            bar.StartPosition = positions[i - 1];
            bar.UpdateCreatingBar(positions[i - 1]);
            elapsed = 0f;
            float duration = Mathf.Max(0.05f, autoDrawSecondsPerPiece);
            while (elapsed < duration)
            {
                if (!CanContinueAutoDraw(material, nodes, location)) { CancelCreation(); yield break; }
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                Vector3 tip = Vector3.Lerp(positions[i - 1], positions[i], t);
                bar.UpdateCreatingBar(tip);
                yield return null;
            }
            bar.startPoint = nodes[i - 1]; bar.endPoint = nodes[i];
            bar.NormalizeEndpointOrder();
            bar.StartPosition = bar.startPoint.transform.position;
            bar.UpdateCreatingBar(bar.endPoint.transform.position);
            bar.startPoint.ConnectedBars.Add(bar); bar.endPoint.ConnectedBars.Add(bar);
            // New joints emerge at the drawing tip instead of appearing ahead of it.
            if (pendingAutoDraw.affectedObjects.Contains(nodes[i].gameObject))
            {
                Vector3 size = pointToInstantiate.transform.localScale;
                for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.1f)
                {
                    if (!CanContinueAutoDraw(material, nodes, location)) { CancelCreation(); yield break; }
                    nodes[i].transform.localScale = size * Mathf.SmoothStep(0f, 1f, t);
                    yield return null;
                }
                nodes[i].transform.localScale = size;
            }
            bar.startPoint.EvaluateAnchorState(); bar.endPoint.EvaluateAnchorState();
            if (AudioManager.Instance != null && !string.IsNullOrWhiteSpace(placeBarSfxId))
                AudioManager.Instance.PlaySFX(placeBarSfxId);
        }
        HistoryAction completed = pendingAutoDraw;
        pendingAutoDraw = null;
        autoDrawRoutine = null;
        Destroy(autoDrawOverlay); autoDrawOverlay = null;
        if (CommandManager.Instance != null) CommandManager.Instance.RecordAction(completed);
        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction($"Auto drew {positions.Count - 1} pieces");
        }
        if (BuildTutorialDirector.Instance != null) BuildTutorialDirector.Instance.OnBuildActionCompleted(completed);
    }

    private bool CanContinueAutoDraw(BridgeMaterialSO material, List<Point> nodes, BuildLocation location)
    {
        return !isSimulating && activeMaterial == material && nodes[0] != null &&
            nodes[nodes.Count - 1] != null && nodes[0].gameObject.activeInHierarchy &&
            nodes[nodes.Count - 1].gameObject.activeInHierarchy &&
            (GameManager.Instance == null || (GameManager.Instance.CurrentState == GameManager.GameState.Building &&
                GameManager.Instance.ActiveBuildLocation == location)) &&
            (BuildTutorialDirector.Instance == null || BuildTutorialDirector.Instance.CanPlaceMaterials);
    }

    private void PlaceAutoDrawRing(RectTransform ring, Vector3 position)
    {
        Camera camera = GetActiveCamera();
        if (camera == null || autoDrawOverlay == null) return;
        Vector3 screen = camera.WorldToScreenPoint(position);
        RectTransform canvasRect = (RectTransform)autoDrawOverlay.transform;
        // WorldToScreenPoint.z is distance from the build camera, not UI depth.
        // Convert only X/Y to the scaled canvas and keep the graphic at local Z = 0.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, new Vector2(screen.x, screen.y), null, out Vector2 local))
        {
            Vector2 half = ring.sizeDelta * 0.5f + new Vector2(8f, 8f);
            local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin + half.x, canvasRect.rect.xMax - half.x);
            local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + half.y, canvasRect.rect.yMax - half.y);
            ring.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }

    private void CancelAutoDraw()
    {
        if (autoDrawRoutine != null) StopCoroutine(autoDrawRoutine);
        autoDrawRoutine = null;
        ResetAutoDrawConfirmation();
        if (pendingAutoDraw != null)
        {
            foreach (GameObject obj in pendingAutoDraw.affectedObjects)
                if (obj != null) { obj.SetActive(false); Destroy(obj); }
            pendingAutoDraw = null;
        }
        if (autoDrawOverlay != null) { autoDrawOverlay.SetActive(false); Destroy(autoDrawOverlay); }
        autoDrawOverlay = null;
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
