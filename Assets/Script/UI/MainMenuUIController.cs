using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slides a manually-created Main Menu button panel between an off-screen position
/// and the center of its parent Canvas. Attach this to an always-active UI manager.
/// </summary>
public class MainMenuUIController : MonoBehaviour
{
    [Header("Dropdown Panel")]
    [SerializeField] private RectTransform dropdownPanel;
    [SerializeField] private CanvasGroup dropdownCanvasGroup;
    [Tooltip("The original full-screen Click/Press to Play button.")]
    [SerializeField] private GameObject initialPlayButton;
    [Tooltip("Existing Main Menu elements hidden while the dropdown is open.")]
    [SerializeField] private GameObject[] uiToHideWhileDropdownOpen;

    [Header("Slide Positions")]
    [Tooltip("Final anchored position. Use (0, 0) with centered anchors/pivot.")]
    [SerializeField] private Vector2 onScreenPosition = Vector2.zero;
    [Tooltip("Used when Calculate Off-Screen Position Automatically is disabled.")]
    [SerializeField] private Vector2 offScreenPosition = new Vector2(0f, 700f);
    [SerializeField] private bool calculateOffScreenPositionAutomatically = true;
    [Min(0f)] [SerializeField] private float offScreenPadding = 50f;

    [Header("Animation")]
    [Min(0.01f)] [SerializeField] private float slideDuration = 0.45f;
    [SerializeField] private bool fadeWithSlide = true;
    [SerializeField] private bool startHidden = true;

    [Header("Button Cascade")]
    [SerializeField] private bool animateButtonsIndividually = true;
    [Min(0f)] [SerializeField] private float buttonCascadeStartDelay = 0.16f;
    [Min(0f)] [SerializeField] private float buttonStaggerDelay = 0.07f;
    [Min(0.01f)] [SerializeField] private float buttonEntranceDuration = 0.2f;
    [Range(0.1f, 1f)] [SerializeField] private float buttonStartScale = 0.82f;

    [Header("Existing Managers")]
    [SerializeField] private SceneController sceneController;
    [SerializeField] private SettingsManager settingsManager;
    [SerializeField] private AchievementUIManager achievementManager;
    [SerializeField] private PlayFabAuthManager authManager;
    [Tooltip("The existing authentication canvas in the Main Menu scene.")]
    [SerializeField] private GameObject authCanvas;

    public bool IsPanelShown { get; private set; }
    public bool IsAnimating => slideCoroutine != null || buttonAnimationCoroutine != null;

    private Coroutine slideCoroutine;
    private Coroutine buttonAnimationCoroutine;
    private readonly List<ButtonAnimationState> buttonAnimationStates =
        new List<ButtonAnimationState>();
    private readonly Dictionary<GameObject, bool> supportingUIStates =
        new Dictionary<GameObject, bool>();
    private bool waitingForAuthentication;

    private void Awake()
    {
        if (sceneController == null) sceneController = FindObjectOfType<SceneController>();
        if (settingsManager == null) settingsManager = FindObjectOfType<SettingsManager>(true);
        if (achievementManager == null)
            achievementManager = FindObjectOfType<AchievementUIManager>(true);
        if (authManager == null) authManager = FindObjectOfType<PlayFabAuthManager>(true);
        if (authCanvas == null && authManager != null) authCanvas = authManager.authCanvas;

        if (dropdownCanvasGroup == null && dropdownPanel != null)
            dropdownCanvasGroup = dropdownPanel.GetComponent<CanvasGroup>();
    }

    private void Start()
    {
        if (dropdownPanel == null)
        {
            Debug.LogError($"{nameof(MainMenuUIController)} on '{name}' needs a Dropdown Panel reference.", this);
            enabled = false;
            return;
        }

        dropdownPanel.gameObject.SetActive(true);
        RefreshPanelLayout();

        if (startHidden)
        {
            dropdownPanel.anchoredPosition = GetOffScreenPosition();
            SetCanvasGroupState(0f, false);
            IsPanelShown = false;
        }
        else
        {
            dropdownPanel.anchoredPosition = onScreenPosition;
            SetCanvasGroupState(1f, true);
            IsPanelShown = true;
        }
    }

    /// <summary>Hook this to the original Click to Play button.</summary>
    public void OnClickToPlay()
    {
        if (authManager != null && authManager.IsPlayerLoggedIn)
        {
            ShowMenuPanel();
            return;
        }

        if (authManager != null)
        {
            waitingForAuthentication = true;
            authManager.MainMenuAuthenticationSucceeded -= HandleAuthenticationSucceeded;
            authManager.MainMenuAuthenticationSucceeded += HandleAuthenticationSucceeded;
            authManager.OpenAuthCanvasForMainMenu();
            return;
        }

        if (authCanvas != null)
        {
            authCanvas.SetActive(true);
            Debug.LogError(
                "MainMenuUIController needs PlayFabAuthManager to continue to the dropdown after login.",
                this);
            return;
        }

        Debug.LogError("MainMenuUIController: authentication references are missing.", this);
    }

