using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShopButtonTrigger : MonoBehaviour
{
    private const string DefaultShopFeatureId = "shop";
    private const string ShopIntroductionLessonId = "Sequence_Shop";

    [SerializeField] private ShopManager shopManager;
    [SerializeField] private Button button;
    [SerializeField] private bool wireButtonAutomatically = true;
    [SerializeField, Tooltip("Hide this overworld Shop button while the player is in Build Mode.")]
    private bool hideDuringBuildMode = true;
    [SerializeField, Tooltip("The persistent feature ID required before this button can appear.")]
    private string requiredFeatureId = "shop";
    [SerializeField, Tooltip("Disable only for testing. When enabled, the Shop remains hidden until its feature ID is unlocked.")]
    private bool requirePersistentUnlock = true;

    [Header("Unlock Guidance")]
    [SerializeField, Tooltip("Point to the unlocked Shop until its first introduction is complete.")]
    private bool showUnlockPointer = true;
    [SerializeField] private Vector2 unlockPointerOffset = new Vector2(110f, 0f);
    [SerializeField] private float unlockPointerRotation = 90f;

    private GameManager subscribedGameManager;
    private PlayerDataManager subscribedPlayerData;
    private bool visibleOutsideBuildMode = true;
    private TutorialPointer unlockPointer;
    private TutorialPointer unlockPointerTemplate;

    public bool CanShowHudEntry => isActiveAndEnabled && IsShopUnlocked() && visibleOutsideBuildMode &&
        (!hideDuringBuildMode || !IsBuilding()) &&
        (shopManager == null || shopManager.Panel == null || !shopManager.Panel.activeInHierarchy);

    private void Reset()
    {
        button = GetComponent<Button>();
    }

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        ResolveShopManager();
        visibleOutsideBuildMode = gameObject.activeSelf;

        if (wireButtonAutomatically && button != null)
            button.onClick.AddListener(OpenShop);
        DialogueHudButtonGate.BindShop(gameObject, this);
    }

    private void Start()
    {
        SubscribeToFeatureUnlocks();
        SubscribeToBuildMode();
        RefreshVisibility();
    }

    private void LateUpdate()
    {
        SubscribeToFeatureUnlocks();
        SubscribeToBuildMode();
        RefreshUnlockPointer();
    }

    private void OnDisable()
    {
        // This button self-hides while locked/building. Its unlock/build
        // subscriptions must remain so it can become visible again.
        HideUnlockPointer();
    }

    private void OnDestroy()
    {
        ReleaseUnlockPointer();
        if (wireButtonAutomatically && button != null)
            button.onClick.RemoveListener(OpenShop);

        if (subscribedGameManager != null)
        {
            subscribedGameManager.OnEnterBuildMode.RemoveListener(RefreshVisibility);
            subscribedGameManager.OnExitBuildMode.RemoveListener(RefreshVisibility);
        }

        if (subscribedPlayerData != null)
            subscribedPlayerData.OnFeatureUnlocksChanged -= RefreshVisibility;
    }

    public void OpenShop()
    {
        if (!IsShopUnlocked())
        {
            RefreshVisibility();
            return;
        }

        if (hideDuringBuildMode && IsBuilding())
        {
            RefreshVisibility();
            return;
        }

        ResolveShopManager();
        if (shopManager != null)
            shopManager.OpenShop();
        else
            Debug.LogError("[ShopButtonTrigger] No ShopManager exists in this scene.", this);
    }

    private void ResolveShopManager()
    {
        if (shopManager == null)
            shopManager = FindObjectOfType<ShopManager>(true);
    }

    public void RefreshVisibility()
    {
        bool allowedByBuildMode = !hideDuringBuildMode || !IsBuilding();
        gameObject.SetActive(IsShopUnlocked() && allowedByBuildMode && visibleOutsideBuildMode);
    }

    private static bool HasBlockingTutorialForUnlockPointer(TutorialManager tutorial)
    {
        if (!tutorial.IsTutorialActive) return false;
        TutorialStep step = tutorial.CurrentStep;
        // "Go back to Professor Bhan" and "Find Reed" remain active as
        // passive walking reminders. They own no UI pointer or required action,
        // so the Shop hint can coexist without finishing or changing that step.
        // Some such reminders have no waypoint; do not depend on their text/ID.
        return step == null || step.screenPosition != TutorialPosition.Left ||
            step.showNextButton || step.canSkip || step.advanceOnClick || step.usePointer ||
            step.requiredAction != TutorialStepAction.None || step.lockLook || step.lockJump ||
            step.lockRun || step.worldHighlightObject != null;
    }

    private bool ShouldShowUnlockPointer()
    {
        PlayerData data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        TutorialManager tutorial = TutorialManager.Instance;
        if (!showUnlockPointer || !requirePersistentUnlock || !isActiveAndEnabled || !IsShopUnlocked() ||
            data == null || (data.completedLessons != null && data.completedLessons.Contains(ShopIntroductionLessonId)) ||
            button == null || !button.isActiveAndEnabled || !button.IsInteractable() ||
            tutorial == null || !tutorial.isActiveAndEnabled || tutorial.gameObject.scene != gameObject.scene ||
            HasBlockingTutorialForUnlockPointer(tutorial) || tutorial.IsTutorialPresentationSuppressed ||
            gameObject.scene.name == "Multiplayer" || MultiplayerChallengeLobbyUI.IsChallengeActive ||
            (FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsGuestSaveProtected) ||
            LoadingScreenManager.IsLoading || Time.timeScale <= 0f || DialogueManager.IsAnyDialogueVisible ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
            (GameManager.Instance != null && (GameManager.Instance.IsTransitioning ||
                GameManager.Instance.CurrentState != GameManager.GameState.Normal)) ||
            (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel)) return false;
        if (shopManager == null || shopManager.Panel == null || shopManager.gameObject.scene != gameObject.scene ||
            shopManager.Panel.activeInHierarchy) return false;
        Canvas canvas = button.GetComponentInParent<Canvas>();
        return canvas != null && canvas.rootCanvas.isActiveAndEnabled;
    }

    private void RefreshUnlockPointer()
    {
        if (shopManager == null && ShopManager.Instance != null && ShopManager.Instance.gameObject.scene == gameObject.scene)
            shopManager = ShopManager.Instance;
        if (!ShouldShowUnlockPointer())
        {
            HideUnlockPointer();
            return;
        }
        TutorialPointer template = TutorialManager.Instance.SharedPointer;
        if (template == null || template.gameObject.activeInHierarchy)
        {
            // Required tutorials and other authored pointer owners take priority.
            HideUnlockPointer();
            return;
        }
        if (unlockPointer != null && unlockPointerTemplate != template) ReleaseUnlockPointer();
        if (unlockPointer == null)
        {
            Canvas canvas = button.GetComponentInParent<Canvas>();
            unlockPointer = Instantiate(template, canvas.rootCanvas.transform, false);
            unlockPointer.name = "Shop Unlock Pointer (Runtime)";
            unlockPointer.Hide();
            unlockPointerTemplate = template;
        }
        // Use edge fitting only on this dedicated copy. Existing authored
        // tutorial arrow offsets and ownership are never modified.
        unlockPointer.PointAtBuildTarget(button.transform as RectTransform, unlockPointerOffset, unlockPointerRotation);
    }

    private void HideUnlockPointer()
    {
        if (unlockPointer != null) unlockPointer.Hide();
    }

    private void ReleaseUnlockPointer()
    {
        HideUnlockPointer();
        if (unlockPointer != null) Destroy(unlockPointer.gameObject);
        unlockPointer = null;
        unlockPointerTemplate = null;
    }

    private void SubscribeToFeatureUnlocks()
    {
        if (subscribedPlayerData == PlayerDataManager.Instance) return;
        if (subscribedPlayerData != null)
            subscribedPlayerData.OnFeatureUnlocksChanged -= RefreshVisibility;
        subscribedPlayerData = PlayerDataManager.Instance;
        if (subscribedPlayerData != null)
            subscribedPlayerData.OnFeatureUnlocksChanged += RefreshVisibility;
        RefreshVisibility();
    }

    public bool IsShopUnlocked()
    {
        return !requirePersistentUnlock ||
               (PlayerDataManager.Instance != null &&
                PlayerDataManager.Instance.IsFeatureUnlocked(
                    string.IsNullOrWhiteSpace(requiredFeatureId)
                        ? DefaultShopFeatureId
                        : requiredFeatureId));
    }

    private void SubscribeToBuildMode()
    {
        if (subscribedGameManager == GameManager.Instance) return;

        if (subscribedGameManager != null)
        {
            subscribedGameManager.OnEnterBuildMode.RemoveListener(RefreshVisibility);
            subscribedGameManager.OnExitBuildMode.RemoveListener(RefreshVisibility);
        }

        subscribedGameManager = GameManager.Instance;
        if (subscribedGameManager == null) return;

        subscribedGameManager.OnEnterBuildMode.AddListener(RefreshVisibility);
        subscribedGameManager.OnExitBuildMode.AddListener(RefreshVisibility);
    }

    private static bool IsBuilding()
    {
        return GameManager.Instance != null &&
               GameManager.Instance.CurrentState == GameManager.GameState.Building;
    }
}
