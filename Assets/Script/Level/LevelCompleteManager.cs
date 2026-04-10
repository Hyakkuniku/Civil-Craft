using UnityEngine;
using UnityEngine.UI; 
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System.IO;

[DefaultExecutionOrder(-30)] 
public class LevelCompleteManager : MonoBehaviour
{
    public static LevelCompleteManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject levelCompletePanel;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI costText;   
    public TextMeshProUGUI costPercentageText; 
    public TextMeshProUGUI budgetText; 
    public TextMeshProUGUI stressText;
    
    [Header("Receipt UI System")]
    public Transform receiptContentParent; 
    public GameObject receiptRowPrefab;    

    [Header("Earnings Breakdown UI")]
    public TextMeshProUGUI baseRewardText; 
    public TextMeshProUGUI bonusText;      
    public TextMeshProUGUI penaltyText;    
    public TextMeshProUGUI goldEarnedText; 
    public TextMeshProUGUI expEarnedText;

    [Header("Photo Display")]
    public RawImage bridgePhotoDisplay; 
    private Texture2D currentBridgePhoto; 

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();
    private bool levelAlreadyCompleted = false;
    private bool wasSimulating = false; 

    public int currentSimulationFrames { get; private set; } = 0;

    private ContractSO activeContract;
    private HashSet<string> alreadyPaidContracts = new HashSet<string>();

    private Dictionary<string, int> contractGoldRewards = new Dictionary<string, int>();
    private Dictionary<string, int> contractExpRewards = new Dictionary<string, int>();

    private BridgePhysicsManager cachedPhysicsManager;

