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

/// <summary>Detached visual check; never opens real readers/dialogue or saves a scene/profile.</summary>
[InitializeOnLoad]
public static class LiveLoadReviewGuidanceValidation
{
    private const string RequestPath = "Temp/live-load-review-guidance.request";
    private const string ReportPath = "Logs/live-load-review-guidance.txt";
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static bool running;
    private static double nextPoll;

    static LiveLoadReviewGuidanceValidation() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Story/Validate Live Load Review Guidance (Read Only)")]
    public static void Validate()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        running = true;
        var report = new StringBuilder("Live Load review: detached native guidance check\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        int active = SceneManager.GetActiveScene().handle;
        var hashes = scenes.Where(scene => !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
            .ToDictionary(scene => scene.path, scene => Hash(scene.path));
        RenderTexture previousRender = RenderTexture.active;
        Scene preview = default;
        try
        {
            NPCProgressionManager bhan = Object.FindObjectsOfType<NPCProgressionManager>(true)
                .FirstOrDefault(npc => npc.gameObject.scene.name == "CanyonCrossing" && npc.ProgressionSaveId == "MainContractNPC");
            Check(bhan != null && bhan.AuthoredPhases.Count > 2, "Open CanyonCrossing in Edit Mode for the authored Live Load check.");
            Check(bhan.AuthoredPhases[1].phaseId == "LiveLoad" && bhan.AuthoredPhases[2].phaseId == "WorkBench",
                "The original inspection-to-workbench phase IDs/order must remain unchanged.");
            Check(bhan.AuthoredPhases[1].repeatDialogue && bhan.AuthoredPhases[1].phaseDialogue != null &&
                bhan.AuthoredPhases[1].phaseDialogue.sentences.Any(line => line.Contains("Inspect")),
                "The pre-inspection authored reminder must remain available.");
            LessonTrigger trigger = Object.FindObjectsOfType<LessonTrigger>(true).FirstOrDefault(candidate =>
                candidate.gameObject.scene == bhan.gameObject.scene &&
                new SerializedObject(candidate).FindProperty("advanceNPC").objectReferenceValue == bhan);
            Check(trigger != null, "Bhan's vehicle lesson trigger was not found.");
            var settings = new SerializedObject(trigger);
            Check(settings.FindProperty("requireAlmanacReviewBeforeAdvance").boolValue &&
                settings.FindProperty("completionSaveKey").stringValue == "Bhan_LiveLoad_Inspection" &&
                settings.FindProperty("requiredNPCPhaseIndex").intValue == 1 &&
                settings.FindProperty("nextNPCPhaseIndex").intValue == 2,
                "The saved inspection/review gate or completion identity changed.");
            string[] reminder = trigger.GetAlmanacReviewReminder();
            Check(reminder.Length >= 2 && string.Join(" ", reminder).Contains("Almanac") &&
                string.Join(" ", reminder).Contains(trigger.Lesson.Title) &&
                !reminder.Any(line => line.Contains("Inspect the vehicle")),
                "Post-inspection guidance must be narrative, lesson-specific, and not repeat the inspection command.");
            foreach (string line in reminder) report.AppendLine("Bhan: " + line);

            AlmanacManager book = Object.FindObjectsOfType<AlmanacManager>(true)
                .FirstOrDefault(candidate => candidate.gameObject.scene == bhan.gameObject.scene);
            TutorialManager manager = Object.FindObjectsOfType<TutorialManager>(true)
                .FirstOrDefault(candidate => candidate.gameObject.scene == bhan.gameObject.scene);
            Check(book != null && book.hudOpenButton != null && manager != null && manager.SharedPointer != null,
                "The authored Almanac HUD button/shared tutorial arrow is missing.");
            string bookBefore = EditorJsonUtility.ToJson(book), triggerBefore = EditorJsonUtility.ToJson(trigger);
            string npcBefore = EditorJsonUtility.ToJson(bhan), pointerBefore = EditorJsonUtility.ToJson(manager.SharedPointer);
            MethodInfo factory = typeof(NPCOrderedLearningFlow).GetMethod("CreateReviewSteps", PrivateStatic);
            Check(factory != null, "Review step factory is missing.");
            var steps = (TutorialStep[])factory.Invoke(null, new object[] { book, "Live Load lesson" });
            Check(steps.Length == 2 && steps[0].usePointer && steps[0].pointerTarget == book.hudOpenButton.GetComponent<RectTransform>() &&
                !steps[0].advanceOnClick && !steps[0].showNextButton && !steps[0].canSkip && !steps[1].usePointer,
                "The first step must point to the exact HUD control without auto-advancing; the open-book step must clear the arrow.");
            var missing = (TutorialStep[])factory.Invoke(null, new object[] { null, null });
            Check(!missing[0].usePointer && missing[0].pointerTarget == null,
                "Missing HUD references must not create a floating/misdirected pointer.");
            report.AppendLine("PASS: original phases and trigger identity retained; actual production steps target the exact HUD button, never advance on click, and clear on book review.");

            Check(bhan.AuthoredPhases.Count > 4 && bhan.AuthoredPhases[3].contract != null &&
                bhan.AuthoredPhases[3].contract.ContractID == "TUT_CONTRACT1" &&
                bhan.AuthoredPhases[3].contract.finishedContractDialogue.sentences.Any(line => line.Contains("review your first entry")),
                "The existing Beam completion/first-entry dialogue is missing.");
            NPCProgressionPhase firstEntryPhase = bhan.AuthoredPhases[4];
            Check(firstEntryPhase.phaseId == "DeadLoad" && firstEntryPhase.playDialogueOnArrival && firstEntryPhase.reviewAlmanacWhileTravelling &&
                firstEntryPhase.optionalDialogueSequence[0].stepType == NPCOptionalDialogueStep.StepType.AlmanacReview &&
                firstEntryPhase.optionalDialogueSequence[0].reviewStepId == "training-bridge-record-review",
                "The first-entry guidance must start during the existing post-turn-in walk, without an extra NPC tap.");
            var firstEntrySteps = (TutorialStep[])factory.Invoke(null, new object[] { book, "first entry" });
            Check(firstEntrySteps[0].usePointer && firstEntrySteps[0].pointerTarget == steps[0].pointerTarget &&
                firstEntrySteps[0].pointerOffset == steps[0].pointerOffset &&
                firstEntrySteps[0].pointerRotation == steps[0].pointerRotation &&
                !firstEntrySteps[0].advanceOnClick && !firstEntrySteps[1].usePointer,
                "Post-Beam first-entry guidance must use the same tested click-through HUD arrow and real review gate.");
            report.AppendLine("PASS: Beam first-entry review offers this same HUD arrow during the paid turn-in walk; original dialogue, review ID and open/close authority retained. Follow-up stays arrival-gated.");

            foreach (Vector2Int resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 1200) })
            {
                preview = EditorSceneManager.NewPreviewScene();
                RenderPointer(preview, resolution, book.hudOpenButton.GetComponent<RectTransform>(), manager.SharedPointer, steps[0], report);
                EditorSceneManager.ClosePreviewScene(preview);
                preview = default;
            }
            Check(EditorJsonUtility.ToJson(book) == bookBefore && EditorJsonUtility.ToJson(trigger) == triggerBefore &&
                EditorJsonUtility.ToJson(bhan) == npcBefore && EditorJsonUtility.ToJson(manager.SharedPointer) == pointerBefore,
                "An authored story/UI component was modified by detached validation.");
        }
        catch (Exception exception) { report.Insert(0, "FAILED\n"); report.AppendLine(exception.ToString()); }
        finally
        {
            RenderTexture.active = previousRender;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            try
            {
                Check(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene().handle == active,
                    "Loaded/active scene setup changed.");
                for (int i = 0; i < scenes.Length; i++)
                    Check(SceneManager.GetSceneAt(i).handle == scenes[i].handle && scenes[i].isDirty == dirty[i], "Scene order or dirty flags changed.");
                foreach (var entry in hashes) Check(Hash(entry.Key) == entry.Value, "A saved scene changed.");
                report.AppendLine("PASS: original scene setup, dirty flags and saved scene bytes preserved; no scene saves or player-data access.");
            }
            catch (Exception exception) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(exception.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            running = false;
        }
    }

