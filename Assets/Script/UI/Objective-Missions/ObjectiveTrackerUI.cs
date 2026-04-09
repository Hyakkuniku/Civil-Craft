using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI; 

public class ObjectiveTrackerUI : MonoBehaviour
{
    public static ObjectiveTrackerUI Instance { get; private set; }

    // --- NEW: Data structure to hold ANY type of quest! ---
    [System.Serializable]
    public class TrackedTask
    {
        public string title;
        public string description;
        public bool isTutorial;
        public ContractSO contract; 
        public NPCContractGiver npc; 
        
        public bool isReadyToTurnIn;
        public int pendingGold;
        public int pendingExp;
    }

    [Header("HUD Alert Notification")]
    [Tooltip("The button on the player screen to open the mission tab.")]
    public GameObject openTrackerButton; 
    [Tooltip("A little red dot or '!' that turns on when a new quest is added or completed.")]
    public GameObject newAlertIcon; 

    [Header("Mission List UI (Left Side)")]
    [Tooltip("The main full-screen/large panel.")]
    public GameObject trackerPanel; 
    [Tooltip("The Content object of your Scroll View.")]
    public Transform questListContent;
    [Tooltip("The Prefab with the ObjectiveTabButton script on it.")]
    public GameObject questTabPrefab;

    [Header("Mission Details UI (Right Side)")]
    public GameObject detailsPanel; // Hide this if no quest is selected!
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

    // The master list of all ongoing quests
    private List<TrackedTask> activeTasks = new List<TrackedTask>();
    private TrackedTask currentlySelectedTask; 

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        if (trackerPanel != null) trackerPanel.SetActive(false);
        if (detailsPanel != null) detailsPanel.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // ADDING QUESTS
    // ─────────────────────────────────────────────────────────────

    // For your standard Bridge Contracts
    public void SetObjective(ContractSO contract)
    {
        if (contract == null) return;

        // Prevent adding duplicates
        if (activeTasks.Exists(t => t.contract == contract)) return;

        TrackedTask newTask = new TrackedTask
        {
            title = contract.clientName + "'s Request",
            description = contract.jobDescription,
            contract = contract,
            isTutorial = false,
            isReadyToTurnIn = false
        };

        activeTasks.Add(newTask);
        AlertPlayer();
        RefreshQuestList();
    }

    // NEW: For tutorial steps or generic RPG fetch quests!
    public void AddGenericTask(string taskTitle, string taskDescription)
    {
        // Prevent adding duplicates
        if (activeTasks.Exists(t => t.title == taskTitle)) return;

        TrackedTask newTask = new TrackedTask
        {
            title = taskTitle,
            description = taskDescription,
            isTutorial = true,
            isReadyToTurnIn = false
        };

        activeTasks.Add(newTask);
        AlertPlayer();
        RefreshQuestList();
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATING & COMPLETING QUESTS
    // ─────────────────────────────────────────────────────────────

    // Updates a specific contract to show it is ready for payout
    public void ShowCompleteButton(int gold, int exp, NPCContractGiver npc)
    {
        if (npc == null || npc.contractToGive == null) return;

        TrackedTask taskToComplete = activeTasks.Find(t => t.contract == npc.contractToGive);
        
        if (taskToComplete != null)
        {
            taskToComplete.isReadyToTurnIn = true;
            taskToComplete.pendingGold = gold;
            taskToComplete.pendingExp = exp;
            taskToComplete.npc = npc;

            AlertPlayer();
            RefreshQuestList();

            // If we are currently looking at this exact task, refresh the details panel to show the Complete button!
            if (currentlySelectedTask == taskToComplete)
            {
                SelectTask(taskToComplete);
            }
        }
    }

    // Tied to the physical "Complete" UI button on the right panel
    public void OnCompleteButtonClicked()
    {
        if (currentlySelectedTask == null) return;

        // If it's a bridge contract, pay the player and save the data
        if (!currentlySelectedTask.isTutorial && currentlySelectedTask.contract != null)
        {
            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.AddGold(currentlySelectedTask.pendingGold);
                PlayerDataManager.Instance.AddExp(currentlySelectedTask.pendingExp);
                PlayerDataManager.Instance.AddBridgeBuilt();
                PlayerDataManager.Instance.CompleteContract(currentlySelectedTask.contract.name);
            }

            if (LevelCompleteManager.Instance != null)
            {
                LevelCompleteManager.Instance.MarkContractAsPaid(currentlySelectedTask.contract.name);
            }

            if (currentlySelectedTask.npc != null)
            {
                currentlySelectedTask.npc.isFullyTurnedIn = true;
            }
        }

        // Remove the task from the list entirely
        activeTasks.Remove(currentlySelectedTask);
        ClearObjective(); // Hides the details panel
        RefreshQuestList();
    }

