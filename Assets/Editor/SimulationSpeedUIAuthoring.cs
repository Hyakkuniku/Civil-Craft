#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring and detached native rendering of solo playback controls.</summary>
public static class SimulationSpeedUIAuthoring
{
    public const string RequestPath = "Temp/simulation-speed-ui.request";
    public const string ReportPath = "Logs/simulation-speed-ui.txt";
    private const string PreviewFolder = "Logs/SimulationSpeedUI";
    private const string SharedPrefab = "Assets/BridgeBuilder/Data/BuildUIManager.prefab";
    private static readonly string[] Paths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static double nextPoll;
    private static bool running;

    private sealed class Target
    {
        public Scene Scene;
        public BuildUIController UI;
        public bool Opened, Listed, Dirty, Changed, Saved;
        public string ControllerJson, PhysicsJson;
        public Dictionary<Button, string> Buttons;
        public string SourceText;
        public bool CreatedHUD;
    }

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 2d;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer || AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath); Run(command);
    }

    [MenuItem("Tools/Civil Craft/Simulation Speed/1. Author and Check Solo Controls")]
    public static void ApplyAndValidate() { if (!Run("apply")) throw new InvalidOperationException("Simulation speed UI checks failed. See " + ReportPath); }
    [MenuItem("Tools/Civil Craft/Simulation Speed/2. Verify Authored Controls")]
    public static void Validate() { Run("validate"); }
    [MenuItem("Tools/Civil Craft/Simulation Speed/3. Preview Phone and Tablet")]
    public static void Preview() { Run("preview"); }

    private static bool Run(string command)
    {
        if (running) return false;
        running = true;
        var report = new StringBuilder("Solo simulation playback UI: " + command + "\n");
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        var targets = new List<Target>();
        int undo = -1;
        bool success = false;
        try
        {
            Check(command == "apply" || command == "validate" || command == "preview", "Use apply, validate, or preview.");
            Check(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                "Run in idle Edit Mode after compilation.");
            Check(PrefabStageUtility.GetCurrentPrefabStage() == null, "Close Prefab Mode before authoring gameplay scenes.");
            foreach (string path in Paths)
            {
                Check(File.Exists(path), "Missing gameplay scene: " + path);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var target = new Target { Scene = scene, Opened = opened, Listed = setup.Any(item => item.path == path),
                    Dirty = scene.isDirty, SourceText = File.ReadAllText(path) };
                targets.Add(target);
                BuildUIController[] controllers = All<BuildUIController>(scene);
                Check(controllers.Length == 1, "Expected one BuildUIController in " + scene.name + ".");
                target.UI = controllers[0];
                Check(target.UI.physicsManager != null && target.UI.playPauseButtonImage != null &&
                    target.UI.stressText != null && target.UI.stressText.font != null,
                    "Missing existing simulation, physics, or font references in " + scene.name + ".");
                target.ControllerJson = EditorJsonUtility.ToJson(target.UI);
                target.PhysicsJson = EditorJsonUtility.ToJson(target.UI.physicsManager);
                target.Buttons = All<Button>(scene).Where(button => button.GetComponentInParent<SimulationSpeedHUD>() == null &&
                    !IsSpeedButton(button)).ToDictionary(button => button, button => EditorJsonUtility.ToJson(button));
                report.AppendLine("PREFLIGHT: " + scene.name + "; existing unsaved edits=" + target.Dirty + ".");
            }

            // Previews execute before any source scene or prefab changes.
            foreach (Target target in targets) RenderPreviews(target.UI, report);
            if (command == "apply")
            {
                string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-SimulationSpeed",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(backup);
                foreach (Target target in targets) File.Copy(target.Scene.path, Path.Combine(backup, target.Scene.name + ".unity"));
                File.Copy(SharedPrefab, Path.Combine(backup, "BuildUIManager.prefab"));
                report.AppendLine("BACKUP (outside Assets): " + backup);
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author solo simulation playback controls");
                foreach (Target target in targets)
                {
                    SimulationSpeedHUD old = target.UI.GetComponent<SimulationSpeedHUD>();
                    if (old != null)
                    {
                        Check(IsValid(old, target.UI), "Existing custom speed controls need review before restyling in " + target.Scene.name + ".");
                        string before = OwnSignature(old);
                        Image previousBackground = old.ToolbarBackground;
                        Undo.RecordObjects(OwnObjects(old), "Restyle playback toolbar");
                        SimulationSpeedHUD.Ensure(target.UI, null, SourceSprite(target.UI));
                        if (previousBackground == null && old.ToolbarBackground != null)
                            Undo.RegisterCreatedObjectUndo(old.ToolbarBackground.gameObject, "Create matching toolbar background");
                        old.ApplyLayout(new Vector2(1920f, 1080f), new Rect(0f, 0f, 1920f, 1080f), 160f, false);
                        old.Panel.gameObject.SetActive(false);
                        if (before != OwnSignature(old))
                        {
                            foreach (Object obj in OwnObjects(old)) EditorUtility.SetDirty(obj);
                            EditorSceneManager.MarkSceneDirty(target.Scene); target.Changed = true;
                            report.AppendLine("RESTYLED: " + target.Scene.name + "; existing arrows, callbacks and labels reused.");
                        }
                        else report.AppendLine("UNCHANGED: " + target.Scene.name + ".");
                        continue;
                    }
                    Check(!All<RectTransform>(target.Scene).Any(rect => rect.name == SimulationSpeedHUD.PanelName),
                        "A custom object already uses the playback panel name in " + target.Scene.name + ".");
                    Undo.AddComponent<SimulationSpeedHUD>(target.UI.gameObject);
                    target.CreatedHUD = true;
                    SimulationSpeedHUD hud = SimulationSpeedHUD.Ensure(target.UI, null, SourceSprite(target.UI));
                    Check(hud != null && hud.Panel != null, "Could not author playback controls in " + target.Scene.name + ".");
                    Undo.RegisterCreatedObjectUndo(hud.Panel.gameObject, "Create simulation playback panel");
                    AuthorCallbacks(hud);
                    hud.ApplyLayout(new Vector2(1920f, 1080f), new Rect(0f, 0f, 1920f, 1080f), 160f, false);
                    hud.Panel.gameObject.SetActive(false);
                    foreach (Component component in hud.Panel.GetComponentsInChildren<Component>(true)) EditorUtility.SetDirty(component);
                    EditorUtility.SetDirty(hud);
                    EditorSceneManager.MarkSceneDirty(target.Scene);
                    target.Changed = true;
                }
                foreach (Target target in targets) ValidateSource(target);
                AuthorSharedPrefab(targets[0].UI, report);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo);
                foreach (Target target in targets.Where(item => item.Changed))
                {
                    if (target.Dirty) report.AppendLine("WAIT SAVE: " + target.Scene.name + "; the user's existing edits and the new controls remain unsaved.");
                    else
                    {
                        Check(EditorSceneManager.SaveScene(target.Scene), "Could not save playback controls in " + target.Scene.name + ".");
                        target.Saved = true;
                        RestoreUnrelatedDocuments(target.Scene.path, target.SourceText, target.UI.GetComponent<SimulationSpeedHUD>(), target.CreatedHUD);
                        report.AppendLine("SAVED: " + target.Scene.path + "; scene was clean before authoring.");
                    }
                }
            }
            else if (command == "validate") foreach (Target target in targets) ValidateSource(target);
            if (command != "preview") ValidateSharedPrefab(report);
            report.AppendLine("PASS: Existing Start/Stop and Undo/Redo artwork, callbacks, BuildUIController references and physics settings are unchanged.");
            report.AppendLine("PASS: New source controls default inactive. Previews use detached copies; no gameplay clicks, player-save writes, contracts, routes, rewards or multiplayer settings were changed.");
            report.AppendLine("RESULT: PASS; UTC=" + DateTime.UtcNow.ToString("O"));
            success = true;
        }
        catch (Exception error)
        {
            if (undo >= 0 && targets.All(item => !item.Saved)) Undo.RevertAllDownToGroup(undo);
            report.AppendLine("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            foreach (Target target in targets.AsEnumerable().Reverse())
                if (target.Opened && target.Scene.IsValid() && target.Scene.isLoaded)
                {
                    if (!target.Scene.isDirty) EditorSceneManager.CloseScene(target.Scene, !target.Listed);
                    else report.AppendLine("LEFT OPEN: " + target.Scene.name + "; unsaved edits preserved.");
                }
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selection;
            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            bool restored = setup.Length == after.Length && setup.Zip(after, (a, b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(value => value);
            report.AppendLine(restored ? "PASS: Original scene setup and selection restored." : "SCENE SETUP: Dirty scenes retained for the user; no edits discarded.");
            report.Insert(0, success ? "PASS\n" : "FAIL\n");
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            running = false;
        }
        return success;
    }

    private static Object[] OwnObjects(SimulationSpeedHUD hud)
    {
        var objects = new List<Object> { hud };
        if (hud.Panel != null)
            foreach (Transform node in hud.Panel.GetComponentsInChildren<Transform>(true))
            {
                objects.Add(node.gameObject);
                objects.AddRange(node.GetComponents<Component>());
            }
        return objects.ToArray();
    }

    private static string OwnSignature(SimulationSpeedHUD hud) =>
        string.Join("\n", OwnObjects(hud).Select(EditorJsonUtility.ToJson));

    private static void ValidateToolbarStyle(SimulationSpeedHUD hud, BuildUIController source)
    {
        Check(hud.UsesToolbarStyle && hud.ToolbarBackground != null, "Playback still uses the old card design.");
        Image background = hud.ToolbarBackground;
        Image reference = SourceControlGroup(source)?.GetComponent<Image>();
        Check(!background.raycastTarget && background.type == Image.Type.Sliced, "Playback toolbar background must not intercept input.");
        if (reference != null)
            Check(background.sprite == reference.sprite && background.color == reference.color &&
                background.material == reference.material && Mathf.Approximately(background.pixelsPerUnitMultiplier, reference.pixelsPerUnitMultiplier),
                "Playback background does not match the existing simulation toolbar.");
        Check(hud.SpeedLabel.color == Color.white, "Playback speed label must match the white toolbar icons.");
        foreach (Button arrow in new[] { hud.Slower, hud.Faster })
        {
            Image hit = arrow.GetComponent<Image>();
            TextMeshProUGUI caption = arrow.GetComponentInChildren<TextMeshProUGUI>(true);
            Check(hit != null && hit.raycastTarget && hit.color.a <= .001f && caption != null &&
                caption.color == Color.white && !caption.raycastTarget && arrow.targetGraphic == caption,
                "Playback arrows need transparent hit surfaces and white caption tint targets.");
            Check(((RectTransform)arrow.transform).sizeDelta == Vector2.one * 76f,
                "Playback arrow artwork must match the existing 76-unit toolbar icons.");
        }
    }

    // Restore every old document outside the owned HUD after Unity serializes the
    // scene/prefab. The pre-command snapshot includes all existing user changes.
    private static void RestoreUnrelatedDocuments(string path, string before, SimulationSpeedHUD hud, bool created)
    {
        string after = File.ReadAllText(path);
        const string pattern = @"(?ms)^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\z)";
        var originals = Regex.Matches(before, pattern).Cast<Match>().ToDictionary(match => match.Groups[1].Value, match => match.Value);
        var current = Regex.Matches(after, pattern).Cast<Match>().ToDictionary(match => match.Groups[1].Value, match => match.Value);
        string script = AssetDatabase.AssetPathToGUID("Assets/Script/UI/SimulationSpeedHUD.cs");
        string hudId = current.Single(pair => Regex.IsMatch(pair.Value, @"m_Script: \{fileID: 11500000, guid: " + script + @", type: 3\}")).Key;
        var owned = new HashSet<string> { hudId };
        var queue = new Queue<string>();
        string panelId = Regex.Match(current[hudId], @"(?m)^  panel: \{fileID: (\d+)\}").Groups[1].Value;
        Check(panelId.Length > 0 && panelId != "0", "Saved playback panel reference is missing.");
        queue.Enqueue(panelId);
        while (queue.Count > 0)
        {
            string rectId = queue.Dequeue();
            if (!owned.Add(rectId)) continue;
            Check(current.TryGetValue(rectId, out string rect), "Saved playback transform is missing.");
            string gameObjectId = Regex.Match(rect, @"(?m)^  m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;
            Check(current.TryGetValue(gameObjectId, out string gameObject), "Saved playback object is missing.");
            owned.Add(gameObjectId);
            foreach (Match component in Regex.Matches(gameObject, @"component: \{fileID: (\d+)\}")) owned.Add(component.Groups[1].Value);
            string children = Regex.Match(rect, @"(?ms)^  m_Children:.*?^  m_Father:").Value;
            foreach (Match child in Regex.Matches(children, @"- \{fileID: (\d+)\}")) queue.Enqueue(child.Groups[1].Value);
        }
        // Only a first-time creation needs references on the existing owner and canvas.
        if (created)
        {
            owned.Add(SceneLocalId(hud.gameObject));
            if (hud.Panel.parent != null) owned.Add(SceneLocalId(hud.Panel.parent));
        }
        string restored = Regex.Replace(after, pattern, match =>
            !owned.Contains(match.Groups[1].Value) && originals.TryGetValue(match.Groups[1].Value, out string original)
                ? original : match.Value);
        File.WriteAllText(path, restored, new UTF8Encoding(false));
        Check(originals.Keys.All(id => owned.Contains(id) || current.ContainsKey(id)), "Saving removed an unrelated serialized object.");
    }

    private static string SceneLocalId(Object obj)
    {
        ulong id = GlobalObjectId.GetGlobalObjectIdSlow(obj).targetObjectId;
        Check(id != 0, "Cannot resolve the saved playback owner identifier.");
        return id.ToString();
    }

    private static bool IsSpeedButton(Button button)
    {
        for (Transform parent = button.transform; parent != null; parent = parent.parent)
            if (parent.name == SimulationSpeedHUD.PanelName) return true;
        return false;
    }

    private static void AuthorCallbacks(SimulationSpeedHUD hud)
    {
        Check(hud.Slower.onClick.GetPersistentEventCount() == 0 && hud.Faster.onClick.GetPersistentEventCount() == 0,
            "Unexpected existing callbacks on new playback controls.");
        UnityEventTools.AddPersistentListener(hud.Slower.onClick, hud.SlowDown);
        UnityEventTools.AddPersistentListener(hud.Faster.onClick, hud.FastForward);
    }

    private static bool IsValid(SimulationSpeedHUD hud, BuildUIController controller)
    {
        return hud != null && hud.Panel != null && hud.Slower != null && hud.Faster != null && hud.SpeedLabel != null &&
            hud.Physics == controller.physicsManager && hud.Panel.name == SimulationSpeedHUD.PanelName &&
            !hud.Panel.gameObject.activeSelf && hud.SpeedLabel.text == "0x" && hud.SpeedLabel.font != null &&
            hud.Slower.onClick.GetPersistentEventCount() == 1 && hud.Faster.onClick.GetPersistentEventCount() == 1 &&
            hud.Slower.onClick.GetPersistentTarget(0) == hud && hud.Faster.onClick.GetPersistentTarget(0) == hud &&
            hud.Slower.onClick.GetPersistentMethodName(0) == nameof(SimulationSpeedHUD.SlowDown) &&
            hud.Faster.onClick.GetPersistentMethodName(0) == nameof(SimulationSpeedHUD.FastForward);
    }

    private static void ValidateSource(Target target)
    {
        SimulationSpeedHUD hud = target.UI.GetComponent<SimulationSpeedHUD>();
        Check(IsValid(hud, target.UI), "Authored playback references, callbacks, label or inactive default are invalid in " + target.Scene.name + ".");
        ValidateToolbarStyle(hud, target.UI);
        Check(hud.Panel.GetComponentsInChildren<Button>(true).Length == 2 && hud.SimulationControls != null,
            "Expected two playback arrows and the existing simulation control group.");
        Check(EditorJsonUtility.ToJson(target.UI) == target.ControllerJson && EditorJsonUtility.ToJson(target.UI.physicsManager) == target.PhysicsJson,
            "Existing controller or physics configuration changed in " + target.Scene.name + ".");
        foreach (var pair in target.Buttons)
            Check(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                "Existing UI button changed in " + target.Scene.name + ".");
    }

    private static Sprite SourceSprite(BuildUIController controller)
    {
        if (controller.playPauseButtonImage == null) return null;
        for (Transform parent = controller.playPauseButtonImage.transform.parent; parent != null; parent = parent.parent)
        {
            Image image = parent.GetComponent<Image>();
            if (image != null && image.sprite != null) return image.sprite;
        }
        return null;
    }

    private static void AuthorSharedPrefab(BuildUIController source, StringBuilder report)
    {
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(SharedPrefab) != null, "Shared BuildUI prefab is missing.");
        string originalFile = File.ReadAllText(SharedPrefab);
        GameObject root = PrefabUtility.LoadPrefabContents(SharedPrefab);
        try
        {
            BuildUIController controller = root.GetComponent<BuildUIController>();
            Check(controller != null, "Shared prefab has no BuildUIController.");
            SimulationSpeedHUD previous = controller.GetComponent<SimulationSpeedHUD>();
            Check(previous == null || IsValid(previous, controller), "Existing custom shared playback controls need review.");
            string ownBefore = previous != null ? OwnSignature(previous) : string.Empty;
            string before = EditorJsonUtility.ToJson(controller);
            SimulationSpeedHUD hud = SimulationSpeedHUD.Ensure(controller, source.stressText.font, SourceSprite(source));
            Check(hud != null && hud.Panel != null, "Could not author shared playback controls.");
            hud.ApplyToolbarStyle(source, SourceSprite(source));
            if (previous == null) AuthorCallbacks(hud);
            hud.Panel.gameObject.SetActive(false);
            Check(IsValid(hud, controller) && EditorJsonUtility.ToJson(controller) == before, "Shared source callbacks or existing controller configuration changed.");
            ValidateToolbarStyle(hud, source);
            if (previous != null && ownBefore == OwnSignature(hud)) { report.AppendLine("UNCHANGED: Shared authored playback controls."); return; }
            PrefabUtility.SaveAsPrefabAsset(root, SharedPrefab, out bool saved);
            Check(saved, "Could not save shared authored playback controls.");
            RestoreUnrelatedDocuments(SharedPrefab, originalFile, hud, previous == null);
            report.AppendLine("SAVED: " + SharedPrefab + "; own inactive speed panel and callbacks only.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ValidateSharedPrefab(StringBuilder report)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(SharedPrefab);
        BuildUIController controller = root != null ? root.GetComponent<BuildUIController>() : null;
        Check(controller != null && IsValid(root.GetComponent<SimulationSpeedHUD>(), controller), "Shared authored speed controls did not verify.");
        Check(root.GetComponent<SimulationSpeedHUD>().UsesToolbarStyle, "Shared playback controls still use the old card design.");
        report.AppendLine("PASS: Shared BuildUI prefab contains the inactive authored panel, font and exactly one callback per arrow.");
    }

    private static void RenderPreviews(BuildUIController source, StringBuilder report)
    {
        Directory.CreateDirectory(PreviewFolder);
        foreach (Vector2Int size in new[] { new Vector2Int(1600, 720), new Vector2Int(1440, 1080), new Vector2Int(1126, 337) })
            foreach (bool undoRedoVisible in new[] { false, true }) Render(source, size, undoRedoVisible, report);
    }

    private static void Render(BuildUIController source, Vector2Int size, bool undoRedoVisible, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null, previous = RenderTexture.active;
        Texture2D pixels = null;
        Camera camera = null;
        try
        {
            GameObject cameraRoot = New(preview, "Detached speed preview camera");
            camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false; camera.scene = preview; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.orthographicSize = size.y * .005f; camera.aspect = size.x / (float)size.y;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.nearClipPlane = .05f; camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(56, 94, 151, 255);
            camera.allowHDR = false; camera.allowMSAA = false;
            var data = cameraRoot.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            target = new RenderTexture(size.x, size.y, 24) { hideFlags = HideFlags.HideAndDontSave };
            Check(target.Create(), "Could not create playback preview render target."); camera.targetTexture = target;
            float scale = Mathf.Sqrt(size.x / 1920f * (size.y / 1080f));
            GameObject root = New(preview, "Detached speed canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)root.transform).sizeDelta = new Vector2(size.x, size.y) / scale;
            root.transform.localScale = Vector3.one * (.01f * scale);

            RectTransform sourceControls = SourceControlGroup(source);
            Check(sourceControls != null, "Could not identify actual authored Start/Stop group.");
            GameObject controls = Object.Instantiate(sourceControls.gameObject, root.transform, false);
            controls.SetActive(true);
            foreach (Button button in controls.GetComponentsInChildren<Button>(true))
            {
                if (button.name == "Undo" || button.name == "Redo") button.gameObject.SetActive(undoRedoVisible);
            }
            // Preserve actual authored Start/Stop art, including the stop sprite used during testing.
            Image play = controls.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.name == source.playPauseButtonImage.name);
            Check(play != null, "Actual Start/Stop image is absent from the detached copy.");
            if (source.stopIcon != null) play.sprite = source.stopIcon;
            GameObject fixture = New(preview, "Detached speed controller");
            BuildUIController controller = fixture.AddComponent<BuildUIController>();
            controller.playPauseButtonImage = play;
            controller.simulationControlsPanel = controls;
            TextMeshProUGUI font = New(preview, "Preview font reference", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<TextMeshProUGUI>();
            font.font = source.stressText.font; font.gameObject.SetActive(false); controller.stressText = font;

            // Copy the whole top-level budget hierarchy. Its stretched children
            // must retain their original parent rather than stretching across the screen.
            if (source.budgetFillBar != null)
            {
                RectTransform original = source.budgetFillBar.rectTransform;
                while (original.parent is RectTransform ancestor && ancestor.GetComponent<Canvas>() == null) original = ancestor;
                if (original != null)
                {
                    GameObject budget = Object.Instantiate(original.gameObject, root.transform, false);
                    budget.SetActive(true);
                    controller.budgetFillBar = budget.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.name == source.budgetFillBar.name);
                    TextMeshProUGUI[] labels = budget.GetComponentsInChildren<TextMeshProUGUI>(true);
                    controller.usedBudgetText = source.usedBudgetText != null ? labels.FirstOrDefault(label => label.name == source.usedBudgetText.name) : null;
                    controller.maxBudgetText = source.maxBudgetText != null ? labels.FirstOrDefault(label => label.name == source.maxBudgetText.name) : null;
                }
            }
            SimulationSpeedHUD hud = SimulationSpeedHUD.Ensure(controller, source.stressText.font, SourceSprite(source));
            Check(hud != null && hud.Panel != null, "Detached playback controls could not be created.");
            hud.Panel.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)controls.transform);
            Canvas.ForceUpdateCanvases();
            float dpi = size.x == 1600 ? 320f : 240f;
            Rect safe = size.x == 1600 ? new Rect(32f, 0f, size.x - 32f, size.y) : new Rect(0f, 0f, size.x, size.y);
            hud.ApplyLayout(size, safe, dpi, true);
            Canvas.ForceUpdateCanvases();
            ValidateToolbarStyle(hud, controller);
            hud.SpeedLabel.text = "0.125x"; hud.SpeedLabel.ForceMeshUpdate();
            Check(!hud.SpeedLabel.isTextOverflowing && hud.SpeedLabel.preferredWidth <= hud.SpeedLabel.rectTransform.rect.width + 1f,
                "Slow-motion speed label does not fit the actual gameplay font.");
            hud.SpeedLabel.text = "0x"; hud.SpeedLabel.ForceMeshUpdate();
            foreach (Button arrow in new[] { hud.Slower, hud.Faster })
            {
                TextMeshProUGUI caption = arrow.GetComponentInChildren<TextMeshProUGUI>();
                caption.ForceMeshUpdate();
                Check(!caption.isTextOverflowing && caption.preferredWidth <= caption.rectTransform.rect.width + 1f &&
                    caption.preferredHeight <= caption.rectTransform.rect.height + 1f,
                    "Playback arrow caption does not fit the actual gameplay font and button.");
            }

            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            Rect slow = TouchBounds(hud.Slower, camera), fast = TouchBounds(hud.Faster, camera);
            Check(!slow.Overlaps(fast), "Playback arrow touch targets overlap.");
            foreach (Rect hit in new[] { slow, fast })
            {
                Check(hit.xMin >= safe.xMin - .5f && hit.xMax <= safe.xMax + .5f && hit.yMin >= safe.yMin - .5f && hit.yMax <= safe.yMax + .5f,
                    "Playback arrow target escapes the device safe area.");
                foreach (Button existing in controls.GetComponentsInChildren<Button>(false))
                    Check(!hit.Overlaps(TouchBounds(existing, camera)), "Playback target overlaps existing Start/Stop, Undo or Redo.");
            }
            ValidateRaycast(canvas, camera, hud.Slower); ValidateRaycast(canvas, camera, hud.Faster);
            foreach (Button existing in controls.GetComponentsInChildren<Button>(false))
                Check(Receiver(canvas, TouchBounds(existing, camera).center) == existing.gameObject,
                    "Playback controls steal an existing simulation-control tap.");
            foreach (Graphic budget in new Graphic[] { controller.budgetFillBar, controller.usedBudgetText, controller.maxBudgetText })
                if (budget != null && budget.gameObject.activeInHierarchy)
                {
                    Rect rect = ScreenBounds(budget.rectTransform, camera);
                    Check(!slow.Overlaps(rect) && !fast.Overlaps(rect), "Playback controls overlap the budget readout.");
                }
            RenderTexture.active = target;
            pixels = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0); pixels.Apply();
            string path = PreviewFolder + "/" + source.gameObject.scene.name.Replace(' ', '-') + "-" + size.x + "x" + size.y +
                (undoRedoVisible ? "-all-controls.png" : "-simulation.png");
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            report.AppendLine("PASS: " + path + "; actual gameplay font and Start/Stop art, safe bounds, separate 48dp arrow targets, budget clearance and native raycasts.");
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static RectTransform SourceControlGroup(BuildUIController source)
    {
        if (source.simulationControlsPanel != null) return source.simulationControlsPanel.transform as RectTransform;
        for (Transform parent = source.playPauseButtonImage.transform.parent; parent != null && parent.GetComponent<Canvas>() == null; parent = parent.parent)
            if (parent.GetComponent<Image>() != null || parent.GetComponent<CanvasGroup>() != null) return parent as RectTransform;
        return null;
    }

    private static void ValidateRaycast(Canvas canvas, Camera camera, Button button)
    {
        Rect bounds = TouchBounds(button, camera);
        Rect visible = ScreenBounds((RectTransform)button.transform, camera);
        Vector2 padded = new Vector2((bounds.xMin + visible.xMin) * .5f, bounds.center.y);
        Check(Receiver(canvas, bounds.center) == button.gameObject && Receiver(canvas, padded) == button.gameObject,
            "Native GraphicRaycaster does not route the visible and expanded arrow target to its own Button.");
    }
    private static GameObject Receiver(Canvas canvas, Vector2 point)
    {
        var hits = new List<RaycastResult>();
        canvas.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) { position = point }, hits);
        return hits.Count == 0 ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
    }
    private static Rect TouchBounds(Button button, Camera camera)
    {
        RectTransform rect = (RectTransform)button.transform;
        Rect bounds = ScreenBounds(rect, camera);
        Graphic image = (Graphic)button.GetComponent<Image>() ?? button.targetGraphic;
        Vector4 padding = image != null ? image.raycastPadding : Vector4.zero;
        float x = bounds.width / rect.rect.width, y = bounds.height / rect.rect.height;
        return Rect.MinMaxRect(bounds.xMin + padding.x * x, bounds.yMin + padding.y * y,
            bounds.xMax - padding.z * x, bounds.yMax - padding.w * y);
    }
    private static Rect ScreenBounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static GameObject New(Scene scene, string name, params Type[] components)
    {
        var obj = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(obj, scene); return obj;
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
