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
    
    [Header("Final Payout UI")]
    public GameObject completeButton; 
    public GameObject rewardContainer; 
    public TextMeshProUGUI rewardGoldText; 
    public TextMeshProUGUI rewardExpText;  

    [Header("Other UI to Hide")]
    public List<GameObject> otherUIElements = new List<GameObject>();

    private bool hasActiveObjective = false;
    
    private int finalGoldPayout = 0;
    private int finalExpPayout = 0;

    private ContractSO currentTrackedContract;
    private NPCContractGiver currentNPC; // <-- Safely hold the exact NPC we are talking to

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        if (trackerPanel != null) trackerPanel.SetActive(false);
        if (openTrackerButton != null) openTrackerButton.SetActive(false);
        if (rewardContainer != null) rewardContainer.SetActive(false);
    }

    public void SetObjective(ContractSO contract)
    {
        if (contract == null) return;

        hasActiveObjective = true;
        currentTrackedContract = contract; 

        if (titleText != null) titleText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (budgetText != null) budgetText.text = "Budget: $" + contract.budget;
        if (weightText != null) weightText.text = "Live Load: " + contract.liveLoadWeight + "kg";
        
        if (completeButton != null) completeButton.SetActive(false); 
        if (rewardContainer != null) rewardContainer.SetActive(false); 

        if (trackerPanel != null) 
        {
            trackerPanel.SetActive(true);
            SetOtherUIActive(false); 
            if (openTrackerButton != null) openTrackerButton.SetActive(false);
        }
    }

    // --- NEW: Requires the NPC so we can officially dismiss them ---
    public void ShowCompleteButton(int gold, int exp, NPCContractGiver npc)
    {
        finalGoldPayout = gold;
        finalExpPayout = exp;
        currentNPC = npc;

        if (rewardContainer != null) rewardContainer.SetActive(true);
        if (rewardGoldText != null) rewardGoldText.text = $"+{gold} Gold";
        if (rewardExpText != null) rewardExpText.text = $"+{exp} EXP";

        if (completeButton != null) completeButton.SetActive(true);
        
        if (trackerPanel != null && !trackerPanel.activeSelf) ToggleTrackerPanel();
    }

    public void OnCompleteButtonClicked()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.AddGold(finalGoldPayout);
            PlayerDataManager.Instance.AddExp(finalExpPayout);
            PlayerDataManager.Instance.AddBridgeBuilt();
        }

        if (LevelCompleteManager.Instance != null && currentTrackedContract != null)
        {
            LevelCompleteManager.Instance.MarkContractAsPaid(currentTrackedContract.name);
        }

        // Retire the specific NPC that handed us the money
        if (currentNPC != null)
        {
            currentNPC.isFullyTurnedIn = true;
        }

        ClearObjective();
    }

    public void ClearObjective()
    {
        hasActiveObjective = false;
        currentTrackedContract = null; 
        currentNPC = null;
        
        if (rewardContainer != null) rewardContainer.SetActive(false);
        if (completeButton != null) completeButton.SetActive(false);
        
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