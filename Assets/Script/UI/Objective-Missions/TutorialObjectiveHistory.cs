using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Stores checked tutorial snapshots without executing tutorials or saving player data.</summary>
public static class TutorialObjectiveHistory
{
    public static bool IsSequenceTask(TrackedTask task) => task != null &&
        !string.IsNullOrWhiteSpace(task.tutorialObjectiveId);

    public static string GetKey(TutorialSequence sequence)
    {
        if (sequence == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(sequence.ObjectiveId))
            return "tutorial:" + sequence.ObjectiveId.Trim();

        // A lesson ID can be shared by old authored sequences. Include the
        // hierarchy and sibling positions so their history cannot be combined.
        // Unlike GetInstanceID, this fallback survives an ordinary scene reload.
        var hierarchy = new StringBuilder();
        for (Transform item = sequence.transform; item != null; item = item.parent)
            hierarchy.Insert(0, "/" + Uri.EscapeDataString(item.name ?? string.Empty) +
                "[" + item.GetSiblingIndex() + "]");
        return "tutorial:" + ScenePath(sequence) + "|" + (sequence.lessonName ?? string.Empty).Trim() +
            "|" + hierarchy;
    }

    public static TrackedTask Find(TutorialSequence sequence, PlayerData data)
    {
        if (sequence == null || data?.activeQuests == null) return null;
        string key = GetKey(sequence);
        return data.activeQuests.Find(task => IsSequenceTask(task) &&
            string.Equals(task.tutorialObjectiveId, key, StringComparison.Ordinal));
    }

    public static TrackedTask Begin(TutorialSequence sequence, PlayerData data) =>
        Begin(sequence, data, out _);

    public static TrackedTask Begin(TutorialSequence sequence, PlayerData data, out bool changed)
    {
        changed = false;
        if (!CanRecord(sequence, data)) return null;
        TrackedTask existing = Find(sequence, data);
        if (existing != null) return existing;

        var task = new TrackedTask
        {
            title = Title(sequence),
            description = Description(FirstStep(sequence), Title(sequence)),
            isTutorial = true,
            tutorialObjectiveId = GetKey(sequence),
            tutorialLessonId = sequence.lessonName ?? string.Empty,
            tutorialScenePath = ScenePath(sequence),
            contractName = string.Empty,
            targetWaypointName = string.Empty
        };
        if (data.activeQuests == null) data.activeQuests = new List<TrackedTask>();
        data.activeQuests.Add(task);
        changed = true;
        return task;
    }

    public static bool RecordStep(TutorialSequence sequence, TutorialStep step, PlayerData data)
    {
        if (step == null) return false;
        TrackedTask task = Begin(sequence, data, out bool changed);
        // A forced replay may project a temporary active row, but it must never
        // reopen or overwrite the completed record from the original playthrough.
        if (task == null || task.isCompleted) return changed;
        string title = Title(sequence);
        string description = Description(step, title);
        if (!string.Equals(task.title, title, StringComparison.Ordinal))
        { task.title = title; changed = true; }
        if (!string.Equals(task.description, description, StringComparison.Ordinal))
        { task.description = description; changed = true; }
        return changed;
    }

    /// <summary>Called only by the tutorial's genuine completion path, never by a UI/history click.</summary>
    public static bool Complete(TutorialSequence sequence, PlayerData data)
    {
        TrackedTask task = Begin(sequence, data, out bool changed);
        if (task == null || task.isCompleted) return changed;
        task.isCompleted = true;
        task.isReadyToTurnIn = false;
        return true;
    }

    /// <summary>Imports old completion only when the caller has ruled out ambiguous legacy lesson IDs.</summary>
    public static bool RestoreCompleted(TutorialSequence sequence, PlayerData data, bool allowLegacyLessonMatch)
    {
        if (!allowLegacyLessonMatch || !CanRecord(sequence, data) ||
            string.IsNullOrWhiteSpace(sequence.lessonName) || data.completedLessons == null ||
            !data.completedLessons.Contains(sequence.lessonName)) return false;
        return Complete(sequence, data);
    }

    /// <summary>Backfills an audited definition even when its source scene is not loaded.</summary>
    public static bool RestoreCompletedDefinition(PlayerData data, string objectiveId, string lessonId,
        string scenePath, string title, string description, bool allowLegacyLessonMatch)
    {
        if (!allowLegacyLessonMatch || data == null || string.IsNullOrWhiteSpace(objectiveId) ||
            string.IsNullOrWhiteSpace(lessonId) || data.completedLessons == null ||
            !data.completedLessons.Contains(lessonId)) return false;
        string key = "tutorial:" + objectiveId.Trim();
        TrackedTask existing = data.activeQuests?.Find(task => IsSequenceTask(task) &&
            string.Equals(task.tutorialObjectiveId, key, StringComparison.Ordinal));
        if (existing != null)
        {
            if (existing.isCompleted) return false;
            existing.isCompleted = true;
            existing.isReadyToTurnIn = false;
            return true;
        }

        string readableTitle = PlainText(title);
        if (string.IsNullOrWhiteSpace(readableTitle)) readableTitle = PlainText(lessonId);
        string readableDescription = TutorialInstructionText.Compact(description);
        if (data.activeQuests == null) data.activeQuests = new List<TrackedTask>();
        data.activeQuests.Add(new TrackedTask
        {
            title = readableTitle,
            description = string.IsNullOrWhiteSpace(readableDescription) ? readableTitle : readableDescription.Trim(),
            isTutorial = true,
            isCompleted = true,
            tutorialObjectiveId = key,
            tutorialLessonId = lessonId,
            tutorialScenePath = scenePath ?? string.Empty,
            contractName = string.Empty,
            targetWaypointName = string.Empty
        });
        return true;
    }

    private static bool CanRecord(TutorialSequence sequence, PlayerData data) =>
        sequence != null && sequence.showInObjectives && data != null &&
        sequence.gameObject.scene.IsValid() && sequence.gameObject.scene.isLoaded;

    private static string ScenePath(TutorialSequence sequence) => sequence.gameObject.scene.path ?? string.Empty;
    private static TutorialStep FirstStep(TutorialSequence sequence) =>
        sequence.tutorialSteps != null && sequence.tutorialSteps.Length > 0 ? sequence.tutorialSteps[0] : null;

    private static string Title(TutorialSequence sequence)
    {
        string title = PlainText(string.IsNullOrWhiteSpace(sequence.objectiveTitle)
            ? TutorialInstructionText.Compact(FirstStep(sequence)?.message) : sequence.objectiveTitle);
        return string.IsNullOrWhiteSpace(title) ? sequence.name : title;
    }

    private static string Description(TutorialStep step, string title)
    {
        string text = TutorialInstructionText.Compact(step?.message);
        return string.IsNullOrWhiteSpace(text) ? title : text.Trim();
    }

    private static string PlainText(string value)
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
