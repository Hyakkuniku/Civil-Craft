#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Authors only the two scenes' owned Untrack controls; never invokes gameplay callbacks.</summary>
[InitializeOnLoad]
public static class ObjectiveUntrackAuthoring
{
    public const string RequestPath = "Temp/objective-untrack-authoring.request";
    public const string ReportPath = "Logs/objective-untrack-authoring.txt";
    private const string ControlName = "Untrack Objective";
    private const string Label = "UNTRACK OBJECTIVE";
    private static readonly string[] Paths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;

    static ObjectiveUntrackAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string command = File.ReadAllText(RequestPath).Trim().ToLowerInvariant(); File.Delete(RequestPath);
        Run(command);
    }

    [MenuItem("Tools/Civil Craft/Objectives/1. Inspect Authored Untrack Controls (Read Only)")]
    public static void InspectBoth() { Run("inspect"); }
    [MenuItem("Tools/Civil Craft/Objectives/2. Apply Untrack to Both Scenes")]
    public static void ApplyBoth() { Run("apply"); }
    [MenuItem("Tools/Civil Craft/Objectives/3. Verify Authored Untrack in Both Scenes (Read Only)")]
    public static void VerifyBoth() { Run("verify"); }

    private sealed class Target
    {
        public Scene Scene;
        public bool Opened, InitiallyDirty, Applied, Saved;
        public string DiskHash;
        public ObjectiveTrackerUI Tracker;
        public TMP_FontAsset Font;
        public Sprite Sprite;
        public GameObject Existing;
    }

    private static void Run(string command)
    {
        if (Busy) return;
        running = true;
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        Scene[] originalScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        var unrelated = originalScenes.Where(scene => !Paths.Contains(scene.path)).ToDictionary(scene => scene,
            scene => new { scene.isDirty, Roots = scene.GetRootGameObjects(), Hash = File.Exists(scene.path) ? Hash(scene.path) : null });
        var targets = new List<Target>();
        var report = new StringBuilder("Authored Objectives Untrack controls: " + command + "\n");
        PlayerDataManager manager = PlayerDataManager.Instance; PlayerData player = manager != null ? manager.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        bool success = false; int undo = -1;
        try
        {
            Require(command == "apply" || command == "verify" || command == "inspect", "Use inspect, apply or verify.");
            // Finish both scene/control preflights before changing either scene.
            foreach (string path in Paths)
            {
                Require(File.Exists(path), "Required scene is missing: " + path);
                Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var target = new Target { Scene = scene, Opened = opened, InitiallyDirty = scene.isDirty, DiskHash = Hash(path) };
                targets.Add(target);
                ObjectiveTrackerUI[] trackers = All<ObjectiveTrackerUI>(scene).ToArray();
                Require(trackers.Length == 1 && trackers[0].trackerPanel != null && trackers[0].trackerPanel.transform is RectTransform &&
                    trackers[0].navigateButton != null, "Expected the unique authored Objectives tracker and Track control: " + path);
                target.Tracker = trackers[0];
                TextMeshProUGUI source = target.Tracker.navigateButton.GetComponentInChildren<TextMeshProUGUI>(true);
                target.Font = source != null ? source.font : Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF");
                Require(target.Font != null && AssetDatabase.Contains(target.Font), "The persistent gameplay font is missing: " + path);
                target.Sprite = target.Tracker.roundedPanelSprite;
                if (target.Sprite == null && target.Tracker.questTabPrefab != null)
                {
                    Image card = target.Tracker.questTabPrefab.GetComponent<Image>(); if (card != null) target.Sprite = card.sprite;
                }
                Require(target.Sprite != null && AssetDatabase.Contains(target.Sprite), "The persistent shared rounded panel sprite is missing: " + path);
                Transform existing = target.Tracker.trackerPanel.transform.Find(ControlName);
                target.Existing = existing != null ? existing.gameObject : null;
                Require(target.Tracker.untrackButton == null || target.Tracker.untrackButton == target.Existing,
                    "The tracker already references a different custom Untrack control; it was not overwritten: " + path);
                if (target.Existing != null)
                {
                    Button button = target.Existing.GetComponent<Button>();
                    Require(button != null && target.Existing.GetComponent<Image>() != null && target.Existing.transform is RectTransform &&
                        target.Existing.GetComponentsInChildren<TextMeshProUGUI>(true).Length == 1,
                        "The owned Untrack control has a custom structure; it was not replaced: " + path);
                    for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                        Require(button.onClick.GetPersistentTarget(index) == target.Tracker &&
                            button.onClick.GetPersistentMethodName(index) == nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked),
                            "Untrack contains a custom callback; it was not overwritten: " + path);
                }
                report.AppendLine("PREFLIGHT: " + scene.name + "; " + (opened ? "opened additively" : "already open") +
                    "; dirty=" + target.InitiallyDirty + "; authored control=" + (target.Existing != null) + ".");
            }
            if (command == "apply")
            {
                string backup = "Logs/ObjectiveUntrackAuthoringBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(backup);
                foreach (Target target in targets) File.Copy(target.Scene.path, backup + "/" + Path.GetFileName(target.Scene.path), false);
                report.AppendLine("BACKUP: " + backup);
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author Untrack Objective controls");
                foreach (Target target in targets) Apply(target);
                foreach (Target target in targets) Validate(target);
                Require(manager == PlayerDataManager.Instance && (player == null || ReferenceEquals(player, manager.CurrentData) && JsonUtility.ToJson(player) == playerJson),
                    "Authoring changed live player data. No callbacks should run in Edit Mode.");
                foreach (Target target in targets)
                {
                    if (target.InitiallyDirty) report.AppendLine("WAIT SAVE: " + target.Scene.name + "; authored control is ready; pre-existing unsaved changes retained.");
                    else
                    {
                        Require(Hash(target.Scene.path) == target.DiskHash, "The scene file changed externally before saving: " + target.Scene.path);
                        Require(EditorSceneManager.SaveScene(target.Scene), "Could not save " + target.Scene.path);
                        target.Saved = true;
                        report.AppendLine("SAVED: " + target.Scene.name + "; actual Untrack button, persistent callback and tracker reference.");
                    }
                }
                Undo.CollapseUndoOperations(undo);
            }
            else if (command == "verify")
            {
                foreach (Target target in targets) { Validate(target); report.AppendLine("PASS: " + target.Scene.name + "; authored control, label, geometry, edit visibility and exactly one callback verified."); }
                DateTime previousPreview = File.Exists(ObjectiveUntrackValidation.ReportPath)
                    ? File.GetLastWriteTimeUtc(ObjectiveUntrackValidation.ReportPath) : DateTime.MinValue;
                ObjectiveUntrackValidation.Run();
                Require(File.Exists(ObjectiveUntrackValidation.ReportPath) &&
                    File.GetLastWriteTimeUtc(ObjectiveUntrackValidation.ReportPath) > previousPreview &&
                    File.ReadAllText(ObjectiveUntrackValidation.ReportPath).StartsWith("PASS", StringComparison.Ordinal),
                    "The detached Objectives previews did not pass; see " + ObjectiveUntrackValidation.ReportPath);
                report.AppendLine("PASS: detached phone/tablet previews checked both scenes' actual authored controls. " + ObjectiveUntrackValidation.ReportPath);
            }
            success = true;
            report.Insert(0, command == "apply" && targets.Any(target => target.InitiallyDirty) ? "WAIT SAVE\n" : "PASS\n");
        }
        catch (Exception error)
        {
            if (undo >= 0 && targets.All(target => !target.Saved)) Undo.RevertAllDownToGroup(undo);
            report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString());
        }
        finally
        {
            foreach (Target target in targets.AsEnumerable().Reverse())
                if (target.Opened && target.Scene.IsValid() && target.Scene.isLoaded)
                {
                    if (!target.Scene.isDirty && (!target.Applied || target.Saved || command != "apply")) EditorSceneManager.CloseScene(target.Scene, true);
                    else report.AppendLine("LEFT OPEN: " + target.Scene.name + "; unsaved changes retained.");
                }
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selection; Selection.activeObject = selected;
            try
            {
                Require(originalScenes.All(scene => scene.IsValid() && scene.isLoaded), "An originally open scene was closed.");
                Require(unrelated.All(pair => pair.Key.isDirty == pair.Value.isDirty && pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Roots) &&
                    (pair.Value.Hash == null || Hash(pair.Key.path) == pair.Value.Hash)), "An unrelated scene changed.");
                Require(manager == PlayerDataManager.Instance && (player == null || ReferenceEquals(player, manager.CurrentData) && JsonUtility.ToJson(player) == playerJson),
                    "Live player data changed.");
                report.AppendLine("PASS: original active scene, selection, unrelated scenes and live player data preserved. No navigation, tutorial, reward or UI callbacks invoked.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); success = false; }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
            if (success) Debug.Log("[Objectives Untrack authoring] " + report);
            else Debug.LogWarning("[Objectives Untrack authoring] See " + ReportPath + " for the stopped operation.");
        }
    }

    private static void Apply(Target target)
    {
        // Create the owned art directly in its target scene. This avoids
        // transiently dirtying another scene when it was originally active.
        if (SceneManager.GetActiveScene() != target.Scene) SceneManager.SetActiveScene(target.Scene);
        Require(SceneManager.GetActiveScene() == target.Scene, "Could not activate the target scene for owned UI creation.");
        ObjectiveTrackerUI tracker = target.Tracker; GameObject panel = tracker.trackerPanel;
        GameObject existing = target.Existing;
        var owned = existing != null ? new HashSet<Object>(existing.GetComponentsInChildren<Component>(true).Cast<Object>()) : new HashSet<Object>();
        var before = All<Component>(target.Scene).Where(component => component != null && component != tracker && component != panel.transform && !owned.Contains(component))
            .ToDictionary(component => component, component => EditorJsonUtility.ToJson(component));
        var active = All<Transform>(target.Scene).Where(item => existing == null || !item.IsChildOf(existing.transform)).ToDictionary(item => item.gameObject, item => item.gameObject.activeSelf);
        string trackerBefore = WithoutUntrack(EditorJsonUtility.ToJson(tracker));
        RectTransform panelRect = (RectTransform)panel.transform;
        Vector2 size = panelRect.sizeDelta, position = panelRect.anchoredPosition, anchorMin = panelRect.anchorMin, anchorMax = panelRect.anchorMax, pivot = panelRect.pivot;
        Vector3 scale = panelRect.localScale; Quaternion rotation = panelRect.localRotation;
        GameObject control = existing;
        if (control == null)
        {
            control = new GameObject(ControlName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            SceneManager.MoveGameObjectToScene(control, target.Scene); Undo.RegisterCreatedObjectUndo(control, "Create authored Untrack control");
            Undo.SetTransformParent(control.transform, panel.transform, "Parent authored Untrack control");
        }
        Undo.RecordObjects(control.GetComponents<Component>(), "Style authored Untrack control");
        RectTransform rect = (RectTransform)control.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = new Vector2(0f, 355f); rect.sizeDelta = new Vector2(420f, 72f); rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        Image image = control.GetComponent<Image>(); image.sprite = target.Sprite; image.type = Image.Type.Sliced;
        image.color = new Color32(119, 88, 61, 255); image.raycastTarget = true;
        TextMeshProUGUI label = control.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
        {
            GameObject child = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            SceneManager.MoveGameObjectToScene(child, target.Scene); Undo.RegisterCreatedObjectUndo(child, "Create authored Untrack label");
            Undo.SetTransformParent(child.transform, control.transform, "Parent authored Untrack label"); label = child.GetComponent<TextMeshProUGUI>();
        }
        Undo.RecordObject(label, "Style authored Untrack label");
        RectTransform labelRect = label.rectTransform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 6f); labelRect.offsetMax = new Vector2(-16f, -6f); labelRect.localScale = Vector3.one;
        label.font = target.Font; label.text = Label; label.color = Color.white; label.fontStyle = FontStyles.Bold;
        label.fontSize = 29f; label.enableAutoSizing = true; label.fontSizeMin = 21f; label.fontSizeMax = 29f;
        label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow;
        Button button = control.GetComponent<Button>(); button.targetGraphic = image; button.interactable = true;
        Undo.RecordObject(button, "Wire authored Untrack control");
        for (int index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--) UnityEventTools.RemovePersistentListener(button.onClick, index);
        UnityEventTools.AddPersistentListener(button.onClick, tracker.OnUntrackObjectiveClicked);
        button.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.RuntimeOnly);
        Undo.RecordObject(tracker, "Bind authored Untrack control"); tracker.untrackButton = control;
        control.SetActive(true); control.transform.SetAsLastSibling();
        foreach (Component component in control.GetComponentsInChildren<Component>(true)) Mark(component);
        Mark(tracker); target.Applied = true; target.Existing = control;
        Require(before.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value), "A component outside the owned Untrack control changed: " + target.Scene.name);
        Require(active.All(pair => pair.Key != null && pair.Key.activeSelf == pair.Value), "An existing object's visibility changed: " + target.Scene.name);
        Require(WithoutUntrack(EditorJsonUtility.ToJson(tracker)) == trackerBefore, "The tracker changed outside its Untrack reference: " + target.Scene.name);
        Require(panelRect.sizeDelta == size && panelRect.anchoredPosition == position && panelRect.anchorMin == anchorMin && panelRect.anchorMax == anchorMax &&
            panelRect.pivot == pivot && panelRect.localScale == scale && panelRect.localRotation == rotation, "The authored panel geometry changed.");
        EditorSceneManager.MarkSceneDirty(target.Scene);
    }

    private static void Validate(Target target)
    {
        ObjectiveTrackerUI tracker = target.Tracker; GameObject control = tracker.untrackButton;
        Require(control != null && control.name == ControlName && control.transform.parent == tracker.trackerPanel.transform && control.activeSelf,
            "The actual authored Untrack control/reference/edit visibility is missing: " + target.Scene.name);
        Require(tracker.trackerPanel.GetComponentsInChildren<Button>(true).Count(button => button.name == ControlName) == 1, "Duplicate authored Untrack controls.");
        RectTransform rect = control.transform as RectTransform;
        Require(rect != null && rect.sizeDelta == new Vector2(420f, 72f) && rect.anchoredPosition == new Vector2(0f, 355f) &&
            rect.anchorMin == Vector2.one * .5f && rect.anchorMax == Vector2.one * .5f && rect.pivot == Vector2.one * .5f && rect.localScale == Vector3.one,
            "Untrack authored geometry differs from the shared runtime layout.");
        Image image = control.GetComponent<Image>(); Button button = control.GetComponent<Button>();
        Require(image != null && image.sprite == target.Sprite && image.type == Image.Type.Sliced && image.raycastTarget &&
            image.color == (Color)new Color32(119, 88, 61, 255) && button != null && button.targetGraphic == image && button.interactable,
            "Untrack image/button styling differs.");
        Require(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == tracker &&
            button.onClick.GetPersistentMethodName(0) == nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked) &&
            button.onClick.GetPersistentListenerState(0) == UnityEngine.Events.UnityEventCallState.RuntimeOnly,
            "Untrack must contain exactly one persistent runtime callback to this scene's tracker.");
        TextMeshProUGUI label = control.GetComponentInChildren<TextMeshProUGUI>(true);
        Require(label != null && label.text == Label && label.font == target.Font && !label.raycastTarget && label.enableAutoSizing &&
            label.fontSizeMin == 21f && label.fontSizeMax == 29f && label.color == Color.white && label.alignment == TextAlignmentOptions.Center,
            "The authored Untrack label is missing or differs from the runtime action style.");
    }

    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static string WithoutUntrack(string json) => Regex.Replace(json, "\\\"untrackButton\\\":\\{\\\"instanceID\\\":-?[0-9]+\\}", "\"untrackButton\":null");
    private static void Mark(Component component) { EditorUtility.SetDirty(component); if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
