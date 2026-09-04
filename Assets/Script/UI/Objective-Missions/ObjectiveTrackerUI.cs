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

    [Header("Shared UI Theme")]
    [Tooltip("Use the same rounded panel sprite used by the achievement and profile cards.")]
    public Sprite roundedPanelSprite;

    private TrackedTask currentlySelectedTask;
    private ContractSO pendingContractOffer;
    private TrackedTask pendingContractPreview;
    private string pendingContractTargetName = string.Empty;
    private System.Action pendingContractAccepted;
    private System.Action pendingContractCancelled;

    private GameObject contractOfferLayout;
    private ScrollRect offerMaterialsScrollRect;
    private RectTransform offerMaterialsContent;
    private TextMeshProUGUI offerTitleLabel;
    private TextMeshProUGUI offerDescriptionLabel;
    private TextMeshProUGUI offerBudgetValue;
    private TextMeshProUGUI offerLoadValue;
    private TextMeshProUGUI offerSpanValue;
    private TextMeshProUGUI offerRewardValue;

    private static readonly Color32 FrameColor = new Color32(88, 57, 35, 255);
    private static readonly Color32 ClipColor = new Color32(67, 42, 27, 255);
    private static readonly Color32 ClipFaceColor = new Color32(139, 94, 55, 255);
    private static readonly Color32 SurfaceColor = new Color32(255, 244, 216, 250);
    private static readonly Color32 PrimaryTextColor = new Color32(75, 46, 29, 255);
    private static readonly Color32 SecondaryTextColor = new Color32(119, 88, 61, 255);
    private static readonly Color32 LightTextColor = new Color32(255, 246, 222, 255);
    private static readonly Color32 TrackButtonColor = new Color32(217, 126, 36, 255);
    private static readonly Color32 AcceptButtonColor = new Color32(113, 177, 44, 255);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        ApplySharedVisualStyle();
        
        if (trackerPanel != null) trackerPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(false);
        if (detailsPanel != null) detailsPanel.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false);
    }

    private void Start()
    {
        if (openTrackerButton != null && PlayerDataManager.Instance != null)
        {
            openTrackerButton.SetActive(PlayerDataManager.Instance.CurrentData.hasUnlockedObjectiveTracker);
        }

        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnObjectiveAlertsChanged += RefreshAlertIcon;
        RefreshAlertIcon();
        RefreshQuestList();
    }

    private void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnObjectiveAlertsChanged -= RefreshAlertIcon;
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
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.ClearObjectiveAlert();

        RefreshAlertIcon();
    }

    private void AlertPlayer()
    {
        // Content that arrives while the tracker is open is already visible/read.
        if (trackerPanel != null && trackerPanel.activeSelf)
        {
            ClearAlert();
            return;
        }

        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.MarkObjectiveAlertUnread();

        RefreshAlertIcon();
    }

    private void RefreshAlertIcon()
    {
        bool trackerIsOpen = trackerPanel != null && trackerPanel.activeSelf;
        bool hasUnreadUpdate = PlayerDataManager.Instance != null &&
                               PlayerDataManager.Instance.CurrentData != null &&
                               PlayerDataManager.Instance.CurrentData.hasUnreadObjectiveAlert;

        if (newAlertIcon != null)
            newAlertIcon.SetActive(hasUnreadUpdate && !trackerIsOpen);
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
        if (activeTasks.Exists(t => contract.MatchesIdentifier(t.contractName))) return;

        TrackedTask newTask = CreateContractTask(contract, targetName, contract.jobDescription);

        activeTasks.Add(newTask);

        UnlockAndShowTracker();
        PlayerDataManager.Instance.SaveGame();

        AlertPlayer();
        RefreshQuestList();
    }

    /// <summary>
    /// Opens the objective clipboard as a confirmation screen without saving the
    /// contract. The contract is only added when the player presses Accept Contract.
    /// </summary>
    public bool ShowContractOffer(
        ContractSO contract,
        string targetName,
        System.Action onAccepted,
        System.Action onCancelled = null)
    {
        if (contract == null || trackerPanel == null || PlayerDataManager.Instance == null)
            return false;

        CancelPendingContractOffer();

        pendingContractOffer = contract;
        pendingContractTargetName = targetName ?? string.Empty;
        pendingContractAccepted = onAccepted;
        pendingContractCancelled = onCancelled;
        pendingContractPreview = CreateContractTask(
            contract,
            pendingContractTargetName,
            contract.jobDescription);

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(trackerPanel);
        else
        {
            trackerPanel.SetActive(true);
            SetOtherUIActive(false);
        }

        if (openTrackerButton != null) openTrackerButton.SetActive(false);
        if (listPanel != null) listPanel.SetActive(false);
        if (detailsPanel != null) detailsPanel.SetActive(true);

        ClearAlert();
        SelectTask(pendingContractPreview);
        return true;
    }

    private static TrackedTask CreateContractTask(
        ContractSO contract,
        string targetName,
        string description)
    {
        return new TrackedTask
        {
            title = contract.clientName + "'s Request",
            description = description,
            contractName = contract.ContractID,
            budget = contract.budget,
            weight = contract.liveLoadWeight,
            isTutorial = false,
            isReadyToTurnIn = false,
            isCompleted = false,
            targetWaypointName = targetName
        };
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

        // Simulation can finish again while redesigning an already paid contract.
        // That is not new objective content and must not create another unread alert.
        if (PlayerDataManager.Instance.IsContractCompleted(contractName))
            return;

        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        TrackedTask task = activeTasks.Find(t => t.contractName == contractName);

        if (task != null && !task.isCompleted && !task.isReadyToTurnIn)
        {
            task.description = "Bridge successfully built! Return to the client to claim your reward.";

            NPCContractGiver[] npcs = Resources.FindObjectsOfTypeAll<NPCContractGiver>();
            foreach (var npc in npcs)
            {
                if (npc.gameObject.scene.name != null && npc.contractToGive != null &&
                    npc.contractToGive.MatchesIdentifier(contractName))
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

        // Alert even if the task was already updated by another completion callback.
        // The persistent flag makes this reliable across the following scene transition.
        AlertPlayer();
    }

    public void ShowCompleteButton(int gold, int exp, NPCContractGiver npc)
    {
        if (npc == null || npc.contractToGive == null || PlayerDataManager.Instance == null) return;

        var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
        TrackedTask taskToComplete = activeTasks.Find(t =>
            npc.contractToGive.MatchesIdentifier(t.contractName));
        
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

        if (!string.IsNullOrEmpty(currentlySelectedTask.contractName))
        {
            if (PlayerDataManager.Instance != null)
            {
                // Tutorial tasks still need to persist contract completion so
                // the contract behaves like a normal contract on future runs.
                // They intentionally do not grant the normal contract payout.
                int goldReward = currentlySelectedTask.isTutorial
                    ? 0
                    : currentlySelectedTask.pendingGold;
                int expReward = currentlySelectedTask.isTutorial
                    ? 0
                    : currentlySelectedTask.pendingExp;

                bool completionSaved = PlayerDataManager.Instance.CompleteContract(
                    currentlySelectedTask.contractName,
                    goldReward,
                    expReward);

                if (!completionSaved &&
                    !PlayerDataManager.Instance.IsContractCompleted(currentlySelectedTask.contractName))
                {
                    Debug.LogError(
                        $"[ObjectiveTrackerUI] Cannot turn in '{currentlySelectedTask.contractName}' because its saved bridge is missing or invalid.",
                        this);
                    return;
                }
            }

            if (LevelCompleteManager.Instance != null)
            {
                LevelCompleteManager.Instance.MarkContractAsPaid(currentlySelectedTask.contractName);
            }

            if (!currentlySelectedTask.isTutorial)
            {
                NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
                foreach(var npc in npcs)
                {
                    if (npc.contractToGive != null &&
                        npc.contractToGive.MatchesIdentifier(currentlySelectedTask.contractName))
                    {
                        npc.isFullyTurnedIn = true;
                    }
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
            var tasksToComplete = activeTasks.FindAll(t =>
                specificContract.MatchesIdentifier(t.contractName));
            
            foreach(var t in tasksToComplete)
            {
                t.isCompleted = true;
                t.isReadyToTurnIn = false;
            }
            
            if (PathGuider.Instance != null) PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());
            
            PlayerDataManager.Instance.SaveGame();
            RefreshQuestList();
            
            if (currentlySelectedTask != null &&
                specificContract.MatchesIdentifier(currentlySelectedTask.contractName))
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
        if (pendingContractOffer != null)
        {
            AcceptPendingContractOffer();
            return;
        }

        if (currentlySelectedTask != null &&
            currentlySelectedTask.isReadyToTurnIn &&
            !currentlySelectedTask.isCompleted)
        {
            OnCompleteButtonClicked();
            return;
        }

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
                    if (npc.gameObject.scene.name != null && npc.contractToGive != null &&
                        npc.contractToGive.MatchesIdentifier(currentlySelectedTask.contractName))
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
                    if (loc.gameObject.scene.name != null && loc.activeContract != null &&
                        loc.activeContract.MatchesIdentifier(currentlySelectedTask.contractName))
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
        if (trackerPanel == null) return;

        if (trackerPanel.activeSelf)
        {
            CloseTrackerPanel(true);
            return;
        }

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(trackerPanel);
        else
        {
            trackerPanel.SetActive(true);
            SetOtherUIActive(false);
        }

        if (openTrackerButton != null) openTrackerButton.SetActive(false);
        ClearAlert();

        currentlySelectedTask = null;
        if (detailsPanel != null) detailsPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);

        RefreshQuestList();
    }

    public void OnBackButtonClicked()
    {
        if (pendingContractOffer != null)
        {
            CloseTrackerPanel(true);
            return;
        }

        currentlySelectedTask = null;
        
        if (detailsPanel != null) detailsPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);
        
        ClearAlert();
        RefreshQuestList();
    }

    private void AcceptPendingContractOffer()
    {
        if (pendingContractOffer == null) return;

        ContractSO acceptedContract = pendingContractOffer;
        string acceptedTargetName = pendingContractTargetName;
        System.Action acceptedCallback = pendingContractAccepted;

        ClearPendingContractOfferState();
        SetObjective(acceptedContract, acceptedTargetName);
        CloseTrackerPanel(false);
        acceptedCallback?.Invoke();
    }

    private void CancelPendingContractOffer()
    {
        if (pendingContractOffer == null) return;

        System.Action cancelledCallback = pendingContractCancelled;
        ClearPendingContractOfferState();
        cancelledCallback?.Invoke();
    }

    private void ClearPendingContractOfferState()
    {
        if (ReferenceEquals(currentlySelectedTask, pendingContractPreview))
            currentlySelectedTask = null;

        pendingContractOffer = null;
        pendingContractPreview = null;
        pendingContractTargetName = string.Empty;
        pendingContractAccepted = null;
        pendingContractCancelled = null;
    }

    private void CloseTrackerPanel(bool cancelPendingOffer)
    {
        if (trackerPanel == null) return;
        if (cancelPendingOffer) CancelPendingContractOffer();

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(trackerPanel);
        else
        {
            trackerPanel.SetActive(false);
            SetOtherUIActive(true);
        }

        bool trackerUnlocked = PlayerDataManager.Instance != null &&
                               PlayerDataManager.Instance.CurrentData != null &&
                               PlayerDataManager.Instance.CurrentData.hasUnlockedObjectiveTracker;
        if (openTrackerButton != null) openTrackerButton.SetActive(trackerUnlocked);

        currentlySelectedTask = null;
        ClearAlert();
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

        if (activeList.Count == 0)
            CreateEmptyState();
    }

    public void SelectTask(TrackedTask task)
    {
        if (task == null) return;
        currentlySelectedTask = task;
        bool isContractOfferPreview =
            pendingContractOffer != null && ReferenceEquals(task, pendingContractPreview);

        ClearAlert();

        if (listPanel != null) listPanel.SetActive(false);
        if (detailsPanel != null) detailsPanel.SetActive(true);

        if (isContractOfferPreview)
        {
            ShowContractDetails(pendingContractOffer, task, true);
            return;
        }

        ContractSO selectedContract = ResolveContract(task.contractName);
        if (!task.isTutorial && selectedContract != null)
        {
            ShowContractDetails(selectedContract, task, false);
            return;
        }

        SetContractOfferLayoutActive(false);
        if (titleText != null)
        {
            titleText.gameObject.SetActive(true);
            titleText.text = task.isCompleted ? "[Done] " + task.title : task.title;
        }

        if (descriptionText != null) descriptionText.gameObject.SetActive(true);
        if (descriptionText != null) descriptionText.text = task.description;

        if (!task.isTutorial && !string.IsNullOrEmpty(task.contractName))
        {
            if (budgetText != null) { budgetText.gameObject.SetActive(true); budgetText.text = $"Budget: ₱{task.budget:N0}"; }
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
            SetCenteredRect(
                navigateButton.GetComponent<RectTransform>(),
                new Vector2(420f, 72f),
                new Vector2(0f, -305f));
            SetActionButtonLabel(navigateButton, "TRACK LOCATION");
            SetActionButtonColor(navigateButton, TrackButtonColor);
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
                            if (npc.gameObject.scene.name != null && npc.contractToGive != null &&
                                npc.contractToGive.MatchesIdentifier(task.contractName))
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
                            if (loc.gameObject.scene.name != null && loc.activeContract != null &&
                                loc.activeContract.MatchesIdentifier(task.contractName))
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

    private void ShowContractDetails(ContractSO contract, TrackedTask task, bool isOfferPreview)
    {
        if (contract == null) return;

        EnsureContractOfferLayout();
        SetContractOfferLayoutActive(true);

        if (titleText != null) titleText.gameObject.SetActive(false);
        if (descriptionText != null) descriptionText.gameObject.SetActive(false);
        if (budgetText != null) budgetText.gameObject.SetActive(false);
        if (weightText != null) weightText.gameObject.SetActive(false);
        if (rewardContainer != null) rewardContainer.SetActive(false);
        if (completeButton != null) completeButton.SetActive(false);

        string clientName = string.IsNullOrWhiteSpace(contract.clientName)
            ? "Client"
            : contract.clientName.Trim();
        string projectBrief = string.IsNullOrWhiteSpace(contract.jobDescription)
            ? "Review the project requirements before accepting this contract."
            : contract.jobDescription.Trim();
        string status = isOfferPreview
            ? "CONTRACT OFFER"
            : task != null && task.isCompleted
                ? "COMPLETED"
                : task != null && task.isReadyToTurnIn
                    ? "READY TO TURN IN"
                    : "ACTIVE CONTRACT";
        offerTitleLabel.text = status + "  •  " + clientName + "'s Request";
        offerDescriptionLabel.text =
            "<size=18><color=#765A43>PROJECT BRIEF</color></size>\n" + projectBrief;
        offerBudgetValue.text =
            $"<size=18><color=#765A43>BUDGET</color></size>\n<b>₱{contract.budget:N0}</b>";
        offerLoadValue.text =
            $"<size=18><color=#765A43>LIVE LOAD</color></size>\n<b>{contract.liveLoadWeight:N0} kg</b>";
        offerSpanValue.text =
            $"<size=18><color=#765A43>REQUIRED SPAN</color></size>\n<b>{contract.bridgeSpan:0.#} m</b>";
        int displayedGold = task != null && task.isReadyToTurnIn
            ? task.pendingGold
            : contract.goldReward;
        int displayedExp = task != null && task.isReadyToTurnIn
            ? task.pendingExp
            : contract.expReward;
        offerRewardValue.text =
            $"<b>REWARDS</b>     {displayedGold:N0} Gold     +{displayedExp:N0} EXP";

        PopulateAllowedMaterialCards(contract);

        if (navigateButton != null)
        {
            SetCenteredRect(
                navigateButton.GetComponent<RectTransform>(),
                new Vector2(420f, 72f),
                new Vector2(0f, -315f));

            if (isOfferPreview)
            {
                navigateButton.SetActive(true);
                SetActionButtonLabel(navigateButton, "ACCEPT CONTRACT");
                SetActionButtonColor(navigateButton, AcceptButtonColor);
            }
            else if (task != null && task.isReadyToTurnIn && !task.isCompleted)
            {
                navigateButton.SetActive(true);
                SetActionButtonLabel(navigateButton, "COLLECT REWARD");
                SetActionButtonColor(navigateButton, AcceptButtonColor);
            }
            else if (task != null && !task.isCompleted)
            {
                navigateButton.SetActive(CanNavigateToTask(task));
                SetActionButtonLabel(navigateButton, "TRACK LOCATION");
                SetActionButtonColor(navigateButton, TrackButtonColor);
            }
            else
            {
                navigateButton.SetActive(false);
            }
        }
    }

    private static ContractSO ResolveContract(string contractIdentifier)
    {
        if (string.IsNullOrWhiteSpace(contractIdentifier)) return null;

        ContractSO contract = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetRegisteredContract(contractIdentifier)
            : null;
        if (contract != null) return contract;

        foreach (NPCContractGiver giver in Resources.FindObjectsOfTypeAll<NPCContractGiver>())
        {
            if (giver != null && giver.contractToGive != null &&
                giver.contractToGive.MatchesIdentifier(contractIdentifier))
                return giver.contractToGive;
        }

        foreach (BuildLocation location in Resources.FindObjectsOfTypeAll<BuildLocation>())
        {
            if (location != null && location.activeContract != null &&
                location.activeContract.MatchesIdentifier(contractIdentifier))
                return location.activeContract;
        }

        return null;
    }

    private static bool CanNavigateToTask(TrackedTask task)
    {
        if (task == null || task.isCompleted || task.isReadyToTurnIn) return false;

        if (!task.isTutorial && !string.IsNullOrEmpty(task.contractName) &&
            PlayerDataManager.Instance != null)
        {
            bool isBridgeBuilt =
                PlayerDataManager.Instance.GetSavedBridge(task.contractName) != null;

            if (isBridgeBuilt)
            {
                foreach (NPCContractGiver npc in Resources.FindObjectsOfTypeAll<NPCContractGiver>())
                {
                    if (npc != null && npc.gameObject.scene.name != null &&
                        npc.contractToGive != null &&
                        npc.contractToGive.MatchesIdentifier(task.contractName))
                        return true;
                }
            }
            else
            {
                foreach (BuildLocation location in Resources.FindObjectsOfTypeAll<BuildLocation>())
                {
                    if (location != null && location.gameObject.scene.name != null &&
                        location.activeContract != null &&
                        location.activeContract.MatchesIdentifier(task.contractName))
                        return true;
                }
            }
        }

        return !string.IsNullOrEmpty(task.targetWaypointName) &&
               GameObject.Find(task.targetWaypointName) != null;
    }

    private void EnsureContractOfferLayout()
    {
        if (contractOfferLayout != null || detailsPanel == null) return;

        contractOfferLayout = new GameObject("Contract Offer Layout", typeof(RectTransform));
        contractOfferLayout.transform.SetParent(detailsPanel.transform, false);
        StretchRect(
            contractOfferLayout.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.zero);

        offerTitleLabel = CreateOfferText(
            contractOfferLayout.transform,
            "Contract Offer Title",
            38f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            new Vector2(950f, 54f),
            new Vector2(0f, 326f));

        CreateOfferDivider(
            contractOfferLayout.transform,
            "Title Divider",
            new Vector2(960f, 2f),
            new Vector2(0f, 292f));

        RectTransform descriptionSection = CreateBorderedOfferSection(
            contractOfferLayout.transform,
            "Project Brief",
            new Vector2(960f, 112f),
            new Vector2(0f, 222f));
        offerDescriptionLabel = CreateOfferText(
            descriptionSection,
            "Project Brief Text",
            25f,
            FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(920f, 88f),
            Vector2.zero);
        offerDescriptionLabel.enableWordWrapping = true;
        offerDescriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform statsSection = CreateBorderedOfferSection(
            contractOfferLayout.transform,
            "Contract Requirements",
            new Vector2(960f, 76f),
            new Vector2(0f, 120f));
        offerBudgetValue = CreateOfferText(
            statsSection, "Budget", 27f, FontStyles.Normal, TextAlignmentOptions.Center,
            new Vector2(300f, 64f), new Vector2(-320f, 0f));
        offerLoadValue = CreateOfferText(
            statsSection, "Live Load", 27f, FontStyles.Normal, TextAlignmentOptions.Center,
            new Vector2(300f, 64f), Vector2.zero);
        offerSpanValue = CreateOfferText(
            statsSection, "Required Span", 27f, FontStyles.Normal, TextAlignmentOptions.Center,
            new Vector2(300f, 64f), new Vector2(320f, 0f));
        CreateOfferDivider(statsSection, "Budget Divider", new Vector2(1.5f, 58f), new Vector2(-160f, 0f));
        CreateOfferDivider(statsSection, "Load Divider", new Vector2(1.5f, 58f), new Vector2(160f, 0f));

        RectTransform materialsSection = CreateBorderedOfferSection(
            contractOfferLayout.transform,
            "Allowed Materials",
            new Vector2(960f, 185f),
            new Vector2(0f, -25f));
        CreateOfferText(
            materialsSection,
            "Allowed Materials Header",
            23f,
            FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(600f, 28f),
            new Vector2(-150f, 72f)).text = "ALLOWED MATERIALS";
        CreateOfferText(
            materialsSection,
            "Scroll Hint",
            16f,
            FontStyles.Normal,
            TextAlignmentOptions.MidlineRight,
            new Vector2(240f, 28f),
            new Vector2(335f, 72f)).text = "DRAG TO SCROLL";
        CreateAllowedMaterialsScrollView(materialsSection);

        RectTransform rewardSection = CreateBorderedOfferSection(
            contractOfferLayout.transform,
            "Contract Rewards",
            new Vector2(960f, 64f),
            new Vector2(0f, -180f));
        offerRewardValue = CreateOfferText(
            rewardSection,
            "Reward Value",
            25f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            new Vector2(920f, 52f),
            Vector2.zero);
    }

    private void CreateAllowedMaterialsScrollView(RectTransform parent)
    {
        GameObject viewportObject = new GameObject(
            "Material Viewport",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(RectMask2D),
            typeof(ScrollRect));
        viewportObject.transform.SetParent(parent, false);

        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        SetCenteredRect(viewport, new Vector2(920f, 128f), new Vector2(0f, -22f));
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color32(255, 251, 239, 255);
        viewportImage.raycastTarget = true;

        GameObject contentObject = new GameObject(
            "Material Cards",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        offerMaterialsContent = contentObject.GetComponent<RectTransform>();
        offerMaterialsContent.anchorMin = new Vector2(0f, 0.5f);
        offerMaterialsContent.anchorMax = new Vector2(0f, 0.5f);
        offerMaterialsContent.pivot = new Vector2(0f, 0.5f);
        offerMaterialsContent.anchoredPosition = new Vector2(8f, 0f);
        offerMaterialsContent.sizeDelta = new Vector2(0f, 116f);

        HorizontalLayoutGroup layout = contentObject.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 8, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        offerMaterialsScrollRect = viewportObject.GetComponent<ScrollRect>();
        offerMaterialsScrollRect.viewport = viewport;
        offerMaterialsScrollRect.content = offerMaterialsContent;
        offerMaterialsScrollRect.horizontal = true;
        offerMaterialsScrollRect.vertical = false;
        offerMaterialsScrollRect.movementType = ScrollRect.MovementType.Clamped;
        offerMaterialsScrollRect.inertia = true;
        offerMaterialsScrollRect.decelerationRate = 0.12f;
        offerMaterialsScrollRect.scrollSensitivity = 30f;
    }

    private void PopulateAllowedMaterialCards(ContractSO contract)
    {
        if (offerMaterialsContent == null) return;

        for (int i = offerMaterialsContent.childCount - 1; i >= 0; i--)
        {
            GameObject oldCard = offerMaterialsContent.GetChild(i).gameObject;
            oldCard.SetActive(false);
            Destroy(oldCard);
        }

        int cardCount = 0;
        if (contract.allowedMaterials != null)
        {
            foreach (MaterialAllowance allowance in contract.allowedMaterials)
            {
                if (allowance == null || allowance.material == null) continue;
                CreateAllowedMaterialCard(offerMaterialsContent, allowance);
                cardCount++;
            }
        }

        if (cardCount == 0)
        {
            TextMeshProUGUI emptyText = CreateOfferText(
                offerMaterialsContent,
                "No Material Restrictions",
                23f,
                FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(890f, 110f),
                Vector2.zero);
            emptyText.text = "No material restrictions for this contract.";
            LayoutElement emptyLayout = emptyText.gameObject.AddComponent<LayoutElement>();
            emptyLayout.preferredWidth = 890f;
            emptyLayout.preferredHeight = 110f;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(offerMaterialsContent);
        if (offerMaterialsScrollRect != null)
        {
            offerMaterialsScrollRect.StopMovement();
            offerMaterialsScrollRect.horizontalNormalizedPosition = 0f;
        }
    }

    private void CreateAllowedMaterialCard(Transform parent, MaterialAllowance allowance)
    {
        GameObject cardObject = new GameObject(
            allowance.material.GetDisplayName(),
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(LayoutElement));
        cardObject.transform.SetParent(parent, false);
        RectTransform cardRect = cardObject.GetComponent<RectTransform>();
        cardRect.sizeDelta = new Vector2(205f, 110f);

        LayoutElement cardLayout = cardObject.GetComponent<LayoutElement>();
        cardLayout.preferredWidth = 205f;
        cardLayout.preferredHeight = 110f;

        Image cardBorder = cardObject.GetComponent<Image>();
        if (roundedPanelSprite != null) cardBorder.sprite = roundedPanelSprite;
        cardBorder.type = Image.Type.Sliced;
        cardBorder.color = new Color32(188, 143, 91, 255);
        cardBorder.raycastTarget = false;

        GameObject innerObject = new GameObject(
            "Card Surface",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        innerObject.transform.SetParent(cardObject.transform, false);
        StretchRect(
            innerObject.GetComponent<RectTransform>(),
            new Vector2(2f, 2f),
            new Vector2(-2f, -2f));
        Image innerImage = innerObject.GetComponent<Image>();
        if (roundedPanelSprite != null) innerImage.sprite = roundedPanelSprite;
        innerImage.type = Image.Type.Sliced;
        innerImage.color = new Color32(255, 247, 225, 255);
        innerImage.raycastTarget = false;

        GameObject iconObject = new GameObject(
            "Material Icon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        iconObject.transform.SetParent(innerObject.transform, false);
        SetCenteredRect(
            iconObject.GetComponent<RectTransform>(),
            new Vector2(76f, 76f),
            new Vector2(-59f, 0f));
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = allowance.material.materialIcon;
        icon.enabled = icon.sprite != null;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        TextMeshProUGUI cardText = CreateOfferText(
            innerObject.transform,
            "Material Name",
            19f,
            FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(108f, 72f),
            new Vector2(43f, -5f));
        cardText.text = allowance.material.GetDisplayName();
        cardText.enableWordWrapping = true;
        cardText.overflowMode = TextOverflowModes.Ellipsis;

        if (allowance.maxPieces > 0)
            CreateMaterialLimitBadge(innerObject.transform, allowance.maxPieces);
    }

    private void CreateMaterialLimitBadge(Transform parent, int maxPieces)
    {
        GameObject badgeObject = new GameObject(
            "Material Limit Badge",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        badgeObject.transform.SetParent(parent, false);
        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.anchorMin = Vector2.one;
        badgeRect.anchorMax = Vector2.one;
        badgeRect.pivot = Vector2.one;
        badgeRect.anchoredPosition = new Vector2(-6f, -6f);
        badgeRect.sizeDelta = new Vector2(66f, 28f);

        Image badgeImage = badgeObject.GetComponent<Image>();
        if (roundedPanelSprite != null) badgeImage.sprite = roundedPanelSprite;
        badgeImage.type = Image.Type.Sliced;
        badgeImage.color = new Color32(158, 91, 43, 255);
        badgeImage.raycastTarget = false;

        TextMeshProUGUI badgeText = CreateOfferText(
            badgeObject.transform,
            "Limit",
            15f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            new Vector2(60f, 24f),
            Vector2.zero);
        badgeText.text = "MAX " + maxPieces;
        badgeText.color = LightTextColor;
    }

    private RectTransform CreateBorderedOfferSection(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 position)
    {
        GameObject borderObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        borderObject.transform.SetParent(parent, false);
        SetCenteredRect(borderObject.GetComponent<RectTransform>(), size, position);

        Image border = borderObject.GetComponent<Image>();
        if (roundedPanelSprite != null) border.sprite = roundedPanelSprite;
        border.type = Image.Type.Sliced;
        border.color = new Color32(188, 143, 91, 255);
        border.raycastTarget = false;

        GameObject surfaceObject = new GameObject(
            "Surface",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        surfaceObject.transform.SetParent(borderObject.transform, false);
        RectTransform surface = surfaceObject.GetComponent<RectTransform>();
        StretchRect(surface, new Vector2(2f, 2f), new Vector2(-2f, -2f));

        Image surfaceImage = surfaceObject.GetComponent<Image>();
        if (roundedPanelSprite != null) surfaceImage.sprite = roundedPanelSprite;
        surfaceImage.type = Image.Type.Sliced;
        surfaceImage.color = new Color32(255, 247, 225, 255);
        surfaceImage.raycastTarget = false;
        return surface;
    }

    private TextMeshProUGUI CreateOfferText(
        Transform parent,
        string name,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions alignment,
        Vector2 size,
        Vector2 position)
    {
        GameObject textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (titleText != null) text.font = titleText.font;
        text.color = PrimaryTextColor;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(12f, fontSize - 4f);
        text.fontSizeMax = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.characterSpacing = 0.5f;
        text.lineSpacing = 2f;
        text.raycastTarget = false;
        SetCenteredRect(text.rectTransform, size, position);
        return text;
    }

    private static void CreateOfferDivider(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 position)
    {
        GameObject dividerObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        dividerObject.transform.SetParent(parent, false);
        SetCenteredRect(dividerObject.GetComponent<RectTransform>(), size, position);
        Image divider = dividerObject.GetComponent<Image>();
        divider.color = new Color32(188, 143, 91, 220);
        divider.raycastTarget = false;
    }

    private void SetContractOfferLayoutActive(bool active)
    {
        if (contractOfferLayout != null) contractOfferLayout.SetActive(active);
    }

    private void SetOtherUIActive(bool isActive)
    {
        foreach (GameObject uiElement in otherUIElements)
        {
            if (uiElement != null) uiElement.SetActive(isActive);
        }
    }

    private void ApplySharedVisualStyle()
    {
        if (trackerPanel == null) return;

        // Older scenes do not serialize the new theme fields yet. The shared quest
        // card prefab is a safe runtime source for the same rounded project sprite.
        if (roundedPanelSprite == null && questTabPrefab != null)
        {
            Image questCardImage = questTabPrefab.GetComponent<Image>();
            if (questCardImage != null) roundedPanelSprite = questCardImage.sprite;
        }

        ConfigurePanel(trackerPanel, FrameColor);
        RectTransform trackerRect = trackerPanel.GetComponent<RectTransform>();
        SetCenteredRect(trackerRect, new Vector2(1180f, 960f), Vector2.zero);

        CreateClipboardClip();

        ConfigurePanel(listPanel, SurfaceColor);
        SetCenteredRect(listPanel != null ? listPanel.GetComponent<RectTransform>() : null,
            new Vector2(1060f, 770f), new Vector2(0f, -70f));

        ConfigurePanel(detailsPanel, SurfaceColor);
        SetCenteredRect(detailsPanel != null ? detailsPanel.GetComponent<RectTransform>() : null,
            new Vector2(1060f, 770f), new Vector2(0f, -70f));

        TextMeshProUGUI heading = trackerPanel.GetComponentsInChildren<TextMeshProUGUI>(true)
            .FirstOrDefault(text => text != null && text.transform.parent == trackerPanel.transform);
        if (heading != null)
        {
            heading.text = "OBJECTIVES";
            heading.color = LightTextColor;
            heading.fontSize = 56f;
            heading.enableAutoSizing = false;
            heading.fontStyle = FontStyles.Bold;
            heading.alignment = TextAlignmentOptions.Center;

            RectTransform headingRect = heading.rectTransform;
            headingRect.anchorMin = new Vector2(0.5f, 1f);
            headingRect.anchorMax = new Vector2(0.5f, 1f);
            headingRect.pivot = new Vector2(0.5f, 0.5f);
            headingRect.anchoredPosition = new Vector2(0f, -42f);
            headingRect.sizeDelta = new Vector2(540f, 78f);
        }

        Button closeButton = trackerPanel.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(button => button != null && button.transform.parent == trackerPanel.transform);
        if (closeButton != null)
        {
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = Vector2.one;
            closeRect.anchorMax = Vector2.one;
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-50f, -50f);
            closeRect.sizeDelta = new Vector2(64f, 64f);
        }

        ScrollRect scrollRect = listPanel != null ? listPanel.GetComponentInChildren<ScrollRect>(true) : null;
        if (scrollRect != null)
        {
            RectTransform scrollTransform = scrollRect.GetComponent<RectTransform>();
            StretchRect(scrollTransform, new Vector2(30f, 30f), new Vector2(-30f, -30f));

            Image scrollBackground = scrollRect.GetComponent<Image>();
            if (scrollBackground != null)
            {
                scrollBackground.color = Color.clear;
                scrollBackground.raycastTarget = false;
            }

            if (scrollRect.viewport != null)
                StretchRect(scrollRect.viewport, Vector2.zero, Vector2.zero);
        }

        if (questListContent is RectTransform contentRect)
        {
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(-8f, 0f);

            ContentSizeFitter contentFitter = contentRect.GetComponent<ContentSizeFitter>();
            if (contentFitter != null)
            {
                contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            VerticalLayoutGroup layout = contentRect.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(16, 16, 16, 16);
                layout.spacing = 16f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
            }
        }

        ConfigureDetailsLayout();
        ConfigureActionButton(
            navigateButton,
            "TRACK LOCATION",
            TrackButtonColor);
        ConfigureActionButton(
            completeButton,
            "COLLECT REWARD",
            new Color32(113, 177, 44, 255));
    }

    private void CreateClipboardClip()
    {
        Transform existingClip = trackerPanel.transform.Find("Clipboard Clip");
        GameObject clipObject;

        if (existingClip != null)
        {
            clipObject = existingClip.gameObject;
        }
        else
        {
            clipObject = new GameObject(
                "Clipboard Clip",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            clipObject.transform.SetParent(trackerPanel.transform, false);
        }

        RectTransform clipRect = clipObject.GetComponent<RectTransform>();
        clipRect.anchorMin = new Vector2(0.5f, 1f);
        clipRect.anchorMax = new Vector2(0.5f, 1f);
        clipRect.pivot = new Vector2(0.5f, 0.5f);
        clipRect.anchoredPosition = new Vector2(0f, -38f);
        clipRect.sizeDelta = new Vector2(640f, 142f);
        clipRect.SetAsFirstSibling();

        Image clipImage = clipObject.GetComponent<Image>();
        if (roundedPanelSprite != null) clipImage.sprite = roundedPanelSprite;
        clipImage.type = Image.Type.Sliced;
        clipImage.color = ClipColor;
        clipImage.raycastTarget = false;

        Transform existingFace = clipObject.transform.Find("Clip Face");
        GameObject faceObject;

        if (existingFace != null)
        {
            faceObject = existingFace.gameObject;
        }
        else
        {
            faceObject = new GameObject(
                "Clip Face",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            faceObject.transform.SetParent(clipObject.transform, false);
        }

        RectTransform faceRect = faceObject.GetComponent<RectTransform>();
        StretchRect(faceRect, new Vector2(12f, 12f), new Vector2(-12f, -20f));

        Image faceImage = faceObject.GetComponent<Image>();
        if (roundedPanelSprite != null) faceImage.sprite = roundedPanelSprite;
        faceImage.type = Image.Type.Sliced;
        faceImage.color = ClipFaceColor;
        faceImage.raycastTarget = false;
    }

    private void ConfigureDetailsLayout()
    {
        ConfigureText(titleText, 40f, PrimaryTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        SetCenteredRect(titleText != null ? titleText.rectTransform : null,
            new Vector2(900f, 78f), new Vector2(0f, 275f));

        ConfigureText(descriptionText, 27f, SecondaryTextColor, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetCenteredRect(descriptionText != null ? descriptionText.rectTransform : null,
            new Vector2(900f, 190f), new Vector2(0f, 125f));
        if (descriptionText != null)
        {
            descriptionText.enableWordWrapping = true;
            descriptionText.overflowMode = TextOverflowModes.Ellipsis;
            descriptionText.margin = new Vector4(12f, 8f, 12f, 8f);
        }

        ConfigureText(budgetText, 25f, PrimaryTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        SetCenteredRect(budgetText != null ? budgetText.rectTransform : null,
            new Vector2(400f, 58f), new Vector2(-220f, -10f));

        ConfigureText(weightText, 25f, PrimaryTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        SetCenteredRect(weightText != null ? weightText.rectTransform : null,
            new Vector2(400f, 58f), new Vector2(220f, -10f));

        if (rewardContainer != null)
        {
            Image rewardImage = rewardContainer.GetComponent<Image>();
            if (rewardImage != null) rewardImage.color = Color.clear;
            SetCenteredRect(rewardContainer.GetComponent<RectTransform>(),
                new Vector2(900f, 190f), new Vector2(0f, -160f));
        }

        ConfigureText(rewardGoldText, 24f, PrimaryTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        ConfigureText(rewardExpText, 24f, PrimaryTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        SetCenteredRect(rewardGoldText != null ? rewardGoldText.rectTransform : null,
            new Vector2(360f, 46f), new Vector2(-190f, 22f));
        SetCenteredRect(rewardExpText != null ? rewardExpText.rectTransform : null,
            new Vector2(360f, 46f), new Vector2(190f, 22f));

        if (rewardContainer != null)
        {
            TextMeshProUGUI rewardHeading = rewardContainer
                .GetComponentsInChildren<TextMeshProUGUI>(true)
                .FirstOrDefault(text => text != null && text.gameObject.name == "Rewards");
            ConfigureText(rewardHeading, 29f, PrimaryTextColor, FontStyles.Bold, TextAlignmentOptions.Center);
            SetCenteredRect(rewardHeading != null ? rewardHeading.rectTransform : null,
                new Vector2(800f, 42f), new Vector2(0f, 70f));
        }

        SetCenteredRect(navigateButton != null ? navigateButton.GetComponent<RectTransform>() : null,
            new Vector2(420f, 72f), new Vector2(0f, -305f));
        SetCenteredRect(completeButton != null ? completeButton.GetComponent<RectTransform>() : null,
            new Vector2(360f, 72f), new Vector2(0f, -55f));
    }

    private void ConfigurePanel(GameObject target, Color color)
    {
        if (target == null) return;
        Image image = target.GetComponent<Image>();
        if (image == null) return;

        if (roundedPanelSprite != null) image.sprite = roundedPanelSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = true;
    }

    private static void ConfigureText(
        TextMeshProUGUI text,
        float fontSize,
        Color color,
        FontStyles style,
        TextAlignmentOptions alignment)
    {
        if (text == null) return;
        text.color = color;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(14f, fontSize - 7f);
        text.fontSizeMax = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
    }

    private void ConfigureActionButton(GameObject target, string label, Color color)
    {
        if (target == null) return;

        Image image = target.GetComponent<Image>();
        if (image != null)
        {
            if (roundedPanelSprite != null) image.sprite = roundedPanelSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
        }

        SetActionButtonLabel(target, label);
    }

    private static void SetActionButtonLabel(GameObject target, string label)
    {
        if (target == null) return;

        TextMeshProUGUI text = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.text = label;
            text.color = Color.white;
            text.fontSize = 29f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 21f;
            text.fontSizeMax = 29f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
        }
    }

    private static void SetActionButtonColor(GameObject target, Color color)
    {
        if (target == null) return;
        Image image = target.GetComponent<Image>();
        if (image != null) image.color = color;
    }

    private void CreateEmptyState()
    {
        if (questListContent == null) return;

        GameObject emptyObject = new GameObject("No Objectives", typeof(RectTransform), typeof(LayoutElement));
        emptyObject.transform.SetParent(questListContent, false);

        LayoutElement layout = emptyObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 150f;

        TextMeshProUGUI emptyText = emptyObject.AddComponent<TextMeshProUGUI>();
        if (titleText != null) emptyText.font = titleText.font;
        emptyText.text = "NO OBJECTIVES YET\n<size=18>New guides and contracts will appear here.</size>";
        emptyText.color = SecondaryTextColor;
        emptyText.fontSize = 24f;
        emptyText.fontStyle = FontStyles.Bold;
        emptyText.alignment = TextAlignmentOptions.Center;
        emptyText.enableWordWrapping = true;
        emptyText.raycastTarget = false;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void StretchRect(RectTransform rect, Vector2 minimumOffset, Vector2 maximumOffset)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = minimumOffset;
        rect.offsetMax = maximumOffset;
    }
}
