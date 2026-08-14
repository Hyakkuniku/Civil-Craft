using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI; 
using System.Linq; 

public class ObjectiveTrackerUI : MonoBehaviour
{
    public static ObjectiveTrackerUI Instance { get; private set; }

    [Header("HUD Alert Notification")]
    public GameObject openTrackerButton; 
    public GameObject newAlertIcon; 

    [Header("Main Menu Base")]
    public GameObject trackerPanel; 

    [Header("State 1: Mission List")]
    public GameObject listPanel; 
    public Transform questListContent;
    public GameObject questTabPrefab;

    [Header("State 2: Mission Details")]
    public GameObject detailsPanel; 
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI budgetText;
    public TextMeshProUGUI weightText; 

    [Header("Navigation UI")]
    public GameObject navigateButton;

    [Header("Final Payout UI")]
    public GameObject completeButton; 
    public GameObject rewardContainer; 
    public TextMeshProUGUI rewardGoldText; 
    public TextMeshProUGUI rewardExpText;  

    [Header("Other UI to Hide")]
    public List<GameObject> otherUIElements = new List<GameObject>();

    private TrackedTask currentlySelectedTask; 

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        if (trackerPanel != null) trackerPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(false);
        if (detailsPanel != null) detailsPanel.SetActive(false);
        ClearAlert();
    }

    private void Start()
    {
        if (openTrackerButton != null && PlayerDataManager.Instance != null)
        {
            openTrackerButton.SetActive(PlayerDataManager.Instance.CurrentData.hasUnlockedObjectiveTracker);
        }

        ClearAlert();
        RefreshQuestList();
    }

    private void Update()
    {
        // --- SAFEGUARD: Forcibly keep alert icon OFF whenever the tracker panel is open ---
        if (trackerPanel != null && trackerPanel.activeSelf && newAlertIcon != null && newAlertIcon.activeSelf)
        {
            ClearAlert();
        }
    }

    public void ClearAlert()
    {
        if (newAlertIcon != null)
        {
            newAlertIcon.SetActive(false);
        }
    }

    private void AlertPlayer()
    {
        // Do NOT turn on the alert icon if the player is currently viewing the tracker panel!
        if (trackerPanel != null && trackerPanel.activeSelf) return;

        if (newAlertIcon != null)
        {
            newAlertIcon.SetActive(true);
        }
    }

    private void UnlockAndShowTracker()
    {
        if (PlayerDataManager.Instance != null && !PlayerDataManager.Instance.CurrentData.hasUnlockedObjectiveTracker)
        {
            PlayerDataManager.Instance.CurrentData.hasUnlockedObjectiveTracker = true;
            if (openTrackerButton != null) openTrackerButton.SetActive(true);
        }
    }

    public void SetObjective(ContractSO contract, string targetName = "")
    {
        if (contract == null || PlayerDataManager.Instance == null) return;
        
        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        if (activeTasks.Exists(t => t.contractName == contract.name)) return;

        TrackedTask newTask = new TrackedTask
        {
            title = contract.clientName + "'s Request",
            description = contract.jobDescription,
            contractName = contract.name,
            budget = contract.budget,
            weight = contract.liveLoadWeight,
            isTutorial = false,
            isReadyToTurnIn = false,
            isCompleted = false,
            targetWaypointName = targetName 
        };

        activeTasks.Add(newTask);
        
        UnlockAndShowTracker(); 
        PlayerDataManager.Instance.SaveGame(); 
        
        AlertPlayer();
        RefreshQuestList();
    }

    public void AddGenericTask(string taskTitle, string taskDescription, string targetName = "")
    {
        if (PlayerDataManager.Instance == null) return;
        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        if (activeTasks.Exists(t => t.title == taskTitle)) return;

        TrackedTask newTask = new TrackedTask
        {
            title = taskTitle,
            description = taskDescription,
            isTutorial = true,
            isReadyToTurnIn = false,
            isCompleted = false,
            targetWaypointName = targetName 
        };

        activeTasks.Add(newTask);
        
        UnlockAndShowTracker(); 
        PlayerDataManager.Instance.SaveGame();
        
        AlertPlayer();
        RefreshQuestList();
    }

    public void CompleteGenericTask(string taskTitle)
    {
        if (PlayerDataManager.Instance == null) return;
        
        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        TrackedTask taskToComplete = activeTasks.Find(t => t.title == taskTitle);
        
        if (taskToComplete != null && !taskToComplete.isCompleted)
        {
            taskToComplete.isCompleted = true;
            taskToComplete.isReadyToTurnIn = false;
            
            PlayerDataManager.Instance.SaveGame();
            RefreshQuestList();

            if (currentlySelectedTask == taskToComplete)
            {
                SelectTask(taskToComplete);
            }
            
            Debug.Log($"<color=green>Generic Task Completed: {taskTitle}</color>");
        }
    }

    public void NotifyBridgeBuilt(string contractName)
    {
        if (PlayerDataManager.Instance == null) return;

        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        TrackedTask task = activeTasks.Find(t => t.contractName == contractName);

        if (task != null && !task.isCompleted && !task.isReadyToTurnIn)
        {
            AlertPlayer();
            
            task.description = "Bridge successfully built! Return to the client to claim your reward.";

            NPCContractGiver[] npcs = Resources.FindObjectsOfTypeAll<NPCContractGiver>();
            foreach (var npc in npcs)
            {
                if (npc.gameObject.scene.name != null && npc.contractToGive != null && npc.contractToGive.name == contractName)
                {
                    task.targetWaypointName = npc.gameObject.name;
                    break;
                }
            }

            PlayerDataManager.Instance.SaveGame();
            RefreshQuestList();

            if (currentlySelectedTask == task)
            {
                SelectTask(task);
            }
        }
    }

    public void ShowCompleteButton(int gold, int exp, NPCContractGiver npc)
    {
        if (npc == null || npc.contractToGive == null || PlayerDataManager.Instance == null) return;

        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        TrackedTask taskToComplete = activeTasks.Find(t => t.contractName == npc.contractToGive.name);
        
        if (taskToComplete != null && !taskToComplete.isCompleted)
        {
            taskToComplete.isReadyToTurnIn = true;
            taskToComplete.pendingGold = gold;
            taskToComplete.pendingExp = exp;
            
            taskToComplete.targetWaypointName = ""; 
            
            PlayerDataManager.Instance.SaveGame();
            RefreshQuestList();

            if (trackerPanel != null && !trackerPanel.activeSelf)
            {
                ToggleTrackerPanel();
            }

            SelectTask(taskToComplete);
            
            if (PathGuider.Instance != null) PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());
        }
    }

    public void OnCompleteButtonClicked()
    {
        if (currentlySelectedTask == null || currentlySelectedTask.isCompleted) return;

        if (!currentlySelectedTask.isTutorial && !string.IsNullOrEmpty(currentlySelectedTask.contractName))
        {
            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.AddGold(currentlySelectedTask.pendingGold);
                PlayerDataManager.Instance.AddExp(currentlySelectedTask.pendingExp);
                PlayerDataManager.Instance.AddBridgeBuilt();
                PlayerDataManager.Instance.CompleteContract(currentlySelectedTask.contractName);
            }

            if (LevelCompleteManager.Instance != null)
            {
                LevelCompleteManager.Instance.MarkContractAsPaid(currentlySelectedTask.contractName);
            }

            NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
            foreach(var npc in npcs)
            {
                if (npc.contractToGive != null && npc.contractToGive.name == currentlySelectedTask.contractName)
                {
                    npc.isFullyTurnedIn = true;
                }
            }
        }

        currentlySelectedTask.isCompleted = true;
        currentlySelectedTask.isReadyToTurnIn = false;
        
        if (PathGuider.Instance != null) PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());
        
        PlayerDataManager.Instance.SaveGame();
        
        ClearAlert();
        SelectTask(currentlySelectedTask);
        RefreshQuestList();
    }

    public void ClearObjective(ContractSO specificContract = null)
    {
        if (specificContract != null && PlayerDataManager.Instance != null)
        {
            var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
            var tasksToComplete = activeTasks.FindAll(t => t.contractName == specificContract.name);
            
            foreach(var t in tasksToComplete)
            {
                t.isCompleted = true;
                t.isReadyToTurnIn = false;
            }
            
            if (PathGuider.Instance != null) PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());
            
            PlayerDataManager.Instance.SaveGame();
            RefreshQuestList();
            
            if (currentlySelectedTask != null && currentlySelectedTask.contractName == specificContract.name)
            {
                SelectTask(currentlySelectedTask); 
            }
        }
        else
        {
            OnBackButtonClicked();
        }
    }

    public void OnNavigateButtonClicked()
    {
        if (currentlySelectedTask == null) return;

        GameObject targetObj = null;

        if (!currentlySelectedTask.isTutorial && !string.IsNullOrEmpty(currentlySelectedTask.contractName) && PlayerDataManager.Instance != null)
        {
            bool isBridgeBuilt = PlayerDataManager.Instance.GetSavedBridge(currentlySelectedTask.contractName) != null;

            if (isBridgeBuilt)
            {
                NPCContractGiver[] npcs = Resources.FindObjectsOfTypeAll<NPCContractGiver>();
                foreach (var npc in npcs)
                {
                    if (npc.gameObject.scene.name != null && npc.contractToGive != null && npc.contractToGive.name == currentlySelectedTask.contractName)
                    {
                        targetObj = npc.gameObject;
                        break;
                    }
                }
            }
            else
            {
                BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
                foreach (var loc in allLocs)
                {
                    if (loc.gameObject.scene.name != null && loc.activeContract != null && loc.activeContract.name == currentlySelectedTask.contractName)
                    {
                        targetObj = loc.navigationTarget != null ? loc.navigationTarget : loc.gameObject;
                        break;
                    }
                }
            }
        }

        if (targetObj == null && !string.IsNullOrEmpty(currentlySelectedTask.targetWaypointName))
        {
            targetObj = GameObject.Find(currentlySelectedTask.targetWaypointName);
        }
        
        if (targetObj != null && PathGuider.Instance != null)
        {
            PathGuider.Instance.RouteToSingleTarget(targetObj.transform);
            ToggleTrackerPanel(); 
        }
        else
        {
            Debug.LogWarning("Could not find the navigation target in the active scene!");
        }
    }

    public void ToggleTrackerPanel()
    {
        if (trackerPanel != null)
        {
            bool isNowActive = !trackerPanel.activeSelf;
            trackerPanel.SetActive(isNowActive);
            
            if (openTrackerButton != null) openTrackerButton.SetActive(!isNowActive);
            
            SetOtherUIActive(!isNowActive);
            ClearAlert();
            
            if (isNowActive)
            {
                currentlySelectedTask = null;
                if (detailsPanel != null) detailsPanel.SetActive(false);
                if (listPanel != null) listPanel.SetActive(true);
                
                RefreshQuestList();
            }
        }
    }

    public void OnBackButtonClicked()
    {
        currentlySelectedTask = null;
        
        if (detailsPanel != null) detailsPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);
        
        ClearAlert();
        RefreshQuestList();
    }

    private void RefreshQuestList()
    {
        if (questListContent == null || questTabPrefab == null || PlayerDataManager.Instance == null) return;

        for (int i = questListContent.childCount - 1; i >= 0; i--)
        {
            Transform child = questListContent.GetChild(i);
            child.SetParent(null); 
            Destroy(child.gameObject);
        }

        var allTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        
        var activeList = allTasks.Where(t => !t.isCompleted)
                                 .OrderByDescending(t => t.isReadyToTurnIn)
                                 .ToList();

        var doneList = allTasks.Where(t => t.isCompleted).ToList();
        doneList.Reverse(); 

        activeList.AddRange(doneList);

        foreach (TrackedTask task in activeList)
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

        ClearAlert();

        if (listPanel != null) listPanel.SetActive(false);
        if (detailsPanel != null) detailsPanel.SetActive(true);

        if (titleText != null) titleText.text = task.isCompleted ? "[Done] " + task.title : task.title;
        if (descriptionText != null) descriptionText.text = task.description;

        if (!task.isTutorial && !string.IsNullOrEmpty(task.contractName))
        {
            if (budgetText != null) { budgetText.gameObject.SetActive(true); budgetText.text = "Budget: $" + task.budget; }
            if (weightText != null) { weightText.gameObject.SetActive(true); weightText.text = "Live Load: " + task.weight + "kg"; }
        }
        else
        {
            if (budgetText != null) budgetText.gameObject.SetActive(false);
            if (weightText != null) weightText.gameObject.SetActive(false);
        }
        
        if (task.isReadyToTurnIn && !task.isCompleted)
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

        if (navigateButton != null)
        {
            bool canNavigate = false;

            if (!task.isCompleted && !task.isReadyToTurnIn)
            {
                if (!task.isTutorial && !string.IsNullOrEmpty(task.contractName) && PlayerDataManager.Instance != null)
                {
                    bool isBridgeBuilt = PlayerDataManager.Instance.GetSavedBridge(task.contractName) != null;

                    if (isBridgeBuilt)
                    {
                        NPCContractGiver[] npcs = Resources.FindObjectsOfTypeAll<NPCContractGiver>();
                        foreach (var npc in npcs)
                        {
                            if (npc.gameObject.scene.name != null && npc.contractToGive != null && npc.contractToGive.name == task.contractName)
                            {
                                canNavigate = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
                        foreach (var loc in allLocs)
                        {
                            if (loc.gameObject.scene.name != null && loc.activeContract != null && loc.activeContract.name == task.contractName)
                            {
                                canNavigate = true; 
                                break;
                            }
                        }
                    }
                }
                
                if (!canNavigate && !string.IsNullOrEmpty(task.targetWaypointName))
                {
                    GameObject targetObj = GameObject.Find(task.targetWaypointName);
                    if (targetObj != null) canNavigate = true;
                }
            }

            navigateButton.SetActive(canNavigate);
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