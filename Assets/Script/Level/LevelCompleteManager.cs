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
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    // --- NEW: The Snapshot Memory List! ---
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
    }

    public void ShowLevelCompleteScreen()
    {
        if (levelCompletePanel != null) levelCompletePanel.SetActive(true);

        // --- THE FIX: Memorize what is currently ON, then hide it! ---
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
            peakStress = physicsManager.peakStressThisRun * 100f; 
        }

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

        if (BridgeHandbookManager.Instance != null && BridgeHandbookManager.globalHasBook)
        {
            Camera photoCam = Camera.main; 

            if (GameManager.Instance != null && GameManager.Instance.IsInBuildMode() && GameManager.Instance.ActiveBuildLocation != null)
            {
                if (GameManager.Instance.ActiveBuildLocation.locationCamera != null)
                {
                    photoCam = GameManager.Instance.ActiveBuildLocation.locationCamera;
                }
            }
            
            if (photoCam != null)
            {
                bool wasEnabled = photoCam.enabled;
                float originalFOV = photoCam.fieldOfView;
                float originalOrtho = photoCam.orthographicSize;

                photoCam.enabled = true;

                if (photoCam.orthographic) photoCam.orthographicSize *= 1.3f;
                else photoCam.fieldOfView *= 1.3f;

                string levelName = SceneManager.GetActiveScene().name; 
                string stats = $"Budget Used: ${Mathf.RoundToInt(finalCost)} / ${Mathf.RoundToInt(maxBudget)}\n\nPeak Stress: {Mathf.RoundToInt(peakStress)}%\n\nStatus: Successfully Engineered!";

                BridgeHandbookManager.Instance.RecordBridgeSnapshot(photoCam, levelName, stats);

                if (photoCam.orthographic) photoCam.orthographicSize = originalOrtho;
                else photoCam.fieldOfView = originalFOV;
                photoCam.enabled = wasEnabled;
            }
        }
    }

    public void ClosePanel()
    {
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);

        // --- THE FIX: Only turn back on the exact panels that were on before! ---
        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();

        // Player controls still stay smartly locked if we are building
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