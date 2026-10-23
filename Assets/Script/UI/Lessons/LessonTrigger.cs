using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class LessonTrigger : MonoBehaviour
{
    public enum TriggerMode
    {
        ReplaceExistingInteraction,
        AfterVehicleInspectionCloses,
        ManualOnly
    }

    [Header("Lesson")]
    [SerializeField] private LessonData lesson;
    [Tooltip("Choose whether this lesson replaces an interaction, waits for a vehicle inspection to close, or is called manually.")]
    [SerializeField] private TriggerMode triggerMode = TriggerMode.ReplaceExistingInteraction;
    [SerializeField] private bool triggerOnlyOnce;

    [Header("Sequenced Vehicle Interaction")]
    [Tooltip("Optional. When omitted, the LiveLoadVehicle on this GameObject is used.")]
    [SerializeField] private LiveLoadVehicle liveLoadVehicle;

    [Header("Optional Events")]
    [SerializeField] private UnityEvent onLessonTriggered;

    [Header("One-time NPC Progression After Lesson Closes")]
    [SerializeField] private NPCProgressionManager advanceNPC;
    [SerializeField] private int requiredNPCPhaseIndex = 1;
    [SerializeField] private int nextNPCPhaseIndex = 2;
    [Tooltip("Unique save key. Keeps this close action one-time across scene reloads.")]
    [SerializeField] private string completionSaveKey;
    [Tooltip("Wait for an Almanac open/close review after inspection before the NPC travels. Optional; existing triggers retain their original behavior.")]
    [SerializeField] private bool requireAlmanacReviewBeforeAdvance;
    [Tooltip("Optional NPC reminder after the inspected lesson closes. Use {PlayerName} and {LessonTitle}; empty entries use the default engineering-record reminder.")]
    [TextArea(2, 5)]
    [SerializeField] private string[] almanacReviewReminder;
    private static readonly HashSet<LessonTrigger> activeTriggers = new HashSet<LessonTrigger>();
    private NPCOrderedLearningFlow reviewFlow;
    private LessonUIManager pendingLessonManager;
    private PlayerData pendingLessonOwner;
    private PlayerData armedOwner;
    private PlayerData completedOwner;
    private PlayerData qualifiedInspectionOwner;
    private bool closeActionCompleted;
    private bool npcAdvanceArmed;
    private bool pendingAuthorizedInspection;

    // Wire only from the NPC's instruction dialogue-finished event, not arrival
    // or inspection itself. Persist so reloading after the prompt remains valid.
    public void ArmNPCAdvance()
    {
        if (ProtectedVisit() || advanceNPC == null || advanceNPC.IsTravelling ||
            advanceNPC.CurrentPhaseIndex != requiredNPCPhaseIndex) return;
        var manager = PlayerDataManager.Instance;
        var data = manager != null ? manager.CurrentData : null;
        npcAdvanceArmed = true;
        armedOwner = data;
        if (data == null || string.IsNullOrWhiteSpace(completionSaveKey)) return;
        if (data.armedLessonCloseActions == null)
            data.armedLessonCloseActions = new System.Collections.Generic.List<string>();
        if (!data.armedLessonCloseActions.Contains(completionSaveKey.Trim()))
        {
            data.armedLessonCloseActions.Add(completionSaveKey.Trim());
            manager.SaveGame();
        }
    }

    private bool hasTriggered;

    public LessonData Lesson => lesson;
    public TriggerMode Mode => triggerMode;
    public bool ReplaceExistingInteraction =>
        triggerMode == TriggerMode.ReplaceExistingInteraction;

    private void OnEnable()
    {
        activeTriggers.Add(this);
        BindVehicleCloseEvent();
    }

    private void OnDisable()
    {
        activeTriggers.Remove(this);
        UnbindVehicleCloseEvent();
        CancelPendingLesson();
        qualifiedInspectionOwner = null;
        if (reviewFlow != null) reviewFlow.Cancel();
    }

    private void OnDestroy()
    {
        activeTriggers.Remove(this);
        CancelPendingLesson();
        qualifiedInspectionOwner = null;
        if (reviewFlow != null) reviewFlow.Cancel();
    }

    private void LateUpdate()
    {
        // Profile changes have no shared event. Release only cached ownership;
        // this does not search the scene, open UI, or touch persisted progress.
        var owner = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        bool protectedVisit = ProtectedVisit();
        if (pendingLessonOwner != null && (protectedVisit || !ReferenceEquals(pendingLessonOwner, owner)))
            CancelPendingLesson();
        if (qualifiedInspectionOwner != null && (protectedVisit || !ReferenceEquals(qualifiedInspectionOwner, owner)))
        {
            qualifiedInspectionOwner = null;
            if (reviewFlow != null) reviewFlow.Cancel();
        }
        if (npcAdvanceArmed && (protectedVisit || !ReferenceEquals(armedOwner, owner)))
        {
            npcAdvanceArmed = false;
            armedOwner = null;
        }
        if (closeActionCompleted && (protectedVisit || !ReferenceEquals(completedOwner, owner)))
        {
            closeActionCompleted = false;
            completedOwner = null;
        }
    }

    /// <summary>Finds a qualified, closed inspection awaiting a real book review. Never opens UI or advances an NPC.</summary>
    public static bool TryGetPendingAlmanacReview(NPCProgressionManager npc, out LessonTrigger trigger)
    {
        trigger = null;
        if (npc == null || ProtectedVisit()) return false;
        foreach (LessonTrigger candidate in activeTriggers)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.advanceNPC != npc ||
                !candidate.IsPendingAlmanacReview()) continue;
            trigger = candidate;
            return true;
        }
        return false;
    }

    public string[] GetAlmanacReviewReminder()
    {
        string title = lesson != null && !string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Title : "newly recorded lesson";
        var lines = new List<string>();
        if (almanacReviewReminder != null)
            foreach (string line in almanacReviewReminder)
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Replace("{LessonTitle}", title));
        if (lines.Count == 0)
        {
            lines.Add("Good work, {PlayerName}. That inspection gave us important information before we build.");
            lines.Add("Your Almanac is an engineer's field record. It keeps the lessons you will use when designing a bridge.");
            lines.Add("Review the " + title + " entry before we head to the construction area. Understanding the load comes before choosing how to support it.");
        }
        return lines.ToArray(); // Callers cannot mutate the authored reminder array.
    }

    public bool TryResumeAlmanacReview()
    {
        if (!IsPendingAlmanacReview()) return false;
        if (reviewFlow == null)
            reviewFlow = GetComponent<NPCOrderedLearningFlow>() ?? gameObject.AddComponent<NPCOrderedLearningFlow>();
        if (reviewFlow.IsAlmanacReviewPending(AlmanacReviewId)) return true;
        return reviewFlow.TryReviewAlmanac(AlmanacReviewId, CompleteNPCAdvance,
            reviewSubject: lesson != null && !string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Title + " lesson" : "newly recorded lesson");
    }

    private string AlmanacReviewId => advanceNPC.ProgressionSaveId + "/LiveLoad/inspection-review";
    private string InspectionProofId => string.IsNullOrWhiteSpace(completionSaveKey) ? null : "StoryInspection/" + completionSaveKey.Trim();

    private bool IsPendingAlmanacReview()
    {
        if (!isActiveAndEnabled || ProtectedVisit() || !requireAlmanacReviewBeforeAdvance || !NeedsNPCAdvance()) return false;
        var data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        return data != null && ((InspectionProofId != null && data.completedLessons != null &&
                data.completedLessons.Contains(InspectionProofId)) ||
            ReferenceEquals(qualifiedInspectionOwner, data));
    }

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        FusionConnectionManager.Instance.IsGuestSaveProtected;

    public void ShowLesson()
    {
        TryShowLesson();
    }

    public bool TryShowLesson()
    {
        if (!isActiveAndEnabled || ProtectedVisit() || lesson == null ||
            (triggerOnlyOnce && hasTriggered && !NeedsNPCAdvance()) || LessonUIManager.Instance == null)
            return false;

        LessonUIManager manager = LessonUIManager.Instance;
        // Do not steal another open lesson's close callback.
        if (manager.IsOpen) return false;
        CancelPendingLesson();
        pendingAuthorizedInspection = NeedsNPCAdvance();
        pendingLessonOwner = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        pendingLessonManager = manager;
        manager.LessonOpened += HandleLessonOpened;
        manager.LessonClosed += HandleLessonClosed;
        manager.ShowLesson(lesson);
        if (!manager.IsOpen || manager.CurrentLesson != lesson)
        {
            CancelPendingLesson();
            return false;
        }
        hasTriggered = true;
        onLessonTriggered?.Invoke();
        return true;
    }

    public void ResetTrigger()
    {
        hasTriggered = false;
    }

    private bool NeedsNPCAdvance()
    {
        var data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        if (ProtectedVisit() || advanceNPC == null || (closeActionCompleted && ReferenceEquals(completedOwner, data)) || advanceNPC.IsTravelling ||
            advanceNPC.CurrentPhaseIndex != requiredNPCPhaseIndex ||
            nextNPCPhaseIndex <= requiredNPCPhaseIndex) return false;
        bool armed = (npcAdvanceArmed && ReferenceEquals(armedOwner, data)) || (data != null && !string.IsNullOrWhiteSpace(completionSaveKey) &&
            data.armedLessonCloseActions != null && data.armedLessonCloseActions.Contains(completionSaveKey.Trim()));
        if (!armed) return false;
        return data == null || string.IsNullOrWhiteSpace(completionSaveKey) ||
            data.completedLessonCloseActions == null ||
            !data.completedLessonCloseActions.Contains(completionSaveKey.Trim());
    }

    private void HandleLessonOpened(LessonData opened)
    {
        // Replacement by another lesson cancels ownership; an Almanac close
        // must never accidentally finish this cart interaction.
        if (opened != lesson) CancelPendingLesson();
    }

    private void HandleLessonClosed(LessonData closed)
    {
        bool authorized = pendingAuthorizedInspection;
        PlayerData owner = pendingLessonOwner;
        CancelPendingLesson();
        if (!authorized || ProtectedVisit() || closed != lesson || !NeedsNPCAdvance() ||
            !ReferenceEquals(owner, PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null)) return;
        if (requireAlmanacReviewBeforeAdvance)
        {
            if (!TryRecordInspectionProof(owner)) return;
            qualifiedInspectionOwner = owner;
            if (!TryResumeAlmanacReview())
                Debug.LogWarning("[LessonTrigger] Almanac review is unavailable. Inspect the vehicle again to retry; NPC progress is unchanged.", this);
            return;
        }
        CompleteNPCAdvance();
    }

    private void CompleteNPCAdvance()
    {
        if (!NeedsNPCAdvance()) return;
        if (requireAlmanacReviewBeforeAdvance && !IsPendingAlmanacReview()) return;
        closeActionCompleted = true; // Set before dispatch to prevent re-entry.
        var manager = PlayerDataManager.Instance;
        var data = manager != null ? manager.CurrentData : null;
        completedOwner = data;
        if (data != null && !string.IsNullOrWhiteSpace(completionSaveKey))
        {
            if (data.completedLessonCloseActions == null)
                data.completedLessonCloseActions = new System.Collections.Generic.List<string>();
            data.completedLessonCloseActions.Add(completionSaveKey.Trim());
        }
        advanceNPC.MoveToPhase(nextNPCPhaseIndex);
        // MoveToPhase records its destination synchronously before yielding.
        // Save the destination and one-time marker together.
        if (manager != null && data != null) manager.SaveGame();
    }

    private bool TryRecordInspectionProof(PlayerData owner)
    {
        var manager = PlayerDataManager.Instance;
        string proofId = InspectionProofId;
        if (ProtectedVisit() || owner == null || manager == null || !ReferenceEquals(owner, manager.CurrentData) ||
            !NeedsNPCAdvance()) return false;
        // Legacy opt-in triggers without a save key can still review in this
        // owned session. Only keyed inspections promise persisted resumption.
        if (proofId == null) return true;
        if (owner.completedLessons != null && owner.completedLessons.Contains(proofId)) return true;
        List<string> previous = owner.completedLessons;
        if (owner.completedLessons == null) owner.completedLessons = new List<string>();
        owner.completedLessons.Add(proofId);
        if (manager.TrySaveGame()) return true;
        owner.completedLessons.Remove(proofId);
        if (previous == null && owner.completedLessons.Count == 0) owner.completedLessons = null;
        qualifiedInspectionOwner = null;
        Debug.LogWarning("[LessonTrigger] The inspection could not be saved. Inspect the vehicle again to retry; NPC progress is unchanged.", this);
        return false;
    }

    private void CancelPendingLesson()
    {
        if (pendingLessonManager != null)
        {
            pendingLessonManager.LessonOpened -= HandleLessonOpened;
            pendingLessonManager.LessonClosed -= HandleLessonClosed;
        }
        pendingLessonManager = null;
        pendingAuthorizedInspection = false;
        pendingLessonOwner = null;
    }

    private void BindVehicleCloseEvent()
    {
        if (triggerMode != TriggerMode.AfterVehicleInspectionCloses)
            return;

        if (liveLoadVehicle == null)
            liveLoadVehicle = GetComponent<LiveLoadVehicle>();

        if (liveLoadVehicle == null)
        {
            Debug.LogWarning(
                "[LessonTrigger] AfterVehicleInspectionCloses requires a LiveLoadVehicle reference.",
                this);
            return;
        }

        // Remove first so re-enabling or changing Inspector state cannot add the
        // same callback more than once.
        liveLoadVehicle.InspectionWindowClosed -= HandleInspectionWindowClosed;
        liveLoadVehicle.InspectionWindowClosed += HandleInspectionWindowClosed;
    }

    private void UnbindVehicleCloseEvent()
    {
        if (liveLoadVehicle != null)
            liveLoadVehicle.InspectionWindowClosed -= HandleInspectionWindowClosed;
    }

    private void HandleInspectionWindowClosed()
    {
        TryShowLesson();
    }
}
