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

        Vector2 size = new Vector2(Mathf.Min(1600f, available.width - 112f),
            Mathf.Min(880f, available.height - 144f));
        float u = Mathf.Min(1f, Mathf.Min(size.x / 1600f, size.y / 880f));
        Place(card, new Vector2(.5f, .5f), available.center - parent.rect.center + new Vector2(0f, -12f * u), size);
        CozyModalLayout.Stretch(cardSurface, 9f * u, 9f * u, 9f * u, 9f * u);
        Place(titleTab, new Vector2(.5f, 1f), new Vector2(0f, 10f * u), new Vector2(900f, 176f) * u);
        if (titleText != null)
        {
            CozyModalLayout.Stretch(titleText.rectTransform, 116f * u, 38f * u, 116f * u, 42f * u);
            titleText.enableAutoSizing = true;
            titleText.fontSize = titleText.fontSizeMax = 58f * u;
            titleText.fontSizeMin = 34f * u;
            titleText.enableWordWrapping = true;
        }
        if (dismissButton != null)
        {
            Place(dismissButton.transform as RectTransform, Vector2.one, new Vector2(-10f, -4f) * u,
                new Vector2(104f, 104f) * u);
            TMP_Text symbol = dismissButton.GetComponentInChildren<TMP_Text>(true);
            if (symbol != null)
            {
                symbol.enableAutoSizing = false;
                symbol.fontSize = 66f * u;
                CozyModalLayout.Stretch(symbol.rectTransform, 8f * u, 8f * u, 8f * u, 12f * u);
            }
        }

        bool hasImage = contentImage != null && contentImage.sprite != null;
        if (imageFrame != null) imageFrame.gameObject.SetActive(hasImage);
        float descriptionHeight = hasImage ? size.y * .31f : size.y - 148f * u;
        float bottom = 32f * u;
        Place(descriptionFrame, new Vector2(.5f, 0f),
            new Vector2(0f, bottom + descriptionHeight * .5f),
            new Vector2(size.x - 88f * u, descriptionHeight));
        CozyModalLayout.Stretch(descriptionSurface, 6f * u, 6f * u, 6f * u, 6f * u);
        if (hasImage)
        {
            float imageTop = 116f * u;
            float imageBottom = bottom + descriptionHeight + 22f * u;
            float imageHeight = Mathf.Max(48f, size.y - imageTop - imageBottom);
            Place(imageFrame, new Vector2(.5f, 1f), new Vector2(0f, -imageTop - imageHeight * .5f),
                new Vector2(Mathf.Min(size.x * .72f, imageHeight * 1.68f), imageHeight));
            CozyModalLayout.Stretch(imageSurface, 6f * u, 6f * u, 6f * u, 6f * u);
            CozyModalLayout.Stretch(contentImage.rectTransform, 14f * u, 14f * u, 14f * u, 14f * u);
        }
        if (descriptionScroll != null)
        {
            CozyModalLayout.Stretch(descriptionScroll.transform as RectTransform, 34f * u, 22f * u, 34f * u, 22f * u);
            CozyModalLayout.Stretch(descriptionScroll.viewport, 0f, 0f, 14f * u, 0f);
            RectTransform content = descriptionScroll.content;
            if (descriptionText != null && content != null && descriptionScroll.viewport != null)
            {
                descriptionText.enableAutoSizing = false;
                descriptionText.fontSize = 40f * u;
                descriptionText.lineSpacing = 5f * u;
                descriptionText.paragraphSpacing = 10f * u;
                descriptionText.enableWordWrapping = true;
                float width = Mathf.Min(1180f * u, descriptionScroll.viewport.rect.width);
                float height = Mathf.Max(descriptionScroll.viewport.rect.height,
                    descriptionText.GetPreferredValues(descriptionText.text, width, Mathf.Infinity).y + 8f * u);
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
