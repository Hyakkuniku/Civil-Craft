using System;
using System.Collections;
using UnityEngine;

/// <summary>Owns temporary readers independently of the panels they open.</summary>
[DisallowMultipleComponent]
public sealed class NPCOrderedLearningFlow : MonoBehaviour
{
    [Header("Review Objectives")]
    [Tooltip("Show this owner's Almanac review tutorials in Objectives while they are active.")]
    public bool showReviewsInObjectives;
    [Tooltip("Optional title for the Almanac review objective. Leave blank to use the tutorial's normal title.")]
    public string reviewObjectiveTitle = string.Empty;

    private LessonUIManager lessonManager;
    private LessonData expectedLesson;
    private Action completed;
    private Coroutine reviewRoutine;
    private TutorialSequence reviewSequence;
    private TutorialManager tutorialManager;
    private AlmanacManager reviewBook;
    private bool reviewOpened;
    private bool reviewClosed;
    private PlayerData saveOwner;
    private Action unavailable;
    private string activeReviewId;

    /// <summary>Read-only ownership query for a stage-aware NPC reminder.</summary>
    public bool IsAlmanacReviewPending(string reviewId) => reviewSequence != null &&
        string.Equals(activeReviewId, "StoryReview/" + reviewId, StringComparison.Ordinal) &&
        completed != null && IsAuthorized();

    public bool TryShowLesson(LessonData lesson, Action onCompleted, Action onUnavailable = null)
    {
        if (!isActiveAndEnabled || ProtectedVisit() || lesson == null || LessonUIManager.Instance == null ||
            LessonUIManager.Instance.IsOpen || completed != null) return false;
        lessonManager = LessonUIManager.Instance;
        expectedLesson = lesson;
        completed = onCompleted;
        unavailable = onUnavailable;
        saveOwner = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        lessonManager.LessonClosed += LessonClosed;
        lessonManager.LessonOpened += LessonOpened;
        lessonManager.ShowLesson(lesson);
        if (lessonManager.IsOpen && lessonManager.CurrentLesson == lesson) return true;
        Cancel();
        return false;
    }

    private void LessonClosed(LessonData lesson)
    {
        if (lesson != expectedLesson) return;
        if (!IsAuthorized()) { Fail(); return; }
        Action callback = completed;
        ClearLesson(); // Unsubscribe before running the next ordered step.
        completed = null;
        unavailable = null;
        callback?.Invoke();
    }

    private void LessonOpened(LessonData lesson)
    {
        if (lesson != expectedLesson) Fail();
    }

    public bool TryReviewAlmanac(string reviewId, Action onCompleted, Action onUnavailable = null,
        string reviewSubject = "newly recorded entry")
    {
        if (!isActiveAndEnabled || ProtectedVisit() || string.IsNullOrWhiteSpace(reviewId) || onCompleted == null ||
            AlmanacManager.Instance == null || TutorialManager.Instance == null) return false;
        var data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        if (data == null) return false;
        string lessonId = "StoryReview/" + reviewId;
        // Re-inspection can close the same lesson again while its original book
        // review is still pending. Keep that session and its once-only callback;
        // don't enqueue another tutorial or report it as unavailable.
        if (completed != null)
            return reviewSequence != null && activeReviewId == lessonId && IsAuthorized() &&
                completed == onCompleted && unavailable == onUnavailable;
        if (data?.completedLessons != null && data.completedLessons.Contains(lessonId))
        {
            onCompleted?.Invoke();
            return true;
        }
        completed = onCompleted;
        unavailable = onUnavailable;
        saveOwner = data;
        activeReviewId = lessonId;
        tutorialManager = TutorialManager.Instance;
        reviewBook = AlmanacManager.Instance;
        reviewOpened = reviewClosed = false;
        reviewBook.OnAlmanacClosed += AlmanacClosed;
        GameObject child = new GameObject("Ordered Almanac Review");
        child.transform.SetParent(transform, false);
        reviewSequence = child.AddComponent<TutorialSequence>();
        reviewSequence.lessonName = lessonId;
        reviewSequence.ConfigureObjectiveIdentity(gameObject.scene.path + "|review|" + lessonId);
        reviewSequence.showInObjectives = showReviewsInObjectives;
        reviewSequence.objectiveTitle = reviewObjectiveTitle;
        reviewSequence.tutorialSteps = CreateReviewSteps(reviewBook, reviewSubject);
        reviewSequence.StartTutorial();
        reviewRoutine = StartCoroutine(WaitForReview(reviewBook));
        return true;
    }

