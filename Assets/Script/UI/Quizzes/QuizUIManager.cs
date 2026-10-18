using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>One local, offline quiz session. The owner lives outside the hideable panel.</summary>
[DisallowMultipleComponent]
public sealed class QuizUIManager : MonoBehaviour
{
    private const string PrefabResource = "UI/QuizUI";
    private static QuizUIManager activeManager;
    public QuizView view;

    private QuizData quiz;
    private QuizContext context;
    private UnityEngine.Object owner;
    private Scene ownerScene;
    private Action<QuizOutcome> closed;
    private PlayerDataManager saveManager;
    private PlayerData saveOwner;
    private string fingerprint;
    private readonly HashSet<string> correctIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> assessmentAnswers = new Dictionary<string, int>(StringComparer.Ordinal);
    private bool assessmentSubmitted;
    private int questionIndex = -1;
    private int selectedIndex = -1;
    private bool questionAnswered;
    private bool sessionActive;
    private bool savePending;
    private bool completionPending;
    private bool acknowledgeUnsavedLeave;
    private bool committing;
    private float nextContentCheck;
    private InputManager lockedInput;
    private PlayerInteract lockedInteraction;
    private bool movementBefore, lookBefore, interactionBefore;
    private GameObject selectionBefore;
    private QuizNPCPresentation npcPresentation;
    private int presentedQuestionIndex = -1;

    public GameObject Panel => view != null ? view.Panel : null;
    public bool HasSession => sessionActive;
    public static bool IsAnyQuizActive => activeManager != null && activeManager.sessionActive;

    private void Awake()
    {
        if (view == null) view = GetComponent<QuizView>();
        BindView(true);
        if (Panel != null) Panel.SetActive(false);
    }

    private void OnDisable()
    {
        if (sessionActive) CloseSession(QuizOutcome.Cancelled);
        else EndNpcPresentation();
    }

    private void OnDestroy()
    {
        if (sessionActive) CloseSession(QuizOutcome.Cancelled);
        else EndNpcPresentation();
        BindView(false);
        if (activeManager == this) activeManager = null;
    }

    private void Update()
    {
        if (!sessionActive) return;
        bool invalidOwner = owner == null || !ownerScene.IsValid() || !ownerScene.isLoaded ||
            (owner is Behaviour behaviour && !behaviour.isActiveAndEnabled);
        bool changedSave = saveManager == null || PlayerDataManager.Instance != saveManager ||
            !ReferenceEquals(saveOwner, saveManager.CurrentData) || IsProtectedVisit();
        bool changedQuiz = quiz == null;
        if (!changedQuiz && Time.unscaledTime >= nextContentCheck)
        {
            nextContentCheck = Time.unscaledTime + 1f;
            changedQuiz = !string.Equals(fingerprint, quiz.ContentFingerprint, StringComparison.Ordinal);
        }
        if (invalidOwner || changedSave || changedQuiz)
        {
            CloseSession(changedQuiz ? QuizOutcome.Unavailable : QuizOutcome.Cancelled);
            return;
        }

        // A locally spawned/replaced player must not acquire controls behind an
        // already-open quiz. Never touch disabled remote-input components.
        if (lockedInput == null) LockPlayerControls();
        UpdateNpcPresentation();

        // Escape is routed by PauseManager when it exists, avoiding two handlers
        // consuming the same key. A standalone quiz still supports desktop/Android Back.
        if (PauseManager.Instance == null && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
            TryHandleBack();
        if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
            TryHandleBack();
    }

    /// <summary>False never invokes onClosed. True owns exactly one subsequent result callback.</summary>
    public static bool TryOpen(QuizData data, QuizContext identity, UnityEngine.Object sessionOwner,
        Action<QuizOutcome> onClosed)
    {
        if (!Application.isPlaying || data == null || identity == null || !identity.IsValid ||
            sessionOwner == null || IsAnyQuizActive || IsProtectedVisit() ||
            PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
            DialogueManager.IsAnyDialogueActive ||
            (GameManager.Instance != null && (GameManager.Instance.IsTransitioning ||
                GameManager.Instance.CurrentState != GameManager.GameState.Normal))) return false;
        if (!data.TryValidate(out string error))
        {
            Debug.LogWarning("[NPC Quiz] " + error, sessionOwner);
            return false;
        }
        GameObject ownerObject = sessionOwner as GameObject;
        if (sessionOwner is Component component) ownerObject = component.gameObject;
        if (ownerObject == null || !ownerObject.scene.IsValid() || !ownerObject.scene.isLoaded) return false;
        if (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel) return false;

        QuizUIManager manager = null;
        foreach (QuizUIManager candidate in FindObjectsOfType<QuizUIManager>(true))
            if (candidate.gameObject.scene == ownerObject.scene && candidate.isActiveAndEnabled &&
                !candidate.sessionActive) { manager = candidate; break; }
        if (manager == null)
        {
            GameObject prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null)
            {
                Debug.LogWarning("[NPC Quiz] Quiz UI is unavailable. Run Tools/Civil Craft/Quizzes/Create and Validate Quiz UI.", sessionOwner);
                return false;
            }
            GameObject instance = Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(instance, ownerObject.scene);
            manager = instance.GetComponent<QuizUIManager>();
            if (manager == null) { Destroy(instance); return false; }
        }
        if (manager.view == null || !manager.view.IsReady) return false;
        return manager.Open(data, identity, sessionOwner, ownerObject.scene, onClosed);
    }

