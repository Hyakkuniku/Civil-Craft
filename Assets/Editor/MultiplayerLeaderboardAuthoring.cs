using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in scene authoring. Preview checks never access PlayFab or player saves.</summary>
public static class MultiplayerLeaderboardAuthoring
{
    private const string ScenePath = "Assets/Scenes/Mode Selection.unity";
    private const string Request = "Temp/multiplayer-leaderboard-ui-v1.request";
    private const string Report = "Temp/MultiplayerLeaderboardValidation.txt";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;
    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetSceneByPath(ScenePath).isDirty)
        { File.WriteAllText(Report, "WAIT: Save Mode Selection first. No unsaved edits overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    [MenuItem("Tools/Civil Craft/Add Multiplayer Leaderboard")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetSceneByPath(ScenePath).isDirty)
            throw new InvalidOperationException("Stop Play Mode and save Mode Selection first.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LeaderboardPanelUI>(true)).Single();
            var originalRows = Get<LeaderboardRowUI[]>(ui, "rows");
            var originalOpen = Get<Button>(ui, "openButton"); var originalClose = Get<Button>(ui, "closeButton");
            string openAction = JsonUtility.ToJson(originalOpen.onClick), closeAction = JsonUtility.ToJson(originalClose.onClick);
            Style(ui);
            Check(Get<LeaderboardRowUI[]>(ui, "rows").SequenceEqual(originalRows), "Story rows changed.");
            Check(JsonUtility.ToJson(originalOpen.onClick) == openAction && JsonUtility.ToJson(originalClose.onClick) == closeAction,
                "Original open/close callbacks changed.");
            var report = new StringBuilder();
            MultiplayerLeaderboardReportingValidation.Run(report);
            Preview(ui, report);
            Get<GameObject>(ui, "panel").SetActive(false);
            EditorUtility.SetDirty(ui); EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Could not save multiplayer leaderboard.");
            report.AppendLine("PASS: Saved editable Story / Multiplayer leaderboard tabs, 15 authored five-column rows and Refresh. Existing contract rows and callbacks preserved.");
            report.AppendLine("NOTE: Preview data is fictional and temporary. No cloud scores, purchases, story saves or Photon rooms changed. Backend deployment and live two-player check are separate.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Multiplayer leaderboard] Authored scene and isolated checks passed.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void Style(LeaderboardPanelUI ui)
    {
        GameObject panel = Get<GameObject>(ui, "panel");
        CozyModalLayout layout = panel.GetComponent<CozyModalLayout>();
        Check(layout != null && layout.card != null, "Existing cozy leaderboard layout missing.");
        RectTransform card = layout.card;
        Transform tabs = Find(card, "Leaderboard Category Tabs");
        if (tabs == null)
        {
            tabs = NewRect("Leaderboard Category Tabs", card);
            MakeButton(tabs, "Story Board Tab", "CONTRACTS", layout, new Vector2(0, 0), new Vector2(.49f, 1));
            MakeButton(tabs, "Multiplayer Board Tab", "MULTIPLAYER", layout, new Vector2(.51f, 0), Vector2.one);
        }
        Button story = Find(tabs, "Story Board Tab").GetComponent<Button>();
        Button multi = Find(tabs, "Multiplayer Board Tab").GetComponent<Button>();
        TMP_Text guide = Find(card, "Multiplayer Board Guide")?.GetComponent<TMP_Text>();
        if (guide == null) guide = NewText(card, "Multiplayer Board Guide", layout, 28);
        guide.text = "YOUR MULTIPLAYER CAREER\nComplete challenges with another signed-in engineer.";
        guide.enableWordWrapping = true;
        guide.alignment = TextAlignmentOptions.Center;
        guide.gameObject.SetActive(false);

        Transform mpHeader = Find(card, "Multiplayer Column Header");
        if (mpHeader == null)
        {
            mpHeader = Object.Instantiate(Find(card, "Column Header"), card, false);
            mpHeader.name = "Multiplayer Column Header";
            var rate = Find(mpHeader, "Cost Header"); rate.name = "Win Rate Header"; rate.GetComponent<TMP_Text>().text = "WIN RATE";
            var wins = Find(mpHeader, "Stress Header"); wins.name = "Wins Header"; wins.GetComponent<TMP_Text>().text = "WINS";
            var losses = Object.Instantiate(wins, mpHeader, false); losses.name = "Losses Header"; losses.GetComponent<TMP_Text>().text = "LOSSES";
            Find(mpHeader, "Builder Header").GetComponent<TMP_Text>().text = "ENGINEER";
        }
        ScrollRect mpScroll = Get<ScrollRect>(ui, "multiplayerScroll");
        if (mpScroll == null)
        {
            mpScroll = Object.Instantiate(Get<ScrollRect>(ui, "leaderboardScroll"), card, false);
            mpScroll.name = "Multiplayer Leaderboard Viewport"; mpScroll.content.name = "Multiplayer Top 15 Content";
            var originalBar = Get<ScrollRect>(ui, "leaderboardScroll").verticalScrollbar;
            if (originalBar != null)
            {
                var bar = Object.Instantiate(originalBar, card, false); bar.name = "Multiplayer Leaderboard Scrollbar";
                mpScroll.verticalScrollbar = bar;
            }
            foreach (var old in mpScroll.GetComponentsInChildren<LeaderboardRowUI>(true))
            {
                Transform root = old.transform; root.name = root.name.Replace("Leaderboard Row", "Multiplayer Row");
                var row = root.gameObject.AddComponent<MultiplayerLeaderboardRowUI>();
                var rate = Find(root, "Cost"); rate.name = "Win Rate";
                var wins = Find(root, "Peak Stress"); wins.name = "Wins";
                var losses = Object.Instantiate(wins, root, false); losses.name = "Losses";
                var serializedRow = new SerializedObject(row);
                Set(serializedRow, "rankText", Find(root, "Rank").GetComponent<TMP_Text>());
                Set(serializedRow, "builderText", Find(root, "Builder").GetComponent<TMP_Text>());
                Set(serializedRow, "winRateText", rate.GetComponent<TMP_Text>());
                Set(serializedRow, "winsText", wins.GetComponent<TMP_Text>());
                Set(serializedRow, "lossesText", losses.GetComponent<TMP_Text>());
                Set(serializedRow, "background", root.GetComponent<Image>());
                serializedRow.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(old);
            }
        }
        mpHeader.gameObject.SetActive(false); mpScroll.gameObject.SetActive(false);
        if (mpScroll.verticalScrollbar != null) mpScroll.verticalScrollbar.gameObject.SetActive(false);
        Button refresh = Find(card, "Refresh Rankings")?.GetComponent<Button>();
        if (refresh == null) refresh = MakeButton(card, "Refresh Rankings", "REFRESH", layout, Vector2.zero, Vector2.one);
        refresh.GetComponentInChildren<TMP_Text>().name = "Refresh Rankings Label";
        var serialized = new SerializedObject(ui);
        Set(serialized, "storyBoardButton", story); Set(serialized, "multiplayerBoardButton", multi); Set(serialized, "refreshBoardButton", refresh);
        Set(serialized, "storyColumnHeader", Find(card, "Column Header").gameObject); Set(serialized, "multiplayerColumnHeader", mpHeader.gameObject);
        Set(serialized, "multiplayerGuide", guide.gameObject); Set(serialized, "multiplayerScroll", mpScroll);
        Set(serialized, "boardFooter", Find(card, "Top 15 Footer").GetComponent<TMP_Text>());
        var rows = mpScroll.GetComponentsInChildren<MultiplayerLeaderboardRowUI>(true);
        Check(rows.Length == 15, "Expected 15 authored multiplayer rows.");
        var array = serialized.FindProperty("multiplayerRows"); array.arraySize = rows.Length;
        for (int index = 0; index < rows.Length; index++) array.GetArrayElementAtIndex(index).objectReferenceValue = rows[index];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        foreach (var row in rows) { row.Bind(1, "Engineer", 0, 0); row.gameObject.SetActive(false); }
        layout.ApplyLayout();
    }

    private static Button MakeButton(Transform parent, string name, string value, CozyModalLayout layout, Vector2 min, Vector2 max)
    {
        RectTransform rect = NewRect(name, parent); rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        Image image = rect.gameObject.AddComponent<Image>(); image.sprite = layout.roundedSprite;
        image.type = Image.Type.Sliced; image.color = CozyModalLayout.Sand;
        var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = CozyModalLayout.Ink; outline.effectDistance = new Vector2(2f, -2f);
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors; colors.highlightedColor = new Color(1.03f, 1.03f, 1.03f); colors.pressedColor = new Color(.88f, .88f, .88f); button.colors = colors;
        TMP_Text label = NewText(rect, "Label", layout, 28); label.text = value; label.fontStyle = FontStyles.Bold;
        return button;
    }
    private static TMP_Text NewText(Transform parent, string name, CozyModalLayout layout, float size)
    {
        RectTransform rect = NewRect(name, parent); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12, 4); rect.offsetMax = new Vector2(-12, -4);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = layout.font;
        label.color = CozyModalLayout.Ink; label.fontSize = size; label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
    private static RectTransform NewRect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.layer = 5;
        var rect = obj.GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect;
    }
    private static Transform Find(Transform parent, string name) => parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(node => node.name == name);
    private static T Get<T>(LeaderboardPanelUI ui, string name) => (T)typeof(LeaderboardPanelUI).GetField(name, Fields).GetValue(ui);
    private static void Set(SerializedObject serialized, string field, Object value) => serialized.FindProperty(field).objectReferenceValue = value;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void Preview(LeaderboardPanelUI source, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("Leaderboard Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var controllerObject = new GameObject("Leaderboard Preview Controller (Temporary)"); controllerObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            var controller = controllerObject.AddComponent<LeaderboardPanelUI>();
            var originalPanel = Get<GameObject>(source, "panel");
            var panel = Object.Instantiate(originalPanel, canvasObject.transform, false);
            foreach (var motion in panel.GetComponentsInChildren<LeaderboardPanelMotion>(true)) Object.DestroyImmediate(motion);
            foreach (FieldInfo field in typeof(LeaderboardPanelUI).GetFields(Fields))
            {
                if (!field.IsDefined(typeof(SerializeField), false)) continue;
                object original = field.GetValue(source);
                if (original is Object obj)
                {
                    Transform originalTransform = obj is GameObject go ? go.transform : obj is Component component ? component.transform : null;
                    if (originalTransform == null || !originalTransform.IsChildOf(originalPanel.transform) && originalTransform != originalPanel.transform) continue;
                    Transform copy = panel.transform.Find(AnimationUtility.CalculateTransformPath(originalTransform, originalPanel.transform));
                    if (originalTransform == originalPanel.transform) copy = panel.transform;
                    field.SetValue(controller, obj is GameObject ? (Object)copy.gameObject : copy.GetComponent(field.FieldType));
                }
            }
            typeof(LeaderboardPanelUI).GetField("rows", Fields).SetValue(controller, panel.GetComponentsInChildren<LeaderboardRowUI>(true));
            typeof(LeaderboardPanelUI).GetField("multiplayerRows", Fields).SetValue(controller, panel.GetComponentsInChildren<MultiplayerLeaderboardRowUI>(true));
            foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var cameraObject = new GameObject("Leaderboard Preview Camera (Temporary)", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255); canvas.worldCamera = camera;
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2340, 1080) })
            {
                canvasObject.GetComponent<RectTransform>().sizeDelta = size; camera.orthographicSize = size.y / 2f; camera.aspect = (float)size.x / size.y;
                target = new RenderTexture(size.x * 2 / 3, size.y * 2 / 3, 24); target.Create(); camera.targetTexture = target;
                panel.SetActive(true); panel.GetComponent<CozyModalLayout>().ApplyLayout();
                foreach (bool multiplayer in new[] { false, true })
                {
                    typeof(LeaderboardPanelUI).GetField("multiplayerSelected", Fields).SetValue(controller, multiplayer);
                    typeof(LeaderboardPanelUI).GetMethod("ApplyBoardVisibility", Fields).Invoke(controller, null);
                    Get<TMP_Text>(controller, "descriptionText").text = multiplayer ? "MULTIPLAYER  •  Ranked by total wins" : "Lowest cost among successful bridges";
                    Get<TMP_Text>(controller, "personalBestText").text = multiplayer ? "YOUR RECORD  •  24 WINS / 8 LOSSES  •  75.0% WIN RATE" : "YOUR BEST  ₱57,562  •  76.7% peak stress";
                    Get<TMP_Text>(controller, "statusText").text = string.Empty;
                    if (multiplayer)
                    {
                        var rows = Get<MultiplayerLeaderboardRowUI[]>(controller, "multiplayerRows");
                        for (int i = 0; i < rows.Length; i++) { rows[i].gameObject.SetActive(true); rows[i].Bind(i+1, i == 1 ? "hyakkimaru" : "Engineer " + (i+1), 52-i, i+3, i==1); }
                    }
                    else
                    {
                        var rows = Get<LeaderboardRowUI[]>(controller, "rows");
                        for (int i = 0; i < rows.Length; i++) { rows[i].gameObject.SetActive(true); rows[i].Bind(i+1, "Engineer " + (i+1), 64000+i*500, 50+i); }
                    }
                    Fit(panel); Capture(camera, target, "Temp/" + (multiplayer ? "MultiplayerLeaderboard_" : "ContractLeaderboard_") + size.x + "x" + size.y + ".png");
                    report.AppendLine("PASS: " + (multiplayer ? "Multiplayer five-column board" : "Existing contract board") + " fits " + size + ".");
                }
                target.Release(); Object.DestroyImmediate(target); target = null;
            }
        }
        finally { if (target != null) { target.Release(); Object.DestroyImmediate(target); } EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Fit(GameObject panel)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            bool expectedEllipsis = text.name.Contains("Builder") || text.name == "Label";
            Check(!text.isTextOverflowing || expectedEllipsis, "Leaderboard text overflows: " + text.name);
        }
        foreach (var row in panel.GetComponentsInChildren<MultiplayerLeaderboardRowUI>())
        {
            var cells = row.GetComponentsInChildren<TMP_Text>();
            Check(cells.Length == 5, "Expected five distinct multiplayer cells.");
            var ordered = cells.OrderBy(cell => cell.rectTransform.anchorMin.x).ToArray();
            for (int i = 1; i < ordered.Length; i++)
                Check(ordered[i-1].rectTransform.anchorMax.x <= ordered[i].rectTransform.anchorMin.x, "Multiplayer columns overlap.");
        }
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
}
