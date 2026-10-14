using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adaptive quiz surface. Long prompts and actual choice lists scroll independently.</summary>
[DisallowMultipleComponent]
public sealed class QuizUILayout : MonoBehaviour
{
    public static readonly Color Ink = new Color32(75, 50, 36, 255);
    public static readonly Color Cream = new Color32(255, 249, 238, 255);
    public static readonly Color Sand = new Color32(239, 226, 204, 255);
    public static readonly Color Gold = new Color32(239, 166, 49, 255);
    public QuizView view;
    public RectTransform safeContent;
    public RectTransform header;
    public RectTransform footer;
    public RectTransform questionViewport;
    public RectTransform questionContent;
    public RectTransform choiceViewport;
    public RectTransform feedbackViewport;
    public RectTransform feedbackContent;
    public ScrollRect feedbackScroll;
    private Rect lastRect;
    private Rect lastSafe;
    private Vector2Int lastScreen;
    private bool applied;
    public int ColumnCount { get; private set; }

    private void OnEnable() { applied = false; ApplyLayout(); }
    private void LateUpdate()
    {
        if (view == null || view.Panel == null || !view.Panel.activeInHierarchy) return;
        RectTransform panel = view.Panel.transform as RectTransform;
        if (!applied || panel.rect != lastRect || Screen.safeArea != lastSafe || lastScreen.x != Screen.width || lastScreen.y != Screen.height) ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (view == null || view.Panel == null || safeContent == null) return;
        RectTransform panel = view.Panel.transform as RectTransform;
        Rect available = panel.rect;
        Canvas canvas = view.GetComponent<Canvas>();
        if (Application.isPlaying && canvas != null && Screen.width > 0 && Screen.height > 0)
        {
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, Screen.safeArea.min, camera, out Vector2 min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, Screen.safeArea.max, camera, out Vector2 max))
                available = Rect.MinMaxRect(Mathf.Max(min.x, available.xMin), Mathf.Max(min.y, available.yMin), Mathf.Min(max.x, available.xMax), Mathf.Min(max.y, available.yMax));
        }
        if (available.width < 100f || available.height < 100f) return;
        safeContent.anchorMin = safeContent.anchorMax = Vector2.one * .5f;
        safeContent.sizeDelta = available.size;
        safeContent.anchoredPosition = available.center - panel.rect.center;
        float u = Mathf.Clamp(Mathf.Min(available.width / 1600f, available.height / 900f), .58f, 1.15f);
        float inset = 30f * u;
        float top = 146f * u;
        float buttonHeight = Mathf.Max(62f * u, PhysicalTouchUnits(canvas));
        float feedbackHeight = Mathf.Max(65f * u, 54f / Mathf.Max(.01f, canvas != null ? canvas.scaleFactor : 1f));
        float footerHeight = feedbackHeight + 18f * u + buttonHeight;
        float bottom = 18f * u + footerHeight + 16f * u;
        Top(header, inset, inset, 18f * u, 112f * u);
        Top(view.title.rectTransform, 0f, 210f * u, 0f, 57f * u);
        view.title.fontSize = ReadableFont(38f * u, 24f, canvas);
        view.title.overflowMode = TextOverflowModes.Ellipsis;
        Top(view.progress.rectTransform, 0f, 0f, 67f * u, 38f * u);
        view.progress.fontSize = ReadableFont(26f * u, 18f, canvas);
        Top(view.pauseButton.transform as RectTransform, header.rect.width - 180f * u, 0f, 0f, Mathf.Max(64f * u, PhysicalTouchUnits(canvas)));
        Bottom(footer, inset, inset, 18f * u, footerHeight);
        Top(feedbackViewport, 0f, 0f, 0f, feedbackHeight);
        view.feedback.fontSize = ReadableFont(25f * u, 18f, canvas);
        FitText(view.feedback, feedbackContent, feedbackViewport, u);
        float buttonWidth = Mathf.Min(280f * u, footer.rect.width * .45f);
        Bottom(view.leaveButton.transform as RectTransform, 0f, footer.rect.width - buttonWidth, 0f, buttonHeight);
        foreach (Button button in new[] { view.submitButton, view.nextButton, view.finishButton, view.retrySaveButton })
            Bottom(button.transform as RectTransform, footer.rect.width - buttonWidth, 0f, 0f, buttonHeight);
        foreach (Button button in new[] { view.leaveButton, view.submitButton, view.nextButton, view.finishButton, view.retrySaveButton, view.pauseButton })
        {
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.fontSize = ReadableFont(27f * u, 18f, canvas);
        }
        float bodyHeight = Mathf.Max(90f, available.height - top - bottom);
        float promptHeight = Mathf.Clamp(bodyHeight * .28f, 82f * u, 200f * u);
        Top(questionViewport, inset, inset, top, promptHeight);
        view.question.fontSize = ReadableFont(31f * u, 20f, canvas);
        FitText(view.question, questionContent, questionViewport, u);
        Stretch(choiceViewport, inset, bottom, inset, top + promptHeight + 18f * u);
        ColumnCount = available.width > available.height && available.width >= 720f ? 2 : 1;
        LayoutChoices(u);
        lastRect = panel.rect; lastSafe = Screen.safeArea; lastScreen = new Vector2Int(Screen.width, Screen.height); applied = true;
    }

    private void LayoutChoices(float u)
    {
        if (view.choiceContent == null || choiceViewport == null) return;
        float gap = 18f * u;
        float width = Mathf.Max(80f, (choiceViewport.rect.width - gap * (ColumnCount - 1) - 8f * u) / ColumnCount);
        float y = 4f * u;
        Canvas canvas = view.GetComponent<Canvas>();
        float physicalScale = canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f;
        var cards = view.VisibleChoices;
        for (int row = 0; row < cards.Count; row += ColumnCount)
        {
            float height = PhysicalTouchUnits(view.GetComponent<Canvas>());
            for (int col = 0; col < ColumnCount && row + col < cards.Count; col++)
            {
                QuizChoiceView card = cards[row + col];
                if (card.label != null) card.label.fontSize = ReadableFont(29f * u, 20f, canvas);
                height = Mathf.Max(height, card.RequiredHeight(width, u));
            }
            for (int col = 0; col < ColumnCount && row + col < cards.Count; col++)
            {
                QuizChoiceView card = cards[row + col];
                RectTransform rect = card.transform as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(4f * u + col * (width + gap), -y);
                rect.sizeDelta = new Vector2(width, height);
                card.Layout(width, height, u, physicalScale);
            }
            y += height + gap;
        }
        view.choiceContent.anchorMin = new Vector2(0f, 1f); view.choiceContent.anchorMax = Vector2.one; view.choiceContent.pivot = new Vector2(.5f, 1f);
        view.choiceContent.sizeDelta = new Vector2(0f, Mathf.Max(choiceViewport.rect.height, y));
        if (feedbackScroll != null) feedbackScroll.StopMovement();
    }

    private static float PhysicalTouchUnits(Canvas canvas)
    {
        return canvas != null && canvas.scaleFactor > 0f ? 48f / canvas.scaleFactor : 48f;
    }

    private static float ReadableFont(float desired, float minimumPixels, Canvas canvas) =>
        Mathf.Max(desired, minimumPixels / Mathf.Max(.01f, canvas != null ? canvas.scaleFactor : 1f));

    private static void FitText(TMP_Text text, RectTransform content, RectTransform viewport, float u)
    {
        if (text == null || content == null || viewport == null) return;
        text.richText = false; text.enableAutoSizing = false; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
        float padding = 14f * u;
        float height = Mathf.Max(viewport.rect.height, text.GetPreferredValues(text.text, Mathf.Max(20f, viewport.rect.width - padding * 2f), 0f).y + padding * 2f);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1f); content.sizeDelta = new Vector2(0f, height);
        Stretch(text.rectTransform, padding, padding, padding, padding);
    }

    public static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); rect.localScale = Vector3.one;
    }
    public static void Top(RectTransform rect, float left, float right, float top, float height)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f;
        rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(-right, -top);
    }
    public static void Bottom(RectTransform rect, float left, float right, float bottom, float height)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1f, 0f); rect.pivot = Vector2.one * .5f;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, bottom + height);
    }
}
