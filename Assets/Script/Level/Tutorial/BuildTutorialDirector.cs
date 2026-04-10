using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MaterialUIMapping
{
    public BridgeMaterialSO material;
    public RectTransform buttonRect;
    [Tooltip("Where should the arrow hover? (0, 80) means above the button.")]
    public Vector2 arrowOffset = new Vector2(0, 80);
    [Tooltip("Rotation: 0 = Pointing Up, 180 = Pointing Down, 90 = Left, -90 = Right")]
    public float arrowRotation = 180f; 
}

[System.Serializable]
public class ToolUIMapping
{
    public GameObject toolObject;
    public RectTransform buttonRect;
    [Tooltip("Where should the arrow hover? (0, -80) means below the button.")]
    public Vector2 arrowOffset = new Vector2(0, -80);
    [Tooltip("Rotation: 0 = Pointing Up, 180 = Pointing Down")]
    public float arrowRotation = 0f; 
}

public class BuildTutorialDirector : MonoBehaviour
{
    public static BuildTutorialDirector Instance { get; private set; }

    [Header("UI References")]
    public TutorialPointer bouncingArrow;
    
    // --- NEW: Slot to hold your exit button! ---
    [Header("Exit Tutorial Control")]
    [Tooltip("Drag your UI Exit/Leave button here so we can hide it during the tutorial.")]
    public GameObject exitBuildModeButton;

    [Header("Material UI Library")]
    public List<MaterialUIMapping> materialMappings = new List<MaterialUIMapping>();

    [Header("Tool UI Library")]
    public List<ToolUIMapping> toolMappings = new List<ToolUIMapping>();

    [HideInInspector] public bool isTracingStep = false;
    [HideInInspector] public bool isCurrentDragValid = true;
    
    // --- NEW: Flag to track if the tutorial is currently active ---
    [HideInInspector] public bool isTutorialRunning = false; 
    
    private GhostSegment[] activeGhosts; 
    private Transform[] activeGhostPoints; 
    private bool wasInvalidLastFrame = false;

    private Bar lastTintedBar;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();

    private void Awake()
    {
        Instance = this;
        if (bouncingArrow != null) bouncingArrow.Hide();
    }

    private void Update()
    {
        if (isTracingStep && activeGhosts != null && BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            var bc = BuildUIController.Instance.barCreator;

            // --- SMART TOOL GUIDANCE ---
            BridgeMaterialSO neededMat = null;
            foreach (var ghost in activeGhosts)
            {
                if (ghost.gameObject.activeSelf && !ghost.isCovered)
                {
                    neededMat = ghost.requiredMaterial;
                    break;
                }
            }

            if (neededMat != null)
            {
                if (bc.activeMaterial != neededMat)
                {
                    BuildUIController.Instance.whitelistedMaterial = neededMat;
                    
                    foreach (var mapping in materialMappings)
                    {
                        if (mapping.material == neededMat)
                        {
                            if (bouncingArrow != null && mapping.buttonRect != null)
                            {
                                bouncingArrow.PointAt(mapping.buttonRect, mapping.arrowOffset);
                                bouncingArrow.transform.localEulerAngles = new Vector3(0, 0, mapping.arrowRotation);
                            }
                            break;
                        }
                    }
                }
                else if (!bc.IsCreating) 
                {
                    bouncingArrow.Hide();
                    BuildUIController.Instance.whitelistedMaterial = null;
                }
            }

            // --- WARDEN & COMPLETION LOGIC ---
            if (!bc.IsCreating)
            {
                CheckGhostBridgeCompletion();
                
                wasInvalidLastFrame = false;
                isCurrentDragValid = true;
                originalColors.Clear();
                lastTintedBar = null;
            }
            else if (bc.IsCreating && bc.currentBar != null && bc.currentStartPoint != null && bc.currentEndPoint != null)
            {
                bool isValidStart = false;
                bool isPerfectlyOnBlueprint = false;
                bool isTouchingGhostPoint = false;

                Vector3 dragStart = bc.currentStartPoint.transform.position;
                Vector3 dragEnd = bc.currentEndPoint.transform.position;
                
                bool isPier = bc.currentBar.materialData != null && bc.currentBar.materialData.isPier;

                foreach (var ghost in activeGhosts)
                {
                    if (!ghost.gameObject.activeSelf || ghost.requiredMaterial != bc.currentBar.materialData) continue;

                    if (isPier) isValidStart = true; 
                    else if (Vector3.Distance(dragStart, ghost.startPos) < 0.8f || Vector3.Distance(dragStart, ghost.endPos) < 0.8f) isValidStart = true;

                    if (IsPointOnLineSegment(dragStart, ghost.startPos, ghost.endPos, 0.8f) &&
                        IsPointOnLineSegment(dragEnd, ghost.startPos, ghost.endPos, 0.8f))
                    {
                        isPerfectlyOnBlueprint = true;
                    }

                    if (Vector3.Distance(dragEnd, ghost.startPos) <= 1.2f || Vector3.Distance(dragEnd, ghost.endPos) <= 1.2f)
                    {
                        isTouchingGhostPoint = true;
                    }
                }

                isCurrentDragValid = isValidStart && isPerfectlyOnBlueprint && isTouchingGhostPoint;

                if (!isCurrentDragValid && !wasInvalidLastFrame)
                {
                    SetBarTint(bc.currentBar, true); 
                    wasInvalidLastFrame = true;
                }
                else if (isCurrentDragValid && wasInvalidLastFrame)
                {
                    SetBarTint(bc.currentBar, false); 
                    wasInvalidLastFrame = false;
                }
            }
        }
    }

