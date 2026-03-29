using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System.IO;

public class LevelCompleteManager : MonoBehaviour
{
    public static LevelCompleteManager Instance { get; private set; }

    [Header("Progression (Map System)")]
    public string nextLevelToUnlock;

    [Header("UI References")]
    public GameObject levelCompletePanel;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI costText;   // <-- Now just shows Total Cost
    public TextMeshProUGUI budgetText; // <-- NEW: Shows the Max Budget separately
    public TextMeshProUGUI stressText;
    
    [Header("Receipt UI System")]
    [Tooltip("The 'Content' object inside your Scroll View")]
    public Transform receiptContentParent; 
    [Tooltip("The Prefab with the ReceiptRowUI script attached")]
    public GameObject receiptRowPrefab;    

    [Header("Reward UI")]
    public TextMeshProUGUI goldEarnedText;
    public TextMeshProUGUI expEarnedText;

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();
    private bool rewardsClaimedThisSession = false;
    private bool levelAlreadyCompleted = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false); 
    }

    public void CompleteLevel(ContractSO currentContract)
    {
        if (levelAlreadyCompleted) return;
        levelAlreadyCompleted = true;

        StartCoroutine(TakeSnapshotAndShowUIRoutine(currentContract));
    }

    private IEnumerator TakeSnapshotAndShowUIRoutine(ContractSO currentContract)
    {
        // 1. Hide Gameplay UI instantly
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

        // 2. Wait exactly one frame so UI disappears from the screenshot
        yield return new WaitForEndOfFrame();

        // 3. Take Photo
        if (currentContract != null)
        {
            Texture2D screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenImage.Apply();

            byte[] imageBytes = screenImage.EncodeToPNG();
            string photoPath = Application.persistentDataPath + "/" + currentContract.name + "_photo.png";
            File.WriteAllBytes(photoPath, imageBytes);
            
            Destroy(screenImage);
            
            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.CompleteContract(currentContract.name);
            }
        }

        // 4. Generate the Itemized Receipt!
        if (receiptContentParent != null && receiptRowPrefab != null)
        {
            // Clear old rows from previous attempts
            foreach (Transform child in receiptContentParent) Destroy(child.gameObject);

            // Group bars by material and sum up their lengths
            Dictionary<BridgeMaterialSO, float> materialUsage = new Dictionary<BridgeMaterialSO, float>();
            HashSet<Bar> countedBars = new HashSet<Bar>();

            foreach (Point p in Point.AllPoints)
            {
                if (!p.gameObject.activeSelf) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && !countedBars.Contains(b))
                    {
                        countedBars.Add(b); // Prevent double-counting bars shared by two points
                        
                        if (!materialUsage.ContainsKey(b.materialData))
                            materialUsage[b.materialData] = 0f;
                        
                        // Dual beams count as twice the length for billing!
                        int multiplier = b.materialData.isDualBeam ? 2 : 1;
                        materialUsage[b.materialData] += (b.currentLength * multiplier);
                    }
                }
            }

            // Spawn a UI row for each material used
            foreach (var kvp in materialUsage)
            {
                BridgeMaterialSO mat = kvp.Key;
                float totalLength = kvp.Value;

                GameObject rowObj = Instantiate(receiptRowPrefab, receiptContentParent);
                ReceiptRowUI rowUI = rowObj.GetComponent<ReceiptRowUI>();
                if (rowUI != null)
                {
                    rowUI.Setup(mat, totalLength);
                }
            }
        }

        // 5. Show Panel and Calculate Grand Totals
        if (levelCompletePanel != null) levelCompletePanel.SetActive(true);

        float maxBudget = currentContract != null ? currentContract.budget : 0f;
        int baseGoldReward = currentContract != null ? currentContract.goldReward : 0;
        int baseExpReward = currentContract != null ? currentContract.expReward : 0;

        float finalCost = 0f;
        if (BuildUIController.Instance != null) finalCost = BuildUIController.Instance.GetTotalCost();

        float peakStress = 0f;
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null) peakStress = physicsManager.peakStressThisRun * 100f; 

        if (!rewardsClaimedThisSession && PlayerDataManager.Instance != null)
        {
            int finalGold = baseGoldReward;
            int finalExp = baseExpReward;

            if (finalCost <= maxBudget)
            {
                int bonusGold = Mathf.RoundToInt((maxBudget - finalCost) * 0.2f); 
                finalGold += bonusGold;
                if (feedbackText != null) feedbackText.text = "<color=green>Under Budget! Excellent Engineering!</color>";
            }
            else
            {
                int penaltyGold = Mathf.RoundToInt((finalCost - maxBudget) * 0.5f);
                finalGold -= penaltyGold;
                if (finalGold < 0) finalGold = 0; 
                
                if (feedbackText != null) feedbackText.text = "<color=red>Over Budget! The client isn't happy, but the bridge held.</color>";
            }

            PlayerDataManager.Instance.AddGold(finalGold);
            PlayerDataManager.Instance.AddExp(finalExp);
            PlayerDataManager.Instance.AddBridgeBuilt();
            
            if (!string.IsNullOrEmpty(nextLevelToUnlock)) PlayerDataManager.Instance.UnlockLevel(nextLevelToUnlock);

            if (goldEarnedText != null) goldEarnedText.text = $"+{finalGold} Gold";
            if (expEarnedText != null) expEarnedText.text = $"+{finalExp} EXP";

            rewardsClaimedThisSession = true;
        }

        // --- NEW: Separated Grand Total and Budget text ---
        if (costText != null) 
        {
            costText.text = $"Total Cost: ${Mathf.RoundToInt(finalCost)}";
            // Optional: Make it red if they went over budget
            costText.color = (finalCost > maxBudget) ? Color.red : Color.white;
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

    public void NextLevel() { if (!string.IsNullOrEmpty(nextLevelToUnlock)) SceneManager.LoadScene(nextLevelToUnlock); }
    public void RestartLevel() { SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
    public void ReturnToMap() { SceneManager.LoadScene("MapScene"); }
}