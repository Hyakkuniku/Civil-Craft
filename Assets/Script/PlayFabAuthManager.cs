using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using PlayFab;
using PlayFab.ClientModels;
using System.Collections;
using System.Collections.Generic;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayFabAuthManager : MonoBehaviour
{
    private const string RememberedPlayFabIdKey = "RememberedPlayFabId";
    private const string DeviceLinkedPlayFabIdKey = "DeviceLinkedPlayFabId";

    public static PlayFabAuthManager Instance { get; private set; }
    public event Action MainMenuAuthenticationSucceeded;
    public event Action<bool> AutomaticLoginCompleted;
    public bool IsPlayerLoggedIn => PlayFabClientAPI.IsClientLoggedIn();
    public bool IsAutomaticLoginInProgress { get; private set; }
    public bool IsGuestSelected => PlayerPrefs.GetInt("LoginChoice", 0) == 1;
    public bool IsCloudSaveReady { get; private set; }

    private bool returnToMainMenuAfterLogin;
    private bool playAfterAutomaticLogin;
    private bool manualLoginInProgress;
    private bool registrationInProgress;
    private bool openingAccountSave;
    private int authenticationGeneration;
    private Action cancelSaveChoice;

    [Header("PlayFab Configuration")]
    public string playFabTitleID = ""; 

    [Header("Player Data")]
    public string loggedInPlayerName = ""; 
    public bool isGuest = false; 

    [Header("Scene Management")]
    public string sceneToLoad = "GameScene"; 

    [Header("UI Panels & Text")]
    public GameObject authCanvas;       
    public GameObject loginPanel;
    public GameObject registerPanel;
    public GameObject forgotPasswordPanel;
    [Tooltip("Inactive, scene-authored account save choice dialog in Main Menu.")]
    public AuthoredSaveChoiceDialog saveChoiceDialog;
    
    [Tooltip("Text on the main menu to show 'Playing as: Name'")]
    public TextMeshProUGUI playerNameDisplay; 

    [Header("Login Inputs")]
    public TMP_InputField loginUsername;
    public TMP_InputField loginPassword;
    [SerializeField] private Button resendVerificationButton;
    private TMP_Text resendVerificationLabel;
    private string verificationTicket;
    private string verificationIdentifier;
    private string verificationNotice;
    private bool resendInProgress;
    private float resendAvailableAt;

    [Header("Register Inputs")]
    public TMP_InputField registerUsername;
    public TMP_InputField registerEmail;
    public TMP_InputField registerPassword;
    public TMP_InputField registerConfirmPassword; 
    [SerializeField] private Toggle termsAgreementToggle;
    [SerializeField] private Toggle emailUpdatesToggle;

    [Header("Forgot Password Inputs")]
    public TMP_InputField resetEmailInput; 
    public Button forgotPasswordSubmitButton;      
    public TextMeshProUGUI forgotPasswordButtonText; 

    private bool recoveryInProgress;
    private float recoveryAvailableAt;
    
    [Header("Password Visibility Icons")]
    public Image loginPasswordToggleImage;
    public Image registerPasswordToggleImage;
    public Sprite showPasswordSprite; 
    public Sprite hidePasswordSprite; 

    [Header("Feedback UI")]
    public TextMeshProUGUI feedbackText; 
    public Color errorColor = Color.red;
    public Color successColor = Color.green;
    public Color processColor = Color.yellow;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        if (!string.IsNullOrEmpty(playFabTitleID))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleID;
        }

        if (authCanvas != null) authCanvas.SetActive(false);
        if (saveChoiceDialog != null) saveChoiceDialog.Hide();
        if (resendVerificationButton != null)
            resendVerificationLabel = resendVerificationButton.GetComponentInChildren<TMP_Text>(true);
        RefreshResendButton();
    }

