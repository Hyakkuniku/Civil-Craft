using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Only sizes the existing, authored material-replacement confirmation.
/// This component never creates controls, changes callbacks, or places materials.</summary>
[DisallowMultipleComponent]
public sealed class MaterialOverlapPanelLayout : MonoBehaviour
{
    [HideInInspector] public int authoredVersion;
    public RectTransform card;
    public RectTransform surface;
    public RectTransform divider;
    public TMP_Text titleText;
    public TMP_Text messageText;
    public Button confirmButton;
    public Button cancelButton;
    public Toggle dontShowAgainToggle;
    public TMP_Text toggleLabel;
    public RectTransform toggleBackground;
    public RectTransform toggleCheckmark;

    private Rect lastRoot;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private bool applied;

    private void OnEnable() { applied = false; ApplyLayout(); }

    private void LateUpdate()
    {
        RectTransform root = transform as RectTransform;
        if (root != null && (!applied || root.rect != lastRoot || Screen.safeArea != lastSafeArea ||
            lastScreen.x != Screen.width || lastScreen.y != Screen.height)) ApplyLayout();
    }

    public void ApplyLayout()
    {
        RectTransform root = transform as RectTransform;
        if (root == null || card == null) return;
        Rect available = root.rect;
        Canvas inheritedCanvas = GetComponentInParent<Canvas>(true);
        Canvas canvas = inheritedCanvas != null ? inheritedCanvas.rootCanvas : null;
        if (Application.isPlaying && canvas != null && Screen.width > 0 && Screen.height > 0)
        {
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Screen.safeArea.min, uiCamera, out Vector2 minimum) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Screen.safeArea.max, uiCamera, out Vector2 maximum))
            {
                minimum = Vector2.Max(minimum, available.min);
                maximum = Vector2.Min(maximum, available.max);
                if (maximum.x > minimum.x && maximum.y > minimum.y)
                    available = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            }
        }
        ApplyLayoutFor(available);
        lastRoot = root.rect;
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        applied = true;
    }

    /// <summary>Local-space safe bounds, also usable by detached Editor previews.</summary>
    public void ApplyLayoutFor(Rect available)
    {
        RectTransform root = transform as RectTransform;
        if (root == null || card == null || available.width < 100f || available.height < 100f) return;
        float margin = Mathf.Clamp(Mathf.Min(available.width, available.height) * .04f, 16f, 40f);
        float width = Mathf.Min(900f, available.width - margin * 2f);
        bool narrow = width < 720f;
        float targetHeight = narrow ? 620f : 480f;
        float height = Mathf.Min(targetHeight, available.height - margin * 2f);
        float unit = Mathf.Clamp(Mathf.Min(width / 900f, height / targetHeight), .55f, 1f);
        float padding = 36f * unit;
        float gap = 14f * unit;
        float buttonHeight = 64f * unit;
        float footerHeight = narrow ? buttonHeight * 2f + gap : buttonHeight;
        float toggleHeight = 54f * unit;
        float footerBottom = 28f * unit;
        float toggleBottom = footerBottom + footerHeight + 12f * unit;
        float titleHeight = (narrow ? 94f : 72f) * unit;
        float titleTop = 30f * unit;
        float messageTop = titleTop + titleHeight + 18f * unit;
        float messageBottom = toggleBottom + toggleHeight + 12f * unit;

        card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
        card.pivot = new Vector2(.5f, .5f);
        card.localScale = Vector3.one;
        card.localRotation = Quaternion.identity;
        card.sizeDelta = new Vector2(width, height);
        card.anchoredPosition = available.center - root.rect.center;
        Stretch(surface, 6f * unit, 6f * unit, 6f * unit, 6f * unit);
        Top(titleText != null ? titleText.rectTransform : null, padding, padding, titleTop, titleHeight);
        Top(divider, padding, padding, titleTop + titleHeight + 8f * unit, 2f * unit);
        Stretch(messageText != null ? messageText.rectTransform : null, padding, messageBottom, padding, messageTop);
        Text(titleText, 40f * unit, 34f * unit, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        Text(messageText, 28f * unit, 25f * unit, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        RectTransform cancel = cancelButton != null ? cancelButton.transform as RectTransform : null;
        RectTransform confirm = confirmButton != null ? confirmButton.transform as RectTransform : null;
        if (narrow)
        {
            Bottom(cancel, padding, padding, footerBottom, buttonHeight);
            Bottom(confirm, padding, padding, footerBottom + buttonHeight + gap, buttonHeight);
        }
        else
        {
            float buttonWidth = (width - padding * 2f - gap) * .5f;
            Bottom(cancel, padding, width - padding - buttonWidth, footerBottom, buttonHeight);
            Bottom(confirm, width - padding - buttonWidth, padding, footerBottom, buttonHeight);
        }
        StyleButtonLabel(cancelButton, 27f * unit);
        StyleButtonLabel(confirmButton, 27f * unit);

        RectTransform toggle = dontShowAgainToggle != null ? dontShowAgainToggle.transform as RectTransform : null;
        Bottom(toggle, padding, padding, toggleBottom, toggleHeight);
        float box = 36f * unit;
        Place(toggleBackground, new Vector2(0f, .5f), new Vector2(box * .5f, 0f), new Vector2(box, box));
        Stretch(toggleCheckmark, 6f * unit, 6f * unit, 6f * unit, 6f * unit);
        Stretch(toggleLabel != null ? toggleLabel.rectTransform : null, 50f * unit, 0f, 0f, 0f);
        Text(toggleLabel, 25f * unit, 22f * unit, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
    }

    private static void StyleButtonLabel(Button button, float size)
    {
        if (button == null) return;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        Stretch(label.rectTransform, 10f, 4f, 10f, 4f);
        Text(label, size, size, FontStyles.Bold, TextAlignmentOptions.Center);
        label.enableWordWrapping = false;
    }

    private static void Text(TMP_Text text, float size, float minimum, FontStyles style, TextAlignmentOptions alignment)
    {
        if (text == null) return;
        text.fontSize = size;
        text.enableAutoSizing = minimum < size;
        text.fontSizeMin = minimum;
        text.fontSizeMax = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.margin = Vector4.zero;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Top(RectTransform rect, float left, float right, float top, float height)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Bottom(RectTransform rect, float left, float right, float bottom, float height)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(.5f, 0f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, bottom + height);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