    public static void CancelOwnedSession(UnityEngine.Object sessionOwner)
    {
        if (activeManager != null && activeManager.sessionActive && activeManager.owner == sessionOwner)
            activeManager.CloseSession(QuizOutcome.Cancelled);
    }

    /// <summary>Only the top quiz consumes Back; nested Pause/Settings keep their own behavior.</summary>
    public static bool TryHandleBack()
    {
        if (activeManager == null || !activeManager.CanInteract()) return false;
        // Assessment is a committed attempt; only its owner/scene teardown can cancel.
        // Pause still owns Back when it is above the quiz.
        if (activeManager.quiz != null && activeManager.quiz.assessmentMode) return true;
        activeManager.Leave();
        return true;
    }

    private bool Open(QuizData data, QuizContext identity, UnityEngine.Object sessionOwner,
        Scene scene, Action<QuizOutcome> onClosed)
    {
        quiz = data;
        // Copy caller-owned identity so later Inspector edits cannot change a save key mid-session.
        context = new QuizContext { progressionId = identity.progressionId, phaseId = identity.phaseId, stepId = identity.stepId };
        owner = sessionOwner;
        ownerScene = scene;
        closed = onClosed;
        saveManager = PlayerDataManager.Instance;
        saveOwner = saveManager.CurrentData;
        fingerprint = data.ContentFingerprint;
        correctIds.Clear();
        if (!saveManager.TryGetQuizProgress(data, context, out QuizProgressSaveData progress) || progress == null)
        {
            quiz = null; owner = null; closed = null; saveManager = null; saveOwner = null;
            return false;
        }
        foreach (string id in progress.correctQuestionIds) correctIds.Add(id);
        assessmentAnswers.Clear();
        foreach (QuizAnswerSaveData answer in progress.answers)
            if (answer != null) assessmentAnswers[answer.questionId] = answer.choiceIndex;
        assessmentSubmitted = progress.submitted;
        sessionActive = true;
        activeManager = this;
        savePending = completionPending = acknowledgeUnsavedLeave = committing = false;
        nextContentCheck = Time.unscaledTime + 1f;
        selectionBefore = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        view.Reset();
        BeginNpcPresentation();
        // Recording may restore its former cursor state while yielding the
        // camera. Apply the quiz's input/cursor lock afterward, in this same frame.
        LockPlayerControls();
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(Panel);
        else Panel.SetActive(true);
        if (quiz.assessmentMode)
        {
            if (assessmentSubmitted) ShowAssessmentResult();
            else ShowAssessmentQuestion(0);
        }
        else ShowFirstUnresolved();
        PauseManager.Instance?.RefreshPauseAvailability();
        return true;
    }

