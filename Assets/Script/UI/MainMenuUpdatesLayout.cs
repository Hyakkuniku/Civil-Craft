using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Fits the authored Updates controls without creating runtime UI.</summary>
[DisallowMultipleComponent]
public sealed class MainMenuUpdatesLayout : MonoBehaviour
{
    [HideInInspector] public int authoredVersion;
    public RectTransform entryButton;
    public RectTransform badgeRect;
    public RectTransform panelRect;
    public RectTransform card;
    public RectTransform surface;
    public RectTransform heading;
    public RectTransform releaseTitle;
    public RectTransform releaseDate;
    public RectTransform notesViewport;
    public RectTransform notesContent;
    public RectTransform scrollbar;
    public RectTransform installedVersion;
    public RectTransform availableVersion;
    public RectTransform status;
    public RectTransform retry;
    public RectTransform download;
    public RectTransform close;
    public ScrollRect notesScroll;
    public TMP_Text notesText;

    private Rect lastParentRect;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private bool applied;

    private void OnEnable() { applied = false; ApplyLayout(); }

    private void LateUpdate()
    {
        if (entryButton == null || !(entryButton.parent is RectTransform parent)) return;
        if (!applied || lastParentRect != parent.rect || lastSafeArea != Screen.safeArea ||
            lastScreen.x != Screen.width || lastScreen.y != Screen.height) ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (entryButton == null || !(entryButton.parent is RectTransform parent)) return;
        Rect available = Available(parent);
        if (available.width < 100f || available.height < 100f) return;

        // These are Canvas units, not raw pixels; inherit the existing scaler.
        float entryUnit = Mathf.Clamp(Mathf.Min(available.width / 1600f, available.height / 900f), .55f, 1f);
        Vector2 entrySize = new Vector2(270f, 80f) * entryUnit;
        Place(entryButton, new Vector2(.5f, .5f), available.max - parent.rect.center -
            new Vector2(28f * entryUnit + entrySize.x * .5f, available.height - 28f * entryUnit - entrySize.y * .5f), entrySize);
        Text(entryButton.GetComponentInChildren<TMP_Text>(true), Mathf.Max(20f, 30f * entryUnit), true, TextAlignmentOptions.Center);
        Place(badgeRect, new Vector2(.5f, 1f), new Vector2(0f, 24f * entryUnit), new Vector2(232f, 44f) * entryUnit);
        if (badgeRect != null) Text(badgeRect.GetComponentInChildren<TMP_Text>(true), Mathf.Max(16f, 22f * entryUnit), true, TextAlignmentOptions.Center);

        if (panelRect != null && card != null && card.parent is RectTransform panelParent)
        {
            Rect modalArea = Available(panelParent);
            float margin = Mathf.Min(32f, Mathf.Min(modalArea.width, modalArea.height) * .04f);
            Vector2 size = new Vector2(Mathf.Min(1440f, modalArea.width - margin * 2f),
                Mathf.Min(900f, modalArea.height - margin * 2f));
            float u = Mathf.Clamp(Mathf.Min(size.x / 1440f, size.y / 900f), .38f, 1f);
            Place(card, Vector2.one * .5f, modalArea.center - panelParent.rect.center, size);
            Stretch(surface, 6f, 6f, 6f, 6f);
            Top(heading, 40f * u, 220f * u, 22f * u, 46f * u);
            Text(heading != null ? heading.GetComponent<TMP_Text>() : null, Mathf.Max(22f, 38f * u), true, TextAlignmentOptions.MidlineLeft);
            Place(close, Vector2.one, new Vector2(-110f * u, -48f * u), new Vector2(170f, 60f) * u);
            StyleButton(close, u);
            Top(releaseTitle, 40f * u, 40f * u, 90f * u, 76f * u);
            TMP_Text title = releaseTitle != null ? releaseTitle.GetComponent<TMP_Text>() : null;
            Text(title, Mathf.Max(21f, 34f * u), true, TextAlignmentOptions.TopLeft);
            if (title != null)
            {
                title.enableWordWrapping = true;
                title.enableAutoSizing = true;
                title.fontSizeMax = Mathf.Max(21f, 34f * u);
                title.fontSizeMin = Mathf.Max(18f, 25f * u);
                title.overflowMode = TextOverflowModes.Ellipsis;
            }
            Top(releaseDate, 40f * u, 40f * u, 171f * u, 34f * u);
            Text(releaseDate != null ? releaseDate.GetComponent<TMP_Text>() : null, Mathf.Max(18f, 25f * u), false, TextAlignmentOptions.MidlineLeft);
            FractionTop(installedVersion, .028f, .49f, 209f * u, 46f * u);
            FractionTop(availableVersion, .51f, .972f, 209f * u, 46f * u);
            Text(installedVersion != null ? installedVersion.GetComponent<TMP_Text>() : null, Mathf.Max(17f, 25f * u), false, TextAlignmentOptions.MidlineLeft);
            Text(availableVersion != null ? availableVersion.GetComponent<TMP_Text>() : null, Mathf.Max(17f, 25f * u), false, TextAlignmentOptions.MidlineRight);
            Stretch(notesViewport, 40f * u, 204f * u, 76f * u, 270f * u);
            if (scrollbar != null)
            {
                scrollbar.anchorMin = new Vector2(1f, 0f); scrollbar.anchorMax = Vector2.one;
                scrollbar.offsetMin = new Vector2(-61f * u, 208f * u);
                scrollbar.offsetMax = new Vector2(-43f * u, -274f * u);
            }
            if (notesContent != null)
            {
                VerticalLayoutGroup group = notesContent.GetComponent<VerticalLayoutGroup>();
                if (group != null)
                {
                    int padding = Mathf.RoundToInt(18f * u);
                    group.padding = new RectOffset(padding, padding, padding, padding);
                }
            }
            Text(notesText, Mathf.Max(18f, 30f * u), false, TextAlignmentOptions.TopLeft);
            if (notesText != null)
            {
                notesText.richText = false;
                notesText.enableWordWrapping = true;
                notesText.overflowMode = TextOverflowModes.Overflow;
            }
            Bottom(status, 40f * u, 40f * u, 115f * u, 72f * u);
            Text(status != null ? status.GetComponent<TMP_Text>() : null, Mathf.Max(17f, 25f * u), false, TextAlignmentOptions.TopLeft);
            if (status != null) status.GetComponent<TMP_Text>().enableWordWrapping = true;
            FractionBottom(retry, .028f, .34f, 32f * u, 64f * u);
            FractionBottom(download, .36f, .972f, 32f * u, 64f * u);
            StyleButton(retry, u); StyleButton(download, u);
            RefreshNotesLayout();
        }
        lastParentRect = parent.rect;
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        applied = true;
    }

