#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit, reference-driven startup visibility repair. Never changes scene content or triggers.</summary>
[InitializeOnLoad]
public static class GameplayStartupUIAuthoring
{
    public const string RequestPath = "Temp/gameplay-startup-ui-v1.request";
    public const string ReportPath = "Logs/gameplay-startup-ui-v1.txt";
    private static readonly string[] ScenePaths = {
        "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/CanyonCrossing.unity"
    };
    private static double nextPoll;
    private static bool running;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private sealed class SceneTargets
    {
        public Scene scene;
        public bool opened, changed;
        public readonly List<GameObject> panels = new List<GameObject>();
        public readonly Dictionary<Object, string> serializedBefore = new Dictionary<Object, string>();
        public readonly Dictionary<Canvas, bool> parentCanvases = new Dictionary<Canvas, bool>();
        public readonly Dictionary<GameObject, bool> ancestors = new Dictionary<GameObject, bool>();
    }

    static GameplayStartupUIAuthoring()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1d;
        if (!File.Exists(RequestPath)) return;
        if (Busy)
        {
            WriteReport("WAIT: Exit Play/Prefab Mode and finish compilation/imports. No scene was changed or saved.");
            return;
        }
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        try
        {
            if (command == "apply") Apply();
            else if (command == "validate") Validate();
            else throw new InvalidOperationException("Unknown startup-UI request. Use apply or validate.");
        }
        catch (Exception error)
        {
            WriteReport("FAIL: " + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/UI/Startup/1. Close Authored Gameplay Overlays")]
    public static void Apply() => Run(true);

    [MenuItem("Tools/Civil Craft/UI/Startup/2. Validate Closed Gameplay Overlays")]
    public static void Validate() => Run(false);

    private static void Run(bool apply)
    {
        Check(!Busy, "Leave Play/Prefab Mode and finish imports before changing startup UI.");
        running = true;
        Scene originalActive = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        var scenes = new List<SceneTargets>();
        var report = new StringBuilder();
        int undoGroup = -1;
        bool applied = false, savedAny = false, succeeded = false;
        try
        {
            // Load and validate BOTH exact scenes before recording/changing a single target.
            foreach (string path in ScenePaths)
            {
                Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, "Missing supported scene: " + path);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var targets = new SceneTargets { scene = scene, opened = opened };
                scenes.Add(targets);
                Check(!opened || !scene.isDirty,
                    "An automatically opened scene already has unrelated Editor changes; left open and unsaved: " + path);
                Preflight(targets);
            }

            if (apply)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Close Authored Gameplay Startup Overlays");
                applied = true;
                foreach (SceneTargets targets in scenes)
                {
                    foreach (GameObject panel in targets.panels)
                    {
                        if (!panel.activeSelf) continue;
                        Undo.RecordObject(panel, "Close Authored Gameplay Startup Overlay");
                        panel.SetActive(false);
                        EditorUtility.SetDirty(panel);
                        targets.changed = true;
                    }
                    if (targets.changed) EditorSceneManager.MarkSceneDirty(targets.scene);
                }
                Undo.FlushUndoRecordObjects();
            }

            foreach (SceneTargets targets in scenes)
            {
                ValidateTargets(targets);
                report.AppendLine("PASS: " + targets.scene.path + ": " + targets.panels.Count +
                    " explicit manager-linked overlays closed; parent Canvases, manager bindings, tutorial sequences and button triggers retained.");
                foreach (GameObject panel in targets.panels)
                    report.AppendLine("  CLOSED: " + Hierarchy(panel.transform) + " | activeSelf=" + panel.activeSelf);
            }

            if (apply)
            {
                foreach (SceneTargets targets in scenes)
                {
                    if (!targets.opened || !targets.changed) continue;
                    Check(EditorSceneManager.SaveScene(targets.scene), "Could not save the startup-only changes in " + targets.scene.path);
                    savedAny = true;
                    report.AppendLine("SAVED: " + targets.scene.path + " (opened solely for this operation).");
                }
                Undo.CollapseUndoOperations(undoGroup);
            }
            string[] needsSave = scenes.Where(item => !item.opened && item.scene.isDirty).Select(item => item.scene.name).ToArray();
            report.AppendLine(needsSave.Length == 0 ? "NEEDS_SAVE: none" : "NEEDS_SAVE: " + string.Join(", ", needsSave));
            report.AppendLine("Already-loaded scenes were never auto-saved or closed. Normal result/tutorial opening callbacks were not invoked or altered.");
            succeeded = true;
            WriteReport("PASS: Gameplay startup UI " + (apply ? "applied and validated.\n" : "read-only validation.\n") + report);
            Debug.Log("[Gameplay Startup UI] " + (apply ? "Applied reference-only startup visibility repair." : "Read-only startup visibility checks passed.") +
                (needsSave.Length > 0 ? " Save your loaded scene edits: " + string.Join(", ", needsSave) : string.Empty));
        }
        catch (Exception error)
        {
            // Roll back this Undo group, never the user's preceding edits. Once a
            // scene was saved, leave any remaining edited scene open for recovery.
            if (applied && !savedAny && undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
            WriteReport("FAIL: " + error + "\n" + report +
                "\nAlready-loaded scenes were not auto-saved. Any dirty temporary scene remains open for review.");
            throw;
        }
        finally
        {
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            foreach (SceneTargets targets in scenes)
                if (targets.opened && targets.scene.IsValid() && targets.scene.isLoaded &&
                    (succeeded || !targets.scene.isDirty)) EditorSceneManager.CloseScene(targets.scene, true);
            Selection.objects = selection;
            Selection.activeObject = activeSelection;
            running = false;
        }
    }

    private static void Preflight(SceneTargets targets)
    {
        Scene scene = targets.scene;
        LevelCompleteManager complete = One<LevelCompleteManager>(scene);
        LevelFailedManager failed = One<LevelFailedManager>(scene);
        TutorialManager tutorial = One<TutorialManager>(scene);
        PauseManager pause = One<PauseManager>(scene);
        AddPanel(targets, complete.levelCompletePanel, "LevelCompleteManager.levelCompletePanel", false);
        AddPanel(targets, failed.levelFailedPanel, "LevelFailedManager.levelFailedPanel", false);
        AddPanel(targets, pause.pausePanel, "PauseManager.pausePanel", false);
        var serialized = new SerializedObject(tutorial);
        AddPanel(targets, Reference<GameObject>(serialized, "centerPanel"), "TutorialManager.centerPanel", false);
        AddPanel(targets, Reference<GameObject>(serialized, "leftPanel"), "TutorialManager.leftPanel", false);
        GameObject next = Reference<GameObject>(serialized, "nextButton");
        GameObject skip = Reference<GameObject>(serialized, "skipButton");
        Check(next != null && next.GetComponent<Button>() != null && skip != null && skip.GetComponent<Button>() != null,
            "Tutorial Continue/Skip button references are incomplete in " + scene.path);
        AddPanel(targets, next, "TutorialManager.nextButton", false);
        AddPanel(targets, skip, "TutorialManager.skipButton", false);
        TutorialPointer pointer = Reference<TutorialPointer>(serialized, "bouncingArrow");
        Check(pointer != null, "Tutorial pointer is unbound in " + scene.path);
        AddPanel(targets, pointer.gameObject, "TutorialManager.bouncingArrow", true);

        foreach (GameObject panel in targets.panels)
            foreach (Component manager in new Component[] { complete, failed, tutorial, pause })
                Check(panel != manager.gameObject && !manager.transform.IsChildOf(panel.transform),
                    "A UI reference points at a manager or manager ancestor; nothing substituted: " + scene.path);
        Remember(targets, complete); Remember(targets, failed); Remember(targets, tutorial); Remember(targets, pointer); Remember(targets, pause);
        foreach (GameObject panel in targets.panels)
            foreach (Button button in panel.GetComponentsInChildren<Button>(true)) Remember(targets, button);
        foreach (TutorialSequence sequence in All<TutorialSequence>(scene)) Remember(targets, sequence);
    }

    private static void AddPanel(SceneTargets targets, GameObject panel, string field, bool allowOwnCanvas)
    {
        Check(panel != null && panel.scene == targets.scene, "Missing/cross-scene " + field + " in " + targets.scene.path);
        Check(!targets.panels.Contains(panel), "Two startup UI fields point at the same object in " + targets.scene.path);
        Check(allowOwnCanvas || panel.GetComponent<Canvas>() == null,
            field + " points at a Canvas instead of an inner panel/control; authoring stopped.");
        targets.panels.Add(panel);
        for (Transform parent = panel.transform.parent; parent != null; parent = parent.parent)
        {
            targets.ancestors[parent.gameObject] = parent.gameObject.activeSelf;
            Canvas canvas = parent.GetComponent<Canvas>();
            if (canvas != null)
            {
                Check(canvas.enabled && canvas.gameObject.activeInHierarchy,
                    "Parent Canvas is already disabled/inactive; it was not changed: " + targets.scene.path + "/" + canvas.name);
                targets.parentCanvases[canvas] = canvas.enabled;
            }
        }
    }

    private static void ValidateTargets(SceneTargets targets)
    {
        foreach (GameObject panel in targets.panels)
            Check(panel != null && !panel.activeSelf, "Startup overlay is still authored open: " + targets.scene.path + "/" + (panel != null ? panel.name : "missing"));
        foreach (var pair in targets.ancestors)
            Check(pair.Key != null && pair.Key.activeSelf == pair.Value, "A parent GameObject activation changed in " + targets.scene.path);
        foreach (var pair in targets.parentCanvases)
            Check(pair.Key != null && pair.Key.enabled == pair.Value && pair.Key.enabled && pair.Key.gameObject.activeInHierarchy,
                "A parent Canvas is disabled or changed in " + targets.scene.path);
        foreach (var pair in targets.serializedBefore)
            Check(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                "A manager/reference/tutorial/button binding changed in " + targets.scene.path + ": " + (pair.Key != null ? pair.Key.name : "missing"));
    }

    private static void Remember(SceneTargets targets, Object value)
    {
        if (value != null && !targets.serializedBefore.ContainsKey(value)) targets.serializedBefore.Add(value, EditorJsonUtility.ToJson(value));
    }
    private static T Reference<T>(SerializedObject owner, string field) where T : Object
    {
        SerializedProperty property = owner.FindProperty(field);
        Check(property != null && property.propertyType == SerializedPropertyType.ObjectReference, "Missing serialized field: " + field);
        return property.objectReferenceValue as T;
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static T One<T>(Scene scene) where T : Component
    {
        T[] values = All<T>(scene);
        Check(values.Length == 1, "Expected exactly one " + typeof(T).Name + " in " + scene.path + "; found " + values.Length + ". Nothing substituted.");
        return values[0];
    }
    private static void WriteReport(string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, value);
    }
    private static string Hierarchy(Transform target)
    {
        var parts = new List<string>();
        for (Transform current = target; current != null; current = current.parent) parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
