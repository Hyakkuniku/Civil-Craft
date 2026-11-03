using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>Small scene-authored status card. It does not schedule tests or own results.</summary>
[DisallowMultipleComponent]
public sealed class MultiLoadTestHUD : MonoBehaviour
{
    public const string PanelName = "Multiple Live Load Status";
    public const string RemoteCanvasName = "Multiple Live Load Spectator UI";
    [SerializeField, Tooltip("Show the vehicle count card during local and spectator tests.")]
    private bool showVehicleCount;
    [SerializeField] private BridgePhysicsManager physics;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI status;
    [SerializeField] private RectTransform remotePanel;
    [SerializeField] private TextMeshProUGUI remoteStatus;
    private static MultiLoadTestHUD remoteHud;
    private static FusionMultiplayerAvatar remoteOwner;
    private static float nextRemoteLookup;
    private string previous;
    private float nextRefresh;

    public RectTransform Panel => panel;
    public TextMeshProUGUI Status => status;
    public RectTransform RemotePanel => remotePanel;
    public TextMeshProUGUI RemoteStatus => remoteStatus;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRemotePresentation() { remoteHud = null; remoteOwner = null; nextRemoteLookup = 0f; }

    public static void PresentRemote(FusionMultiplayerAvatar owner, string text)
    {
        if (owner == null || string.IsNullOrWhiteSpace(text)) { HideRemote(owner); return; }
        if ((remoteHud == null || remoteHud.gameObject.scene != owner.gameObject.scene) && Time.unscaledTime >= nextRemoteLookup)
        {
            remoteHud = null; nextRemoteLookup = Time.unscaledTime + .5f;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<MultiLoadTestHUD>())
                if (candidate != null && candidate.gameObject.scene == owner.gameObject.scene && candidate.remotePanel != null)
                { remoteHud = candidate; break; }
        }
        if (remoteHud == null || remoteHud.remotePanel == null || remoteHud.remoteStatus == null) return;
        if (!remoteHud.showVehicleCount)
        {
            if (remoteOwner != null) HideRemote(remoteOwner);
            return;
        }
        remoteOwner = owner;
        bool visible = (LevelCompleteManager.Instance == null || LevelCompleteManager.Instance.levelCompletePanel == null || !LevelCompleteManager.Instance.levelCompletePanel.activeInHierarchy) &&
            (LevelFailedManager.Instance == null || LevelFailedManager.Instance.levelFailedPanel == null || !LevelFailedManager.Instance.levelFailedPanel.activeInHierarchy);
        GameObject root = remoteHud.remotePanel.GetComponentInParent<Canvas>(true).gameObject;
        if (root.activeSelf != visible) { root.SetActive(visible); UIReservedRegionLayout.NotifyLayoutChanging(); }
        if (visible)
        {
            UIReservedRegionLayout.Register(remoteHud.remotePanel);
            if (remoteHud.remoteStatus.text != text) remoteHud.remoteStatus.text = text;
        }
    }

    public static void HideRemote(FusionMultiplayerAvatar owner)
    {
        if (owner != remoteOwner) return;
        if (remoteHud != null && remoteHud.remotePanel != null)
        {
            remoteHud.remotePanel.GetComponentInParent<Canvas>(true).gameObject.SetActive(false);
            UIReservedRegionLayout.NotifyLayoutChanging();
        }
        remoteOwner = null;
    }

    public void Configure(BridgePhysicsManager manager, RectTransform card, TextMeshProUGUI label)
    {
        physics = manager; panel = card; status = label;
    }

    private void Awake()
    {
        UIReservedRegionLayout.Register(panel);
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (panel == null || status == null) return;
        bool visible = showVehicleCount && physics != null && physics.MultiLoadSession != null && physics.IsSimulationActive &&
            GameManager.Instance != null && GameManager.Instance.IsInBuildMode();
        if (panel.gameObject.activeSelf != visible)
        {
            panel.gameObject.SetActive(visible);
            UIReservedRegionLayout.NotifyLayoutChanging();
            nextRefresh = 0f;
        }
        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        string text = ContractLiveLoadPresentation.SessionStatus(physics.MultiLoadSession);
        if (text == previous) return;
        previous = text; status.text = text;
    }

    private void OnDisable()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        previous = null; nextRefresh = 0f;
    }

    private void OnDestroy() { UIReservedRegionLayout.Unregister(panel); UIReservedRegionLayout.Unregister(remotePanel); }

    /// <summary>Shared by the explicit authoring command and old-scene runtime fallback.</summary>
    public static MultiLoadTestHUD Ensure(BuildUIController controller)
    {
        if (controller == null) return null;
        MultiLoadTestHUD hud = controller.GetComponent<MultiLoadTestHUD>();
        if (hud != null && hud.panel != null && hud.status != null)
        { EnsureRemoteCard(hud, controller, hud.status); return hud; }
        TMP_Text fontSource = controller.stressText != null ? controller.stressText : controller.actionLogText;
        Canvas canvas = fontSource != null ? fontSource.GetComponentInParent<Canvas>(true) : null;
        if (canvas == null) return null;
        Transform existing = canvas.transform.Find(PanelName);
        RectTransform root = existing != null ? existing as RectTransform :
            new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(.5f, 0f); root.pivot = new Vector2(.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 150f); root.sizeDelta = new Vector2(620f, 100f);
        Image image = root.GetComponent<Image>(); image.color = new Color32(89, 56, 33, 240); image.raycastTarget = false;
        Image sourceImage = controller.stressText != null ? controller.stressText.GetComponentInParent<Image>(true) : null;
        if (sourceImage != null) { image.sprite = sourceImage.sprite; image.type = Image.Type.Sliced; }
        TextMeshProUGUI label = root.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
        {
            RectTransform rect = new GameObject("Status", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
            rect.SetParent(root, false); label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        }
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(18f, 10f); label.rectTransform.offsetMax = new Vector2(-18f, -10f);
        label.font = fontSource.font; label.text = "Vehicle 2/4 — Cargo Truck\n1/4 passed";
        label.color = new Color32(255, 247, 224, 255); label.fontSize = 28f; label.enableAutoSizing = true;
        label.fontSizeMin = 23f; label.fontSizeMax = 28f; label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Ellipsis; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        if (hud == null) hud = controller.gameObject.AddComponent<MultiLoadTestHUD>();
        hud.Configure(controller.physicsManager, root, label);
        EnsureRemoteCard(hud, controller, label);
        root.gameObject.SetActive(false);
        return hud;
    }

    private static void EnsureRemoteCard(MultiLoadTestHUD hud, BuildUIController controller, TextMeshProUGUI source)
    {
        if (hud.remotePanel != null && hud.remoteStatus != null) return;
        GameObject canvasObject = null;
        foreach (GameObject root in controller.gameObject.scene.GetRootGameObjects())
            if (root.name == RemoteCanvasName) { canvasObject = root; break; }
        if (canvasObject == null)
        {
            canvasObject = new GameObject(RemoteCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, controller.gameObject.scene);
        }
        Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        CanvasScaler original = source.GetComponentInParent<CanvasScaler>(true);
        scaler.referenceResolution = original != null ? original.referenceResolution : new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = original != null ? original.matchWidthOrHeight : .5f;
        GameObject copy = Object.Instantiate(hud.panel.gameObject, canvasObject.transform, false);
        copy.name = PanelName; copy.SetActive(true);
        hud.remotePanel = (RectTransform)copy.transform; hud.remoteStatus = copy.GetComponentInChildren<TextMeshProUGUI>(true);
        canvasObject.SetActive(false);
    }
}
