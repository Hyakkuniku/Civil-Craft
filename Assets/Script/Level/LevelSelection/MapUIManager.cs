using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    public GameObject levelInfoPanel;
    public TextMeshProUGUI levelTitleText;
    
    private int selectedLevelID;

    void Awake()
    {
        // Simple Singleton pattern
        if (Instance == null) Instance = this;
        levelInfoPanel.SetActive(false); // Hide at start
    }

    public void OpenLevelInfo(int levelID)
    {
        selectedLevelID = levelID;
        levelTitleText.text = "Level " + levelID;
        // You could pull specific level data (loot, enemies) from a ScriptableObject here
        
        levelInfoPanel.SetActive(true);
    }

    public void CloseLevelInfo()
    {
        levelInfoPanel.SetActive(false);
    }

    public void StartLevel()
    {
        Debug.Log("Loading Scene for Level: " + selectedLevelID);
        // SceneManager.LoadScene("Level" + selectedLevelID);
    }
}