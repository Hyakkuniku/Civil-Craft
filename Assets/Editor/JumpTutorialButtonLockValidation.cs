#if UNITY_EDITOR
using System;
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

/// <summary>Explicit detached check; never authors or saves a gameplay scene.</summary>
[InitializeOnLoad]
public static class JumpTutorialButtonLockValidation
{
    private const string RequestPath = "Temp/jump-tutorial-button-lock.request";
    private const string ReportPath = "Logs/jump-tutorial-button-lock.txt";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextPoll;
    private static bool running;

    static JumpTutorialButtonLockValidation() { EditorApplication.update += Poll; }

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

    [MenuItem("Tools/Civil Craft/UI/Validate Tutorial Jump Lock (Read Only)")]
    public static void Validate()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        running = true;
        var report = new StringBuilder("Tutorial Jump lock: detached native Button validation\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        int active = SceneManager.GetActiveScene().handle;
        var hashes = scenes.Where(scene => !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
            .ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene preview = default;
        RenderTexture previousRender = RenderTexture.active;
        try
        {
            Button sourceJump = Object.FindObjectsOfType<Button>(true).FirstOrDefault(IsJump);
            RunToggleButton run = Object.FindObjectOfType<RunToggleButton>(true);
            Check(sourceJump != null && run != null, "Open CanyonCrossing or Multiplayer in Edit Mode for the authored button comparison.");
            Button sourceRun = run.GetComponent<Button>();
            Check(sourceRun != null && sourceJump.transition == Selectable.Transition.ColorTint &&
                sourceRun.transition == Selectable.Transition.ColorTint, "Both controls must use their authored ColorTint.");
            Check(Same(sourceRun.colors.disabledColor, sourceJump.colors.disabledColor),
                "The authored Jump and Sprint disabled colors differ.");
            string jumpBefore = EditorJsonUtility.ToJson(sourceJump), runBefore = EditorJsonUtility.ToJson(sourceRun);
            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Detached Jump Lock Canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(720, 320);
            Button sprint = CopyButton(sourceRun, root.transform, new Vector2(-225, 15));
            Button jump = CopyButton(sourceJump, root.transform, new Vector2(0, 15));
            Button normal = CopyButton(sourceJump, root.transform, new Vector2(225, 15));
            sprint.interactable = false;
            var visual = jump.gameObject.AddComponent<JumpTutorialButtonLock>();
            MethodInfo apply = typeof(JumpTutorialButtonLock).GetMethod("ApplyLock", Private);
            Check(apply != null, "Runtime lock method is missing.");
            int clicks = 0; jump.onClick.AddListener(() => clicks++);
            var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            apply.Invoke(visual, new object[] { true });
            Refresh(jump); Refresh(sprint); Refresh(normal);
            Check(jump.interactable && !jump.IsInteractable(), "Tutorial lock must disable effective interaction without overwriting the authored Button flag.");
            Check(visual.GetComponents<CanvasGroup>().Length == 1, "The visual must own exactly one temporary gate.");
            Check(Same(jump.targetGraphic.canvasRenderer.GetColor(), sprint.targetGraphic.canvasRenderer.GetColor()),
                "The native Jump disabled tint does not match Sprint.");
            jump.OnPointerClick(pointer); Check(clicks == 0, "A locked native Button accepted a click.");
            Label(root.transform, "SPRINT LOCKED", new Vector2(-225, -105));
            Label(root.transform, "JUMP LOCKED", new Vector2(0, -105));
            Label(root.transform, "JUMP UNLOCKED", new Vector2(225, -105));
            Render(preview, root.transform, report);
            report.AppendLine("PASS: native UGUI matches Sprint gray/half-alpha tint; locked Jump rejects clicks, with sprite/color/shape unchanged.");

            apply.Invoke(visual, new object[] { false }); Refresh(jump);
            Check(jump.IsInteractable(), "Unlock did not restore normal effective interaction.");
            jump.OnPointerClick(pointer); Check(clicks == 1, "Unlocked Jump click did not fire exactly once.");
            jump.interactable = false;
            apply.Invoke(visual, new object[] { true }); apply.Invoke(visual, new object[] { false }); Refresh(jump);
            Check(!jump.interactable && !jump.IsInteractable(), "Unlock overrode another Button restriction.");
            jump.interactable = true;
            var external = root.AddComponent<CanvasGroup>(); external.interactable = false;
            apply.Invoke(visual, new object[] { true }); apply.Invoke(visual, new object[] { false }); Refresh(jump);
            Check(!jump.IsInteractable() && !external.interactable, "Unlock overrode the parent panel's restriction.");
            external.interactable = true; Refresh(jump);
            apply.Invoke(visual, new object[] { true });
            typeof(JumpTutorialButtonLock).GetMethod("OnDisable", Private).Invoke(visual, null); Refresh(jump);
            Check(jump.IsInteractable(), "Disabling the visual left a stale lock.");
            Check(jump.onClick.GetPersistentEventCount() == 0 && clicks == 1,
                "Detached visual altered click listeners or invoked gameplay.");
            Check(EditorJsonUtility.ToJson(sourceJump) == jumpBefore && EditorJsonUtility.ToJson(sourceRun) == runBefore,
                "An authored control was changed by detached validation.");
            report.AppendLine("PASS: unlock, prior Button disable, parent panel disable, cleanup and exactly-once native click behavior.");
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
                    Check(SceneManager.GetSceneAt(i).handle == scenes[i].handle && scenes[i].isDirty == dirty[i],
                        "Scene order or dirty flags changed.");
                foreach (var entry in hashes) Check(Hash(entry.Key) == entry.Value, "A saved user scene changed.");
                report.AppendLine("PASS: original scene setup, active scene, dirty flags and saved scene bytes preserved. No scene saves or player-data changes.");
            }
            catch (Exception exception) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(exception.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            running = false;
        }
    }

    private static bool IsJump(Button button)
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            if (button.onClick.GetPersistentMethodName(i) == nameof(PlayerMotor.Jump)) return true;
        return false;
    }

