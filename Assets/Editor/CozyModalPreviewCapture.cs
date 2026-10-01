#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Non-destructive render check using cloned authored panels and sample data.</summary>
[InitializeOnLoad]
public static class CozyModalPreviewCapture
{
    private const string Output = "Temp/CozyModalPreviews-v4";
    static CozyModalPreviewCapture()
    {
        EditorApplication.delayCall += CaptureOnce;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += CaptureOnce;
        };
    }
    private static void CaptureOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += CaptureOnce; return; }
        if (!File.Exists(Output + "/Leaderboards-tablet.png")) Capture();
    }
    [MenuItem("Tools/Civil Craft/Capture Mobile Modal Previews")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Output);
        Scene original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Mode Selection.unity", "Assets/Scenes/CanyonCrossing.unity" })
        {
            Scene source = SceneManager.GetSceneByPath(path);
            bool opened = !source.IsValid() || !source.isLoaded;
            if (opened) source = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (GameObject root in source.GetRootGameObjects())
                    foreach (CozyModalLayout panel in root.GetComponentsInChildren<CozyModalLayout>(true))
                    {
                        if (panel.authoredVersion < 3) continue;
                        string label = (source.name == "CanyonCrossing" ? "Canyon-" : "") + panel.kind;
                        Render(panel.gameObject, label, 2340, 1080, "phone");
                        Render(panel.gameObject, label, 1920, 1200, "tablet");
                    }
                if (source.name == "CanyonCrossing")
                    foreach (GameObject root in source.GetRootGameObjects())
                        foreach (ObjectiveTrackerUI tracker in root.GetComponentsInChildren<ObjectiveTrackerUI>(true))
                            if (tracker.trackerPanel != null && tracker.trackerPanel.GetComponent<ObjectiveMobileFit>() != null)
                            {
                                Render(tracker.trackerPanel, "Objectives", 2340, 1080, "phone", tracker);
                                Render(tracker.trackerPanel, "Objectives", 1920, 1200, "tablet", tracker);
                                Render(tracker.trackerPanel, "ObjectiveDetails", 2340, 1080, "phone", tracker, true);
                                Render(tracker.trackerPanel, "ObjectiveDetails", 1920, 1200, "tablet", tracker, true);
                            }
            }
            finally { if (opened) EditorSceneManager.CloseScene(source, true); }
        }
        if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        Debug.Log("[Cozy UI QA] Rendered phone/tablet authored panel previews to " + Output);
    }
    private static void Render(GameObject source, string label, int width, int height, string device, ObjectiveTrackerUI tracker = null, bool details = false)
    {
        Scene staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture texture = new RenderTexture(width, height, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D pixels = null;
        Camera previewCamera = null;
        try
        {
            GameObject cameraObject = new GameObject("Modal Preview Camera", typeof(Camera));
            Camera camera = previewCamera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -100);
            camera.orthographic = true; camera.orthographicSize = 540;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f, .28f, .34f);
            camera.cullingMask = 1 << 5; camera.targetTexture = texture;
            GameObject canvasObject = new GameObject("Modal Preview Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.layer = 5;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            RectTransform canvasRect = canvas.transform as RectTransform;
            canvasRect.sizeDelta = new Vector2(width * 1080f / height, 1080f);
            canvasRect.position = Vector3.zero;
            GameObject clone = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(clone, staging);
            clone.transform.SetParent(canvas.transform, false);
            clone.SetActive(true);
            CozyModalLayout layout = clone.GetComponent<CozyModalLayout>();
            layout?.ApplyLayout();
            clone.GetComponent<ObjectiveMobileFit>()?.ApplyLayout();
            if (tracker != null)
            {
                CozyModalLayout.Find(clone.transform, tracker.listPanel.name)?.gameObject.SetActive(!details);
                CozyModalLayout.Find(clone.transform, tracker.detailsPanel.name)?.gameObject.SetActive(details);
                if (details)
                {
                    PreviewText(clone, tracker.titleText, "Silas's Request");
                    PreviewText(clone, tracker.descriptionText, "Build a dependable bridge across the canyon. Keep it within budget and strong enough to carry the live-load vehicle safely.");
                    PreviewText(clone, tracker.budgetText, "Budget: ₱200,000");
                    PreviewText(clone, tracker.weightText, "Live load: 12,000 kg");
                    PreviewText(clone, tracker.rewardGoldText, "1,200");
                    PreviewText(clone, tracker.rewardExpText, "700");
                    CozyModalLayout.Find(clone.transform, "Back to Objective List")?.gameObject.SetActive(true);
                }
                RectTransform content = CozyModalLayout.Find(clone.transform, tracker.questListContent.name);
                for (int i = 0; i < 6; i++)
                {
                    GameObject row = Object.Instantiate(tracker.questTabPrefab, content);
                    row.GetComponent<ObjectiveTabButton>()?.Setup(new TrackedTask {
                        title = new[] { "Silas's Request", "Vance Side Realignment", "Learn to Build a Bridge" }[i%3],
                        isReadyToTurnIn = i == 1, isTutorial = i == 2
                    });
                }
            }
            if (layout != null && layout.kind == CozyModalLayout.PanelKind.Settings)
                clone.GetComponentInChildren<SettingsTabController>(true)?.SwitchTab(0);
            if (layout != null && layout.kind == CozyModalLayout.PanelKind.Leaderboards)
            {
                int i = 0;
                foreach (LeaderboardRowUI row in clone.GetComponentsInChildren<LeaderboardRowUI>(true))
                {
                    row.gameObject.SetActive(i < 8);
                    row.Bind(i+1, new[] {"Alex", "Sam", "You", "Rowan", "Taylor", "Jordan", "Casey", "Morgan"}[i%8], 1420+i*90, 48f-i*2);
                    i++;
                }
                CozyModalLayout.Find(clone.transform, "Leaderboard Status")?.gameObject.SetActive(false);
                TMP_Text personal = CozyModalLayout.Find(clone.transform, "Personal Best")?.GetComponent<TMP_Text>();
                if (personal != null) personal.text = "YOUR BEST   ₱1,560  •  44.0% peak stress";
            }
            if (layout != null && layout.kind == CozyModalLayout.PanelKind.Achievements)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Script/Player/Achievements/AchivementRow_Prefab.prefab");
                RectTransform content = CozyModalLayout.Find(clone.transform, "Content");
                string[] names = { "Canyon Conqueror", "River Restorer", "Industrial Champion", "Civil Craft Graduate" };
                for (int i = 0; i < names.Length; i++)
                {
                    AchievementSO achievement = AssetDatabase.LoadAssetAtPath<AchievementSO>("Assets/BridgeBuilder/Data/Achievements/"+names[i]+".asset");
                    if (achievement == null) continue;
                    GameObject row = Object.Instantiate(prefab, content);
                    RectTransform rect = row.transform as RectTransform;
                    rect.anchoredPosition = new Vector2(0f, -10f-i*172f);
                    rect.sizeDelta = new Vector2(-20f, 160f);
                    row.GetComponent<AchievementRowUI>().Setup(achievement, i == 0 ? 4 : 0, false, i == 0 ? 5 : 30);
                }
                content.sizeDelta = new Vector2(0f, 700f);
            }
            Canvas.ForceUpdateCanvases();
            layout?.ApplyLayout();
            clone.GetComponent<ObjectiveMobileFit>()?.ApplyLayout();
            // Capture settled toggle visuals, not their editor-time fade tween.
            foreach (Toggle toggle in clone.GetComponentsInChildren<Toggle>(true))
                if (toggle.graphic != null) toggle.graphic.canvasRenderer.SetAlpha(toggle.isOn ? 1f : 0f);
            foreach (TMP_Text text in clone.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else camera.Render();
            RenderTexture.active = texture;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + label + "-" + device + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (previewCamera != null) previewCamera.targetTexture = null;
            texture.Release(); Object.DestroyImmediate(texture);
            EditorSceneManager.CloseScene(staging, true);
        }
    }
    private static void PreviewText(GameObject clone, TMP_Text original, string value)
    {
        if (original == null) return;
        TMP_Text text = CozyModalLayout.Find(clone.transform, original.name)?.GetComponent<TMP_Text>();
        if (text != null) text.text = value;
    }
}
#endif
