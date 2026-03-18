using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using System.Collections.Generic;

public class BuildUIController : MonoBehaviour
{
    public static BuildUIController Instance { get; private set; }

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
    public TextMeshProUGUI totalLengthText; 
    public TextMeshProUGUI membersCountText;  
    public TextMeshProUGUI deadLoadText;  
    public TextMeshProUGUI targetCargoWeightText; 
    public TextMeshProUGUI estimatedCapacityText;
    public TextMeshProUGUI efficiencyRatioText;
    public TextMeshProUGUI factorOfSafetyText; 

    private void Awake() { Instance = this; }

    private void Start()
    {
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();
        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();
    }

    private void Update()
    {
        if (useKeyboardShortcuts)
        {
            if (Input.GetKeyDown(simulateKey)) OnSimulateButtonClicked();
            if (Input.GetKeyDown(restartKey)) OnRestartButtonClicked();
        }

        UpdateContractUI();
        UpdateStressUI();
        UpdatePlayPauseButtonUI();
        
        if (physicsManager != null && !physicsManager.isSimulating) UpdateStatsUI();
    }

    private void UpdatePlayPauseButtonUI()
    {
        if (playPauseButtonImage == null || physicsManager == null) return;
        playPauseButtonImage.sprite = physicsManager.isSimulating ? (stopIcon != null ? stopIcon : playPauseButtonImage.sprite) : (playIcon != null ? playIcon : playPauseButtonImage.sprite);
    }

    // --- THE FIX: Point this directly to the new global contract memory ---
    private ContractSO GetActiveContract()
    {
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.CurrentContract;
        }
        return null;
    }

    private void UpdateStatsUI()
    {
        HashSet<Bar> uniqueBars = new HashSet<Bar>();
        HashSet<Point> activePoints = new HashSet<Point>(); 

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

        int logicalJ = activePoints.Count;
        int displayJ = logicalJ * 2; 
        int displayM = 0;
        float roadLength = 0f;
        float deadLoad = 0f;
        float weakestStressLimit = Mathf.Infinity;

        foreach (Bar b in uniqueBars)
        {
            if (barCreator != null && barCreator.currentBar == b && barCreator.IsCreating) continue;
            
            if (b.materialData != null)
            {
                displayM += b.materialData.isDualBeam ? 2 : 1;
                if (b.materialData.isRoad) roadLength += b.currentLength;
                deadLoad += b.currentLength * b.materialData.massPerMeter;
                
                if (b.materialData.maxCompression < weakestStressLimit) weakestStressLimit = b.materialData.maxCompression;
                if (b.materialData.maxTension < weakestStressLimit) weakestStressLimit = b.materialData.maxTension;
            }
        }

        float theoreticalCapacityKg = 0f;
        if (weakestStressLimit != Mathf.Infinity && weakestStressLimit > 0)
        {
            float safetyFactor = 0.2f; 
            theoreticalCapacityKg = ((weakestStressLimit / 9.81f) * safetyFactor) - (deadLoad * 0.5f);
            if (theoreticalCapacityKg < 0) theoreticalCapacityKg = 0;
        }

        ContractSO currentContract = GetActiveContract();
        float liveLoad = currentContract != null ? currentContract.cargoWeight : 1000f;
        
        float estimatedFoS = 0f;
        if (liveLoad > 0) estimatedFoS = theoreticalCapacityKg / liveLoad;

        float efficiencyRatio = 0f;
        if (deadLoad > 0) efficiencyRatio = theoreticalCapacityKg / deadLoad;

        if (totalLengthText != null) totalLengthText.text = $"Road Length: {roadLength:F1}m";
        if (membersCountText != null) membersCountText.text = $"Members (M): {displayM} | Joints (J): {displayJ}";
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

    private void UpdateStressUI()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            float maxStress = physicsManager.GetMaxBridgeStress();
            int stressPercent = Mathf.RoundToInt(maxStress * 100f);
            float liveFoS = maxStress > 0.05f ? (1f / maxStress) : 99.9f;

            Color currentStressColor = maxStress <= 0.5f ? 
                Color.Lerp(safeStressColor, warningStressColor, maxStress * 2f) : 
                Color.Lerp(warningStressColor, criticalStressColor, (maxStress - 0.5f) * 2f);

            if (stressText != null) { stressText.text = liveFoS > 99f ? $"{stressPercent}% (FoS: ∞)" : $"{stressPercent}% (FoS: {liveFoS:F1})"; stressText.color = currentStressColor; }
            if (stressFillBar != null) { stressFillBar.fillAmount = maxStress; stressFillBar.color = currentStressColor; }
        }
        else
        {
            if (stressText != null) { stressText.text = "0% (FoS: ∞)"; stressText.color = safeStressColor; }
            if (stressFillBar != null) { stressFillBar.fillAmount = 0f; stressFillBar.color = safeStressColor; }
        }
    }

    public float GetTotalCost()
    {
        float totalCost = 0f;
        HashSet<Bar> uniqueBars = new HashSet<Bar>();
        foreach (Point p in Point.AllPoints) foreach (Bar b in p.ConnectedBars) if (b != null && b.gameObject.activeSelf) uniqueBars.Add(b);
        foreach (Bar b in uniqueBars) if (!(barCreator != null && barCreator.currentBar == b && barCreator.IsCreating)) totalCost += b.GetCost();
        return totalCost;
    }

    private void UpdateContractUI()
    {
        ContractSO currentContract = GetActiveContract();
        maxBudget = currentContract != null ? currentContract.budget : fallbackMaxBudget;

        float baseCost = GetTotalCost();
        float previewCost = 0f;
        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null) previewCost = barCreator.currentBar.GetCost();
        float totalProjectedCost = baseCost + previewCost;

        if (usedBudgetText != null) { usedBudgetText.text = $" ${Mathf.RoundToInt(totalProjectedCost)}"; usedBudgetText.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor; }
        if (maxBudgetText != null) maxBudgetText.text = $" ${Mathf.RoundToInt(maxBudget)}";
        if (budgetFillBar != null) { budgetFillBar.fillAmount = totalProjectedCost / maxBudget; budgetFillBar.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor; }
    }

    public void OnToggleSimulationButtonClicked()
    {
        if (physicsManager == null) return;
        if (physicsManager.isSimulating) OnRestartButtonClicked(); else OnSimulateButtonClicked();
    }

    public void OnSimulateButtonClicked() { if (physicsManager != null && !physicsManager.isSimulating) { if (barCreator != null) { barCreator.CancelCreation(); barCreator.isSimulating = true; } physicsManager.ActivatePhysics(); } }
    public void OnRestartButtonClicked() { if (physicsManager != null && physicsManager.isSimulating) { physicsManager.StopPhysicsAndReset(); if (barCreator != null) barCreator.isSimulating = false; } }
    public void OnToggleGridButtonClicked() { if (barCreator != null) barCreator.ToggleGrid(); }
    public void OnCancelDrawingButtonClicked() { if (barCreator != null) barCreator.CancelCreation(); }
    public void OnExitBuildModeButtonClicked() { if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode(); }
    public void OnMaterialSelected(BridgeMaterialSO newMaterial) { if (barCreator != null) { barCreator.isDeleteMode = false; barCreator.SetActiveMaterial(newMaterial); } }
    public void OnToggleDeleteModeButtonClicked() { if (barCreator != null) barCreator.ToggleDeleteMode(); }
    public void OnUndoButtonClicked() { if (barCreator != null) barCreator.Undo(); }
    public void OnRedoButtonClicked() { if (barCreator != null) barCreator.Redo(); }
}