    private static Button CopyButton(Button source, Transform parent, Vector2 position)
    {
        var owner = new GameObject(source.name + " Preview", typeof(RectTransform), typeof(Image), typeof(Button));
        owner.transform.SetParent(parent, false);
        var rect = (RectTransform)owner.transform; rect.anchoredPosition = position; rect.sizeDelta = new Vector2(130, 130);
        Image image = owner.GetComponent<Image>(); Image original = source.targetGraphic as Image;
        image.sprite = original != null ? original.sprite : null;
        image.color = original != null ? original.color : Color.white;
        image.preserveAspect = true;
        Button button = owner.GetComponent<Button>(); button.targetGraphic = image; button.colors = source.colors;
        button.transition = source.transition;
        return button;
    }

    private static void Refresh(Button button)
    {
        typeof(Selectable).GetMethod("OnCanvasGroupChanged", Private).Invoke(button, null);
        object state = typeof(Selectable).GetProperty("currentSelectionState", Private).GetValue(button);
        typeof(Selectable).GetMethod("DoStateTransition", Private).Invoke(button, new[] { state, (object)true });
    }

    private static void Label(Transform parent, string title, Vector2 position)
    {
        var owner = new GameObject(title, typeof(RectTransform), typeof(Text)); owner.transform.SetParent(parent, false);
        var rect = (RectTransform)owner.transform; rect.anchoredPosition = position; rect.sizeDelta = new Vector2(220, 45);
        var text = owner.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = title; text.fontSize = 19; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
    }

    private static void Render(Scene scene, Transform parent, StringBuilder report)
    {
        var owner = new GameObject("Detached Jump Preview Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(owner, scene);
        Camera camera = owner.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 160;
        camera.transform.position = new Vector3(0, 0, -1000); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.18f, .14f, .11f, 1); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
        camera.useOcclusionCulling = false; camera.scene = scene;
        var target = new RenderTexture(720, 320, 24); target.Create(); camera.targetTexture = target;
        Texture2D image = null;
        try
        {
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            image = new Texture2D(720, 320, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, 720, 320), 0, 0); image.Apply();
            Directory.CreateDirectory("Temp/JumpTutorialButtonLock");
            const string path = "Temp/JumpTutorialButtonLock/LockedComparison.png";
            File.WriteAllBytes(path, image.EncodeToPNG()); report.AppendLine("Preview: " + path);
        }
        finally { camera.targetTexture = null; if (image != null) Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target); }
    }

    private static bool Same(Color a, Color b) => Mathf.Abs(a.r-b.r) < .001f && Mathf.Abs(a.g-b.g) < .001f && Mathf.Abs(a.b-b.b) < .001f && Mathf.Abs(a.a-b.a) < .001f;
    private static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