    private float lastFinalCost = 0f;
    private float lastPeakStress = 0f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false); 
    }

    private void Start()
    {
        cachedPhysicsManager = FindObjectOfType<BridgePhysicsManager>();
    }

    private void Update()
    {
        if (cachedPhysicsManager == null) cachedPhysicsManager = FindObjectOfType<BridgePhysicsManager>();
        bool isSimulating = cachedPhysicsManager != null && cachedPhysicsManager.isSimulating;

        if (isSimulating && !wasSimulating)
        {
            ResetCompletionState();
        }
        
        if (!isSimulating && wasSimulating)
        {
            if (BuildUIController.Instance != null) BuildUIController.Instance.ShowTimer(false);
        }
        
        wasSimulating = isSimulating;
    }

    private void FixedUpdate()
    {
        if (cachedPhysicsManager == null) return;
        
        if (!cachedPhysicsManager.isSimulating)
        {
            currentSimulationFrames = 0;
            return;
        }

        bool isSimulating = cachedPhysicsManager.isSimulating;

        if (isSimulating && !levelAlreadyCompleted)
        {
            ContractSO currentContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;

            if (currentContract != null && currentContract.winCondition == ContractSO.WinCondition.Timer)
            {
                if (LevelFailedManager.Instance == null || !LevelFailedManager.Instance.isFailed)
                {
                    BuildLocation activeLoc = null;
                    if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
                    {
                        activeLoc = GameManager.Instance.ActiveBuildLocation;
                    }
                    else
                    {
                        BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
                        foreach (var loc in allLocs)
                        {
                            if (loc.gameObject.scene.name != null && loc.activeContract == currentContract)
                            {
                                activeLoc = loc;
                                break;
                            }
                        }
                    }

                    if (activeLoc != null && IsBridgeConnected(activeLoc))
                    {
                        currentSimulationFrames++; 
                        
                        float requiredTime = currentContract.requiredIntactTime;
                        float elapsedTime = currentSimulationFrames * Time.fixedDeltaTime;
                        float timeRemaining = requiredTime - elapsedTime;
                        if (timeRemaining < 0) timeRemaining = 0;

                        if (BuildUIController.Instance != null)
                        {
                            BuildUIController.Instance.ShowTimer(true);
                            BuildUIController.Instance.UpdateTimerText("Hold Bridge: ", timeRemaining);
                        }

                        int requiredFrames = Mathf.RoundToInt(requiredTime / Time.fixedDeltaTime);

                        if (currentSimulationFrames >= requiredFrames)
                        {
                            if (BuildUIController.Instance != null)
                            {
                                BuildUIController.Instance.ShowTimer(false); 
                            }
                            
                            if (ObjectiveTrackerUI.Instance != null)
                            {
                                ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {currentContract.clientName}.";
                            }

                            CompleteLevel(currentContract);
                        }
                    }
                    else
                    {
                        currentSimulationFrames = 0;
                        
                        if (BuildUIController.Instance != null)
                        {
                            BuildUIController.Instance.ShowTimer(true);
                            BuildUIController.Instance.UpdateTimerText("Hold Bridge: ", currentContract.requiredIntactTime);
                        }
                    }
                }
            }
        }
    }

    private bool IsBridgeConnected(BuildLocation loc)
    {
        if (loc == null || loc.startingAnchors.Count == 0) return false;
        if (loc.endingAnchors.Count == 0) return false;

        HashSet<Point> visited = new HashSet<Point>();
        Queue<Point> queue = new Queue<Point>();

        foreach (Point p in loc.startingAnchors)
        {
            if (p != null && p.gameObject.activeSelf)
            {
                visited.Add(p);
                queue.Enqueue(p);
            }
        }

        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();

            if (loc.endingAnchors.Contains(current)) return true;

            foreach (Bar b in current.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && b.materialData != null && b.materialData.isRoad)
                {
                    BarStressHandler stress = b.GetComponent<BarStressHandler>();
                    if (stress != null && stress.isBroken) continue;

                    Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                    if (neighbor != null && neighbor.gameObject.activeSelf && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return false; 
    }

    private void OnDestroy()
    {
        if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
    }

    public int GetContractGold(string contractName) { return contractGoldRewards.ContainsKey(contractName) ? contractGoldRewards[contractName] : 0; }
    public int GetContractExp(string contractName) { return contractExpRewards.ContainsKey(contractName) ? contractExpRewards[contractName] : 0; }

    public void MarkContractAsPaid(string contractName)
    {
        if (!string.IsNullOrEmpty(contractName)) alreadyPaidContracts.Add(contractName);
    }

    public bool IsContractPaid(string contractName)
    {
        if (string.IsNullOrEmpty(contractName)) return false;
        if (alreadyPaidContracts.Contains(contractName)) return true;

        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.completedContracts.Contains(contractName))
        {
            return true;
        }

        return false;
    }

    public void ResetCompletionState()
    {
        levelAlreadyCompleted = false;
        currentSimulationFrames = 0;
    }

    public void CompleteLevel(ContractSO currentContract)
    {
        if (levelAlreadyCompleted) return;
        
        levelAlreadyCompleted = true;
        activeContract = currentContract;

        if (cachedPhysicsManager != null)
        {
            cachedPhysicsManager.lockStressTracking = true;
        }

        LiveLoadVehicle vehicle = FindObjectOfType<LiveLoadVehicle>();
        if (vehicle != null)
        {
            vehicle.StopAndFreezeForWin();
        }

        StartCoroutine(TakeSnapshotAndShowUIRoutine(currentContract));
    }

    private IEnumerator TakeSnapshotAndShowUIRoutine(ContractSO currentContract)
    {
        temporarilyHiddenPanels.Clear();
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui != null && ui.activeSelf)
            {
                temporarilyHiddenPanels.Add(ui);
                ui.SetActive(false);
            }
        }

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = false;

        yield return new WaitForEndOfFrame();

        if (currentContract != null)
        {
            Camera snapCam = null;
            BuildLocation targetLoc = null;
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            
            foreach (var loc in allLocs)
            {
                if (loc.gameObject.scene.name != null && loc.activeContract == currentContract)
                {
                    targetLoc = loc;
                    snapCam = loc.cinematicCamera != null ? loc.cinematicCamera : loc.locationCamera;
                    break;
                }
            }

            Texture2D screenImage;

            if (snapCam != null)
            {
                int resWidth = 1920;
                int resHeight = 1080;
                
                RenderTexture rt = new RenderTexture(resWidth, resHeight, 24);
                snapCam.targetTexture = rt;
                
                bool wasEnabled = snapCam.enabled;
                snapCam.enabled = true;

                bool locGridWasOn = targetLoc != null && targetLoc.gridImage != null && targetLoc.gridImage.enabled;
                if (locGridWasOn) targetLoc.gridImage.enabled = false;

                snapCam.Render();
                
                RenderTexture.active = rt;
                screenImage = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
                screenImage.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
                screenImage.Apply();
                
                snapCam.enabled = wasEnabled;
                snapCam.targetTexture = null;
                RenderTexture.active = null;
                Destroy(rt);

                if (locGridWasOn) targetLoc.gridImage.enabled = true;
            }
            else
            {
                screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                screenImage.Apply();
            }

            if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
            currentBridgePhoto = screenImage;

            if (bridgePhotoDisplay != null) bridgePhotoDisplay.texture = currentBridgePhoto;

            byte[] imageBytes = currentBridgePhoto.EncodeToPNG();
            string photoPath = Application.persistentDataPath + "/" + currentContract.name + "_photo.png";
            File.WriteAllBytes(photoPath, imageBytes);
        }

        float totalCalculatedCost = 0f;

        if (receiptContentParent != null && receiptRowPrefab != null)
        {
            foreach (Transform child in receiptContentParent) Destroy(child.gameObject);

            Dictionary<BridgeMaterialSO, float> materialUsage = new Dictionary<BridgeMaterialSO, float>();
            HashSet<Bar> countedBars = new HashSet<Bar>();

            foreach (Point p in Point.AllPoints)
            {
                if (!p.gameObject.activeSelf || !p.enabled) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.materialData != null && !countedBars.Contains(b))
                    {
                        countedBars.Add(b);
                    }
                }
            }

            if (currentContract != null)
            {
                BuildLocation targetLoc = null;
                BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
                foreach (var loc in allLocs)
                {
                    if (loc.gameObject.scene.name != null && loc.activeContract == currentContract)
                    {
                        targetLoc = loc;
                        break;
                    }
                }

                if (targetLoc != null)
                {
                    foreach (Bar b in targetLoc.bakedBars)
                    {
                        if (b != null && b.materialData != null && !countedBars.Contains(b)) countedBars.Add(b);
                    }

                    HashSet<Point> visitedPoints = new HashSet<Point>();
                    Queue<Point> queue = new Queue<Point>();

                    foreach (Point anchor in targetLoc.startingAnchors)
                    {
                        if (anchor != null) { visitedPoints.Add(anchor); queue.Enqueue(anchor); }
                    }
                    foreach (Point anchor in targetLoc.endingAnchors)
                    {
                        if (anchor != null && !visitedPoints.Contains(anchor)) { visitedPoints.Add(anchor); queue.Enqueue(anchor); }
                    }

                    while (queue.Count > 0)
                    {
                        Point current = queue.Dequeue();
                        foreach (Bar b in current.ConnectedBars)
                        {
                            if (b != null && b.gameObject.activeSelf && b.materialData != null && !countedBars.Contains(b))
                            {
                                countedBars.Add(b);

                                Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                                if (neighbor != null && !visitedPoints.Contains(neighbor))
                                {
                                    visitedPoints.Add(neighbor);
                                    queue.Enqueue(neighbor);
                                }
                            }
                        }
                    }
                }
            }

            foreach (Bar b in countedBars)
            {
                if (!materialUsage.ContainsKey(b.materialData)) materialUsage[b.materialData] = 0f;
                int multiplier = b.materialData.isDualBeam ? 2 : 1;
                materialUsage[b.materialData] += (b.currentLength * multiplier);
                totalCalculatedCost += (b.currentLength * b.materialData.costPerMeter * multiplier);
            }

            foreach (var kvp in materialUsage)
            {
                GameObject rowObj = Instantiate(receiptRowPrefab, receiptContentParent);
                ReceiptRowUI rowUI = rowObj.GetComponent<ReceiptRowUI>();
                if (rowUI != null) rowUI.Setup(kvp.Key, kvp.Value);
            }
        }

        if (levelCompletePanel != null) levelCompletePanel.SetActive(true);

        float maxBudget = currentContract != null ? currentContract.budget : 0f;
        int baseGoldReward = currentContract != null ? currentContract.goldReward : 0;
        int baseExpReward = currentContract != null ? currentContract.expReward : 0;

        float finalCost = totalCalculatedCost;
        if (finalCost == 0f && BuildUIController.Instance != null) finalCost = BuildUIController.Instance.GetTotalCost();

        lastFinalCost = finalCost;

        float costPercentage = 0f;
        if (maxBudget > 0f)
        {
            costPercentage = (finalCost / maxBudget) * 100f;
        }

        float peakStress = 0f;
        if (cachedPhysicsManager != null) peakStress = cachedPhysicsManager.peakStressThisRun * 100f; 

        lastPeakStress = peakStress;

        int calculatedGold = 0;
        int calculatedExp = 0;
        int bonusGold = 0;
        int budgetPenalty = 0;
        int failPenalty = 0;

        if (LevelFailedManager.Instance != null)
        {
            failPenalty = LevelFailedManager.Instance.currentFailCount * LevelFailedManager.Instance.goldPenaltyPerFail;
        }

        if (currentContract != null && currentContract.isTutorialContract)
        {
            calculatedGold = 0;
            calculatedExp = 0;

            if (feedbackText != null) feedbackText.text = "<color=green>Tutorial Complete! Great Job!</color>";
            if (baseRewardText != null) baseRewardText.text = "";
            if (bonusText != null) bonusText.text = "";
            if (penaltyText != null) penaltyText.text = "";
        }
        else if (currentContract != null && alreadyPaidContracts.Contains(currentContract.name))
        {
            calculatedGold = 0;
            calculatedExp = 0;

            if (feedbackText != null) feedbackText.text = "<color=yellow>Redesign Successful! (Rewards already claimed)</color>";
            if (baseRewardText != null) baseRewardText.text = "Base Reward: 0";
            if (bonusText != null) bonusText.text = "Bonus: 0";
            if (penaltyText != null) penaltyText.text = "Penalty: 0";
        }
        else
        {
            calculatedGold = baseGoldReward;
            calculatedExp = baseExpReward;

            if (finalCost <= maxBudget)
            {
                bonusGold = Mathf.RoundToInt((maxBudget - finalCost) * 0.2f); 
                calculatedGold += bonusGold;
                
                if (feedbackText != null) feedbackText.text = "<color=green>Excellent Engineering!</color>";
                if (bonusText != null) bonusText.text = $"Bonus (Under Budget): <color=green>+{bonusGold}</color>";
            }
            else
            {
                budgetPenalty = Mathf.RoundToInt((finalCost - maxBudget) * 0.5f);
                
                if (feedbackText != null) feedbackText.text = "<color=red>Over Budget! The client isn't happy.</color>";
                if (bonusText != null) bonusText.text = $"Bonus: 0";
            }

            int totalPenalty = budgetPenalty + failPenalty;
            calculatedGold -= totalPenalty;
            if (calculatedGold < 0) calculatedGold = 0; 

            if (baseRewardText != null) baseRewardText.text = $"Base Reward: {baseGoldReward}";

            if (penaltyText != null)
            {
                if (totalPenalty > 0)
                {
                    string pText = "Penalty";
                    if (budgetPenalty > 0 && failPenalty > 0) pText += " (Over Budget & Fails)";
                    else if (budgetPenalty > 0) pText += " (Over Budget)";
                    else if (failPenalty > 0) pText += $" ({LevelFailedManager.Instance.currentFailCount} Fails)";

                    penaltyText.text = $"{pText}: <color=red>-{totalPenalty}</color>";
                }
                else
                {
                    penaltyText.text = "Penalty: 0";
                }
            }
        }

        if (currentContract != null)
        {
            contractGoldRewards[currentContract.name] = calculatedGold;
            contractExpRewards[currentContract.name] = calculatedExp;
        }

        if (goldEarnedText != null) 
        {
            if (currentContract != null && currentContract.isTutorialContract) goldEarnedText.text = "";
            else goldEarnedText.text = $"Total Earnings: {calculatedGold} Gold (Pending)";
        }
        
        if (expEarnedText != null) 
        {
            if (currentContract != null && currentContract.isTutorialContract) expEarnedText.text = "";
            else expEarnedText.text = $"+{calculatedExp} EXP (Pending)";
        }

        if (costText != null) 
        {
            costText.text = $"Total Cost: ${Mathf.RoundToInt(finalCost)}";
            costText.color = (finalCost > maxBudget) ? Color.red : Color.white;
        }
        
        if (costPercentageText != null)
        {
            costPercentageText.text = $"({Mathf.RoundToInt(costPercentage)}%)";
            costPercentageText.color = (finalCost > maxBudget) ? Color.red : Color.white;
        }
        
        if (budgetText != null) 
        {
            budgetText.text = $"Budget: ${Mathf.RoundToInt(maxBudget)}";
        }

        if (stressText != null)
        {
            stressText.text = $"Peak Bridge Stress: {Mathf.RoundToInt(peakStress)}%";
            
            if (peakStress >= 100f) stressText.color = Color.red;
            else if (peakStress >= 50f) stressText.color = Color.yellow;
            else stressText.color = Color.green;
        }
    }

    public void RetrySimulation()
    {
        ResetCompletionState();

        if (cachedPhysicsManager != null) cachedPhysicsManager.StopPhysicsAndReset();
        
        BarCreator creator = FindObjectOfType<BarCreator>();
        if (creator != null) creator.isSimulating = false;

        ClosePanel();
    }

    public void SaveAndBakeBridge()
    {
        if (LevelFailedManager.Instance != null) LevelFailedManager.Instance.ResetFailCount();

        NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
        foreach (var npc in npcs)
        {
            if (npc.contractToGive == activeContract) 
            {
                if (!alreadyPaidContracts.Contains(activeContract.name))
                {
                    npc.isContractCompleted = true;
                }
            }
        }

        if (activeContract != null && activeContract.autoCollectReward && !alreadyPaidContracts.Contains(activeContract.name))
        {
            int earnedGold = GetContractGold(activeContract.name);
            int earnedExp = GetContractExp(activeContract.name);

            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.AddGold(earnedGold);
                PlayerDataManager.Instance.AddExp(earnedExp);
                PlayerDataManager.Instance.AddBridgeBuilt();
                PlayerDataManager.Instance.CompleteContract(activeContract.name);
            }
            
            MarkContractAsPaid(activeContract.name);
            
            if (ObjectiveTrackerUI.Instance != null)
            {
                ObjectiveTrackerUI.Instance.ClearObjective(activeContract);
            }
        }

        if (cachedPhysicsManager != null) cachedPhysicsManager.BakeBridge(activeContract); 

        if (PlayerDataManager.Instance != null && activeContract != null)
        {
            BuildLocation targetLoc = null;
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            foreach (var loc in allLocs) { if (loc.gameObject.scene.name != null && loc.activeContract == activeContract) { targetLoc = loc; break; } }

            if (targetLoc != null)
            {
                PlayerDataManager.Instance.SaveBridgeData(
                    activeContract.name, 
                    targetLoc.bakedPoints, 
                    targetLoc.bakedBars, 
                    lastFinalCost, 
                    lastPeakStress
                );
            }
        }

        if (ObjectiveTrackerUI.Instance != null && activeContract != null && !activeContract.autoCollectReward)
        {
            ObjectiveTrackerUI.Instance.NotifyBridgeBuilt(activeContract.name);
        }

        // --- THE FIX: Now targets the "Contracts" tab specifically! ---
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.TriggerTabAlert("Contracts");
        }

        if (CommandManager.Instance != null) CommandManager.Instance.ClearHistory();

        ClosePanel();
        
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
    }

    public void ClosePanel()
    {
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);

        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();

        bool isBuilding = (GameManager.Instance != null && GameManager.Instance.IsInBuildMode());
        bool shouldEnableInput = !isBuilding;

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(shouldEnableInput);
            inputObj.SetLookEnabled(shouldEnableInput);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = shouldEnableInput;
    }
}