#if UNITY_EDITOR
    // One-time authoring action. Presentation belongs to the saved Main Menu scene,
    // not to the authentication flow that runs each time the game starts.
    [ContextMenu("Author Authentication UI In Scene")]
    public void AuthorAuthenticationUIInScene()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Stop Play Mode before authoring the authentication UI.", this);
            return;
        }

        if (authCanvas == null || loginPanel == null || registerPanel == null) return;

        UnityEditor.Undo.RegisterFullObjectHierarchyUndo(authCanvas,
            "Author Authentication UI");

        Color brown = new Color32(73, 56, 44, 255);
        Color cream = new Color32(255, 250, 242, 255);
        Color white = new Color32(255, 253, 249, 255);
        Color muted = new Color32(116, 96, 80, 255);

        Sprite outlineSprite = null;
        Sprite surfaceSprite = null;
        SceneController sceneController = FindObjectOfType<SceneController>(true);
        if (sceneController != null && sceneController.quitConfirmationPanel != null)
        {
            Image outlineSource = sceneController.quitConfirmationPanel.GetComponent<Image>();
            if (outlineSource != null) outlineSprite = outlineSource.sprite;
            Transform sourceContent = sceneController.quitConfirmationPanel.transform
                .Find("QuitConfirmationContent");
            Image surfaceSource = sourceContent != null ? sourceContent.GetComponent<Image>() : null;
            if (surfaceSource != null) surfaceSprite = surfaceSource.sprite;
        }
        Sprite controlSprite = surfaceSprite;
        foreach (Image candidate in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (candidate.sprite != null &&
                candidate.sprite.name.IndexOf("Rounded5", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                controlSprite = candidate.sprite;
                break;
            }
        }

        Image authBackground = authCanvas.transform.Find("BG")?.GetComponent<Image>();
        if (authBackground != null)
            authBackground.color = new Color(0.08f, 0.055f, 0.04f, 0.76f);

        StyleAuthCard(loginPanel, new Vector2(720f, 736f), outlineSprite, surfaceSprite,
            brown, cream);
        StyleAuthCard(registerPanel, new Vector2(720f, 802f), outlineSprite, surfaceSprite,
            brown, cream);
        if (forgotPasswordPanel != null)
            StyleAuthCard(forgotPasswordPanel, new Vector2(700f, 560f), outlineSprite,
                surfaceSprite, brown, cream);

        TMP_Text loginTitle = FindText(loginPanel, "txt_login");
        StyleTitle(loginTitle, "WELCOME BACK", new Vector2(0f, 245f), brown);
        CreateSubtitle(loginPanel, loginTitle, "Sign in to continue your journey",
            new Vector2(0f, 198f), muted);

        TMP_Text loginUserLabel = FindText(loginPanel, "txt_LogUser");
        TMP_Text loginPassLabel = FindText(loginPanel, "txt_LogPass");
        StyleFieldLabel(loginUserLabel, "USERNAME OR EMAIL", new Vector2(0f, 135f), brown);
        StyleFieldLabel(loginPassLabel, "PASSWORD", new Vector2(0f, 35f), brown);
        StyleInput(loginUsername, loginPanel.transform, new Vector2(0f, 95f),
            new Vector2(520f, 58f), controlSprite, brown, white, "Username or email");
        StyleInput(loginPassword, loginPanel.transform, new Vector2(0f, -5f),
            new Vector2(520f, 58f), controlSprite, brown, white, "Password");

        StyleNamedButton(loginPanel, "btn_login", "SIGN IN", new Vector2(0f, -110f),
            new Vector2(520f, 62f), true, controlSprite, brown, cream);
        StyleNamedButton(loginPanel, "btn_Register", "CREATE ACCOUNT", new Vector2(-132f, -200f),
            new Vector2(245f, 52f), false, controlSprite, brown, cream);
        StyleNamedButton(loginPanel, "btn_ForgotPass", "FORGOT PASSWORD", new Vector2(132f, -200f),
            new Vector2(245f, 52f), false, controlSprite, brown, cream);
        StyleNamedButton(loginPanel, "btn_guest", "CONTINUE AS GUEST", new Vector2(0f, -280f),
            new Vector2(330f, 50f), false, controlSprite, brown, cream);
        StyleCloseButton(loginPanel, "btn_exit", new Vector2(303f, 289f), brown);

        TMP_Text registerTitle = FindText(registerPanel, "txt_Title");
        StyleTitle(registerTitle, "CREATE ACCOUNT", new Vector2(0f, 350f), brown);
        CreateSubtitle(registerPanel, registerTitle, "Save your progress and play on another device",
            new Vector2(0f, 303f), muted);

        StyleFieldLabel(FindText(registerPanel, "txt_Email"), "EMAIL",
            new Vector2(0f, 256f), brown);
        StyleFieldLabel(FindText(registerPanel, "txt_RegUSer"), "USERNAME",
            new Vector2(0f, 182f), brown);
        StyleFieldLabel(FindText(registerPanel, "txt_RegPass"), "PASSWORD",
            new Vector2(0f, 108f), brown);
        StyleFieldLabel(FindText(registerPanel, "txt_ConfRegPass"), "CONFIRM PASSWORD",
            new Vector2(0f, -1f), brown);
        StyleInput(registerEmail, registerPanel.transform, new Vector2(0f, 222f),
            new Vector2(520f, 50f), controlSprite, brown, white, "Email address");
        StyleInput(registerUsername, registerPanel.transform, new Vector2(0f, 148f),
            new Vector2(520f, 50f), controlSprite, brown, white, "3–24 letters, numbers or underscores");
        StyleInput(registerPassword, registerPanel.transform, new Vector2(0f, 74f),
            new Vector2(520f, 50f), controlSprite, brown, white, "Create a password");
        StyleInput(registerConfirmPassword, registerPanel.transform, new Vector2(0f, -35f),
            new Vector2(520f, 50f), controlSprite, brown, white, "Repeat your password");

        StyleNamedButton(registerPanel, "btn_Register", "CREATE ACCOUNT", new Vector2(0f, -278f),
            new Vector2(520f, 62f), true, controlSprite, brown, cream);
        StyleNamedButton(registerPanel, "btn_signup", "BACK TO SIGN IN", new Vector2(0f, -348f),
            new Vector2(260f, 46f), false, controlSprite, brown, cream, true);
        StyleCloseButton(registerPanel, "btn_exit (2)", new Vector2(303f, 347f), brown);

        if (forgotPasswordPanel != null)
        {
            TMP_Text recoverTitle = FindText(forgotPasswordPanel, "txt_title");
            StyleTitle(recoverTitle, "RECOVER ACCOUNT", new Vector2(0f, 190f), brown);
            CreateSubtitle(forgotPasswordPanel, recoverTitle,
                "We'll send a password reset link to your email",
                new Vector2(0f, 140f), muted);
            StyleFieldLabel(FindText(forgotPasswordPanel, "txt_LogUser"), "EMAIL",
                new Vector2(0f, 72f), brown);
            StyleInput(resetEmailInput, forgotPasswordPanel.transform, new Vector2(0f, 29f),
                new Vector2(520f, 58f), controlSprite, brown, white, "Email address");
            StyleNamedButton(forgotPasswordPanel, "btn_recover", "SEND RECOVERY EMAIL",
                new Vector2(0f, -78f), new Vector2(520f, 62f), true,
                controlSprite, brown, cream);
            StyleNamedButton(forgotPasswordPanel, "btn_back", "BACK TO SIGN IN",
                new Vector2(0f, -166f), new Vector2(260f, 46f), false,
                controlSprite, brown, cream, true);
            StyleCloseButton(forgotPasswordPanel, "btn_exit (1)",
                new Vector2(293f, 224f), brown);
        }

        if (feedbackText != null)
        {
            SetRect(feedbackText.rectTransform, new Vector2(0f, -405f), new Vector2(900f, 60f));
            feedbackText.enableAutoSizing = true;
            feedbackText.fontSizeMin = 18f;
            feedbackText.fontSizeMax = 26f;
            feedbackText.alignment = TextAlignmentOptions.Center;
            feedbackText.fontStyle = FontStyles.Bold;
            feedbackText.raycastTarget = false;
        }

        errorColor = new Color32(174, 57, 49, 255);
        successColor = new Color32(56, 126, 75, 255);
        processColor = new Color32(166, 111, 43, 255);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private static void StyleAuthCard(GameObject panel, Vector2 size, Sprite outlineSprite,
        Sprite surfaceSprite, Color outlineColor, Color surfaceColor)
    {
        RectTransform panelRect = panel.transform as RectTransform;
        SetRect(panelRect, Vector2.zero, size);
        Image outline = panel.GetComponent<Image>();
        if (outline != null)
        {
            if (outlineSprite != null) outline.sprite = outlineSprite;
            outline.type = Image.Type.Sliced;
            outline.color = outlineColor;
        }

        Transform existing = panel.transform.Find("AuthCardInterior");
        GameObject interior = existing != null ? existing.gameObject :
            new GameObject("AuthCardInterior", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (existing == null)
            UnityEditor.Undo.RegisterCreatedObjectUndo(interior, "Create auth card interior");
        interior.layer = panel.layer;
        interior.transform.SetParent(panel.transform, false);
        interior.transform.SetAsFirstSibling();
        RectTransform interiorRect = interior.transform as RectTransform;
        interiorRect.anchorMin = Vector2.zero;
        interiorRect.anchorMax = Vector2.one;
        interiorRect.offsetMin = new Vector2(8f, 8f);
        interiorRect.offsetMax = new Vector2(-8f, -8f);
        Image surface = interior.GetComponent<Image>();
        if (surfaceSprite != null) surface.sprite = surfaceSprite;
        surface.type = Image.Type.Sliced;
        surface.color = surfaceColor;
        surface.raycastTarget = false;
    }

    private static void StyleTitle(TMP_Text title, string text, Vector2 position, Color color)
    {
        if (title == null) return;
        title.text = text;
        title.color = color;
        title.fontStyle = FontStyles.Bold;
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 46f;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        SetRect(title.rectTransform, position, new Vector2(620f, 64f));
    }

    private static void CreateSubtitle(GameObject panel, TMP_Text source, string text,
        Vector2 position, Color color)
    {
        if (source == null) return;
        Transform existing = panel.transform.Find("AuthSubtitle");
        TMP_Text subtitle;
        if (existing != null) subtitle = existing.GetComponent<TMP_Text>();
        else
        {
            subtitle = Instantiate(source, panel.transform, false);
            subtitle.gameObject.name = "AuthSubtitle";
            UnityEditor.Undo.RegisterCreatedObjectUndo(subtitle.gameObject, "Create auth subtitle");
        }
        subtitle.text = text;
        subtitle.color = color;
        subtitle.fontStyle = FontStyles.Normal;
        subtitle.enableAutoSizing = true;
        subtitle.fontSizeMin = 18f;
        subtitle.fontSizeMax = 25f;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.raycastTarget = false;
        SetRect(subtitle.rectTransform, position, new Vector2(640f, 42f));
    }

    private static void StyleFieldLabel(TMP_Text label, string text, Vector2 position, Color color)
    {
        if (label == null) return;
        label.text = text;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 22f;
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;
        SetRect(label.rectTransform, position, new Vector2(560f, 30f));
    }

    private static void StyleInput(TMP_InputField input, Transform panel, Vector2 position,
        Vector2 size, Sprite roundedSprite, Color outlineColor, Color surfaceColor,
        string placeholder)
    {
        if (input == null) return;
        input.transform.SetParent(panel, false);
        RectTransform rect = input.transform as RectTransform;
        SetRect(rect, position, size);
        Image background = input.GetComponent<Image>();
        if (background != null)
        {
            if (roundedSprite != null) background.sprite = roundedSprite;
            background.type = Image.Type.Sliced;
            background.color = new Color32(248, 242, 233, 255);
            Outline border = input.GetComponent<Outline>();
            if (border == null) border = UnityEditor.Undo.AddComponent<Outline>(input.gameObject);
            border.effectColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.48f);
            border.effectDistance = new Vector2(1.25f, -1.25f);
            border.useGraphicAlpha = true;
        }
        if (input.textComponent != null)
        {
            input.textComponent.color = outlineColor;
            input.textComponent.fontSize = 22f;
            input.textComponent.margin = new Vector4(18f, 0f, 50f, 0f);
        }
        TMP_Text placeholderText = input.placeholder as TMP_Text;
        if (placeholderText != null)
        {
            placeholderText.text = placeholder;
            placeholderText.color = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.42f);
            placeholderText.fontSize = 21f;
            placeholderText.fontStyle = FontStyles.Normal;
            placeholderText.margin = new Vector4(18f, 0f, 50f, 0f);
        }
        ColorBlock inputColors = input.colors;
        inputColors.normalColor = Color.white;
        inputColors.highlightedColor = new Color32(255, 252, 247, 255);
        inputColors.selectedColor = new Color32(255, 252, 247, 255);
        inputColors.pressedColor = new Color32(244, 235, 224, 255);
        inputColors.disabledColor = new Color32(215, 208, 199, 180);
        inputColors.colorMultiplier = 1f;
        inputColors.fadeDuration = 0.12f;
        input.colors = inputColors;
        input.customCaretColor = true;
        input.caretColor = outlineColor;
        input.selectionColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.25f);
    }

    private static void StyleNamedButton(GameObject panel, string objectName, string text,
        Vector2 position, Vector2 size, bool primary, Sprite roundedSprite,
        Color brown, Color cream, bool linkStyle = false)
    {
        Button button = FindButton(panel, objectName);
        if (button == null) return;
        RectTransform rect = button.transform as RectTransform;
        button.transform.SetParent(panel.transform, false);
        SetRect(rect, position, size);
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            if (roundedSprite != null) image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            Color secondaryFill = new Color32(237, 224, 207, 255);
            image.color = linkStyle ? new Color(1f, 1f, 1f, 0f) :
                primary ? brown : secondaryFill;
            Outline border = button.GetComponent<Outline>();
            if (!primary && !linkStyle)
            {
                if (border == null) border = UnityEditor.Undo.AddComponent<Outline>(button.gameObject);
                border.effectColor = new Color(brown.r, brown.g, brown.b, 0.3f);
                border.effectDistance = new Vector2(1f, -1f);
                border.useGraphicAlpha = true;
                border.enabled = true;
            }
            else if (border != null) border.enabled = false;

            Shadow depth = null;
            foreach (Shadow shadow in button.GetComponents<Shadow>())
            {
                if (!(shadow is Outline)) { depth = shadow; break; }
            }
            if (!linkStyle)
            {
                if (depth == null) depth = UnityEditor.Undo.AddComponent<Shadow>(button.gameObject);
                depth.effectColor = new Color(0.16f, 0.11f, 0.08f, primary ? 0.24f : 0.12f);
                depth.effectDistance = new Vector2(0f, primary ? -4f : -2f);
                depth.useGraphicAlpha = true;
                depth.enabled = true;
            }
            else if (depth != null) depth.enabled = false;
        }
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = primary
            ? new Color32(255, 244, 232, 255) : new Color32(255, 250, 242, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = primary
            ? new Color32(200, 179, 158, 255) : new Color32(224, 208, 190, 255);
        colors.disabledColor = new Color32(170, 162, 153, 150);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text;
            label.color = primary ? cream : brown;
            label.fontStyle = linkStyle ? FontStyles.Normal : FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 15f;
            label.fontSizeMax = primary ? 24f : 21f;
            label.alignment = TextAlignmentOptions.Center;
        }
    }

    private static void StyleCloseButton(GameObject panel, string objectName, Vector2 position,
        Color brown)
    {
        Button button = FindButton(panel, objectName);
        if (button == null) return;
        button.transform.SetParent(panel.transform, false);
        SetRect(button.transform as RectTransform, position, new Vector2(54f, 54f));
        Image image = button.GetComponent<Image>();
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            if (image != null) image.color = new Color(1f, 1f, 1f, 0f);
            label.text = "×";
            label.color = brown;
            label.fontStyle = FontStyles.Bold;
            label.fontSize = 38f;
            label.alignment = TextAlignmentOptions.Center;
        }
        else if (image != null) image.color = brown;
    }

    private static TMP_Text FindText(GameObject root, string objectName)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            if (text.gameObject.name == objectName) return text;
        return null;
    }

    private static Button FindButton(GameObject root, string objectName)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
            if (button.gameObject.name == objectName) return button;
        return null;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
