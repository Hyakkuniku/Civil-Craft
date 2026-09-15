using UnityEditor;

[CustomEditor(typeof(Interactable), true)]
public class InteractableEditor : Editor
{
    [InitializeOnLoadMethod]
    private static void ScheduleInspectorRecovery()
    {
        EditorApplication.delayCall += RecoverInspectorsAfterReload;
        AssemblyReloadEvents.beforeAssemblyReload += RecoverInvalidInspectors;
    }

    private static void RecoverInspectorsAfterReload()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RecoverInspectorsAfterReload;
            return;
        }
        const string key = "CivilCraft.InspectorRecovery.ClearRetainedTargets.v2";
        bool firstRecovery = !SessionState.GetBool(key, false);
        RecoverInspectorTargets(firstRecovery);
        SessionState.SetBool(key, true);
    }

    private static void RecoverInvalidInspectors() => RecoverInspectorTargets(false);

    [MenuItem("Tools/Civil Craft/Recover Missing Inspector Targets")]
    private static void RecoverMissingInspectorTargets()
    {
        RecoverInspectorTargets(true);
    }

    private static void RecoverInspectorTargets(bool force)
    {
        var trackers = new System.Collections.Generic.HashSet<ActiveEditorTracker>();
        var windows = new System.Collections.Generic.Dictionary<ActiveEditorTracker, UnityEngine.Object>();
        trackers.Add(ActiveEditorTracker.sharedTracker);
        // Unity has no public InspectorWindow type. Include locked/additional Inspectors
        // through their tracker without changing any valid Inspector's lock or selection.
        var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        if (inspectorType != null)
        {
            var property = inspectorType.GetProperty("tracker",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (property != null)
                foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll(inspectorType))
                    if (property.GetValue(window) is ActiveEditorTracker tracker)
                    {
                        trackers.Add(tracker);
                        windows[tracker] = window;
                    }
        }

        int repaired = 0;
        foreach (var tracker in trackers)
        {
            bool hasMissingTarget = force;
            foreach (var editor in tracker.activeEditors)
            {
                if (editor == null) { hasMissingTarget = true; break; }
                foreach (var inspected in editor.targets)
                    if (inspected == null) { hasMissingTarget = true; break; }
                if (hasMissingTarget) break;
            }
            if (!hasMissingTarget) continue;
            // Unlocking the tracker alone leaves the Inspector's serialized lock list
            // intact; Unity can restore those dead instance IDs on the next reload.
            if (windows.TryGetValue(tracker, out var inspector))
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;
                inspectorType.GetProperty("isLocked", flags)?.SetValue(inspector, false);
                inspectorType.GetMethod("ClearSerializedLockedObjects", flags)?.Invoke(inspector, null);
            }
            typeof(ActiveEditorTracker).GetMethod("SetObjectsLockedByThisTracker",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)?.Invoke(tracker,
                    new object[] { new System.Collections.Generic.List<UnityEngine.Object>() });
            tracker.isLocked = false;
            var validSelection = new System.Collections.Generic.List<UnityEngine.Object>();
            foreach (var selected in Selection.objects)
                if (selected != null) validSelection.Add(selected);
            Selection.objects = force ? new UnityEngine.Object[0] : validSelection.ToArray();
            tracker.ForceRebuild();
            repaired++;
        }
        if (repaired > 0)
            UnityEngine.Debug.Log($"[Inspector Recovery] Rebuilt {repaired} Inspector(s) with missing targets.");
    }

    public override void OnInspectorGUI()
    {
        if (target == null) return;
        Interactable interactable = (Interactable)target;

        if (target.GetType() == typeof(EventOnlyInteractable))
        {
            interactable.promptMessage =
                EditorGUILayout.TextField("Prompt Message", interactable.promptMessage);

            EditorGUILayout.HelpBox(
                "EventOnlyInteractable can ONLY use UnityEvents.",
                MessageType.Info
            );

            interactable.useEvents = true;

            if (interactable.GetComponent<InteractionEvent>() == null)
            {
                interactable.gameObject.AddComponent<InteractionEvent>();
            }
        }
        else
        {
            base.OnInspectorGUI();

            if (interactable.useEvents)
            {
                if (interactable.GetComponent<InteractionEvent>() == null)
                    interactable.gameObject.AddComponent<InteractionEvent>();
            }
            else
            {
                InteractionEvent evt = interactable.GetComponent<InteractionEvent>();
                if (evt != null)
                    DestroyImmediate(evt);
            }
        }
    }
}