    public void RefreshNotesLayout()
    {
        if (notesText != null) notesText.SetAllDirty();
        if (notesContent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(notesContent);
    }

    private static Rect Available(RectTransform parent)
    {
        Rect available = parent.rect;
        Canvas canvas = parent.GetComponentInParent<Canvas>(true);
        if (!Application.isPlaying || canvas == null || Screen.width <= 0 || Screen.height <= 0) return available;
        canvas = canvas.rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.min, camera, out Vector2 min) &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.max, camera, out Vector2 max))
        {
            min = Vector2.Max(min, available.min); max = Vector2.Min(max, available.max);
            if (max.x > min.x && max.y > min.y) available = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        return available;
    }

    private static void StyleButton(RectTransform rect, float unit)
    {
        if (rect == null) return;
        Text(rect.GetComponentInChildren<TMP_Text>(true), Mathf.Max(18f, 28f * unit), true, TextAlignmentOptions.Center);
        RectTransform face = rect.Find("Button Face") as RectTransform;
        Stretch(face, 4f, 4f, 4f, 4f);
        TMP_Text label = rect.GetComponentInChildren<TMP_Text>(true);
        if (label != null) Stretch(label.rectTransform, 8f, 4f, 8f, 4f);
    }

    private static void Text(TMP_Text text, float size, bool bold, TextAlignmentOptions alignment)
    {
        if (text == null) return;
        text.fontSize = size; text.enableAutoSizing = false;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = alignment; text.richText = false; text.raycastTarget = false;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
    }
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
    }
    private static void Top(RectTransform rect, float left, float right, float top, float height)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(-right, -top);
    }
    private static void Bottom(RectTransform rect, float left, float right, float bottom, float height)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1f, 0f);
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, bottom + height);
    }
    private static void FractionTop(RectTransform rect, float left, float right, float top, float height)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(left, 1f); rect.anchorMax = new Vector2(right, 1f);
        rect.offsetMin = new Vector2(0f, -top - height); rect.offsetMax = new Vector2(0f, -top);
    }
    private static void FractionBottom(RectTransform rect, float left, float right, float bottom, float height)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(left, 0f); rect.anchorMax = new Vector2(right, 0f);
        rect.offsetMin = new Vector2(0f, bottom); rect.offsetMax = new Vector2(0f, bottom + height);
    }
}
