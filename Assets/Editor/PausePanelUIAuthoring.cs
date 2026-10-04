using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>One-shot scene authoring, never a runtime UI generator.</summary>
public static class PausePanelUIAuthoring
{
    private static readonly string[] Paths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private const string Request = "Temp/pause-panel-ui-authoring.request";
    private const string Report = "Temp/PausePanelUIValidation.txt";
    private static readonly Color Wood = new Color32(90, 55, 31, 255);
    private static readonly Color Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255);
    private static readonly Color Red = new Color32(167, 65, 49, 255);
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= Check;
        EditorApplication.update += Check;
    }

    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Paths.Any(path => SceneManager.GetSceneByPath(path).isDirty))
        {
            File.WriteAllText(Report, "WAIT: Save CanyonCrossing/BHAN HOUSE before authoring. No unsaved scene has been overwritten.");
            return;
        }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Author Pause Panel Theme")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode first.");
        if (Paths.Any(path => SceneManager.GetSceneByPath(path).isDirty))
            throw new Exception("Save current scene edits first. No unsaved scene will be overwritten.");
        var rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        if (rounded == null || font == null) throw new Exception("Existing rounded panel sprite or themed font missing.");
        var report = new StringBuilder();
        foreach (string path in Paths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var pause = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PauseManager>(true)).Single();
                GameObject panel = pause.pausePanel;
                var buttons = panel.GetComponentsInChildren<Button>(true);
                Button resume = Action(buttons, pause, nameof(PauseManager.ResumeGame));
                Button modes = Action(buttons, pause, nameof(PauseManager.ReturnToModeSelection));
                Button settings = buttons.Single(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount())
                    .Any(i => b.onClick.GetPersistentMethodName(i) == "OpenSettings"));
                Button achievements = buttons.SingleOrDefault(b => b.name == "btn_Achievements");
                string originalActions = Events(buttons);
                string pauseRules = Rules(pause);
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
                panelRect.pivot = new Vector2(.5f, .5f);
                panelRect.anchoredPosition = Vector2.zero;
                panelRect.sizeDelta = new Vector2(720, 660);
                panelRect.localScale = Vector3.one;
                panelRect.localRotation = Quaternion.identity;
                Paint(panel.GetComponent<Image>(), rounded, Wood, true);
                var background = panel.transform.Find("BG").gameObject;
                Place(background, 0, 0, 1, 1);
                ((RectTransform)background.transform).sizeDelta = new Vector2(-16, -16);
                Paint(background.GetComponent<Image>(), rounded, Cream, true);
                background.transform.SetAsFirstSibling();
                var content = background.transform.Find("Content");
                foreach (LayoutGroup layout in content.GetComponents<LayoutGroup>()) layout.enabled = false;
                foreach (ContentSizeFitter fitter in content.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
                Place(content.gameObject, 0, 0, 1, 1);
                var header = panel.transform.Find("Image");
                if (header != null)
                {
                    Place(header.gameObject, .08f, .83f, .92f, .96f);
                    foreach (Image image in header.GetComponentsInChildren<Image>(true)) image.enabled = false;
                    var title = header.GetComponentInChildren<TMP_Text>(true);
                    title.transform.SetParent(header, false);
                    StyleText(title, font, "PAUSED", 46, Ink);
                    Place(title.gameObject, 0, 0, 1, 1);
                }
                var divider = ImageChild(background.transform, "Pause Header Divider");
                Paint(divider, null, new Color32(177, 131, 76, 255), false);
                Place(divider.gameObject, .12f, .8f, .88f, .804f);
                StyleButton(resume, rounded, font, Gold, "RESUME", .64f, .77f);
                StyleButton(settings, rounded, font, new Color32(239, 219, 177, 255), "SETTINGS", .48f, .61f);
                if (achievements != null)
                {
                    StyleButton(achievements, rounded, font, new Color32(239, 219, 177, 255), "ACHIEVEMENTS", .48f, .61f);
                    Place(settings.gameObject, .1f, .48f, .485f, .61f);
                    Place(achievements.gameObject, .515f, .48f, .9f, .61f);
                    foreach (TMP_Text text in achievements.GetComponentsInChildren<TMP_Text>(true))
                        StyleText(text, font, "ACHIEVEMENTS", 26, Ink);
                }
                var leaveCaption = Label(content, "Pause Leave Caption", font, "LEAVE THE CURRENT WORLD", 20);
                Place(leaveCaption.gameObject, .1f, .40f, .9f, .455f);
                leaveCaption.characterSpacing = 2;
                StyleButton(modes, rounded, font, Cream, "MODE SELECTION", .255f, .385f);
                var main = content.Find("BackToMainMenuButton");
                Button mainButton;
                if (main == null)
                {
                    var obj = new GameObject("BackToMainMenuButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                    obj.layer = panel.layer; obj.transform.SetParent(content, false);
                    mainButton = obj.GetComponent<Button>();
                    Label(obj.transform, "Label", font, "BACK TO MAIN MENU", 29);
                    UnityEventTools.AddPersistentListener(mainButton.onClick, pause.ReturnToMainMenu);
                }
                else mainButton = main.GetComponent<Button>();
                StyleButton(mainButton, rounded, font, Red, "BACK TO MAIN MENU", .08f, .21f);
                // Explicit navigation avoids jumps into hidden HUD or settings controls.
                var ordered = achievements == null ? new[] { resume, settings, modes, mainButton }
                    : new[] { resume, settings, achievements, modes, mainButton };
                for (int i = 0; i < ordered.Length; i++)
                    ordered[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                        selectOnUp = ordered[(i + ordered.Length - 1) % ordered.Length],
                        selectOnDown = ordered[(i + 1) % ordered.Length] };
                pause.mainMenuSceneName = "Main Menu";
                if (originalActions != Events(buttons) || pauseRules != Rules(pause))
                    throw new Exception("Existing actions or pause rules were changed.");
                if (mainButton.onClick.GetPersistentEventCount() != 1 ||
                    mainButton.onClick.GetPersistentTarget(0) != pause ||
                    mainButton.onClick.GetPersistentMethodName(0) != nameof(PauseManager.ReturnToMainMenu))
                    throw new Exception("Main Menu action is missing or duplicated.");
                if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == "Assets/Scenes/Main Menu.unity"))
                    throw new Exception("Main Menu is missing from enabled build scenes.");
                EditorUtility.SetDirty(pause);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Preview(panel, scene.name);
                report.AppendLine("PASS: " + path + ": authored layout saved; Resume/Settings/Mode Selection callbacks and pause rules unchanged; Main Menu listener and build scene verified; 16:9 and 16:10 previews rendered.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        report.AppendLine("Live pause/resume and scene transitions still require a Play Mode test.");
        File.WriteAllText(Report, report.ToString());
        Debug.Log("[Pause panel authoring] " + report);
    }

    private static string Rules(PauseManager pause)
    {
        var data = new SerializedObject(pause);
        return string.Join("|", new[] { "allowPauseInBuildMode", "freezeTimeOnPause" }.Select(p => data.FindProperty(p).boolValue)) +
            "|" + pause.modeSelectionSceneName + "|" + pause.settingsPanel.GetInstanceID();
    }
    private static Button Action(Button[] buttons, Object target, string method) => buttons.Single(b =>
        Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentTarget(i) == target && b.onClick.GetPersistentMethodName(i) == method));
    private static string Events(Button[] buttons) => string.Join("|", buttons.SelectMany(b =>
        Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Select(i => b.onClick.GetPersistentTarget(i).GetInstanceID() + ":" + b.onClick.GetPersistentMethodName(i))));
    private static void Place(GameObject obj, float left, float bottom, float right, float top)
    {
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static Image ImageChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) return child.GetComponent<Image>();
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false); return obj.GetComponent<Image>();
    }
    private static void Paint(Image image, Sprite sprite, Color color, bool raycast)
    {
        image.enabled = true; image.sprite = sprite; image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color; image.raycastTarget = raycast;
    }
    private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, string value, float size)
    {
        var child = parent.Find(name);
        var obj = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TMP_Text>(); StyleText(text, font, value, size, Ink); return text;
    }
    private static void StyleText(TMP_Text text, TMP_FontAsset font, string value, float size, Color color)
    {
        text.font = font; text.fontSharedMaterial = font.material; text.text = value; text.color = color;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size; text.enableAutoSizing = true; text.fontSizeMin = size * .8f; text.fontSizeMax = size;
        text.richText = true; text.raycastTarget = false; text.margin = Vector4.zero;
        text.overflowMode = TextOverflowModes.Truncate;
    }
    private static void StyleButton(Button button, Sprite rounded, TMP_FontAsset font, Color fill, string value, float bottom, float top)
    {
        Place(button.gameObject, .1f, bottom, .9f, top);
        Paint(button.GetComponent<Image>(), rounded, Wood, true);
        var inset = ImageChild(button.transform, "Image");
        Place(inset.gameObject, 0, 0, 1, 1); ((RectTransform)inset.transform).sizeDelta = new Vector2(-8, -8);
        Paint(inset, rounded, fill, false); inset.transform.SetAsFirstSibling();
        button.targetGraphic = inset; button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1, .95f, .82f); colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.83f, .76f, .65f); colors.disabledColor = new Color(.6f, .6f, .6f, .7f); button.colors = colors;
        foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
        {
            StyleText(text, font, value, 30, fill == Red ? new Color32(255, 248, 231, 255) : Ink);
            Place(text.gameObject, .035f, .06f, .965f, .94f); text.transform.SetAsLastSibling();
        }
    }

    private static void Preview(GameObject panel, string sceneName)
    {
        // Clone only the authored UI into an isolated preview scene. No gameplay
        // behaviours, Photon rooms, Time.timeScale changes, or original transforms.
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var canvasObj = new GameObject("Pause Preview Canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObj, preview);
            canvasObj.layer = 5;
            var canvas = canvasObj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.pivot = new Vector2(.5f, .5f);
            canvasRect.position = new Vector3(10000, 10000, 10000);
            canvasRect.localScale = Vector3.one * .01f;
            var copy = Object.Instantiate(panel, canvas.transform, false); copy.SetActive(true);
            var group = copy.GetComponent<CanvasGroup>(); if (group != null) group.alpha = 1;
            var cameraObj = new GameObject("Pause Preview Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObj, preview);
            var camera = cameraObj.GetComponent<Camera>(); camera.scene = preview; camera.orthographic = true;
            camera.orthographicSize = 5.4f; camera.nearClipPlane = .1f; camera.farClipPlane = 20;
            camera.transform.position = canvasRect.position + Vector3.back * 10;
            camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(83, 67, 53, 255); canvas.worldCamera = camera;
            foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 800) })
            {
                canvasRect.sizeDelta = new Vector2(1080f * size.x / size.y, 1080);
                var target = new RenderTexture(size.x, size.y, 24); target.Create();
                try
                {
                    camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                    Canvas.ForceUpdateCanvases();
                    foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
                    if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                    else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                        new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    var image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                        File.WriteAllBytes($"Temp/PausePanel_{sceneName}_{size.x}x{size.y}.png", image.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(image); }
                }
                finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            }
        }
        finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview); }
    }
}
