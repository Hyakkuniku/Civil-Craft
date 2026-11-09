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

/// <summary>Authors one chat action and verifies detached host/guest layouts.</summary>
public static class RandomChallengeChatUIAuthoring
{
    public const string ButtonName = "Random Challenge Button";
    public const string ReportPath = "Logs/random-challenge-chat-ui.txt";
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string PreviewFolder = "Logs/RandomChallengeChatUI";
    private const string Request = "Temp/random-challenge-chat-ui.request";
    private static double nextPoll;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || !File.Exists(Request) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextPoll = EditorApplication.timeSinceStartup + 2d;
        File.Delete(Request);
        ApplyAndValidate();
    }

    [MenuItem("Tools/Civil Craft/Multiplayer/Author Random Challenge Chat Button")]
    public static void ApplyAndValidate()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
            "Run in idle Edit Mode after compilation.");
        Check(PrefabStageUtility.GetCurrentPrefabStage() == null, "Close Prefab Mode first.");
        var report = new StringBuilder();
        Scene active = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        Object[] selection = Selection.objects;
        bool dirty = false, saved = false;
        int undo = -1;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            dirty = scene.isDirty;
            SessionChatUI[] candidates = All<SessionChatUI>(scene);
            Check(candidates.Length == 1, "Expected exactly one authored chat service.");
            SessionChatUI ui = candidates[0];
            GameObject panel = Ref<GameObject>(ui, "panel");
            ScrollRect scroll = Ref<ScrollRect>(ui, "messageScroll");
            Button send = Ref<Button>(ui, "sendButton");
            Check(panel != null && scroll != null && send != null, "Chat panel, message viewport or Send binding is missing.");
            RectTransform frame = scroll.transform.parent as RectTransform;
            TMP_Text title = frame != null ? frame.Find("Session Chat Title")?.GetComponent<TMP_Text>() : null;
            Button close = frame != null ? frame.Find("Close Session Chat")?.GetComponent<Button>() : null;
            Check(title != null && title.font != null && close != null, "Existing chat header is incomplete.");
            var oldButtons = All<Button>(scene).Where(button => button.name != ButtonName)
                .ToDictionary(button => button, button => EditorJsonUtility.ToJson(button));
            var oldRefs = new Dictionary<string, Object>();
            foreach (string name in new[] { "panel", "openButton", "messageInput", "messages", "messageScroll", "sendButton", "status", "unreadBadge" })
                oldRefs[name] = new SerializedObject(ui).FindProperty(name).objectReferenceValue;

            // Every preview is detached and precedes source changes.
            foreach (Vector2Int size in new[] { new Vector2Int(1600, 720), new Vector2Int(1440, 1080), new Vector2Int(1126, 337) })
                foreach (bool host in new[] { true, false }) Render(panel, send, title.font, size, host, report);

            Button action = Ref<Button>(ui, "randomChallengeButton");
            if (action == null)
            {
                Check(!All<Button>(scene).Any(button => button.name == ButtonName), "A custom unbound Random Challenge button needs review.");
                string sourceText = File.ReadAllText(ScenePath);
                string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-RandomChallengeChat", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(backup); File.Copy(ScenePath, Path.Combine(backup, "CanyonCrossing.unity"));
                report.AppendLine("BACKUP: " + backup);
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author host Random Challenge chat action");
                Undo.RecordObject(ui, "Bind Random Challenge action");
                action = CreateButton(frame, send, title.font);
                Undo.RegisterCreatedObjectUndo(action.gameObject, "Create Random Challenge action");
                UnityEventTools.AddPersistentListener(action.onClick, ui.SendRandomChallenge);
                var serialized = new SerializedObject(ui); serialized.FindProperty("randomChallengeButton").objectReferenceValue = action;
                serialized.ApplyModifiedProperties();
                action.gameObject.SetActive(false);
                foreach (Component component in action.GetComponentsInChildren<Component>(true)) EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(ui); EditorSceneManager.MarkSceneDirty(scene);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo);
                ValidateSource(ui, action, oldButtons, oldRefs);
                if (!dirty)
                {
                    Check(EditorSceneManager.SaveScene(scene), "Could not save the authored chat action.");
                    saved = true;
                    // ExecuteAlways widgets and TMP can rewrite unrelated defaults
                    // during a scene save. Retain exact pre-command documents.
                    RestoreUnrelatedDocuments(sourceText, ui, frame, action);
                    report.AppendLine("SAVED: One inactive authored button; existing source documents preserved.");
                }
                else report.AppendLine("WAIT SAVE: Existing unsaved edits and the new button remain unsaved.");
            }
            else
            {
                ValidateSource(ui, action, oldButtons, oldRefs);
                report.AppendLine("UNCHANGED: The authored chat action is already valid.");
            }
            report.AppendLine("PASS: Host/guest phone, tablet and short-screen previews; separate 48dp host action; native Challenge/Close/Send/Input raycasts; guest history geometry restored.");
            report.AppendLine("PASS: No invitations, gameplay clicks, chat sends, player saves, contracts or world progression were invoked.");
            report.AppendLine("RESULT: PASS; UTC=" + DateTime.UtcNow.ToString("O"));
            report.Insert(0, "PASS\n");
        }
        catch (Exception error)
        {
            if (undo >= 0 && !saved) Undo.RevertAllDownToGroup(undo);
            report.Insert(0, "FAIL\n"); report.AppendLine(error.ToString());
            throw;
        }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty)
                EditorSceneManager.CloseScene(scene, !setup.Any(item => item.path == ScenePath));
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selection;
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
        }
        Debug.Log(report.ToString());
    }

    private static void ValidateSource(SessionChatUI ui, Button button, Dictionary<Button, string> oldButtons, Dictionary<string, Object> oldRefs)
    {
        Check(button != null && !button.gameObject.activeSelf && button.name == ButtonName &&
            button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == ui &&
            button.onClick.GetPersistentMethodName(0) == nameof(SessionChatUI.SendRandomChallenge), "Challenge button binding/default is invalid.");
        foreach (var item in oldButtons) Check(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value, "Existing button changed: " + item.Key?.name);
        foreach (var item in oldRefs) Check(new SerializedObject(ui).FindProperty(item.Key).objectReferenceValue == item.Value, "Existing chat binding changed: " + item.Key);
    }

    private static Button CreateButton(RectTransform parent, Button source, TMP_FontAsset font)
    {
        GameObject obj = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false); obj.layer = parent.gameObject.layer;
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.08f, .78f); rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(440f, 80f);
        var image = obj.GetComponent<Image>(); Image original = source.targetGraphic as Image;
        image.sprite = original != null ? original.sprite : null; image.type = Image.Type.Sliced;
        image.color = new Color32(232, 152, 31, 255); image.raycastTarget = true;
        Button button = obj.GetComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors; colors.highlightedColor = new Color32(255, 226, 176, 255);
        colors.pressedColor = new Color32(210, 154, 72, 255); colors.disabledColor = new Color32(149, 138, 117, 180); button.colors = colors;
        var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(obj.transform, false); textObject.layer = obj.layer;
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>(); label.font = font;
        label.text = "RANDOM CHALLENGE"; label.fontSize = 30f; label.enableAutoSizing = true; label.fontSizeMin = 24f; label.fontSizeMax = 30f;
        label.color = new Color32(79, 49, 30, 255); label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false; label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(18f, 8f); label.rectTransform.offsetMax = new Vector2(-18f, -8f);
        obj.SetActive(false);
        return button;
    }

    private static void Render(GameObject sourcePanel, Button sourceSend, TMP_FontAsset font, Vector2Int size, bool host, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null, previous = RenderTexture.active;
        Texture2D pixels = null; Camera camera = null; Button action = null;
        try
        {
            GameObject cameraObject = New(preview, "Detached chat camera"); camera = cameraObject.AddComponent<Camera>();
            camera.scene = preview; camera.enabled = false; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.orthographicSize = size.y * .005f; camera.aspect = size.x / (float)size.y;
            camera.transform.position = new Vector3(0f, 0f, -20f); camera.nearClipPlane = .05f; camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(75, 103, 126, 255);
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            target = new RenderTexture(size.x, size.y, 24) { hideFlags = HideFlags.HideAndDontSave };
            Check(target.Create(), "Could not create detached chat render target."); camera.targetTexture = target;
            float scale = Mathf.Sqrt(size.x / 1920f * (size.y / 1080f));
            GameObject canvasObject = New(preview, "Detached chat canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(size.x, size.y) / scale;
            canvasObject.transform.localScale = Vector3.one * (.01f * scale);
            GameObject panel = Object.Instantiate(sourcePanel, canvasObject.transform, false); panel.SetActive(true);
            ScrollRect scroll = panel.GetComponentInChildren<ScrollRect>(true);
            RectTransform frame = scroll.transform.parent as RectTransform;
            Button send = panel.GetComponentsInChildren<Button>(true).First(button => button.name == sourceSend.name);
            Button close = panel.GetComponentsInChildren<Button>(true).First(button => button.name == "Close Session Chat");
            TMP_InputField input = panel.GetComponentInChildren<TMP_InputField>(true);
            action = frame.Find(ButtonName)?.GetComponent<Button>() ?? CreateButton(frame, send, font);
            Vector2 originalMin = ((RectTransform)scroll.transform).anchorMin, originalMax = ((RectTransform)scroll.transform).anchorMax;
            Canvas.ForceUpdateCanvases();
            // Exercise host -> guest -> host so a hidden action returns the full history viewport.
            float dpi = size.x == 1600 ? 320f : 240f;
            RandomChallengeChatLayout.Apply(action, scroll, true, size, dpi, true); Canvas.ForceUpdateCanvases();
            RandomChallengeChatLayout.Apply(action, scroll, false, size, dpi, true); Canvas.ForceUpdateCanvases();
            Check(((RectTransform)scroll.transform).anchorMin == originalMin && ((RectTransform)scroll.transform).anchorMax == originalMax,
                "Guest chat did not restore the original history bounds.");
            RandomChallengeChatLayout.Apply(action, scroll, host, size, dpi, true); Canvas.ForceUpdateCanvases();
            TMP_Text messages = scroll.content.GetComponent<TMP_Text>();
            string sample = string.Join("\n\n", Enumerable.Range(1, 18).Select(index => SessionChatHistory.FormatLine(index % 2 == 0 ? "Guest Engineer" : "Host Engineer", "Ready for the next bridge challenge?")));
            messages.text = sample; messages.ForceMeshUpdate();
            messages.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(scroll.viewport.rect.height, messages.preferredHeight + 24f));
            scroll.verticalNormalizedPosition = 1f;
            input.SetTextWithoutNotify("Let's build!"); input.ForceLabelUpdate();
            foreach (TMP_Text label in action.GetComponentsInChildren<TMP_Text>(true))
            {
                label.ForceMeshUpdate(true, true);
                Check(label.preferredWidth <= label.rectTransform.rect.width + 1f && label.preferredHeight <= label.rectTransform.rect.height + 1f,
                    "Challenge label is clipped with the actual gameplay font.");
            }
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            if (host)
            {
                Rect hit = TouchBounds(action, camera);
                float minimum = 48f * dpi / 160f;
                Check(hit.width >= minimum - 1f && hit.height >= minimum - 1f, "Challenge action is below the mobile touch size.");
                Check(hit.xMin >= 0f && hit.xMax <= size.x && hit.yMin >= 0f && hit.yMax <= size.y, "Challenge touch area escapes the screen.");
                foreach (RectTransform existing in new[] { (RectTransform)close.transform, (RectTransform)send.transform, input.GetComponent<RectTransform>(), scroll.viewport })
                    Check(!hit.Overlaps(Bounds(existing, camera)), "Challenge action overlaps existing chat controls or message viewport: " + existing.name);
                Check(Receiver(canvas, hit.center) == action.gameObject, "Native challenge raycast is not routed to its button.");
            }
            else Check(!action.gameObject.activeSelf, "Guest chat exposes the host-only action.");
            Check(Receiver(canvas, Bounds((RectTransform)close.transform, camera).center) == close.gameObject, "Close chat no longer receives its tap.");
            Check(Receiver(canvas, Bounds((RectTransform)send.transform, camera).center) == send.gameObject, "Send chat no longer receives its tap.");
            Check(Receiver(canvas, Bounds(input.GetComponent<RectTransform>(), camera).center) == input.gameObject, "Chat input no longer receives its tap.");
            Check(messages.text == sample, "Layout changes changed message history.");
            RenderTexture.active = target; pixels = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); pixels.Apply();
            Directory.CreateDirectory(PreviewFolder);
            string file = PreviewFolder + "/" + size.x + "x" + size.y + (host ? "-host" : "-guest") + ".png";
            File.WriteAllBytes(file, pixels.EncodeToPNG()); report.AppendLine("PASS: " + file + "; full action text, host-only visibility, separate touch targets and existing chat raycasts.");
        }
        finally
        {
            if (action != null) RandomChallengeChatLayout.Release(action);
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void RestoreUnrelatedDocuments(string before, SessionChatUI ui, RectTransform frame, Button action)
    {
        string after = File.ReadAllText(ScenePath);
        string serviceId = LocalId(ui), frameId = LocalId(frame), buttonId = LocalId(action), rectId = LocalId(action.transform);
        var originals = Regex.Matches(before, @"(?ms)^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\z)").Cast<Match>()
            .ToDictionary(match => match.Groups[1].Value, match => match.Value);
        string restored = Regex.Replace(after, @"(?ms)^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\z)", match =>
        {
            string id = match.Groups[1].Value;
            if (!originals.TryGetValue(id, out string original)) return match.Value;
            if (id == serviceId)
            {
                string line = "  randomChallengeButton: {fileID: " + buttonId + "}\n";
                return Regex.IsMatch(original, @"(?m)^  randomChallengeButton:")
                    ? Regex.Replace(original, @"(?m)^  randomChallengeButton:.*\r?\n", line) : original.TrimEnd('\r', '\n') + "\n" + line;
            }
            if (id == frameId) return Regex.Replace(original, @"(?m)^  m_Father:", "  - {fileID: " + rectId + "}\n  m_Father:");
            return original;
        });
        File.WriteAllText(ScenePath, restored, new UTF8Encoding(false));
    }

    private static string LocalId(Object obj)
    {
        Check(obj != null, "Cannot resolve a missing authored object.");
        ulong id = GlobalObjectId.GetGlobalObjectIdSlow(obj).targetObjectId;
        Check(id != 0, "Cannot resolve authored local identifier.");
        return id.ToString();
    }
    private static T Ref<T>(SessionChatUI ui, string field) where T : Object => new SerializedObject(ui).FindProperty(field)?.objectReferenceValue as T;
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static GameObject New(Scene scene, string name, params Type[] components)
    { var obj = new GameObject(name, components); SceneManager.MoveGameObjectToScene(obj, scene); obj.layer = 5; return obj; }
    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners) { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static Rect TouchBounds(Button button, Camera camera)
    {
        var rect = (RectTransform)button.transform; Rect bounds = Bounds(rect, camera);
        Vector4 p = button.targetGraphic.raycastPadding;
        float x = bounds.width / rect.rect.width, y = bounds.height / rect.rect.height;
        return Rect.MinMaxRect(bounds.xMin + p.x * x, bounds.yMin + p.y * y, bounds.xMax - p.z * x, bounds.yMax - p.w * y);
    }
    private static GameObject Receiver(Canvas canvas, Vector2 point)
    { var hits = new List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) { position = point }, hits); return hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null; }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