    private static void RenderPointer(Scene scene, Vector2Int resolution, RectTransform sourceButton,
        TutorialPointer sourcePointer, TutorialStep step, StringBuilder report)
    {
        float width = 1920, height = width * resolution.y / resolution.x;
        var root = new GameObject("Detached Almanac Guidance", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, scene);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        canvas.scaleFactor = resolution.x / width;
        ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
        var cameraOwner = new GameObject("Detached Guidance Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraOwner, scene);
        Camera camera = cameraOwner.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = height / 2;
        camera.transform.position = new Vector3(0, 0, -1000); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
        camera.scene = scene; camera.useOcclusionCulling = false; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.32f, .24f, .19f); canvas.worldCamera = camera;
        RectTransform target = CopyArt(sourceButton, root.transform, "Almanac HUD (detached)");
        target.anchorMin = target.anchorMax = new Vector2(0, 1);
        target.anchoredPosition = sourceButton.anchoredPosition; target.localScale = Vector3.one;
        RectTransform arrow = CopyArt((RectTransform)sourcePointer.transform, root.transform, "Review Arrow (detached)");
        arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, .5f); arrow.localScale = Vector3.one;
        var pointer = arrow.gameObject.AddComponent<TutorialPointer>();
        pointer.bounceAmount = sourcePointer.bounceAmount; pointer.bounceSpeed = sourcePointer.bounceSpeed;
        typeof(TutorialPointer).GetMethod("Awake", PrivateInstance).Invoke(pointer, null);
        arrow.localEulerAngles = new Vector3(0, 0, step.pointerRotation);
        var texture = new RenderTexture(resolution.x, resolution.y, 24); texture.Create(); camera.targetTexture = texture;
        Texture2D image = null;
        try
        {
            Canvas.ForceUpdateCanvases(); pointer.PointAt(target, step.pointerOffset); Canvas.ForceUpdateCanvases();
            Check(pointer.IsPointingAt(target) && pointer.gameObject.activeInHierarchy && arrow.GetComponent<Canvas>().enabled,
                "The native pointer is not visible/targeted.");
            Check(pointer.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget),
                "The arrow intercepts UI raycasts.");
            var corners = new Vector3[4]; arrow.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 point = camera.WorldToViewportPoint(corner);
                Check(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1, "The arrow is clipped by the viewport.");
            }
            camera.Render();
            var raycast = new List<RaycastResult>();
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(target.rect.center));
            root.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) { position = buttonPoint }, raycast);
            Check(raycast.Any(hit => hit.gameObject.transform == target || hit.gameObject.transform.IsChildOf(target)) &&
                !raycast.Any(hit => hit.gameObject.transform == arrow || hit.gameObject.transform.IsChildOf(arrow)),
                "The native HUD raycast missed its control or was intercepted by the arrow.");
            RenderTexture.active = texture;
            image = new Texture2D(resolution.x, resolution.y, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0); image.Apply();
            Directory.CreateDirectory("Temp/LiveLoadReviewGuidance");
            string path = "Temp/LiveLoadReviewGuidance/AlmanacPointer-" + resolution.x + "x" + resolution.y + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            pointer.Hide(); Check(!pointer.IsPointingAt(target) && !pointer.gameObject.activeSelf, "The pointer failed to clear.");
            report.AppendLine("PASS: " + resolution.x + "x" + resolution.y + " native arrow visible/in bounds; HUD raycast passes; Hide clears. Preview: " + path);
        }
        finally
        {
            camera.targetTexture = null; if (image != null) Object.DestroyImmediate(image);
            texture.Release(); Object.DestroyImmediate(texture);
        }
    }

    // Copy presentation only, never the HUD's live buttons, callbacks or controllers.
    private static RectTransform CopyArt(RectTransform source, Transform parent, string name)
    {
        var owner = new GameObject(name, typeof(RectTransform)); owner.transform.SetParent(parent, false);
        var rect = (RectTransform)owner.transform;
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale; rect.localRotation = source.localRotation;
        Image original = source.GetComponent<Image>();
        if (original != null)
        {
            var image = owner.AddComponent<Image>(); image.sprite = original.sprite; image.color = original.color;
            image.type = original.type; image.preserveAspect = original.preserveAspect; image.raycastTarget = original.raycastTarget;
        }
        for (int i = 0; i < source.childCount; i++)
            if (source.GetChild(i) is RectTransform child && child.gameObject.activeSelf) CopyArt(child, rect, child.name);
        return rect;
    }

    private static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