    // Can be called to silently remove a quest (e.g., if auto-collected)
    public void ClearObjective(ContractSO specificContract = null)
    {
        if (specificContract != null)
        {
            activeTasks.RemoveAll(t => t.contract == specificContract);
            RefreshQuestList();
            
            // If we deleted the one we were looking at, hide the details
            if (currentlySelectedTask != null && currentlySelectedTask.contract == specificContract)
            {
                currentlySelectedTask = null;
                if (detailsPanel != null) detailsPanel.SetActive(false);
            }
        }
        else
        {
            // If no specific contract is passed, just clear the Details Panel
            currentlySelectedTask = null; 
            if (detailsPanel != null) detailsPanel.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // UI MANAGEMENT
    // ─────────────────────────────────────────────────────────────

    private void AlertPlayer()
    {
        if (newAlertIcon != null) newAlertIcon.SetActive(true);
        if (openTrackerButton != null && !openTrackerButton.activeSelf) openTrackerButton.SetActive(true);
    }

    public void ToggleTrackerPanel()
    {
        if (trackerPanel != null)
        {
            bool isNowActive = !trackerPanel.activeSelf;
            trackerPanel.SetActive(isNowActive);
            SetOtherUIActive(!isNowActive);
            
            if (isNowActive)
            {
                if (newAlertIcon != null) newAlertIcon.SetActive(false); // Clear the alert
                RefreshQuestList();
                
                // Auto-select the first quest if we have one and haven't selected one
                if (currentlySelectedTask == null && activeTasks.Count > 0)
                {
                    SelectTask(activeTasks[0]);
                }
            }
            else
            {
                if (openTrackerButton != null) openTrackerButton.SetActive(activeTasks.Count > 0);
            }
        }
    }

    private void RefreshQuestList()
    {
        if (questListContent == null || questTabPrefab == null) return;

        // Clear old buttons
        foreach (Transform child in questListContent) Destroy(child.gameObject);

        // Spawn new buttons
        foreach (TrackedTask task in activeTasks)
        {
            GameObject btnObj = Instantiate(questTabPrefab, questListContent);
            ObjectiveTabButton btnScript = btnObj.GetComponent<ObjectiveTabButton>();
            
            if (btnScript != null)
            {
                btnScript.Setup(task);
            }
        }
    }

    public void SelectTask(TrackedTask task)
    {
        if (task == null) return;
        currentlySelectedTask = task;

        if (detailsPanel != null) detailsPanel.SetActive(true);

        if (titleText != null) titleText.text = task.title;
        if (descriptionText != null) descriptionText.text = task.description;

        // Only show Bridge constraints if it's an actual bridge contract
        if (!task.isTutorial && task.contract != null)
        {
            if (budgetText != null) { budgetText.gameObject.SetActive(true); budgetText.text = "Budget: $" + task.contract.budget; }
            if (weightText != null) { weightText.gameObject.SetActive(true); weightText.text = "Live Load: " + task.contract.liveLoadWeight + "kg"; }
        }
        else
        {
            if (budgetText != null) budgetText.gameObject.SetActive(false);
            if (weightText != null) weightText.gameObject.SetActive(false);
        }
        
        // Show Rewards & Complete button IF it's ready to turn in
        if (task.isReadyToTurnIn)
        {
            if (rewardContainer != null) rewardContainer.SetActive(true);
            if (rewardGoldText != null) rewardGoldText.text = $"+{task.pendingGold} Gold";
            if (rewardExpText != null) rewardExpText.text = $"+{task.pendingExp} EXP";
            if (completeButton != null) completeButton.SetActive(true);
        }
        else
        {
            if (rewardContainer != null) rewardContainer.SetActive(false);
            if (completeButton != null) completeButton.SetActive(false);
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