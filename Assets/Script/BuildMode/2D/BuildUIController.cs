using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using System.Collections.Generic;
using System.Collections; 
using UnityEngine.InputSystem;

[System.Serializable]
public class BuildToolUIBinding
{
    public BuildModeTool tool;
    [Tooltip("Assign the button's outer wrapper so hiding it also removes its layout space.")]
    public GameObject uiObject;
}

public class BuildUIController : MonoBehaviour
{
    public static BuildUIController Instance { get; private set; }

    [Header("Tutorial System Locks")]
    [HideInInspector] public bool isTutorialUI_Locked = false;
    [HideInInspector] public BridgeMaterialSO whitelistedMaterial = null;
    [HideInInspector] public GameObject whitelistedButton = null;

    [Header("Action Log")]
    public TextMeshProUGUI actionLogText; 
    public float logDisplayTime = 3f;
    [SerializeField, Min(0f)] private float actionLogSafeAreaMarginPixels = 12f;
    [Tooltip("Pixel gap between Action Logs and the tutorial banner or other reserved UI.")]
    [SerializeField, Min(0f)] private float actionLogPanelGapPixels = 12f;
    [Tooltip("Optional additional UI controls whose actual bounds Action Logs must avoid.")]
    [SerializeField] private List<RectTransform> actionLogProtectedRegions = new List<RectTransform>();
    private RectTransform actionLogRect;
    private Vector2 authoredActionLogPosition;
    private Vector2 authoredActionLogSize;
    private Vector2 preferredActionLogScreenCenter;
    private string currentActionLog;
    private float currentActionLogRemaining;
    private bool actionLogPrepared;
    private bool actionLogVisible;
    private bool actionLogInitialized;
    private int lastActionLogScreenWidth = -1;
    private int lastActionLogScreenHeight = -1;
    private Rect lastActionLogSafeArea;
    private float lastActionLogCanvasScale = -1f;

    [Header("System References")]
    public BarCreator barCreator;
    public BridgePhysicsManager physicsManager;

    [Header("Build UI Audio")]
    [Tooltip("SFX ID configured in AudioManager for build UI button presses.")]
    [SerializeField] private string buildButtonClickSfxId = "Click";

    [Header("Global Keyboard Shortcuts")]
    public bool useKeyboardShortcuts = true;
    public KeyCode simulateKey = KeyCode.Return;   
    public KeyCode restartKey = KeyCode.Backspace; 

    [Header("Play/Pause Button UI")]
    [Tooltip("Optional explicit reference. If empty, the button is found from Play Pause Button Image.")]
    public Button simulationButton;
    public Image playPauseButtonImage; 
    public Sprite playIcon;            
    public Sprite stopIcon;            
    [Tooltip("Optional standalone Simulation Off/Pause button or its layout wrapper. It is hidden for tutorial contracts. Leave empty when Play/Stop share one button.")]
    public GameObject tutorialSimulationStopObject;

    [Header("Simulation Panel Visibility")]
    [Tooltip("Background/container that should disappear when neither simulation control is usable.")]
    public GameObject simulationControlsPanel;
    [Tooltip("Play button or its layout wrapper. Defaults to Simulation Button when empty.")]
    public GameObject playSimulationButtonObject;
    [Tooltip("Stop/Pause button or its layout wrapper. Can be the same reference as Tutorial Simulation Stop Object.")]
    public GameObject stopSimulationButtonObject;

    private readonly List<Button> simulationPanelButtons = new List<Button>(4);
    private readonly List<Button> simulationControlButtons = new List<Button>(4);

    [Header("Contract Info (Budget)")]
    public float fallbackMaxBudget = 1000f; 
    [HideInInspector] public float maxBudget = 1000f; 
    
    public TextMeshProUGUI usedBudgetText; 
    public Image budgetFillBar; 
    public TextMeshProUGUI maxBudgetText; 
    
    public Color normalTextColor = Color.white;
    public Color overBudgetTextColor = Color.red;

    [Header("Budget Remaining Colors")]
    [Tooltip("Used while more than half of the contract budget remains.")]
    public Color safeBudgetColor = new Color32(80, 166, 105, 255);
    [Tooltip("Used while 25% to 50% of the contract budget remains.")]
    public Color warningBudgetColor = new Color32(225, 174, 65, 255);
    [Tooltip("Used while less than 25% of the contract budget remains.")]
    public Color criticalBudgetColor = new Color32(218, 119, 54, 255);
    [Tooltip("Used when no budget remains or the bridge is over budget.")]
    public Color emptyBudgetColor = new Color32(190, 72, 62, 255);
    [Range(0f, 1f)] public float budgetWarningThreshold = 0.5f;
    [Range(0f, 1f)] public float budgetCriticalThreshold = 0.25f;
    [Tooltip("Horizontal space between the colored fill and its background track.")]
    [Min(0f)] public float budgetFillHorizontalInset = 7f;
    [Tooltip("Height of the colored fill inside the background track.")]
    [Min(8f)] public float budgetFillHeight = 30f;
    [Tooltip("How quickly the remaining-budget bar catches up to cost changes.")]
    [Min(0.1f)] public float budgetFillAnimationSpeed = 5f;

    [Header("Stress Visualization")]
    public TextMeshProUGUI stressText;
    public Image stressFillBar;
    public Color safeStressColor = Color.green;
    public Color warningStressColor = Color.yellow;
    public Color criticalStressColor = Color.red;
    [Tooltip("How quickly the stress gauge rises and falls, in full-gauge units per second. Visual only; bridge stress and failure timing are unchanged.")]
    [Min(0.1f)] public float stressFillAnimationSpeed = 2.5f;
    [Tooltip("Numeric stress-label refresh rate. The fill bar still animates every rendered frame.")]
    [Range(5f, 30f)] public float stressTextUpdatesPerSecond = 10f;

    [Header("Universal Timer UI (Time Attack & Hold)")]
    public GameObject timerPanel; 
    public TextMeshProUGUI timerText; 

    [Header("Engineering Stats (CAD Readout)")]
    public GameObject statsPanel; 
    [SerializeField, Min(0.05f)] private float statsOpenDuration = 0.22f;
    [SerializeField, Min(0.05f)] private float statsCloseDuration = 0.16f;
    public TextMeshProUGUI totalLengthText; 
    public TextMeshProUGUI membersCountText;  
    public TextMeshProUGUI deadLoadText;  
    public TextMeshProUGUI targetCargoWeightText; 
    public TextMeshProUGUI estimatedCapacityText;
    public TextMeshProUGUI efficiencyRatioText;
    public TextMeshProUGUI factorOfSafetyText; 
    private RectTransform statsPanelRect;
    private CanvasGroup statsPanelCanvasGroup;
    private Vector2 statsPanelRestPosition;
    private Vector3 statsPanelRestScale;
    private Coroutine statsPanelAnimation;
    private float statsPanelProgress;
    private bool statsPanelVisible;

    [Header("Selection UI")]
    public GameObject selectionActionPanel; 

    [Header("Live Beam Stats (Drawing/Moving Readout)")]
    public GameObject liveBeamStatsPanel; 
    public TextMeshProUGUI liveBeamLengthText;
    public TextMeshProUGUI liveBeamCostText;
    public TextMeshProUGUI liveBeamAngleText;

    [Header("Unlock Material UI")]
    public GameObject unlockMaterialPanel; 
    public TextMeshProUGUI unlockMaterialText; 
    private MaterialButtonTrigger pendingUnlockButton;

    [Header("Material Panel Visibility")]
    [Tooltip("Optional material-button scroll panel. If empty, it is found from the material buttons' ScrollRect.")]
    [SerializeField] private GameObject materialsPanel;
    private MaterialButtonTrigger[] materialButtons = System.Array.Empty<MaterialButtonTrigger>();
    private bool materialsPanelHiddenForEmpty;

    [Header("Tool Highlighting")]
    public Image selectToolImage;
    public Image moveToolImage;
    public Image deleteToolImage;
    public Image gridToolImage;
    public Image infoToolImage;
    
    public Color activeToolColor = new Color(0.902f, 0.737f, 0.463f, 1f); 
    public Color inactiveToolColor = Color.white;

    [Header("Contract Tool Visibility")]
    [Tooltip("Map every hideable tool enum to its outermost UI button/wrapper.")]
    public List<BuildToolUIBinding> contractToolBindings = new List<BuildToolUIBinding>();
    private readonly HashSet<BuildModeTool> forcedVisibleTools = new HashSet<BuildModeTool>();
    private readonly HashSet<GameObject> contractHiddenToolObjects = new HashSet<GameObject>();

    [Header("Automatic Tools Panel Sizing")]
    [Tooltip("Usually the RectTransform that also has your Horizontal/Vertical Layout Group.")]
    public RectTransform toolsPanelToAutoSize;
    public bool autoSizeToolsPanelWidth = true;
    public bool autoSizeToolsPanelHeight = false;
    public Vector2 toolsPanelExtraPadding = Vector2.zero;
    private Coroutine resizeToolsPanelCoroutine;
    private RectTransform EffectiveToolsPanel => toolsPanelToAutoSize != null
        ? toolsPanelToAutoSize
        : layoutPanelToRebuild;

    [Header("Simulation UI Hiding")]
    public List<GameObject> hideDuringSimulation = new List<GameObject>();
    public RectTransform layoutPanelToRebuild; 
    [Tooltip("Optional outer wrapper for the mobile Back/Exit button. It is found automatically when empty.")]
    [SerializeField] private GameObject exitBuildModeButtonObject;

    private List<GameObject> temporarilyHiddenSimUI = new List<GameObject>();
    private bool simulationInProgressForUI;

    private float cachedBaseCost = 0f;
    private float cachedBaseDeadLoad = 0f;
    private int cachedBaseM = 0;
    private int cachedBaseJ = 0;
    private float cachedBaseRoadLength = 0f;
    private float cachedEstimatedCapacityKg = 0f;
    private Coroutine capacityEstimateCoroutine;
    private bool capacityEstimatePending;
    private bool lastCapacityEstimatePending;