    public void ShowMenuPanel()
    {
        if (dropdownPanel == null) return;
        StartSlide(true);
    }

    public void HideMenuPanel()
    {
        if (dropdownPanel == null) return;
        StartSlide(false);
    }

    public void ToggleMenuPanel()
    {
        if (IsPanelShown) HideMenuPanel();
        else ShowMenuPanel();
    }

    public void OnModeSelectionClicked()
    {
        // Preserve the existing login/guest decision before loading Mode Selection.
        if (authManager != null) authManager.OnMainPlayButtonClicked();
        else if (sceneController != null) sceneController.LoadModeSelection();
        else Debug.LogError("MainMenuUIController: SceneController is not assigned.", this);
    }

    public void OnSettingsClicked()
    {
        if (settingsManager != null) settingsManager.OpenSettings();
        else Debug.LogError("MainMenuUIController: SettingsManager is not assigned.", this);
    }

    public void OnAchievementsClicked()
    {
        if (achievementManager == null)
        {
            Debug.LogError("MainMenuUIController: AchievementUIManager is not assigned.", this);
            return;
        }

        HideMenuPanel();
        achievementManager.OpenPanel();
    }

    public void OnLoginClicked()
    {
        // Let the dropdown close underneath the full-screen authentication UI so
        // closing AuthCanvas returns the player to the original Main Menu state.
        HideMenuPanel();

        if (authManager != null)
        {
            waitingForAuthentication = true;
            authManager.MainMenuAuthenticationSucceeded -= HandleAuthenticationSucceeded;
            authManager.MainMenuAuthenticationSucceeded += HandleAuthenticationSucceeded;
            authManager.OpenAuthCanvasForMainMenu();
        }
        else if (authCanvas != null)
        {
            authCanvas.SetActive(true);
        }
        else
        {
            Debug.LogError("MainMenuUIController: AuthCanvas is not assigned.", this);
        }
    }

    private void HandleAuthenticationSucceeded()
    {
        if (!waitingForAuthentication) return;

        waitingForAuthentication = false;
        if (authManager != null)
            authManager.MainMenuAuthenticationSucceeded -= HandleAuthenticationSucceeded;

        ShowMenuPanel();
    }

    private void OnDestroy()
    {
        RestoreButtonAnimationStates();
        if (authManager != null)
            authManager.MainMenuAuthenticationSucceeded -= HandleAuthenticationSucceeded;
    }

    public void OnQuitClicked()
    {
        if (sceneController != null) sceneController.RequestQuit();
        else Debug.LogError("MainMenuUIController: SceneController is not assigned.", this);
    }

    private void StartSlide(bool show)
    {
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        if (buttonAnimationCoroutine != null)
        {
            StopCoroutine(buttonAnimationCoroutine);
            buttonAnimationCoroutine = null;
        }

        dropdownPanel.gameObject.SetActive(true);
        if (show && !IsPanelShown) CaptureAndHideSupportingUI();
        RefreshPanelLayout();
        if (show && animateButtonsIndividually)
        {
            PrepareButtonAnimationStates();
            buttonAnimationCoroutine = StartCoroutine(AnimateButtonsRoutine());
        }
        else
        {
            RestoreButtonAnimationStates();
        }
        IsPanelShown = show;
        slideCoroutine = StartCoroutine(SlideRoutine(show));
    }

    private IEnumerator SlideRoutine(bool show)
    {
        Vector2 startPosition = dropdownPanel.anchoredPosition;
        Vector2 targetPosition = show ? onScreenPosition : GetOffScreenPosition();
        float startAlpha = dropdownCanvasGroup != null ? dropdownCanvasGroup.alpha : 1f;
        float targetAlpha = show || !fadeWithSlide ? 1f : 0f;
        float duration = Mathf.Max(0.01f, slideDuration);
        float elapsed = 0f;

        if (dropdownCanvasGroup != null)
        {
            dropdownCanvasGroup.interactable = false;
            dropdownCanvasGroup.blocksRaycasts = false;
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);

            dropdownPanel.anchoredPosition = Vector2.LerpUnclamped(
                startPosition,
                targetPosition,
                easedTime);

            if (dropdownCanvasGroup != null && fadeWithSlide)
                dropdownCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, easedTime);

