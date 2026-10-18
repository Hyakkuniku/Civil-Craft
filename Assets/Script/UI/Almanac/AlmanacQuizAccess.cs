using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene-local NPC offer; the Almanac is reviewable until Start Quiz is confirmed.</summary>
[DisallowMultipleComponent]
public sealed class AlmanacQuizAccess : MonoBehaviour
{
    private AlmanacManager book;
    private Button tab, start, back;
    private TMP_Text title, description, tabLabel;
    private QuizData quiz;
    private QuizContext context;
    private UnityEngine.Object owner;
    private Action<QuizOutcome> closed;
    private PlayerData saveOwner;
    private PlayerDataManager saveManager;
    private bool starting;
    private int generation;
    private int selectedCategory = -1;
    private int quizCategoryIndex = -1;

    public static void Install(AlmanacManager manager)
    {
        if (manager == null || manager.GetComponent<AlmanacQuizAccess>() != null) return;
        AlmanacQuizAccess access = manager.gameObject.AddComponent<AlmanacQuizAccess>();
        access.book = manager;
        access.BuildTab();
        manager.OnCategoryChanged += access.ApplySelection;
    }

    /// <summary>True owns one callback. A pending offer never locks movement or opens the book automatically.</summary>
    public static bool TryOffer(QuizData data, QuizContext identity, UnityEngine.Object sessionOwner, Action<QuizOutcome> callback)
    {
        AlmanacManager manager = AlmanacManager.Instance;
        AlmanacQuizAccess access = manager != null ? manager.GetComponent<AlmanacQuizAccess>() : null;
        if (access == null || access.tab == null || access.closed != null || callback == null || data == null || !data.assessmentMode ||
            !data.TryValidate(out _) || identity == null || !identity.IsValid || sessionOwner == null ||
            (sessionOwner is Behaviour behaviour && !behaviour.isActiveAndEnabled) ||
            PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null || ProtectedVisit()) return false;
        access.quiz = data;
        access.context = new QuizContext(identity.progressionId, identity.phaseId, identity.stepId);
        access.owner = sessionOwner;
        access.closed = callback;
        access.saveOwner = PlayerDataManager.Instance.CurrentData;
        access.saveManager = PlayerDataManager.Instance;
        access.generation++;
        access.Refresh();
        return true;
    }

    public static void CancelOwned(UnityEngine.Object sessionOwner)
    {
        AlmanacQuizAccess access = AlmanacManager.Instance != null ? AlmanacManager.Instance.GetComponent<AlmanacQuizAccess>() : null;
        if (access != null && access.owner == sessionOwner) access.Resolve(QuizOutcome.Cancelled);
    }

    /// <summary>Read-only reminder eligibility. Starting/active quizzes never count as waiting for the Almanac.</summary>
    public static bool TryGetWaitingQuiz(UnityEngine.Object sessionOwner, out QuizData data, out QuizContext identity)
    {
        data = null;
        identity = null;
        AlmanacQuizAccess access = AlmanacManager.Instance != null ? AlmanacManager.Instance.GetComponent<AlmanacQuizAccess>() : null;
        if (sessionOwner == null || access == null || access.owner != sessionOwner || access.closed == null ||
            access.starting || access.quiz == null || !access.quiz.assessmentMode || access.context == null ||
            !access.context.IsValid || !access.IsAuthorized() || QuizUIManager.IsAnyQuizActive) return false;
        data = access.quiz;
        // Never expose the callback owner's mutable identity to an interaction.
        identity = new QuizContext(access.context.progressionId, access.context.phaseId, access.context.stepId);
        return true;
    }

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsGuestSaveProtected;

    private void Update()
    {
        if (closed == null) return;
        if (!IsAuthorized()) Resolve(QuizOutcome.Cancelled);
    }

    private bool IsAuthorized() => owner != null &&
        (!(owner is Behaviour behaviour) || behaviour.isActiveAndEnabled) &&
        saveManager != null && PlayerDataManager.Instance == saveManager &&
        ReferenceEquals(saveOwner, saveManager.CurrentData) && !ProtectedVisit();