    private bool CanInteract()
    {
        return sessionActive && !committing && view != null && Panel != null && Panel.activeInHierarchy &&
            (PauseManager.Instance == null || !PauseManager.Instance.isPaused) &&
            (UIPanelCoordinator.Instance == null || UIPanelCoordinator.Instance.IsOpen(Panel));
    }

    private void Select(int index)
    {
        if (!CanInteract() || questionAnswered || savePending || questionIndex < 0 ||
            index < 0 || index >= quiz.questions[questionIndex].choices.Count) return;
        selectedIndex = index;
        acknowledgeUnsavedLeave = false;
        if (quiz.assessmentMode)
        {
            if (assessmentSubmitted) return;
            assessmentAnswers[quiz.questions[questionIndex].questionId.Trim()] = index;
            savePending = true;
            if (CommitProgress(false)) ShowAssessmentQuestion(questionIndex);
            return;
        }
        view.SetSelection(index);
    }

    private void Submit()
    {
        if (quiz != null && quiz.assessmentMode)
        {
            if (!CanInteract() || assessmentSubmitted || savePending ||
                !QuizProgressUtility.HasAllAssessmentAnswers(quiz, assessmentAnswers)) return;
            assessmentSubmitted = true;
            savePending = true;
            if (CommitProgress(false)) ShowAssessmentResult();
            return;
        }
        if (!CanInteract() || questionAnswered || savePending || questionIndex < 0 || selectedIndex < 0) return;
        QuizQuestion question = quiz.questions[questionIndex];
        if (selectedIndex != question.correctChoiceIndex)
        {
            view.ShowFeedback(false, "Try again. Choose another answer.");
            return;
        }
        questionAnswered = true;
        correctIds.Add(question.questionId.Trim());
        savePending = true;
        view.ShowFeedback(true, question.feedback);
        CommitProgress(false);
    }

    private void Next()
    {
        if (quiz != null && quiz.assessmentMode)
        {
            if (CanInteract() && !assessmentSubmitted && !savePending && questionIndex + 1 < quiz.QuestionCount)
                ShowAssessmentQuestion(questionIndex + 1);
            return;
        }
        if (!CanInteract() || !questionAnswered || savePending || completionPending) return;
        ShowFirstUnresolved();
    }

    private void Previous()
    {
        if (CanInteract() && quiz != null && quiz.assessmentMode && !assessmentSubmitted && !savePending && questionIndex > 0)
            ShowAssessmentQuestion(questionIndex - 1);
    }

    private void ShowAssessmentQuestion(int index)
    {
        questionIndex = index;
        questionAnswered = false;
        selectedIndex = assessmentAnswers.TryGetValue(quiz.questions[index].questionId.Trim(), out int selected) ? selected : -1;
        view.ShowAssessmentQuestion(quiz, index, selectedIndex, assessmentAnswers.Count);
        PresentNpcQuestion();
    }

    private void ShowAssessmentResult()
    {
        questionIndex = selectedIndex = -1;
        questionAnswered = true;
        npcPresentation?.SetSpeaking(false);
        view.ShowAssessmentResult(quiz, QuizProgressUtility.AssessmentScore(quiz, assessmentAnswers), assessmentAnswers);
    }

    private void ShowFirstUnresolved()
    {
        selectedIndex = -1;
        questionAnswered = false;
        questionIndex = QuizProgressUtility.FirstUnresolvedQuestionIndex(quiz, correctIds);
        if (questionIndex >= quiz.QuestionCount)
        {
            questionIndex = -1;
            npcPresentation?.SetSpeaking(false);
            view.ShowCompletion(quiz);
        }
        else
        {
            view.ShowQuestion(quiz, questionIndex, correctIds);
            PresentNpcQuestion();
        }
    }

    private void Finish()
    {
        if (quiz != null && quiz.assessmentMode)
        {
            if (!CanInteract() || !assessmentSubmitted || savePending || completionPending) return;
            completionPending = savePending = true;
            if (CommitProgress(true)) CloseSession(QuizOutcome.Completed);
            return;
        }
        if (!CanInteract() || questionIndex >= 0 || savePending || completionPending ||
            !QuizProgressUtility.AreAllQuestionsCorrect(quiz, correctIds)) return;
        completionPending = true;
        savePending = true;
        if (CommitProgress(true)) CloseSession(QuizOutcome.Completed);
    }

