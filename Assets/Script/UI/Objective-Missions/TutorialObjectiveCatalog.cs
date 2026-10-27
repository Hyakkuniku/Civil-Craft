using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Authored objective definitions for recovering history from completed lessons, including scenes that are not loaded.</summary>
[CreateAssetMenu(menuName = "Civil Craft/Tutorial Objective Catalog", fileName = "TutorialObjectiveCatalog")]
public sealed class TutorialObjectiveCatalog : ScriptableObject
{
    public const string ResourceName = "TutorialObjectiveCatalog";

    [Serializable]
    public sealed class Entry
    {
        public string objectiveId;
        public string lessonId;
        public string scenePath;
        public string title;
        [TextArea(2, 6)] public string description;
        [Tooltip("Allow this unique legacy completed lesson to recover its objective history. Disable for lesson IDs shared by different sequences.")]
        public bool allowLegacyLessonMatch;
    }

    public List<Entry> entries = new List<Entry>();
}
