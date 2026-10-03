#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit recovery for a shared Inspector holding destroyed targets.</summary>
public static class InspectorRecovery
{
    [MenuItem("Tools/Civil Craft/Repair Inspector Selection")]
    public static void Repair()
    {
        // Only editor selection changes; no objects, scenes or saves are modified.
        ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
        tracker.isLocked = false;
        Selection.objects = Array.Empty<UnityEngine.Object>();
        EditorApplication.delayCall += () =>
        {
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            Debug.Log("[Inspector] Cleared stale selection and rebuilt the shared Inspector. " +
                "If another locked Inspector still reports errors, unlock or close that tab and reopen Window > General > Inspector.");
        };
    }
}
#endif
