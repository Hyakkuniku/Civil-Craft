using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class LevelCompleteManager : MonoBehaviour
{
    public static LevelCompleteManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject levelCompletePanel;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI stressText;
    
    // --- NEW: UI for showing what they earned! ---
    [Header("Reward UI")]
    public TextMeshProUGUI goldEarnedText;
    public TextMeshProUGUI expEarnedText;

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();
    
    // --- NEW: Make sure they only get paid once per level! ---
    private bool rewardsClaimedThisSession = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
    }

    public void ShowLevelCompleteScreen()
    {
        if (levelCompletePanel != null) levelCompletePanel.SetActive(true);

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

        float maxBudget = 0f;
        int baseGoldReward = 0;
        int baseExpReward = 0;
        
        ContractSO currentContract = null;

        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            currentContract = GameManager.Instance.CurrentContract;
            maxBudget = currentContract.budget;
            baseGoldReward = currentContract.goldReward;
            baseExpReward = currentContract.expReward;
        }

        float finalCost = 0f;
        if (BuildUIController.Instance != null)
        {
            finalCost = BuildUIController.Instance.GetTotalCost();
        }

        float peakStress = 0f;
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null)
        {
            peakStress = physicsManager.peakStressThisRun * 100f; 
        }

        // --- NEW: Reward Calculation & Saving ---
        if (!rewardsClaimedThisSession && PlayerDataManager.Instance != null)
        {
            int finalGold = baseGoldReward;
            int finalExp = baseExpReward;

            // Optional: Give them a bonus if they were under budget!
            if (finalCost <= maxBudget)
            {
                int bonusGold = Mathf.RoundToInt((maxBudget - finalCost) * 0.2f); // 20% of the saved money!
                finalGold += bonusGold;
                feedbackText.text = "<color=green>Under Budget! Excellent Engineering!</color>";
            }
            else
            {
                // Penalize them if they went over budget
                int penaltyGold = Mathf.RoundToInt((finalCost - maxBudget) * 0.5f);
                finalGold -= penaltyGold;
                if (finalGold < 0) finalGold = 0; // Don't let them go into debt
                
                feedbackText.text = "<color=red>Over Budget! The client isn't happy, but the bridge held.</color>";
            }

            // Save the data to the hard drive
            PlayerDataManager.Instance.AddGold(finalGold);
            PlayerDataManager.Instance.AddExp(finalExp);
            PlayerDataManager.Instance.AddBridgeBuilt();
            
            // Mark this scene as unlocked so they can play the next level!
            string nextSceneName = GetNextSceneName(); 
            if (!string.IsNullOrEmpty(nextSceneName))
            {
                PlayerDataManager.Instance.UnlockLevel(nextSceneName);
            }

            // Update the UI to show the player what they earned
            if (goldEarnedText != null) goldEarnedText.text = $"+{finalGold} Gold";
            if (expEarnedText != null) expEarnedText.text = $"+{finalExp} EXP";

            // Lock it so they can't just drive across 5 times to farm gold
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

    private string GetNextSceneName()
    {
        // Simple helper to guess the next scene name (e.g. "Level_1" -> "Level_2")
        // You can replace this with a hardcoded string if you prefer.
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        if (currentIndex + 1 < SceneManager.sceneCountInBuildSettings)
        {
            string nextScenePath = SceneUtility.GetScenePathByBuildIndex(currentIndex + 1);
            return System.IO.Path.GetFileNameWithoutExtension(nextScenePath);
        }
        return "";
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

    public void NextLevel()
    {
        string nextScene = GetNextSceneName();
        if (!string.IsNullOrEmpty(nextScene))
        {
            SceneManager.LoadScene(nextScene);
        }
    }

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMainMenu(string mainMenuName)
    {
        SceneManager.LoadScene(mainMenuName);
    }
}