using System;
using System.Text;
using UnityEngine;

/// <summary>Projects the active tutorial, using its saved row when available. Never executes or saves progress.</summary>
public sealed class TutorialObjectiveProjection
{
    public TrackedTask Task { get; private set; }
    public string Key => Task != null ? key : string.Empty;
    public bool CanNavigate => IsCurrentOwner() && HasPresentWaypoint(sourceStep);

    private TutorialManager sourceManager;
    private PlayerDataManager sourceSaveManager;
    private PlayerData sourceData;
    private TutorialSequence sourceSequence;
    private TutorialStep sourceStep;
    private string key = string.Empty;
    private int sourceStepIndex = -1;
    private string rawMessage, rawTitle, rawFirstMessage, rawLessonName, rawScenePath;

    /// <summary>Updates the one runtime-only row. Returns true only when the projected content or owner changed.</summary>
    public bool Sync(TutorialManager manager, PlayerData data)
    {
        TutorialSequence sequence = manager != null ? manager.ActiveSequence : null;
        TutorialStep step = manager != null ? manager.CurrentStep : null;
        if (!IsUsableManager(manager) || data == null || IsGuest() || sequence == null || step == null ||
            !sequence.showInObjectives || !sequence.gameObject.scene.IsValid() || !sequence.gameObject.scene.isLoaded)
            return Drop();

        PlayerDataManager saveManager = PlayerDataManager.Instance;
        if (saveManager != null && !ReferenceEquals(saveManager.CurrentData, data)) return Drop();
        string firstMessage = sequence.tutorialSteps != null && sequence.tutorialSteps.Length > 0 &&
            sequence.tutorialSteps[0] != null ? sequence.tutorialSteps[0].message : null;
        string scenePath = sequence.gameObject.scene.path;
        bool ownerChanged = Task == null || sourceManager != manager || sourceSaveManager != saveManager ||
            !ReferenceEquals(sourceData, data) || sourceSequence != sequence;
        bool identityChanged = ownerChanged || !string.Equals(rawLessonName, sequence.lessonName, StringComparison.Ordinal) ||
            !string.Equals(rawScenePath, scenePath, StringComparison.Ordinal);
        bool titleChanged = ownerChanged || !string.Equals(rawTitle, sequence.objectiveTitle, StringComparison.Ordinal) ||
            !string.Equals(rawFirstMessage, firstMessage, StringComparison.Ordinal);
        bool stepChanged = ownerChanged || sourceStep != step || sourceStepIndex != manager.CurrentStepIndex ||
            !string.Equals(rawMessage, step.message, StringComparison.Ordinal);
        if (!identityChanged && !titleChanged && !stepChanged) return false;

        if (ownerChanged)
        {
            TrackedTask saved = TutorialObjectiveHistory.Find(sequence, data);
            // A repeated walk can be active again while the completed record
            // stays in history. Do not reopen that record or create duplicates.
            Task = saved != null && !saved.isCompleted ? saved : new TrackedTask {
                isTutorial = true, contractName = string.Empty, targetWaypointName = string.Empty,
                tutorialObjectiveId = TutorialObjectiveHistory.GetKey(sequence)
            };
        }
        sourceManager = manager;
        sourceSaveManager = saveManager;
        sourceData = data;
        sourceSequence = sequence;
        sourceStep = step;
        sourceStepIndex = manager.CurrentStepIndex;
        rawMessage = step.message;
        rawTitle = sequence.objectiveTitle;
        rawFirstMessage = firstMessage;
        rawLessonName = sequence.lessonName;
        rawScenePath = scenePath;

        if (identityChanged) key = TutorialObjectiveHistory.GetKey(sequence);
        if (titleChanged)
        {
            string title = string.IsNullOrWhiteSpace(sequence.objectiveTitle)
                ? PlainTitle(TutorialInstructionText.Compact(firstMessage)) : PlainTitle(sequence.objectiveTitle);
            Task.title = string.IsNullOrWhiteSpace(title) ? sequence.name : title;
        }
        if (stepChanged || titleChanged)
        {
            string instruction = TutorialInstructionText.Compact(step.message);
            Task.description = string.IsNullOrWhiteSpace(instruction) ? Task.title : instruction.Trim();
        }
        return true;
    }

    public bool Owns(TrackedTask task) => Task != null && ReferenceEquals(Task, task);

    /// <summary>Recalls this exact active tutorial's existing route; never starts or advances a tutorial.</summary>
    public bool TryNavigate()
    {
        if (!CanNavigate) return false;
        sourceManager.RecallCurrentTutorialGuide();
        return true;
    }

    public void Clear() { Drop(); }

    private bool IsCurrentOwner()
    {
        return Task != null && IsUsableManager(sourceManager) && TutorialManager.Instance == sourceManager &&
            sourceSaveManager != null && PlayerDataManager.Instance == sourceSaveManager &&
            ReferenceEquals(sourceSaveManager.CurrentData, sourceData) && !IsGuest() &&
            sourceSequence != null && sourceSequence.showInObjectives && sourceSequence.gameObject.scene.IsValid() &&
            sourceSequence.gameObject.scene.isLoaded && sourceManager.ActiveSequence == sourceSequence &&
            sourceManager.CurrentStep == sourceStep && sourceManager.CurrentStepIndex == sourceStepIndex;
    }

    private static bool IsUsableManager(TutorialManager manager) => manager != null && manager.isActiveAndEnabled &&
        manager.gameObject.activeInHierarchy && manager.gameObject.scene.IsValid() && manager.gameObject.scene.isLoaded;

    private static bool IsGuest() => FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsGuestInHostWorld;

    private static bool HasPresentWaypoint(TutorialStep step)
    {
        if (PathGuider.Instance == null || step == null || step.stepWaypoints == null) return false;
        // The guider owns the current waypoint index and its NPC-presence cache.
        // Recalling a later step must not reconsider already-reached targets.
        return PathGuider.Instance.CanRecallTutorialGuide(step.stepWaypoints);
    }

    private bool Drop()
    {
        bool changed = Task != null;
        Task = null;
        key = string.Empty;
        sourceManager = null;
        sourceSaveManager = null;
        sourceData = null;
        sourceSequence = null;
        sourceStep = null;
        sourceStepIndex = -1;
        rawMessage = rawTitle = rawFirstMessage = rawLessonName = rawScenePath = null;
        return changed;
    }

    private static string PlainTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        bool space = false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '<')
            {
                int end = value.IndexOf('>', index + 1);
                if (end >= 0) { index = end; continue; }
            }
            if (char.IsWhiteSpace(character)) { space = result.Length > 0; continue; }
            if (space) { result.Append(' '); space = false; }
            result.Append(character);
        }
        return result.ToString();
    }
}