    private void OnDestroy()
    {
        if (book != null) book.OnCategoryChanged -= ApplySelection;
        Resolve(QuizOutcome.Cancelled);
    }
    private void OnDisable() { Resolve(QuizOutcome.Cancelled); }

    private void Resolve(QuizOutcome outcome)
    {
        if (closed == null) return;
        Action<QuizOutcome> callback = closed;
        closed = null;
        UnityEngine.Object previousOwner = owner;
        owner = null;
        quiz = null;
        saveOwner = null;
        saveManager = null;
        starting = false;
        generation++;
        Refresh();
        if (outcome != QuizOutcome.Completed && previousOwner != null) QuizUIManager.CancelOwnedSession(previousOwner);
        callback?.Invoke(outcome);
    }

    private void StartQuiz()
    {
        if (starting || closed == null || !book.IsOpenAndReady) return;
        if (!IsAuthorized()) { Resolve(QuizOutcome.Cancelled); return; }
        starting = true;
        Refresh();
        int expected = generation;
        book.CloseAlmanacThen(() =>
        {
            if (expected != generation || owner == null || closed == null) return;
            // Account/visit/owner changes may occur during the book animation,
            // before this component has another Update. Recheck at handoff.
            if (!IsAuthorized()) { Resolve(QuizOutcome.Cancelled); return; }
            if (!QuizUIManager.TryOpen(quiz, context, owner, outcome => { if (expected == generation) Resolve(outcome); }))
            {
                starting = false;
                Refresh();
                // Keep the offer retryable; an unavailable UI cannot advance the NPC.
                book.OpenAlmanac();
                if (description != null) description.text = "Quiz could not open. Close other panels, then try Start Quiz again.";
            }
        });
        // CloseAlmanacThen may be blocked by an active mandatory book walkthrough.
        if (book.IsOpenAndReady) { starting = false; Refresh(); }
    }

    private void Refresh()
    {
        bool available = closed != null && owner != null && !ProtectedVisit();
        if (tab != null) tab.interactable = available && !starting;
        RefreshTabContrast();
        if (start != null) { start.interactable = available && !starting; start.gameObject.SetActive(available); }
        if (back != null) back.interactable = !starting;
        if (title != null) title.text = available && quiz != null ? quiz.title : "Quiz";
        if (description != null) description.text = available && quiz != null ?
            "Review your lessons before starting.\n\nAnswer all " + quiz.QuestionCount + " questions, then press Submit. You can move between questions before submitting.\n\nOnce started, finish the quiz before returning to the Almanac. Any score lets you continue; no rewards are added." :
            "A quiz becomes available here when an NPC asks you to take it.";
    }

    private void ApplySelection(int categoryIndex)
    {
        selectedCategory = categoryIndex;
        RefreshTabContrast();
    }

    private void RefreshTabContrast()
    {
        // Current bookmark faces are cream paper, not the legacy dark sprite.
        // Keep the label readable in both available and disabled states.
        if (tabLabel == null || tab == null) return;
        tabLabel.color = !tab.interactable ? new Color32(126, 99, 71, 255) : QuizUILayout.Ink;
        tabLabel.fontStyle = selectedCategory == quizCategoryIndex ? FontStyles.Bold : FontStyles.Normal;
    }

