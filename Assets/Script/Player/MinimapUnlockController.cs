using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
    private bool subscribedToPlayerData;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameObject panel = null;
        Camera camera = null;
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || candidate.gameObject.scene != scene) continue;

            if (candidate.name == "MinimapPanel")
                panel = candidate.gameObject;
            else if (candidate.name == "MinimapCamera")
                camera = candidate.GetComponent<Camera>();
        }

        if (panel == null && camera == null)
            return;

        if (camera == null && panel != null)
            camera = CreateFallbackMinimapCamera(scene, panel);

        MinimapUnlockController controller = FindControllerInScene(scene);
        if (controller == null || !controller.gameObject.activeInHierarchy)
        {
            // Never place the lifecycle controller on the locked panel itself.
            // An inactive panel cannot run Start and therefore cannot listen for
            // the event that is supposed to activate it.
            GameObject owner = camera != null && camera.gameObject.activeInHierarchy
                ? camera.gameObject
                : new GameObject("MinimapUnlockRuntime");
            if (owner.scene != scene)
                SceneManager.MoveGameObjectToScene(owner, scene);

            controller = owner.GetComponent<MinimapUnlockController>();
            if (controller == null)
                controller = owner.AddComponent<MinimapUnlockController>();
        }

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

    private void OnEnable()
    {
        TrySubscribeToPlayerData();
    }

    private void Start()
    {
        TrySubscribeToPlayerData();
        RefreshVisibility();
    }

    private void Update()
    {
        // PlayerDataManager normally exists before scene Start, but this also
        // covers bootstrap/loading orders where it is created one frame later.
        if (!subscribedToPlayerData)
        {
            TrySubscribeToPlayerData();
            if (subscribedToPlayerData)
                RefreshVisibility();
        }
    }

    private void OnDestroy()
    {
        if (subscribedToPlayerData && PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnMinimapUnlockChanged -= RefreshVisibility;
            PlayerDataManager.Instance.OnFeatureUnlocksChanged -= RefreshVisibility;
        }

        subscribedToPlayerData = false;
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

    private void TrySubscribeToPlayerData()
    {
        if (subscribedToPlayerData || PlayerDataManager.Instance == null) return;

        PlayerDataManager.Instance.OnMinimapUnlockChanged += RefreshVisibility;
        PlayerDataManager.Instance.OnFeatureUnlocksChanged += RefreshVisibility;
        subscribedToPlayerData = true;
    }

    private static MinimapUnlockController FindControllerInScene(Scene scene)
    {
        foreach (MinimapUnlockController candidate in
                 Resources.FindObjectsOfTypeAll<MinimapUnlockController>())
        {
            if (candidate != null && candidate.gameObject.scene == scene)
                return candidate;
        }

        return null;
    }

    private static Camera CreateFallbackMinimapCamera(Scene scene, GameObject panel)
    {
        RawImage mapImage = panel.GetComponentInChildren<RawImage>(true);
        RenderTexture targetTexture = mapImage != null
            ? mapImage.texture as RenderTexture
            : null;
        if (targetTexture == null)
        {
            Debug.LogWarning(
                $"[MinimapUnlockController] Scene '{scene.name}' has a MinimapPanel " +
                "but no MinimapCamera or RenderTexture to render into.", panel);
            return null;
        }

        GameObject cameraObject = new GameObject("MinimapCamera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);
        camera.orthographic = true;
        camera.orthographicSize = 15f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000f;
        camera.targetTexture = targetTexture;
        cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        MinimapFollow follow = cameraObject.AddComponent<MinimapFollow>();
        follow.mapHeight = 50f;
        follow.rotateWithPlayer = true;
        PlayerMotor player = null;
        foreach (PlayerMotor candidate in FindObjectsOfType<PlayerMotor>())
        {
            if (candidate != null && candidate.gameObject.scene == scene)
            {
                player = candidate;
                break;
            }
        }

        if (player != null)
        {
            follow.player = player.transform;
            follow.SnapToPlayer();
        }

        // This handler may run after ExpandedMinimapController's scene callback,
        // so ensure the dynamically repaired camera still receives expansion UI.
        cameraObject.AddComponent<ExpandedMinimapController>();
        return camera;
    }
}
