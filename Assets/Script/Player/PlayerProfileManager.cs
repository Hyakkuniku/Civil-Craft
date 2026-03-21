using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PlayFab;
using PlayFab.ClientModels;
using System.Collections.Generic;

public class PlayerProfileManager : MonoBehaviour
{
    public static PlayerProfileManager Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject profilePanel; // The main background panel

    [Header("Profile Display Texts")]
    public TextMeshProUGUI profileNameText;
    public TextMeshProUGUI profileLevelText; 
    public TextMeshProUGUI profileGoldText;  

    [Header("Profile Icon / Avatar")]
    public Image currentAvatarImage;
    [Tooltip("Drag all your possible profile pictures here!")]
    public List<Sprite> availableAvatars; 

    [Header("UI & Gameplay Toggles")]
    [Tooltip("Drag the canvases/panels you want to HIDE when the profile opens (e.g., Main Menu, HUD).")]
    public GameObject[] otherCanvasesToHide;
    [Tooltip("Check this to freeze the game (stops movement) while the profile is open.")]
    public bool freezeGameTime = true;
    
    // Optional: If you prefer disabling a specific movement script instead of pausing time, 
    // drag the specific script component here!
    [Tooltip("Optional: Drag your player's movement script here to disable it directly.")]
    public MonoBehaviour playerMovementScript;

    private int currentAvatarIndex = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (profilePanel != null) profilePanel.SetActive(false);
    }

    // ────────────────────────────────────────────────
    // UI NAVIGATION & TOGGLES
    // ────────────────────────────────────────────────

    public void OpenProfilePanel()
    {
        profilePanel.SetActive(true);
        LoadProfileData(); 

        // 1. Hide the other canvases (HUD, Main Menu, etc.)
        foreach (GameObject canvas in otherCanvasesToHide)
        {
            if (canvas != null) canvas.SetActive(false);
        }

        // 2. Disable Movement (Pause time or disable script)
        if (freezeGameTime) Time.timeScale = 0f;
        if (playerMovementScript != null) playerMovementScript.enabled = false;
    }

    public void CloseProfilePanel()
    {
        profilePanel.SetActive(false);
        
        // 1. Show the other canvases again
        foreach (GameObject canvas in otherCanvasesToHide)
        {
            if (canvas != null) canvas.SetActive(true);
        }

        // 2. Enable Movement (Unpause time or enable script)
        if (freezeGameTime) Time.timeScale = 1f;
        if (playerMovementScript != null) playerMovementScript.enabled = true;
    }

    // ────────────────────────────────────────────────
    // DATA LOADING (GUEST VS CLOUD)
    // ────────────────────────────────────────────────

    private void LoadProfileData()
    {
        string savedName = PlayerPrefs.GetString("SavedPlayerName", "Player");
        if (profileNameText != null) profileNameText.text = savedName;

        int loginChoice = PlayerPrefs.GetInt("LoginChoice", 0);

        if (loginChoice == 1 || !PlayFabClientAPI.IsClientLoggedIn())
        {
            // GUEST MODE
            Debug.Log("Loading Guest Profile Data...");
            currentAvatarIndex = PlayerPrefs.GetInt("AvatarIndex", 0);
            UpdateAvatarUI();
            
            if (profileLevelText != null) profileLevelText.text = "Lv: " + PlayerPrefs.GetInt("PlayerLevel", 1).ToString();
            if (profileGoldText != null) profileGoldText.text = "Gold: " + PlayerPrefs.GetInt("PlayerGold", 0).ToString();
        }
        else if (loginChoice == 2)
        {
            // ACCOUNT MODE
            Debug.Log("Downloading Account Profile Data...");
            PlayFabClientAPI.GetUserData(new GetUserDataRequest(), OnDataDownloaded, 
                error => Debug.LogError("Failed to get PlayFab Data: " + error.ErrorMessage));
        }
    }

    private void OnDataDownloaded(GetUserDataResult result)
    {
        if (result.Data != null && result.Data.ContainsKey("AvatarIndex"))
        {
            currentAvatarIndex = int.Parse(result.Data["AvatarIndex"].Value);
        }
        else
        {
            currentAvatarIndex = 0; 
        }
        
        UpdateAvatarUI();

        int level = result.Data != null && result.Data.ContainsKey("PlayerLevel") ? int.Parse(result.Data["PlayerLevel"].Value) : 1;
        int gold = result.Data != null && result.Data.ContainsKey("PlayerGold") ? int.Parse(result.Data["PlayerGold"].Value) : 0;

        if (profileLevelText != null) profileLevelText.text = "Lv: " + level.ToString();
        if (profileGoldText != null) profileGoldText.text = "Gold: " + gold.ToString();
    }

    private void UpdateAvatarUI()
    {
        if (availableAvatars != null && availableAvatars.Count > 0)
        {
            if (currentAvatarIndex >= 0 && currentAvatarIndex < availableAvatars.Count)
            {
                if (currentAvatarImage != null)
                {
                    currentAvatarImage.sprite = availableAvatars[currentAvatarIndex];
                }
            }
        }
    }

    // ────────────────────────────────────────────────
    // CHANGING AND SAVING AVATAR DATA
    // ────────────────────────────────────────────────

    public void SetNewAvatar(int newIndex)
    {
        currentAvatarIndex = newIndex;
        UpdateAvatarUI();

        int loginChoice = PlayerPrefs.GetInt("LoginChoice", 0);

        if (loginChoice == 1)
        {
            PlayerPrefs.SetInt("AvatarIndex", currentAvatarIndex);
            PlayerPrefs.Save();
            Debug.Log("Avatar saved locally!");
        }
        else if (loginChoice == 2 && PlayFabClientAPI.IsClientLoggedIn())
        {
            var request = new UpdateUserDataRequest
            {
                Data = new Dictionary<string, string> { { "AvatarIndex", currentAvatarIndex.ToString() } }
            };

            PlayFabClientAPI.UpdateUserData(request, 
                result => Debug.Log("Avatar successfully saved to PlayFab Cloud!"), 
                error => Debug.LogError("Failed to save avatar to cloud: " + error.ErrorMessage));
        }
    }
}