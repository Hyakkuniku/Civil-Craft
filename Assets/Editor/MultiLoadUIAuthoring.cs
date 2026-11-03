#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, scoped authoring. Importing the scripts never edits a scene or contract.</summary>
[InitializeOnLoad]
public static class MultiLoadUIAuthoring
{
    public const string RequestPath = "Temp/multi-load-ui.request";
    public const string ReportPath = "Logs/multi-load-ui.txt";
    private static readonly string[] Paths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity" };
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;
    static MultiLoadUIAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string command = File.ReadAllText(RequestPath).Trim().ToLowerInvariant(); File.Delete(RequestPath); Run(command);
    }
    [MenuItem("Tools/Civil Craft/Multiple Live Loads/1. Validate Open Scene Setups (Read Only)")]
    public static void ValidateSetups()
    {
        var text = new StringBuilder("Multiple live-load scene setup validation\n"); int count = 0, errors = 0;
        foreach (BuildLocation site in Resources.FindObjectsOfTypeAll<BuildLocation>())
        {
            if (site == null || !site.gameObject.scene.IsValid() || !site.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(site.gameObject.scene) ||
                site.activeContract == null || !site.activeContract.HasMultipleLiveLoads) continue;
            count++;
            bool valid = LiveLoadTestRoster.TryCreate(site, site.activeContract, out _, out string reason, false);
            text.AppendLine((valid ? "PASS: " : "INVALID: ") + site.gameObject.scene.name + "/" + site.name + (valid ? string.Empty : "; " + reason));
            if (!valid) errors++;
        }
        text.AppendLine(count + " multi-load setups checked. Existing Single contracts were not changed. Cargo need not be loaded for this read-only authoring check.");
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/multi-load-setup-validation.txt", (errors == 0 ? "PASS\n" : "FAILED\n") + text);
        if (errors == 0) Debug.Log(text.ToString()); else Debug.LogWarning(text.ToString());
    }
    [MenuItem("Tools/Civil Craft/Multiple Live Loads/2. Author Status Cards in Gameplay Scenes")]
    public static void Apply() { Run("apply"); }
    [MenuItem("Tools/Civil Craft/Multiple Live Loads/3. Verify Authored Status Cards (Read Only)")]
    public static void Verify() { Run("verify"); }
    // Also usable by a graphics-enabled batch Editor when the main Editor is
    // closed. Only loads the saved scenes and renders detached copies.
    public static void PreviewGameplayScenes() { Run("preview-scenes"); }
    public static void FinalizeSavedScenes()
    {
        PreviewGameplayScenes();
        Require(File.Exists(ReportPath) && File.ReadAllText(ReportPath).StartsWith("PASS", StringComparison.Ordinal), "Preview failed before authoring. See " + ReportPath);
        MultiLoadNativeValidation.Run();
        Require(File.Exists(MultiLoadNativeValidation.ReportPath) && File.ReadAllText(MultiLoadNativeValidation.ReportPath).StartsWith("PASS", StringComparison.Ordinal), "Native roster checks failed before authoring.");
        Apply();
        Require(File.Exists(ReportPath) && File.ReadAllText(ReportPath).StartsWith("PASS", StringComparison.Ordinal), "Authored controls need attention. See " + ReportPath);
        Verify();
        Require(File.ReadAllText(ReportPath).StartsWith("PASS", StringComparison.Ordinal), "Saved authored controls did not verify.");
        ValidateSetups();
    }

    private sealed class Target
    {
        public Scene Scene; public bool Opened, Listed, Dirty, Saved, Changed;
        public BuildUIController Controller; public string Hash;
    }

    private static void Run(string command)
    {
        if (Busy) return;
        if (command == "validate") { ValidateSetups(); return; }
        if (command == "preview") { MultiLoadUIPresentationValidation.Run(); return; }
        running = true;
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup(); Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData player = saves != null ? saves.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        var targets = new List<Target>(); var report = new StringBuilder("Multiple live-load authored status cards: " + command + "\n");
        int undo = -1;
        try
        {
            Require(command == "apply" || command == "verify" || command == "preview-scenes", "Use apply, verify, validate, preview, or preview-scenes.");
            foreach (string path in Paths)
            {
                Require(File.Exists(path), "Required scene is missing: " + path);
                Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var target = new Target { Scene = scene, Opened = opened, Listed = setup.Any(entry => entry.path == path), Dirty = scene.isDirty, Hash = Hash(path) };
                targets.Add(target);
                var controllers = All<BuildUIController>(scene).ToArray();
                Require(controllers.Length == 1 && controllers[0].physicsManager != null, "Expected one authored Build UI and physics manager: " + path);
                target.Controller = controllers[0];
                MultiLoadTestHUD existingCard = target.Controller.GetComponent<MultiLoadTestHUD>();
                GameObject spectatorRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MultiLoadTestHUD.RemoteCanvasName);
                Require(spectatorRoot == null || (existingCard != null && existingCard.RemotePanel != null &&
                    existingCard.RemotePanel.GetComponentInParent<Canvas>(true).gameObject == spectatorRoot),
                    "A custom object already uses the spectator status name; it was not overwritten: " + path);
                TMP_Text font = target.Controller.stressText != null ? target.Controller.stressText : target.Controller.actionLogText;
                Require(font != null && font.font != null && font.GetComponentInParent<Canvas>(true) != null, "Missing status-card font or build Canvas: " + path);
                report.AppendLine("PREFLIGHT: " + scene.name + "; existing unsaved changes=" + target.Dirty);
            }
            if (command == "apply")
            {
                string backup = "Logs/MultiLoadUIBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(backup);
                foreach (Target target in targets) File.Copy(target.Scene.path, Path.Combine(backup, target.Scene.name + ".unity"));
                report.AppendLine("BACKUP: " + backup);
                foreach (Target target in targets)
                {
                    bool alreadyAuthored;
                    try { Validate(target); alreadyAuthored = true; } catch (InvalidOperationException) { alreadyAuthored = false; }
                    if (alreadyAuthored) report.AppendLine("UNCHANGED: " + target.Scene.name + "; authored controls already match.");
                    else
                    {
                        if (undo < 0) { Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author multiple live-load status cards"); }
                        Author(target); target.Changed = true;
                    }
                }
                foreach (Target target in targets) Validate(target);
                // Render before persistence, while owned edits are already dirty.
                // Deferred Undo/layout notifications must not make a successful
                // read-only preview look as if it dirtied an already-saved scene.
                ValidatePreviews();
                if (undo >= 0) { Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo); }
                foreach (Target target in targets.Where(target => target.Changed))
                    if (target.Dirty) report.AppendLine("WAIT SAVE: " + target.Scene.name + "; existing edits remain unsaved.");
                    else
                    {
                        Require(Hash(target.Scene.path) == target.Hash, "Scene changed externally before save: " + target.Scene.path);
                        Require(EditorSceneManager.SaveScene(target.Scene), "Could not save " + target.Scene.path); target.Saved = true;
                        report.AppendLine("SAVED: " + target.Scene.name + "; owned status card only.");
                    }
            }
            else if (command == "verify") foreach (Target target in targets) { Validate(target); report.AppendLine("PASS: " + target.Scene.name + "; authored card, font, references and dormant default verified."); }
            if (command != "apply") ValidatePreviews();
            report.AppendLine("PASS: detached phone/tablet status and scrollable-roster checks.");
            report.Insert(0, command == "apply" && targets.Any(target => target.Dirty) ? "WAIT SAVE\n" : "PASS\n");
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
                    if (!target.Scene.isDirty) EditorSceneManager.CloseScene(target.Scene, !target.Listed);
                    else report.AppendLine("LEFT OPEN: " + target.Scene.name + "; unsaved edits retained.");
                }
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selection; Selection.activeObject = selected;
            if (saves != PlayerDataManager.Instance || (player != null && (!ReferenceEquals(player, saves.CurrentData) || JsonUtility.ToJson(player) != playerJson)))
                report.Insert(0, "FAILED SAFETY: player data changed.\n");
            SceneSetup[] restored = EditorSceneManager.GetSceneManagerSetup();
            if (setup.Length != restored.Length || !setup.Zip(restored, (a, b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(value => value))
                report.AppendLine("SCENE SETUP: unsaved scenes left open for the user; no edits discarded.");
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
            Debug.Log("[Multiple Live Load UI] " + report);
        }
    }

    private static void ValidatePreviews()
    {
        DateTime previous = File.Exists(MultiLoadUIPresentationValidation.ReportPath) ? File.GetLastWriteTimeUtc(MultiLoadUIPresentationValidation.ReportPath) : DateTime.MinValue;
        MultiLoadUIPresentationValidation.Run();
        Require(File.Exists(MultiLoadUIPresentationValidation.ReportPath) && File.GetLastWriteTimeUtc(MultiLoadUIPresentationValidation.ReportPath) > previous &&
            File.ReadAllText(MultiLoadUIPresentationValidation.ReportPath).StartsWith("PASS", StringComparison.Ordinal),
            "Detached phone/tablet previews failed: " + MultiLoadUIPresentationValidation.ReportPath);
    }

    private static void Author(Target target)
    {
        BuildUIController controller = target.Controller;
        MultiLoadTestHUD previous = controller.GetComponent<MultiLoadTestHUD>();
        bool hadRemote = previous != null && previous.RemotePanel != null;
        TMP_Text font = controller.stressText != null ? controller.stressText : controller.actionLogText;
        Canvas canvas = font.GetComponentInParent<Canvas>(true);
        Transform existing = canvas.transform.Find(MultiLoadTestHUD.PanelName);
        var owned = existing != null ? new HashSet<Component>(existing.GetComponentsInChildren<Component>(true)) : new HashSet<Component>();
        var protectedComponents = All<Component>(target.Scene).Where(component => component != null && component != previous && component != canvas.transform && !owned.Contains(component))
            .ToDictionary(component => component, component => EditorJsonUtility.ToJson(component));
        string controllerJson = EditorJsonUtility.ToJson(controller);
        if (previous == null) Undo.AddComponent<MultiLoadTestHUD>(controller.gameObject);
        MultiLoadTestHUD card = MultiLoadTestHUD.Ensure(controller);
        Require(card != null && card.Panel != null, "Status-card creation failed: " + target.Scene.name);
        if (existing == null) Undo.RegisterCreatedObjectUndo(card.Panel.gameObject, "Create live-load status card");
        if (!hadRemote) Undo.RegisterCreatedObjectUndo(card.RemotePanel.GetComponentInParent<Canvas>(true).gameObject, "Create spectator live-load status card");
        foreach (Component component in card.Panel.GetComponentsInChildren<Component>(true)) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(card);
        Require(protectedComponents.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value) && EditorJsonUtility.ToJson(controller) == controllerJson,
            "A component outside the owned status card changed: " + target.Scene.name);
        EditorSceneManager.MarkSceneDirty(target.Scene);
    }
    private static void Validate(Target target)
    {
        MultiLoadTestHUD card = target.Controller.GetComponent<MultiLoadTestHUD>();
        Require(card != null && card.Panel != null && card.Status != null && !card.Panel.gameObject.activeSelf, "Authored status card must exist and start hidden: " + target.Scene.name);
        Require(card.Panel.GetComponentInParent<Canvas>(true) == (target.Controller.stressText != null ? target.Controller.stressText : target.Controller.actionLogText).GetComponentInParent<Canvas>(true), "Status card is outside the build Canvas.");
        Require(card.Panel.sizeDelta == new Vector2(620f, 100f) && card.Panel.anchoredPosition == new Vector2(0f, 150f) && card.Status.font != null && !card.Status.raycastTarget,
            "Authored status-card geometry or font differs from the shared template.");
        Require(card.RemotePanel != null && card.RemoteStatus != null && !card.RemotePanel.GetComponentInParent<Canvas>(true).gameObject.activeSelf,
            "Authored spectator status must exist and start hidden.");
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
