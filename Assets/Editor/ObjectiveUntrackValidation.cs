#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in previews of copied Objectives graphics. Never invokes navigation or save callbacks.</summary>
[InitializeOnLoad]
public static class ObjectiveUntrackValidation
{
    public const string RequestPath = "Temp/objective-untrack-validation.request";
    public const string ReportPath = "Logs/objective-untrack-validation.txt";
    private static bool running;
    private static double nextPoll;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    static ObjectiveUntrackValidation() { EditorApplication.update += Poll; }
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath); Run();
    }

    [MenuItem("Tools/Civil Craft/Objectives/Validate Untrack Button (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder("Objectives Untrack: detached graphics, control bindings and mobile layout\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.GetRootGameObjects()).ToArray();
        var files = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        ObjectiveTrackerUI tracker = ObjectiveTrackerUI.Instance;
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData player = saves != null ? saves.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null; float time = Time.timeScale;
        try
        {
            ObjectiveTrackerUI[] sources = Resources.FindObjectsOfTypeAll<ObjectiveTrackerUI>().Where(source => source != null &&
                source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(source.gameObject.scene)).ToArray();
            Require(sources.Length > 0, "Open a gameplay scene containing its authored Objectives panel first.");
            foreach (ObjectiveTrackerUI source in sources) Capture(source, report);
            report.Insert(0, "PASS\n");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            try
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Loaded scene setup changed.");
                for (int index = 0; index < scenes.Length; index++)
                    Require(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isDirty == dirty[index] &&
                        scenes[index].GetRootGameObjects().SequenceEqual(roots[index]), "Source scene order, roots or dirty flags changed: " + scenes[index].path);
                Require(files.All(pair => Hash(pair.Key) == pair.Value), "Saved scene bytes changed.");
                Require(Selection.activeObject == selected && Selection.objects.SequenceEqual(selection) && Time.timeScale == time,
                    "Selection or time scale changed.");
                Require(ObjectiveTrackerUI.Instance == tracker && PlayerDataManager.Instance == saves &&
                    (player == null || ReferenceEquals(player, saves.CurrentData) && JsonUtility.ToJson(player) == playerJson), "Live singleton or player data changed.");
                report.AppendLine("PASS: live scene setup, source graphics/callbacks, saved bytes, selection, time scale and player data preserved. No game button was invoked or scene saved.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }
    }

    private static void Capture(ObjectiveTrackerUI source, StringBuilder report)
    {
        Require(source.trackerPanel != null && source.listPanel != null && source.detailsPanel != null && source.navigateButton != null,
            "Missing authored Objectives panels or Track button in " + source.gameObject.scene.name);
        var originals = source.trackerPanel.GetComponentsInChildren<Component>(true).Cast<Object>().Append(source).Distinct()
            .ToDictionary(item => item, EditorJsonUtility.ToJson);
        var originalCalls = source.trackerPanel.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
        var fonts = source.trackerPanel.GetComponentsInChildren<TMP_Text>(true).Select(text => text.font).Where(font => font != null).Distinct()
            .ToDictionary(font => font, EditorJsonUtility.ToJson);
        Scene preview = EditorSceneManager.NewPreviewScene(); RenderTexture previous = RenderTexture.active;
        try
        {
            GameObject root = Owned(preview, "Detached Objectives Canvas", typeof(RectTransform), typeof(Canvas));
            root.transform.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = (RectTransform)root.transform;
            var map = new Dictionary<Transform, RectTransform>();
            RectTransform panel = CloneArt((RectTransform)source.trackerPanel.transform, root.transform, map);
            // No game behaviours are cloned; the inactive controller is used only for its styling helpers.
            GameObject owner = Owned(preview, "Detached inactive Objectives controller"); owner.SetActive(false);
            ObjectiveTrackerUI copy = owner.AddComponent<ObjectiveTrackerUI>();
            copy.trackerPanel = panel.gameObject; copy.listPanel = Remap(source.listPanel, map); copy.detailsPanel = Remap(source.detailsPanel, map);
            copy.navigateButton = Remap(source.navigateButton, map); copy.completeButton = Remap(source.completeButton, map);
            copy.rewardContainer = Remap(source.rewardContainer, map); copy.questListContent = source.questListContent != null && map.TryGetValue(source.questListContent, out RectTransform content) ? content : null;
            copy.questTabPrefab = source.questTabPrefab; copy.roundedPanelSprite = source.roundedPanelSprite;
            copy.titleText = Text(source.titleText, map); copy.descriptionText = Text(source.descriptionText, map);
            copy.budgetText = Text(source.budgetText, map); copy.weightText = Text(source.weightText, map);
            copy.rewardGoldText = Text(source.rewardGoldText, map); copy.rewardExpText = Text(source.rewardExpText, map);
            copy.untrackButton = Remap(source.untrackButton, map);
            PrepareOwnedArt(preview, copy);
            // Only the copied Untrack callback is rebound to the inactive preview
            // controller. All other authored callbacks remain untouched and none run.
            Button copiedUntrack = panel.Find("Untrack Objective").GetComponent<Button>();
            for (int index = 0; index < copiedUntrack.onClick.GetPersistentEventCount(); index++)
                if (copiedUntrack.onClick.GetPersistentTarget(index) == source &&
                    copiedUntrack.onClick.GetPersistentMethodName(index) == nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked))
                    UnityEventTools.RegisterPersistentListener(copiedUntrack.onClick, index, copy.OnUntrackObjectiveClicked);
            var calls = map.Keys.Where(item => item.GetComponent<Button>() != null && map[item].GetComponent<Button>() != copiedUntrack)
                .ToDictionary(item => map[item].GetComponent<Button>(), item => JsonUtility.ToJson(map[item].GetComponent<Button>().onClick));
            panel.gameObject.SetActive(true);
            Invoke(copy, "EnsureUntrackButton");
            GameObject untrack = copy.untrackButton; Require(untrack != null, "Runtime Untrack button was not created.");
            Button untrackControl = untrack.GetComponent<Button>(); int id = untrack.GetInstanceID();
            int children = panel.GetComponentsInChildren<Transform>(true).Length;
            Invoke(copy, "EnsureUntrackButton");
            Require(copy.untrackButton.GetInstanceID() == id && children == panel.GetComponentsInChildren<Transform>(true).Length &&
                panel.GetComponentsInChildren<Button>(true).Count(button => button.gameObject.name == "Untrack Objective") == 1,
                "Repeated initialization duplicates the Untrack control.");
            ValidateUntrackBinding(untrackControl, copy);
            copy.ApplySharedVisualStyle();
            Require(copy.untrackButton == untrack,
                "Repeated shared styling duplicates the Untrack callback.");
            ValidateUntrackBinding(untrackControl, copy);
            Require(calls.All(pair => pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value),
                "Styling changed an existing Close, Track, Accept or Collect persistent callback.");
            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) text.isOrthographic = true;
            TMP_Text label = untrack.GetComponentInChildren<TMP_Text>(true);
            Require(label != null && label.text == "UNTRACK OBJECTIVE" && !label.raycastTarget && label.font != null,
                "Untrack label is missing, intercepts taps or has no gameplay font.");
            GameObject cameraObject = Owned(preview, "Detached Objectives preview camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100f; camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(119, 156, 165, 255); canvas.worldCamera = camera;
            TMP_Text heading = panel.GetComponentsInChildren<TMP_Text>(true).Single(text => text.transform.parent == panel && text.text == "OBJECTIVES");
            CanvasScaler sourceScaler = source.trackerPanel.GetComponentInParent<CanvasScaler>(true);
            Vector2 reference = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(1920f, 1080f);
            float match = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
            foreach (Vector2Int screen in new[] { new Vector2Int(1600, 720), new Vector2Int(1920, 1080), new Vector2Int(1440, 1080) })
            {
                float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
                canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / scale; canvas.scaleFactor = scale;
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = canvasRect.rect.height * .005f;
                var texture = new RenderTexture(screen.x, screen.y, 24); texture.Create(); camera.targetTexture = texture;
                try
                {
                panel.GetComponent<ObjectiveMobileFit>().ApplyLayout(); untrack.SetActive(true); untrack.transform.SetAsLastSibling();
                Rect bounds = Bounds((RectTransform)untrack.transform, camera);
                Require(Contains(camera.pixelRect, Bounds(panel, camera)) && Contains(Bounds(panel, camera), bounds), "Panel or Untrack control leaves the preview screen.");
                Require(!bounds.Overlaps(Bounds(heading.rectTransform, camera)) && !bounds.Overlaps(Bounds((RectTransform)copy.listPanel.transform, camera)) &&
                    !bounds.Overlaps(Bounds((RectTransform)copy.detailsPanel.transform, camera)), "Untrack overlaps the heading or page panels.");
                Require(bounds.height >= 44f, "Untrack touch target is smaller than 44 pixels.");
                foreach (bool details in new[] { false, true })
                {
                    copy.listPanel.SetActive(!details); copy.detailsPanel.SetActive(details);
                    if (details)
                    {
                        if (copy.titleText != null) copy.titleText.text = "Follow Reed";
                        if (copy.descriptionText != null) copy.descriptionText.text = "Follow Reed to the factory crossing, then talk to him about the bridge.";
                        if (copy.budgetText != null) copy.budgetText.gameObject.SetActive(false);
                        if (copy.weightText != null) copy.weightText.gameObject.SetActive(false);
                        if (copy.rewardContainer != null) copy.rewardContainer.SetActive(false);
                        if (copy.completeButton != null) copy.completeButton.SetActive(false);
                        copy.navigateButton.SetActive(true);
                    }
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
                    foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                    foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(false)) graphic.Rebuild(CanvasUpdate.PreRender);
                    label.ForceMeshUpdate(true, true);
                    Require(label.GetParsedText() == "UNTRACK OBJECTIVE" && !label.isTextOverflowing && !label.isTextTruncated && label.fontSize >= 21f,
                        "Untrack text is truncated, overflowing or shrunk below the shared action size.");
                    foreach (TMP_CharacterInfo character in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                        if (character.isVisible) Require(Contains(bounds, GlyphBounds(label, character, camera)), "An Untrack glyph leaves its button.");
                    string path = "Temp/ObjectiveUntrack/" + source.gameObject.scene.name + "-" + (details ? "Details" : "List") + "-" + screen.x + "x" + screen.y + ".png";
                    Render(camera, texture, path);
                    report.AppendLine("PASS: " + source.gameObject.scene.name + " " + (details ? "Details" : "List") + " " + screen.x + "x" + screen.y +
                        "; one Untrack callback, original action bindings preserved, complete glyphs, in-bounds header and " + bounds.height.ToString("0.0") + "px touch height. " + path);
                }
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; texture.Release(); Object.DestroyImmediate(texture); }
            }
        }
        finally
        {
            RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview);
            bool originalSafe = RecordDifferences(originals, source.gameObject.scene.name, "component", report);
            bool fontsSafe = RecordDifferences(fonts.ToDictionary(pair => (Object)pair.Key, pair => pair.Value), source.gameObject.scene.name, "font", report);
            bool callbacksSafe = originalCalls.All(pair => pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value);
            Require(originalSafe && fontsSafe && callbacksSafe, "Detached validation changed source Objectives graphics, callbacks or font assets; see exact diagnostics above.");
        }
    }

    private static int CountRuntimeCalls(UnityEvent calls, Object target, string name)
    {
        IList prepared = (IList)typeof(UnityEventBase).GetMethod("PrepareInvoke", Hidden).Invoke(calls, null);
        int result = 0;
        foreach (object call in prepared)
            for (Type type = call.GetType(); type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (field.GetValue(call) is Delegate callback) result += callback.GetInvocationList().Count(item => ReferenceEquals(item.Target, target) && item.Method.Name == name);
        // RuntimeOnly persistent calls are intentionally absent from PrepareInvoke
        // in Edit Mode. Include their runtime contribution without invoking them.
        if (!Application.isPlaying)
            for (int index = 0; index < calls.GetPersistentEventCount(); index++)
                if (calls.GetPersistentListenerState(index) == UnityEventCallState.RuntimeOnly &&
                    calls.GetPersistentTarget(index) == target && calls.GetPersistentMethodName(index) == name) result++;
        return result;
    }
    private static void ValidateUntrackBinding(Button button, ObjectiveTrackerUI owner)
    {
        UnityEvent calls = button.onClick;
        if (calls.GetPersistentEventCount() > 0)
            Require(calls.GetPersistentEventCount() == 1 && calls.GetPersistentTarget(0) == owner &&
                calls.GetPersistentMethodName(0) == nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked) &&
                calls.GetPersistentListenerState(0) == UnityEventCallState.RuntimeOnly,
                "Authored Untrack must have one RuntimeOnly callback to the preview owner.");
        Require(CountRuntimeCalls(calls, owner, nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked)) == 1,
            "Untrack must have exactly one effective runtime click callback, including its authored binding.");
    }
    private static bool RecordDifferences(Dictionary<Object, string> before, string scene, string kind, StringBuilder report)
    {
        bool unchanged = true;
        foreach (var pair in before)
        {
            string after = pair.Key != null ? EditorJsonUtility.ToJson(pair.Key) : "<destroyed>";
            if (after == pair.Value) continue;
            unchanged = false; int offset = 0, limit = Math.Min(pair.Value.Length, after.Length);
            while (offset < limit && pair.Value[offset] == after[offset]) offset++;
            int start = Math.Max(0, offset - 90);
            string stem = "Logs/ObjectiveUntrackChanges/" + scene + "-" + kind + "-" + (pair.Key != null ? pair.Key.GetInstanceID().ToString() : "destroyed");
            Directory.CreateDirectory("Logs/ObjectiveUntrackChanges"); File.WriteAllText(stem + "-before.json", pair.Value); File.WriteAllText(stem + "-after.json", after);
            report.AppendLine("SOURCE CHANGE: " + (pair.Key != null ? pair.Key.name + " [" + pair.Key.GetType().Name + "]" : "destroyed") +
                "; first difference at " + offset + "; before=" + pair.Value.Substring(start, Math.Min(260, pair.Value.Length - start)) +
                "; after=" + after.Substring(Math.Min(start, after.Length), Math.Min(260, after.Length - Math.Min(start, after.Length))) + "; JSON: " + stem + "-{before,after}.json");
        }
        return unchanged;
    }
    private static void PrepareOwnedArt(Scene preview, ObjectiveTrackerUI copy)
    {
        // Runtime helpers normally allocate then parent their controls. Prepare
        // only detached fixtures so no transient root enters the live scene.
        GameObject Child(Transform parent, string name, params Type[] components)
        {
            Transform existing = parent.Find(name); if (existing != null) return existing.gameObject;
            GameObject value = Owned(preview, name, components); value.transform.SetParent(parent, false); return value;
        }
        GameObject clip = Child(copy.trackerPanel.transform, "Clipboard Clip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Child(clip.transform, "Clip Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        GameObject untrack = Child(copy.trackerPanel.transform, "Untrack Objective", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        Child(untrack.transform, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        GameObject back = Child(copy.detailsPanel.transform, "Back to Objective List", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        GameObject backLabel = Child(back.transform, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI label = backLabel.GetComponent<TextMeshProUGUI>();
        if (copy.titleText != null) label.font = copy.titleText.font;
        label.text = "BACK TO LIST"; label.raycastTarget = false;
        if (copy.rewardContainer != null)
            foreach (string name in new[] { "CoinRewardIcon", "ExperienceRewardIcon" })
                Child(copy.rewardContainer.transform, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    }
    private static void Invoke(ObjectiveTrackerUI owner, string name) => typeof(ObjectiveTrackerUI).GetMethod(name, Hidden).Invoke(owner, null);
    private static GameObject Remap(GameObject source, Dictionary<Transform, RectTransform> map) => source != null && map.TryGetValue(source.transform, out RectTransform copy) ? copy.gameObject : null;
    private static TextMeshProUGUI Text(TextMeshProUGUI source, Dictionary<Transform, RectTransform> map) => source != null && map.TryGetValue(source.transform, out RectTransform copy) ? copy.GetComponent<TextMeshProUGUI>() : null;
    private static GameObject Owned(Scene scene, string name, params Type[] components)
    {
        GameObject item = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
        SceneManager.MoveGameObjectToScene(item, scene); return item;
    }
    private static RectTransform CloneArt(RectTransform source, Transform parent, Dictionary<Transform, RectTransform> map)
    {
        var item = EditorUtility.CreateGameObjectWithHideFlags(source.name, HideFlags.HideAndDontSave, typeof(RectTransform)); item.layer = source.gameObject.layer;
        RectTransform rect = (RectTransform)item.transform; rect.SetParent(parent, false);
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition; rect.localScale = source.localScale; rect.localRotation = source.localRotation;
        map.Add(source, rect);
        foreach (Type type in new[] { typeof(Image), typeof(RawImage), typeof(TextMeshProUGUI), typeof(Button), typeof(RectMask2D), typeof(Mask), typeof(VerticalLayoutGroup), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement) })
        {
            Component original = source.GetComponent(type); if (original == null) continue;
            Component copy = item.AddComponent(type); EditorUtility.CopySerialized(original, copy);
            if (copy is Graphic graphic) graphic.raycastTarget = false;
            if (copy is TMP_Text text) text.isOrthographic = true;
        }
        foreach (Transform child in source) if (child is RectTransform childRect) CloneArt(childRect, rect, map);
        item.SetActive(source.gameObject.activeSelf); return rect;
    }
    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners); Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        foreach (Vector3 corner in corners) { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static Rect GlyphBounds(TMP_Text text, TMP_CharacterInfo character, Camera camera)
    {
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.bottomLeft));
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.topRight));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f && inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;
    private static void Render(Camera camera, RenderTexture target, string path)
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); Directory.CreateDirectory("Temp/ObjectiveUntrack"); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
        finally { Object.DestroyImmediate(pixels); }
    }
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
