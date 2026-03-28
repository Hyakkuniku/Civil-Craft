using UnityEngine;
using TMPro;

public class MapUIManager : MonoBehaviour
{
    public static MapUIManager Instance;

    [Header("Overarching Map UI")]
    public TextMeshProUGUI mapTitleText; 

    [Header("Popup Panel UI")]
    public GameObject levelInfoPanel;
    public TextMeshProUGUI panelLevelTitleText;
    public TextMeshProUGUI panelLevelDescriptionText;
    
    private string selectedSceneName; 

    void Awake()
    {
        if (Instance == null) Instance = this;
        if (levelInfoPanel != null) levelInfoPanel.SetActive(false); 
    }

    public void UpdateMapTitle(string regionName)
    {
        if (mapTitleText != null && mapTitleText.text != regionName)
        {
            mapTitleText.text = regionName;
        }
    }

    public void OpenLevelInfo(int levelID, string levelTitle, string description, string sceneName)
    {
        selectedSceneName = sceneName; 
        
        if (panelLevelTitleText != null) 
            panelLevelTitleText.text = "Level " + levelID + ": " + levelTitle;
        
        if (panelLevelDescriptionText != null)
            panelLevelDescriptionText.text = description;
            
        if (levelInfoPanel != null) 
            levelInfoPanel.SetActive(true);
    }

    public void CloseLevelInfo()
    {
        if (levelInfoPanel != null && levelInfoPanel.activeSelf) 
            levelInfoPanel.SetActive(false);
    }

    public void StartLevel()
    {
        if (!string.IsNullOrEmpty(selectedSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(selectedSceneName);
        }
        else
        {
            Debug.LogError("No scene name provided for this level!");
        }
    }
}