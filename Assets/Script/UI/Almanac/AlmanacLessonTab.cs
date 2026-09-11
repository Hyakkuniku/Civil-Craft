using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AlmanacLessonTab : MonoBehaviour
{
    [Header("Lesson Database")]
    [Tooltip("Assign every LessonData asset that can appear in the Almanac.")]
    [SerializeField] private List<LessonData> allLessons = new List<LessonData>();

    [Header("Grid")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private AlmanacLessonButton lessonButtonPrefab;
    [SerializeField] private bool showLockedLessons = true;
    [SerializeField] private bool sortAlphabetically = true;
    [Tooltip("Allows listed lessons to be previewed before they have been discovered.")]
    [SerializeField] private bool allowOpeningLockedLessons;

    [Header("Empty State")]
    [SerializeField] private GameObject emptyStateRoot;
    [SerializeField] private TMP_Text emptyStateText;

    [Header("Two-page Reader")]
    [Tooltip("Shows the selected lesson on the book's right page instead of opening the full-screen lesson window.")]
    [SerializeField] private bool useInlineReader = true;
    [SerializeField] private bool selectFirstUnlockedLesson = true;

    [Header("Reader Colors")]
    [SerializeField] private Color inkColor = new Color(0.25f, 0.16f, 0.10f, 1f);
    [SerializeField] private Color mutedInkColor = new Color(0.45f, 0.33f, 0.24f, 1f);
    [SerializeField] private Color accentColor = new Color(0.65f, 0.36f, 0.14f, 1f);
    [SerializeField] private Color imageFrameColor = new Color(0.91f, 0.85f, 0.75f, 0.72f);

    private readonly List<AlmanacLessonButton> spawnedButtons = new List<AlmanacLessonButton>();
    private readonly Dictionary<LessonData, AlmanacLessonButton> buttonsByLesson =
        new Dictionary<LessonData, AlmanacLessonButton>();

    private RectTransform rightPage;
    private RectTransform listScrollRect;
    private TMP_Text progressText;
    private TMP_Text readerKickerText;
    private TMP_Text readerTitleText;
    private TMP_Text readerDescriptionText;
    private TMP_Text readerPageText;
    private GameObject readerImageFrame;
    private Image readerImage;
    private ScrollRect readerScrollRect;
    private LessonData selectedLesson;
    private TMP_Text styleSource;

    private void OnEnable()
    {
        if (useInlineReader)
        {
            EnsureTwoPageLayout();
            if (rightPage != null) rightPage.gameObject.SetActive(true);
        }

        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnLessonUnlocked += HandleLessonUnlocked;
            PlayerDataManager.Instance.MarkLessonsAlmanacRead();
        }

        RefreshLessons();
    }

    private void OnDisable()
    {
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnLessonUnlocked -= HandleLessonUnlocked;

        if (rightPage != null)
            rightPage.gameObject.SetActive(false);
    }

    public void RefreshLessons()
    {
        ClearSpawnedButtons();

        if (buttonContainer == null || lessonButtonPrefab == null)
        {
            Debug.LogWarning("[AlmanacLessonTab] Button Container or Lesson Button Prefab is missing.", this);
            SetEmptyState(true, "Lesson archive is not configured.");
            return;
        }

        List<LessonData> displayLessons = new List<LessonData>();
        HashSet<string> seenIds = new HashSet<string>();

        foreach (LessonData lesson in allLessons)
        {
            if (lesson == null) continue;

            if (!seenIds.Add(lesson.Id))
            {
                Debug.LogWarning($"[AlmanacLessonTab] Duplicate Lesson ID '{lesson.Id}'.", lesson);
                continue;
            }

            bool unlocked = LessonSaveManager.IsUnlocked(lesson);
            if (unlocked || showLockedLessons)
                displayLessons.Add(lesson);
        }

        if (sortAlphabetically)
            displayLessons.Sort((a, b) => string.Compare(a.Title, b.Title,
                System.StringComparison.OrdinalIgnoreCase));

        foreach (LessonData lesson in displayLessons)
        {
            bool unlocked = LessonSaveManager.IsUnlocked(lesson);
            AlmanacLessonButton entry = Instantiate(lessonButtonPrefab, buttonContainer, false);
            entry.Configure(lesson, unlocked, OpenLesson, allowOpeningLockedLessons);
            entry.SetDisplayNumber(spawnedButtons.Count + 1);
            // The scene may use an inactive styled template instead of a prefab asset.
            entry.gameObject.SetActive(true);
            spawnedButtons.Add(entry);
            buttonsByLesson[lesson] = entry;
        }

        SetEmptyState(spawnedButtons.Count == 0,
            showLockedLessons ? "No lessons are available." : "Discover lessons while exploring.");

        UpdateProgress(displayLessons);
        RestoreOrChooseSelection(displayLessons);
    }

    private void OpenLesson(LessonData lesson)
    {
        if (!LessonSaveManager.IsUnlocked(lesson) && !allowOpeningLockedLessons) return;

        if (useInlineReader)
        {
            SelectLesson(lesson);
            return;
        }

        if (LessonUIManager.Instance == null)
        {
            Debug.LogWarning("[AlmanacLessonTab] LessonUIManager is not available.", this);
            return;
        }

        LessonUIManager.Instance.ShowLesson(lesson);
    }

    private void HandleLessonUnlocked(string lessonId)
    {
        RefreshLessons();
    }

    private void ClearSpawnedButtons()
    {
        foreach (AlmanacLessonButton entry in spawnedButtons)
        {
            if (entry != null) Destroy(entry.gameObject);
        }
        spawnedButtons.Clear();
        buttonsByLesson.Clear();
    }

    private void SetEmptyState(bool visible, string message)
    {
        if (emptyStateRoot != null) emptyStateRoot.SetActive(visible);
        if (emptyStateText != null) emptyStateText.text = message;
    }

    private void EnsureTwoPageLayout()
    {
        if (rightPage != null) return;

        RectTransform leftPage = transform as RectTransform;
        if (leftPage == null || leftPage.parent == null) return;

        styleSource = lessonButtonPrefab != null
            ? lessonButtonPrefab.GetComponentInChildren<TMP_Text>(true)
            : GetComponentInChildren<TMP_Text>(true);

        ConfigureLeftPage(leftPage);
        CreateRightPage(leftPage);
        ShowReaderPlaceholder();
    }

    private void ConfigureLeftPage(RectTransform leftPage)
    {
        Transform existingHeader = leftPage.Find("LessonListHeader_Runtime");
        if (existingHeader == null)
        {
            RectTransform header = CreateRect("LessonListHeader_Runtime", leftPage);
            SetAnchors(header, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(34f, -92f), new Vector2(-28f, -22f));

            TMP_Text heading = CreateText("Heading", header, "LESSON JOURNAL", 29f,
                FontStyles.Bold, inkColor, TextAlignmentOptions.MidlineLeft);
            SetAnchors(heading.rectTransform, new Vector2(0f, 0f), new Vector2(0.66f, 1f),
                Vector2.zero, Vector2.zero);

            progressText = CreateText("Progress", header, string.Empty, 15f,
                FontStyles.Normal, mutedInkColor, TextAlignmentOptions.MidlineRight);
            SetAnchors(progressText.rectTransform, new Vector2(0.55f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);

            RectTransform rule = CreateImage("Rule", header, accentColor).rectTransform;
            rule.anchorMin = new Vector2(0f, 0f);
            rule.anchorMax = new Vector2(1f, 0f);
            rule.pivot = new Vector2(0.5f, 0f);
            rule.anchoredPosition = Vector2.zero;
            rule.sizeDelta = new Vector2(0f, 2f);
        }
        else
        {
            progressText = existingHeader.Find("Progress")?.GetComponent<TMP_Text>();
        }

        ScrollRect listScroll = buttonContainer != null
            ? buttonContainer.GetComponentInParent<ScrollRect>()
            : null;
        listScrollRect = listScroll != null ? listScroll.transform as RectTransform : null;
        if (listScrollRect != null)
        {
            listScrollRect.anchorMin = Vector2.zero;
            listScrollRect.anchorMax = Vector2.one;
            listScrollRect.offsetMin = new Vector2(28f, 28f);
            listScrollRect.offsetMax = new Vector2(-24f, -108f);
        }

        VerticalLayoutGroup layout = buttonContainer != null
            ? buttonContainer.GetComponent<VerticalLayoutGroup>()
            : null;
        if (layout != null)
        {
            layout.padding = new RectOffset(6, 6, 8, 8);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
        }
    }

    private void CreateRightPage(RectTransform leftPage)
    {
        rightPage = CreateRect("LessonReaderPage_Runtime", leftPage.parent);
        rightPage.anchorMin = leftPage.anchorMin;
        rightPage.anchorMax = leftPage.anchorMax;
        rightPage.pivot = new Vector2(0f, leftPage.pivot.y);

        float leftPageRightEdge = leftPage.anchoredPosition.x +
            ((1f - leftPage.pivot.x) * leftPage.rect.width);
        rightPage.anchoredPosition = new Vector2(leftPageRightEdge, leftPage.anchoredPosition.y);
        rightPage.sizeDelta = leftPage.sizeDelta;
        rightPage.SetSiblingIndex(Mathf.Min(leftPage.GetSiblingIndex() + 1, leftPage.parent.childCount - 1));

        LayoutElement layoutElement = rightPage.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        RectTransform content = CreateRect("ReaderContent", rightPage);
        SetAnchors(content, Vector2.zero, Vector2.one, new Vector2(42f, 34f), new Vector2(-42f, -30f));

        readerKickerText = CreateText("Kicker", content, "SELECT A LESSON", 15f,
            FontStyles.Bold, accentColor, TextAlignmentOptions.MidlineLeft);
        SetTopRect(readerKickerText.rectTransform, 0f, 26f);

        readerTitleText = CreateText("Title", content, "Lesson Journal", 32f,
            FontStyles.Bold, inkColor, TextAlignmentOptions.MidlineLeft);
        readerTitleText.enableAutoSizing = true;
        readerTitleText.fontSizeMin = 23f;
        readerTitleText.fontSizeMax = 32f;
        SetTopRect(readerTitleText.rectTransform, 30f, 56f);

        readerPageText = CreateText("PageNumber", content, string.Empty, 14f,
            FontStyles.Normal, mutedInkColor, TextAlignmentOptions.MidlineRight);
        SetTopRect(readerPageText.rectTransform, 2f, 24f);
        readerPageText.rectTransform.anchorMin = new Vector2(0.65f, 1f);

        RectTransform rule = CreateImage("Rule", content, accentColor).rectTransform;
        SetTopRect(rule, 91f, 2f);

        Image frame = CreateImage("ImageFrame", content, imageFrameColor);
        readerImageFrame = frame.gameObject;
        SetTopRect(frame.rectTransform, 110f, 188f);

        readerImage = CreateImage("LessonImage", frame.rectTransform, Color.white);
        readerImage.preserveAspect = true;
        readerImage.raycastTarget = false;
        SetAnchors(readerImage.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(10f, 10f), new Vector2(-10f, -10f));

        CreateReaderScrollView(content);
    }

    private void CreateReaderScrollView(RectTransform parent)
    {
        RectTransform scrollRoot = CreateRect("DescriptionScroll", parent);
        scrollRoot.anchorMin = Vector2.zero;
        scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = Vector2.zero;
        scrollRoot.offsetMax = new Vector2(0f, -316f);

        readerScrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
        readerScrollRect.horizontal = false;
        readerScrollRect.vertical = true;
        readerScrollRect.movementType = ScrollRect.MovementType.Clamped;
        readerScrollRect.scrollSensitivity = 26f;

        RectTransform viewport = CreateRect("Viewport", scrollRoot);
        SetAnchors(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        readerDescriptionText = CreateText("Description", viewport,
            string.Empty, 20f, FontStyles.Normal, inkColor, TextAlignmentOptions.TopLeft);
        readerDescriptionText.enableWordWrapping = true;
        readerDescriptionText.lineSpacing = 10f;
        SetAnchors(readerDescriptionText.rectTransform, new Vector2(0f, 1f), Vector2.one,
            Vector2.zero, Vector2.zero);
        readerDescriptionText.rectTransform.pivot = new Vector2(0.5f, 1f);

        ContentSizeFitter fitter = readerDescriptionText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        readerScrollRect.viewport = viewport;
        readerScrollRect.content = readerDescriptionText.rectTransform;
    }

    private void RestoreOrChooseSelection(List<LessonData> displayLessons)
    {
        if (!useInlineReader) return;
        EnsureTwoPageLayout();

        if (selectedLesson != null && buttonsByLesson.ContainsKey(selectedLesson) &&
            (LessonSaveManager.IsUnlocked(selectedLesson) || allowOpeningLockedLessons))
        {
            SelectLesson(selectedLesson);
            return;
        }

        selectedLesson = null;
        if (selectFirstUnlockedLesson)
        {
            foreach (LessonData lesson in displayLessons)
            {
                if (LessonSaveManager.IsUnlocked(lesson) || allowOpeningLockedLessons)
                {
                    SelectLesson(lesson);
                    return;
                }
            }
        }

        ShowReaderPlaceholder();
    }

    private void SelectLesson(LessonData lesson)
    {
        if (lesson == null || rightPage == null) return;

        foreach (KeyValuePair<LessonData, AlmanacLessonButton> pair in buttonsByLesson)
            pair.Value.SetSelected(pair.Key == lesson);

        selectedLesson = lesson;
        int index = 0;
        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            AlmanacLessonButton button = spawnedButtons[i];
            if (buttonsByLesson.TryGetValue(lesson, out AlmanacLessonButton selected) && button == selected)
            {
                index = i + 1;
                break;
            }
        }

        readerKickerText.text = "LESSON " + (index > 0 ? index.ToString("00") : string.Empty);
        readerTitleText.text = lesson.Title;
        readerDescriptionText.text = lesson.Description;
        readerPageText.text = index > 0 ? index + " / " + spawnedButtons.Count : string.Empty;

        bool hasImage = lesson.Image != null;
        readerImageFrame.SetActive(hasImage);
        readerImage.sprite = lesson.Image;
        readerScrollRect.transform.GetComponent<RectTransform>().offsetMax =
            new Vector2(0f, hasImage ? -316f : -110f);

        Canvas.ForceUpdateCanvases();
        readerScrollRect.StopMovement();
        readerScrollRect.verticalNormalizedPosition = 1f;
    }

    private void ShowReaderPlaceholder()
    {
        if (rightPage == null) return;

        foreach (AlmanacLessonButton button in spawnedButtons)
            button.SetSelected(false);

        readerKickerText.text = "LESSON JOURNAL";
        readerTitleText.text = "Your discoveries live here";
        readerDescriptionText.text =
            "Select an unlocked lesson from the left page to review its diagram and notes. " +
            "New lessons are added as you explore and complete activities.";
        readerPageText.text = string.Empty;
        readerImageFrame.SetActive(false);
        readerScrollRect.transform.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -110f);
        readerScrollRect.verticalNormalizedPosition = 1f;
    }

    private void UpdateProgress(List<LessonData> displayLessons)
    {
        if (progressText == null) return;

        int unlockedCount = 0;
        foreach (LessonData lesson in displayLessons)
        {
            if (LessonSaveManager.IsUnlocked(lesson)) unlockedCount++;
        }

        progressText.text = unlockedCount + " / " + displayLessons.Count + " DISCOVERED";
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.layer = gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string value, float fontSize,
        FontStyles fontStyle, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (styleSource != null)
        {
            text.font = styleSource.font;
            text.fontSharedMaterial = styleSource.fontSharedMaterial;
        }

        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private Image CreateImage(string objectName, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetTopRect(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(0f, height);
    }

    private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