    private void SetBarTint(Bar bar, bool isRed)
    {
        if (bar == null) return;
        if (bar != lastTintedBar)
        {
            originalColors.Clear();
            lastTintedBar = bar;
        }

        foreach (Renderer rend in bar.GetComponentsInChildren<Renderer>())
        {
            string colorProp = rend.material.HasProperty("_Color") ? "_Color" : (rend.material.HasProperty("_BaseColor") ? "_BaseColor" : null);
            if (colorProp != null)
            {
                if (isRed)
                {
                    if (!originalColors.ContainsKey(rend)) originalColors[rend] = rend.material.GetColor(colorProp);
                    rend.material.SetColor(colorProp, new Color(1f, 0.2f, 0.2f, 1f));
                }
                else if (originalColors.ContainsKey(rend)) rend.material.SetColor(colorProp, originalColors[rend]);
            }
        }
    }

    public Vector3 GetClosestValidNode(Vector3 playerPos)
    {
        Vector3 bestNode = playerPos;
        float minDist = 2.0f; 
        if (activeGhosts == null) return playerPos;

        foreach (var ghost in activeGhosts)
        {
            if (!ghost.gameObject.activeSelf) continue;
            
            float d1 = Vector3.Distance(playerPos, ghost.startPos);
            if (d1 < minDist) { minDist = d1; bestNode = ghost.startPos; }

            float d2 = Vector3.Distance(playerPos, ghost.endPos);
            if (d2 < minDist) { minDist = d2; bestNode = ghost.endPos; }
        }
        return bestNode;
    }

