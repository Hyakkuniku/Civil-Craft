using UnityEngine;
using UnityEngine.UI; // We still need this for the Image (Fill Bar)
using TMPro; // ADDED: This lets us use TextMeshPro!
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

    // --- UPDATED: Now using TextMeshProUGUI ---
    [Header("Budget Visualization")]
    public float maxBudget = 1000f;
    [Tooltip("Text element above the bar showing how much is spent.")]
    public TextMeshProUGUI usedBudgetText; 
    [Tooltip("The fill bar image.")]
    public Image budgetFillBar; 
    [Tooltip("Text element below the bar showing the limit.")]
    public TextMeshProUGUI maxBudgetText; 
    
    public Color normalTextColor = Color.white;
    public Color overBudgetTextColor = Color.red;

    // --- UPDATED: Now using TextMeshProUGUI ---
    [Header("Stress Visualization")]
    public TextMeshProUGUI stressText;
    public Image stressFillBar;
    public Color safeStressColor = Color.green;
    public Color warningStressColor = Color.yellow;
    public Color criticalStressColor = Color.red;

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