using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class BuildLocationLessonTrigger : MonoBehaviour
{
    [Header("Location Lesson")]
    [SerializeField] private BuildLocation buildLocation;
    [Tooltip("Legacy single lesson. It is used when Lessons is empty, preserving existing scene setup.")]
    [SerializeField] private LessonData lesson;
    [Tooltip("Lessons shown in order when this bridge is finished. Closing one opens the next.")]
    [SerializeField] private List<LessonData> lessons = new List<LessonData>();

    [Header("Behavior")]
    [Tooltip("Automatically listens for bridges saved by LevelCompleteManager.")]
    [SerializeField] private bool listenForSavedBridge = true;
    [Tooltip("Prevents the same scene component from opening its lesson repeatedly during one play session.")]
    [SerializeField] private bool triggerOnlyOnce = true;

    [Header("Optional Events")]
    [SerializeField] private UnityEvent onLessonTriggered;
    [SerializeField] private UnityEvent onLessonSequenceFinished;

    private bool hasTriggered;
    private Coroutine lessonSequenceRoutine;

    private void Reset()
    {
        buildLocation = GetComponent<BuildLocation>();
    }

    private void Awake()
    {
        if (buildLocation == null)
            buildLocation = GetComponent<BuildLocation>();
    }

    private void OnEnable()
    {
        if (listenForSavedBridge)
            LevelCompleteManager.BridgeSavedAtLocation += HandleBridgeSaved;
    }

    private void OnDisable()
    {
        LevelCompleteManager.BridgeSavedAtLocation -= HandleBridgeSaved;
        lessonSequenceRoutine = null;
    }

    /// <summary>
    /// Inspector/UnityEvent-friendly entry point for this exact Build Location.
    /// </summary>
    public void OnBridgeFinishedAtLocation()
    {
        TryShowLesson();
    }

    /// <summary>
    /// Central-manager entry point when the caller knows only the completed contract.
    /// </summary>
    public void OnBridgeFinishedAtLocation(ContractSO completedContract)
    {
        if (buildLocation == null || completedContract == null ||
            buildLocation.activeContract != completedContract)
            return;

        TryShowLesson();
    }

    public bool TryShowLesson()
    {
        List<LessonData> lessonSequence = GetLessonSequence();
        if (!isActiveAndEnabled || lessonSequence.Count == 0 ||
            lessonSequenceRoutine != null ||
            (triggerOnlyOnce && hasTriggered) || LessonUIManager.Instance == null)
            return false;

        hasTriggered = true;
        lessonSequenceRoutine = StartCoroutine(PlayLessonSequence(lessonSequence));
        onLessonTriggered?.Invoke();
        return true;
    }

    private List<LessonData> GetLessonSequence()
    {
        List<LessonData> result = new List<LessonData>();

        // Once at least one list entry is assigned, the ordered list becomes the
        // source of truth. Otherwise the original single-lesson scene field is
        // retained for backward compatibility.
        if (lessons != null)
        {
            foreach (LessonData configuredLesson in lessons)
            {
                if (configuredLesson != null && !result.Contains(configuredLesson))
                    result.Add(configuredLesson);
            }
        }

        if (result.Count == 0 && lesson != null)
            result.Add(lesson);

        return result;
    }

    private IEnumerator PlayLessonSequence(List<LessonData> lessonSequence)
    {
        foreach (LessonData lessonToShow in lessonSequence)
        {
            LessonUIManager manager = LessonUIManager.Instance;
            if (manager == null) break;

            manager.ShowLesson(lessonToShow);

            // Wait until this exact lesson is closed. The next frame delay keeps
            // the close-button event and panel coordinator from overlapping the
            // following lesson's open operation.
            while (manager != null && manager.IsOpen &&
                   manager.CurrentLesson == lessonToShow)
            {
                yield return null;
            }

            yield return null;
        }

        lessonSequenceRoutine = null;
        onLessonSequenceFinished?.Invoke();
    }

    public void ResetTrigger()
    {
        hasTriggered = false;
    }

    private void HandleBridgeSaved(ContractSO completedContract, BuildLocation completedLocation)
    {
        if (buildLocation == null || completedLocation != buildLocation)
            return;

        // The location identity is authoritative. The contract check protects
        // against a stale/misconfigured location reference.
        if (completedContract == null || buildLocation.activeContract != completedContract)
            return;

        TryShowLesson();
    }
}
