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
    private Coroutine clearLogCoroutine;

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

    [Header("Universal Timer UI (Time Attack & Hold)")]
    public GameObject timerPanel; 
    public TextMeshProUGUI timerText; 

    [Header("Engineering Stats (CAD Readout)")]
    public GameObject statsPanel; 
    public TextMeshProUGUI totalLengthText; 
    public TextMeshProUGUI membersCountText;  
    public TextMeshProUGUI deadLoadText;  
    public TextMeshProUGUI targetCargoWeightText; 
    public TextMeshProUGUI estimatedCapacityText;
    public TextMeshProUGUI efficiencyRatioText;
    public TextMeshProUGUI factorOfSafetyText; 

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

    private HashSet<Bar> uniqueBars = new HashSet<Bar>();
    private HashSet<Point> activePoints = new HashSet<Point>();

    private float lastStressPercent = -1f;
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

    private Dictionary<BridgeMaterialSO, int> materialUsageCount = new Dictionary<BridgeMaterialSO, int>();
    private readonly HashSet<Bar> liveBeamAffectedBars = new HashSet<Bar>();
    private Bar lastLiveBeamBar;
    private int lastLiveBeamLengthHundredths = int.MinValue;
    private int lastLiveBeamCost = int.MinValue;
    private int lastLiveBeamAngleTenths = int.MinValue;

    private void Awake() { Instance = this; }

    private void OnEnable()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
            RefreshContractBuildUI();
    }

    private void Start()
    {
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();
        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        if (selectionActionPanel != null) selectionActionPanel.SetActive(false);
        if (statsPanel != null) statsPanel.SetActive(false); 
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

        if (actionLogText != null) actionLogText.text = ""; 

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
        if (timerPanel != null) timerPanel.SetActive(isVisible);
    }

    public void UpdateTimerText(string prefix, float timeInSeconds)
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(timeInSeconds / 60F);
            int seconds = Mathf.FloorToInt(timeInSeconds - minutes * 60);
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
                if (statsPanel != null && statsPanel.activeInHierarchy)
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
            infoToolImage.color = (statsPanel != null && statsPanel.activeSelf) ? activeToolColor : inactiveToolColor;
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
                var selectedPoints = barCreator.selectedBars.Count == 0
                    ? barCreator.selectedPoints
                    : barCreator.GetSelectedPoints();
                liveBeamAffectedBars.Clear();
                foreach (Point p in selectedPoints)
                {
                    if (p == null || p.IsScenePlacedAnchor) continue;
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
        if (actionLogText != null)
        {
            actionLogText.text = message;
            if (clearLogCoroutine != null) StopCoroutine(clearLogCoroutine);
            clearLogCoroutine = StartCoroutine(ClearLogRoutine());
        }
        Debug.Log("[Action Log] " + message);
    }

    private IEnumerator ClearLogRoutine()
    {
        yield return new WaitForSeconds(logDisplayTime);
        if (actionLogText != null) actionLogText.text = "";
    }

    public void MarkBridgeDirty() 
    { 
        // Moving a node changes cost immediately, but the capacity readout is
        // informational. Its repeated stress solves are deferred until release.
        bool draggingNodes = barCreator != null && barCreator.IsMoving && barCreator.isDraggingSelection;
        bool showEngineeringStats = statsPanel != null && statsPanel.activeInHierarchy;
        RecalculateStaticBridge(showEngineeringStats && !draggingNodes, draggingNodes);
        if (showEngineeringStats) UpdateStatsUI();
        UpdateContractUI();

        if (!draggingNodes) RefreshAllMaterialButtons();
    }

    private void RecalculateStaticBridge(bool refreshCapacity, bool reuseTopology)
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

        if (refreshCapacity)
            cachedEstimatedCapacityKg = EstimateBridgeCapacityKg();
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

    private float EstimateBridgeCapacityKg()
    {
        if (uniqueBars.Count == 0 || activePoints.Count < 2 || cachedBaseRoadLength <= 0.001f)
            return 0f;

        List<Bar> bars = new List<Bar>(uniqueBars);
        List<Point> points = new List<Point>(activePoints);
        const int samples = 9;

        DeterministicBridgeStressSolver.Result unloaded =
            DeterministicBridgeStressSolver.Analyze(points, bars, 0f, false, samples);
        if (unloaded == null || !unloaded.IsValid || !unloaded.IsStructurallyStable ||
            unloaded.PeakStructuralStress >= 1f)
            return 0f;

        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        float high = Mathf.Max(100f, LiveLoadVehicle.GetContractTestWeight(contract) * 2f);
        float low = 0f;
        bool foundFailure = false;

        // Establish a failing upper bound, then locate the first-failure load.
        for (int i = 0; i < 8; i++)
        {
            DeterministicBridgeStressSolver.Result result =
                DeterministicBridgeStressSolver.Analyze(points, bars, high, false, samples);
            if (result == null || !result.IsValid || !result.IsStructurallyStable) return 0f;
            if (result.PeakStructuralStress >= 1f)
            {
                foundFailure = true;
                break;
            }

            low = high;
            high *= 2f;
        }

        if (!foundFailure) return high;

        for (int i = 0; i < 10; i++)
        {
            float candidate = (low + high) * 0.5f;
            DeterministicBridgeStressSolver.Result result =
                DeterministicBridgeStressSolver.Analyze(points, bars, candidate, false, samples);
            if (result == null || !result.IsValid || !result.IsStructurallyStable) return 0f;

            if (result.PeakStructuralStress >= 1f) high = candidate;
            else low = candidate;
        }

        return low;
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
                            Mathf.Abs(lastEstimatedFoS - estimatedFoS) > 0.005f;
        if (statsChanged)
        {
            lastRoadLength = roadLength;
            lastDeadLoad = deadLoad;
            lastLiveLoad = liveLoad;
            lastEstimatedCapacity = theoreticalCapacityKg;
            lastEfficiencyRatio = efficiencyRatio;
            lastEstimatedFoS = estimatedFoS;
            if (totalLengthText != null) totalLengthText.text = $"Road Length: {roadLength:F1}m";
            if (deadLoadText != null) deadLoadText.text = $"Dead Load: {deadLoad:F1}kg";
            
            if (targetCargoWeightText != null) targetCargoWeightText.text = $"Live Load: {liveLoad:F0}kg";
            if (estimatedCapacityText != null) estimatedCapacityText.text = $"Est. Capacity: ~{theoreticalCapacityKg:F0}kg";
            if (efficiencyRatioText != null) efficiencyRatioText.text = $"Efficiency Ratio: {efficiencyRatio:F2}";
            
            if (factorOfSafetyText != null)
            {
                if (estimatedFoS >= 2.0f) factorOfSafetyText.text = $"Est. FoS: <color=green>{estimatedFoS:F2} (Safe)</color>";
                else if (estimatedFoS >= 1.0f) factorOfSafetyText.text = $"Est. FoS: <color=yellow>{estimatedFoS:F2} (Risky)</color>";
                else factorOfSafetyText.text = $"Est. FoS: <color=red>{estimatedFoS:F2} (Will Fail)</color>";
            }
        }

        if (displayM != lastDisplayM || displayJ != lastDisplayJ)
        {
            lastDisplayM = displayM;
            lastDisplayJ = displayJ;
            if (membersCountText != null) membersCountText.text = $"Members (M): {displayM} | Joints (J): {displayJ}";
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
        playPauseButtonImage.sprite = physicsManager.isSimulating ? (stopIcon != null ? stopIcon : playPauseButtonImage.sprite) : (playIcon != null ? playIcon : playPauseButtonImage.sprite);
    }

    private void UpdateStressUI()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            // The in-test visualizer follows the current smoothed load so players
            // can see stress rise and fall as the vehicle crosses the bridge. The
            // manager still records the run peak separately for result scoring.
            float currentStress = physicsManager.GetMaxBridgeStress();
            float stressPercent = currentStress * 100f;

            Color currentStressColor = currentStress <= 0.5f ?
                Color.Lerp(safeStressColor, warningStressColor, currentStress * 2f) :
                Color.Lerp(warningStressColor, criticalStressColor, (currentStress - 0.5f) * 2f);

            if (stressFillBar != null) 
            { 
                stressFillBar.fillAmount = currentStress;
                stressFillBar.color = currentStressColor; 
            }

            if (!Mathf.Approximately(stressPercent, lastStressPercent))
            {
                lastStressPercent = stressPercent;
                if (stressText != null) 
                { 
                    stressText.text = $"{stressPercent:0.0}%";
                    stressText.color = currentStressColor; 
                }
            }
        }
        else
        {
            if (!Mathf.Approximately(lastStressPercent, 0f))
            {
                lastStressPercent = 0f;
                if (stressText != null) { stressText.text = "0.0%"; stressText.color = safeStressColor; }
            }
            if (stressFillBar != null) { stressFillBar.fillAmount = 0f; stressFillBar.color = safeStressColor; }
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
        if (statsPanel != null) statsPanel.SetActive(!statsPanel.activeSelf);
        MarkBridgeDirty();
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

        // Inspect every Button under the container rather than only its wrapper.
        // GetComponentsInChildren(true) still works while the panel itself is hidden,
        // allowing a newly re-enabled child button to bring the container back.
        Button[] containedButtons = simulationControlsPanel.GetComponentsInChildren<Button>(true);
        bool shouldShowPanel = false;
        foreach (Button button in containedButtons)
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
        if (containedButtons.Length == 0)
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

        Button[] childButtons = controlObject.GetComponentsInChildren<Button>(true);
        foreach (Button button in childButtons)
        {
            if (button != null && IsLocallyVisibleWithinPanel(
                    button.transform,
                    simulationControlsPanel.transform))
                return true;
        }

        return childButtons.Length == 0 && IsLocallyVisibleWithinPanel(
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