    private void RetrySave()
    {
        if (!CanInteract() || !savePending) return;
        bool finishAfterSave = completionPending;
        if (!CommitProgress(finishAfterSave)) return;
        if (finishAfterSave) CloseSession(QuizOutcome.Completed);
        else if (quiz.assessmentMode)
        {
            if (assessmentSubmitted) ShowAssessmentResult();
            else ShowAssessmentQuestion(questionIndex);
        }
        else if (questionIndex >= 0)
        {
            QuizQuestion question = quiz.questions[questionIndex];
            view.ShowFeedback(true, question.feedback);
        }
    }

    private bool CommitProgress(bool completed)
    {
        if (saveManager == null || PlayerDataManager.Instance != saveManager ||
            !ReferenceEquals(saveOwner, saveManager.CurrentData) || IsProtectedVisit()) return false;
        committing = true;
        view.SetBusy(true);
        bool saved;
        string error;
        try
        {
            saved = quiz.assessmentMode ?
                saveManager.TrySaveQuizAssessment(quiz, context, assessmentAnswers, assessmentSubmitted, completed, out error) :
                saveManager.TrySaveQuizProgress(quiz, context, correctIds, completed, out error);
        }
        finally { committing = false; view.SetBusy(false); }
        savePending = !saved;
        if (!saved)
            view.ShowSaveError(string.IsNullOrWhiteSpace(error) ? "Could not save progress. Retry Save or leave safely." : error);
        else acknowledgeUnsavedLeave = false;
        return saved;
    }

    private void Leave()
    {
        if (!CanInteract()) return;
        if (quiz != null && quiz.assessmentMode) return;
        if (savePending && !acknowledgeUnsavedLeave && !CommitProgress(false))
        {
            acknowledgeUnsavedLeave = true;
            view.ShowSaveError("Progress could not be saved. Retry Save, or press Leave Quiz again to leave with only your previously saved answers.");
            return;
        }
        CloseSession(QuizOutcome.Cancelled);
    }

    private void Pause()
    {
        if (!CanInteract()) return;
        npcPresentation?.SetSpeaking(false);
        PauseManager.Instance?.PauseGame();
    }

    private void BeginNpcPresentation()
    {
        presentedQuestionIndex = -1;
        if (view == null || view.layout == null) return;
        // Resolve the split layout even while the coordinator still hides the panel.
        // A non-NPC owner or unsafe camera keeps the ordinary fullscreen quiz.
        view.SetNpcPresentation(true, string.Empty);
        if (npcPresentation == null)
        {
            npcPresentation = GetComponent<QuizNPCPresentation>();
            if (npcPresentation == null) npcPresentation = gameObject.AddComponent<QuizNPCPresentation>();
        }
        bool available = npcPresentation.Begin(owner, view.layout.NpcScreenViewport);
        if (!available) npcPresentation.End(true);
        view.SetNpcPresentation(available, available ? npcPresentation.SpeakerName : string.Empty);
    }

    private void UpdateNpcPresentation()
    {
        if (npcPresentation == null || view == null) return;
        if (!npcPresentation.IsActive)
        {
            if (view.HasNpcPresentation) view.SetNpcPresentation(false, string.Empty);
            return;
        }
        if (view.layout != null) npcPresentation.SetViewport(view.layout.NpcScreenViewport);
        npcPresentation.SetSpeaking(questionIndex >= 0 && !questionAnswered && Panel != null &&
            Panel.activeInHierarchy && (PauseManager.Instance == null || !PauseManager.Instance.isPaused));
    }

    private void PresentNpcQuestion()
    {
        if (npcPresentation == null || !npcPresentation.IsActive || questionIndex < 0 ||
            presentedQuestionIndex == questionIndex) return;
        presentedQuestionIndex = questionIndex;
        npcPresentation.SetSpeaking(true);
        npcPresentation.PresentQuestion();
    }

