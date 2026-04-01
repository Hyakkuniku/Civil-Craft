using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using System.Collections.Generic;
using System.Collections; 

public class BuildUIController : MonoBehaviour
{
    public static BuildUIController Instance { get; private set; }

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
    public Image playPauseButtonImage; 
    public Sprite playIcon;            
    public Sprite stopIcon;            

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

    private void Awake() { Instance = this; }

    private void Start()
    {
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();
        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        if (selectionActionPanel != null) selectionActionPanel.SetActive(false);
        if (statsPanel != null) statsPanel.SetActive(false); 
        
        if (liveBeamStatsPanel != null) liveBeamStatsPanel.SetActive(false);

        if (actionLogText != null) actionLogText.text = ""; 

        MarkBridgeDirty();
    }

    private void Update()
    {
        if (useKeyboardShortcuts)
        {
            if (Input.GetKeyDown(simulateKey)) OnSimulateButtonClicked();
            if (Input.GetKeyDown(restartKey)) OnRestartButtonClicked();
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
    }

    private void UpdateLiveBeamStatsUI()
    {
        Bar targetBar = null;

        if (barCreator != null)
        {
            // Scenario 1: We are actively drawing a brand new beam
            if (barCreator.IsCreating && barCreator.currentBar != null)
            {
                targetBar = barCreator.currentBar;
            }
            // Scenario 2: We are actively moving an existing node using the Move Tool
            else if (barCreator.IsMoving && barCreator.isDraggingSelection)
            {
                var selectedPoints = barCreator.GetSelectedPoints();
                HashSet<Bar> affectedBars = new HashSet<Bar>();
                foreach (Point p in selectedPoints)
                {
                    foreach (Bar b in p.ConnectedBars)
                    {
                        if (b != null && b.gameObject.activeSelf)
                        {
                            affectedBars.Add(b);
                        }
                    }
                }

                if (affectedBars.Count == 1)
                {
                    var enumerator = affectedBars.GetEnumerator();
                    enumerator.MoveNext();
                    targetBar = enumerator.Current;
                }
            }
            // Scenario 3 (NEW!): We just tapped a single beam in Select Mode!
            else if (barCreator.IsSelecting && barCreator.selectedBars.Count == 1 && barCreator.selectedPoints.Count == 0)
            {
                targetBar = barCreator.selectedBars[0];
            }
        }

        // Output to the panel if we have a valid beam
        if (targetBar != null && targetBar.materialData != null)
        {
            if (liveBeamStatsPanel != null && !liveBeamStatsPanel.activeSelf) 
                liveBeamStatsPanel.SetActive(true);

            if (liveBeamLengthText != null) 
                liveBeamLengthText.text = $"{targetBar.currentLength:F2}m";
                
            if (liveBeamCostText != null) 
                liveBeamCostText.text = $"${targetBar.GetCost():F0}";
                
            if (liveBeamAngleText != null) 
                liveBeamAngleText.text = $"{targetBar.currentAngle:F1}°";
        }
        else
        {
            if (liveBeamStatsPanel != null && liveBeamStatsPanel.activeSelf) 
                liveBeamStatsPanel.SetActive(false);
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
    }

    private void RecalculateStaticBridge()
    {
        uniqueBars.Clear();
        activePoints.Clear();

        foreach (Point p in Point.AllPoints)
        {
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

        ContractSO currentContract = GetActiveContract();
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
        ContractSO currentContract = GetActiveContract();
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
                usedBudgetText.text = $" ${totalProjectedCost}"; 
                usedBudgetText.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor; 
            }
            if (maxBudgetText != null) maxBudgetText.text = $" ${Mathf.RoundToInt(maxBudget)}";
        }
    }

    private void UpdatePlayPauseButtonUI()
    {
        if (playPauseButtonImage == null || physicsManager == null) return;
        playPauseButtonImage.sprite = physicsManager.isSimulating ? (stopIcon != null ? stopIcon : playPauseButtonImage.sprite) : (playIcon != null ? playIcon : playPauseButtonImage.sprite);
    }

    private ContractSO GetActiveContract()
    {
        if (GameManager.Instance != null) return GameManager.Instance.CurrentContract;
        return null;
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

    public void OnResetCameraButtonClicked() { BuildCameraController camCtrl = FindObjectOfType<BuildCameraController>(); if (camCtrl != null) camCtrl.ResetCameraRotation(); LogAction("Camera Reset"); }
    
    public void OnToggleStatsButtonClicked() 
    { 
        if (statsPanel != null) statsPanel.SetActive(!statsPanel.activeSelf); 
        LogAction(statsPanel != null && statsPanel.activeSelf ? "Stats Panel Opened" : "Stats Panel Closed"); 
    }
    
    public void OnSimulateButtonClicked() 
    { 
        if (physicsManager != null && !physicsManager.isSimulating) 
        { 
            if (barCreator != null) { barCreator.CancelAllModes(); barCreator.isSimulating = true; } 
            SetSelectionPanelActive(false);
            physicsManager.ActivatePhysics(); 
            LogAction("Simulation Started");
        } 
    }
    
    public void OnCutSelectedButtonClicked() { if (ClipboardManager.Instance != null && barCreator != null) ClipboardManager.Instance.CutSelected(barCreator.GetSelectedPoints()); }
    public void OnCopyButtonClicked() { if (ClipboardManager.Instance != null && barCreator != null) ClipboardManager.Instance.CopySelected(barCreator.GetSelectedPoints()); }
    public void OnPasteButtonClicked() { if (ClipboardManager.Instance != null) ClipboardManager.Instance.StampPaste(); }
    public void OnUndoButtonClicked() { if (CommandManager.Instance != null) CommandManager.Instance.Undo(); }
    public void OnRedoButtonClicked() { if (CommandManager.Instance != null) CommandManager.Instance.Redo(); }
    public void OnDeleteSelectedButtonClicked() { if (barCreator != null) barCreator.DeleteSelected(); }

    public void OnToggleSimulationButtonClicked() { if (physicsManager == null) return; if (physicsManager.isSimulating) OnRestartButtonClicked(); else OnSimulateButtonClicked(); }
    public void OnToggleSelectModeButtonClicked() { if (barCreator != null) barCreator.ToggleSelectMode(); }
    public void OnToggleMoveModeButtonClicked() { if (barCreator != null) barCreator.ToggleMoveMode(); }
    
    public void OnRestartButtonClicked() 
    { 
        if (physicsManager != null && physicsManager.isSimulating) 
        { 
            physicsManager.StopPhysicsAndReset(); 
            if (barCreator != null) barCreator.isSimulating = false; 
            LogAction("Simulation Stopped");
        } 
    }

    public void OnToggleGridButtonClicked() { if (barCreator != null) barCreator.ToggleGrid(); }
    public void OnCancelDrawingButtonClicked() { if (barCreator != null) barCreator.CancelCreation(); }
    public void OnExitBuildModeButtonClicked() { if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode(); }
    
    public void OnMaterialSelected(BridgeMaterialSO newMaterial) 
    { 
        if (barCreator != null) 
        { 
            barCreator.isDeleteMode = false; 
            barCreator.SetActiveMaterial(newMaterial); 
            SetSelectionPanelActive(false);
            LogAction($"Selected Material: {newMaterial.name}");
        } 
    }

    public void OnToggleDeleteModeButtonClicked() { if (barCreator != null) barCreator.ToggleDeleteMode(); }
}