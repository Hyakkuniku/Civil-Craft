using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

internal sealed class CinematicCaptionOverlay
{
    private const float FadeSeconds = 0.18f;

    private readonly Canvas canvas;
    private readonly RectTransform safeArea;
    private readonly RectTransform panel;
    private readonly CanvasGroup group;
    private readonly TextMeshProUGUI title;
    private readonly TextMeshProUGUI body;
    private Vector2Int lastScreenSize;
    private Rect lastSafeArea;

    public CinematicCaptionOverlay(Scene scene)
    {
        var root = new GameObject("Cinematic Captions", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        if (root.scene != scene) SceneManager.MoveGameObjectToScene(root, scene);

        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        safeArea = CreateRect(root.transform, "Safe area");
        panel = CreateRect(safeArea, "Caption panel");
        panel.anchorMax = new Vector2(0.72f, 0f);
        panel.anchorMin = Vector2.zero;
        panel.offsetMin = new Vector2(32f, 30f);

        var background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.10f, 0.075f, 0.05f, 0.88f);
        background.raycastTarget = false;

        group = panel.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        title = CreateText(panel, "Heading", 26f, new Color32(255, 217, 147, 255));
        title.fontStyle = FontStyles.Bold;
        SetBounds(title.rectTransform, Vector2.up, Vector2.one,
            new Vector2(22f, -48f), new Vector2(-22f, -10f));

        body = CreateText(panel, "Text", 28f, new Color32(255, 249, 235, 255));
        canvas.enabled = false;
    }

    public void Show(string heading, string text)
    {
        bool hasHeading = !string.IsNullOrWhiteSpace(heading);
        bool hasBody = !string.IsNullOrWhiteSpace(text);
        title.gameObject.SetActive(hasHeading);
        body.gameObject.SetActive(hasBody);
        title.text = heading;
        body.text = text;

        float height = hasHeading ? (hasBody ? 116f : 60f) : 76f;
        panel.offsetMax = new Vector2(-16f, 30f + height);
        SetBounds(body.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(22f, 10f), new Vector2(-22f, hasHeading ? -50f : -10f));

        group.alpha = 0f;
        canvas.enabled = true;
        UpdateSafeArea();
    }

    public void SetProgress(float elapsed, float duration)
    {
        if (canvas == null || !canvas.enabled) return;
        UpdateSafeArea();
        group.alpha = Mathf.Clamp01(Mathf.Min(elapsed, duration - elapsed) / FadeSeconds);
    }

    public void Hide()
    {
        if (canvas != null) canvas.enabled = false;
    }

    public void Destroy()
    {
        if (canvas == null) return;
        canvas.enabled = false;
        Object.Destroy(canvas.gameObject);
    }

    private void UpdateSafeArea()
    {
        var size = new Vector2Int(Screen.width, Screen.height);
        Rect bounds = Screen.safeArea;
        if (size == lastScreenSize && bounds == lastSafeArea) return;

        lastScreenSize = size;
        lastSafeArea = bounds;
        Vector2 screen = new Vector2(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        bool valid = bounds.width > 0f && bounds.height > 0f;
        SetBounds(safeArea,
            valid ? new Vector2(bounds.xMin / screen.x, bounds.yMin / screen.y) : Vector2.zero,
            valid ? new Vector2(bounds.xMax / screen.x, bounds.yMax / screen.y) : Vector2.one,
            Vector2.zero, Vector2.zero);
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, float size, Color color)
    {
        var text = CreateRect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontSizeMin = size - 5f;
        text.fontSizeMax = size;
        text.enableAutoSizing = true;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.richText = false;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetBounds(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
