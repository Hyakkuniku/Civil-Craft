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

    private List<GameObject> temporarilyHiddenSimUI = new List<GameObject>();
    private bool simulationInProgressForUI;

    private float cachedBaseCost = 0f;
    private float cachedBaseDeadLoad = 0f;
    private int cachedBaseM = 0;
    private int cachedBaseJ = 0;
    private float cachedBaseRoadLength = 0f;
    private float cachedBaseWeakestStress = Mathf.Infinity;

    private HashSet<Bar> uniqueBars = new HashSet<Bar>();
    private HashSet<Point> activePoints = new HashSet<Point>();

    private int lastStressPercent = -1;
    private int lastProjectedCost = -1;
    private float lastRoadLength = -1f;
    private int lastDisplayM = -1;
    private int lastDisplayJ = -1;

    private Dictionary<BridgeMaterialSO, int> materialUsageCount = new Dictionary<BridgeMaterialSO, int>();

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
    }

    public void RefreshAllMaterialButtons()
    {
        MaterialButtonTrigger[] allButtons = FindObjectsOfType<MaterialButtonTrigger>(true);
        foreach (var b in allButtons) 
        {
            b.EvaluateMaterialRestriction();
        }
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
                UpdateStatsUI();
                UpdateContractUI();
            }
        }
        
        UpdateLiveBeamStatsUI();
        UpdatePlayPauseButtonUI();
        
        UpdateToolHighlights();
    }

    private void LateUpdate()
    {
        RefreshSimulationPanelVisibility();
    }

    public void RefreshContractBuildUI()
    {
        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;

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

        RefreshAllMaterialButtons();
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
                PlayerDataManager.Instance.UnlockMaterialForContract(GameManager.Instance.CurrentContract.name, pendingUnlockButton.buttonMaterial.name);

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
                var selectedPoints = barCreator.GetSelectedPoints();
                HashSet<Bar> affectedBars = new HashSet<Bar>();
                foreach (Point p in selectedPoints)
                {
                    foreach (Bar b in p.ConnectedBars) if (b != null && b.gameObject.activeSelf) affectedBars.Add(b);
                }
                if (affectedBars.Count == 1)
                {
                    var enumerator = affectedBars.GetEnumerator();
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
            if (liveBeamLengthText != null) liveBeamLengthText.text = $"{targetBar.currentLength:F2}m";
            if (liveBeamCostText != null) liveBeamCostText.text = $"₱{targetBar.GetCost():F0}";
            if (liveBeamAngleText != null) liveBeamAngleText.text = $"{targetBar.currentAngle:F1}°";
        }
        else
        {
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
        RecalculateStaticBridge(); 
        UpdateStatsUI();
        UpdateContractUI();

        RefreshAllMaterialButtons(); 
    }

    private void RecalculateStaticBridge()
    {
        uniqueBars.Clear();
        activePoints.Clear();
        materialUsageCount.Clear(); 

        foreach (Point p in Point.AllPoints)
        {
            if (!p.gameObject.activeSelf || !p.enabled) continue;
            bool hasActiveBar = false;
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf) 
                {
                    uniqueBars.Add(b);
                    hasActiveBar = true;
                }
            }
            if (hasActiveBar) activePoints.Add(p);
        }

        ContractSO activeContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        if (activeContract != null)
        {
            BuildLocation targetLoc = null;
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            foreach (var loc in allLocs)
            {
                if (loc.gameObject.scene.name != null && loc.activeContract == activeContract)
                {
                    targetLoc = loc;
                    break;
                }
            }

            if (targetLoc != null)
            {
                foreach (Bar b in targetLoc.bakedBars)
                {
                    if (b != null && b.gameObject.activeSelf)
                    {
                        uniqueBars.Add(b);
                        if (b.startPoint != null) activePoints.Add(b.startPoint);
                        if (b.endPoint != null) activePoints.Add(b.endPoint);
                    }
                }

                HashSet<Point> visitedPoints = new HashSet<Point>();
                Queue<Point> queue = new Queue<Point>();

                foreach (Point anchor in targetLoc.startingAnchors)
                {
                    if (anchor != null) { visitedPoints.Add(anchor); queue.Enqueue(anchor); activePoints.Add(anchor); }
                }
                foreach (Point anchor in targetLoc.endingAnchors)
                {
                    if (anchor != null && !visitedPoints.Contains(anchor)) { visitedPoints.Add(anchor); queue.Enqueue(anchor); activePoints.Add(anchor); }
                }

                while (queue.Count > 0)
                {
                    Point current = queue.Dequeue();
                    foreach (Bar b in current.ConnectedBars)
                    {
                        if (b != null && b.gameObject.activeSelf)
                        {
                            uniqueBars.Add(b);
                            Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                            if (neighbor != null && !visitedPoints.Contains(neighbor))
                            {
                                visitedPoints.Add(neighbor);
                                queue.Enqueue(neighbor);
                                activePoints.Add(neighbor);
                            }
                        }
                    }
                }
            }
        }

        cachedBaseJ = activePoints.Count * 2; 
        cachedBaseM = 0;
        cachedBaseRoadLength = 0f;
        cachedBaseDeadLoad = 0f;
        cachedBaseWeakestStress = Mathf.Infinity;
        cachedBaseCost = 0f;

        foreach (Bar b in uniqueBars)
        {
            if (barCreator != null && barCreator.currentBar == b && barCreator.IsCreating) continue;
            
            cachedBaseCost += b.GetCost();

            if (b.materialData != null)
            {
                if (!materialUsageCount.ContainsKey(b.materialData)) materialUsageCount[b.materialData] = 0;
                materialUsageCount[b.materialData]++;

                cachedBaseM += b.materialData.isDualBeam ? 2 : 1;
                if (b.materialData.isRoad) cachedBaseRoadLength += b.currentLength;
                cachedBaseDeadLoad += b.currentLength * b.materialData.massPerMeter;
                
                if (b.materialData.maxCompression < cachedBaseWeakestStress) cachedBaseWeakestStress = b.materialData.maxCompression;
                if (b.materialData.maxTension < cachedBaseWeakestStress) cachedBaseWeakestStress = b.materialData.maxTension;
            }
        }
        
        lastRoadLength = -1f; 
        lastDisplayM = -1;
    }

    private void UpdateStatsUI()
    {
        int displayJ = cachedBaseJ; 
        int displayM = cachedBaseM;
        float roadLength = cachedBaseRoadLength;
        float deadLoad = cachedBaseDeadLoad;
        float weakestStressLimit = cachedBaseWeakestStress;

        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null && barCreator.currentBar.materialData != null)
        {
            Bar preview = barCreator.currentBar;
            displayM += preview.materialData.isDualBeam ? 2 : 1;
            if (preview.materialData.isRoad) roadLength += preview.currentLength;
            deadLoad += preview.currentLength * preview.materialData.massPerMeter;
            
            if (preview.materialData.maxCompression < weakestStressLimit) weakestStressLimit = preview.materialData.maxCompression;
            if (preview.materialData.maxTension < weakestStressLimit) weakestStressLimit = preview.materialData.maxTension;
        }

        float theoreticalCapacityKg = 0f;
        if (weakestStressLimit != Mathf.Infinity && weakestStressLimit > 0)
        {
            float safetyFactor = 0.2f; 
            theoreticalCapacityKg = ((weakestStressLimit / 9.81f) * safetyFactor) - (deadLoad * 0.5f);
            if (theoreticalCapacityKg < 0) theoreticalCapacityKg = 0;
        }

        ContractSO currentContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        float liveLoad = currentContract != null ? currentContract.liveLoadWeight : 1000f;
        
        float estimatedFoS = 0f;
        if (liveLoad > 0) estimatedFoS = theoreticalCapacityKg / liveLoad;

        float efficiencyRatio = 0f;
        if (deadLoad > 0) efficiencyRatio = theoreticalCapacityKg / deadLoad;

        if (Mathf.Abs(lastRoadLength - roadLength) > 0.05f)
        {
            lastRoadLength = roadLength;
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

        float baseCost = GetTotalCost();
        float previewCost = 0f;
        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null) previewCost = barCreator.currentBar.GetCost();
        
        int totalProjectedCost = Mathf.RoundToInt(baseCost + previewCost);

        if (budgetFillBar != null) 
        { 
            budgetFillBar.fillAmount = totalProjectedCost / maxBudget; 
            budgetFillBar.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor; 
        }

        if (totalProjectedCost != lastProjectedCost)
        {
            lastProjectedCost = totalProjectedCost;
            if (usedBudgetText != null) 
            { 
                usedBudgetText.text = $" ₱{totalProjectedCost}";
                usedBudgetText.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor; 
            }
            if (maxBudgetText != null) maxBudgetText.text = $" ₱{Mathf.RoundToInt(maxBudget)}";
        }
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
            float maxStress = physicsManager.GetMaxBridgeStress();
            int stressPercent = Mathf.RoundToInt(maxStress * 100f);

            Color currentStressColor = maxStress <= 0.5f ? 
                Color.Lerp(safeStressColor, warningStressColor, maxStress * 2f) : 
                Color.Lerp(warningStressColor, criticalStressColor, (maxStress - 0.5f) * 2f);

            if (stressFillBar != null) 
            { 
                stressFillBar.fillAmount = maxStress; 
                stressFillBar.color = currentStressColor; 
            }

            if (stressPercent != lastStressPercent)
            {
                lastStressPercent = stressPercent;
                if (stressText != null) 
                { 
                    stressText.text = $"{stressPercent}%"; 
                    stressText.color = currentStressColor; 
                }
            }
        }
        else
        {
            if (lastStressPercent != 0)
            {
                lastStressPercent = 0;
                if (stressText != null) { stressText.text = "0%"; stressText.color = safeStressColor; }
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

    public void OnToggleSelectModeButtonClicked() { if (!IsToolAllowed()) return; if (barCreator != null) barCreator.ToggleSelectMode(); }
    public void OnToggleMoveModeButtonClicked() { if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (barCreator != null) barCreator.ToggleMoveMode(); }
    public void OnToggleDeleteModeButtonClicked() { if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (barCreator != null) barCreator.ToggleDeleteMode(); }
    public void OnToggleGridButtonClicked() { if (!IsToolAllowed()) return; if (barCreator != null) barCreator.ToggleGrid(); }
    public void OnCancelDrawingButtonClicked() { if (!IsToolAllowed()) return; if (barCreator != null) barCreator.CancelCreation(); }
    public void OnExitBuildModeButtonClicked() { if (!IsToolAllowed()) return; if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode(); }
    public void OnResetCameraButtonClicked() { if (!IsToolAllowed()) return; BuildCameraController camCtrl = FindObjectOfType<BuildCameraController>(); if (camCtrl != null) camCtrl.ResetCameraRotation(); }
    public void OnToggleStatsButtonClicked() { if (!IsToolAllowed()) return; if (statsPanel != null) statsPanel.SetActive(!statsPanel.activeSelf); }
    public void OnCutSelectedButtonClicked() { if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (ClipboardManager.Instance != null && barCreator != null) ClipboardManager.Instance.CutSelected(barCreator.GetSelectedPoints()); }
    public void OnCopyButtonClicked() { if (!IsToolAllowed()) return; if (ClipboardManager.Instance != null && barCreator != null) ClipboardManager.Instance.CopySelected(barCreator.GetSelectedPoints()); }
    public void OnPasteButtonClicked() { if (!IsToolAllowed()) return; if (ClipboardManager.Instance != null) ClipboardManager.Instance.StampPaste(); }
    public void OnUndoButtonClicked()
    {
        BuildTutorialDirector director = BuildTutorialDirector.Instance;
        if (director == null || !director.IsAwaitingInvalidBarUndo)
        {
            if (!IsToolAllowed()) return;
        }

        if (CommandManager.Instance != null) CommandManager.Instance.Undo();
        if (director != null) director.NotifyUndoCompleted();
    }
    public void OnRedoButtonClicked() { if (!IsToolAllowed()) return; if (CommandManager.Instance != null) CommandManager.Instance.Redo(); }
    public void OnDeleteSelectedButtonClicked() { if (!IsToolAllowed() || IsTopologyEditBlockedDuringTracing()) return; if (barCreator != null) barCreator.DeleteSelected(); }

    public void OnToggleSimulationButtonClicked() 
    { 
        if (physicsManager == null) return; 
        if (physicsManager.isSimulating) OnRestartButtonClicked(); 
        else OnSimulateButtonClicked(); 
    }
    
    public void OnSimulateButtonClicked() 
    { 
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
            if (barCreator != null) { barCreator.CancelAllModes(); barCreator.isSimulating = true; } 
            SetSelectionPanelActive(false);
            physicsManager.ActivatePhysics(); 
            LogAction("Simulation Started");
        } 
    }
    
    public void OnRestartButtonClicked() 
    { 
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
        if (simulationButton == null && playPauseButtonImage != null)
            simulationButton = playPauseButtonImage.GetComponentInParent<Button>(true);

        if (simulationButton == null) return;

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


        RefreshSimulationPanelVisibility();
    }

    public void RefreshSimulationPanelVisibility()
    {
        if (simulationControlsPanel == null) return;

        GameObject playObject = playSimulationButtonObject != null
            ? playSimulationButtonObject
            : simulationButton != null ? simulationButton.gameObject : null;
        GameObject stopObject = stopSimulationButtonObject != null
            ? stopSimulationButtonObject
            : tutorialSimulationStopObject;

        bool hasUsablePlayControl = IsSimulationControlUsable(playObject);
        bool hasUsableStopControl = IsSimulationControlUsable(stopObject);
        bool shouldShowPanel = hasUsablePlayControl || hasUsableStopControl;

        if (simulationControlsPanel.activeSelf != shouldShowPanel)
            simulationControlsPanel.SetActive(shouldShowPanel);
    }

    private static bool IsSimulationControlUsable(GameObject controlObject)
    {
        if (controlObject == null || !controlObject.activeSelf) return false;

        Button button = controlObject.GetComponent<Button>();
        if (button == null) button = controlObject.GetComponentInChildren<Button>(true);
        return button == null || button.interactable;
    }

    private static bool IsTutorialContractActive()
    {
        return GameManager.Instance != null &&
               GameManager.Instance.CurrentContract != null &&
               GameManager.Instance.CurrentContract.isTutorialContract;
    }

    public void OnMaterialSelected(BridgeMaterialSO newMaterial) 
    { 
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
}
