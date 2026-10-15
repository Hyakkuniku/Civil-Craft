using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One short-lived, noninteractive screen-space bubble per validated sender.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(160)]
public sealed class PlayerChatBubble : MonoBehaviour
{
    private FusionMultiplayerAvatar owner;
    private readonly PlayerChatBubbleMessage message = new PlayerChatBubbleMessage();
    private GameObject ui;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform bubble;
    private TextMeshProUGUI label;
    private Transform lastAnchor;
    private Renderer[] bodyRenderers;
    private float headHeight = 2.3f;
    private float nextHeadCheck;
    private string laidOutText;
    private float laidOutWidth;
    private float laidOutScale;

    public void Show(FusionMultiplayerAvatar sender, string text)
    {
        owner = sender;
        message.Show(text, Time.unscaledTime);
    }

    public void Clear()
    {
        message.Clear(); owner = null; lastAnchor = null; bodyRenderers = null;
        Hide();
    }

    private void LateUpdate()
    {
        if (!message.IsVisible(Time.unscaledTime) || owner == null || !owner.isActiveAndEnabled ||
            !owner.TryGetChatAnchor(out Transform anchor)) { Clear(); return; }
        if ((UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel) ||
            DialogueManager.IsAnyDialogueVisible ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
            (GameManager.Instance != null && (GameManager.Instance.IsTransitioning ||
                GameManager.Instance.CurrentState != GameManager.GameState.Normal))) { Hide(); return; }

        Camera camera = GameManager.Instance != null ? GameManager.Instance.MainCamera : Camera.main;
        if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null ||
            camera.gameObject.scene.name != FusionConnectionManager.HostWorldSceneName ||
            Vector3.SqrMagnitude(camera.transform.position - anchor.position) > 30f * 30f) { Hide(); return; }
        if (lastAnchor != anchor)
        {
            lastAnchor = anchor; bodyRenderers = anchor.GetComponentsInChildren<Renderer>(true);
            nextHeadCheck = 0f;
        }
        if (Time.unscaledTime >= nextHeadCheck)
        {
            nextHeadCheck = Time.unscaledTime + .5f;
            float top = anchor.position.y + 1.8f;
            foreach (Renderer body in bodyRenderers)
                if (body != null && body.enabled && body.gameObject.activeInHierarchy)
                    top = Mathf.Max(top, body.bounds.max.y);
            headHeight = Mathf.Clamp(top - anchor.position.y + .3f, 1.5f, 3.5f);
        }
        Vector3 screen = camera.WorldToScreenPoint(anchor.position + Vector3.up * headHeight);
        if (screen.z <= 0f) { Hide(); return; }
        EnsureUI(camera.gameObject.scene);
        if (!ui.activeSelf) ui.SetActive(true);
        float scale = Mathf.Clamp(Screen.height / 1080f, .75f, 1.5f);
        float width = Mathf.Min(380f * scale, Screen.safeArea.width * .65f);
        LayoutMessage(width, scale);
        Rect bounds = new Rect(screen.x - width / 2f, screen.y + 8f * scale, width, bubble.sizeDelta.y);
        if (!Contains(Screen.safeArea, bounds) || !Contains(camera.pixelRect, bounds) ||
            UIReservedRegionLayout.OverlapsVisibleReservation(bounds, 4f)) { Hide(); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,
            new Vector2(screen.x, bounds.yMin), null, out Vector2 local);
        bubble.anchoredPosition = local;
        group.alpha = message.Alpha(Time.unscaledTime);
    }

    private void LayoutMessage(float width, float scale)
    {
        if (laidOutText == message.Text && Mathf.Approximately(laidOutWidth, width) && Mathf.Approximately(laidOutScale, scale)) return;
        label.fontSize = 25f * scale;
        label.text = message.Text;
        float height = label.GetPreferredValues(message.Text, width - 28f * scale, Mathf.Infinity).y + 24f * scale;
        bubble.sizeDelta = new Vector2(width, height);
        label.rectTransform.offsetMin = new Vector2(14f * scale, 12f * scale);
        label.rectTransform.offsetMax = new Vector2(-14f * scale, -12f * scale);
        laidOutText = message.Text; laidOutWidth = width; laidOutScale = scale;
    }

    private void EnsureUI(Scene scene)
    {
        if (ui != null) return;
        ui = new GameObject("Player Chat Bubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        ui.layer = LayerMask.NameToLayer("UI");
        SceneManager.MoveGameObjectToScene(ui, scene);
        canvas = ui.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 21;
        group = ui.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
        // No GraphicRaycaster or world renderer: cannot intercept gameplay or appear in a map/capture camera.
        bubble = Rect("Paper", ui.transform);
        bubble.anchorMin = bubble.anchorMax = new Vector2(.5f, .5f); bubble.pivot = new Vector2(.5f, 0f);
        Image paper = bubble.gameObject.AddComponent<Image>();
        paper.color = new Color32(255, 248, 229, 245); paper.raycastTarget = false;
        Outline border = bubble.gameObject.AddComponent<Outline>();
        border.effectColor = new Color32(83, 58, 39, 255); border.effectDistance = new Vector2(2, -2);
        label = Rect("Message", bubble).gameObject.AddComponent<TextMeshProUGUI>();
        SessionChatUI chat = FindObjectOfType<SessionChatUI>(true); // Once per sender, never per frame.
        label.font = chat != null ? chat.ChatFont : TMP_Settings.defaultFontAsset;
        label.color = new Color32(62, 43, 30, 255); label.alignment = TextAlignmentOptions.Center;
        label.richText = false; label.parseCtrlCharacters = false; label.raycastTarget = false;
        label.enableWordWrapping = true; label.enableAutoSizing = false;
        laidOutText = null;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin && inner.xMax <= outer.xMax &&
        inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
    private void Hide() { if (ui != null) ui.SetActive(false); }
    private void OnDisable() => Clear();
    private void OnDestroy()
    {
        if (ui == null) return;
        if (Application.isPlaying) Destroy(ui); else DestroyImmediate(ui);
    }
}

