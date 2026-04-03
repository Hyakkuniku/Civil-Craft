using UnityEngine;
using UnityEngine.UI; // --- NEW: Required for RawImage ---
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

    [Header("Photo Display")]
    [Tooltip("Drag the RawImage component from your UI here to display the bridge photo!")]
    public RawImage bridgePhotoDisplay; // --- NEW: The UI element to show the picture ---
    private Texture2D currentBridgePhoto; // --- NEW: Memory cache for the picture ---

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();
    private bool levelAlreadyCompleted = false;
    private bool wasSimulating = false; 

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

    private void Update()
    {
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        bool isSimulating = physicsManager != null && physicsManager.isSimulating;

        if (isSimulating && !wasSimulating)
        {
            ResetCompletionState();
        }
        
        wasSimulating = isSimulating;
    }

    private void OnDestroy()
    {
        // Clean up the texture from memory when changing scenes
        if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
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

    public bool IsContractPaid(string contractName)
    {
        if (string.IsNullOrEmpty(contractName)) return false;
        return alreadyPaidContracts.Contains(contractName);
    }

    public void ResetCompletionState()
    {
        levelAlreadyCompleted = false;
    }

    public void CompleteLevel(ContractSO currentContract)
    {
        if (levelAlreadyCompleted) 
        {
            Debug.LogWarning("<b>[Level Complete Manager]</b> Level is already marked as completed. Ignoring duplicate trigger.");
            return;
        }
        
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

                bool locGridWasOn = targetLoc != null && targetLoc.gridCanvas != null && targetLoc.gridCanvas.enabled;
                if (locGridWasOn) targetLoc.gridCanvas.enabled = false;

                BarCreator bc = FindObjectOfType<BarCreator>();
                bool bcGridWasOn = bc != null && bc.gridVisual != null && bc.gridVisual.canvasRenderer.GetAlpha() > 0;
                if (bcGridWasOn) bc.gridVisual.canvasRenderer.SetAlpha(0f);
                
                snapCam.Render();
                
                RenderTexture.active = rt;
                screenImage = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
                screenImage.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
                screenImage.Apply();
                
                snapCam.enabled = wasEnabled;
                snapCam.targetTexture = null;
                RenderTexture.active = null;
                Destroy(rt);

                if (locGridWasOn) targetLoc.gridCanvas.enabled = true;
                if (bcGridWasOn) bc.gridVisual.canvasRenderer.SetAlpha(1f);
            }
            else
            {
                screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                screenImage.Apply();
            }

            // --- THE FIX: Display the Photo! ---
            // Clear out the old photo from memory if we took one previously
            if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
            
            // Save the new photo to memory
            currentBridgePhoto = screenImage;

            // Slap it onto the UI Panel!
            if (bridgePhotoDisplay != null)
            {
                bridgePhotoDisplay.texture = currentBridgePhoto;
            }

            // Still save it to the hard drive for your saving/loading system
            byte[] imageBytes = currentBridgePhoto.EncodeToPNG();
            string photoPath = Application.persistentDataPath + "/" + currentContract.name + "_photo.png";
            File.WriteAllBytes(photoPath, imageBytes);
            
            // Notice: We DO NOT Destroy(screenImage) here anymore, because the UI is currently using it!
            
            if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.CompleteContract(currentContract.name);
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
                        if (anchor != null)
                        {
                            visitedPoints.Add(anchor);
                            queue.Enqueue(anchor);
                        }
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
            if (npc.contractToGive == activeContract) 
            {
                if (!alreadyPaidContracts.Contains(activeContract.name))
                {
                    npc.isContractCompleted = true;
                }
            }
        }

        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null) physicsManager.BakeBridge(activeContract); 

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