    private void BuildTab()
    {
        if (book.categories == null || book.categories.Count == 0 || book.Panel == null) return;
        AlmanacCategory template = book.categories.Find(category => category != null && category.tabButton != null && category.leftPageZone != null && category.rightPageZone != null);
        if (template == null) return;
        tab = Instantiate(template.tabButton, template.tabButton.transform.parent);
        tab.name = "Quiz Tab";
        tab.onClick = new Button.ButtonClickedEvent();
        // A template's target graphic may be a child face, which is replaced
        // below. Keep disabled/pressed tint attached to the surviving tab face.
        tab.targetGraphic = tab.GetComponent<Image>();
        // The existing tab may be icon-only. Do not keep its Profile icon/alert on Quiz.
        for (int childIndex = tab.transform.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = tab.transform.GetChild(childIndex);
            child.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
        }
        tabLabel = Text("Quiz Label", tab.transform, 24, TextAlignmentOptions.Center);
        tabLabel.text = "Quiz";
        AlmanacBookmarkLayout.ConfigureQuizLabel(tabLabel);
        Transform left = CloneZone(template.leftPageZone, "Quiz Left Page Zone");
        Transform right = CloneZone(template.rightPageZone, "Quiz Right Page Zone");
        RectTransform leftPage = Rect("Quiz Introduction", left);
        RectTransform rightPage = Rect("Quiz Instructions", right);
        Fill(leftPage, 38, 52, 42, 64); Fill(rightPage, 42, 52, 38, 64);
        title = Text("Quiz Title", leftPage, 38, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, 0, 0, 0, .23f);
        description = Text("Quiz Guidance", leftPage, 26, TextAlignmentOptions.TopLeft);
        Place(description.rectTransform, 0, .25f, 0, .72f);
        TMP_Text instructions = Text("Start Confirmation", rightPage, 28, TextAlignmentOptions.Center);
        instructions.text = "Ready to take the quiz?\n\nYou can still return and review your Almanac before starting.";
        Place(instructions.rectTransform, .04f, .12f, .04f, .52f);
        start = MakeButton("Start Quiz", rightPage, "Start Quiz", StartQuiz);
        Place(start.transform as RectTransform, .05f, .68f, .05f, .13f);
        back = MakeButton("Back to Review", rightPage, "Back", () => book.SelectCategory(0));
        Place(back.transform as RectTransform, .05f, .84f, .05f, .13f);
        book.categories.Add(new AlmanacCategory { categoryName = "Quiz", tabType = AlmanacTabType.Quiz, tabButton = tab,
            inactiveSprite = template.inactiveSprite, activeSprite = template.activeSprite, leftPageZone = left, rightPageZone = right });
        quizCategoryIndex = book.categories.Count - 1;
        AlmanacBookmarkLayout.Reflow(book.categories);
        book.BookMotion?.AddChapterFrame(book.categories.IndexOf(template), book.categories.Count - 1);
        Refresh();
    }

    private static Transform CloneZone(Transform source, string name)
    {
        RectTransform rect = Rect(name, source.parent);
        RectTransform original = source as RectTransform;
        rect.anchorMin = original.anchorMin; rect.anchorMax = original.anchorMax;
        rect.pivot = original.pivot; rect.sizeDelta = original.sizeDelta; rect.anchoredPosition = original.anchoredPosition;
        Image originalImage = source.GetComponent<Image>();
        if (originalImage != null)
        {
            Image paper = rect.gameObject.AddComponent<Image>(); paper.sprite = originalImage.sprite;
            paper.type = originalImage.type; paper.color = originalImage.color; paper.material = originalImage.material;
            paper.raycastTarget = originalImage.raycastTarget;
        }
        rect.gameObject.AddComponent<CanvasGroup>();
        return rect;
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer; rect.SetParent(parent, false); return rect;
    }
    private static TMP_Text Text(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        AlmanacTypography.ApplyFont(label); label.fontSize = size; label.color = QuizUILayout.Ink;
        label.richText = false; label.enableWordWrapping = true; label.alignment = alignment; label.raycastTarget = false;
        return label;
    }
    private static Button MakeButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction clicked)
    {
        RectTransform rect = Rect(name, parent); Image image = rect.gameObject.AddComponent<Image>(); image.color = QuizUILayout.Sand;
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(clicked);
        TMP_Text text = Text("Label", rect, 28, TextAlignmentOptions.Center); text.text = label; Fill(text.rectTransform, 6, 4, 6, 4); return button;
    }
    private static void Fill(RectTransform rect, float left, float bottom, float right, float top)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); }
    private static void Place(RectTransform rect, float left, float top, float right, float height)
    { rect.anchorMin = new Vector2(left, 1f - top - height); rect.anchorMax = new Vector2(1f - right, 1f - top); rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