    private bool IsPointOnLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd, float tolerance)
    {
        Vector3 lineDir = lineEnd - lineStart;
        float lineLength = lineDir.magnitude;
        if (lineLength < 0.01f) return Vector3.Distance(point, lineStart) <= tolerance;
        
        lineDir.Normalize();
        Vector3 pointVec = point - lineStart;
        float dotProduct = Mathf.Clamp(Vector3.Dot(pointVec, lineDir), 0f, lineLength);
        Vector3 closestPoint = lineStart + lineDir * dotProduct;
        
        return Vector3.Distance(point, closestPoint) <= tolerance;
    }

    public void LockAllUI()
    {
        // --- NEW: Mark the tutorial as active and hide the exit button! ---
        isTutorialRunning = true;

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.isTutorialUI_Locked = true;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
        }
        
        if (bouncingArrow != null) bouncingArrow.Hide();
        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(false);
    }

    public void PromptMaterialClick(BridgeMaterialSO mat)
    {
        LockAllUI();
        if (BuildUIController.Instance != null) BuildUIController.Instance.whitelistedMaterial = mat;

        foreach (var mapping in materialMappings)
        {
            if (mapping.material == mat)
            {
                if (bouncingArrow != null && mapping.buttonRect != null)
                {
                    bouncingArrow.PointAt(mapping.buttonRect, mapping.arrowOffset);
                    bouncingArrow.transform.localEulerAngles = new Vector3(0, 0, mapping.arrowRotation);
                }
                break;
            }
        }
        if (TutorialManager.Instance != null) TutorialManager.Instance.SetNextButtonActive(false);
    }

    public void PromptToolClick(GameObject toolObj)
    {
        LockAllUI();
        if (BuildUIController.Instance != null) BuildUIController.Instance.whitelistedButton = toolObj;

        foreach (var mapping in toolMappings)
        {
            if (mapping.toolObject == toolObj)
            {
                if (bouncingArrow != null && mapping.buttonRect != null)
                {
                    bouncingArrow.PointAt(mapping.buttonRect, mapping.arrowOffset);
                    bouncingArrow.transform.localEulerAngles = new Vector3(0, 0, mapping.arrowRotation);
                }
                break;
            }
        }
        if (TutorialManager.Instance != null) TutorialManager.Instance.SetNextButtonActive(false);
    }

    public void PromptDrawBridge()
    {
        LockAllUI();
        if (TutorialManager.Instance != null) TutorialManager.Instance.SetNextButtonActive(false);
        
        activeGhosts = FindObjectsOfType<GhostSegment>(false);
        
        if (activeGhosts != null && activeGhosts.Length > 0 && activeGhosts[0] != null)
        {
            Transform parentFolder = activeGhosts[0].transform.parent;
            List<Transform> gPoints = new List<Transform>();
            foreach (Transform child in parentFolder)
            {
                if (child.name.Contains("Ghost_Point")) gPoints.Add(child);
            }
            activeGhostPoints = gPoints.ToArray();
        }

        isTracingStep = true;
    }

    public void CheckGhostBridgeCompletion()
    {
        if (activeGhosts == null || activeGhosts.Length == 0) return;

        List<Bar> allRealBars = new List<Bar>();
        foreach (Point p in Point.AllPoints)
        {
            if (!p.gameObject.activeSelf) continue;
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && !allRealBars.Contains(b)) allRealBars.Add(b);
            }
        }

        bool allGhostsCovered = true;

        foreach (var ghost in activeGhosts)
        {
            bool isSegCovered = false;
            Vector3 segMidPoint = (ghost.startPos + ghost.endPos) / 2f;

            foreach (Bar realBar in allRealBars)
            {
                if (realBar.materialData != ghost.requiredMaterial) continue;

                Vector3 rs = realBar.startPoint.transform.position;
                Vector3 re = realBar.endPoint.transform.position;

                if (IsPointOnLineSegment(segMidPoint, rs, re, 0.8f))
                {
                    isSegCovered = true;
                    break;
                }
            }

            ghost.isCovered = isSegCovered;
            if (ghost.gameObject.activeSelf == isSegCovered) ghost.gameObject.SetActive(!isSegCovered);
            if (!isSegCovered) allGhostsCovered = false;
        }

        if (activeGhostPoints != null)
        {
            foreach (Transform gPoint in activeGhostPoints)
            {
                bool isPointCovered = false;
                foreach (Point p in Point.AllPoints)
                {
                    if (!p.gameObject.activeSelf) continue;
                    
                    Vector2 gp2D = new Vector2(gPoint.position.x, gPoint.position.y);
                    Vector2 p2D = new Vector2(p.transform.position.x, p.transform.position.y);
                    
                    if (Vector2.Distance(gp2D, p2D) < 0.5f)
                    {
                        isPointCovered = true;
                        break;
                    }
                }

                if (gPoint.gameObject.activeSelf == isPointCovered) 
                {
                    gPoint.gameObject.SetActive(!isPointCovered);
                }
            }
        }

        if (allGhostsCovered)
        {
            isTracingStep = false; 
            
            if (activeGhosts.Length > 0 && activeGhosts[0] != null)
            {
                activeGhosts[0].transform.parent.gameObject.SetActive(false);
            }

            if (TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep(); 
        }
    }

    public void OnMaterialClicked(BridgeMaterialSO clickedMat)
    {
        if (BuildUIController.Instance != null && BuildUIController.Instance.isTutorialUI_Locked)
        {
            if (clickedMat == BuildUIController.Instance.whitelistedMaterial)
            {
                bouncingArrow.Hide();
                if (TutorialManager.Instance != null && !isTracingStep) TutorialManager.Instance.ShowNextStep();
            }
        }
    }

    public void EndTutorial()
    {
        // --- NEW: Mark the tutorial as completely finished and turn the exit button back on! ---
        isTutorialRunning = false; 

        if (BuildUIController.Instance != null) BuildUIController.Instance.isTutorialUI_Locked = false;
        if (bouncingArrow != null) bouncingArrow.Hide();
        isTracingStep = false;

        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(true);
    }
}