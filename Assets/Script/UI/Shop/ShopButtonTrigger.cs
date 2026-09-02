using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShopButtonTrigger : MonoBehaviour
{
    private const string DefaultShopFeatureId = "shop";

    [SerializeField] private ShopManager shopManager;
    [SerializeField] private Button button;
    [SerializeField] private bool wireButtonAutomatically = true;
    [SerializeField, Tooltip("Hide this overworld Shop button while the player is in Build Mode.")]
    private bool hideDuringBuildMode = true;
    [SerializeField, Tooltip("The persistent feature ID required before this button can appear.")]
    private string requiredFeatureId = "shop";
    [SerializeField, Tooltip("Disable only for testing. When enabled, the Shop remains hidden until its feature ID is unlocked.")]
    private bool requirePersistentUnlock = true;

    private GameManager subscribedGameManager;
    private bool visibleOutsideBuildMode = true;

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
    }

    private void Start()
    {
        SubscribeToFeatureUnlocks();
        SubscribeToBuildMode();
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (wireButtonAutomatically && button != null)
            button.onClick.RemoveListener(OpenShop);

        if (subscribedGameManager != null)
        {
            subscribedGameManager.OnEnterBuildMode.RemoveListener(RefreshVisibility);
            subscribedGameManager.OnExitBuildMode.RemoveListener(RefreshVisibility);
        }

        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnFeatureUnlocksChanged -= RefreshVisibility;
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

    private void SubscribeToFeatureUnlocks()
    {
        if (PlayerDataManager.Instance == null) return;
        PlayerDataManager.Instance.OnFeatureUnlocksChanged -= RefreshVisibility;
        PlayerDataManager.Instance.OnFeatureUnlocksChanged += RefreshVisibility;
    }

    private bool IsShopUnlocked()
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
