using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Reed's interrupted introduction stays retryable without adding or reordering saved phases.</summary>
[DisallowMultipleComponent]
public sealed class ReedStoryIntroduction : MonoBehaviour
{
    [SerializeField] private NPCProgressionManager npc;
    [SerializeField] private NPCContractGiver contractGiver;
    [SerializeField] private CinematicDirector cinematic;
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private Animator npcAnimator;
    [SerializeField] private LessonData bendingLesson;
    [SerializeField] private Dialogue situationDialogue;
    [SerializeField] private Dialogue bendingDialogue;

    public bool IsRunning { get; private set; }

    private Coroutine routine;
    private PlayerData saveOwner;
    private NPCProgressionPhase sourcePhase;
    private NPCOrderedLearningFlow learning;
    private Action ownedDialogue;
    private UnityAction cinematicFinished;
    private bool ownsInteractionLock;
    private bool ownsLesson;

    public void BeginIntroduction()
    {
        if (IsRunning || !CanBegin() || cinematic == null || !cinematic.isActiveAndEnabled ||
            dialogueManager == null || !dialogueManager.isActiveAndEnabled || bendingLesson == null ||
            !Usable(situationDialogue) || !Usable(bendingDialogue)) return;

        saveOwner = PlayerDataManager.Instance.CurrentData;
        sourcePhase = npc.CurrentPhase;
        IsRunning = true;
        ownsInteractionLock = true;
        contractGiver.SetProgressionInteractionLocked(true, "Reed is explaining the crossing...");
        routine = StartCoroutine(RunIntroduction());
    }

    private IEnumerator RunIntroduction()
    {
        // The previous dialogue callback fires before its closing animation ends.
        yield return WaitForReady();
        if (!Authorized()) { StopFlow(false); yield break; }
        // Never attach our completion to a cinematic owned by another caller.
        if (cinematic == null || !cinematic.isActiveAndEnabled || cinematic.IsPlaying)
        { StopFlow(false); yield break; }

        bool movieCompleted = false;
        cinematicFinished = () => { movieCompleted = true; };
        cinematic.OnCinematicFinished.AddListener(cinematicFinished);
        cinematic.PlayContractOfferCinematic();
        while (!movieCompleted)
        {
            if (!Authorized() || cinematic == null || !cinematic.isActiveAndEnabled || !cinematic.IsPlaying)
            { StopFlow(false); yield break; }
            yield return null;
        }
        DetachCinematic();
        if (!Authorized()) { StopFlow(false); yield break; }

        yield return ShowDialogue(situationDialogue);
        if (!Authorized()) { StopFlow(false); yield break; }
        yield return ShowDialogue(bendingDialogue);
        if (!Authorized()) { StopFlow(false); yield break; }
        yield return WaitForReady();
        if (!Authorized()) { StopFlow(false); yield break; }

        learning = GetComponent<NPCOrderedLearningFlow>();
        if (learning == null) learning = gameObject.AddComponent<NPCOrderedLearningFlow>();
        bool lessonCompleted = false, unavailable = false;
        ownsLesson = true;
        bool opened = learning.TryShowLesson(bendingLesson,
            () => { ownsLesson = false; lessonCompleted = true; },
            () => { ownsLesson = false; unavailable = true; });
        if (!opened)
        {
            // A rejected request never owns another caller's reader.
            ownsLesson = false;
            StopFlow(false);
            yield break;
        }
        while (!lessonCompleted)
        {
            if (!Authorized() || unavailable || learning == null || !learning.isActiveAndEnabled)
            { StopFlow(false); yield break; }
            yield return null;
        }
        if (!Authorized()) { StopFlow(false); yield break; }
        NPCProgressionManager destination = npc;
        // Release our lock before the new phase can install its own lock.
        StopFlow(false);
        destination.MoveToPhaseById("Reed Contract Give");
    }

    private IEnumerator ShowDialogue(Dialogue dialogue)
    {
        yield return WaitForReady();
        if (!Authorized() || dialogueManager == null || !dialogueManager.isActiveAndEnabled)
        { StopFlow(false); yield break; }
        bool completed = false;
        ownedDialogue = () => { completed = true; };
        dialogueManager.StartDialogue(dialogue, ownedDialogue, npcAnimator, npc.transform);
        while (!completed)
        {
            if (!Authorized() || dialogueManager == null || !dialogueManager.isActiveAndEnabled ||
                !DialogueManager.IsAnyDialogueActive)
            { StopFlow(false); yield break; }
            yield return null;
        }
        ownedDialogue = null;
    }

    private IEnumerator WaitForReady()
    {
        while (Authorized() && !ReadyForPresentation()) yield return null;
    }

    private bool CanBegin() => isActiveAndEnabled && gameObject.scene.IsValid() && gameObject.scene.isLoaded &&
        npc != null && npc.isActiveAndEnabled && npc.gameObject.scene == gameObject.scene &&
        npc.ProgressionSaveId == "ReedNPC" && !npc.IsTravelling &&
        npc.CurrentPhase != null && npc.CurrentPhase.phaseId == "ExplainSituation" &&
        contractGiver != null && contractGiver.gameObject == npc.gameObject &&
        PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null && !ProtectedVisit();

    private bool Authorized() => IsRunning && CanBegin() &&
        ReferenceEquals(sourcePhase, npc.CurrentPhase) &&
        ReferenceEquals(saveOwner, PlayerDataManager.Instance.CurrentData);

    private static bool ReadyForPresentation() => Time.timeScale > 0f &&
        !DialogueManager.IsAnyDialogueVisible && !QuizUIManager.IsAnyQuizActive &&
        (LessonUIManager.Instance == null || !LessonUIManager.Instance.IsOpen) &&
        (PauseManager.Instance == null || !PauseManager.Instance.isPaused) &&
        (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel) &&
        (GameManager.Instance == null || (!GameManager.Instance.IsTransitioning &&
            GameManager.Instance.CurrentState == GameManager.GameState.Normal));

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        (FusionConnectionManager.Instance.IsGuestInHostWorld || FusionConnectionManager.Instance.IsGuestSaveProtected);

    private static bool Usable(Dialogue dialogue) => dialogue != null && dialogue.sentences != null &&
        dialogue.sentences.Length > 0 && Array.Exists(dialogue.sentences, sentence => !string.IsNullOrWhiteSpace(sentence));

    private void DetachCinematic()
    {
        if (cinematic != null && cinematicFinished != null)
            cinematic.OnCinematicFinished.RemoveListener(cinematicFinished);
        cinematicFinished = null;
    }

    private void StopFlow(bool stopRoutine)
    {
        IsRunning = false; // Invalidate callbacks before closing an owned reader.
        DetachCinematic();
        Action callback = ownedDialogue;
        ownedDialogue = null;
        if (dialogueManager != null && callback != null) dialogueManager.CancelDialogueIfOwned(callback);
        bool closeLesson = ownsLesson;
        ownsLesson = false;
        if (closeLesson && learning != null) learning.Cancel();
        if (ownsInteractionLock && contractGiver != null && npc != null &&
            ReferenceEquals(sourcePhase, npc.CurrentPhase))
            contractGiver.SetProgressionInteractionLocked(false);
        ownsInteractionLock = false;
        sourcePhase = null;
        saveOwner = null;
        Coroutine active = routine;
        routine = null;
        if (stopRoutine && active != null) StopCoroutine(active);
    }

    private void OnDisable() => StopFlow(true);
    private void OnDestroy() => StopFlow(true);
}
