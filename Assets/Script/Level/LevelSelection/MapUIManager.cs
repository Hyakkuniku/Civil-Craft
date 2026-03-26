using UnityEngine;
using TMPro;

public class MapUIManager : MonoBehaviour
{
    public static MapUIManager Instance;

    [Header("Overarching Map UI")]
    [Tooltip("The text at the top/middle of the screen that changes as you pan")]
    public TextMeshProUGUI mapTitleText; 

    [Header("Popup Panel UI")]
    [Tooltip("The panel containing the Play button, level title, and description.")]
    public GameObject levelInfoPanel;
    
    [Tooltip("The text INSIDE the panel for the Level Title")]
    public TextMeshProUGUI panelLevelTitleText;
    
    [Tooltip("The text INSIDE the panel for the Level Description")]
    public TextMeshProUGUI panelLevelDescriptionText;
    
    private int selectedLevelID;

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

    public void OpenLevelInfo(int levelID, string levelTitle, string description)
    {
        selectedLevelID = levelID;
        
        if (panelLevelTitleText != null) 
        {
            panelLevelTitleText.text = "Level " + levelID + ": " + levelTitle;
        }
        
        if (panelLevelDescriptionText != null)
        {
            panelLevelDescriptionText.text = description;
        }
            
        if (levelInfoPanel != null) 
            levelInfoPanel.SetActive(true);
    }

    public void CloseLevelInfo()
    {
        if (levelInfoPanel != null && levelInfoPanel.activeSelf) 
        {
            levelInfoPanel.SetActive(false);
        }
    }

    public void StartLevel()
    {
        SceneController sceneController = FindObjectOfType<SceneController>();
        if (sceneController != null)
        {
            sceneController.LoadLevel(selectedLevelID);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Level" + selectedLevelID);
        }
    }
}