#endif

    private void Start()
    {
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        IsCloudSaveReady = PlayFabClientAPI.IsClientLoggedIn() &&
            cloud != null && cloud.IsAccountActive;
        UpdatePlayerNameDisplay();
        TryRestoreLogin();
    }

    private static bool TryGetDeviceId(out string deviceId)
    {
        deviceId = string.Empty;
        if (Application.platform != RuntimePlatform.Android &&
            Application.platform != RuntimePlatform.IPhonePlayer)
            return false;

        deviceId = SystemInfo.deviceUniqueIdentifier;
        return !string.IsNullOrWhiteSpace(deviceId) &&
               deviceId != SystemInfo.unsupportedIdentifier;
    }

    private void TryRestoreLogin()
    {
        if (PlayerPrefs.GetInt("LoginChoice", 0) != 2 ||
            PlayFabClientAPI.IsClientLoggedIn() ||
            !TryGetDeviceId(out string deviceId)) return;

        string expectedPlayFabId = PlayerPrefs.GetString(RememberedPlayFabIdKey, string.Empty);
        if (string.IsNullOrEmpty(expectedPlayFabId)) return;

        IsAutomaticLoginInProgress = true;
        int generation = ++authenticationGeneration;
        var client = new PlayFabClientInstanceAPI();
        var info = AccountInfoRequest();

        if (Application.platform == RuntimePlatform.Android)
        {
            client.LoginWithAndroidDeviceID(new LoginWithAndroidDeviceIDRequest
            {
                AndroidDeviceId = deviceId,
                CreateAccount = false,
                InfoRequestParameters = info
            }, result => OnAutomaticLoginSuccess(result, expectedPlayFabId, generation),
               error => OnAutomaticLoginError(error, generation));
        }
        else
        {
            client.LoginWithIOSDeviceID(new LoginWithIOSDeviceIDRequest
            {
                DeviceId = deviceId,
                CreateAccount = false,
                InfoRequestParameters = info
            }, result => OnAutomaticLoginSuccess(result, expectedPlayFabId, generation),
               error => OnAutomaticLoginError(error, generation));
        }
    }

    private void OnAutomaticLoginSuccess(LoginResult result, string expectedPlayFabId, int generation)
    {
        if (this == null || generation != authenticationGeneration) return;

        if (result == null || !string.Equals(result.PlayFabId, expectedPlayFabId, StringComparison.Ordinal))
        {
            // A device may have been re-linked outside this installation. Never
            // accept a different account merely because its device login worked.
            PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
            PlayerPrefs.Save();
            Debug.LogWarning("Automatic login returned a different PlayFab account. Please sign in manually.", this);
            IsAutomaticLoginInProgress = false;
            FinishAutomaticLogin(false);
            return;
        }

        StartCoroutine(CivilCraftAccountWebsite.CheckEmail(result.SessionTicket, result.PlayFabId, status =>
        {
            if (this == null || generation != authenticationGeneration) return;
            if (status == CivilCraftAccountPolicy.Verification.Confirmed)
                CompleteAutomaticLogin(result, generation);
            else
            {
                IsAutomaticLoginInProgress = false;
                var account = result.InfoResultPayload?.AccountInfo;
                string identifier = account?.PrivateInfo?.Email ?? account?.Username;
                ShowVerificationResult(result, identifier, status);
                FinishAutomaticLogin(false);
            }
        }));
    }

    private void CompleteAutomaticLogin(LoginResult result, int generation)
    {
        if (!ActivateVerifiedSession(result))
        {
            IsAutomaticLoginInProgress = false;
            FinishAutomaticLogin(false);
            return;
        }
        ClearVerificationPrompt();
        isGuest = false;
        string displayName = result.InfoResultPayload?.PlayerProfile?.DisplayName;
        loggedInPlayerName = string.IsNullOrWhiteSpace(displayName)
            ? PlayerPrefs.GetString("SavedPlayerName", "Player") : displayName;
        PlayerPrefs.SetString("SavedPlayerName", loggedInPlayerName);
        PlayerPrefs.Save();
        UpdatePlayerNameDisplay();
        BeginCloudSaveLogin(result, CloudSaveManager.StartMode.Resume, generation, succeeded =>
        {
            if (generation != authenticationGeneration) return;
            IsAutomaticLoginInProgress = false;
            if (!succeeded) PlayFabClientAPI.ForgetAllCredentials();
            FinishAutomaticLogin(succeeded);
        });
    }

    private void OnAutomaticLoginError(PlayFabError error, int generation)
    {
        if (generation != authenticationGeneration) return;
        IsAutomaticLoginInProgress = false;
        Debug.LogWarning("Automatic login failed; manual sign-in is available. " + error.ErrorMessage, this);
        FinishAutomaticLogin(false);
    }

    private void FinishAutomaticLogin(bool succeeded)
    {
        AutomaticLoginCompleted?.Invoke(succeeded);
        if (!playAfterAutomaticLogin) return;
        playAfterAutomaticLogin = false;
        if (succeeded) LoadGameScene();
        else OpenAuthCanvas();
    }

    private void Update()
    {
        if (saveChoiceDialog != null && saveChoiceDialog.IsVisible && SaveChoiceEscapePressed())
        {
            cancelSaveChoice?.Invoke();
            return;
        }
        if (forgotPasswordPanel != null && forgotPasswordPanel.activeInHierarchy)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(recoveryAvailableAt - Time.realtimeSinceStartup));
            if (forgotPasswordSubmitButton != null) forgotPasswordSubmitButton.interactable = !recoveryInProgress && seconds == 0;
            if (forgotPasswordButtonText != null) forgotPasswordButtonText.text = recoveryInProgress
                ? "SENDING…" : seconds > 0 ? $"WAIT {seconds}s" : "SEND RECOVERY EMAIL";
        }
        if (resendVerificationButton != null && resendVerificationButton.gameObject.activeInHierarchy)
            RefreshResendButton();
    }

    private static bool SaveChoiceEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    // ────────────────────────────────────────────────
    // NAME DISPLAY LOGIC
    // ────────────────────────────────────────────────

    private void UpdatePlayerNameDisplay()
    {
        if (playerNameDisplay == null) return;

        int choice = PlayerPrefs.GetInt("LoginChoice", 0);

        if (choice == 0)
        {
            playerNameDisplay.gameObject.SetActive(false);
        }
        else
        {
            playerNameDisplay.gameObject.SetActive(true);
            string savedName = PlayerPrefs.GetString("SavedPlayerName", "Player");
            playerNameDisplay.text = choice == 2 && !PlayFabClientAPI.IsClientLoggedIn()
                ? "Last played as: " + savedName
                : "Playing as: " + savedName;
        }
    }

    // ────────────────────────────────────────────────
    // MAIN PLAY BUTTON LOGIC
    // ────────────────────────────────────────────────

    public void OnMainPlayButtonClicked()
    {
        int choice = PlayerPrefs.GetInt("LoginChoice", 0); 

        if (choice == 0)
        {
            OpenAuthCanvas();
        }
        else if (choice == 1)
        {
            isGuest = true;
            LoadGameScene();
        }
        else if (choice == 2)
        {
            if (IsAutomaticLoginInProgress)
            {
                playAfterAutomaticLogin = true;
                return;
            }
            if (PlayFabClientAPI.IsClientLoggedIn())
            {
                if (IsCloudSaveReady) LoadGameScene();
                else SetFeedbackMessage("Checking online save. Please wait...", processColor);
            }
            else
            {
                OpenAuthCanvas();
            }
        }
    }

    private void LoadGameScene()
    {
        Debug.Log("Loading Scene: " + sceneToLoad);
        LoadingScreenManager.LoadScene(sceneToLoad);
    }

    // ────────────────────────────────────────────────
    // UI NAVIGATION & TOGGLES
    // ────────────────────────────────────────────────

    public void OpenAuthCanvas()
    {
        if (authCanvas == null)
        {
            Debug.LogError("PlayFabAuthManager: AuthCanvas is not assigned.", this);
            return;
        }

        authCanvas.SetActive(true);
        ShowLoginPanel(); 
    }

    public void OpenAuthCanvasForMainMenu()
    {
        returnToMainMenuAfterLogin = true;
        OpenAuthCanvas();
    }

    public void CloseAuthCanvas()
    {
        if (registrationInProgress) return;
        if (manualLoginInProgress)
        {
            authenticationGeneration++;
            if (openingAccountSave)
                FailPendingLogin("Account switch canceled. Your guest save is unchanged.");
            else
                manualLoginInProgress = false;
        }
        ClearVerificationPrompt();
        CloseSaveChoice();
        returnToMainMenuAfterLogin = false;
        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        if (authCanvas != null) authCanvas.SetActive(false);
    }

    /// <summary>Clears the current PlayFab/guest session without deleting game progress.</summary>
    public void LogoutToSignedOutState()
    {
        CloseSaveChoice();
        CancelInvoke(nameof(LoadGameScene));
        authenticationGeneration++;
        IsAutomaticLoginInProgress = false;
        playAfterAutomaticLogin = false;
        manualLoginInProgress = false;
        openingAccountSave = false;
        registrationInProgress = false;
        ClearVerificationPrompt();
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud != null) cloud.EndSession();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        PlayFabClientAPI.ForgetAllCredentials();
        returnToMainMenuAfterLogin = false;
        isGuest = false;
        loggedInPlayerName = string.Empty;

        PlayerPrefs.SetInt("LoginChoice", 0);
        PlayerPrefs.DeleteKey("SavedPlayerName");
        PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();

        if (authCanvas != null) authCanvas.SetActive(false);
        UpdatePlayerNameDisplay();
    }

    public void OnPlayAsGuestClicked()
    {
        if (registrationInProgress) return;
        CloseSaveChoice();
        authenticationGeneration++;
        IsAutomaticLoginInProgress = false;
        playAfterAutomaticLogin = false;
        manualLoginInProgress = false;
        openingAccountSave = false;
        ClearVerificationPrompt();
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud != null) cloud.EndSession();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        PlayFabClientAPI.ForgetAllCredentials();
        isGuest = true;
        loggedInPlayerName = "Guest";
        
        PlayerPrefs.SetInt("LoginChoice", 1); 
        PlayerPrefs.SetString("SavedPlayerName", "Guest"); 
        PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();

        UpdatePlayerNameDisplay(); 

        SetFeedbackMessage("Starting as Guest...", successColor);
        if (returnToMainMenuAfterLogin)
        {
            CompleteMainMenuAuthenticationRequest();
        }
        else
        {
            Invoke(nameof(LoadGameScene), 1.0f);
        }
    }

    public void ShowLoginPanel()
    {
        if (registrationInProgress) return;
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        PositionFeedback(-405f);
        SetFeedbackMessage(verificationNotice ?? string.Empty, verificationNotice == null ? Color.white : errorColor);
        RefreshResendButton();
    }

    public void ShowRegisterPanel()
    {
        if (registrationInProgress || manualLoginInProgress || IsAutomaticLoginInProgress) return;
        ClearVerificationPrompt();
        if (termsAgreementToggle != null) termsAgreementToggle.SetIsOnWithoutNotify(false);
        if (emailUpdatesToggle != null) emailUpdatesToggle.SetIsOnWithoutNotify(false);
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        PositionFeedback(-429f);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowForgotPasswordPanel()
    {
        if (registrationInProgress || manualLoginInProgress) return;
        ClearVerificationPrompt();
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(true);
        PositionFeedback(-320f);
        SetFeedbackMessage("", Color.white);
    }

    private void PositionFeedback(float verticalPosition)
    {
        if (feedbackText == null) return;
        RectTransform rect = feedbackText.rectTransform;
        rect.anchoredPosition = new Vector2(0f, verticalPosition);
    }

    private void SetFeedbackMessage(string message, Color color)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }
    }

    public void ToggleLoginPasswordVisibility()
    {
        if (loginPassword.contentType == TMP_InputField.ContentType.Password)
        {
            loginPassword.contentType = TMP_InputField.ContentType.Standard;
            if (loginPasswordToggleImage != null && hidePasswordSprite != null)
                loginPasswordToggleImage.sprite = hidePasswordSprite;
        }
        else
        {
            loginPassword.contentType = TMP_InputField.ContentType.Password;
            if (loginPasswordToggleImage != null && showPasswordSprite != null)
                loginPasswordToggleImage.sprite = showPasswordSprite;
        }

        loginPassword.ForceLabelUpdate();
    }

    public void ToggleRegisterPasswordVisibility()
    {
        if (registerPassword.contentType == TMP_InputField.ContentType.Password)
        {
            registerPassword.contentType = TMP_InputField.ContentType.Standard;
            if (registerConfirmPassword != null) registerConfirmPassword.contentType = TMP_InputField.ContentType.Standard; 
            if (registerPasswordToggleImage != null && hidePasswordSprite != null)
                registerPasswordToggleImage.sprite = hidePasswordSprite;
        }
        else
        {
            registerPassword.contentType = TMP_InputField.ContentType.Password;
            if (registerConfirmPassword != null) registerConfirmPassword.contentType = TMP_InputField.ContentType.Password; 
            if (registerPasswordToggleImage != null && showPasswordSprite != null)
                registerPasswordToggleImage.sprite = showPasswordSprite;
        }

        registerPassword.ForceLabelUpdate();
        if (registerConfirmPassword != null) registerConfirmPassword.ForceLabelUpdate();
    }

    // ────────────────────────────────────────────────
    // PLAYFAB API CALLS
    // ────────────────────────────────────────────────


    public void OnLoginButtonClicked()
    {
        if (manualLoginInProgress || registrationInProgress) return;
        if (IsAutomaticLoginInProgress)
        {
            SetFeedbackMessage("Finishing automatic sign-in. Please wait…", processColor);
            return;
        }

        string identifier = CivilCraftAccountPolicy.TrimIdentifier(loginUsername.text);
        string password = loginPassword.text;
        string validationError = CivilCraftAccountPolicy.LoginError(identifier, password);
        if (validationError != null)
        {
            SetFeedbackMessage(validationError, errorColor);
            return;
        }

        ClearVerificationPrompt();
        SetFeedbackMessage("Signing in…", processColor);
        manualLoginInProgress = true;
        int generation = ++authenticationGeneration;
        var client = new PlayFabClientInstanceAPI();
        var info = AccountInfoRequest();

        // Keep the provisional credentials out of the global SDK session until
        // the website confirms this account and the player chooses its save.
        if (CivilCraftAccountPolicy.IsEmailLogin(identifier))
        {
            client.LoginWithEmailAddress(new LoginWithEmailAddressRequest
            {
                Email = identifier,
                Password = password,
                InfoRequestParameters = info
            }, result => OnLoginSuccess(result, generation, identifier),
               error => OnLoginError(error, generation));
        }
        else
        {
            client.LoginWithPlayFab(new LoginWithPlayFabRequest
            {
                Username = identifier,
                Password = password,
                InfoRequestParameters = info
            }, result => OnLoginSuccess(result, generation, identifier),
               error => OnLoginError(error, generation));
        }
    }

    private static GetPlayerCombinedInfoRequestParams AccountInfoRequest() =>
        new GetPlayerCombinedInfoRequestParams
        {
            GetUserAccountInfo = true,
            GetPlayerProfile = true,
            ProfileConstraints = new PlayerProfileViewConstraints { ShowDisplayName = true }
        };

    public void OnRegisterButtonClicked()
    {
        if (registrationInProgress || manualLoginInProgress || IsAutomaticLoginInProgress) return;
        string username = CivilCraftAccountPolicy.TrimIdentifier(registerUsername.text);
        string email = CivilCraftAccountPolicy.TrimIdentifier(registerEmail.text);
        string password = registerPassword.text;
        string errorMessage = CivilCraftAccountPolicy.RegistrationError(username, email, password,
            registerConfirmPassword != null ? registerConfirmPassword.text : null,
            termsAgreementToggle != null && termsAgreementToggle.isOn);
        if (errorMessage != null)
        {
            SetFeedbackMessage(errorMessage, errorColor);
            return;
        }

        bool emailUpdates = emailUpdatesToggle != null && emailUpdatesToggle.isOn;
        string acceptedAt = DateTime.UtcNow.ToString("o");
        registrationInProgress = true;
        int generation = ++authenticationGeneration;
        SetFeedbackMessage("Creating account…", processColor);
        var client = new PlayFabClientInstanceAPI();
        client.RegisterPlayFabUser(new RegisterPlayFabUserRequest
        {
            Username = username,
            DisplayName = username,
            Email = email,
            Password = password,
            RequireBothUsernameAndEmail = true
        }, result =>
        {
            if (this == null || generation != authenticationGeneration) return;
            registerPassword.text = string.Empty;
            registerConfirmPassword.text = string.Empty;
            StartCoroutine(CompleteRegistration(result, username, email, emailUpdates, acceptedAt, generation));
        }, error =>
        {
            if (this == null || generation != authenticationGeneration) return;
            registrationInProgress = false;
            SetFeedbackMessage("Could not create account. " + error.ErrorMessage, errorColor);
        });
    }

    public void OnForgotPasswordButtonClicked()
    {
        if (recoveryInProgress || Time.realtimeSinceStartup < recoveryAvailableAt) return;
        string email = CivilCraftAccountPolicy.TrimIdentifier(resetEmailInput.text);
        if (!CivilCraftAccountPolicy.IsEmail(email))
        {
            SetFeedbackMessage("Enter a valid email address.", errorColor);
            return;
        }

        SetFeedbackMessage("Requesting a recovery email…", processColor);
        recoveryInProgress = true;
        StartCoroutine(CivilCraftAccountWebsite.SendRecovery(email, succeeded =>
        {
            recoveryInProgress = false;
            if (succeeded) recoveryAvailableAt = Time.realtimeSinceStartup + 60f;
            if (forgotPasswordPanel == null || !forgotPasswordPanel.activeInHierarchy) return;
            SetFeedbackMessage(succeeded ? CivilCraftAccountWebsite.RecoveryRequested :
                "Unable to request password recovery. Please try again later.", succeeded ? successColor : errorColor);
        }));
    }

    public void OpenAccountTerms() => Application.OpenURL(CivilCraftAccountWebsite.Origin + "/terms");
    public void OpenAccountPrivacy() => Application.OpenURL(CivilCraftAccountWebsite.Origin + "/privacy");


    public void SubmitNewDisplayName(string newName)
    {
        string trimmedName = (newName ?? string.Empty).Trim();
        if (!PlayerNamePolicy.TryValidate(trimmedName, out string nameError))
        {
            SetFeedbackMessage(nameError, errorColor);
            return;
        }

        var request = new UpdateUserTitleDisplayNameRequest { DisplayName = trimmedName };
        PlayFabClientAPI.UpdateUserTitleDisplayName(request, 
            result => {
                loggedInPlayerName = result.DisplayName;
                PlayerPrefs.SetString("SavedPlayerName", result.DisplayName); 
                UpdatePlayerNameDisplay();
                Debug.Log("Display Name successfully changed to: " + result.DisplayName);
            }, 
            error => Debug.LogError("Failed to change name: " + error.ErrorMessage));
    }

    // ────────────────────────────────────────────────
    // PLAYFAB SUCCESS/ERROR CALLBACKS
    // ────────────────────────────────────────────────


    private void OnLoginSuccess(LoginResult result, int generation, string identifier)
    {
        if (this == null || generation != authenticationGeneration) return;
        loginPassword.text = string.Empty;
        SetFeedbackMessage("Checking email verification…", processColor);
        StartCoroutine(CivilCraftAccountWebsite.CheckEmail(result?.SessionTicket, result?.PlayFabId, status =>
        {
            if (this == null || generation != authenticationGeneration) return;
            if (status == CivilCraftAccountPolicy.Verification.Confirmed)
                AcceptVerifiedManualLogin(result, generation);
            else
            {
                manualLoginInProgress = false;
                ShowVerificationResult(result, identifier, status);
            }
        }));
    }

    private void ShowVerificationResult(LoginResult result, string identifier, CivilCraftAccountPolicy.Verification status)
    {
        if (status == CivilCraftAccountPolicy.Verification.Unconfirmed)
            RememberVerificationPrompt(result.SessionTicket,
                result.InfoResultPayload?.AccountInfo?.PrivateInfo?.Email ?? identifier,
                CivilCraftAccountWebsite.VerificationRequired);
        else
            RememberVerificationPrompt(null, null, CivilCraftAccountWebsite.VerificationUnavailable);

        if (loginPanel != null && loginPanel.activeInHierarchy)
            SetFeedbackMessage(verificationNotice, errorColor);
    }

    private void RememberVerificationPrompt(string ticket, string identifier, string message)
    {
        verificationTicket = ticket;
        verificationIdentifier = identifier;
        verificationNotice = message;
        RefreshResendButton();
    }

    private void ClearVerificationPrompt()
    {
        RememberVerificationPrompt(null, null, null);
    }

    private void RefreshResendButton()
    {
        if (resendVerificationButton == null) return;
        bool available = !string.IsNullOrWhiteSpace(verificationTicket) &&
                         !string.IsNullOrWhiteSpace(verificationIdentifier);
        resendVerificationButton.gameObject.SetActive(available);
        if (!available) return;

        int seconds = Mathf.Max(0, Mathf.CeilToInt(resendAvailableAt - Time.realtimeSinceStartup));
        resendVerificationButton.interactable = !resendInProgress && seconds == 0;
        if (resendVerificationLabel != null)
            resendVerificationLabel.text = resendInProgress ? "SENDING…" :
                seconds > 0 ? $"RESEND IN {seconds}s" : "RESEND VERIFICATION EMAIL";
    }

    public void OnResendVerificationClicked()
    {
        if (resendInProgress || manualLoginInProgress || IsAutomaticLoginInProgress ||
            Time.realtimeSinceStartup < resendAvailableAt || string.IsNullOrWhiteSpace(verificationTicket) ||
            string.IsNullOrWhiteSpace(verificationIdentifier)) return;

        string ticket = verificationTicket;
        string identifier = verificationIdentifier;
        resendInProgress = true;
        RefreshResendButton();
        StartCoroutine(CivilCraftAccountWebsite.RequestVerification(ticket, identifier,
            (code, reply) => FinishVerificationResend(ticket, code, reply)));
    }

    private void FinishVerificationResend(string ticket, long code, CivilCraftAccountWebsite.ResendReply reply)
    {
        resendInProgress = false;
        if (ticket != verificationTicket)
        {
            RefreshResendButton();
            return;
        }
        if (reply != null && reply.success)
        {
            if (reply.alreadyVerified) ClearVerificationPrompt();
            else resendAvailableAt = Time.realtimeSinceStartup + Mathf.Clamp(reply.cooldownSeconds, 1, 600);
            SetFeedbackMessage(reply.alreadyVerified ? "Your email is verified. Sign in to continue." :
                "Verification requested. Check your inbox and spam folder.", successColor);
        }
        else
        {
            if (code == 429) resendAvailableAt = Time.realtimeSinceStartup + 60f;
            if (code == 401) ClearVerificationPrompt();
            Debug.LogWarning($"Verification resend request failed (HTTP {code}).", this);
            SetFeedbackMessage(code == 429 ? "Please wait before requesting another verification email." :
                code == 401 ? "Sign in again to request a new verification email." :
                "Could not request verification. Try again later or contact Civil Craft support.", errorColor);
        }
        RefreshResendButton();
    }

    private static bool ActivateVerifiedSession(LoginResult result)
    {
        if (result?.AuthenticationContext == null || !result.AuthenticationContext.IsClientLoggedIn() ||
            !result.AuthenticationContext.IsEntityLoggedIn()) return false;
        PlayFabSettings.staticPlayer.CopyFrom(result.AuthenticationContext);
        return true;
    }


    private void AcceptVerifiedManualLogin(LoginResult result, int generation)
    {
        if (this == null || generation != authenticationGeneration) return;
        // Inspect saves before linking this device: canceling the choice must
        // leave both the guest slot and the remembered account untouched.
        loggedInPlayerName = string.Empty;
        if (result.InfoResultPayload != null && result.InfoResultPayload.PlayerProfile != null)
        {
            loggedInPlayerName = result.InfoResultPayload.PlayerProfile.DisplayName;
        }

        CompleteManualLogin(result, generation);
    }

    private void CompleteManualLogin(LoginResult result, int generation)
    {
        if (generation != authenticationGeneration) return;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud == null)
        {
            FailPendingLogin("Account save is unavailable. Please try again.");
            return;
        }

        SetFeedbackMessage("Checking account save...", processColor);
        cloud.InspectAccount(result, availability =>
        {
            if (generation != authenticationGeneration) return;
            switch (availability)
            {
                case CloudSaveManager.SaveAvailability.Empty:
                    if (!ShowSaveChoice(
                            "This account has no save.\nStart fresh or use your guest save?",
                            "Start Fresh", "Use Guest Save",
                            () => StartSelectedLogin(result, generation,
                                CloudSaveManager.StartMode.StartFresh),
                            () => StartSelectedLogin(result, generation,
                                CloudSaveManager.StartMode.UseGuestSave)))
                        FailPendingLogin("Could not show the save choice. Please try again.");
                    break;

                case CloudSaveManager.SaveAvailability.Online:
                case CloudSaveManager.SaveAvailability.LocalOnly:
                    bool online = availability == CloudSaveManager.SaveAvailability.Online;
                    string message = online
                        ? "This account has saved progress.\nSwitch accounts? Your guest save stays."
                        : "This account has a device save.\nSwitch accounts? Your guest save stays.";
                    if (!ShowSaveChoice(message, "Switch Account", "Stay Guest",
                            () => StartSelectedLogin(result, generation,
                                online ? CloudSaveManager.StartMode.LoadOnline :
                                    CloudSaveManager.StartMode.LoadLocalAccount),
                            CancelPendingLogin))
                        FailPendingLogin("Could not show the account choice. Please try again.");
                    break;

                default:
                    FailPendingLogin("Could not check this account's save. Your guest save is unchanged.");
                    break;
            }
        });
    }

    private void StartSelectedLogin(LoginResult result, int generation,
        CloudSaveManager.StartMode mode)
    {
        if (generation != authenticationGeneration) return;
        CloseSaveChoice();
        if (!ActivateVerifiedSession(result))
        {
            FailPendingLogin("Could not open a verified account session. Please sign in again.");
            return;
        }
        openingAccountSave = true;
        SetFeedbackMessage("Opening account save...", processColor);
        BeginCloudSaveLogin(result, mode, generation, succeeded =>
        {
            if (generation != authenticationGeneration) return;
            if (!succeeded)
            {
                FailPendingLogin("Could not open this account's save. Your guest save is unchanged.");
                return;
            }

            SetFeedbackMessage("Finishing sign-in...", processColor);
            LinkDeviceAfterSaveChoice(result, generation,
                deviceLinked => FinishSelectedLogin(result, deviceLinked, generation, mode));
        });
    }

    private void LinkDeviceAfterSaveChoice(LoginResult result, int generation,
        Action<bool> onComplete)
    {
        if (!TryGetDeviceId(out string deviceId))
        {
            onComplete(false);
            return;
        }

        bool forceLink = !string.IsNullOrEmpty(PlayerPrefs.GetString(DeviceLinkedPlayFabIdKey, string.Empty)) &&
            !string.Equals(PlayerPrefs.GetString(DeviceLinkedPlayFabIdKey, string.Empty),
                result.PlayFabId, StringComparison.Ordinal);
        Action<PlayFabError> onError = error =>
        {
            if (generation != authenticationGeneration) return;
            Debug.LogWarning("Device sign-in could not be enabled: " + error.ErrorMessage, this);
            onComplete(false);
        };
        if (Application.platform == RuntimePlatform.Android)
            PlayFabClientAPI.LinkAndroidDeviceID(new LinkAndroidDeviceIDRequest
            {
                AndroidDeviceId = deviceId,
                ForceLink = forceLink
            }, _ => { if (generation == authenticationGeneration) onComplete(true); }, onError);
        else
            PlayFabClientAPI.LinkIOSDeviceID(new LinkIOSDeviceIDRequest
            {
                DeviceId = deviceId,
                ForceLink = forceLink
            }, _ => { if (generation == authenticationGeneration) onComplete(true); }, onError);
    }

    private void FinishSelectedLogin(LoginResult result, bool deviceLinked, int generation,
        CloudSaveManager.StartMode mode)
    {
        if (generation != authenticationGeneration) return;
        manualLoginInProgress = false;
        openingAccountSave = false;
        string welcomeName = string.IsNullOrEmpty(loggedInPlayerName)
            ? "Player" : loggedInPlayerName;
        PlayerPrefs.SetInt("LoginChoice", 2);
        PlayerPrefs.SetString("SavedPlayerName", welcomeName);
        if (deviceLinked)
        {
            PlayerPrefs.SetString(RememberedPlayFabIdKey, result.PlayFabId);
            PlayerPrefs.SetString(DeviceLinkedPlayFabIdKey, result.PlayFabId);
        }
        else PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();

        isGuest = false;
        // This is the account greeting, not an engineer-name confirmation.
        // Fresh saves stay "Guest" until Bhan's prompt is confirmed. Imported
        // guest saves and returning account saves keep their chosen name.
        UpdatePlayerNameDisplay();
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        SetFeedbackMessage(cloud != null && !string.IsNullOrEmpty(cloud.LastSyncError)
            ? "Signed in. Online sync will retry; progress is saved on this device."
            : "Login successful. Save is ready.", successColor);
        if (returnToMainMenuAfterLogin) CompleteMainMenuAuthenticationRequest();
        else Invoke(nameof(LoadGameScene), 1.5f);
    }

    private void CancelPendingLogin()
    {
        CloseSaveChoice();
        manualLoginInProgress = false;
        openingAccountSave = false;
        IsCloudSaveReady = false;
        PlayFabClientAPI.ForgetAllCredentials();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        isGuest = true;
        loggedInPlayerName = "Guest";
        PlayerPrefs.SetInt("LoginChoice", 1);
        PlayerPrefs.SetString("SavedPlayerName", "Guest");
        PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();
        UpdatePlayerNameDisplay();
        if (returnToMainMenuAfterLogin) CompleteMainMenuAuthenticationRequest();
        else CloseAuthCanvas();
    }

    private void FailPendingLogin(string message)
    {
        CloseSaveChoice();
        manualLoginInProgress = false;
        if (!openingAccountSave)
        {
            SetFeedbackMessage(message, errorColor);
            return;
        }
        openingAccountSave = false;
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud != null) cloud.EndSession();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        PlayFabClientAPI.ForgetAllCredentials();
        if (PlayerPrefs.GetInt("LoginChoice", 0) == 2)
        {
            PlayerPrefs.SetInt("LoginChoice", 0);
            PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
            PlayerPrefs.Save();
        }
        isGuest = IsGuestSelected;
        loggedInPlayerName = isGuest ? "Guest" : string.Empty;
        UpdatePlayerNameDisplay();
        SetFeedbackMessage(message, errorColor);
    }

    private void BeginCloudSaveLogin(LoginResult result, CloudSaveManager.StartMode mode, int generation,
        Action<bool> onComplete)
    {
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud == null)
        {
            Debug.LogError("Cloud save manager is missing from PlayerDataManager.", this);
            onComplete(false);
            return;
        }
        cloud.BeginSession(result, mode, ready =>
        {
            if (generation != authenticationGeneration) return;
            IsCloudSaveReady = ready;
            onComplete(ready);
        });
    }

    private bool ShowSaveChoice(string message, string leftLabel, string rightLabel,
        Action onLeft, Action onRight)
    {
        CloseSaveChoice();
        if (saveChoiceDialog == null) return false;
        if (!saveChoiceDialog.Show("ACCOUNT SAVE", message, leftLabel, rightLabel,
                onLeft, onRight))
            return false;
        cancelSaveChoice = CancelPendingLogin;
        return true;
    }

    private void CloseSaveChoice()
    {
        cancelSaveChoice = null;
        if (saveChoiceDialog != null) saveChoiceDialog.Hide();
    }

    private void OnDestroy()
    {
        CloseSaveChoice();
        if (Instance == this) Instance = null;
    }

    private void OnLoginError(PlayFabError error, int generation)
    {
        if (this == null || generation != authenticationGeneration) return;
        manualLoginInProgress = false;
        loginPassword.text = string.Empty;
        SetFeedbackMessage("Login Failed: " + error.ErrorMessage, errorColor);
    }

    private void CompleteMainMenuAuthenticationRequest()
    {
        CancelInvoke(nameof(LoadGameScene));
        returnToMainMenuAfterLogin = false;
        if (authCanvas != null) authCanvas.SetActive(false);
        MainMenuAuthenticationSucceeded?.Invoke();
    }


    [Serializable]
    private sealed class PolicyAgreement
    {
        public string termsVersion = CivilCraftAccountPolicy.TermsVersion;
        public string privacyVersion = CivilCraftAccountPolicy.PrivacyVersion;
        public string acceptedAt;
    }

    private IEnumerator CompleteRegistration(RegisterPlayFabUserResult result, string username,
        string email, bool emailUpdates, string acceptedAt, int generation)
    {
        var context = result?.AuthenticationContext;
        string ticket = context?.ClientSessionTicket;
        bool policySaved = false, preferenceSaved = false;
        bool verificationRequested = false, alreadyVerified = false;
        long verificationStatus = 0;
        float nextResendAt = 0f;
        if (context != null && context.IsClientLoggedIn())
        {
            int waitingFor = 2;
            PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest
            {
                AuthenticationContext = context,
                Permission = UserDataPermission.Private,
                Data = new Dictionary<string, string>
                {
                    ["PolicyAcceptedAt"] = acceptedAt,
                    ["TermsAcceptedVersion"] = CivilCraftAccountPolicy.TermsVersion,
                    ["PrivacyAcceptedVersion"] = CivilCraftAccountPolicy.PrivacyVersion,
                    ["PolicyAgreement"] = JsonUtility.ToJson(new PolicyAgreement { acceptedAt = acceptedAt })
                }
            }, _ => { policySaved = true; waitingFor--; }, _ => waitingFor--);

            // The news preference is independent of the required verification request.
            float deadline = Time.realtimeSinceStartup + 25f;
            StartCoroutine(CivilCraftAccountWebsite.SaveEmailPreference(ticket, emailUpdates, succeeded =>
            {
                preferenceSaved = succeeded;
                waitingFor--;
            }));

            SetFeedbackMessage("Requesting verification email…", processColor);
            yield return CivilCraftAccountWebsite.RequestVerification(ticket, email, (code, reply) =>
            {
                verificationStatus = code;
                verificationRequested = reply?.success == true;
                alreadyVerified = verificationRequested && reply.alreadyVerified;
                if (verificationRequested && !alreadyVerified)
                    nextResendAt = Time.realtimeSinceStartup + Mathf.Clamp(reply.cooldownSeconds, 1, 600);
                else if (code == 429)
                    nextResendAt = Time.realtimeSinceStartup + 60f;
                if (!verificationRequested) Debug.LogWarning($"Website verification request failed (HTTP {code}).", this);
            });
            yield return new WaitUntil(() => waitingFor == 0 || Time.realtimeSinceStartup >= deadline ||
                generation != authenticationGeneration);
        }

        if (generation != authenticationGeneration) yield break;
        registrationInProgress = false;
        loginUsername.text = username;
        string message = alreadyVerified ? "Account created. Your email is verified. Sign in to continue." :
            verificationRequested ? "Account created. Verification requested. Check your inbox before signing in." :
            string.IsNullOrEmpty(ticket) || verificationStatus == 401
                ? "Account created. Sign in to request your verification email."
                : "Account created, but verification could not be requested. Select Resend Verification Email to try again.";
        if (!policySaved)
            message += " Your agreement could not be recorded. Contact Civil Craft support.";
        else if (!preferenceSaved && emailUpdates)
            message += " Set email updates in Player Settings on the website.";
        resendAvailableAt = nextResendAt;
        RememberVerificationPrompt(alreadyVerified || verificationStatus == 401 ? null : ticket, email, message);
        ShowLoginPanel();
        SetFeedbackMessage(message, verificationRequested && policySaved && (preferenceSaved || !emailUpdates) ? successColor : errorColor);
    }

}
