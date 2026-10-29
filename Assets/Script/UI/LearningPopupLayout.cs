using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Fits an authored lesson/material reader without generating UI at runtime.</summary>
[DisallowMultipleComponent]
public sealed class LearningPopupLayout : MonoBehaviour
{
    [HideInInspector] public int authoredVersion;
    public RectTransform card;
    public RectTransform cardSurface;
    public RectTransform titleTab;
    public TMP_Text titleText;
    public TMP_Text contextText;
    public RectTransform headerDivider;
    public Button dismissButton;
    public RectTransform imageFrame;
    public RectTransform imageSurface;
    public Image contentImage;
    public RectTransform descriptionFrame;
    public RectTransform descriptionSurface;
    public ScrollRect descriptionScroll;
    public TMP_Text descriptionText;

    private Rect lastParentRect;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private bool applied;

    private void OnEnable() { applied = false; RefreshContent(); }

    private void LateUpdate()
    {
        if (card == null || !(card.parent is RectTransform parent)) return;
        if (!applied || parent.rect != lastParentRect || Screen.safeArea != lastSafeArea ||
            lastScreen.x != Screen.width || lastScreen.y != Screen.height) ApplyLayout();
    }

    public void SetContent(string title, Sprite image, string description)
    {
        if (titleText != null) titleText.text = title ?? string.Empty;
        if (descriptionText != null) descriptionText.text = description ?? string.Empty;
        if (contentImage != null)
        {
            contentImage.sprite = image;
            contentImage.enabled = image != null;
            contentImage.gameObject.SetActive(image != null);
            contentImage.preserveAspect = true;
        }
        RefreshContent();
    }

    public void RefreshContent()
    {
        ApplyLayout();
        if (descriptionScroll == null) return;
        descriptionScroll.StopMovement();
        descriptionScroll.verticalNormalizedPosition = 1f;
    }

