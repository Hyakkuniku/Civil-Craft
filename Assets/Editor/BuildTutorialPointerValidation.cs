#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Detached rendering/binding checks only. Never authors or saves gameplay objects.</summary>
[InitializeOnLoad]
public static class BuildTutorialPointerValidation
{
    const string Request = "Temp/build-tutorial-pointers.request", Report = "Logs/build-tutorial-pointers.txt";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static bool running;
    static double nextPoll;
    static BuildTutorialPointerValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request); Validate();
    }
    [MenuItem("Tools/Civil Craft/Tutorials/Validate Build Arrows (Detached)")]
    public static void Validate()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        running = true;
        var report = new StringBuilder("Build tutorial arrows: native detached rendering and binding audit\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        int active = SceneManager.GetActiveScene().handle;
        var hashes = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene preview = default;
        RenderTexture oldRender = RenderTexture.active;
        try
        {
            BuildTutorialDirector director = Object.FindObjectsOfType<BuildTutorialDirector>(true)
                .Single(item => item.gameObject.scene.name == "CanyonCrossing");
            TutorialSequence[] sequences = Object.FindObjectsOfType<TutorialSequence>(true)
                .Where(item => item.gameObject.scene == director.gameObject.scene &&
                    (item.lessonName == "Sequence_Build1" || item.lessonName == "Sequence_Build2")).ToArray();
            Check(sequences.Length == 2 && director.bouncingArrow != null, "Open CanyonCrossing with the two existing build tutorials.");
            TutorialManager manager = Object.FindObjectsOfType<TutorialManager>(true)
                .Single(item => item.gameObject.scene == director.gameObject.scene);
            Check(manager.SharedPointer == director.bouncingArrow, "The audited shared arrow binding differs.");
            string[] originalSteps = sequences.Select(EditorJsonUtility.ToJson).ToArray();
            string originalDirector = EditorJsonUtility.ToJson(director);
            int targets = 0;
            foreach (TutorialSequence sequence in sequences)
                for (int i = 0; i < sequence.tutorialSteps.Length; i++)
                {
                    TutorialStep step = sequence.tutorialSteps[i];
                    if (!step.usePointer) continue;
                    Check(step.pointerTarget != null, sequence.lessonName + " has a missing arrow target at " + i);
                    report.AppendLine("Target: " + sequence.lessonName + "[" + i + "] -> " + Path(step.pointerTarget));
                    targets++;
                }
            Check(director.materialMappings.All(mapping => mapping.material != null && mapping.buttonRect != null), "A material-arrow mapping is missing.");
            Check(director.undoButtonRect != null, "Undo's existing arrow target is missing.");
            BuildUIController controls = Object.FindObjectsOfType<BuildUIController>(true)
                .Single(item => item.gameObject.scene == director.gameObject.scene);
            TutorialSequence beam = sequences.Single(item => item.lessonName == "Sequence_Build1");
            TutorialSequence truss = sequences.Single(item => item.lessonName == "Sequence_Build2");
            // Camera reset is intentionally wired straight to the camera, not
            // through BuildUIController's optional tool binding.
            Button cameraButton = beam.tutorialSteps[5].pointerTarget.GetComponent<Button>();
            Check(cameraButton != null && Enumerable.Range(0, cameraButton.onClick.GetPersistentEventCount())
                .Any(index => cameraButton.onClick.GetPersistentTarget(index) is BuildCameraController &&
                    cameraButton.onClick.GetPersistentMethodName(index) == nameof(BuildCameraController.ResetCameraRotation)),
                "The camera-reset arrow is not bound to the existing camera-reset action.");
            foreach (var pair in new[] { (beam.tutorialSteps[11], BuildModeTool.Statistics), (beam.tutorialSteps[12], BuildModeTool.Grid),
                (beam.tutorialSteps[27], BuildModeTool.Simulate), (truss.tutorialSteps[4], BuildModeTool.Delete),
                (truss.tutorialSteps[5], BuildModeTool.Move), (truss.tutorialSteps[12], BuildModeTool.Select),
                (truss.tutorialSteps[14], BuildModeTool.Copy), (truss.tutorialSteps[16], BuildModeTool.Paste) })
            {
                RectTransform actual = controls.GetToolRectTransform(pair.Item2), authored = pair.Item1.pointerTarget;
                Check(actual != null && (actual == authored || actual.IsChildOf(authored) || authored.IsChildOf(actual)),
                    "An authored arrow differs from the control's actual binding: " + pair.Item2);
            }
            report.AppendLine("PASS: Camera reset's direct binding and Statistics/Grid/Play/Erase/Move/Select/Copy/Paste toolbar bindings match their authored arrow targets.");
            report.AppendLine("PASS: " + targets + " authored step targets, material mappings and Undo reference intact; dynamic director prompts retain their existing targets.");
            RectTransform sample = director.materialMappings[0].buttonRect;
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 1200), new Vector2Int(2340, 1080) })
            {
                preview = EditorSceneManager.NewPreviewScene();
                Render(preview, size, sample, director.bouncingArrow, report);
                EditorSceneManager.ClosePreviewScene(preview); preview = default;
            }
            Check(sequences.Select(EditorJsonUtility.ToJson).SequenceEqual(originalSteps) &&
                EditorJsonUtility.ToJson(director) == originalDirector, "Detached check changed an authored tutorial or director.");
            report.AppendLine(BeamTutorialBuildValidation.ValidateDetached());
            report.AppendLine(TrussTutorialBuildValidation.ValidateReadOnly(director.gameObject.scene));
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            RenderTexture.active = oldRender;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            try
            {
                Check(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene().handle == active, "Scene setup changed.");
                for (int i = 0; i < scenes.Length; i++) Check(SceneManager.GetSceneAt(i).handle == scenes[i].handle && scenes[i].isDirty == dirty[i], "Scene order/dirty state changed.");
                foreach (var pair in hashes) Check(Hash(pair.Key) == pair.Value, "Saved scene bytes changed.");
                report.AppendLine("PASS: scene setup, dirty flags and saved bytes preserved. No scene/player save edits.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(Report, report.ToString()); running = false;
        }
    }

    static void Render(Scene scene, Vector2Int size, RectTransform sample, TutorialPointer source, StringBuilder report)
    {
        const float width = 1920;
        float height = width * size.y / size.x;
        var root = new GameObject("Detached build-arrow canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, scene);
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)root.transform).sizeDelta = new Vector2(width, height); canvas.scaleFactor = size.x / width;
        var cameraOwner = new GameObject("Detached arrow camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraOwner, scene);
        Camera camera = cameraOwner.GetComponent<Camera>(); camera.scene = scene; camera.useOcclusionCulling = false;
        camera.orthographic = true; camera.orthographicSize = height / 2;
        camera.transform.position = new Vector3(0, 0, -1000); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .2f, .4f); canvas.worldCamera = camera;
        var target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
        Texture2D pixels = null;
        var pointers = new List<TutorialPointer>();
        try
        {
            Vector2[] positions = { new Vector2(-width / 2 + 110, -height / 2 + 70), new Vector2(width / 2 - 110, height / 2 - 70),
                new Vector2(width / 2 - 110, -height / 2 + 70), new Vector2(-width / 2 + 110, height / 2 - 70), new Vector2(0, 0) };
            for (int i = 0; i < positions.Length; i++)
            {
                RectTransform button = Frame(sample, root.transform, "Detached target " + i);
                button.anchorMin = button.anchorMax = new Vector2(.5f, .5f); button.pivot = new Vector2(.5f, .5f);
                button.localScale = Vector3.one; button.localRotation = Quaternion.identity;
                button.sizeDelta = new Vector2(i == 0 ? 150 : 100, 90); button.anchoredPosition = positions[i];
                Image hitSurface = button.GetComponent<Image>();
                if (hitSurface == null) { hitSurface = button.gameObject.AddComponent<Image>(); hitSurface.color = Color.clear; }
                hitSurface.raycastTarget = true; // Detached hit-test surface, not the authored control.
                RectTransform arrow = Art((RectTransform)source.transform, root.transform, "Detached arrow " + i);
                arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, .5f); arrow.localScale = Vector3.one;
                var pointer = arrow.gameObject.AddComponent<TutorialPointer>();
                typeof(TutorialPointer).GetMethod("Awake", Private).Invoke(pointer, null);
                pointer.bounceAmount = source.bounceAmount; pointer.bounceSpeed = source.bounceSpeed;
                Canvas.ForceUpdateCanvases();
                pointer.PointAtBuildTarget(button, i == 1 ? Vector2.zero : new Vector2(0, i == 2 ? -30 : 20), i == 1 ? 0 : 180);
                typeof(TutorialPointer).GetMethod("LateUpdate", Private).Invoke(pointer, null);
                Check(pointer.IsPointingAt(button) && pointer.GetComponent<Canvas>().enabled, "Native build arrow is not visible.");
                Rect arrowBounds = Bounds(arrow, camera), buttonBounds = Bounds(button, camera);
                Check(!arrowBounds.Overlaps(buttonBounds), "Native build arrow overlaps its target graphic.");
                Check(arrowBounds.xMin >= 0 && arrowBounds.yMin >= 0 && arrowBounds.xMax <= size.x && arrowBounds.yMax <= size.y,
                    "Native build arrow is clipped at a viewport edge.");
                Check(pointer.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), "Arrow captures UI clicks.");
                camera.Render(); // Native UGUI populates Graphic.depth during rendering.
                var hits = new List<RaycastResult>();
                root.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) {
                    position = RectTransformUtility.WorldToScreenPoint(camera, button.TransformPoint(button.rect.center)) }, hits);
                Check(hits.Any(hit => hit.gameObject.transform == button || hit.gameObject.transform.IsChildOf(button)) &&
                    !hits.Any(hit => hit.gameObject.transform == arrow || hit.gameObject.transform.IsChildOf(arrow)), "Native arrow interferes with target raycasts.");
                pointers.Add(pointer);
            }
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Temp/BuildTutorialPointers");
            string path = "Temp/BuildTutorialPointers/Arrows-" + size.x + "x" + size.y + ".png";
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            foreach (TutorialPointer pointer in pointers) { pointer.Hide(); Check(!pointer.gameObject.activeSelf, "A hidden arrow remained visible."); }
            // Real scroll clipping: content may extend far outside its viewport.
            var viewport = new GameObject("Detached material viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewRect = (RectTransform)viewport.transform; viewRect.sizeDelta = new Vector2(350, 100);
            RectTransform content = Frame(sample, viewRect, "Wide material content"); content.sizeDelta = new Vector2(1800, 90);
            TutorialPointer clipPointer = pointers[0]; Canvas.ForceUpdateCanvases();
            clipPointer.PointAtBuildTarget(content, new Vector2(0, 20), 180);
            Rect clipBounds = Bounds((RectTransform)clipPointer.transform, camera), viewBounds = Bounds(viewRect, camera);
            Check(Mathf.Abs(clipBounds.center.x - viewBounds.center.x) < 2f, "Wide-content pointer does not align to the visible toolbar.");
            content.anchoredPosition = new Vector2(2500, 0); Canvas.ForceUpdateCanvases();
            typeof(TutorialPointer).GetMethod("LateUpdate", Private).Invoke(clipPointer, null);
            Check(!clipPointer.GetComponent<Canvas>().enabled, "Fully clipped material target left a floating arrow.");
            content.anchoredPosition = Vector2.zero; Canvas.ForceUpdateCanvases();
            typeof(TutorialPointer).GetMethod("LateUpdate", Private).Invoke(clipPointer, null);
            Check(clipPointer.GetComponent<Canvas>().enabled, "Returning target did not restore its arrow.");
            clipPointer.Hide();
            report.AppendLine("PASS: " + size.x + "x" + size.y + " native corner/center/zero-Undo placement, no target overlap/clipping, click-through, hide cleanup. Preview: " + path);
            report.AppendLine("PASS: visible scroll-viewport targeting; fully clipped target hides, and scrolling back restores the arrow.");
        }
        finally { camera.targetTexture = null; if (pixels != null) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); }
    }
    static RectTransform Frame(RectTransform sample, Transform parent, string name)
    {
        var owner = new GameObject(name, typeof(RectTransform), typeof(Image)); owner.transform.SetParent(parent, false);
        owner.GetComponent<Image>().color = new Color(.95f, .88f, .71f);
        Image original = sample.GetComponentsInChildren<Image>(true).FirstOrDefault(item => item.sprite != null);
        if (original != null)
        {
            var art = new GameObject("Original material sprite", typeof(RectTransform), typeof(Image)); art.transform.SetParent(owner.transform, false);
            ((RectTransform)art.transform).sizeDelta = new Vector2(75, 75);
            art.GetComponent<Image>().sprite = original.sprite; art.GetComponent<Image>().preserveAspect = true;
        }
        return (RectTransform)owner.transform;
    }
    static RectTransform Art(RectTransform source, Transform parent, string name)
    {
        var owner = new GameObject(name, typeof(RectTransform)); owner.transform.SetParent(parent, false);
        var rect = (RectTransform)owner.transform; rect.sizeDelta = source.rect.size; rect.pivot = source.pivot;
        Image original = source.GetComponent<Image>();
        if (original != null)
        {
            Image copy = owner.AddComponent<Image>(); copy.sprite = original.sprite; copy.color = original.color;
            copy.type = original.type; copy.preserveAspect = true; copy.raycastTarget = original.raycastTarget;
        }
        foreach (Transform child in source)
            if (child is RectTransform childRect && child.gameObject.activeSelf)
            {
                RectTransform copy = Art(childRect, rect, child.name);
                copy.anchorMin = childRect.anchorMin; copy.anchorMax = childRect.anchorMax; copy.anchoredPosition = childRect.anchoredPosition;
                copy.localScale = childRect.localScale; copy.localRotation = childRect.localRotation;
            }
        return rect;
    }
    static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        foreach (Vector3 corner in corners) { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static string Path(Transform value) => value.parent == null ? value.name : Path(value.parent) + "/" + value.name;
    static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
