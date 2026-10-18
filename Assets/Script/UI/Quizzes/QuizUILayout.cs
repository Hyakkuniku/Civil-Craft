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
    [HideInInspector] public int presentationVersion;
    public Image paperFrame;
    public Image paperFace;
    public CanvasGroup paperGroup;
    public RectTransform npcNameplate;
    public TMP_Text npcName;
    private bool npcPresentation;
    private bool wasVisible;
    private float entryProgress = 1f;
    private Vector2 paperRestPosition;
    private Rect lastRect;
    private Rect lastSafe;
    private Vector2Int lastScreen;
    private bool applied;
    public int ColumnCount { get; private set; }
    /// <summary>Screen-normalized, safe-area-aware space left unobstructed for the asking NPC.</summary>
    public Rect NpcScreenViewport { get; private set; } = new Rect(0f, 0f, .4f, 1f);

    public void SetNpcPresentation(bool active, string speakerName)
    {
        EnsurePresentationElements();
        npcPresentation = active;
        if (npcName != null) npcName.text = string.IsNullOrWhiteSpace(speakerName) ? "Your guide" : speakerName;
        if (npcNameplate != null) npcNameplate.gameObject.SetActive(active);
        applied = false;
        ApplyLayout();
    }

    /// <summary>Upgrades old authored prefabs once without replacing their controls or custom content.</summary>
    public void EnsurePresentationElements()
    {
        if (view == null || view.Panel == null || safeContent == null) return;
        view.HideLegacyPauseControl();
        Image blocker = view.Panel.GetComponent<Image>();
        if (blocker != null)
        {
            blocker.color = Color.clear; blocker.raycastTarget = true; blocker.alphaHitTestMinimumThreshold = 0f;
            // Preserve the modal hit surface even when Unity strips transparent UI geometry.
            blocker.canvasRenderer.cullTransparentMesh = false;
        }
        if (paperFrame == null) paperFrame = safeContent.GetComponent<Image>();
        if (paperFrame == null) paperFrame = safeContent.gameObject.AddComponent<Image>();
        paperFrame.color = Ink; paperFrame.raycastTarget = false;
        Image sample = view.choiceTemplate != null ? view.choiceTemplate.GetComponent<Image>() : null;
        if (sample != null) { paperFrame.sprite = sample.sprite; paperFrame.type = sample.type; }
        if (paperFace == null)
        {
            paperFace = NewImage(safeContent, "Quiz Paper", Cream, paperFrame.sprite);
            paperFace.transform.SetAsFirstSibling();
        }
        Stretch(paperFace.rectTransform, 3f, 3f, 3f, 3f);
        // Unity's Editor can return a native fake-null component wrapper, which
        // must use Unity equality rather than the C# null-coalescing operator.
        if (paperGroup == null) paperGroup = safeContent.GetComponent<CanvasGroup>();
        if (paperGroup == null) paperGroup = safeContent.gameObject.AddComponent<CanvasGroup>();
        if (npcNameplate == null)
        {
            Image plate = NewImage(view.Panel.transform, "Asking NPC Nameplate", Ink, paperFrame.sprite);
            npcNameplate = plate.rectTransform;
            Image face = NewImage(npcNameplate, "Paper", Cream, paperFrame.sprite);
            Stretch(face.rectTransform, 2f, 2f, 2f, 2f);
            GameObject label = new GameObject("NPC Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.layer = view.Panel.layer; label.transform.SetParent(npcNameplate, false);
            npcName = label.GetComponent<TMP_Text>();
            npcName.font = view.title != null ? view.title.font : null;
            npcName.color = Ink; npcName.richText = false; npcName.raycastTarget = false;
            npcName.alignment = TextAlignmentOptions.Center;
            npcName.enableWordWrapping = false; npcName.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(npcName.rectTransform, 12f, 4f, 12f, 4f);
        }
        ClearScrollBackground(view.questionScroll);
        ClearScrollBackground(view.choiceScroll);
        ClearScrollBackground(feedbackScroll);
        presentationVersion = 3;
    }

    private static void ClearScrollBackground(ScrollRect scroll)
    {
        Image background = scroll != null ? scroll.GetComponent<Image>() : null;
        if (background == null) return;
        background.color = Color.clear; background.canvasRenderer.cullTransparentMesh = false;
    }

    private static Image NewImage(Transform parent, string name, Color fill, Sprite sprite)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.layer = parent.gameObject.layer; item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>(); image.color = fill; image.raycastTarget = false;
        image.sprite = sprite; if (sprite != null) image.type = Image.Type.Sliced;
        return image;
    }

    public void ResetFeedbackScroll()
    {
        if (feedbackScroll != null) { feedbackScroll.StopMovement(); feedbackScroll.verticalNormalizedPosition = 1f; }
    }

    private void OnEnable() { applied = false; wasVisible = false; ApplyLayout(); }
    private void LateUpdate()
    {
        if (view == null || view.Panel == null) return;
        bool visible = view.Panel.activeInHierarchy;
        if (!visible) { wasVisible = false; return; }
        if (!wasVisible) entryProgress = Application.isPlaying ? 0f : 1f;
        wasVisible = true;
        RectTransform panel = view.Panel.transform as RectTransform;
        if (!applied || panel.rect != lastRect || Screen.safeArea != lastSafe || lastScreen.x != Screen.width || lastScreen.y != Screen.height) ApplyLayout();
        if (paperGroup != null)
        {
            entryProgress = Mathf.Min(1f, entryProgress + Time.unscaledDeltaTime / .28f);
            float ease = 1f - Mathf.Pow(1f - entryProgress, 3f);
            paperGroup.alpha = Mathf.Lerp(.15f, 1f, ease);
            safeContent.anchoredPosition = paperRestPosition + Vector2.right * ((1f - ease) * 48f);
        }
    }

    public void ApplyLayout()
    {
        if (view == null || view.Panel == null || safeContent == null) return;
        EnsurePresentationElements();
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
        float u = Mathf.Clamp(Mathf.Min(available.width / 1600f, available.height / 900f), .58f, 1.15f);
        float margin = 18f * u;
        Rect paper = Rect.MinMaxRect(available.xMin + margin, available.yMin + margin, available.xMax - margin, available.yMax - margin);
        Rect npc = new Rect(available.xMin, available.yMin, available.width * .39f, available.height);
        if (npcPresentation)
        {
            if (available.width >= available.height)
                paper.xMin = available.xMin + available.width * .405f;
            else
            {
                float npcHeight = available.height * .27f;
                npc = new Rect(available.xMin, available.yMax - npcHeight, available.width, npcHeight);
                paper.yMax = npc.yMin - margin * .5f;
            }
        }
        NpcScreenViewport = new Rect((npc.xMin - panel.rect.xMin) / panel.rect.width,
            (npc.yMin - panel.rect.yMin) / panel.rect.height, npc.width / panel.rect.width, npc.height / panel.rect.height);
        safeContent.anchorMin = safeContent.anchorMax = Vector2.one * .5f;
        safeContent.sizeDelta = paper.size;
        paperRestPosition = paper.center - panel.rect.center;
        safeContent.anchoredPosition = paperRestPosition;
        if (npcNameplate != null)
        {
            npcNameplate.gameObject.SetActive(npcPresentation);
            npcNameplate.anchorMin = npcNameplate.anchorMax = Vector2.one * .5f;
            npcNameplate.sizeDelta = new Vector2(Mathf.Min(npc.width - margin * 2f, 330f * u), Mathf.Max(42f * u, 30f / Mathf.Max(.01f, canvas != null ? canvas.scaleFactor : 1f)));
            npcNameplate.anchoredPosition = new Vector2(npc.center.x, npc.yMin + npcNameplate.sizeDelta.y * .5f + margin) - panel.rect.center;
            npcName.fontSize = ReadableFont(28f * u, 18f, canvas);
        }
        float inset = 24f * u;
        float physicalScale = Mathf.Max(.01f, canvas != null ? canvas.scaleFactor : 1f);
        float titleHeight = Mathf.Max(48f * u, 36f / physicalScale);
        float progressHeight = Mathf.Max(38f * u, 28f / physicalScale);
        float headerHeight = titleHeight + 6f * u + progressHeight;
        float top = 18f * u + headerHeight + 16f * u;
        float buttonHeight = Mathf.Max(62f * u, PhysicalTouchUnits(canvas));
        float feedbackHeight = Mathf.Max(65f * u, 54f / Mathf.Max(.01f, canvas != null ? canvas.scaleFactor : 1f));
        float footerHeight = feedbackHeight + 18f * u + buttonHeight;
        float bottom = 18f * u + footerHeight + 16f * u;
        Top(header, inset, inset, 18f * u, headerHeight);
        Top(view.title.rectTransform, 0f, 0f, 0f, titleHeight);
        view.title.fontSize = ReadableFont(35f * u, 23f, canvas);
        view.title.overflowMode = TextOverflowModes.Ellipsis;
        Top(view.progress.rectTransform, 0f, 0f, titleHeight + 6f * u, progressHeight);
        view.progress.fontSize = ReadableFont(23f * u, 17f, canvas);
        view.progress.overflowMode = TextOverflowModes.Ellipsis;
        Bottom(footer, inset, inset, 18f * u, footerHeight);
        Top(feedbackViewport, 0f, 0f, 0f, feedbackHeight);
        view.feedback.fontSize = ReadableFont(25f * u, 18f, canvas);
        FitText(view.feedback, feedbackContent, feedbackViewport, u);
        float buttonWidth = Mathf.Min(280f * u, footer.rect.width * .45f);
        Bottom(view.leaveButton.transform as RectTransform, 0f, footer.rect.width - buttonWidth, 0f, buttonHeight);
        foreach (Button button in new[] { view.submitButton, view.nextButton, view.finishButton, view.retrySaveButton })
            Bottom(button.transform as RectTransform, footer.rect.width - buttonWidth, 0f, 0f, buttonHeight);
        if (view.IsAssessment && view.previousButton != null)
        {
            float gap = 12f * u;
            float width = (footer.rect.width - gap * 2f) / 3f;
            Bottom(view.previousButton.transform as RectTransform, 0f, footer.rect.width - width, 0f, buttonHeight);
            Bottom(view.nextButton.transform as RectTransform, width + gap, width + gap, 0f, buttonHeight);
            Bottom(view.submitButton.transform as RectTransform, (width + gap) * 2f, 0f, 0f, buttonHeight);
        }
        foreach (Button button in new[] { view.leaveButton, view.submitButton, view.nextButton, view.finishButton, view.retrySaveButton, view.previousButton })
        {
            TMP_Text text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null) text.fontSize = ReadableFont(27f * u, 18f, canvas);
        }
        float bodyHeight = Mathf.Max(90f, paper.height - top - bottom);
        float promptHeight = Mathf.Clamp(bodyHeight * .27f, 80f * u, 160f * u);
        promptHeight = Mathf.Max(promptHeight, 56f / physicalScale);
        Top(questionViewport, inset, inset, top, promptHeight);
        view.question.fontSize = ReadableFont(31f * u, 20f, canvas);
        FitText(view.question, questionContent, questionViewport, u);
        Stretch(choiceViewport, inset, bottom, inset, top + promptHeight + 18f * u);
        float scale = canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f;
        ColumnCount = paper.width * scale >= 760f && available.width > available.height ? 2 : 1;
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