    public void ApplyLayout()
    {
        if (card == null || !(card.parent is RectTransform parent)) return;
        Rect available = parent.rect;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (Application.isPlaying && canvas != null && Screen.width > 0 && Screen.height > 0)
        {
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.min, camera, out Vector2 min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.max, camera, out Vector2 max))
            {
                min = Vector2.Max(min, available.min);
                max = Vector2.Min(max, available.max);
                if (max.x > min.x && max.y > min.y) available = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
        if (available.width < 160f || available.height < 160f) return;

        float availableScale = Mathf.Min(1f, Mathf.Min(available.width / 1440f, available.height / 920f));
        float outerMargin = Mathf.Max(12f, 32f * availableScale);
        Vector2 size = new Vector2(Mathf.Min(1440f, available.width - outerMargin * 2f),
            Mathf.Min(920f, available.height - outerMargin * 2f));
        float u = Mathf.Min(1f, Mathf.Min(size.x / 1440f, size.y / 920f));
        // The CanvasScaler can make reference-sized controls too small on a
        // short phone display. Reserve real touch/text sizes inside the same
        // authored card; the body scrolls rather than shrinking to illegibility.
        Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
        float pixelScale = rootCanvas != null ? Mathf.Max(.01f, rootCanvas.scaleFactor) : 1f;
        float padding = Mathf.Max(12f, 28f * u), gap = Mathf.Max(12f, 24f * u);
        float contentWidth = Mathf.Max(32f, size.x - padding * 2f);
        float closeHeight = Mathf.Max(80f * u, 44f / pixelScale);
        float closeWidth = contentWidth * .80f;
        float contextFont = Mathf.Max(20f * u, 14f / pixelScale);
        float contextHeight = contextText != null ? contextFont * 1.5f : 0f;
        float titleFont = Mathf.Max(46f * u, 18f / pixelScale);
        float titleHeight = Mathf.Max(76f * u, titleFont * 2.3f);
        float titleTop = padding + contextHeight + (contextText != null ? gap * .4f : 0f);
        float dividerTop = titleTop + titleHeight + gap * .35f;
        Place(card, new Vector2(.5f, .5f), available.center - parent.rect.center, size);
        CozyModalLayout.Stretch(cardSurface, 8f * u, 8f * u, 8f * u, 8f * u);
        if (contextText != null)
        {
            Place(contextText.rectTransform, new Vector2(.5f, 1f),
                new Vector2(0f, -padding - contextHeight * .5f), new Vector2(contentWidth, contextHeight));
            contextText.fontStyle = FontStyles.Normal;
            contextText.enableAutoSizing = false;
            contextText.fontSize = contextFont;
            contextText.characterSpacing = 1f;
            contextText.enableWordWrapping = false;
            contextText.overflowMode = TextOverflowModes.Overflow;
            contextText.alignment = TextAlignmentOptions.Center;
        }
        Place(titleTab, new Vector2(.5f, 1f), new Vector2(0f,
            -titleTop - titleHeight * .5f), new Vector2(contentWidth, titleHeight));
        if (titleText != null)
        {
            CozyModalLayout.Stretch(titleText.rectTransform, 0f, 0f, 0f, 0f);
            titleText.enableAutoSizing = true;
            titleText.fontSize = titleText.fontSizeMax = titleFont;
            titleText.fontSizeMin = Mathf.Min(titleFont, Mathf.Max(32f * u, 18f / pixelScale));
            titleText.fontStyle = FontStyles.Normal;
            titleText.characterSpacing = 0f;
            titleText.enableWordWrapping = true;
            titleText.overflowMode = TextOverflowModes.Overflow;
            titleText.alignment = TextAlignmentOptions.Center;
        }
        Place(headerDivider, new Vector2(.5f, 1f), new Vector2(0f, -dividerTop),
            new Vector2(contentWidth * .90f, Mathf.Max(1f, 2f * u)));
        if (dismissButton != null)
        {
            Place(dismissButton.transform as RectTransform, new Vector2(.5f, 0f),
                new Vector2(0f, padding + closeHeight * .5f),
                new Vector2(closeWidth, closeHeight));
            TMP_Text symbol = dismissButton.GetComponentInChildren<TMP_Text>(true);
            if (symbol != null)
            {
                symbol.enableAutoSizing = true;
                symbol.fontSize = symbol.fontSizeMax = Mathf.Max(30f * u, 16f / pixelScale);
                symbol.fontSizeMin = Mathf.Min(symbol.fontSizeMax, Mathf.Max(22f * u, 16f / pixelScale));
                symbol.fontStyle = FontStyles.Normal;
                symbol.enableWordWrapping = false;
                symbol.overflowMode = TextOverflowModes.Overflow;
                symbol.alignment = TextAlignmentOptions.Center;
                CozyModalLayout.Stretch(symbol.rectTransform, 12f * u, 5f * u, 12f * u, 8f * u);
            }
        }

        bool hasImage = contentImage != null && contentImage.sprite != null;
        if (imageFrame != null) imageFrame.gameObject.SetActive(hasImage);
        float contentTop = dividerTop + gap;
        float footerHeight = padding + closeHeight + gap;
        float contentHeight = Mathf.Max(32f, size.y - contentTop - footerHeight);
        bool stacked = available.width < available.height;
        float imageWidth = hasImage && !stacked ? contentWidth * .40f : contentWidth;
        float imageHeight = hasImage && stacked ? contentHeight * .36f : contentHeight;
        float descriptionWidth = hasImage && !stacked ? contentWidth - imageWidth - gap : contentWidth;
        float descriptionHeight = hasImage && stacked ? contentHeight - imageHeight - gap : contentHeight;
        float descriptionLeft = padding + (hasImage && !stacked ? imageWidth + gap : 0f);
        float descriptionTop = contentTop + (hasImage && stacked ? imageHeight + gap : 0f);
        Place(descriptionFrame, new Vector2(0f, 1f),
            new Vector2(descriptionLeft + descriptionWidth * .5f, -descriptionTop - descriptionHeight * .5f),
            new Vector2(descriptionWidth, descriptionHeight));
        CozyModalLayout.Stretch(descriptionSurface, 2f * u, 2f * u, 2f * u, 2f * u);
        if (hasImage)
        {
            Place(imageFrame, new Vector2(0f, 1f),
                new Vector2(padding + imageWidth * .5f, -contentTop - imageHeight * .5f),
                new Vector2(imageWidth, imageHeight));
            CozyModalLayout.Stretch(imageSurface, 2f * u, 2f * u, 2f * u, 2f * u);
            CozyModalLayout.Stretch(contentImage.rectTransform, 18f * u, 18f * u, 18f * u, 18f * u);
            contentImage.preserveAspect = true;
        }
        if (descriptionScroll != null)
        {
            float innerPadding = Mathf.Max(10f, 24f * u);
            CozyModalLayout.Stretch(descriptionScroll.transform as RectTransform, innerPadding, innerPadding, innerPadding, innerPadding);
            float scrollbarWidth = Mathf.Clamp(6f * u, 4f / pixelScale, 12f / pixelScale);
            if (descriptionScroll.verticalScrollbar != null)
            {
                Scrollbar scrollbar = descriptionScroll.verticalScrollbar;
                RectTransform rail = scrollbar.transform as RectTransform;
                if (rail != null)
                {
                    rail.anchorMin = new Vector2(1f, 0f); rail.anchorMax = Vector2.one;
                    rail.pivot = new Vector2(1f, .5f); rail.anchoredPosition = Vector2.zero;
                    rail.sizeDelta = new Vector2(scrollbarWidth, 0f); rail.localScale = Vector3.one;
                }
                RectTransform handle = scrollbar.handleRect;
                if (handle != null)
                {
                    // Scrollbar owns its vertical anchors/position. Fit only
                    // the width so an old 100-unit authored handle cannot cover
                    // the reading column when the rail becomes narrow.
                    handle.anchorMin = new Vector2(0f, handle.anchorMin.y);
                    handle.anchorMax = new Vector2(1f, handle.anchorMax.y);
                    handle.offsetMin = new Vector2(0f, handle.offsetMin.y);
                    handle.offsetMax = new Vector2(0f, handle.offsetMax.y);
                }
            }
            CozyModalLayout.Stretch(descriptionScroll.viewport, 0f, 0f,
                Mathf.Max(Mathf.Max(10f, 14f * u), scrollbarWidth + 6f * u), 0f);
            RectTransform content = descriptionScroll.content;
            if (descriptionText != null && content != null && descriptionScroll.viewport != null)
            {
                descriptionText.enableAutoSizing = false;
                descriptionText.fontSize = Mathf.Max(20f, Mathf.Max(32f * u, 14f / pixelScale));
                descriptionText.lineSpacing = 3f * u;
                descriptionText.paragraphSpacing = 12f * u;
                descriptionText.enableWordWrapping = true;
                descriptionText.overflowMode = TextOverflowModes.Overflow;
                descriptionText.alignment = TextAlignmentOptions.TopLeft;
                descriptionText.margin = Vector4.zero;
                float width = Mathf.Max(1f, descriptionScroll.viewport.rect.width);
                float height = Mathf.Max(descriptionScroll.viewport.rect.height,
                    descriptionText.GetPreferredValues(descriptionText.text, width, Mathf.Infinity).y + 12f * u);
                Vector2 previousPosition = content.anchoredPosition;
                content.anchorMin = content.anchorMax = new Vector2(.5f, 1f);
                content.pivot = new Vector2(.5f, 1f);
                content.localScale = Vector3.one;
                content.sizeDelta = new Vector2(width, height);
                content.anchoredPosition = new Vector2(0f, Mathf.Clamp(previousPosition.y, 0f,
                    Mathf.Max(0f, height - descriptionScroll.viewport.rect.height)));
                CozyModalLayout.Stretch(descriptionText.rectTransform, 0f, 0f, 0f, 0f);
                if (descriptionScroll.verticalScrollbar != null)
                    descriptionScroll.verticalScrollbar.gameObject.SetActive(height > descriptionScroll.viewport.rect.height + 1f);
            }
        }
        lastParentRect = parent.rect;
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        authoredVersion = 3;
        applied = true;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.localScale = Vector3.one;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
