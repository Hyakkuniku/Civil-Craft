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
    private sealed class TraceStepState
    {
        public int stepIndex;
        public GhostSegment[] ghosts;
        public Transform[] ghostPoints;
        public Transform parent;
        public bool completed;
        public readonly Dictionary<GhostSegment, Bar> coveringBars =
            new Dictionary<GhostSegment, Bar>();
    }

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
    public bool CanStartSimulation => !isTutorialRunning || simulationUnlockedForTutorial;

    private GhostSegment[] activeGhosts;
    private Transform[] activeGhostPoints;
    private int activeStepIndex = -1;
    private TraceStepState activeTraceState;
    private readonly Dictionary<int, TraceStepState> traceStatesByStep =
        new Dictionary<int, TraceStepState>();

    private BridgeMaterialSO expectedMaterial;
    private GameObject expectedTool;
    private bool hasAdvancedFromRequiredClickThisStep;
    private Button trackedToolButton;
    private UnityAction trackedToolButtonAction;
    private Coroutine undoPointerCoroutine;
    private bool simulationUnlockedForTutorial;

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
        activeTraceState = null;
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
        ClearTraceStepStates();
        isTutorialRunning = isBuildSequence;
        simulationUnlockedForTutorial = false;
        if (BuildUIController.Instance != null)
            BuildUIController.Instance.RefreshSimulationButtonLock();
        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(!isBuildSequence);
    }

    public void LockAllUI()
    {
        if (!isTutorialRunning)
            simulationUnlockedForTutorial = false;
        isTutorialRunning = true;

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.isTutorialUI_Locked = true;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
            BuildUIController.Instance.RefreshSimulationButtonLock();
        }

        if (bouncingArrow != null) bouncingArrow.Hide();
        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(false);
    }

    /// <summary>
    /// Assign this to a TutorialStep OnStepStart UnityEvent when simulation should
    /// become available. It remains available for the rest of the current sequence.
    /// </summary>
    public void UnlockSimulation()
    {
        simulationUnlockedForTutorial = true;
        if (BuildUIController.Instance != null)
            BuildUIController.Instance.RefreshSimulationButtonLock();
    }

    /// <summary>Allows a later tutorial step to explicitly lock simulation again.</summary>
    public void LockSimulation()
    {
        simulationUnlockedForTutorial = false;
        if (BuildUIController.Instance != null)
            BuildUIController.Instance.RefreshSimulationButtonLock();
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
        BeginTracingStep(null);
    }

    /// <summary>
    /// Preferred for sequences with multiple baked bridges. Assign the existing ghost
    /// parent GameObject in the TutorialStep UnityEvent so this step owns only that set.
    /// </summary>
    public void PromptDrawBridgeForContainer(GameObject ghostContainer)
    {
        BeginTracingStep(ghostContainer);
    }

    private void BeginTracingStep(GameObject ghostContainer)
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

        if (!traceStatesByStep.TryGetValue(activeStepIndex, out activeTraceState) ||
            activeTraceState == null)
        {
            if (ghostContainer != null)
            {
                ghostContainer.SetActive(true);
                MakeGhostContainerNonBlocking(ghostContainer.transform);
                activeGhosts = ghostContainer.GetComponentsInChildren<GhostSegment>(true);
            }
            else
            {
                activeGhosts = FindObjectsOfType<GhostSegment>(false);
            }

            activeGhostPoints = FindGhostPoints(activeGhosts);
            activeTraceState = new TraceStepState
            {
                stepIndex = activeStepIndex,
                ghosts = activeGhosts,
                ghostPoints = activeGhostPoints,
                parent = ghostContainer != null ? ghostContainer.transform : GetGhostParent(activeGhosts)
            };
            traceStatesByStep[activeStepIndex] = activeTraceState;
            MakeGhostContainerNonBlocking(activeTraceState.parent);
        }
        else
        {
            activeGhosts = activeTraceState.ghosts;
            activeGhostPoints = activeTraceState.ghostPoints;
            if (activeTraceState.parent != null)
            {
                activeTraceState.parent.gameObject.SetActive(true);
                MakeGhostContainerNonBlocking(activeTraceState.parent);
            }
        }

        activeTraceState.completed = false;
        ResetGhostVisualState(activeTraceState);

        if (activeGhosts == null || activeGhosts.Length == 0)
            Debug.LogWarning("Build tutorial tracing started, but no active GhostSegment objects were found.");
        else
            RefreshGhostCoverage(activeTraceState);
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
            if (activeTraceState != null)
                activeTraceState.coveringBars[matchedGhost] = bar;
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
        // Contract visibility and layout rebuilds can span more than one frame.
        // Retry briefly so the pointer is not lost when Undo was previously hidden.
        const int maxLayoutFrames = 5;
        for (int frame = 0; frame < maxLayoutFrames && IsAwaitingInvalidBarUndo; frame++)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();

            if (undoButtonRect != null && undoButtonRect.gameObject.activeInHierarchy &&
                PointArrow(undoButtonRect, undoArrowOffset, undoArrowRotation))
            {
                break;
            }

            if (BuildUIController.Instance != null)
                BuildUIController.Instance.SetToolForcedVisible(BuildModeTool.Undo, true);
        }

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

    /// <summary>Called by CommandManager after an Undo has changed the live bridge.</summary>
    public void OnHistoryActionUndone(HistoryAction undoneAction)
    {
        if (undoneAction == null) return;

        if (IsAwaitingInvalidBarUndo)
        {
            NotifyUndoCompleted();
            return;
        }

        OnBridgeHistoryChanged();
    }

    /// <summary>
    /// Called after Redo has restored an action. Rebuilt bars use the same strict,
    /// one-bar-per-ghost validation path as manually drawn and pasted bars.
    /// </summary>
    public void OnHistoryActionRedone(HistoryAction redoneAction)
    {
        if (redoneAction == null || IsAwaitingInvalidBarUndo) return;

        if (isTracingStep && redoneAction.isBuildEvent)
        {
            OnBuildActionCompleted(redoneAction);
            return;
        }

        OnBridgeHistoryChanged();
    }

    /// <summary>
    /// Re-evaluates ghost visibility after Undo, Redo, delete, move, or merge actions.
    /// If a completed trace became incomplete, the tutorial is returned to that trace step.
    /// </summary>
    public void OnBridgeHistoryChanged()
    {
        if (IsAwaitingInvalidBarUndo) return;

        TraceStepState earliestBrokenCompletedStep = null;
        List<TraceStepState> orderedStates = new List<TraceStepState>(traceStatesByStep.Values);
        orderedStates.Sort((a, b) => a.stepIndex.CompareTo(b.stepIndex));

        // Re-evaluate every bridge that this sequence has already completed. This is
        // essential when Undo removes a first-bridge bar while a later paste step is active.
        foreach (TraceStepState state in orderedStates)
        {
            if (state == null || !state.completed) continue;

            bool stillComplete = RefreshGhostCoverage(state);
            if (stillComplete)
            {
                if (state.parent != null) state.parent.gameObject.SetActive(false);
                continue;
            }

            state.completed = false;
            if (earliestBrokenCompletedStep == null)
                earliestBrokenCompletedStep = state;
        }

        if (earliestBrokenCompletedStep != null)
        {
            RestoreTraceStep(earliestBrokenCompletedStep, orderedStates);
            return;
        }

        if (isTracingStep && activeTraceState != null)
        {
            if (activeTraceState.parent != null)
                activeTraceState.parent.gameObject.SetActive(true);
            CheckGhostBridgeCompletion();
        }
    }

    private void RestoreTraceStep(TraceStepState targetState, List<TraceStepState> orderedStates)
    {
        if (targetState == null) return;

        // Later blueprint containers must not leak into the restored step.
        foreach (TraceStepState state in orderedStates)
        {
            if (state == null || state == targetState || state.stepIndex < targetState.stepIndex) continue;
            if (state.parent != null) state.parent.gameObject.SetActive(false);
        }

        if (targetState.parent != null)
        {
            targetState.parent.gameObject.SetActive(true);
            MakeGhostContainerNonBlocking(targetState.parent);
        }

        activeStepIndex = targetState.stepIndex;
        activeTraceState = targetState;
        activeGhosts = targetState.ghosts;
        activeGhostPoints = targetState.ghostPoints;

        TutorialManager tutorial = TutorialManager.Instance;
        bool returnedToTracingStep = tutorial != null &&
                                     tutorial.CurrentStepIndex > targetState.stepIndex &&
                                     tutorial.ReturnToStep(targetState.stepIndex);

        if (!returnedToTracingStep)
        {
            // Either the manager was already on this step or it was unavailable.
            isTracingStep = true;
            LockAllUI();
        }

        // ReturnToStep reruns PromptDrawBridge and reconnects activeTraceState. Reapply
        // coverage afterward so only the specific missing GhostSegments are visible.
        activeTraceState = targetState;
        activeGhosts = targetState.ghosts;
        activeGhostPoints = targetState.ghostPoints;
        isTracingStep = true;
        RefreshGhostCoverage(targetState);
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
        if (!isTracingStep || IsAwaitingInvalidBarUndo || activeTraceState == null ||
            activeGhosts == null || activeGhosts.Length == 0) return;

        bool allGhostsCovered = RefreshGhostCoverage(activeTraceState);
        if (!allGhostsCovered) return;

        activeTraceState.completed = true;

        isTracingStep = false;
        RestoreTintedBar();

        if (activeTraceState.parent != null)
            activeTraceState.parent.gameObject.SetActive(false);

        if (TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
    }

    private bool RefreshGhostCoverage(TraceStepState state)
    {
        if (state == null) return false;
        GhostSegment[] ghosts = state.ghosts;
        Transform[] ghostPoints = state.ghostPoints;
        if (ghosts == null || ghosts.Length == 0) return false;

        Bar[] allRealBars = FindObjectsOfType<Bar>();
        bool allGhostsCovered = true;
        HashSet<Bar> usedBars = new HashSet<Bar>();
        Dictionary<GhostSegment, Bar> previousCoverage =
            new Dictionary<GhostSegment, Bar>(state.coveringBars);
        state.coveringBars.Clear();

        foreach (GhostSegment ghost in ghosts)
        {
            if (ghost == null) continue;
            bool covered = false;

            if (previousCoverage.TryGetValue(ghost, out Bar previousBar) &&
                IsActiveBarMatch(previousBar, ghost) && !usedBars.Contains(previousBar))
            {
                covered = true;
                usedBars.Add(previousBar);
                state.coveringBars[ghost] = previousBar;
            }

            foreach (Bar realBar in allRealBars)
            {
                if (covered) break;
                if (usedBars.Contains(realBar) || !IsActiveBarMatch(realBar, ghost)) continue;

                covered = true;
                usedBars.Add(realBar);
                state.coveringBars[ghost] = realBar;
            }

            ghost.isCovered = covered;
            ghost.gameObject.SetActive(!covered);
            if (!covered) allGhostsCovered = false;
        }

        UpdateGhostPointVisibility(ghostPoints);
        return allGhostsCovered;
    }

    private bool IsActiveBarMatch(Bar bar, GhostSegment ghost)
    {
        return bar != null && ghost != null && bar.gameObject.activeInHierarchy &&
               bar.materialData == ghost.requiredMaterial && bar.startPoint != null && bar.endPoint != null &&
               DoesPlacementMatchGhost(bar.materialData, bar.startPoint.transform.position,
                   bar.endPoint.transform.position, ghost);
    }

    private void UpdateGhostPointVisibility(Transform[] ghostPoints)
    {
        if (ghostPoints == null) return;

        foreach (Transform ghostPoint in ghostPoints)
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

    private static Transform GetGhostParent(GhostSegment[] ghosts)
    {
        if (ghosts == null) return null;
        foreach (GhostSegment ghost in ghosts)
        {
            if (ghost != null) return ghost.transform.parent;
        }
        return null;
    }

    private static void MakeGhostContainerNonBlocking(Transform ghostParent)
    {
        if (ghostParent == null) return;

        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        foreach (Transform child in ghostParent.GetComponentsInChildren<Transform>(true))
        {
            if (ignoreRaycastLayer >= 0) child.gameObject.layer = ignoreRaycastLayer;

            foreach (Collider collider in child.GetComponents<Collider>())
                collider.enabled = false;
            foreach (Collider2D collider in child.GetComponents<Collider2D>())
                collider.enabled = false;
            foreach (Graphic graphic in child.GetComponents<Graphic>())
                graphic.raycastTarget = false;
        }
    }

    private void ClearTraceStepStates()
    {
        // Restore every segment before discarding the step-to-container mapping.
        // Parent containers stay hidden until their own tutorial step starts again.
        ResetAllTrackedGhostVisuals();
        traceStatesByStep.Clear();
        activeTraceState = null;
    }

    /// <summary>
    /// Restores all baked ghost visuals in this scene after a tutorial failure.
    /// Each container is then hidden so only the matching TutorialStep can reveal it.
    /// This also catches ghost steps whose runtime tracking was already cleared when
    /// the final Play-button step completed before physics reported the failure.
    /// </summary>
    public void PrepareGhostsForTutorialRestart()
    {
        ResetAllTrackedGhostVisuals();

        HashSet<Transform> ghostContainers = new HashSet<Transform>();
        foreach (GhostSegment ghost in Resources.FindObjectsOfTypeAll<GhostSegment>())
        {
            if (ghost == null || ghost.gameObject.scene != gameObject.scene) continue;

            ghost.isCovered = false;
            ghost.gameObject.SetActive(true);

            Transform container = ghost.transform.parent;
            if (container != null) ghostContainers.Add(container);
        }

        foreach (Transform container in ghostContainers)
        {
            if (container == null) continue;

            foreach (Transform child in container)
            {
                if (child != null && child.name.Contains("Ghost_Point"))
                    child.gameObject.SetActive(true);
            }

            container.gameObject.SetActive(false);
        }

        traceStatesByStep.Clear();
        activeTraceState = null;
        activeGhosts = null;
        activeGhostPoints = null;
    }

    private static void ResetGhostVisualState(TraceStepState state)
    {
        if (state == null) return;

        state.completed = false;
        state.coveringBars.Clear();

        if (state.ghosts != null)
        {
            foreach (GhostSegment ghost in state.ghosts)
            {
                if (ghost == null) continue;
                ghost.isCovered = false;
                ghost.gameObject.SetActive(true);
            }
        }

        if (state.ghostPoints != null)
        {
            foreach (Transform ghostPoint in state.ghostPoints)
            {
                if (ghostPoint != null) ghostPoint.gameObject.SetActive(true);
            }
        }
    }

    private void ResetAllTrackedGhostVisuals()
    {
        foreach (TraceStepState state in traceStatesByStep.Values)
        {
            ResetGhostVisualState(state);
            if (state != null && state.parent != null)
                state.parent.gameObject.SetActive(false);
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

    private bool PointArrow(RectTransform target, Vector2 offset, float rotation)
    {
        TutorialPointer pointer = bouncingArrow;
        if (pointer == null && TutorialManager.Instance != null)
            pointer = TutorialManager.Instance.SharedPointer;

        if (pointer == null || target == null)
        {
            Debug.LogWarning("Cannot show the build tutorial pointer. Assign both the pointer and Undo Button Rect references.");
            return false;
        }

        if (!target.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("Cannot point at Undo because its RectTransform is inactive in the hierarchy.");
            return false;
        }

        pointer.PointAt(target, offset);
        pointer.transform.localEulerAngles = new Vector3(0, 0, rotation);
        return pointer.gameObject.activeInHierarchy;
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
        // A sequence can finish when the player clicks Play, before physics has
        // confirmed success. Keep every blueprint ready in case failure restarts it.
        // Parent containers are hidden and enabled again only by their own step.
        ResetAllTrackedGhostVisuals();

        isTutorialRunning = false;
        simulationUnlockedForTutorial = false;
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
        ClearTraceStepStates();

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.isTutorialUI_Locked = false;
            BuildUIController.Instance.whitelistedMaterial = null;
            BuildUIController.Instance.whitelistedButton = null;
            BuildUIController.Instance.RefreshSimulationButtonLock();
        }

        if (bouncingArrow != null) bouncingArrow.Hide();
        if (exitBuildModeButton != null) exitBuildModeButton.SetActive(true);
    }
}
