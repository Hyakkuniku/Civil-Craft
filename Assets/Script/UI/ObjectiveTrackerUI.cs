using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI; 

public class ObjectiveTrackerUI : MonoBehaviour
{
    public static ObjectiveTrackerUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject trackerPanel; 
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
        if (openTrackerButton != null) openTrackerButton.SetActive(false);
    }

    public void SetObjective(ContractSO contract)
    {
        if (contract == null) return;

        hasActiveObjective = true;

        if (titleText != null) titleText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (budgetText != null) budgetText.text = "Budget: $" + contract.budget;
        
        // --- THE FIX: Changed to Live Load ---
        if (weightText != null) weightText.text = "Live Load: " + contract.liveLoadWeight + "kg";
        
        if (completeButton != null) completeButton.SetActive(false); 

        if (trackerPanel != null) 
        {
            trackerPanel.SetActive(true);
            SetOtherUIActive(false); 
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
            if (openTrackerButton != null) openTrackerButton.SetActive(false);
        }
    }

    public void ToggleTrackerPanel()
    {
        if (trackerPanel != null && hasActiveObjective)
        {
            bool isNowActive = !trackerPanel.activeSelf;
            trackerPanel.SetActive(isNowActive);
            SetOtherUIActive(!isNowActive);
            
            if (openTrackerButton != null) openTrackerButton.SetActive(!isNowActive);
        }
    }

    private void SetOtherUIActive(bool isActive)
    {
        foreach (GameObject uiElement in otherUIElements)
        {
            if (uiElement != null) uiElement.SetActive(isActive);
        }
    }
}