            yield return null;
        }

        dropdownPanel.anchoredPosition = targetPosition;
        SetCanvasGroupState(targetAlpha, show);
        if (!show) RestoreSupportingUI();
        slideCoroutine = null;
    }

    private void PrepareButtonAnimationStates()
    {
        RestoreButtonAnimationStates();

        Button[] buttons = dropdownPanel.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();

            ButtonAnimationState state = new ButtonAnimationState
            {
                button = button,
                rect = button.transform as RectTransform,
                group = group,
                originalScale = button.transform.localScale,
                originalAlpha = group.alpha,
                originalGroupInteractable = group.interactable,
                originalBlocksRaycasts = group.blocksRaycasts,
                originalButtonInteractable = button.interactable
            };
            buttonAnimationStates.Add(state);

            button.transform.localScale = state.originalScale * buttonStartScale;
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            button.interactable = false;
        }
    }

    private IEnumerator AnimateButtonsRoutine()
    {
        float delay = 0f;
        while (delay < buttonCascadeStartDelay)
        {
            delay += Time.unscaledDeltaTime;
            yield return null;
        }

        float duration = Mathf.Max(0.01f, buttonEntranceDuration);
        float totalDuration = duration +
                              Mathf.Max(0, buttonAnimationStates.Count - 1) * buttonStaggerDelay;
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            for (int index = 0; index < buttonAnimationStates.Count; index++)
            {
                ButtonAnimationState state = buttonAnimationStates[index];
                if (state.button == null || state.group == null) continue;

                float buttonTime = elapsed - index * buttonStaggerDelay;
                float normalizedTime = Mathf.Clamp01(buttonTime / duration);
                if (buttonTime < 0f) continue;

                float easedTime = normalizedTime * normalizedTime *
                                  (3f - 2f * normalizedTime);
                state.group.alpha = Mathf.Lerp(0f, state.originalAlpha, easedTime);
                state.button.transform.localScale = Vector3.LerpUnclamped(
                    state.originalScale * buttonStartScale,
                    state.originalScale,
                    easedTime);

                if (normalizedTime >= 1f) RestoreButtonInput(state);
            }

            yield return null;
        }

        foreach (ButtonAnimationState state in buttonAnimationStates)
        {
            if (state.button == null || state.group == null) continue;
            state.button.transform.localScale = state.originalScale;
            state.group.alpha = state.originalAlpha;
            RestoreButtonInput(state);
        }

        buttonAnimationCoroutine = null;
    }

    private void RestoreButtonAnimationStates()
    {
        foreach (ButtonAnimationState state in buttonAnimationStates)
        {
            if (state.button == null || state.group == null) continue;
            state.button.transform.localScale = state.originalScale;
            state.group.alpha = state.originalAlpha;
            RestoreButtonInput(state);
        }

        buttonAnimationStates.Clear();
    }

    private static void RestoreButtonInput(ButtonAnimationState state)
    {
        state.group.interactable = state.originalGroupInteractable;
        state.group.blocksRaycasts = state.originalBlocksRaycasts;
        state.button.interactable = state.originalButtonInteractable;
    }

    private void CaptureAndHideSupportingUI()
    {
        supportingUIStates.Clear();

        RememberAndHide(initialPlayButton);
        if (uiToHideWhileDropdownOpen == null) return;

        foreach (GameObject uiObject in uiToHideWhileDropdownOpen)
            RememberAndHide(uiObject);
    }

    private void RememberAndHide(GameObject uiObject)
    {
        if (uiObject == null || uiObject == dropdownPanel.gameObject ||
            supportingUIStates.ContainsKey(uiObject))
        {
            return;
        }

        supportingUIStates.Add(uiObject, uiObject.activeSelf);
        uiObject.SetActive(false);
    }

    private void RestoreSupportingUI()
    {
        foreach (KeyValuePair<GameObject, bool> entry in supportingUIStates)
        {
            if (entry.Key != null) entry.Key.SetActive(entry.Value);
        }

        supportingUIStates.Clear();
    }

    private Vector2 GetOffScreenPosition()
    {
        if (!calculateOffScreenPositionAutomatically) return offScreenPosition;

        RectTransform parentRect = dropdownPanel.parent as RectTransform;
        if (parentRect == null) return offScreenPosition;

        float verticalDistance = parentRect.rect.height * 0.5f +
                                 dropdownPanel.rect.height * 0.5f +
                                 offScreenPadding;
        return new Vector2(onScreenPosition.x, onScreenPosition.y + verticalDistance);
    }

    private void RefreshPanelLayout()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(dropdownPanel);
    }

    private void SetCanvasGroupState(float alpha, bool acceptsInput)
    {
        if (dropdownCanvasGroup == null) return;
        dropdownCanvasGroup.alpha = alpha;
        dropdownCanvasGroup.interactable = acceptsInput;
        dropdownCanvasGroup.blocksRaycasts = acceptsInput;
    }

    private sealed class ButtonAnimationState
    {
        public Button button;
        public RectTransform rect;
        public CanvasGroup group;
        public Vector3 originalScale;
        public float originalAlpha;
        public bool originalGroupInteractable;
        public bool originalBlocksRaycasts;
        public bool originalButtonInteractable;
    }
}
