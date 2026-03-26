using UnityEngine;
using TMPro;
using PlayFab;
using PlayFab.ClientModels;

public class PlayerProfileManager : MonoBehaviour
{
    public static PlayerProfileManager Instance { get; private set; }

    [Header("Profile Display")]
    public TextMeshProUGUI profileNameText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnEnable()
    {
        // Automatically load the name whenever the Handbook turns to this page
        LoadProfileData();
    }

    // ────────────────────────────────────────────────
    // DATA LOADING
    // ────────────────────────────────────────────────

    private void LoadProfileData()
    {
        // 1. Instantly load the local saved name so the UI doesn't look empty
        string savedName = PlayerPrefs.GetString("SavedPlayerName", "Player");
        if (profileNameText != null) profileNameText.text = savedName;

        int loginChoice = PlayerPrefs.GetInt("LoginChoice", 0);

        // 2. If it's a cloud account, ping PlayFab to make sure we have the absolute latest Display Name
        if (loginChoice == 2 && PlayFabClientAPI.IsClientLoggedIn())
        {
            Debug.Log("Verifying Account Name with PlayFab...");
            
            PlayFabClientAPI.GetPlayerProfile(new GetPlayerProfileRequest(), 
                result => 
                {
                    if (profileNameText != null) profileNameText.text = result.PlayerProfile.DisplayName;
                    
                    // Update local save just in case it changed
                    PlayerPrefs.SetString("SavedPlayerName", result.PlayerProfile.DisplayName);
                    PlayerPrefs.Save();
                }, 
                error => Debug.LogError("Failed to get PlayFab Profile Data: " + error.ErrorMessage));
        }
        else
        {
            Debug.Log("Loading Guest Name...");
            // Guest mode just relies on the local PlayerPrefs string we already set above.
        }
    }
}