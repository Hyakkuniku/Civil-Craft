using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    public GameObject exitBuildModeButton;

    [Header("Invalid Placement / Undo UI")]
    public RectTransform undoButtonRect;
    public GameObject undoWarningPanel;
    public Vector2 undoArrowOffset = new Vector2(0, 80);
    public float undoArrowRotation = 180f;

    [Header("Material UI Library")]
    public List<MaterialUIMapping> materialMappings = new List<MaterialUIMapping>();

    [Header("Tool UI Library")]
    public List<ToolUIMapping> toolMappings = new List<ToolUIMapping>();

    [Header("Ghost Matching Tolerances")]
    [Min(0.01f)] public float endpointTolerance = 0.8f;
    [Min(0.01f)] public float pierXTolerance = 0.8f;
    [Min(0.01f)] public float snapTolerance = 1.2f;

    [HideInInspector] public bool isTracingStep;
    [HideInInspector] public bool isCurrentDragValid = true;
    [HideInInspector] public bool isTutorialRunning;

    public bool IsAwaitingInvalidBarUndo { get; private set; }
    public bool CanPlaceMaterials => !IsAwaitingInvalidBarUndo;

    private GhostSegment[] activeGhosts;
    private Transform[] activeGhostPoints;
    private int activeStepIndex = -1;

    private BridgeMaterialSO expectedMaterial;
    private GameObject expectedTool;
    private bool hasAdvancedFromRequiredClickThisStep;
    private Button trackedToolButton;
    private UnityAction trackedToolButtonAction;
    private Coroutine undoPointerCoroutine;

    private Bar lastTintedBar;
    private readonly Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    private readonly List<GameObject> invalidActionObjects = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Debug.LogWarning($"Duplicate BuildTutorialDirector on '{name}' was removed. TutorialSequence objects must not be destroyed with duplicate directors.");
            Destroy(this);
            return;
        }

        if (bouncingArrow != null) bouncingArrow.Hide();
        if (undoWarningPanel != null) undoWarningPanel.SetActive(false);
        trackedToolButtonAction = OnTrackedToolButtonClicked;
    }

    private void Update()
    {
        if (!isTracingStep || IsAwaitingInvalidBarUndo || activeGhosts == null) return;

        BarCreator barCreator = BuildUIController.Instance != null
            ? BuildUIController.Instance.barCreator
            : null;

        if (barCreator == null) return;

        if (!barCreator.IsCreating)
        {
            RestoreTintedBar();
            isCurrentDragValid = true;
            CheckGhostBridgeCompletion();
            return;
        }

        if (barCreator.currentBar == null || barCreator.currentStartPoint == null || barCreator.currentEndPoint == null)
        {
            isCurrentDragValid = false;
            return;
        }

        isCurrentDragValid = IsPlacementValid(
            barCreator.currentBar.materialData,
            barCreator.currentStartPoint.transform.position,
            barCreator.currentEndPoint.transform.position,
            null);

        SetBarTint(barCreator.currentBar, !isCurrentDragValid);
    }

    /// <summary>
    /// Called by TutorialManager before every step event. This makes step state independent
    /// from whichever prompt method the previous or next step happens to invoke.
    /// </summary>
    public void BeginStep(int stepIndex)
    {
        activeStepIndex = stepIndex;
        isTracingStep = false;
        isCurrentDragValid = true;
        hasAdvancedFromRequiredClickThisStep = false;
        expectedMaterial = null;
        expectedTool = null;
        activeGhosts = null;
        activeGhostPoints = null;
        ClearTrackedToolButton();
        RestoreTintedBar();
        ClearUndoPrompt();

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.isTutorialUI_Locked = false;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
        }

        if (bouncingArrow != null) bouncingArrow.Hide();
    }

    public void BeginSequence()
    {
        bool isBuildSequence = GameManager.Instance != null &&
                               GameManager.Instance.CurrentState == GameManager.GameState.Building;

        EndTutorial();
        isTutorialRunning = isBuildSequence;
        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(!isBuildSequence);
    }

    public void LockAllUI()
    {
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

    public void PromptMaterialClick(BridgeMaterialSO material)
    {
        LockAllUI();
        ClearTrackedToolButton();
        isTracingStep = false;
        expectedMaterial = material;
        expectedTool = null;
        hasAdvancedFromRequiredClickThisStep = false;

        if (BuildUIController.Instance != null)
            BuildUIController.Instance.whitelistedMaterial = material;

        foreach (MaterialUIMapping mapping in materialMappings)
        {
            if (mapping.material != material) continue;
            PointArrow(mapping.buttonRect, mapping.arrowOffset, mapping.arrowRotation);
            break;
        }

        if (TutorialManager.Instance != null) TutorialManager.Instance.SetNextButtonActive(false);
    }

    public void PromptToolClick(GameObject toolObject)
    {
        LockAllUI();
        isTracingStep = false;
        expectedMaterial = null;
        expectedTool = toolObject;
        hasAdvancedFromRequiredClickThisStep = false;
        ClearTrackedToolButton();

        if (BuildUIController.Instance != null)
            BuildUIController.Instance.whitelistedButton = toolObject;

        foreach (ToolUIMapping mapping in toolMappings)
        {
            if (mapping.toolObject != toolObject) continue;
            PointArrow(mapping.buttonRect, mapping.arrowOffset, mapping.arrowRotation);
            break;
        }

        if (toolObject != null)
        {
            trackedToolButton = toolObject.GetComponent<Button>();
            if (trackedToolButton == null) trackedToolButton = toolObject.GetComponentInChildren<Button>(true);
            if (trackedToolButton == null) trackedToolButton = toolObject.GetComponentInParent<Button>();
            if (trackedToolButton != null) trackedToolButton.onClick.AddListener(trackedToolButtonAction);
        }

        if (TutorialManager.Instance != null) TutorialManager.Instance.SetNextButtonActive(false);
    }

    public void PromptDrawBridge()
    {
        LockAllUI();
        ClearTrackedToolButton();
        isTracingStep = true;
        isCurrentDragValid = true;
        expectedMaterial = null;
        expectedTool = null;
        hasAdvancedFromRequiredClickThisStep = false;

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
            if (BuildUIController.Instance.barCreator != null)
                BuildUIController.Instance.barCreator.CancelAllModes();
        }

        if (TutorialManager.Instance != null) TutorialManager.Instance.SetNextButtonActive(false);
        if (bouncingArrow != null) bouncingArrow.Hide();

        activeGhosts = FindObjectsOfType<GhostSegment>(false);
        activeGhostPoints = FindGhostPoints(activeGhosts);

        if (activeGhosts == null || activeGhosts.Length == 0)
            Debug.LogWarning("Build tutorial tracing started, but no active GhostSegment objects were found.");
    }

    public void OnMaterialClicked(BridgeMaterialSO clickedMaterial)
    {
        if (clickedMaterial == null || clickedMaterial != expectedMaterial) return;
        AdvanceFromRequiredClick();
    }

    public void OnToolClicked(GameObject clickedObject)
    {
        if (!ObjectsRepresentSameButton(clickedObject, expectedTool)) return;
        ClearTrackedToolButton();
        AdvanceFromRequiredClick();
    }

    private void OnTrackedToolButtonClicked()
    {
        OnToolClicked(expectedTool);
    }

    private void ClearTrackedToolButton()
    {
        if (trackedToolButton != null && trackedToolButtonAction != null)
            trackedToolButton.onClick.RemoveListener(trackedToolButtonAction);
        trackedToolButton = null;
    }

    private void AdvanceFromRequiredClick()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial == null || !tutorial.IsTutorialActive || isTracingStep ||
            hasAdvancedFromRequiredClickThisStep || activeStepIndex != tutorial.CurrentStepIndex)
        {
            return;
        }

        hasAdvancedFromRequiredClickThisStep = true;
        ClearTrackedToolButton();
        expectedMaterial = null;
        expectedTool = null;
        if (bouncingArrow != null) bouncingArrow.Hide();
        tutorial.ShowNextStep();
    }

    public bool IsExpectedTool(GameObject clickedObject)
    {
        return ObjectsRepresentSameButton(clickedObject, expectedTool);
    }

    private static bool ObjectsRepresentSameButton(GameObject clickedObject, GameObject expectedObject)
    {
        if (clickedObject == null || expectedObject == null) return false;
        if (clickedObject == expectedObject) return true;
        return clickedObject.transform.IsChildOf(expectedObject.transform) ||
               expectedObject.transform.IsChildOf(clickedObject.transform);
    }

    /// <summary>
    /// Snaps a near-correct road/steel endpoint exactly onto its matching ghost endpoint.
    /// Piers intentionally are not endpoint-snapped because only their X coordinate matters.
    /// </summary>
    public bool TryGetSnappedEndPosition(
        BridgeMaterialSO material,
        Vector3 startPosition,
        Vector3 candidateEndPosition,
        out Vector3 snappedEndPosition)
    {
        snappedEndPosition = candidateEndPosition;
        if (!isTracingStep || IsAwaitingInvalidBarUndo || material == null || activeGhosts == null) return false;

        foreach (GhostSegment ghost in activeGhosts)
        {
            if (!IsUsableGhost(ghost, material)) continue;

            if (material.isPier)
            {
                if (Mathf.Abs(startPosition.x - ghost.startPos.x) <= snapTolerance)
                {
                    snappedEndPosition.x = ghost.startPos.x;
                    return true;
                }
                continue;
            }

            if (Vector3.Distance(startPosition, ghost.startPos) <= snapTolerance &&
                Vector3.Distance(candidateEndPosition, ghost.endPos) <= snapTolerance)
            {
                snappedEndPosition = ghost.endPos;
                return true;
            }

            if (Vector3.Distance(startPosition, ghost.endPos) <= snapTolerance &&
                Vector3.Distance(candidateEndPosition, ghost.startPos) <= snapTolerance)
            {
                snappedEndPosition = ghost.startPos;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Called after BarCreator has finalized and recorded the build action. Validation here
    /// uses the real endpoints, so release-frame ordering, snapping, limits, and terrain clamps
    /// cannot desynchronize the tutorial.
    /// </summary>
    public void OnBuildActionCompleted(HistoryAction buildAction)
    {
        if (!isTracingStep || IsAwaitingInvalidBarUndo || buildAction == null) return;

        HashSet<GhostSegment> matchedGhosts = new HashSet<GhostSegment>();
        Bar firstInvalidBar = null;

        foreach (GameObject affectedObject in buildAction.affectedObjects)
        {
            if (affectedObject == null || !affectedObject.activeSelf) continue;
            Bar bar = affectedObject.GetComponent<Bar>();
            if (bar == null) continue;

            GhostSegment matchedGhost;
            if (!IsCompletedBarValid(bar, matchedGhosts, out matchedGhost))
            {
                firstInvalidBar = bar;
                break;
            }

            matchedGhosts.Add(matchedGhost);
        }

        if (firstInvalidBar != null)
        {
            invalidActionObjects.Clear();
            invalidActionObjects.AddRange(buildAction.affectedObjects);
            SetBarTint(firstInvalidBar, true);
            PromptUndoInvalidBar();
            return;
        }

        RestoreTintedBar();
        CheckGhostBridgeCompletion();
    }

    private bool IsCompletedBarValid(Bar bar, HashSet<GhostSegment> alreadyMatched, out GhostSegment matchedGhost)
    {
        matchedGhost = null;
        if (bar == null || bar.materialData == null || bar.startPoint == null || bar.endPoint == null) return false;

        Vector3 start = bar.startPoint.transform.position;
        Vector3 end = bar.endPoint.transform.position;

        foreach (GhostSegment ghost in activeGhosts)
        {
            if (alreadyMatched.Contains(ghost)) continue;
            if (!IsPlacementValid(bar.materialData, start, end, ghost)) continue;
            matchedGhost = ghost;
            return true;
        }

        return false;
    }

    private bool IsPlacementValid(BridgeMaterialSO material, Vector3 start, Vector3 end, GhostSegment onlyGhost)
    {
        if (material == null || activeGhosts == null) return false;

        foreach (GhostSegment ghost in activeGhosts)
        {
            if (onlyGhost != null && ghost != onlyGhost) continue;
            if (!IsUsableGhost(ghost, material)) continue;
            if (DoesPlacementMatchGhost(material, start, end, ghost)) return true;
        }

        return false;
    }

    private static bool IsUsableGhost(GhostSegment ghost, BridgeMaterialSO material)
    {
        return ghost != null && ghost.gameObject.activeInHierarchy && !ghost.isCovered &&
               ghost.requiredMaterial == material;
    }

    private bool DoesPlacementMatchGhost(BridgeMaterialSO material, Vector3 start, Vector3 end, GhostSegment ghost)
    {
        if (material == null || ghost == null || ghost.requiredMaterial != material) return false;

        if (material.isPier)
        {
            // Piers intentionally match only the blueprint's X axis.
            return Mathf.Abs(start.x - ghost.startPos.x) <= pierXTolerance;
        }

        bool forwardMatch = Vector3.Distance(start, ghost.startPos) <= endpointTolerance &&
                            Vector3.Distance(end, ghost.endPos) <= endpointTolerance;
        bool reverseMatch = Vector3.Distance(start, ghost.endPos) <= endpointTolerance &&
                            Vector3.Distance(end, ghost.startPos) <= endpointTolerance;
        return forwardMatch || reverseMatch;
    }

    public void PromptUndoInvalidBar()
    {
        IsAwaitingInvalidBarUndo = true;
        isCurrentDragValid = false;

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.SetToolForcedVisible(BuildModeTool.Undo, true);
            BuildUIController.Instance.isTutorialUI_Locked = true;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = undoButtonRect != null ? undoButtonRect.gameObject : null;
        }

        if (undoWarningPanel != null) undoWarningPanel.SetActive(true);
        Canvas.ForceUpdateCanvases();
        PointArrow(undoButtonRect, undoArrowOffset, undoArrowRotation);

        if (undoPointerCoroutine != null) StopCoroutine(undoPointerCoroutine);
        undoPointerCoroutine = StartCoroutine(RepointUndoArrowAfterLayout());
    }

    private IEnumerator RepointUndoArrowAfterLayout()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (IsAwaitingInvalidBarUndo)
            PointArrow(undoButtonRect, undoArrowOffset, undoArrowRotation);

        undoPointerCoroutine = null;
    }

    public void NotifyUndoCompleted()
    {
        if (!IsAwaitingInvalidBarUndo) return;

        foreach (GameObject actionObject in invalidActionObjects)
        {
            if (actionObject != null && actionObject.activeSelf) return;
        }

        ClearUndoPrompt();
        isCurrentDragValid = true;
        RestoreTintedBar();

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.isTutorialUI_Locked = true;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
        }

        if (bouncingArrow != null) bouncingArrow.Hide();
        CheckGhostBridgeCompletion();
    }

    private void ClearUndoPrompt()
    {
        if (undoPointerCoroutine != null)
        {
            StopCoroutine(undoPointerCoroutine);
            undoPointerCoroutine = null;
        }

        IsAwaitingInvalidBarUndo = false;
        invalidActionObjects.Clear();
        if (undoWarningPanel != null) undoWarningPanel.SetActive(false);
        if (BuildUIController.Instance != null)
            BuildUIController.Instance.SetToolForcedVisible(BuildModeTool.Undo, false);
    }

    public void CheckGhostBridgeCompletion()
    {
        if (!isTracingStep || IsAwaitingInvalidBarUndo || activeGhosts == null || activeGhosts.Length == 0) return;

        Bar[] allRealBars = FindObjectsOfType<Bar>();
        bool allGhostsCovered = true;
        HashSet<Bar> usedBars = new HashSet<Bar>();

        foreach (GhostSegment ghost in activeGhosts)
        {
            if (ghost == null) continue;
            bool covered = false;

            foreach (Bar realBar in allRealBars)
            {
                if (usedBars.Contains(realBar) || realBar == null || !realBar.gameObject.activeInHierarchy ||
                    realBar.materialData != ghost.requiredMaterial || realBar.startPoint == null || realBar.endPoint == null)
                {
                    continue;
                }

                if (DoesPlacementMatchGhost(realBar.materialData, realBar.startPoint.transform.position,
                        realBar.endPoint.transform.position, ghost))
                {
                    covered = true;
                    usedBars.Add(realBar);
                    break;
                }
            }

            ghost.isCovered = covered;
            ghost.gameObject.SetActive(!covered);
            if (!covered) allGhostsCovered = false;
        }

        UpdateGhostPointVisibility();
        if (!allGhostsCovered) return;

        isTracingStep = false;
        RestoreTintedBar();

        if (activeGhosts.Length > 0 && activeGhosts[0] != null && activeGhosts[0].transform.parent != null)
            activeGhosts[0].transform.parent.gameObject.SetActive(false);

        if (TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
    }

    private void UpdateGhostPointVisibility()
    {
        if (activeGhostPoints == null) return;

        foreach (Transform ghostPoint in activeGhostPoints)
        {
            if (ghostPoint == null) continue;
            bool covered = false;

            foreach (Point point in Point.AllPoints)
            {
                if (point == null || !point.gameObject.activeSelf) continue;
                Vector2 ghostPosition = new Vector2(ghostPoint.position.x, ghostPoint.position.y);
                Vector2 pointPosition = new Vector2(point.transform.position.x, point.transform.position.y);
                if (Vector2.Distance(ghostPosition, pointPosition) < 0.5f)
                {
                    covered = true;
                    break;
                }
            }

            ghostPoint.gameObject.SetActive(!covered);
        }
    }

    private static Transform[] FindGhostPoints(GhostSegment[] ghosts)
    {
        if (ghosts == null || ghosts.Length == 0 || ghosts[0] == null || ghosts[0].transform.parent == null)
            return new Transform[0];

        List<Transform> points = new List<Transform>();
        foreach (Transform child in ghosts[0].transform.parent)
        {
            if (child.name.Contains("Ghost_Point")) points.Add(child);
        }
        return points.ToArray();
    }

    private void PointArrow(RectTransform target, Vector2 offset, float rotation)
    {
        TutorialPointer pointer = bouncingArrow;
        if (pointer == null && TutorialManager.Instance != null)
            pointer = TutorialManager.Instance.SharedPointer;

        if (pointer == null || target == null)
        {
            Debug.LogWarning("Cannot show the build tutorial pointer. Assign both the pointer and Undo Button Rect references.");
            return;
        }

        if (!target.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("Cannot point at Undo because its RectTransform is inactive in the hierarchy.");
            return;
        }

        pointer.PointAt(target, offset);
        pointer.transform.localEulerAngles = new Vector3(0, 0, rotation);
    }

    private void SetBarTint(Bar bar, bool red)
    {
        if (bar == null) return;
        if (bar != lastTintedBar)
        {
            RestoreTintedBar();
            lastTintedBar = bar;
        }

        foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>())
        {
            string property = renderer.material.HasProperty("_Color")
                ? "_Color"
                : renderer.material.HasProperty("_BaseColor") ? "_BaseColor" : null;
            if (property == null) continue;

            if (!originalColors.ContainsKey(renderer)) originalColors[renderer] = renderer.material.GetColor(property);
            renderer.material.SetColor(property, red ? new Color(1f, 0.2f, 0.2f, 1f) : originalColors[renderer]);
        }
    }

    private void RestoreTintedBar()
    {
        foreach (KeyValuePair<Renderer, Color> pair in originalColors)
        {
            if (pair.Key == null) continue;
            string property = pair.Key.material.HasProperty("_Color")
                ? "_Color"
                : pair.Key.material.HasProperty("_BaseColor") ? "_BaseColor" : null;
            if (property != null) pair.Key.material.SetColor(property, pair.Value);
        }

        originalColors.Clear();
        lastTintedBar = null;
    }

    public void EndTutorial()
    {
        isTutorialRunning = false;
        activeStepIndex = -1;
        isTracingStep = false;
        isCurrentDragValid = true;
        expectedMaterial = null;
        expectedTool = null;
        hasAdvancedFromRequiredClickThisStep = false;
        activeGhosts = null;
        activeGhostPoints = null;
        ClearTrackedToolButton();
        RestoreTintedBar();
        ClearUndoPrompt();

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.isTutorialUI_Locked = false;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
        }

        if (bouncingArrow != null) bouncingArrow.Hide();
        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(true);
    }
}