    private void EndNpcPresentation()
    {
        // Release the borrowed camera/animation before any follow-up dialogue can
        // take ownership, including cancellation and nested Pause teardown.
        if (npcPresentation != null) npcPresentation.End(true);
        if (view != null) view.SetNpcPresentation(false, string.Empty);
        presentedQuestionIndex = -1;
    }

    private void CloseSession(QuizOutcome result)
    {
        if (!sessionActive) return;
        // Forced teardown (NPC travel/unload) may arrive with Pause above the
        // quiz. Resolve that nested pause while the quiz still owns its input
        // lock, so closing the modal stack cannot leave time frozen afterward.
        PauseManager.Instance?.ResumeForQuizTeardown(Panel);
        sessionActive = false;
        if (activeManager == this) activeManager = null;
        Action<QuizOutcome> callback = closed;
        closed = null;
        if (Panel != null)
        {
            if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(Panel);
            else Panel.SetActive(false);
        }
        EndNpcPresentation();
        RestorePlayerControls();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(selectionBefore != null && selectionBefore.activeInHierarchy ? selectionBefore : null);
        selectionBefore = null;
        quiz = null;
        owner = null;
        saveManager = null;
        saveOwner = null;
        correctIds.Clear();
        assessmentAnswers.Clear();
        questionIndex = selectedIndex = -1;
        PauseManager.Instance?.RefreshPauseAvailability();
        callback?.Invoke(result);
    }

    private void LockPlayerControls()
    {
        lockedInput = null;
        foreach (InputManager candidate in FindObjectsOfType<InputManager>())
            if (candidate.isActiveAndEnabled && candidate.gameObject.scene == ownerScene)
            { lockedInput = candidate; break; }
        lockedInteraction = lockedInput != null ? lockedInput.GetComponent<PlayerInteract>() : null;
        if (lockedInput != null)
        {
            movementBefore = lockedInput.IsPlayerInputEnabled;
            lookBefore = lockedInput.IsLookInputEnabled;
            lockedInput.SetPlayerInputEnable(false);
            lockedInput.SetLookEnabled(false);
        }
        if (lockedInteraction != null)
        {
            interactionBefore = lockedInteraction.enabled;
            lockedInteraction.enabled = false;
        }
    }

    private void RestorePlayerControls()
    {
        bool mayRestore = !IsAnyQuizActive && !DialogueManager.IsAnyDialogueActive &&
            (PauseManager.Instance == null || !PauseManager.Instance.isPaused) &&
            (GameManager.Instance == null || (!GameManager.Instance.IsTransitioning &&
                GameManager.Instance.CurrentState == GameManager.GameState.Normal)) &&
            (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel);
        if (lockedInput != null && lockedInput.gameObject.scene == ownerScene && ownerScene.isLoaded && mayRestore)
        {
            lockedInput.SetPlayerInputEnable(movementBefore);
            lockedInput.SetLookEnabled(lookBefore);
        }
        if (lockedInteraction != null && lockedInteraction.gameObject.scene == ownerScene && ownerScene.isLoaded && mayRestore)
            lockedInteraction.enabled = interactionBefore;
        lockedInput = null;
        lockedInteraction = null;
    }

    private static bool IsProtectedVisit() => FusionConnectionManager.Instance != null &&
        FusionConnectionManager.Instance.IsGuestSaveProtected;

    private void BindView(bool bind)
    {
        if (view == null) return;
        if (bind)
        {
            view.ChoiceSelected += Select;
            view.SubmitRequested += Submit;
            view.NextRequested += Next;
            view.PreviousRequested += Previous;
            view.FinishRequested += Finish;
            view.LeaveRequested += Leave;
            view.RetrySaveRequested += RetrySave;
            view.PauseRequested += Pause;
        }
        else
        {
            view.ChoiceSelected -= Select;
            view.SubmitRequested -= Submit;
            view.NextRequested -= Next;
            view.PreviousRequested -= Previous;
            view.FinishRequested -= Finish;
            view.LeaveRequested -= Leave;
            view.RetrySaveRequested -= RetrySave;
            view.PauseRequested -= Pause;
        }
    }
}