    private HashSet<Bar> uniqueBars = new HashSet<Bar>();
    private HashSet<Point> activePoints = new HashSet<Point>();

    private float lastStressPercent = -1f;
    private float lastStressFillAmount = -1f;
    private static readonly Color StressReadoutColor = new Color(0.973f, 0.918f, 0.843f, 1f);
    private float nextStressTextUpdateTime;
    private int lastProjectedCost = -1;
    private int lastDisplayedMaxBudget = -1;
    private float lastRoadLength = -1f;
    private float lastDeadLoad = -1f;
    private float lastLiveLoad = -1f;
    private float lastEstimatedCapacity = -1f;
    private float lastEfficiencyRatio = -1f;
    private float lastEstimatedFoS = -1f;
    private int lastDisplayM = -1;
    private int lastDisplayJ = -1;
    private RectTransform budgetFillRect;
    private float targetBudgetRemainingRatio = 1f;
    private float displayedBudgetRemainingRatio = 1f;
    private Color targetBudgetBarColor;
    private Color displayedBudgetBarColor;
    private bool budgetBarVisualInitialized;
    private Vector2 lastBudgetTrackSize = new Vector2(-1f, -1f);
    private float nextSimulationPanelVisibilityCheck;
    private string lastTimerPrefix;
    private int lastTimerMinutes = -1;
    private int lastTimerSeconds = -1;

    private Dictionary<BridgeMaterialSO, int> materialUsageCount = new Dictionary<BridgeMaterialSO, int>();
    private readonly HashSet<Bar> liveBeamAffectedBars = new HashSet<Bar>();
    private readonly HashSet<Point> liveBeamSelectedPoints = new HashSet<Point>();
    private Bar lastLiveBeamBar;
    private int lastLiveBeamLengthHundredths = int.MinValue;
    private int lastLiveBeamCost = int.MinValue;
    private int lastLiveBeamAngleTenths = int.MinValue;

    private void Awake() { Instance = this; }

    private void OnEnable()
    {
        UIReservedRegionLayout.LayoutChanging += HideActionLogTemporarily;
        RegisterActionLogProtectedRegions();
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
            RefreshContractBuildUI();
    }

    private void OnDisable()
    {
        CancelCapacityEstimate();
        UIReservedRegionLayout.LayoutChanging -= HideActionLogTemporarily;
        UnregisterActionLogProtectedRegions();
        HideActionLogTemporarily();
        ResetStatsPanel();
    }

    private void Start()
    {
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();
        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();

        // Keep the readout legible over both the dark empty card and bright stress colors.
        if (stressText != null)
        {
            stressText.outlineColor = new Color32(35, 23, 17, 230);
            stressText.outlineWidth = 0.12f;
        }
        
        if (selectionActionPanel != null) selectionActionPanel.SetActive(false);
        InitializeStatsPanel();
        if (liveBeamStatsPanel != null) liveBeamStatsPanel.SetActive(false);
        if (timerPanel != null) timerPanel.SetActive(false); 
        if (unlockMaterialPanel != null) unlockMaterialPanel.SetActive(false); 

        if (budgetFillBar != null)
        {
            // The authored fill used a 3D White material, which ignores UI vertex
            // tinting. Use the default UI material so budget state colors render.
            budgetFillBar.material = null;
            budgetFillBar.type = Image.Type.Sliced;
            budgetFillBar.pixelsPerUnitMultiplier = 2f;
            budgetFillBar.raycastTarget = false;

            // Width-driven slicing preserves the sprite's rounded corners. Unity's
            // Filled mode ignores nine-slice borders and produced the pointed oval.
            budgetFillRect = budgetFillBar.rectTransform;
            budgetFillRect.anchorMin = new Vector2(0f, 0.5f);
            budgetFillRect.anchorMax = new Vector2(0f, 0.5f);
            budgetFillRect.pivot = new Vector2(0f, 0.5f);

            Shadow fillShadow = budgetFillBar.GetComponent<Shadow>();
            if (fillShadow == null) fillShadow = budgetFillBar.gameObject.AddComponent<Shadow>();
            fillShadow.effectColor = new Color(0.08f, 0.10f, 0.20f, 0.25f);
            fillShadow.effectDistance = new Vector2(0f, -2f);
            fillShadow.useGraphicAlpha = true;
        }

        InitializeActionLog();
        RegisterActionLogProtectedRegions();

        MarkBridgeDirty();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(RefreshContractBuildUI);
        }

        if (physicsManager != null)
        {
            physicsManager.OnSettlePhaseStarted += HandleSimulationBegan;
            physicsManager.OnSimulationStopped += HandleSimulationEnded;
        }

