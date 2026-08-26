using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

    [Header("Empty State")]
    [SerializeField] private GameObject emptyStateRoot;
    [SerializeField] private TMP_Text emptyStateText;

    private readonly List<AlmanacLessonButton> spawnedButtons = new List<AlmanacLessonButton>();

    private void OnEnable()
    {
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
            entry.Configure(lesson, unlocked, OpenLesson);
            // The scene may use an inactive styled template instead of a prefab asset.
            entry.gameObject.SetActive(true);
            spawnedButtons.Add(entry);
        }

        SetEmptyState(spawnedButtons.Count == 0,
            showLockedLessons ? "No lessons are available." : "Discover lessons while exploring.");
    }

    private void OpenLesson(LessonData lesson)
    {
        if (!LessonSaveManager.IsUnlocked(lesson)) return;

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
    }

    private void SetEmptyState(bool visible, string message)
    {
        if (emptyStateRoot != null) emptyStateRoot.SetActive(visible);
        if (emptyStateText != null) emptyStateText.text = message;
    }
}
