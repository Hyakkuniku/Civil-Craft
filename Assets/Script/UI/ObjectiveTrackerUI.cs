using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI; 

public class ObjectiveTrackerUI : MonoBehaviour
{
    public static ObjectiveTrackerUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject trackerPanel; 
    
    // NEW: The button that stays on the screen to open the panel
    public GameObject openTrackerButton; 
    
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI budgetText;
    public TextMeshProUGUI weightText; 
    public GameObject completeButton; 

    [Header("Other UI to Hide")]
    public List<GameObject> otherUIElements = new List<GameObject>();

    private bool hasActiveObjective = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        if (trackerPanel != null) trackerPanel.SetActive(false);
        
        // NEW: Hide the open button at the start since we don't have a contract yet
        if (openTrackerButton != null) openTrackerButton.SetActive(false);
    }

    public void SetObjective(ContractSO contract)
    {
        if (contract == null) return;

        hasActiveObjective = true;

        if (titleText != null) titleText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (budgetText != null) budgetText.text = "Budget: $" + contract.budget;
        if (weightText != null) weightText.text = "Cargo Weight: " + contract.cargoWeight + "kg";
        
        if (completeButton != null) completeButton.SetActive(false); 

        // Automatically open the panel
        if (trackerPanel != null) 
        {
            trackerPanel.SetActive(true);
            SetOtherUIActive(false); 
            
            // Hide the open button while the panel is open
            if (openTrackerButton != null) openTrackerButton.SetActive(false);
        }
    }

    public void ShowCompleteButton()
    {
        if (completeButton != null) completeButton.SetActive(true);
    }

    public void OnCompleteButtonClicked()
    {
        ClearObjective();
    }

    public void ClearObjective()
    {
        hasActiveObjective = false;
        
        if (trackerPanel != null) 
        {
            trackerPanel.SetActive(false);
            SetOtherUIActive(true); 
            
            // Hide the open button because we no longer have an active mission
            if (openTrackerButton != null) openTrackerButton.SetActive(false);
        }
    }

    // ────────────────────────────────────────────────────────────
    // Both of your buttons will call this exact same method!
    // ────────────────────────────────────────────────────────────
    public void ToggleTrackerPanel()
    {
        if (trackerPanel != null && hasActiveObjective)
        {
            bool isNowActive = !trackerPanel.activeSelf;
            trackerPanel.SetActive(isNowActive);
            SetOtherUIActive(!isNowActive);
            
            // If the panel is now OFF, show the Open button. If it is ON, hide it.
            if (openTrackerButton != null) openTrackerButton.SetActive(!isNowActive);
        }
    }

    private void SetOtherUIActive(bool isActive)
    {
        foreach (GameObject uiElement in otherUIElements)
        {
            if (uiElement != null)
            {
                uiElement.SetActive(isActive);
            }
        }
    }
}