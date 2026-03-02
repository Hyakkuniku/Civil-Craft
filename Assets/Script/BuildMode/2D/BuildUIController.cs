using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class BuildUIController : MonoBehaviour
{
    // ADDED: Singleton access so the BarCreator can easily read the budget
    public static BuildUIController Instance { get; private set; }

    [Header("System References")]
    public BarCreator barCreator;
    public BridgePhysicsManager physicsManager;

    [Header("Global Keyboard Shortcuts")]
    public bool useKeyboardShortcuts = true;
    public KeyCode simulateKey = KeyCode.Return;   
    public KeyCode restartKey = KeyCode.Backspace; 

    // ADDED: Budget Visualization System
    [Header("Budget Visualization")]
    public float maxBudget = 1000f;
    [Tooltip("Text element to display Budget (e.g., 'Budget: $400 / $1000')")]
    public TextMeshProUGUI budgetText; 
    [Tooltip("Optional image fill bar to visually represent spent budget")]
    public Image budgetFillBar; 
    public Color normalTextColor = Color.white;
    public Color overBudgetTextColor = Color.red;

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

        // ADDED: Update UI every frame to catch real-time drawing costs
        UpdateBudgetUI();
    }

    // ADDED: Core logic to calculate the total cost of placed bridge parts
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
            // Don't count the active "ghost" preview bar; we handle that below
            if (barCreator != null && barCreator.currentBar == b && barCreator.IsCreating) continue;
            totalCost += b.GetCost();
        }

        return totalCost;
    }

    // ADDED: Pushes the calculated costs into the UI elements
    private void UpdateBudgetUI()
    {
        float baseCost = GetTotalCost();
        float previewCost = 0f;

        if (barCreator != null && barCreator.IsCreating && barCreator.currentBar != null)
        {
            previewCost = barCreator.currentBar.GetCost();
        }

        float totalProjectedCost = baseCost + previewCost;

        if (budgetText != null)
        {
            budgetText.text = $"Budget: ${Mathf.RoundToInt(totalProjectedCost)} / ${Mathf.RoundToInt(maxBudget)}";
            budgetText.color = totalProjectedCost > maxBudget ? overBudgetTextColor : normalTextColor;
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