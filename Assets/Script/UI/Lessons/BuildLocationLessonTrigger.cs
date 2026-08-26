using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class BuildLocationLessonTrigger : MonoBehaviour
{
    [Header("Location Lesson")]
    [SerializeField] private BuildLocation buildLocation;
    [SerializeField] private LessonData lesson;

    [Header("Behavior")]
    [Tooltip("Automatically listens for bridges saved by LevelCompleteManager.")]
    [SerializeField] private bool listenForSavedBridge = true;
    [Tooltip("Prevents the same scene component from opening its lesson repeatedly during one play session.")]
    [SerializeField] private bool triggerOnlyOnce = true;

    [Header("Optional Events")]
    [SerializeField] private UnityEvent onLessonTriggered;

    private bool hasTriggered;

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
