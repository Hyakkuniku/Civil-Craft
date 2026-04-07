using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.EventSystems;

[System.Serializable]
public class CopiedBarInfo 
{ 
    public int startIdx; 
    public int endIdx; 
    public BridgeMaterialSO mat; 
}

[RequireComponent(typeof(BarCreator))]
public class ClipboardManager : MonoBehaviour
{
    public static ClipboardManager Instance { get; private set; }
    private BarCreator barCreator;

    [Header("Override Confirmation UI")]
    public GameObject overrideConfirmPanel;
    public UnityEngine.UI.Toggle dontShowAgainToggle;
    private bool skipOverrideConfirm = false;

    [HideInInspector] public bool isPasteMode = false;
    private bool isPasteFromCut = false; 
    [HideInInspector] public bool isDraggingSelection = false;
    
    private List<Vector3> copiedRelativePoints = new List<Vector3>();
    private List<CopiedBarInfo> copiedBars = new List<CopiedBarInfo>();
    private List<GameObject> ghostPastePoints = new List<GameObject>();
    private List<Bar> ghostPasteBars = new List<Bar>();
    
    private bool isValidPaste = false;
    private Vector3 pasteRootPos;
    private Vector3 pasteDragOffset; 

    private float initialRotationAngle = 0f;
    private float pasteRotationOffset = 0f;

    private void Awake() 
    { 
        Instance = this; 
        barCreator = GetComponent<BarCreator>();
        if (overrideConfirmPanel != null) overrideConfirmPanel.SetActive(false);
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building) return;
        if (barCreator.isSimulating) return;

