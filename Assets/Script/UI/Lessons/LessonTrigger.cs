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
    }

    public void ShowLesson()
    {
        TryShowLesson();
    }

    public bool TryShowLesson()
    {
        if (!isActiveAndEnabled || lesson == null ||
            (triggerOnlyOnce && hasTriggered) || LessonUIManager.Instance == null)
            return false;

        LessonUIManager.Instance.ShowLesson(lesson);
        hasTriggered = true;
        onLessonTriggered?.Invoke();
        return true;
    }

    public void ResetTrigger()
    {
        hasTriggered = false;
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
