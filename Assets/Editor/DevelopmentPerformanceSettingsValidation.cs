#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DevelopmentPerformanceSettingsValidation
{
    const string Request = "Temp/development-performance-settings-validation.request";
    const string Report = "Temp/development-performance-settings-validation.txt";
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static double next;
    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || StaticOcclusionCulling.isRunning) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request); Validate();
    }
    [MenuItem("Tools/Civil Craft/Performance/Validate Development Settings")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene sourceScene = SceneManager.GetActiveScene();
        bool dirty = sourceScene.isDirty;
        GameObject[] originalRoots = sourceScene.GetRootGameObjects();
        var report = new StringBuilder();
        Scene preview = default;
        RenderTexture target = null;
        RenderTexture oldTarget = RenderTexture.active;
        var fields = typeof(DevelopmentPerformanceSettings).GetFields(Flags).Where(field => !field.IsLiteral).ToArray();
        object[] values = fields.Select(field => field.GetValue(null)).ToArray();
        try
        {
            // Isolate session callbacks from any source profile/overlay.
            foreach (FieldInfo field in fields) field.SetValue(null, field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
            SettingsManager source = originalRoots.SelectMany(root => root.GetComponentsInChildren<SettingsManager>(true))
                .First(manager => manager.settingsPanel != null && manager.shadowsToggle != null);
            preview = EditorSceneManager.NewPreviewScene();
            var canvasGo = new GameObject("Detached Development Settings", typeof(RectTransform), typeof(Canvas));
            canvasGo.SetActive(false); SceneManager.MoveGameObjectToScene(canvasGo, preview);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            GameObject copy = Object.Instantiate(source.settingsPanel, canvasGo.transform, false);
            foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && script.GetType().Namespace != "UnityEngine.UI" && script.GetType().Namespace != "TMPro") script.enabled = false;
            foreach (Button button in copy.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
            foreach (Toggle toggle in copy.GetComponentsInChildren<Toggle>(true)) toggle.onValueChanged = new Toggle.ToggleEvent();
            foreach (Slider slider in copy.GetComponentsInChildren<Slider>(true)) slider.onValueChanged = new Slider.SliderEvent();
            foreach (TMP_Dropdown dropdown in copy.GetComponentsInChildren<TMP_Dropdown>(true)) dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            Toggle template = copy.GetComponentsInChildren<Toggle>(true).Single(toggle => toggle.name == source.shadowsToggle.name);
            var controls = copy.AddComponent<DevelopmentPerformanceSettings>();
            controls.enabled = false;
            controls.Initialize(template); controls.Initialize(template);
            var newToggles = copy.GetComponentsInChildren<Toggle>(true).Where(toggle => toggle.name.StartsWith("Development")).ToArray();
            Check(newToggles.Length == 2 && newToggles.All(toggle => !toggle.isOn), "Controls must be unique and default off.");
            bool shadowBefore = template.isOn;
            Toggle occlusion = newToggles.Single(toggle => toggle.name == "DevelopmentOcclusionToggle");
            Toggle fps = newToggles.Single(toggle => toggle.name == "DevelopmentFpsToggle");
            occlusion.isOn = true; fps.isOn = true;
            Check(DevelopmentPerformanceSettings.OcclusionOverride == true && DevelopmentPerformanceSettings.ShowFps && template.isOn == shadowBefore,
                "Test controls must change only diagnostic session state, never shadows.");
            occlusion.isOn = false; fps.isOn = false;
            Check(DevelopmentPerformanceSettings.OcclusionOverride == false && !DevelopmentPerformanceSettings.ShowFps, "Both controls must turn off immediately.");
            report.AppendLine("PASS: two unique development toggles, default off; ON/OFF callbacks isolated from shadows; repeat initialization creates no duplicates.");
            CozyModalLayout layout = copy.GetComponent<CozyModalLayout>();
            SettingsTabController tabs = copy.GetComponentInChildren<SettingsTabController>(true);
            tabs.SwitchTab(0);
            RectTransform page = (RectTransform)tabs.tabPanels[0].transform;
            foreach (Transform child in canvasGo.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            var cameraGo = new GameObject("Detached Settings Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, preview);
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.enabled = false; camera.scene = preview; camera.orthographic = true;
            camera.useOcclusionCulling = false; camera.cullingMask = 1 << 30;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.farClipPlane = 2000; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(148, 113, 85, 255);
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            copy.SetActive(true);
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
            {
                canvasGo.SetActive(false);
                target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y / 2f;
                ((RectTransform)canvasGo.transform).sizeDelta = size;
                canvasGo.SetActive(true);
                Canvas.ForceUpdateCanvases(); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(layout.card); Canvas.ForceUpdateCanvases();
                RectTransform[] rows = page.Cast<Transform>().Where(child => child.gameObject.activeSelf && child.name.EndsWith("Row")).Cast<RectTransform>().ToArray();
                Check(rows.Length == 5, "Expected three original graphics rows and two development controls.");
                foreach (RectTransform row in rows)
                {
                    Rect bounds = Bounds(row, page);
                    Check(bounds.yMin >= page.rect.yMin - 1 && bounds.yMax <= page.rect.yMax + 1, "Row leaves the Graphics page: " + row.name);
                    foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>())
                    { text.ForceMeshUpdate(); Check(!text.isTextOverflowing, "Text overflows: " + row.name); }
                }
                for (int a = 0; a < rows.Length; a++) for (int b = a + 1; b < rows.Length; b++)
                    Check(!Bounds(rows[a], page).Overlaps(Bounds(rows[b], page)), "Rows overlap.");
                camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                try { image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply(); File.WriteAllBytes("Temp/development-settings-" + size.x + ".png", image.EncodeToPNG()); }
                finally { Object.DestroyImmediate(image); }
                RenderTexture.active = oldTarget;
                camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); target = null;
                report.AppendLine("PASS: " + size + " five readable Graphics rows fit without overlap.");
            }
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            RenderTexture.active = oldTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            for (int index = 0; index < fields.Length; index++) fields[index].SetValue(null, values[index]);
            if (sourceScene.isDirty != dirty || !sourceScene.GetRootGameObjects().SequenceEqual(originalRoots)) report.AppendLine("FAIL: source scene state changed.");
            else report.AppendLine("PASS: source scene roots/dirtiness and diagnostic session state preserved; no scene or player saves.");
            File.WriteAllText(Report, report.ToString());
        }
    }
    static Rect Bounds(RectTransform child, RectTransform owner)
    {
        var corners = new Vector3[4]; child.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (Vector3 corner in corners) { Vector2 point = owner.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static void Check(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
}
#endif
