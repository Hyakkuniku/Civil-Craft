using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the minimap locked by default and reveals it only after the persistent
/// player unlock has been earned. Attach this to the always-active MinimapCamera.
/// </summary>
[DisallowMultipleComponent]
public sealed class MinimapUnlockController : MonoBehaviour
{
    private const string DefaultFeatureId = "minimap";

    [SerializeField] private GameObject minimapPanel;
    [SerializeField] private Camera minimapCamera;
    [SerializeField, Tooltip("Persistent feature ID that reveals the minimap.")]
    private string requiredFeatureId = DefaultFeatureId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindObjectOfType<MinimapUnlockController>(true) != null)
            return;

        GameObject panel = null;
        Camera camera = null;
        foreach (Transform candidate in FindObjectsOfType<Transform>(true))
        {
            if (candidate == null || candidate.gameObject.scene != scene) continue;

            if (candidate.name == "MinimapPanel")
                panel = candidate.gameObject;
            else if (candidate.name == "MinimapCamera")
                camera = candidate.GetComponent<Camera>();
        }

        if (panel == null && camera == null)
            return;

        GameObject owner = camera != null ? camera.gameObject : panel;
        MinimapUnlockController controller = owner.AddComponent<MinimapUnlockController>();
        controller.minimapPanel = panel;
        controller.minimapCamera = camera;

        // sceneLoaded runs before Start, so enforce the locked state immediately.
        controller.RefreshVisibility();
    }

    private void Awake()
    {
        if (minimapCamera == null)
            minimapCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnMinimapUnlockChanged += RefreshVisibility;
            PlayerDataManager.Instance.OnFeatureUnlocksChanged += RefreshVisibility;
        }

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnMinimapUnlockChanged -= RefreshVisibility;
            PlayerDataManager.Instance.OnFeatureUnlocksChanged -= RefreshVisibility;
        }
    }

    /// <summary>Hook this to the reward/pickup that should grant the minimap.</summary>
    public void UnlockMinimap()
    {
        if (PlayerDataManager.Instance == null)
        {
            Debug.LogWarning("[MinimapUnlockController] PlayerDataManager is not available.", this);
            return;
        }

        PlayerDataManager.Instance.UnlockFeature(GetFeatureId());
        RefreshVisibility();
    }

    public void RefreshVisibility()
    {
        // The feature-ID path matches Shop and every newer unlockable system.
        // Keep the legacy boolean fallback so existing saves remain compatible.
        bool unlocked = PlayerDataManager.Instance != null &&
                        PlayerDataManager.Instance.CurrentData != null &&
                        (PlayerDataManager.Instance.IsFeatureUnlocked(GetFeatureId()) ||
                         PlayerDataManager.Instance.CurrentData.hasUnlockedMinimap);
        bool overworldVisible = GameManager.Instance == null || !GameManager.Instance.IsInBuildMode();
        bool lessonClosed = LessonUIManager.Instance == null || !LessonUIManager.Instance.IsOpen;
        bool shouldShow = unlocked && overworldVisible && lessonClosed;

        if (minimapPanel != null)
            minimapPanel.SetActive(shouldShow);
        if (minimapCamera != null)
            minimapCamera.enabled = shouldShow;
    }

    public static void RefreshAll()
    {
        foreach (MinimapUnlockController controller in
                 FindObjectsOfType<MinimapUnlockController>(true))
        {
            controller.RefreshVisibility();
        }
    }

    private string GetFeatureId()
    {
        return string.IsNullOrWhiteSpace(requiredFeatureId)
            ? DefaultFeatureId
            : requiredFeatureId;
    }
}