        RefreshSimulationButtonLock();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(RefreshContractBuildUI);
        }

        if (physicsManager != null)
        {
            physicsManager.OnSettlePhaseStarted -= HandleSimulationBegan;
            physicsManager.OnSimulationStopped -= HandleSimulationEnded;
        }
    }

    private void HandleSimulationBegan()
    {
        simulationInProgressForUI = true;
        RefreshSimulationButtonLock();
        temporarilyHiddenSimUI.Clear();

        foreach (GameObject ui in hideDuringSimulation)
        {
            if (ui != null && ui.activeSelf)
            {
                temporarilyHiddenSimUI.Add(ui);
                ui.SetActive(false);
            }
        }

        HideExitBuildModeButtonDuringSimulation();
        
        if (layoutPanelToRebuild != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutPanelToRebuild);
        }
        
        BuildCameraController cam = FindObjectOfType<BuildCameraController>();
        if (cam != null) cam.GoToSimulationView();
    }

    private void HandleSimulationEnded()
    {
        simulationInProgressForUI = false;
        foreach (GameObject ui in temporarilyHiddenSimUI)
        {
            if (ui != null) ui.SetActive(true);
        }
        
        temporarilyHiddenSimUI.Clear();
        
        if (layoutPanelToRebuild != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutPanelToRebuild);
        }
        
        BuildCameraController cam = FindObjectOfType<BuildCameraController>();
        if (cam != null) cam.ReturnToBuildView();
        RefreshSimulationButtonLock();
        RefreshMaterialPanelVisibility();
    }

    private void HideExitBuildModeButtonDuringSimulation()
    {
        ResolveExitBuildModeButton();
        if (exitBuildModeButtonObject == null || !exitBuildModeButtonObject.activeSelf)
            return;

        if (!temporarilyHiddenSimUI.Contains(exitBuildModeButtonObject))
            temporarilyHiddenSimUI.Add(exitBuildModeButtonObject);
        exitBuildModeButtonObject.SetActive(false);
    }

    private void ResolveExitBuildModeButton()
    {
        if (exitBuildModeButtonObject != null) return;

        GameObject inactiveFallback = null;
        foreach (Button button in FindObjectsOfType<Button>(true))
        {
            if (button == null || !button.gameObject.scene.IsValid()) continue;

            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                string method = button.onClick.GetPersistentMethodName(i);
                if (method != nameof(GameManager.ExitBuildMode) &&
                    method != nameof(OnExitBuildModeButtonClicked))
                {
                    continue;
                }

                if (button.gameObject.activeInHierarchy)
                {
                    exitBuildModeButtonObject = button.gameObject;
                    return;
                }

                if (inactiveFallback == null)
                    inactiveFallback = button.gameObject;
            }
        }

        exitBuildModeButtonObject = inactiveFallback;
    }

    public void RefreshAllMaterialButtons()
    {
        materialButtons = FindObjectsOfType<MaterialButtonTrigger>(true);
        foreach (var b in materialButtons)
        {
            b.EvaluateMaterialRestriction();
        }
        RefreshMaterialPanelVisibility();
    }

    private void RefreshMaterialPanelVisibility()
    {
        if (simulationInProgressForUI) return;

        if (materialsPanel == null)
        {
            foreach (MaterialButtonTrigger button in materialButtons)
            {
                if (button == null) continue;
                ScrollRect scroll = button.GetComponentInParent<ScrollRect>(true);
                if (scroll == null || scroll.name != "MaterialsScrollPanel") continue;
                materialsPanel = scroll.gameObject;
                UIReservedRegionLayout.Register(materialsPanel.transform as RectTransform);
                break;
            }
        }
        if (materialsPanel == null) return;

        bool hasVisibleButton = false;
        Transform panelTransform = materialsPanel.transform;
        foreach (MaterialButtonTrigger button in materialButtons)
        {
            if (button == null || !button.gameObject.activeSelf ||
                !button.transform.IsChildOf(panelTransform)) continue;
            Transform wrapper = button.parentWrapper != null
                ? button.parentWrapper.transform : button.transform;
            if (IsVisibleInsidePanel(wrapper, panelTransform))
            {
                hasVisibleButton = true;
                break;
            }
        }

        if (!hasVisibleButton && materialsPanel.activeSelf)
        {
            materialsPanel.SetActive(false);
            materialsPanelHiddenForEmpty = true;
        }
        else if (hasVisibleButton && materialsPanelHiddenForEmpty)
        {
            materialsPanel.SetActive(true);
            materialsPanelHiddenForEmpty = false;
        }
    }

    private static bool IsVisibleInsidePanel(Transform target, Transform panel)
    {
        for (Transform current = target; current != null && current != panel; current = current.parent)
            if (!current.gameObject.activeSelf) return false;
        return target != null && target.IsChildOf(panel);
    }

    public void ShowTimer(bool isVisible)
    {
        if (timerPanel != null && timerPanel.activeSelf != isVisible)
            timerPanel.SetActive(isVisible);
    }

    public void UpdateTimerText(string prefix, float timeInSeconds)
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(timeInSeconds / 60F);
            int seconds = Mathf.FloorToInt(timeInSeconds - minutes * 60);
            if (prefix == lastTimerPrefix && minutes == lastTimerMinutes &&
                seconds == lastTimerSeconds) return;
            lastTimerPrefix = prefix;
            lastTimerMinutes = minutes;
            lastTimerSeconds = seconds;
            timerText.text = $"{prefix}<color=red>{minutes:00}:{seconds:00}</color>";
        }
    }

    private void Update()
    {
        if (useKeyboardShortcuts)
        {
            if (WasKeyPressedThisFrame(simulateKey)) OnSimulateButtonClicked();
            if (WasKeyPressedThisFrame(restartKey)) OnRestartButtonClicked();
        }

        if (physicsManager != null && physicsManager.isSimulating)
        {
            UpdateStressUI();
        }
        else
        {
            UpdateStressUI(); 

            if (barCreator != null && barCreator.IsCreating)
            {
                if (statsPanelVisible && statsPanel != null && statsPanel.activeInHierarchy)
                    UpdateStatsUI();
                UpdateContractUI();
            }
        }
        
        UpdateLiveBeamStatsUI();
        UpdatePlayPauseButtonUI();
        UpdateBudgetBarVisual();
        
        UpdateToolHighlights();
    }

    private void LateUpdate()
    {
        UpdateActionLog();
        // Scene-authored tutorial controls can change visibility outside this
        // controller, but scanning every Button each frame allocates on mobile.
        if (Time.unscaledTime < nextSimulationPanelVisibilityCheck) return;
        nextSimulationPanelVisibilityCheck = Time.unscaledTime + 0.1f;
        RefreshSimulationPanelVisibility();
        RefreshMaterialPanelVisibility();
    }

    public void RefreshContractBuildUI()
    {
        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;

        // A newly selected build location can have the same spent cost as the
        // previous one. Force the contract-specific values to refresh anyway.
        lastDisplayedMaxBudget = -1;

        foreach (GameObject previouslyHiddenObject in contractHiddenToolObjects)
        {
            if (previouslyHiddenObject != null) previouslyHiddenObject.SetActive(true);
        }
        contractHiddenToolObjects.Clear();

        foreach (BuildModeTool tool in System.Enum.GetValues(typeof(BuildModeTool)))
        {
            GameObject toolObject = FindToolUIObject(tool);
            if (toolObject == null) continue;

            bool shouldHide = contract != null && contract.IsToolHidden(tool) &&
                              !forcedVisibleTools.Contains(tool);
            toolObject.SetActive(!shouldHide);
            if (shouldHide) contractHiddenToolObjects.Add(toolObject);
        }

        // Rebuild against the location that GameManager has just activated.
        // The controller may have started earlier while no location was active.
        MarkBridgeDirty();
        if (layoutPanelToRebuild != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutPanelToRebuild);
        ScheduleToolsPanelResize();
        RefreshSimulationButtonLock();
    }

    public void SetToolForcedVisible(BuildModeTool tool, bool forceVisible)
    {
        if (forceVisible) forcedVisibleTools.Add(tool);
        else forcedVisibleTools.Remove(tool);
        RefreshContractBuildUI();
    }

    /// <summary>
    /// Returns the mapped button/wrapper for tutorial pointers. The same lookup
    /// used by contract visibility is used so a missing explicit pointer target
    /// cannot silently break the Undo prompt.
    /// </summary>
    public RectTransform GetToolRectTransform(BuildModeTool tool)
    {
        GameObject toolObject = FindToolUIObject(tool);
        return toolObject != null ? toolObject.GetComponent<RectTransform>() : null;
    }

    private GameObject FindToolUIObject(BuildModeTool tool)
    {
        foreach (BuildToolUIBinding binding in contractToolBindings)
        {
            if (binding != null && binding.tool == tool && binding.uiObject != null)
                return binding.uiObject;
        }

        string handlerName = GetToolHandlerName(tool);
        if (string.IsNullOrEmpty(handlerName)) return null;

        foreach (Button button in FindObjectsOfType<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == handlerName)
                {
                    Transform layoutItem = button.transform;
                    RectTransform toolsPanel = EffectiveToolsPanel;
                    if (toolsPanel != null && layoutItem.IsChildOf(toolsPanel))
                    {
                        while (layoutItem.parent != null && layoutItem.parent != toolsPanel)
                            layoutItem = layoutItem.parent;
                    }
                    return layoutItem.gameObject;
                }
            }
        }

        return null;
    }

    private static string GetToolHandlerName(BuildModeTool tool)
    {
        switch (tool)
        {
            case BuildModeTool.Select: return nameof(OnToggleSelectModeButtonClicked);
            case BuildModeTool.Move: return nameof(OnToggleMoveModeButtonClicked);
            case BuildModeTool.Delete: return nameof(OnToggleDeleteModeButtonClicked);
            case BuildModeTool.Grid: return nameof(OnToggleGridButtonClicked);
            case BuildModeTool.CancelDrawing: return nameof(OnCancelDrawingButtonClicked);
            case BuildModeTool.ExitBuildMode: return nameof(OnExitBuildModeButtonClicked);
            case BuildModeTool.ResetCamera: return nameof(OnResetCameraButtonClicked);
            case BuildModeTool.Statistics: return nameof(OnToggleStatsButtonClicked);
            case BuildModeTool.Cut: return nameof(OnCutSelectedButtonClicked);
            case BuildModeTool.Copy: return nameof(OnCopyButtonClicked);
            case BuildModeTool.Paste: return nameof(OnPasteButtonClicked);
            case BuildModeTool.Undo: return nameof(OnUndoButtonClicked);
            case BuildModeTool.Redo: return nameof(OnRedoButtonClicked);
            case BuildModeTool.DeleteSelected: return nameof(OnDeleteSelectedButtonClicked);
            case BuildModeTool.Simulate: return nameof(OnToggleSimulationButtonClicked);
            default: return null;
        }
    }

    private void ScheduleToolsPanelResize()
    {
        if (EffectiveToolsPanel == null) return;
        if (resizeToolsPanelCoroutine != null) StopCoroutine(resizeToolsPanelCoroutine);
        resizeToolsPanelCoroutine = StartCoroutine(ResizeToolsPanelAfterLayout());
    }

    private IEnumerator ResizeToolsPanelAfterLayout()
    {
        yield return null;
        RectTransform toolsPanel = EffectiveToolsPanel;
        if (toolsPanel == null)
        {
            resizeToolsPanelCoroutine = null;
            yield break;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(toolsPanel);

        // A ContentSizeFitter is the preferred setup and already applies the
        // LayoutGroup's preferred size after the rebuild above.
        ContentSizeFitter fitter = toolsPanel.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            bool foundVisibleChild = false;
            Bounds combinedBounds = new Bounds();

            foreach (Transform child in toolsPanel)
            {
                if (!child.gameObject.activeSelf) continue;
                RectTransform childRect = child as RectTransform;
                if (childRect == null) continue;

                Bounds childBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    toolsPanel, childRect);

                if (!foundVisibleChild)
                {
                    combinedBounds = childBounds;
                    foundVisibleChild = true;
                }
                else
                {
                    combinedBounds.Encapsulate(childBounds.min);
                    combinedBounds.Encapsulate(childBounds.max);
                }
            }

            Vector2 desiredSize = foundVisibleChild
                ? new Vector2(combinedBounds.size.x, combinedBounds.size.y) + toolsPanelExtraPadding
                : toolsPanelExtraPadding;

            if (autoSizeToolsPanelWidth)
                toolsPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, desiredSize.x);
            if (autoSizeToolsPanelHeight)
                toolsPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, desiredSize.y);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(toolsPanel);
        if (layoutPanelToRebuild != null && layoutPanelToRebuild != toolsPanel)
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutPanelToRebuild);
        resizeToolsPanelCoroutine = null;
    }

    private static bool WasKeyPressedThisFrame(KeyCode keyCode)
    {
        if (Keyboard.current == null) return false;

        string keyName = keyCode == KeyCode.Return ? nameof(Key.Enter) : keyCode.ToString();
        return System.Enum.TryParse(keyName, out Key key) && key != Key.None && Keyboard.current[key].wasPressedThisFrame;
    }

    private void UpdateToolHighlights()
    {
        if (barCreator != null)
        {
            if (selectToolImage != null)
                selectToolImage.color = barCreator.IsSelecting ? activeToolColor : inactiveToolColor;

            if (moveToolImage != null)
                moveToolImage.color = barCreator.IsMoving ? activeToolColor : inactiveToolColor;

            if (deleteToolImage != null)
                deleteToolImage.color = barCreator.isDeleteMode ? activeToolColor : inactiveToolColor;

            if (gridToolImage != null)
                gridToolImage.color = barCreator.isGridSnappingEnabled ? activeToolColor : inactiveToolColor;
        }

        if (infoToolImage != null)
        {
            infoToolImage.color = statsPanelVisible ? activeToolColor : inactiveToolColor;
        }
    }

    public int GetMaterialUsageCount(BridgeMaterialSO material)
    {
        if (materialUsageCount.ContainsKey(material)) return materialUsageCount[material];
        return 0;
    }

    public void PromptUnlockMaterial(MaterialButtonTrigger btn)
    {
        pendingUnlockButton = btn;
        if (unlockMaterialPanel != null && btn != null)
        {
            unlockMaterialPanel.SetActive(true);
            int cost = btn.buttonMaterial.unlockCost;
            if (unlockMaterialText != null) unlockMaterialText.text = $"Unlock {btn.buttonMaterial.name} for this level?\nCost: {cost} Gold";
        }
    }

    public void ConfirmUnlockMaterial()
    {
        if (pendingUnlockButton != null && GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            int cost = pendingUnlockButton.buttonMaterial.unlockCost;
            
            if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.gold >= cost)
            {
                PlayerDataManager.Instance.SpendGold(cost);
                PlayerDataManager.Instance.UnlockMaterialForContract(
                    GameManager.Instance.CurrentContract.ContractID,
                    pendingUnlockButton.buttonMaterial.name);

                RefreshAllMaterialButtons(); 

                LogAction($"{pendingUnlockButton.buttonMaterial.name} Unlocked!");
            }
            else LogAction("Not enough Gold to unlock!");
        }

        if (unlockMaterialPanel != null) unlockMaterialPanel.SetActive(false);
        pendingUnlockButton = null;
    }

    public void CancelUnlockMaterial()
    {
        if (unlockMaterialPanel != null) unlockMaterialPanel.SetActive(false);
        pendingUnlockButton = null;
    }

    private void UpdateLiveBeamStatsUI()
    {
        Bar targetBar = null;

        if (barCreator != null)
        {
            if (barCreator.IsCreating && barCreator.currentBar != null) targetBar = barCreator.currentBar;
            else if (barCreator.IsMoving && barCreator.isDraggingSelection)
            {
                // GetSelectedPoints creates a HashSet and List; the HUD checks
                // this on every move frame, so reuse one set instead.
                liveBeamSelectedPoints.Clear();
                foreach (Point point in barCreator.selectedPoints)
                    if (point != null && !point.IsScenePlacedAnchor)
                        liveBeamSelectedPoints.Add(point);
                foreach (Bar selectedBar in barCreator.selectedBars)
                {
                    if (selectedBar == null) continue;
                    if (selectedBar.startPoint != null && !selectedBar.startPoint.IsScenePlacedAnchor)
                        liveBeamSelectedPoints.Add(selectedBar.startPoint);
                    if (selectedBar.endPoint != null && !selectedBar.endPoint.IsScenePlacedAnchor)
                        liveBeamSelectedPoints.Add(selectedBar.endPoint);
                }
                liveBeamAffectedBars.Clear();
                foreach (Point p in liveBeamSelectedPoints)
                {
                    foreach (Bar b in p.ConnectedBars)
                        if (b != null && b.gameObject.activeSelf) liveBeamAffectedBars.Add(b);
                }
                if (liveBeamAffectedBars.Count == 1)
                {
                    var enumerator = liveBeamAffectedBars.GetEnumerator();
                    enumerator.MoveNext();
                    targetBar = enumerator.Current;
                }
            }
            else if (barCreator.IsSelecting && barCreator.selectedBars.Count == 1 && barCreator.selectedPoints.Count == 0)
            {
                targetBar = barCreator.selectedBars[0];
            }
        }

        if (targetBar != null && targetBar.materialData != null)
        {
            if (liveBeamStatsPanel != null && !liveBeamStatsPanel.activeSelf) liveBeamStatsPanel.SetActive(true);
            if (targetBar != lastLiveBeamBar)
            {
                lastLiveBeamBar = targetBar;
                lastLiveBeamLengthHundredths = int.MinValue;
                lastLiveBeamCost = int.MinValue;
                lastLiveBeamAngleTenths = int.MinValue;
            }
            int lengthHundredths = Mathf.RoundToInt(targetBar.currentLength * 100f);
            int cost = Mathf.RoundToInt(targetBar.GetCost());
            int angleTenths = Mathf.RoundToInt(targetBar.currentAngle * 10f);
            if (lengthHundredths != lastLiveBeamLengthHundredths)
            {
                lastLiveBeamLengthHundredths = lengthHundredths;
                if (liveBeamLengthText != null) liveBeamLengthText.text = $"{targetBar.currentLength:F2}m";
            }
            if (cost != lastLiveBeamCost)
            {
                lastLiveBeamCost = cost;
                if (liveBeamCostText != null) liveBeamCostText.text = $"₱{targetBar.GetCost():N0}";
            }
            if (angleTenths != lastLiveBeamAngleTenths)
            {
                lastLiveBeamAngleTenths = angleTenths;
                if (liveBeamAngleText != null) liveBeamAngleText.text = $"{targetBar.currentAngle:F1}°";
            }
        }
        else
        {
            lastLiveBeamBar = null;
            if (liveBeamStatsPanel != null && liveBeamStatsPanel.activeSelf) liveBeamStatsPanel.SetActive(false);
        }
    }

    public void LogAction(string message)
    {
        if (actionLogText == null || string.IsNullOrEmpty(message)) return;

        if (currentActionLog == message)
        {
            // Refresh the same message without rewriting TMP or restarting its UI.
            currentActionLogRemaining = Mathf.Max(0f, logDisplayTime);
            TryShowCurrentActionLog();
            return;
        }

        // Action feedback represents the latest input. A new action replaces the
        // previous message immediately; if UI space is blocked, only this latest
        // message waits to become visible.
        currentActionLog = message;
        currentActionLogRemaining = Mathf.Max(0f, logDisplayTime);
        actionLogPrepared = false;
        HideActionLogTemporarily();
        TryShowCurrentActionLog();
        Debug.Log("[Action Log] " + message);
    }

    private void UpdateActionLog()
    {
        if (!actionLogInitialized) InitializeActionLog();
        if (actionLogRect == null || actionLogText == null || currentActionLog == null) return;

        bool wasVisible = actionLogVisible;
        if (!TryShowCurrentActionLog())
        {
            HideActionLogTemporarily();
            return;
        }
        if (!wasVisible) return;
        currentActionLogRemaining -= Time.deltaTime;
        if (currentActionLogRemaining > 0f) return;

        actionLogText.enabled = false;
        actionLogText.text = "";
        actionLogRect.anchoredPosition = authoredActionLogPosition;
        actionLogRect.sizeDelta = authoredActionLogSize;
        currentActionLog = null;
        actionLogPrepared = false;
        actionLogVisible = false;
    }

    private bool CanPresentActionLog()
    {
        if (!isActiveAndEnabled || actionLogRect == null ||
            !UIReservedRegionLayout.IsRenderable(actionLogRect) ||
            UIReservedRegionLayout.IsModalTransitioning) return false;

        TutorialManager tutorial = TutorialManager.Instance;
        // A centered banner has measured bounds even while its entrance animation
        // runs. Follow those bounds immediately; continue waiting for other
        // tutorial transitions so an old panel cannot briefly cover the log.
        return tutorial == null || !tutorial.IsBannerTransitioning ||
               tutorial.VisibleCenteredBannerRect != null;
    }

    private bool TryShowCurrentActionLog()
    {
        if (!actionLogInitialized) InitializeActionLog();
        if (actionLogRect == null || actionLogText == null ||
            currentActionLog == null ||
            !CanPresentActionLog()) return false;

        if (!actionLogPrepared) PrepareActionLog(currentActionLog);
        if (!TryPlaceActionLog(actionLogVisible)) return false;

        if (!actionLogVisible)
        {
            actionLogText.enabled = true;
            actionLogVisible = true;
        }
        return true;
    }

    private void InitializeActionLog()
    {
        if (actionLogInitialized || actionLogText == null) return;
        actionLogRect = actionLogText.rectTransform;
        authoredActionLogPosition = actionLogRect.anchoredPosition;
        authoredActionLogSize = actionLogRect.sizeDelta;
        actionLogText.text = "";
        actionLogText.enabled = false;
        actionLogText.raycastTarget = false;
        actionLogInitialized = true;
    }

    private void PrepareActionLog(string message)
    {
        actionLogText.text = message;
        actionLogRect.anchoredPosition = authoredActionLogPosition;
        actionLogRect.sizeDelta = authoredActionLogSize;
        float preferredHeight = actionLogText.GetPreferredValues(
            message, actionLogRect.rect.width, Mathf.Infinity).y;
        actionLogRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Max(actionLogRect.rect.height, preferredHeight + 4f));
        actionLogPrepared = true;
        lastActionLogScreenWidth = -1;
    }

    private bool TryPlaceActionLog(bool keepCurrentIfSafe)
    {
        RectTransform centeredBanner = TutorialManager.Instance != null
            ? TutorialManager.Instance.VisibleCenteredBannerRect : null;
        Vector2 center;
        if (centeredBanner != null)
        {
            if (!UIReservedRegionLayout.TryFindPlacementBelow(actionLogRect,
                    centeredBanner, actionLogSafeAreaMarginPixels,
                    actionLogPanelGapPixels, out center)) return false;
        }
        else
        {
            if (!RefreshPreferredActionLogCenter() ||
                !UIReservedRegionLayout.TryFindVerticalPlacement(actionLogRect,
                    preferredActionLogScreenCenter, keepCurrentIfSafe,
                    actionLogSafeAreaMarginPixels, actionLogPanelGapPixels,
                    out center)) return false;
        }
        return UIReservedRegionLayout.TrySetScreenCenter(actionLogRect, center);
    }

    private void RegisterActionLogProtectedRegions()
    {
        UIReservedRegionLayout.Register(EffectiveToolsPanel);
        UIReservedRegionLayout.Register(selectionActionPanel != null
            ? selectionActionPanel.transform as RectTransform : null);
        UIReservedRegionLayout.Register(materialsPanel != null
            ? materialsPanel.transform as RectTransform : null);
        UIReservedRegionLayout.Register(simulationControlsPanel != null
            ? simulationControlsPanel.transform as RectTransform : null);
        UIReservedRegionLayout.Register(selectToolImage != null ? selectToolImage.rectTransform : null);
        UIReservedRegionLayout.Register(moveToolImage != null ? moveToolImage.rectTransform : null);
        UIReservedRegionLayout.Register(deleteToolImage != null ? deleteToolImage.rectTransform : null);
        UIReservedRegionLayout.Register(gridToolImage != null ? gridToolImage.rectTransform : null);
        UIReservedRegionLayout.Register(infoToolImage != null ? infoToolImage.rectTransform : null);
        foreach (GameObject ui in hideDuringSimulation)
            if (ui != null) UIReservedRegionLayout.Register(ui.transform as RectTransform);
        if (actionLogProtectedRegions == null) return;
        foreach (RectTransform region in actionLogProtectedRegions)
            UIReservedRegionLayout.Register(region);
    }

    private void UnregisterActionLogProtectedRegions()
    {
        UIReservedRegionLayout.Unregister(EffectiveToolsPanel);
        UIReservedRegionLayout.Unregister(selectionActionPanel != null
            ? selectionActionPanel.transform as RectTransform : null);
        UIReservedRegionLayout.Unregister(materialsPanel != null
            ? materialsPanel.transform as RectTransform : null);
        UIReservedRegionLayout.Unregister(simulationControlsPanel != null
            ? simulationControlsPanel.transform as RectTransform : null);
        UIReservedRegionLayout.Unregister(selectToolImage != null ? selectToolImage.rectTransform : null);
        UIReservedRegionLayout.Unregister(moveToolImage != null ? moveToolImage.rectTransform : null);
        UIReservedRegionLayout.Unregister(deleteToolImage != null ? deleteToolImage.rectTransform : null);
        UIReservedRegionLayout.Unregister(gridToolImage != null ? gridToolImage.rectTransform : null);
        UIReservedRegionLayout.Unregister(infoToolImage != null ? infoToolImage.rectTransform : null);
        foreach (GameObject ui in hideDuringSimulation)
            if (ui != null) UIReservedRegionLayout.Unregister(ui.transform as RectTransform);
        if (actionLogProtectedRegions == null) return;
        foreach (RectTransform region in actionLogProtectedRegions)
            UIReservedRegionLayout.Unregister(region);
    }

    private bool RefreshPreferredActionLogCenter()
    {
        Canvas canvas = actionLogText.canvas;
        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        Rect safeArea = Screen.safeArea;
        if (lastActionLogScreenWidth == Screen.width &&
            lastActionLogScreenHeight == Screen.height &&
            lastActionLogSafeArea == safeArea &&
            Mathf.Approximately(lastActionLogCanvasScale, scale)) return true;

        Vector2 currentPosition = actionLogRect.anchoredPosition;
        actionLogRect.anchoredPosition = authoredActionLogPosition;
        bool measured = UIReservedRegionLayout.TryGetScreenRect(
            actionLogRect, out Rect authoredBounds);
        actionLogRect.anchoredPosition = currentPosition;
        if (!measured) return false;

        preferredActionLogScreenCenter = authoredBounds.center;
        lastActionLogScreenWidth = Screen.width;
        lastActionLogScreenHeight = Screen.height;
        lastActionLogSafeArea = safeArea;
        lastActionLogCanvasScale = scale;
        return true;
    }

    private void HideActionLogTemporarily()
    {
        if (!actionLogVisible) return;
        if (actionLogText != null) actionLogText.enabled = false;
        actionLogVisible = false;
    }

    public void MarkBridgeDirty() 
    { 
        // Moving a node changes cost immediately, but the capacity readout is
        // informational. Its repeated stress solves are deferred until release.
        bool draggingNodes = barCreator != null && barCreator.IsMoving && barCreator.isDraggingSelection;
        bool showEngineeringStats = statsPanelVisible && statsPanel != null && statsPanel.activeInHierarchy;
        CancelCapacityEstimate();
        RecalculateStaticBridge(draggingNodes);
        if (showEngineeringStats)
        {
            if (draggingNodes) capacityEstimatePending = true;
            else RequestCapacityEstimate();
            UpdateStatsUI();
        }
        UpdateContractUI();

        if (!draggingNodes) RefreshAllMaterialButtons();
    }

    private void RecalculateStaticBridge(bool reuseTopology)
    {
        // A drag changes member lengths, not which bars or points exist. Reuse
        // the last topology so budget checks avoid a scene-wide object search.
        if (!reuseTopology || uniqueBars.Count == 0)
        {
            uniqueBars.Clear();
            activePoints.Clear();

            BuildLocation targetLocation = GameManager.Instance != null
                ? GameManager.Instance.ActiveBuildLocation
                : null;

            // Bar ownership is authoritative. Walking Point.ConnectedBars could use
            // stale links left by a disabled/saved bridge or include another ravine.
            foreach (Bar bar in FindObjectsOfType<Bar>(true))
            {
                if (bar == null || !bar.gameObject.activeInHierarchy || !bar.enabled ||
                    bar.materialData == null ||
                    (barCreator != null && barCreator.IsCreating && barCreator.currentBar == bar) ||
                    (targetLocation != null && !targetLocation.Owns(bar)))
                {
                    continue;
                }

                uniqueBars.Add(bar);
                if (bar.startPoint != null && bar.startPoint.gameObject.activeInHierarchy)
                    activePoints.Add(bar.startPoint);
                if (bar.endPoint != null && bar.endPoint.gameObject.activeInHierarchy)
                    activePoints.Add(bar.endPoint);
            }
        }

        materialUsageCount.Clear();

        // M and J describe the planar engineering model. A dual visual/physical
        // beam still represents one model member and one joint at each endpoint;
        // its doubled mass and strength are handled by the material/solver.
        cachedBaseJ = activePoints.Count;
        cachedBaseM = 0;
        cachedBaseRoadLength = 0f;
        cachedBaseDeadLoad = 0f;
        cachedBaseCost = 0f;

        foreach (Bar b in uniqueBars)
        {
            if (barCreator != null && barCreator.currentBar == b && barCreator.IsCreating) continue;
            
            cachedBaseCost += b.GetCost();

            if (b.materialData != null)
            {
                if (!materialUsageCount.ContainsKey(b.materialData)) materialUsageCount[b.materialData] = 0;
                materialUsageCount[b.materialData]++;

                float length = GetStructuralLength(b);
                cachedBaseM++;
                if (b.materialData.isRoad) cachedBaseRoadLength += length;
                cachedBaseDeadLoad += length * b.materialData.GetPlacedMassPerMeter();
            }
        }

    }

    private static float GetStructuralLength(Bar bar)
    {
        if (bar == null) return 0f;

        Vector3 start = bar.startPoint != null ? bar.startPoint.transform.position : bar.StartPosition;
        Vector3 end = bar.endPoint != null ? bar.endPoint.transform.position : bar.EndPosition;
        start.z = 0f;
        end.z = 0f;
        float endpointLength = Vector3.Distance(start, end);
        return endpointLength > 0.001f ? endpointLength : Mathf.Max(0f, bar.currentLength);
    }

    private void CancelCapacityEstimate()
    {
        if (capacityEstimateCoroutine != null)
        {
            StopCoroutine(capacityEstimateCoroutine);
            capacityEstimateCoroutine = null;
        }
        capacityEstimatePending = false;
    }

    private void RequestCapacityEstimate()
    {
        CancelCapacityEstimate();
        capacityEstimatePending = true;
        // Let the panel begin its entrance animation before the first solve.
        capacityEstimateCoroutine = StartCoroutine(EstimateBridgeCapacityOverFrames());
    }

    private void FinishCapacityEstimate(float capacityKg)
    {
        cachedEstimatedCapacityKg = capacityKg;
        capacityEstimatePending = false;
        capacityEstimateCoroutine = null;
        if (statsPanelVisible) UpdateStatsUI();
    }

    private IEnumerator EstimateBridgeCapacityOverFrames()
    {
        yield return null;
        if (uniqueBars.Count == 0 || activePoints.Count < 2 || cachedBaseRoadLength <= 0.001f)
        {
            FinishCapacityEstimate(0f);
            yield break;
        }

        List<Bar> bars = new List<Bar>(uniqueBars);
        List<Point> points = new List<Point>(activePoints);
        const int samples = 9;

        DeterministicBridgeStressSolver.Result unloaded =
            DeterministicBridgeStressSolver.Analyze(points, bars, 0f, false, samples);
        if (unloaded == null || !unloaded.IsValid || !unloaded.IsStructurallyStable ||
            unloaded.PeakStructuralStress >= 1f)
        {
            FinishCapacityEstimate(0f);
            yield break;
        }
        yield return null;

        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        float high = Mathf.Max(100f, LiveLoadVehicle.GetContractTestWeight(contract) * 2f);
        float low = 0f;
        bool foundFailure = false;

        // Establish a failing upper bound, then locate the first-failure load.
        for (int i = 0; i < 8; i++)
        {
            DeterministicBridgeStressSolver.Result result =
                DeterministicBridgeStressSolver.Analyze(points, bars, high, false, samples);
            if (result == null || !result.IsValid || !result.IsStructurallyStable)
            {
                FinishCapacityEstimate(0f);
                yield break;
            }
            if (result.PeakStructuralStress >= 1f)
            {
                foundFailure = true;
                break;
            }

            low = high;
            high *= 2f;
            yield return null;
        }

        if (!foundFailure)
        {
            FinishCapacityEstimate(high);
            yield break;
        }

        yield return null;

        for (int i = 0; i < 10; i++)
        {
            float candidate = (low + high) * 0.5f;
            DeterministicBridgeStressSolver.Result result =
                DeterministicBridgeStressSolver.Analyze(points, bars, candidate, false, samples);
            if (result == null || !result.IsValid || !result.IsStructurallyStable)
            {
                FinishCapacityEstimate(0f);
                yield break;
            }

            if (result.PeakStructuralStress >= 1f) high = candidate;
            else low = candidate;
            yield return null;
        }

        FinishCapacityEstimate(low);
    }

    private void UpdateStatsUI()
    {
        int displayJ = cachedBaseJ; 
        int displayM = cachedBaseM;
        float roadLength = cachedBaseRoadLength;
        float deadLoad = cachedBaseDeadLoad;

        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null && barCreator.currentBar.materialData != null)
        {
            Bar preview = barCreator.currentBar;
            float previewLength = GetStructuralLength(preview);
            displayM++;
            if (preview.materialData.isRoad) roadLength += previewLength;
            deadLoad += previewLength * preview.materialData.GetPlacedMassPerMeter();
        }

        float theoreticalCapacityKg = cachedEstimatedCapacityKg;

        ContractSO currentContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        float liveLoad = LiveLoadVehicle.GetContractTestWeight(currentContract);
        
        float estimatedFoS = 0f;
        if (liveLoad > 0) estimatedFoS = theoreticalCapacityKg / liveLoad;

        float efficiencyRatio = 0f;
        if (deadLoad > 0) efficiencyRatio = theoreticalCapacityKg / deadLoad;

        bool statsChanged = Mathf.Abs(lastRoadLength - roadLength) > 0.05f ||
                            Mathf.Abs(lastDeadLoad - deadLoad) > 0.05f ||
                            Mathf.Abs(lastLiveLoad - liveLoad) > 0.05f ||
                            Mathf.Abs(lastEstimatedCapacity - theoreticalCapacityKg) > 0.05f ||
                            Mathf.Abs(lastEfficiencyRatio - efficiencyRatio) > 0.005f ||
                            Mathf.Abs(lastEstimatedFoS - estimatedFoS) > 0.005f ||
                            lastCapacityEstimatePending != capacityEstimatePending;
        if (statsChanged)
        {
            lastCapacityEstimatePending = capacityEstimatePending;
            lastRoadLength = roadLength;
            lastDeadLoad = deadLoad;
            lastLiveLoad = liveLoad;
            lastEstimatedCapacity = theoreticalCapacityKg;
            lastEfficiencyRatio = efficiencyRatio;
            lastEstimatedFoS = estimatedFoS;
            if (totalLengthText != null) totalLengthText.text = $"<color=#F2BF72>ROAD LENGTH</color>  <b>{roadLength:F1} m</b>";
            if (deadLoadText != null) deadLoadText.text = $"<color=#F2BF72>DEAD LOAD</color>  <b>{deadLoad:F1} kg</b>";
            
            if (targetCargoWeightText != null) targetCargoWeightText.text = $"<color=#F2BF72>LIVE LOAD</color>  <b>{liveLoad:F0} kg</b>";
            if (estimatedCapacityText != null) estimatedCapacityText.text = capacityEstimatePending
                ? "<color=#F2BF72>EST. CAPACITY</color>  Calculating..." : $"<color=#F2BF72>EST. CAPACITY</color>  <b>~{theoreticalCapacityKg:F0} kg</b>";
            if (efficiencyRatioText != null) efficiencyRatioText.text = capacityEstimatePending
                ? "<color=#F2BF72>EFFICIENCY RATIO</color>  Calculating..." : $"<color=#F2BF72>EFFICIENCY RATIO</color>  <b>{efficiencyRatio:F2}</b>";
            
            if (factorOfSafetyText != null)
            {
                if (capacityEstimatePending) factorOfSafetyText.text = "<color=#F2BF72>SAFETY</color>  Calculating...";
                else if (estimatedFoS >= 2.0f) factorOfSafetyText.text = $"<color=#F2BF72>SAFETY</color>  <color=#83DBA6><b>{estimatedFoS:F2} · Safe</b></color>";
                else if (estimatedFoS >= 1.0f) factorOfSafetyText.text = $"<color=#F2BF72>SAFETY</color>  <color=#FFE088><b>{estimatedFoS:F2} · Risky</b></color>";
                else factorOfSafetyText.text = $"<color=#F2BF72>SAFETY</color>  <color=#FF9284><b>{estimatedFoS:F2} · Will Fail</b></color>";
            }
        }

        if (displayM != lastDisplayM || displayJ != lastDisplayJ)
        {
            lastDisplayM = displayM;
            lastDisplayJ = displayJ;
            if (membersCountText != null) membersCountText.text = $"<color=#F2BF72>STRUCTURE</color>  <b>{displayM} members  |  {displayJ} joints</b>";
        }
    }

    public float GetTotalCost()
    {
        return cachedBaseCost;
    }

    private void UpdateContractUI()
    {
        ContractSO currentContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        maxBudget = currentContract != null ? currentContract.budget : fallbackMaxBudget;
        int displayedMaxBudget = Mathf.RoundToInt(maxBudget);

        if (displayedMaxBudget != lastDisplayedMaxBudget)
        {
            lastDisplayedMaxBudget = displayedMaxBudget;
            if (maxBudgetText != null) maxBudgetText.text = $" ₱{displayedMaxBudget:N0}";
        }

        float baseCost = GetTotalCost();
        float previewCost = 0f;
        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null) previewCost = barCreator.currentBar.GetCost();
        
        int totalProjectedCost = Mathf.RoundToInt(baseCost + previewCost);

        bool overBudget = maxBudget <= 0f || totalProjectedCost > maxBudget;
        float remainingRatio = maxBudget > 0f
            ? Mathf.Clamp01((maxBudget - totalProjectedCost) / maxBudget)
            : 0f;
        Color budgetStateColor = GetBudgetRemainingColor(remainingRatio, overBudget);

        if (budgetFillBar != null)
        {
            targetBudgetRemainingRatio = remainingRatio;
            targetBudgetBarColor = budgetStateColor;

            if (!budgetBarVisualInitialized)
            {
                displayedBudgetRemainingRatio = targetBudgetRemainingRatio;
                displayedBudgetBarColor = targetBudgetBarColor;
                budgetBarVisualInitialized = true;
                ApplyBudgetBarVisual();
            }
        }

        if (usedBudgetText != null)
        {
            if (totalProjectedCost != lastProjectedCost)
            {
                lastProjectedCost = totalProjectedCost;
                usedBudgetText.text = $" ₱{totalProjectedCost:N0}";
            }

            // Budget status is communicated by the bar; keep the number legible
            // and visually consistent with the other build-mode text.
            usedBudgetText.color = normalTextColor;
        }
    }

    private Color GetBudgetRemainingColor(float remainingRatio, bool overBudget)
    {
        if (overBudget || remainingRatio <= 0f)
            return emptyBudgetColor;

        float warningThreshold = Mathf.Clamp01(budgetWarningThreshold);
        float criticalThreshold = Mathf.Clamp(budgetCriticalThreshold, 0f, warningThreshold);

        if (remainingRatio <= criticalThreshold)
            return criticalBudgetColor;
        if (remainingRatio <= warningThreshold)
            return warningBudgetColor;
        return safeBudgetColor;
    }

    private void UpdateBudgetBarVisual()
    {
        if (!budgetBarVisualInitialized || budgetFillBar == null) return;

        float remainingDistance = Mathf.Abs(displayedBudgetRemainingRatio - targetBudgetRemainingRatio);
        Color colorDifference = displayedBudgetBarColor - targetBudgetBarColor;
        RectTransform trackRect = budgetFillRect != null ? budgetFillRect.parent as RectTransform : null;
        bool trackResized = trackRect != null &&
            (Mathf.Abs(trackRect.rect.width - lastBudgetTrackSize.x) > 0.5f ||
             Mathf.Abs(trackRect.rect.height - lastBudgetTrackSize.y) > 0.5f);
        if (remainingDistance < 0.0005f &&
            Mathf.Abs(colorDifference.r) + Mathf.Abs(colorDifference.g) +
            Mathf.Abs(colorDifference.b) + Mathf.Abs(colorDifference.a) < 0.002f &&
            !trackResized)
            return;

        float speed = Mathf.Max(0.1f, budgetFillAnimationSpeed);
        displayedBudgetRemainingRatio = Mathf.MoveTowards(
            displayedBudgetRemainingRatio,
            targetBudgetRemainingRatio,
            speed * Time.unscaledDeltaTime);

        float colorBlend = 1f - Mathf.Exp(-speed * 2f * Time.unscaledDeltaTime);
        displayedBudgetBarColor = Color.Lerp(
            displayedBudgetBarColor,
            targetBudgetBarColor,
            colorBlend);
        if (Mathf.Abs(displayedBudgetRemainingRatio - targetBudgetRemainingRatio) < 0.0005f)
            displayedBudgetRemainingRatio = targetBudgetRemainingRatio;
        if (Mathf.Abs(displayedBudgetBarColor.r - targetBudgetBarColor.r) +
            Mathf.Abs(displayedBudgetBarColor.g - targetBudgetBarColor.g) +
            Mathf.Abs(displayedBudgetBarColor.b - targetBudgetBarColor.b) +
            Mathf.Abs(displayedBudgetBarColor.a - targetBudgetBarColor.a) < 0.002f)
            displayedBudgetBarColor = targetBudgetBarColor;
        ApplyBudgetBarVisual();
    }

    private void ApplyBudgetBarVisual()
    {
        if (budgetFillBar == null) return;
        if (budgetFillRect == null) budgetFillRect = budgetFillBar.rectTransform;

        RectTransform trackRect = budgetFillRect.parent as RectTransform;
        float trackWidth = trackRect != null ? trackRect.rect.width : 0f;
        float trackHeight = trackRect != null ? trackRect.rect.height : budgetFillHeight;
        lastBudgetTrackSize = new Vector2(trackWidth, trackHeight);
        float inset = Mathf.Max(0f, budgetFillHorizontalInset);
        float availableWidth = Mathf.Max(0f, trackWidth - inset * 2f);
        float fillWidth = availableWidth * Mathf.Clamp01(displayedBudgetRemainingRatio);
        float fillHeight = Mathf.Min(
            Mathf.Max(8f, budgetFillHeight),
            Mathf.Max(8f, trackHeight - 8f));

        budgetFillRect.anchorMin = new Vector2(0f, 0.5f);
        budgetFillRect.anchorMax = new Vector2(0f, 0.5f);
        budgetFillRect.pivot = new Vector2(0f, 0.5f);
        budgetFillRect.anchoredPosition = new Vector2(inset, 0f);
        budgetFillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fillWidth);
        budgetFillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fillHeight);

        budgetFillBar.enabled = fillWidth > 0.5f;
        budgetFillBar.color = displayedBudgetBarColor;
    }

    private void UpdatePlayPauseButtonUI()
    {
        if (playPauseButtonImage == null || physicsManager == null) return;
        Sprite desiredSprite = physicsManager.isSimulating ? stopIcon : playIcon;
        if (desiredSprite != null && playPauseButtonImage.sprite != desiredSprite)
            playPauseButtonImage.sprite = desiredSprite;
    }

    private void UpdateStressUI()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            // The in-test visualizer follows the current smoothed load so players
            // can see stress rise and fall as the vehicle crosses the bridge. The
            // manager still records the run peak separately for result scoring.
            float currentStress = physicsManager.GetMaxBridgeStress();
            float previousFill = Mathf.Max(0f, lastStressFillAmount);
            float displayedStress = Mathf.MoveTowards(previousFill, currentStress,
                Mathf.Max(0.1f, stressFillAnimationSpeed) * Time.unscaledDeltaTime);
            float stressPercent = displayedStress * 100f;

            Color currentStressColor = displayedStress <= 0.5f ?
                Color.Lerp(safeStressColor, warningStressColor, displayedStress * 2f) :
                Color.Lerp(warningStressColor, criticalStressColor, (displayedStress - 0.5f) * 2f);

            if (stressFillBar != null)
            {
                RectTransform fillRect = stressFillBar.rectTransform;
                Vector2 anchorMax = fillRect.anchorMax;
                float fillHeight = Mathf.Clamp01(displayedStress);
                if (!Mathf.Approximately(anchorMax.y, fillHeight))
                {
                    anchorMax.y = fillHeight;
                    fillRect.anchorMax = anchorMax;
                }

                if (!Mathf.Approximately(displayedStress, lastStressFillAmount))
                    stressFillBar.color = currentStressColor;
            }
            lastStressFillAmount = displayedStress;

            bool failureValue = currentStress >= 1f;
            if (!Mathf.Approximately(stressPercent, lastStressPercent) &&
                (failureValue || Time.unscaledTime >= nextStressTextUpdateTime))
            {
                nextStressTextUpdateTime = Time.unscaledTime +
                    1f / Mathf.Max(5f, stressTextUpdatesPerSecond);
                lastStressPercent = stressPercent;
                if (stressText != null) 
                { 
                    stressText.text = $"<size=15><color=#F8EAD7>BRIDGE STRESS</color></size>\n<size=31>{stressPercent:0.0}%</size>";
                    stressText.color = StressReadoutColor;
                }
            }
        }
        else
        {
            if (!Mathf.Approximately(lastStressPercent, 0f))
            {
                lastStressPercent = 0f;
                if (stressText != null)
                {
                    stressText.text = "<size=15><color=#F8EAD7>BRIDGE STRESS</color></size>\n<size=31>0.0%</size>";
                    stressText.color = StressReadoutColor;
                }
            }
            nextStressTextUpdateTime = 0f;
            if (stressFillBar != null)
            {
                RectTransform fillRect = stressFillBar.rectTransform;
                Vector2 anchorMax = fillRect.anchorMax;
                if (!Mathf.Approximately(anchorMax.y, 0f))
                {
                    anchorMax.y = 0f;
                    fillRect.anchorMax = anchorMax;
                }

                if (!Mathf.Approximately(lastStressFillAmount, 0f))
                    stressFillBar.color = safeStressColor;
            }
            lastStressFillAmount = 0f;
        }
    }

    public void SetSelectionPanelActive(bool isActive)
    {
        if (selectionActionPanel != null) selectionActionPanel.SetActive(isActive);
    }

    public void OnCloseSelectionPanelButtonClicked()
    {
        PlayBuildButtonClickSfx();
        if (barCreator != null) barCreator.CancelAllModes();
        SetSelectionPanelActive(false);
        LogAction("Selection Cleared");
    }

    // --- THE FIX: Removed tool locking here so players can use tools freely ---
    private bool IsToolAllowed()
    {
        GameObject clickedObject = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        BuildTutorialDirector director = BuildTutorialDirector.Instance;

        if (director != null && director.IsAwaitingInvalidBarUndo)
            return false;

        if (isTutorialUI_Locked && director != null)
        {
            director.OnToolClicked(clickedObject);
        }

        return true;
    }

    private bool IsTopologyEditBlockedDuringTracing()
    {
        if (BuildTutorialDirector.Instance == null || !BuildTutorialDirector.Instance.isTracingStep) return false;
        LogAction("Finish tracing the blueprint before using that edit tool.");
        return true;
    }

    public void OnToggleSelectModeButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (barCreator != null) barCreator.ToggleSelectMode(); }
    public void OnToggleMoveModeButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (barCreator != null) barCreator.ToggleMoveMode(); }
    public void OnToggleDeleteModeButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (barCreator != null) barCreator.ToggleDeleteMode(); }
    public void OnToggleGridButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (barCreator != null) barCreator.ToggleGrid(); }
    public void OnCancelDrawingButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (barCreator != null) barCreator.CancelCreation(); }
    public void OnExitBuildModeButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode(); }
    public void OnResetCameraButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; BuildCameraController camCtrl = FindObjectOfType<BuildCameraController>(); if (camCtrl != null) camCtrl.ResetCameraRotation(); }
    public void OnToggleStatsButtonClicked()
    {
        PlayBuildButtonClickSfx();
        if (!IsToolAllowed()) return;
        SetStatsPanelVisible(!statsPanelVisible);
    }

    // Called only after BarCreator accepts an actual bridge-placement start.
    public void OnBridgePlacementStarted()
    {
        if (statsPanelVisible) SetStatsPanelVisible(false);
    }

    private void InitializeStatsPanel()
    {
        if (statsPanel == null) return;
        statsPanelRect = statsPanel.GetComponent<RectTransform>();
        statsPanelCanvasGroup = statsPanel.GetComponent<CanvasGroup>();
        StyleStatsPanel();
        if (statsPanelRect != null)
        {
            statsPanelRestPosition = statsPanelRect.anchoredPosition;
            statsPanelRestScale = statsPanelRect.localScale;
        }
        statsPanelVisible = false;
        statsPanelProgress = 0f;
        statsPanel.SetActive(false);
        ApplyStatsPanelVisual(0f);
    }

    private void StyleStatsPanel()
    {
        if (statsPanelRect == null) return;

        // The authored panel still contains its old white title. Its data rows
        // share that placeholder text, so preserve the bound readouts.
        foreach (TextMeshProUGUI label in statsPanel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null || label == totalLengthText || label == membersCountText ||
                label == deadLoadText || label == targetCargoWeightText ||
                label == estimatedCapacityText || label == efficiencyRatioText ||
                label == factorOfSafetyText) continue;
            if (string.Equals(label.text?.Trim(), "Bridge Engineering Stats",
                    System.StringComparison.OrdinalIgnoreCase))
                label.gameObject.SetActive(false);
        }

        bool showSafetyRow = factorOfSafetyText != null;
        statsPanelRect.sizeDelta = new Vector2(471f, showSafetyRow ? 370f : 330f);
        Image background = statsPanel.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(0.045f, 0.09f, 0.19f, 0.92f);
            background.raycastTarget = false;
        }

        float rowOffset = showSafetyRow ? 20f : 0f;
        StyleStatLine(targetCargoWeightText, 88f + rowOffset);
        StyleStatLine(totalLengthText, 48f + rowOffset);
        StyleStatLine(membersCountText, 8f + rowOffset);
        StyleStatLine(deadLoadText, -32f + rowOffset);
        StyleStatLine(estimatedCapacityText, -72f + rowOffset);
        StyleStatLine(efficiencyRatioText, -112f + rowOffset);
        StyleStatLine(factorOfSafetyText, -152f + rowOffset);

        Transform existingHeading = statsPanel.transform.Find("BridgeStatsHeading");
        if (existingHeading == null)
        {
            GameObject headingObject = new GameObject("BridgeStatsHeading", typeof(RectTransform));
            headingObject.transform.SetParent(statsPanel.transform, false);
            TextMeshProUGUI heading = headingObject.AddComponent<TextMeshProUGUI>();
            if (totalLengthText != null) heading.font = totalLengthText.font;
            heading.text = "BRIDGE STATS";
            heading.fontSize = 27f;
            heading.fontStyle = FontStyles.Bold;
            heading.color = new Color(1f, 0.77f, 0.42f, 1f);
            heading.alignment = TextAlignmentOptions.Center;
            heading.enableWordWrapping = false;
            heading.raycastTarget = false;
            RectTransform headingRect = heading.rectTransform;
            headingRect.anchorMin = headingRect.anchorMax = new Vector2(0.5f, 0.5f);
            headingRect.anchoredPosition = new Vector2(0f, showSafetyRow ? 157f : 137f);
            headingRect.sizeDelta = new Vector2(420f, 36f);
        }
    }

    private static void StyleStatLine(TextMeshProUGUI label, float y)
    {
        if (label == null) return;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(420f, 36f);
        label.fontSize = 22f;
        label.enableAutoSizing = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.fontStyle = FontStyles.Normal;
        label.color = new Color(0.98f, 0.95f, 0.88f, 1f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
    }

    private void ResetStatsPanel()
    {
        if (statsPanelAnimation != null)
        {
            StopCoroutine(statsPanelAnimation);
            statsPanelAnimation = null;
        }
        statsPanelVisible = false;
        statsPanelProgress = 0f;
        if (statsPanelRect != null)
        {
            statsPanelRect.anchoredPosition = statsPanelRestPosition;
            statsPanelRect.localScale = statsPanelRestScale;
        }
        if (statsPanelCanvasGroup != null) statsPanelCanvasGroup.alpha = 1f;
        if (statsPanel != null) statsPanel.SetActive(false);
    }

    private void SetStatsPanelVisible(bool visible)
    {
        if (statsPanel == null || statsPanelVisible == visible) return;
        statsPanelVisible = visible;
        if (statsPanelAnimation != null) StopCoroutine(statsPanelAnimation);
        if (visible) statsPanel.SetActive(true);
        if (visible)
        {
            RequestCapacityEstimate();
            UpdateStatsUI();
        }
        else CancelCapacityEstimate();
        if (statsPanelCanvasGroup != null)
            statsPanelCanvasGroup.blocksRaycasts = false; // Readout only; never block building.
        statsPanelAnimation = StartCoroutine(AnimateStatsPanel(visible));
    }

    private IEnumerator AnimateStatsPanel(bool visible)
    {
        float start = statsPanelProgress;
        float target = visible ? 1f : 0f;
        float duration = visible ? statsOpenDuration : statsCloseDuration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            statsPanelProgress = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            ApplyStatsPanelVisual(statsPanelProgress);
            yield return null;
        }
        statsPanelProgress = target;
        ApplyStatsPanelVisual(target);
        if (!visible && statsPanel != null) statsPanel.SetActive(false);
        statsPanelAnimation = null;
    }

    private void ApplyStatsPanelVisual(float progress)
    {
        float eased = progress * progress * (3f - 2f * progress);
        if (statsPanelRect != null)
        {
            statsPanelRect.anchoredPosition = statsPanelRestPosition + Vector2.right * (22f * (1f - eased));
            statsPanelRect.localScale = statsPanelRestScale * Mathf.Lerp(0.94f, 1f, eased);
        }
        if (statsPanelCanvasGroup != null) statsPanelCanvasGroup.alpha = eased;
    }
    public void OnCutSelectedButtonClicked()
    {
        PlayBuildButtonClickSfx();
        BuildTutorialDirector director = BuildTutorialDirector.Instance;
        if (director != null && director.IsCutBlockedForCurrentTutorial())
        {
            LogAction("Use Copy for this tutorial. Cut is temporarily disabled.");
            director.NotifyCutBlocked();
            return;
        }

        if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return;
        if (ClipboardManager.Instance != null && barCreator != null)
            ClipboardManager.Instance.CutSelected(barCreator.GetSelectedPoints());
    }
    public void OnCopyButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (ClipboardManager.Instance != null && barCreator != null) ClipboardManager.Instance.CopySelected(barCreator.GetSelectedPoints()); }
    public void OnPasteButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (ClipboardManager.Instance != null) ClipboardManager.Instance.StampPaste(); }
    public void OnUndoButtonClicked()
    {
        PlayBuildButtonClickSfx();
        BuildTutorialDirector director = BuildTutorialDirector.Instance;
        if (director == null || !director.IsAwaitingInvalidBarUndo)
        {
            if (!IsToolAllowed()) return;
        }

        if (CommandManager.Instance != null) CommandManager.Instance.Undo();
        if (director != null) director.NotifyUndoCompleted();
    }
    public void OnRedoButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed()) return; if (CommandManager.Instance != null) CommandManager.Instance.Redo(); }
    public void OnDeleteSelectedButtonClicked() { PlayBuildButtonClickSfx(); if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (barCreator != null) barCreator.DeleteSelected(); }

    public void OnToggleSimulationButtonClicked() 
    { 
        if (physicsManager == null) return; 
        if (physicsManager.isSimulating) OnRestartButtonClicked(); 
        else OnSimulateButtonClicked(); 
    }
    
    public void OnSimulateButtonClicked() 
    { 
        if (barCreator != null && barCreator.IsAutoDrawing) return;
        PlayBuildButtonClickSfx();
        if (!IsToolAllowed()) return;

        BuildTutorialDirector director = BuildTutorialDirector.Instance;
        if (director != null && !director.CanStartSimulation)
        {
            LogAction("Simulation is locked until the tutorial enables it.");
            RefreshSimulationButtonLock();
            return;
        }

        if (physicsManager != null && !physicsManager.isSimulating) 
        { 
            ContractSO testContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
            if (testContract != null && testContract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle &&
                testContract.allowVehicleCargo && testContract.minimumLoadedCargo > 0)
            {
                LiveLoadVehicle vehicle = LiveLoadVehicle.FindActiveForContract(testContract);
                int loaded = vehicle != null ? vehicle.LoadedCargoCount : 0;
                if (loaded < testContract.minimumLoadedCargo)
                {
                    LogAction($"Load cargo into the vehicle before testing ({loaded}/{testContract.minimumLoadedCargo}).");
                    return;
                }
            }
            if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null &&
                GameManager.Instance.CurrentContract.liveLoadMode == ContractSO.LiveLoadMode.PlayerCarriedCargo)
            {
                if (GameManager.Instance.TryBeginCargoTest(physicsManager))
                {
                    if (barCreator != null) { barCreator.CancelAllModes(); barCreator.isSimulating = true; }
                    SetSelectionPanelActive(false);
                    LogAction("Cargo crossing test started. Carry the assigned cargo to its drop location and tap Place Cargo.");
                }
                return;
            }
            if (barCreator != null) { barCreator.CancelAllModes(); barCreator.isSimulating = true; } 
            SetSelectionPanelActive(false);
            physicsManager.ActivatePhysics(); 
            LogAction("Simulation Started");
        } 
    }
    
    public void OnRestartButtonClicked() 
    { 
        if (GameManager.Instance != null && GameManager.Instance.IsCargoTestActive)
        { GameManager.Instance.CancelCargoTest(); return; }
        PlayBuildButtonClickSfx();
        if (IsTutorialContractActive())
        {
            LogAction("Simulation cannot be stopped early during a tutorial.");
            RefreshSimulationButtonLock();
            return;
        }

        if (physicsManager != null && physicsManager.isSimulating) 
        { 
            physicsManager.StopPhysicsAndReset(); 
            if (barCreator != null) barCreator.isSimulating = false; 
            LogAction("Simulation Stopped");
        } 
    }

    public void RefreshSimulationButtonLock()
    {
        ResolveSimulationUIReferences();

        if (simulationButton != null)
        {
            bool simulationIsRunning = simulationInProgressForUI ||
                                       (physicsManager != null && physicsManager.isSimulating);
            bool tutorialContractActive = IsTutorialContractActive();
            bool hiddenByContract = GameManager.Instance != null &&
                                    GameManager.Instance.CurrentContract != null &&
                                    GameManager.Instance.CurrentContract.IsToolHidden(BuildModeTool.Simulate);
            BuildTutorialDirector director = BuildTutorialDirector.Instance;
            bool tutorialAllowsPlay = director == null || director.CanStartSimulation;
            simulationButton.interactable = tutorialContractActive
                ? !simulationIsRunning && tutorialAllowsPlay
                : simulationIsRunning || tutorialAllowsPlay;

            if (tutorialSimulationStopObject != null)
            {
                tutorialSimulationStopObject.SetActive(!tutorialContractActive && !hiddenByContract);
            }
            else
            {
                // A shared Play/Stop button must remain visible before simulation so the
                // tutorial can unlock Play. Once running, hide it to prevent early stopping.
                simulationButton.gameObject.SetActive(!hiddenByContract &&
                                                       !(tutorialContractActive && simulationIsRunning));
            }
        }

        RefreshSimulationPanelVisibility();
    }

    public void RefreshSimulationPanelVisibility()
    {
        ResolveSimulationUIReferences();
        if (simulationControlsPanel == null) return;

        // Refill a reusable list to detect added/removed controls without allocating
        // an array each check. Include inactive children so a re-enabled tutorial
        // button can bring its hidden panel back.
        simulationControlsPanel.GetComponentsInChildren(true, simulationPanelButtons);
        bool shouldShowPanel = false;
        foreach (Button button in simulationPanelButtons)
        {
            if (button != null && IsLocallyVisibleWithinPanel(
                    button.transform,
                    simulationControlsPanel.transform))
            {
                shouldShowPanel = true;
                break;
            }
        }

        // Support containers that use non-Button wrappers supplied in the Inspector.
        if (simulationPanelButtons.Count == 0)
        {
            shouldShowPanel = IsControlObjectVisible(playSimulationButtonObject) ||
                              IsControlObjectVisible(stopSimulationButtonObject) ||
                              IsControlObjectVisible(tutorialSimulationStopObject);
        }

        if (simulationControlsPanel.activeSelf != shouldShowPanel)
            simulationControlsPanel.SetActive(shouldShowPanel);
    }

    private void ResolveSimulationUIReferences()
    {
        if (simulationButton == null && playPauseButtonImage != null)
            simulationButton = playPauseButtonImage.GetComponentInParent<Button>(true);

        if (playSimulationButtonObject == null && simulationButton != null)
            playSimulationButtonObject = simulationButton.gameObject;

        if (stopSimulationButtonObject == null && tutorialSimulationStopObject != null)
            stopSimulationButtonObject = tutorialSimulationStopObject;

        if (simulationControlsPanel != null || simulationButton == null) return;

        // Find the nearest visual ancestor. This resolves existing scene layouts such
        // as CanyonCrossing's Play button -> Content -> Panel hierarchy without adding
        // or replacing any scene objects.
        Transform ancestor = simulationButton.transform.parent;
        while (ancestor != null && ancestor != transform)
        {
            if (ancestor.GetComponent<Image>() != null ||
                ancestor.GetComponent<CanvasGroup>() != null)
            {
                simulationControlsPanel = ancestor.gameObject;
                break;
            }

            ancestor = ancestor.parent;
        }
    }

    private bool IsControlObjectVisible(GameObject controlObject)
    {
        if (controlObject == null || simulationControlsPanel == null) return false;

        controlObject.GetComponentsInChildren(true, simulationControlButtons);
        foreach (Button button in simulationControlButtons)
        {
            if (button != null && IsLocallyVisibleWithinPanel(
                    button.transform,
                    simulationControlsPanel.transform))
                return true;
        }

        return simulationControlButtons.Count == 0 && IsLocallyVisibleWithinPanel(
            controlObject.transform,
            simulationControlsPanel.transform);
    }

    private static bool IsLocallyVisibleWithinPanel(Transform item, Transform panel)
    {
        if (item == null || panel == null) return false;

        Transform current = item;
        while (current != null && current != panel)
        {
            if (!current.gameObject.activeSelf) return false;
            current = current.parent;
        }

        return current == panel;
    }

    private static bool IsTutorialContractActive()
    {
        return GameManager.Instance != null &&
               GameManager.Instance.CurrentContract != null &&
               GameManager.Instance.CurrentContract.IsTutorialForCurrentPlayer();
    }

    public void OnMaterialSelected(BridgeMaterialSO newMaterial) 
    { 
        PlayBuildButtonClickSfx();
        if (BuildTutorialDirector.Instance != null && BuildTutorialDirector.Instance.IsAwaitingInvalidBarUndo)
        {
            LogAction("Undo the invalid bar before selecting another material.");
            return;
        }
        
        if (barCreator != null) 
        { 
            barCreator.isDeleteMode = false; 
            barCreator.SetActiveMaterial(newMaterial); 
            SetSelectionPanelActive(false);
            LogAction($"Selected Material: {newMaterial.name}");

            if (BuildTutorialDirector.Instance != null)
                BuildTutorialDirector.Instance.OnMaterialClicked(newMaterial);
        } 
    }

    public void PlayBuildButtonClickSfx()
    {
        if (AudioManager.Instance != null && !string.IsNullOrWhiteSpace(buildButtonClickSfxId))
            AudioManager.Instance.PlaySFX(buildButtonClickSfxId);
    }
}
