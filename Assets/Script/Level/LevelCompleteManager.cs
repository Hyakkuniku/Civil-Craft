using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System.IO;

public class LevelCompleteManager : MonoBehaviour
{
    public static LevelCompleteManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject levelCompletePanel;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI costText;   
    public TextMeshProUGUI budgetText; 
    public TextMeshProUGUI stressText;
    
    [Header("Receipt UI System")]
    public Transform receiptContentParent; 
    public GameObject receiptRowPrefab;    

    [Header("Potential Reward UI (Visual Only)")]
    public TextMeshProUGUI goldEarnedText;
    public TextMeshProUGUI expEarnedText;

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();
    private bool levelAlreadyCompleted = false;

    private ContractSO activeContract;
    private HashSet<string> alreadyPaidContracts = new HashSet<string>();

    private Dictionary<string, int> contractGoldRewards = new Dictionary<string, int>();
    private Dictionary<string, int> contractExpRewards = new Dictionary<string, int>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false); 
    }

    public int GetContractGold(string contractName) { return contractGoldRewards.ContainsKey(contractName) ? contractGoldRewards[contractName] : 0; }
    public int GetContractExp(string contractName) { return contractExpRewards.ContainsKey(contractName) ? contractExpRewards[contractName] : 0; }

    public void MarkContractAsPaid(string contractName)
    {
        if (!string.IsNullOrEmpty(contractName))
        {
            alreadyPaidContracts.Add(contractName);
        }
    }

    public void ResetCompletionState()
    {
        levelAlreadyCompleted = false;
    }

    public void CompleteLevel(ContractSO currentContract)
    {
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null && !physicsManager.isSimulating) return;

        if (levelAlreadyCompleted) return;
        levelAlreadyCompleted = true;
        activeContract = currentContract;

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
            Texture2D screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenImage.Apply();

            byte[] imageBytes = screenImage.EncodeToPNG();
            string photoPath = Application.persistentDataPath + "/" + currentContract.name + "_photo.png";
            File.WriteAllBytes(photoPath, imageBytes);
            Destroy(screenImage);
            
            if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.CompleteContract(currentContract.name);
        }

        if (receiptContentParent != null && receiptRowPrefab != null)
        {
            foreach (Transform child in receiptContentParent) Destroy(child.gameObject);

            Dictionary<BridgeMaterialSO, float> materialUsage = new Dictionary<BridgeMaterialSO, float>();
            HashSet<Bar> countedBars = new HashSet<Bar>();

            foreach (Point p in Point.AllPoints)
            {
                if (!p.gameObject.activeSelf) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && !countedBars.Contains(b))
                    {
                        countedBars.Add(b); 
                        if (!materialUsage.ContainsKey(b.materialData)) materialUsage[b.materialData] = 0f;
                        int multiplier = b.materialData.isDualBeam ? 2 : 1;
                        materialUsage[b.materialData] += (b.currentLength * multiplier);
                    }
                }
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

        float finalCost = 0f;
        if (BuildUIController.Instance != null) finalCost = BuildUIController.Instance.GetTotalCost();

        float peakStress = 0f;
        BridgePhysicsManager manager = FindObjectOfType<BridgePhysicsManager>();
        if (manager != null) peakStress = manager.peakStressThisRun * 100f; 

        int calculatedGold = 0;
        int calculatedExp = 0;

        if (currentContract != null && alreadyPaidContracts.Contains(currentContract.name))
        {
            if (feedbackText != null) feedbackText.text = "<color=yellow>Redesign Successful! (Rewards already claimed)</color>";
        }
        else
        {
            calculatedGold = baseGoldReward;
            calculatedExp = baseExpReward;

            if (finalCost <= maxBudget)
            {
                int bonusGold = Mathf.RoundToInt((maxBudget - finalCost) * 0.2f); 
                calculatedGold += bonusGold;
                if (feedbackText != null) feedbackText.text = "<color=green>Under Budget! Excellent Engineering!</color>";
            }
            else
            {
                int penaltyGold = Mathf.RoundToInt((finalCost - maxBudget) * 0.5f);
                calculatedGold -= penaltyGold;
                if (calculatedGold < 0) calculatedGold = 0; 
                if (feedbackText != null) feedbackText.text = "<color=red>Over Budget! The client isn't happy, but the bridge held.</color>";
            }
        }

        if (currentContract != null)
        {
            contractGoldRewards[currentContract.name] = calculatedGold;
            contractExpRewards[currentContract.name] = calculatedExp;
        }

        if (goldEarnedText != null) goldEarnedText.text = $"+{calculatedGold} Gold (Pending)";
        if (expEarnedText != null) expEarnedText.text = $"+{calculatedExp} EXP (Pending)";

        if (costText != null) 
        {
            costText.text = $"Total Cost: ${Mathf.RoundToInt(finalCost)}";
            costText.color = (finalCost > maxBudget) ? Color.red : Color.white;
        }
        if (budgetText != null) budgetText.text = $"Budget: ${Mathf.RoundToInt(maxBudget)}";

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
        levelAlreadyCompleted = false; 
        
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null) physicsManager.StopPhysicsAndReset();
        
        BarCreator creator = FindObjectOfType<BarCreator>();
        if (creator != null) creator.isSimulating = false;

        ClosePanel();
    }

    public void SaveAndBakeBridge()
    {
        NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
        foreach (var npc in npcs)
        {
            if (npc.contractToGive == activeContract) npc.isContractCompleted = true;
        }

        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null) physicsManager.BakeBridge();

        // --- NEW: Wipe the memory banks! You can no longer Undo the baked bridge! ---
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