        if (isPasteMode && Touch.activeTouches.Count == 2)
        {
            var touch0 = Touch.activeTouches[0];
            var touch1 = Touch.activeTouches[1];

            if (touch1.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                Vector2 dir = touch1.screenPosition - touch0.screenPosition;
                initialRotationAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            }
            else if (touch0.phase == UnityEngine.InputSystem.TouchPhase.Moved || touch1.phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector2 currentDir = touch1.screenPosition - touch0.screenPosition;
                float currentAngle = Mathf.Atan2(currentDir.y, currentDir.x) * Mathf.Rad2Deg;
                float deltaAngle = currentAngle - initialRotationAngle;
                
                pasteRotationOffset += deltaAngle;
                initialRotationAngle = currentAngle; 

                UpdatePasteGhostsWorldPosition(pasteRootPos);
            }
        }
    }

    public void CopySelected(List<Point> ignoredPointsParam)
    {
        if (barCreator.selectedPoints.Count == 0 && barCreator.selectedBars.Count == 0) return;
        
        HashSet<Point> expandedPoints = new HashSet<Point>(barCreator.selectedPoints);
        HashSet<Bar> capturedBars = new HashSet<Bar>(barCreator.selectedBars);

        if (barCreator.selectedPoints.Count == 1 && barCreator.selectedBars.Count == 0)
        {
            foreach (Bar b in barCreator.selectedPoints[0].ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf) capturedBars.Add(b);
            }
        }
        else
        {
            foreach (Point p in barCreator.selectedPoints)
            {
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf &&
                        barCreator.selectedPoints.Contains(b.startPoint) &&
                        barCreator.selectedPoints.Contains(b.endPoint))
                    {
                        capturedBars.Add(b);
                    }
                }
            }
        }

        if (capturedBars.Count == 0)
        {
            barCreator.ClearSelectionPublic();
            return;
        }

        foreach(Bar b in capturedBars)
        {
            expandedPoints.Add(b.startPoint);
            expandedPoints.Add(b.endPoint);
        }

        List<Point> finalPointList = new List<Point>(expandedPoints);
        copiedRelativePoints.Clear();
        copiedBars.Clear();

        Vector3 rootPos = finalPointList[0].transform.position;
        for (int i = 0; i < finalPointList.Count; i++)
        {
            copiedRelativePoints.Add(finalPointList[i].transform.position - rootPos);
        }

        foreach (Bar b in capturedBars)
        {
            int sIdx = finalPointList.IndexOf(b.startPoint);
            int eIdx = finalPointList.IndexOf(b.endPoint);
            
            if (sIdx != -1 && eIdx != -1) 
            {
                copiedBars.Add(new CopiedBarInfo { startIdx = sIdx, endIdx = eIdx, mat = b.materialData });
            }
        }

        barCreator.CancelAllModes(); 
        isPasteMode = true;
        isPasteFromCut = false; 
        pasteRotationOffset = 0f; 

        Vector3 spawnPos = barCreator.GetWorldMousePosition(new Vector2(Screen.width / 2f, Screen.height / 2f));
        if (barCreator.isGridSnappingEnabled) spawnPos = new Vector3(Mathf.Round(spawnPos.x), Mathf.Round(spawnPos.y), spawnPos.z);
        
        pasteRootPos = spawnPos; 
        CreatePasteGhosts();
        UpdatePasteGhostsWorldPosition(pasteRootPos); 
        
        if (BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.SetSelectionPanelActive(true);
            BuildUIController.Instance.LogAction("Selection Copied");
        }
    }
    
    public void CutSelected(List<Point> ignoredPointsParam)
    {
        if (barCreator.selectedPoints.Count == 0 && barCreator.selectedBars.Count == 0) return;
        
        HashSet<Point> expandedPoints = new HashSet<Point>(barCreator.selectedPoints);
        HashSet<Bar> capturedBars = new HashSet<Bar>(barCreator.selectedBars);

        if (barCreator.selectedPoints.Count == 1 && barCreator.selectedBars.Count == 0)
        {
            foreach (Bar b in barCreator.selectedPoints[0].ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf) capturedBars.Add(b);
            }
        }
        else
        {
            foreach (Point p in barCreator.selectedPoints)
            {
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf &&
                        barCreator.selectedPoints.Contains(b.startPoint) &&
                        barCreator.selectedPoints.Contains(b.endPoint))
                    {
                        capturedBars.Add(b);
                    }
                }
            }
        }

        if (capturedBars.Count == 0)
        {
            barCreator.ClearSelectionPublic();
            return;
        }

        foreach(Bar b in capturedBars)
        {
            expandedPoints.Add(b.startPoint);
            expandedPoints.Add(b.endPoint);
        }

        List<Point> finalPointList = new List<Point>(expandedPoints);
        copiedRelativePoints.Clear();
        copiedBars.Clear();

        Vector3 rootPos = finalPointList[0].transform.position;
        for (int i = 0; i < finalPointList.Count; i++)
            copiedRelativePoints.Add(finalPointList[i].transform.position - rootPos);

        foreach (Bar b in capturedBars)
        {
            int sIdx = finalPointList.IndexOf(b.startPoint);
            int eIdx = finalPointList.IndexOf(b.endPoint);
            if (sIdx != -1 && eIdx != -1) 
                copiedBars.Add(new CopiedBarInfo { startIdx = sIdx, endIdx = eIdx, mat = b.materialData });
        }

        HistoryAction cutAction = new HistoryAction { isBuildEvent = false };
        
        foreach (Bar b in capturedBars)
        {
            barCreator.DeleteBar(b, cutAction);
        }

        foreach (Point p in finalPointList)
        {
            bool hasActiveNeighbors = false;
            foreach (Bar b in p.ConnectedBars)
            {
                if (b.gameObject.activeSelf) hasActiveNeighbors = true;
            }

            if (!hasActiveNeighbors && p.Runtime && p.gameObject.activeSelf)
            {
                cutAction.affectedObjects.Add(p.gameObject);
                p.gameObject.SetActive(false);
            }
        }

        if (cutAction.affectedObjects.Count > 0) CommandManager.Instance.RecordAction(cutAction);

        barCreator.CancelAllModes();
        isPasteMode = true;
        isPasteFromCut = true; 
        pasteRotationOffset = 0f; 

        Vector3 spawnPos = barCreator.GetWorldMousePosition(new Vector2(Screen.width / 2f, Screen.height / 2f));
        if (barCreator.isGridSnappingEnabled) spawnPos = new Vector3(Mathf.Round(spawnPos.x), Mathf.Round(spawnPos.y), spawnPos.z);
        
        pasteRootPos = spawnPos; 
        CreatePasteGhosts();
        UpdatePasteGhostsWorldPosition(pasteRootPos); 

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.SetSelectionPanelActive(true);
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Selection Cut");
        }
    }

    private bool CanAffordPaste()
    {
        if (BuildUIController.Instance == null) return true;
        
        float pasteCost = 0f;
        foreach (var cb in copiedBars)
        {
            Vector3 p1 = copiedRelativePoints[cb.startIdx];
            Vector3 p2 = copiedRelativePoints[cb.endIdx];
            float length = Vector3.Distance(p1, p2);
            int multiplier = cb.mat.isDualBeam ? 2 : 1;
            pasteCost += length * cb.mat.costPerMeter * multiplier;
        }

        float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
        return pasteCost <= remainingBudget;
    }

    private Bar GetExistingBar(Point p1, Point p2) 
    {
        if (p1 == null || p2 == null) return null;
        foreach(Bar b in p1.ConnectedBars) 
        {
            if (b.gameObject.activeSelf && ((b.startPoint == p1 && b.endPoint == p2) || (b.startPoint == p2 && b.endPoint == p1))) 
            {
                return b;
            }
        }
        return null;
    }

    public void StampPaste()
    {
        if (!isPasteMode) return;

        if (!isValidPaste) 
        {
            if (!CanAffordPaste() && BuildUIController.Instance != null)
            {
                BuildUIController.Instance.LogAction("Insufficient budget to paste!");
            }
            return;
        }

        List<Point> newRealPoints = new List<Point>();
        float snappedRotation = Mathf.Round(pasteRotationOffset / 15f) * 15f;
        Quaternion rotation = Quaternion.Euler(0, 0, snappedRotation);

        for (int i = 0; i < copiedRelativePoints.Count; i++)
        {
            Vector3 rotatedOffset = rotation * copiedRelativePoints[i];
            Vector3 targetPos = pasteRootPos + rotatedOffset;
            Point mappedPoint = null;

            foreach (Point existingP in Point.AllPoints)
            {
                if (existingP.gameObject.activeSelf && Vector3.Distance(targetPos, existingP.transform.position) < 0.2f)
                {
                    mappedPoint = existingP;
                    break;
                }
            }
            newRealPoints.Add(mappedPoint); 
        }

        bool needsOverride = false;
        for (int i = 0; i < copiedBars.Count; i++)
        {
            var cb = copiedBars[i];
            Point p1 = newRealPoints[cb.startIdx];
            Point p2 = newRealPoints[cb.endIdx];

            Bar existingBar = GetExistingBar(p1, p2);
            if (existingBar != null && existingBar.materialData != cb.mat) 
            {
                needsOverride = true;
                break;
            }
        }

        if (needsOverride && !skipOverrideConfirm)
        {
            if (overrideConfirmPanel != null) 
            {
                overrideConfirmPanel.SetActive(true);
            }
            else ExecutePaste(); 
            return;
        }

        ExecutePaste();
    }

    public void ConfirmOverride()
    {
        if (dontShowAgainToggle != null) 
        {
            skipOverrideConfirm = dontShowAgainToggle.isOn; 
        }
        if (overrideConfirmPanel != null) overrideConfirmPanel.SetActive(false);
        
        ExecutePaste();
    }

    public void CancelOverride()
    {
        if (overrideConfirmPanel != null) overrideConfirmPanel.SetActive(false);
        
        if (BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.LogAction("Paste Canceled"); 
        }
    }

    private void ExecutePaste()
    {
        HistoryAction pasteAction = new HistoryAction { isBuildEvent = true };
        List<Point> newRealPoints = new List<Point>();
        
        float snappedRotation = Mathf.Round(pasteRotationOffset / 15f) * 15f;
        Quaternion rotation = Quaternion.Euler(0, 0, snappedRotation);

        for (int i = 0; i < copiedRelativePoints.Count; i++)
        {
            Vector3 rotatedOffset = rotation * copiedRelativePoints[i];
            Vector3 targetPos = pasteRootPos + rotatedOffset;
            Point mappedPoint = null;

            foreach (Point existingP in Point.AllPoints)
            {
                if (existingP.gameObject.activeSelf && Vector3.Distance(targetPos, existingP.transform.position) < 0.2f)
                {
                    mappedPoint = existingP;
                    break;
                }
            }

            if (mappedPoint == null)
            {
                GameObject pObj = Instantiate(barCreator.pointToInstantiate, targetPos, Quaternion.identity, barCreator.pointParent);
                mappedPoint = pObj.GetComponent<Point>();
                pasteAction.affectedObjects.Add(pObj);
            }
            newRealPoints.Add(mappedPoint);
        }

        for (int i = 0; i < copiedBars.Count; i++)
        {
            var cb = copiedBars[i];
            Point p1 = newRealPoints[cb.startIdx];
            Point p2 = newRealPoints[cb.endIdx];

            if (cb.mat.isPier && p1.transform.position.y > p2.transform.position.y)
            {
                Point temp = p1; p1 = p2; p2 = temp;
            }

            Bar existingBar = GetExistingBar(p1, p2);

            if (existingBar != null)
            {
                if (existingBar.materialData != cb.mat)
                {
                    pasteAction.disabledObjects.Add(existingBar.gameObject);
                    existingBar.gameObject.SetActive(false); 
                }
                else
                {
                    continue; 
                }
            }

            GameObject bObj = Instantiate(barCreator.barToInstantiate, barCreator.barParent);
            Bar newBar = bObj.GetComponent<Bar>();
            newBar.Initialize(cb.mat);
            newBar.StartPosition = p1.transform.position;
            newBar.UpdateCreatingBar(p2.transform.position);
            newBar.startPoint = p1;
            newBar.endPoint = p2;
            
            p1.ConnectedBars.Add(newBar);
            p2.ConnectedBars.Add(newBar);
            
            pasteAction.affectedObjects.Add(bObj);
        }

        foreach(Point p in newRealPoints) p.EvaluateAnchorState();

        CommandManager.Instance.RecordAction(pasteAction);
        
        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Selection Pasted");
        }

        if (isPasteFromCut)
        {
            CancelPasteMode();
            if (BuildUIController.Instance != null) BuildUIController.Instance.SetSelectionPanelActive(false);
        }
    }

    public void CancelPasteMode()
    {
        isPasteMode = false;
        isPasteFromCut = false;
        DestroyPasteGhosts();
    }

    private void CreatePasteGhosts()
    {
        DestroyPasteGhosts(); 
        
        foreach (Vector3 relPos in copiedRelativePoints)
        {
            GameObject gp = Instantiate(barCreator.pointToInstantiate, barCreator.pointParent);
            gp.name = "GhostPastePoint";
            Destroy(gp.GetComponent<Collider>());
            
            // The Point script deletes its own renderer when destroyed,
            // so we'll destroy it but force the renderer to turn back on in the Update loop!
            Destroy(gp.GetComponent<Point>()); 
            ghostPastePoints.Add(gp);
        }
        
        foreach (var cb in copiedBars)
        {
            GameObject gb = Instantiate(barCreator.barToInstantiate, barCreator.barParent);
            gb.name = "GhostPasteBar";
            Bar bar = gb.GetComponent<Bar>();
            bar.Initialize(cb.mat);
            ghostPasteBars.Add(bar);
        }
    }

    private void UpdatePasteGhostsWorldPosition(Vector3 rootWorldPos)
    {
        if (!isPasteMode || ghostPastePoints.Count == 0) return;

        float snappedRotation = Mathf.Round(pasteRotationOffset / 15f) * 15f;
        Quaternion rotation = Quaternion.Euler(0, 0, snappedRotation);
        
        pasteRootPos = rootWorldPos; 
        isValidPaste = false;
        Vector3 rigidSnapShift = Vector3.zero;

        for (int i = 0; i < ghostPastePoints.Count; i++)
        {
            Vector3 rotatedOffset = rotation * copiedRelativePoints[i];
            ghostPastePoints[i].transform.position = pasteRootPos + rotatedOffset;
        }

        foreach (GameObject gp in ghostPastePoints)
        {
            foreach (Point existingP in Point.AllPoints)
            {
                if (existingP.gameObject.activeSelf && !ghostPastePoints.Contains(existingP.gameObject))
                {
                    if (Vector3.Distance(gp.transform.position, existingP.transform.position) < 0.6f)
                    {
                        rigidSnapShift = existingP.transform.position - gp.transform.position;
                        isValidPaste = true;
                        break;
                    }
                }
            }
            if (isValidPaste) break; 
        }

        pasteRootPos += rigidSnapShift;

        for (int i = 0; i < ghostPastePoints.Count; i++)
        {
            Vector3 rotatedOffset = rotation * copiedRelativePoints[i];
            ghostPastePoints[i].transform.position = pasteRootPos + rotatedOffset;
        }

        for (int i = 0; i < ghostPasteBars.Count; i++)
        {
            Bar gb = ghostPasteBars[i];
            var cb = copiedBars[i];
            gb.StartPosition = ghostPastePoints[cb.startIdx].transform.position;
            gb.UpdateCreatingBar(ghostPastePoints[cb.endIdx].transform.position);
        }

        if (isValidPaste)
        {
            foreach (var cb in copiedBars)
            {
                if (cb.mat.isPier)
                {
                    float bottomY = Mathf.Min(ghostPastePoints[cb.startIdx].transform.position.y, ghostPastePoints[cb.endIdx].transform.position.y);
                    if (Mathf.Abs(bottomY - barCreator.pierBaseY) > 1.5f) 
                    {
                        isValidPaste = false; 
                        break;
                    }
                }
            }
        }

        if (isValidPaste && !CanAffordPaste())
        {
            isValidPaste = false;
        }

        Color tintColor = isValidPaste ? new Color(0.2f, 1f, 0.2f, 0.6f) : new Color(1f, 0.2f, 0.2f, 0.6f);
        
        foreach (GameObject gp in ghostPastePoints)
        {
            if (gp == null) continue;
            Renderer r = gp.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                // --- THE FIX: Force the node renderer back ON! ---
                r.enabled = true; 
                
                if (r.material.HasProperty("_Color")) r.material.color = tintColor;
                else if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", tintColor);
            }
        }
        
        foreach (Bar gb in ghostPasteBars)
        {
            Renderer[] rends = gb.GetComponentsInChildren<Renderer>();
            foreach(Renderer r in rends)
            {
                if (r.material.HasProperty("_Color")) r.material.color = tintColor;
                else if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", tintColor);
            }
        }
    }

    public void DestroyPasteGhosts()
    {
        foreach (GameObject gp in ghostPastePoints) if (gp != null) Destroy(gp);
        foreach (Bar gb in ghostPasteBars) if (gb != null) Destroy(gb.gameObject);
        ghostPastePoints.Clear();
        ghostPasteBars.Clear();
    }

    public void HandlePointerDown(PointerEventData eventData)
    {
        isDraggingSelection = true;
        Vector3 worldPos = barCreator.GetWorldMousePosition(eventData.position);
        pasteDragOffset = pasteRootPos - worldPos; 
    }

    public void HandleDrag(PointerEventData eventData)
    {
        if (isDraggingSelection)
        {
            Vector3 worldPos = barCreator.GetWorldMousePosition(eventData.position);
            Vector3 targetRoot = worldPos + pasteDragOffset;
            
            if (barCreator.isGridSnappingEnabled) 
            {
                targetRoot = new Vector3(Mathf.Round(targetRoot.x), Mathf.Round(targetRoot.y), targetRoot.z);
            }
            
            UpdatePasteGhostsWorldPosition(targetRoot);
        }
    }

    public void HandlePointerUp(PointerEventData eventData)
    {
        if (isDraggingSelection) isDraggingSelection = false;
    }
}