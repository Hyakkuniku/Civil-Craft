using UnityEngine;
using TMPro; 
using PlayFab;
using PlayFab.ClientModels;

public class PlayFabAuthManager : MonoBehaviour
{
    public static PlayFabAuthManager Instance { get; private set; }

    [Header("PlayFab Configuration")]
    [Tooltip("Paste your Title ID from the PlayFab website here!")]
    public string playFabTitleID = ""; 

    [Header("UI Panels")]
    public GameObject authCanvas;       
    public GameObject loginPanel;
    public GameObject registerPanel;
    public GameObject forgotPasswordPanel;

    [Header("Login Inputs")]
    public TMP_InputField loginUsername;
    public TMP_InputField loginPassword;

    [Header("Register Inputs")]
    public TMP_InputField registerUsername;
    public TMP_InputField registerEmail;
    public TMP_InputField registerPassword;

    [Header("Forgot Password Inputs")]
    public TMP_InputField resetEmailInput; 

    [Header("Feedback UI")]
    public TextMeshProUGUI feedbackText; 
    public Color errorColor = Color.red;
    public Color successColor = Color.green;
    public Color processColor = Color.yellow;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Tell the SDK what our Title ID is the moment the game starts
        if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleID;
        }

        if (authCanvas != null) authCanvas.SetActive(false);
    }

    // ────────────────────────────────────────────────
    // UI NAVIGATION
    // ────────────────────────────────────────────────

    public void OpenAuthCanvas()
    {
        authCanvas.SetActive(true);
        ShowLoginPanel(); 
    }

    public void CloseAuthCanvas()
    {
        authCanvas.SetActive(false);
    }

    public void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowRegisterPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowForgotPasswordPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(true);
        SetFeedbackMessage("Enter your email to reset your password.", Color.white);
    }

    private void SetFeedbackMessage(string message, Color color)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }
    }

    // ────────────────────────────────────────────────
    // PLAYFAB API CALLS
    // ────────────────────────────────────────────────

    public void OnLoginButtonClicked()
    {
        if (string.IsNullOrEmpty(loginUsername.text) || string.IsNullOrEmpty(loginPassword.text))
        {
            SetFeedbackMessage("Please enter Username and Password.", errorColor);
            return;
        }

        SetFeedbackMessage("Logging in...", processColor);

        var request = new LoginWithPlayFabRequest
        {
            Username = loginUsername.text,
            Password = loginPassword.text
        };

        PlayFabClientAPI.LoginWithPlayFab(request, OnLoginSuccess, OnLoginError);
    }

    public void OnRegisterButtonClicked()
    {
        if (string.IsNullOrEmpty(registerUsername.text) || string.IsNullOrEmpty(registerEmail.text) || string.IsNullOrEmpty(registerPassword.text))
        {
            SetFeedbackMessage("Please fill out all fields.", errorColor);
            return;
        }

        if (registerPassword.text.Length < 6)
        {
            SetFeedbackMessage("Password must be at least 6 characters.", errorColor);
            return;
        }

        SetFeedbackMessage("Registering account...", processColor);

        var request = new RegisterPlayFabUserRequest
        {
            Username = registerUsername.text,
            Email = registerEmail.text,
            Password = registerPassword.text,
            RequireBothUsernameAndEmail = true 
        };

        PlayFabClientAPI.RegisterPlayFabUser(request, OnRegisterSuccess, OnRegisterError);
    }

    public void OnForgotPasswordButtonClicked()
    {
        if (string.IsNullOrEmpty(resetEmailInput.text))
        {
            SetFeedbackMessage("Please enter your email address.", errorColor);
            return;
        }

        SetFeedbackMessage("Sending recovery email...", processColor);

        var request = new SendAccountRecoveryEmailRequest
        {
            Email = resetEmailInput.text,
            TitleId = playFabTitleID
        };

        PlayFabClientAPI.SendAccountRecoveryEmail(request, OnPasswordResetSuccess, OnPasswordResetError);
    }

    // ────────────────────────────────────────────────
    // PLAYFAB SUCCESS/ERROR CALLBACKS
    // ────────────────────────────────────────────────

    private void OnLoginSuccess(LoginResult result)
    {
        SetFeedbackMessage("Checking email verification...", processColor);
        
        // We ask PlayFab for the Player's Profile, requesting their Contact Email info
        var request = new GetPlayerProfileRequest
        {
            PlayFabId = result.PlayFabId,
            ProfileConstraints = new PlayerProfileViewConstraints
            {
                ShowContactEmailAddresses = true // This requires the box you checked in the Dashboard!
            }
        };

        PlayFabClientAPI.GetPlayerProfile(request, OnProfileSuccess, OnLoginError);
    }

    private void OnProfileSuccess(GetPlayerProfileResult result)
    {
        bool isEmailVerified = false;
        
        // Loop through all emails attached to the profile to see if any are Confirmed
        if (result.PlayerProfile != null && result.PlayerProfile.ContactEmailAddresses != null)
        {
            foreach (var emailInfo in result.PlayerProfile.ContactEmailAddresses)
            {
                if (emailInfo.VerificationStatus == EmailVerificationStatus.Confirmed)
                {
                    isEmailVerified = true;
                    break;
                }
            }
        }

        if (isEmailVerified)
        {
            SetFeedbackMessage("Login Successful!", successColor);
            Debug.Log("Logged in and Verified! PlayFab ID: " + result.PlayerProfile.PlayerId);
            Invoke(nameof(CloseAuthCanvas), 1.5f); 
        }
        else
        {
            // Block them from entering the game
            SetFeedbackMessage("Please check your email and click the verification link before logging in.", errorColor);
            
            // Forcefully clear their local login session since they aren't verified
            PlayFabClientAPI.ForgetAllCredentials(); 
        }
    }

    private void OnLoginError(PlayFabError error)
    {
        SetFeedbackMessage("Login Failed: " + error.ErrorMessage, errorColor);
        Debug.LogWarning("PlayFab Login Error: " + error.GenerateErrorReport());
    }

    private void OnRegisterSuccess(RegisterPlayFabUserResult result)
    {
        SetFeedbackMessage("Registration Successful! Please check your email for the verification link.", successColor);
        Debug.Log("Registered! PlayFab ID: " + result.PlayFabId);
        
        registerPassword.text = "";
        loginUsername.text = registerUsername.text; 
        
        // Tell PlayFab to save this email as their Contact Email (this triggers the Verification Email)
        var emailRequest = new AddOrUpdateContactEmailRequest
        {
            EmailAddress = registerEmail.text
        };
        PlayFabClientAPI.AddOrUpdateContactEmail(emailRequest, 
            success => Debug.Log("Verification email sent!"), 
            failure => Debug.LogWarning("Failed to send verification email: " + failure.ErrorMessage));

        Invoke(nameof(ShowLoginPanel), 2.5f);
    }

    private void OnRegisterError(PlayFabError error)
    {
        SetFeedbackMessage("Registration Failed: " + error.ErrorMessage, errorColor);
    }

    // --- Password Reset Callbacks ---
    private void OnPasswordResetSuccess(SendAccountRecoveryEmailResult result)
    {
        SetFeedbackMessage("Recovery email sent! Check your inbox.", successColor);
        resetEmailInput.text = ""; 
        Invoke(nameof(ShowLoginPanel), 2.0f);
    }

    private void OnPasswordResetError(PlayFabError error)
    {
        SetFeedbackMessage("Password Reset Failed: " + error.ErrorMessage, errorColor);
    }
}