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

    [Header("Budget Visualization")]
    public float maxBudget = 1000f;
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
    [Tooltip("Shows total meters of the ROAD used")]
    public TextMeshProUGUI totalLengthText; 
    [Tooltip("Shows true 3D number of members (M) and joints (J)")]
    public TextMeshProUGUI membersCountText;  
    [Tooltip("The physical weight of the bridge itself")]
    public TextMeshProUGUI deadLoadText;  
    [Tooltip("The estimated theoretical max weight")]
    public TextMeshProUGUI estimatedCapacityText;
    [Tooltip("Ratio of Capacity vs Dead Load")]
    public TextMeshProUGUI efficiencyRatioText;
    [Tooltip("Calculates M = 2J - 3 to find redundancy")]
    public TextMeshProUGUI determinacyText;

    private void Awake()
    {
        Instance = this;
    }

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

        UpdateBudgetUI();
        UpdateStressUI(); 
        
        if (!physicsManager.isSimulating) 
        {
            UpdateStatsUI();
        }
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

        int logicalM = uniqueBars.Count;
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

                // --- THE FIX: Now using your explicit isRoad checkbox! ---
                if (b.materialData.isRoad)
                {
                    roadLength += b.currentLength;
                }

                deadLoad += b.currentLength * b.materialData.massPerMeter;
                
                if (b.materialData.maxCompression < weakestStressLimit) weakestStressLimit = b.materialData.maxCompression;
                if (b.materialData.maxTension < weakestStressLimit) weakestStressLimit = b.materialData.maxTension;
            }
        }

        float theoreticalCapacityKg = 0f;
        if (weakestStressLimit != Mathf.Infinity && weakestStressLimit > 0)
        {
            theoreticalCapacityKg = (weakestStressLimit / 9.81f) - (deadLoad * 0.5f);
            if (theoreticalCapacityKg < 0) theoreticalCapacityKg = 0; 
        }

        float efficiencyRatio = 0f;
        if (deadLoad > 0) efficiencyRatio = theoreticalCapacityKg / deadLoad;

        string determinacyString = "N/A";
        if (logicalJ >= 3) 
        {
            int redundancy = logicalM - ((2 * logicalJ) - 3);
            
            if (redundancy == 0) determinacyString = "<color=green>Determinate (Perfect)</color>";
            else if (redundancy > 0) determinacyString = $"<color=yellow>Indeterminate (+{redundancy} Redundant)</color>";
            else determinacyString = $"<color=red>Unstable ({redundancy} Members)</color>";
        }

        if (totalLengthText != null) totalLengthText.text = $"Road Length: {roadLength:F1}m";
        if (membersCountText != null) membersCountText.text = $"Members (M): {displayM} | Joints (J): {displayJ}";
        if (deadLoadText != null) deadLoadText.text = $"Dead Load: {deadLoad:F1}kg";
        if (estimatedCapacityText != null) estimatedCapacityText.text = $"Est. Ultimate Capacity: ~{theoreticalCapacityKg:F0}kg";
        
        if (efficiencyRatioText != null) efficiencyRatioText.text = $"Efficiency Ratio: {efficiencyRatio:F2}";
        if (determinacyText != null) determinacyText.text = $"Statical Determinacy: {determinacyString}";
    }

    private void UpdateStressUI()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            float maxStress = physicsManager.GetMaxBridgeStress();
            int stressPercent = Mathf.RoundToInt(maxStress * 100f);

            Color currentStressColor = safeStressColor;
            if (maxStress <= 0.5f)
                currentStressColor = Color.Lerp(safeStressColor, warningStressColor, maxStress * 2f);
            else
                currentStressColor = Color.Lerp(warningStressColor, criticalStressColor, (maxStress - 0.5f) * 2f);

            if (stressText != null)
            {
                stressText.text = $"{stressPercent}%";
                stressText.color = currentStressColor;
            }

            if (stressFillBar != null)
            {
                stressFillBar.fillAmount = maxStress;
                stressFillBar.color = currentStressColor;
            }
        }
        else
        {
            if (stressText != null)
            {
                stressText.text = "0%";
                stressText.color = safeStressColor;
            }
            if (stressFillBar != null)
            {
                stressFillBar.fillAmount = 0f;
                stressFillBar.color = safeStressColor;
            }
        }
    }

    public float GetTotalCost()
    {
        float totalCost = 0f;
        HashSet<Bar> uniqueBars = new HashSet<Bar>();

        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf) 
                    uniqueBars.Add(b);
            }
        }

        foreach (Bar b in uniqueBars)
        {
            if (barCreator != null && barCreator.currentBar == b && barCreator.IsCreating) continue;
            totalCost += b.GetCost();
        }

        return totalCost;
    }

    private void UpdateBudgetUI()
    {
        float baseCost = GetTotalCost();
        float previewCost = 0f;

        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null)
        {
            previewCost = barCreator.currentBar.GetCost();
        }

        float totalProjectedCost = baseCost + previewCost;

        if (usedBudgetText != null)
        {
            usedBudgetText.text = $" ${Mathf.RoundToInt(totalProjectedCost)}";
            usedBudgetText.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor;
        }

        if (maxBudgetText != null)
        {
            maxBudgetText.text = $" ${Mathf.RoundToInt(maxBudget)}";
        }

        if (budgetFillBar != null)
        {
            budgetFillBar.fillAmount = totalProjectedCost / maxBudget;
            budgetFillBar.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor;
        }
    }

    public void OnSimulateButtonClicked()
    {
        if (physicsManager != null && !physicsManager.isSimulating)
        {
            if (barCreator != null) 
            {
                barCreator.CancelCreation();
                barCreator.isSimulating = true;
            }
            physicsManager.ActivatePhysics();
        }
    }

    public void OnRestartButtonClicked()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            physicsManager.StopPhysicsAndReset();
            if (barCreator != null) barCreator.isSimulating = false;
        }
    }

    public void OnToggleGridButtonClicked()
    {
        if (barCreator != null) barCreator.ToggleGrid();
    }

    public void OnCancelDrawingButtonClicked()
    {
        if (barCreator != null) barCreator.CancelCreation();
    }

    public void OnExitBuildModeButtonClicked()
    {
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
    }

    public void OnMaterialSelected(BridgeMaterialSO newMaterial)
    {
        if (barCreator != null)
        {
            barCreator.isDeleteMode = false;
            barCreator.SetActiveMaterial(newMaterial);
        }
    }

    public void OnToggleDeleteModeButtonClicked()
    {
        if (barCreator != null) barCreator.ToggleDeleteMode();
    }

    public void OnUndoButtonClicked()
    {
        if (barCreator != null) barCreator.Undo();
    }

    public void OnRedoButtonClicked()
    {
        if (barCreator != null) barCreator.Redo();
    }
}