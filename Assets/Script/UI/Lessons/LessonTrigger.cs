using UnityEngine;
using UnityEngine.Events;

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
    private LessonUIManager pendingLessonManager;
    private bool closeActionCompleted;
    private bool npcAdvanceArmed;
    private bool pendingAuthorizedInspection;

    // Wire only from the NPC's instruction dialogue-finished event, not arrival
    // or inspection itself. Persist so reloading after the prompt remains valid.
    public void ArmNPCAdvance()
    {
        if (advanceNPC == null || advanceNPC.IsTravelling ||
            advanceNPC.CurrentPhaseIndex != requiredNPCPhaseIndex) return;
        npcAdvanceArmed = true;
        var manager = PlayerDataManager.Instance;
        var data = manager != null ? manager.CurrentData : null;
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
        BindVehicleCloseEvent();
    }

    private void OnDisable()
    {
        UnbindVehicleCloseEvent();
        CancelPendingLesson();
    }

    public void ShowLesson()
    {
        TryShowLesson();
    }

    public bool TryShowLesson()
    {
        if (!isActiveAndEnabled || lesson == null ||
            (triggerOnlyOnce && hasTriggered && !NeedsNPCAdvance()) || LessonUIManager.Instance == null)
            return false;

        LessonUIManager manager = LessonUIManager.Instance;
        // Do not steal another open lesson's close callback.
        if (manager.IsOpen) return false;
        CancelPendingLesson();
        pendingAuthorizedInspection = NeedsNPCAdvance();
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
        if (advanceNPC == null || closeActionCompleted || advanceNPC.IsTravelling ||
            advanceNPC.CurrentPhaseIndex != requiredNPCPhaseIndex ||
            nextNPCPhaseIndex <= requiredNPCPhaseIndex) return false;
        var data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        bool armed = npcAdvanceArmed || (data != null && !string.IsNullOrWhiteSpace(completionSaveKey) &&
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
        CancelPendingLesson();
        if (!authorized || closed != lesson || !NeedsNPCAdvance()) return;
        closeActionCompleted = true; // Set before dispatch to prevent re-entry.
        var manager = PlayerDataManager.Instance;
        var data = manager != null ? manager.CurrentData : null;
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

    private void CancelPendingLesson()
    {
        if (pendingLessonManager != null)
        {
            pendingLessonManager.LessonOpened -= HandleLessonOpened;
            pendingLessonManager.LessonClosed -= HandleLessonClosed;
        }
        pendingLessonManager = null;
        pendingAuthorizedInspection = false;
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
