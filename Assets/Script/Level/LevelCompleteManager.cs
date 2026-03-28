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
    public TextMeshProUGUI costText;
    public TextMeshProUGUI stressText;
    
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

        // Start the snapshot process!
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

        // Disable Player Input so the camera stops moving
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = false;

        // 2. Wait exactly one frame so the UI actually disappears from the screen!
        yield return new WaitForEndOfFrame();

        // 3. Take the Photo and Save it to the Hard Drive!
        if (currentContract != null)
        {
            // Read the pixels directly from the screen
            Texture2D screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenImage.Apply();

            // Encode to PNG and save it using the Contract's name
            byte[] imageBytes = screenImage.EncodeToPNG();
            string photoPath = Application.persistentDataPath + "/" + currentContract.name + "_photo.png";
            File.WriteAllBytes(photoPath, imageBytes);
            
            // Clean up memory
            Destroy(screenImage);
            
            // Mark the contract as complete in the save file
            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.CompleteContract(currentContract.name);
            }
        }

        // 4. Show the Panel and do the Math
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
            
            if (!string.IsNullOrEmpty(nextLevelToUnlock))
            {
                PlayerDataManager.Instance.UnlockLevel(nextLevelToUnlock);
            }

            if (goldEarnedText != null) goldEarnedText.text = $"+{finalGold} Gold";
            if (expEarnedText != null) expEarnedText.text = $"+{finalExp} EXP";

            rewardsClaimedThisSession = true;
        }

        if (costText != null) costText.text = $"Final Cost: ${Mathf.RoundToInt(finalCost)} / ${Mathf.RoundToInt(maxBudget)}";

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