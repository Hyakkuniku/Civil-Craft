using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Fits an authored lesson/material reader without generating UI at runtime.</summary>
[DisallowMultipleComponent]
public sealed class LearningPopupLayout : MonoBehaviour
{
    [HideInInspector] public int authoredVersion;
    [Tooltip("Use the centered Name Registration composition for material introductions.")]
    public bool useRegistrationStyle;
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
                min = Vector2.Max(min, available.min); max = Vector2.Min(max, available.max);
                if (max.x > min.x && max.y > min.y) available = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
        if (available.width < 160f || available.height < 160f) return;

        if (useRegistrationStyle)
        {
            ApplyRegistrationLayout(parent, available, canvas);
            return;
        }

        Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
        float pixelScale = rootCanvas != null ? Mathf.Max(.01f, rootCanvas.scaleFactor) : 1f;
        float outerMargin = Mathf.Max(12f, 28f * Mathf.Min(1f, available.height / 720f));
        // Compact on large screens, but use the phone's width instead of
        // shrinking a 1280-unit card to an unreadably narrow physical panel.
        float maxWidth = Mathf.Max(1280f, 720f / pixelScale);
        float width = Mathf.Min(maxWidth, available.width - outerMargin * 2f);
        float maxHeight = Mathf.Min(720f, available.height - outerMargin * 2f);
        float u = Mathf.Min(1f, width / 1280f);
        float padding = Mathf.Max(20f * u, 10f / pixelScale);
        float gap = Mathf.Max(24f * u, 12f / pixelScale);
        float smallGap = Mathf.Max(10f * u, 5f / pixelScale);
        float contentWidth = Mathf.Max(32f, width - padding * 2f);
        bool hasImage = contentImage != null && contentImage.sprite != null;
        bool stacked = available.width < available.height;
        float stageWidth = hasImage && !stacked ? contentWidth * .35f : contentWidth;
        float copyWidth = hasImage && !stacked ? contentWidth - stageWidth - gap : contentWidth;
        float copyLeft = padding + (hasImage && !stacked ? stageWidth + gap : 0f);
        float buttonHeight = Mathf.Max(64f * u, 44f / pixelScale);
        float buttonWidth = Mathf.Min(copyWidth, Mathf.Max(280f * u, 140f / pixelScale));
        float bodyFont = Mathf.Max(30f * u, 14f / pixelScale);
        float titleFont = Mathf.Max(42f * u, 20f / pixelScale);
        float contextFont = Mathf.Max(17f * u, 12f / pixelScale);
        float contextHeight = 0f;
        if (contextText != null)
        {
            SetReadingText(contextText, contextFont, TextAlignmentOptions.MidlineLeft);
            contextText.characterSpacing = 1.5f;
            contextHeight = PreferredHeight(contextText, copyWidth, contextFont);
        }
        if (titleText != null) SetReadingText(titleText, titleFont, TextAlignmentOptions.MidlineLeft);
        float titleHeight = PreferredHeight(titleText, copyWidth, titleFont);
        float accentHeight = Mathf.Max(2f * u, 1f / pixelScale);
        float headerHeight = (contextText != null ? contextHeight + smallGap : 0f) + titleHeight + smallGap + accentHeight + gap;
        float scrollbarWidth = Mathf.Clamp(6f * u, 4f / pixelScale, 12f / pixelScale);
        float readingWidth = Mathf.Max(1f, copyWidth - scrollbarWidth - 8f * u);
        if (descriptionText != null)
        {
            SetReadingText(descriptionText, bodyFont, TextAlignmentOptions.TopLeft);
            descriptionText.lineSpacing = 2f * u; descriptionText.paragraphSpacing = 10f * u;
        }
        float preferredBodyHeight = PreferredHeight(descriptionText, readingWidth, bodyFont);
        float stageHeight = hasImage && stacked ? Mathf.Min(contentWidth * .50f, 220f * u) : 0f;
        float minimumBodyHeight = bodyFont * 1.8f;
        // Preserve reading space even in a short portrait viewport.
        if (hasImage && stacked)
            stageHeight = Mathf.Min(stageHeight, Mathf.Max(0f, maxHeight - padding * 2f - headerHeight - buttonHeight - gap * 2f - minimumBodyHeight));
        float stackedStage = hasImage && stacked ? stageHeight + gap : 0f;
        float desiredHeight = padding * 2f + stackedStage + headerHeight + preferredBodyHeight + gap + buttonHeight;
        float minimumHeight = Mathf.Min(maxHeight, (hasImage ? 440f : 320f) * u);
        float height = Mathf.Clamp(desiredHeight, minimumHeight, maxHeight);
        float contentHeight = Mathf.Max(1f, height - padding * 2f);
        float copyAreaHeight = Mathf.Max(1f, contentHeight - stackedStage);
        float bodyHeight = Mathf.Min(preferredBodyHeight, Mathf.Max(1f, copyAreaHeight - headerHeight - gap - buttonHeight));
        float copyBlockHeight = headerHeight + bodyHeight + gap + buttonHeight;
        // Short notes are balanced around their illustration. Long references
        // fill the available column and keep their complete text scrollable.
        float cursor = padding + stackedStage + Mathf.Max(0f, (copyAreaHeight - copyBlockHeight) * .5f);
        Place(card, new Vector2(.5f, .5f), available.center - parent.rect.center, new Vector2(width, height));
        CozyModalLayout.Stretch(cardSurface, 8f * u, 8f * u, 8f * u, 8f * u);
        if (contextText != null)
        {
            Place(contextText.rectTransform, new Vector2(0f, 1f), new Vector2(copyLeft + copyWidth * .5f,
                -cursor - contextHeight * .5f), new Vector2(copyWidth, contextHeight));
            cursor += contextHeight + smallGap;
        }
        Place(titleTab, new Vector2(0f, 1f), new Vector2(copyLeft + copyWidth * .5f,
            -cursor - titleHeight * .5f), new Vector2(copyWidth, titleHeight));
        if (titleText != null) CozyModalLayout.Stretch(titleText.rectTransform, 0f, 0f, 0f, 0f);
        cursor += titleHeight + smallGap;
        float accentWidth = Mathf.Min(copyWidth, 76f * u);
        Place(headerDivider, new Vector2(0f, 1f), new Vector2(copyLeft + accentWidth * .5f,
            -cursor - accentHeight * .5f), new Vector2(accentWidth, accentHeight));
        cursor += accentHeight + gap;
        Place(descriptionFrame, new Vector2(0f, 1f), new Vector2(copyLeft + copyWidth * .5f,
            -cursor - bodyHeight * .5f), new Vector2(copyWidth, bodyHeight));
        CozyModalLayout.Stretch(descriptionSurface, 0f, 0f, 0f, 0f);
        cursor += bodyHeight + gap;
        if (dismissButton != null)
        {
            Place(dismissButton.transform as RectTransform, new Vector2(0f, 1f),
                new Vector2(copyLeft + buttonWidth * .5f, -cursor - buttonHeight * .5f),
                new Vector2(buttonWidth, buttonHeight));
            TMP_Text label = dismissButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                SetReadingText(label, Mathf.Max(26f * u, 14f / pixelScale), TextAlignmentOptions.Center);
                label.enableWordWrapping = false;
                CozyModalLayout.Stretch(label.rectTransform, 12f * u, 5f * u, 12f * u, 8f * u);
            }
        }
        if (imageFrame != null) imageFrame.gameObject.SetActive(hasImage);
        if (hasImage)
        {
            float pictureHeight = stacked ? stageHeight : contentHeight;
            Place(imageFrame, new Vector2(0f, 1f), new Vector2(padding + stageWidth * .5f,
                -padding - pictureHeight * .5f), new Vector2(stageWidth, pictureHeight));
            CozyModalLayout.Stretch(imageSurface, 4f * u, 4f * u, 4f * u, 4f * u);
            CozyModalLayout.Stretch(contentImage.rectTransform, stageWidth * .10f, pictureHeight * .10f,
                stageWidth * .10f, pictureHeight * .10f);
            contentImage.preserveAspect = true;
        }
        LayoutReadingScroll(u, scrollbarWidth);
        lastParentRect = parent.rect; lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        authoredVersion = 4; applied = true;
    }

    private void ApplyRegistrationLayout(RectTransform parent, Rect available, Canvas canvas)
    {
        Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
        float pixelScale = rootCanvas != null ? Mathf.Max(.01f, rootCanvas.scaleFactor) : 1f;
        float margin = Mathf.Max(12f, 28f * Mathf.Min(1f, available.height / 720f));
        float width = Mathf.Min(Mathf.Max(1000f, 650f / pixelScale), available.width - margin * 2f);
        float u = Mathf.Min(1f, width / 1000f);
        float padding = Mathf.Max(64f * u, 16f / pixelScale);
        float gap = Mathf.Max(24f * u, 10f / pixelScale);
        float smallGap = Mathf.Max(12f * u, 5f / pixelScale);
        float innerWidth = width - padding * 2f;
        float maxHeight = Mathf.Min(780f, available.height - margin * 2f);
        bool hasImage = contentImage != null && contentImage.sprite != null;
        bool stacked = available.width < available.height;
        float titleFont = Mathf.Max(46f * u, 20f / pixelScale);
        float bodyFont = Mathf.Max(30f * u, 14f / pixelScale);
        float contextFont = Mathf.Max(20f * u, 12f / pixelScale);
        SetReadingText(contextText, contextFont, TextAlignmentOptions.Center);
        if (contextText != null) contextText.characterSpacing = 1f;
        SetReadingText(titleText, titleFont, TextAlignmentOptions.Center);
        float contextHeight = contextText != null ? PreferredHeight(contextText, innerWidth, contextFont) : 0f;
        float titleHeight = PreferredHeight(titleText, innerWidth, titleFont);
        float ruleHeight = Mathf.Max(2f * u, 1f / pixelScale);
        float headerHeight = contextHeight + smallGap + titleHeight + gap + ruleHeight + gap;
        float buttonHeight = Mathf.Max(76f * u, 44f / pixelScale);
        float imageWidth = hasImage && !stacked ? innerWidth * .31f : innerWidth;
        float copyWidth = hasImage && !stacked ? innerWidth - imageWidth - gap : innerWidth;
        float scrollbarWidth = Mathf.Clamp(6f * u, 4f / pixelScale, 12f / pixelScale);
        SetReadingText(descriptionText, bodyFont, TextAlignmentOptions.TopLeft);
        if (descriptionText != null) { descriptionText.lineSpacing = 2f * u; descriptionText.paragraphSpacing = 10f * u; }
        float preferred = PreferredHeight(descriptionText, copyWidth - scrollbarWidth - 8f * u, bodyFont);
        float imageHeight = hasImage ? 240f * u : 0f;
        if (stacked) imageHeight = Mathf.Min(imageHeight, Mathf.Max(0f,
            maxHeight - padding * 2f - headerHeight - buttonHeight - gap * 2f - bodyFont * 2f));
        float stackedImage = hasImage && stacked ? imageHeight + gap : 0f;
        float desiredMiddle = stackedImage + Mathf.Max(preferred, hasImage && !stacked ? imageHeight : 0f);
        float height = Mathf.Clamp(padding * 2f + headerHeight + desiredMiddle + gap + buttonHeight,
            Mathf.Min(maxHeight, 600f * u), maxHeight);
        float middleHeight = Mathf.Max(1f, height - padding * 2f - headerHeight - gap - buttonHeight);
        Place(card, new Vector2(.5f, .5f), available.center - parent.rect.center, new Vector2(width, height));
        CozyModalLayout.Stretch(cardSurface, 8f * u, 8f * u, 8f * u, 8f * u);
        float cursor = padding;
        if (contextText != null)
            Place(contextText.rectTransform, new Vector2(.5f, 1f), new Vector2(0, -cursor - contextHeight * .5f), new Vector2(innerWidth, contextHeight));
        cursor += contextHeight + smallGap;
        Place(titleTab, new Vector2(.5f, 1f), new Vector2(0, -cursor - titleHeight * .5f), new Vector2(innerWidth, titleHeight));
        if (titleText != null) CozyModalLayout.Stretch(titleText.rectTransform, 0, 0, 0, 0);
        cursor += titleHeight + gap;
        Place(headerDivider, new Vector2(.5f, 1f), new Vector2(0, -cursor - ruleHeight * .5f), new Vector2(innerWidth, ruleHeight));
        cursor += ruleHeight + gap;
        float bodyHeight = Mathf.Max(1f, middleHeight - stackedImage);
        float bodyLeft = padding + (hasImage && !stacked ? imageWidth + gap : 0f);
        Place(descriptionFrame, new Vector2(0, 1), new Vector2(bodyLeft + copyWidth * .5f,
            -cursor - stackedImage - bodyHeight * .5f), new Vector2(copyWidth, bodyHeight));
        CozyModalLayout.Stretch(descriptionSurface, 0, 0, 0, 0);
        if (imageFrame != null) imageFrame.gameObject.SetActive(hasImage);
        if (hasImage)
        {
            float stageHeight = stacked ? imageHeight : middleHeight;
            Place(imageFrame, new Vector2(0, 1), new Vector2(padding + imageWidth * .5f,
                -cursor - stageHeight * .5f), new Vector2(imageWidth, stageHeight));
            CozyModalLayout.Stretch(imageSurface, 4f * u, 4f * u, 4f * u, 4f * u);
            CozyModalLayout.Stretch(contentImage.rectTransform, imageWidth * .10f, stageHeight * .10f, imageWidth * .10f, stageHeight * .10f);
            contentImage.preserveAspect = true;
        }
        if (dismissButton != null)
        {
            Place(dismissButton.transform as RectTransform, new Vector2(.5f, 0), new Vector2(0, padding + buttonHeight * .5f), new Vector2(innerWidth, buttonHeight));
            TMP_Text label = dismissButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                SetReadingText(label, Mathf.Max(30f * u, 14f / pixelScale), TextAlignmentOptions.Center);
                label.enableWordWrapping = false;
                CozyModalLayout.Stretch(label.rectTransform, 12f * u, 5f * u, 12f * u, 8f * u);
            }
        }
        LayoutReadingScroll(u, scrollbarWidth);
        lastParentRect = parent.rect; lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        authoredVersion = 5; applied = true;
    }

    private static void SetReadingText(TMP_Text text, float size, TextAlignmentOptions alignment)
    {
        if (text == null) return;
        text.fontSize = size; text.enableAutoSizing = false; text.fontStyle = FontStyles.Normal;
        text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = alignment; text.margin = Vector4.zero;
    }

    private static float PreferredHeight(TMP_Text text, float width, float fallbackSize)
    {
        if (text == null) return fallbackSize * 1.5f;
        return Mathf.Max(fallbackSize * 1.5f, text.GetPreferredValues(text.text, Mathf.Max(1f, width), Mathf.Infinity).y + fallbackSize * .18f);
    }

    private void LayoutReadingScroll(float u, float scrollbarWidth)
    {
        if (descriptionScroll == null) return;
        CozyModalLayout.Stretch(descriptionScroll.transform as RectTransform, 0f, 0f, 0f, 0f);
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
                // Retain the vertical range controlled by Scrollbar itself.
                handle.anchorMin = new Vector2(0f, handle.anchorMin.y);
                handle.anchorMax = new Vector2(1f, handle.anchorMax.y);
                handle.offsetMin = new Vector2(0f, handle.offsetMin.y);
                handle.offsetMax = new Vector2(0f, handle.offsetMax.y);
            }
        }
        CozyModalLayout.Stretch(descriptionScroll.viewport, 0f, 0f, scrollbarWidth + 8f * u, 0f);
        RectTransform content = descriptionScroll.content;
        if (descriptionText == null || content == null || descriptionScroll.viewport == null) return;
        float width = Mathf.Max(1f, descriptionScroll.viewport.rect.width);
        float height = Mathf.Max(descriptionScroll.viewport.rect.height,
            descriptionText.GetPreferredValues(descriptionText.text, width, Mathf.Infinity).y + descriptionText.fontSize * .18f);
        Vector2 previousPosition = content.anchoredPosition;
        content.anchorMin = content.anchorMax = new Vector2(.5f, 1f); content.pivot = new Vector2(.5f, 1f);
        content.localScale = Vector3.one; content.sizeDelta = new Vector2(width, height);
        content.anchoredPosition = new Vector2(0f, Mathf.Clamp(previousPosition.y, 0f,
            Mathf.Max(0f, height - descriptionScroll.viewport.rect.height)));
        CozyModalLayout.Stretch(descriptionText.rectTransform, 0f, 0f, 0f, 0f);
        if (descriptionScroll.verticalScrollbar != null)
            descriptionScroll.verticalScrollbar.gameObject.SetActive(height > descriptionScroll.viewport.rect.height + 1f);
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
