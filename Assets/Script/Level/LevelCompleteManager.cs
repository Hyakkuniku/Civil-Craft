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

    [Header("Gameplay Elements to Hide")]
    [Tooltip("Drag the player's crosshair, interaction UI, or any canvas you want to hide when the level ends.")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
    }

    public void ShowLevelCompleteScreen()
    {
        if (levelCompletePanel != null) levelCompletePanel.SetActive(true);

        // 1. Hide the normal game UI (crosshairs, interact buttons, etc.)
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui != null) ui.SetActive(false);
        }

        // 2. Disable player movement and camera look using your InputManager
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }

        // Disable PlayerMotor directly just to be completely sure gravity/movement stops
        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = false;

        // --- Pull from GameManager's Global Memory ---
        float maxBudget = 0f;
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            maxBudget = GameManager.Instance.CurrentContract.budget;
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
            peakStress = physicsManager.peakStressThisRun * 100f; // Convert decimal to percentage
        }

        // --- UPDATE TEXT READOUTS ---
        if (costText != null)
        {
            costText.text = $"Final Cost: ${Mathf.RoundToInt(finalCost)} / ${Mathf.RoundToInt(maxBudget)}";
        }

        if (stressText != null)
        {
            stressText.text = $"Peak Bridge Stress: {Mathf.RoundToInt(peakStress)}%";
            
            if (peakStress >= 100f) stressText.color = Color.red;
            else if (peakStress >= 50f) stressText.color = Color.yellow;
            else stressText.color = Color.green;
        }

        if (feedbackText != null)
        {
            if (finalCost <= maxBudget)
            {
                feedbackText.text = "<color=green>Under Budget! Excellent Engineering!</color>";
            }
            else
            {
                feedbackText.text = "<color=red>Over Budget! The client isn't happy, but the bridge held.</color>";
            }
        }
    }

    // --- NEW: The method to close the screen and keep playing! ---
    public void ClosePanel()
    {
        // 1. Hide the Level Complete screen
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);

        // 2. Turn the normal game UI back on
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui != null) ui.SetActive(true);
        }

        // 3. Give the player their controls back
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;
    }

    public void NextLevel(string nextSceneName)
    {
        SceneManager.LoadScene(nextSceneName);
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