    private static TutorialStep[] CreateReviewSteps(AlmanacManager book, string reviewSubject)
    {
        RectTransform bookButton = book != null && book.hudOpenButton != null
            ? book.hudOpenButton.GetComponent<RectTransform>() : null;
        return new[]
        {
            new TutorialStep { message = "Open your <b><#E09500>Almanac</color></b> and review the " +
                    (string.IsNullOrWhiteSpace(reviewSubject) ? "newly recorded entry" : reviewSubject) +
                    ". After reading, close the book to continue.",
                screenPosition = TutorialPosition.Left, showNextButton = false, canSkip = false,
                usePointer = bookButton != null, pointerTarget = bookButton,
                pointerOffset = new Vector2(80f, -80f), pointerRotation = 45f,
                // A click/animation is not a completed review. The ready book
                // and genuine close event remain the only progression gate.
                advanceOnClick = false },
            new TutorialStep { message = "This entry is part of your engineering record. Close the <b><#E09500>Almanac</color></b> to continue.",
                screenPosition = TutorialPosition.Left, showNextButton = false, canSkip = false }
        };
    }

    private IEnumerator WaitForReview(AlmanacManager book)
    {
        while (reviewSequence != null && book != null && tutorialManager != null)
        {
            if (!IsAuthorized()) { reviewRoutine = null; Fail(); yield break; }
            // A newly unlocked tab may temporarily own the tutorial UI. Its
            // walkthrough must not make a genuine book visit disappear.
            if (book.IsOpenAndReady) reviewOpened = true;
            if (tutorialManager.IsPlayingSequence(reviewSequence))
            {
                if (reviewClosed)
                {
                    tutorialManager.CompleteTutorialIfPlaying(reviewSequence.lessonName);
                    Action callback = completed;
                    completed = null;
                    unavailable = null;
                    reviewRoutine = null;
                    ClearReviewBook();
                    Destroy(reviewSequence.gameObject);
                    reviewSequence = null;
                    tutorialManager = null;
                    callback?.Invoke();
                    yield break;
                }
                if (reviewOpened && tutorialManager.CurrentStepIndex == 0)
                    tutorialManager.ShowNextStep();
            }
            yield return null;
        }
        // A missing reader cannot count as a completed review.
        reviewRoutine = null;
        Fail();
    }

    public void Cancel()
    {
        completed = null;
        unavailable = null;
        LessonUIManager reader = lessonManager;
        LessonData lesson = expectedLesson;
        ClearLesson();
        if (reader != null && reader.IsOpen && reader.CurrentLesson == lesson) reader.CloseLesson();
        if (reviewRoutine != null) StopCoroutine(reviewRoutine);
        reviewRoutine = null;
        ClearReviewBook();
        if (tutorialManager != null && reviewSequence != null)
            tutorialManager.CancelTutorialWithoutCompletion(reviewSequence);
        if (reviewSequence != null) Destroy(reviewSequence.gameObject);
        reviewSequence = null;
        tutorialManager = null;
    }

    private void AlmanacClosed()
    {
        // Raised after the animation and input restoration, never merely when
        // Pause/the coordinator temporarily hides the book's panel.
        if (reviewOpened) reviewClosed = true;
    }

    private void ClearReviewBook()
    {
        if (reviewBook != null) reviewBook.OnAlmanacClosed -= AlmanacClosed;
        reviewBook = null;
        activeReviewId = null;
        reviewOpened = reviewClosed = false;
    }

    private void ClearLesson()
    {
        if (lessonManager != null)
        {
            lessonManager.LessonClosed -= LessonClosed;
            lessonManager.LessonOpened -= LessonOpened;
        }
        lessonManager = null;
        expectedLesson = null;
    }

    private bool IsAuthorized() => PlayerDataManager.Instance != null &&
        ReferenceEquals(saveOwner, PlayerDataManager.Instance.CurrentData) &&
        !ProtectedVisit();

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        FusionConnectionManager.Instance.IsGuestSaveProtected;

    private void Fail()
    {
        Action callback = unavailable;
        Cancel();
        callback?.Invoke();
    }

    private void OnDisable() => Cancel();
    private void OnDestroy() => Cancel();
}
