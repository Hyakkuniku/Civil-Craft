#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, detached authored Gameplay UI validation. No gameplay, preferences, or account calls run.</summary>
public static class FixedJoystickSettingsValidation
{
    private const string Request = "Temp/fixed-joystick-settings-validation.request";
    private const string Report = "Temp/fixed-joystick-settings-validation-report.txt";
    private static readonly string[] Paths =
    {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };
    private static double nextCheck;
    private static bool validating;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (validating || !File.Exists(Request) || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request); // A consumed request never repeats, including failed diagnostics.
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Fixed Joystick Settings")]
    public static void Validate()
    {
        if (validating || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        validating = true;
        Scene previous = SceneManager.GetActiveScene();
        var report = new StringBuilder();
        bool failed = false;
        try
        {
            foreach (string path in Paths)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    Check(File.Exists(path), "Saved scene is missing.");
                    ValidateSavedScene(path);
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    SettingsManager[] managers = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<SettingsManager>(true)).ToArray();
                    Check(managers.Length == 1, "Expected one SettingsManager, found " + managers.Length + ".");
                    ValidateHierarchy(managers[0]);
                    report.AppendLine("PASS: " + path + " has one saved, wired, default-ON Fixed Joystick toggle in Gameplay with its explanatory label.");
                    if (path == Paths[0]) failed |= !Preview(managers[0], report);
                }
                catch (Exception error)
                {
                    failed = true;
                    report.AppendLine("FAIL: " + path + ": " + error.Message);
                }
                finally
                {
                    if (opened && scene.IsValid() && scene.isLoaded)
                    {
                        if (!scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                        else report.AppendLine("NOTE: " + path + " became dirty outside validation and was left open, never saved or discarded.");
                    }
                }
            }
            report.AppendLine("NOTE: No PlayerPrefs, player saves, purchases, authentication, live UI events, or source-scene save calls were made. Preview copies had non-UI scripts disabled before activation.");
            report.AppendLine(failed ? "RESULT: FAIL (see diagnostics above)." : "RESULT: PASS.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            File.WriteAllText(Report, report.ToString());
            validating = false;
        }
    }

    private static void ValidateSavedScene(string path)
    {
        string yaml = File.ReadAllText(path);
        Check(Regex.Matches(yaml, @"(?m)^  m_Name: FixedJoystickRow\r?$").Count == 1,
            "Saved scene must contain exactly one FixedJoystickRow; save any pending authored changes.");
        Check(Regex.Matches(yaml, @"(?m)^  m_Name: FixedJoystickToggle\r?$").Count == 1,
            "Saved scene must contain exactly one FixedJoystickToggle.");
        Match[] references = Regex.Matches(yaml, @"(?m)^  fixedJoystickToggle: \{fileID: (?<id>\d+)\}\r?$")
            .Cast<Match>().ToArray();
        Check(references.Length == 1 && references[0].Groups["id"].Value != "0",
            "Saved SettingsManager must reference the authored toggle.");
        string id = references[0].Groups["id"].Value;
        Match record = Regex.Match(yaml, @"(?ms)^--- !u!114 &" + Regex.Escape(id) + @"\r?\n(?<body>.*?)(?=^--- !u!|\z)");
        Check(record.Success && Regex.IsMatch(record.Groups["body"].Value, @"(?m)^  m_IsOn: 1\r?$"),
            "Saved Fixed Joystick toggle is not default ON.");
    }

    private static RectTransform ValidateHierarchy(SettingsManager manager)
    {
        Check(manager.settingsPanel != null && manager.fixedJoystickToggle != null,
            "Existing settings panel or Fixed Joystick reference is missing.");
        Transform page = Find(manager.settingsPanel.transform, "GameplayPage");
        Check(page is RectTransform, "Authored Gameplay page is missing.");
        Toggle[] matches = page.GetComponentsInChildren<Toggle>(true)
            .Where(toggle => toggle.name == "FixedJoystickToggle").ToArray();
        Check(matches.Length == 1 && matches[0] == manager.fixedJoystickToggle,
            "Gameplay must contain one wired Fixed Joystick toggle, not duplicate or cross-panel references.");
        Check(matches[0].transform.parent.name == "FixedJoystickRow" && matches[0].graphic != null && matches[0].targetGraphic != null,
            "Fixed Joystick must retain its authored row, checkmark, and background.");
        TMP_Text label = matches[0].transform.parent.GetComponentsInChildren<TMP_Text>(true)
            .SingleOrDefault(text => text.name == "Label");
        Check(label != null && label.richText && label.text.Contains("Fixed Joystick") &&
            label.text.Contains("left") && label.text.Contains("follows"),
            "Fixed Joystick label/help is missing or not readable rich text.");
        return (RectTransform)page;
    }

    private static bool Preview(SettingsManager source, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        bool passed = true;
        try
        {
            var canvasObject = new GameObject("Detached Gameplay Settings Preview", typeof(RectTransform), typeof(Canvas));
            canvasObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            GameObject copy = Object.Instantiate(source.settingsPanel, canvasObject.transform, false);
            foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null) continue;
                string owner = script.GetType().Namespace;
                if (owner != "UnityEngine.UI" && owner != "TMPro") script.enabled = false;
            }
            // All event objects are detached before any preview control can run.
            foreach (Button button in copy.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
            foreach (Toggle toggle in copy.GetComponentsInChildren<Toggle>(true)) toggle.onValueChanged = new Toggle.ToggleEvent();
            foreach (Slider slider in copy.GetComponentsInChildren<Slider>(true)) slider.onValueChanged = new Slider.SliderEvent();
            foreach (TMP_Dropdown dropdown in copy.GetComponentsInChildren<TMP_Dropdown>(true)) dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            CozyModalLayout layout = copy.GetComponent<CozyModalLayout>();
            SettingsTabController tabs = copy.GetComponentInChildren<SettingsTabController>(true);
            Check(layout != null && layout.card != null && tabs != null && tabs.tabPanels.Length == 4,
                "Existing responsive Settings card or tabs are missing from detached preview.");
            Check(!layout.enabled && !tabs.enabled && copy.GetComponentsInChildren<SettingsManager>(true).All(manager => !manager.enabled),
                "Preview non-UI scripts must remain disabled.");
            RectTransform gameplay = Find(copy.transform, "GameplayPage") as RectTransform;
            Check(gameplay != null && tabs.tabPanels[2] == gameplay.gameObject, "Gameplay is not the existing third tab.");
            tabs.SwitchTab(2); // Pure, local visibility/colors only; no Start or settings load.
            var cameraObject = new GameObject("Detached Gameplay Settings Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview; camera.enabled = false; camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(148, 113, 85, 255);
            // URP's detached camera request can bind ScreenSpaceCamera canvases
            // to the live Editor Game View instead of its offscreen target. Like
            // the existing Settings Account validator, use a pixel-sized world
            // canvas for deterministic detached rendering of the same authored
            // responsive RectTransforms. The source scene's Canvas is untouched.
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            canvasObject.transform.position = Vector3.zero;
            canvasObject.transform.localScale = Vector3.one;
            copy.SetActive(true);
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768) };
            string[] labels = { "phone", "tablet" };
            for (int index = 0; index < sizes.Length; index++)
            {
                Vector2Int size = sizes[index];
                try
                {
                    canvasObject.SetActive(false);
                    target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                    camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y / 2f;
                    RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                    canvasRect.sizeDelta = size;
                    canvasObject.SetActive(true);
                    Canvas.ForceUpdateCanvases(); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(layout.card); Canvas.ForceUpdateCanvases();
                    // Do not sample a just-enabled Toggle halfway through its
                    // editor-only visual fade; render the authored state directly.
                    foreach (Toggle toggle in copy.GetComponentsInChildren<Toggle>(true))
                        if (toggle.graphic != null) toggle.graphic.canvasRenderer.SetAlpha(toggle.isOn ? 1f : 0f);
                    foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                    report.AppendLine("NOTE: " + labels[index] + " deterministic world-canvas preview=" + canvasRect.rect.size +
                        ", panel=" + (copy.transform as RectTransform).rect.size + ", card=" + layout.card.rect.size +
                        ", camera=" + camera.pixelWidth + "x" + camera.pixelHeight + ".");
                    ValidateFit(layout, gameplay);
                    Capture(camera, target, "Temp/fixed-joystick-settings-" + labels[index] + ".png");
                    report.AppendLine("PASS: " + labels[index] + " " + size.x + "x" + size.y + " Gameplay preview fits four rows, visible help text, toggle and footer without overlap or overflow.");
                }
                catch (Exception error)
                {
                    passed = false;
                    report.AppendLine("FAIL: " + labels[index] + " preview: " + error.Message);
                }
                finally
                {
                    camera.targetTexture = null; Release(target); target = null;
                }
            }
        }
        finally
        {
            Release(target);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        return passed;
    }

    private static void ValidateFit(CozyModalLayout layout, RectTransform page)
    {
        RectTransform[] rows = page.Cast<Transform>().Select(child => child as RectTransform)
            .Where(child => child != null && child.name.EndsWith("Row") && child.gameObject.activeSelf).ToArray();
        Check(rows.Length == 4, "Expected the existing three gameplay rows plus Fixed Joystick.");
        foreach (RectTransform row in rows)
        {
            Inside(row, page, "Gameplay row leaves the page: " + row.name);
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                Check(!text.isTextOverflowing, "Gameplay text overflows: " + row.name + " / " + text.name);
                Inside(text.rectTransform, row, "Gameplay text leaves its row: " + row.name);
            }
        }
        for (int first = 0; first < rows.Length; first++)
        for (int second = first + 1; second < rows.Length; second++)
            Check(!Bounds(rows[first], page).Overlaps(Bounds(rows[second], page)), "Gameplay rows overlap.");
        RectTransform toggle = Find(page, "FixedJoystickToggle") as RectTransform;
        Check(toggle != null, "Detached Fixed Joystick toggle is missing.");
        RectTransform label = Find(toggle.parent, "Label") as RectTransform;
        Check(toggle != null && label != null, "Detached Fixed Joystick toggle/label is missing.");
        Inside(toggle, toggle.parent as RectTransform, "Fixed Joystick toggle leaves its row.");
        Check(!Bounds(toggle, toggle.parent as RectTransform).Overlaps(Bounds(label, toggle.parent as RectTransform)),
            "Fixed Joystick toggle overlaps its label.");
        RectTransform footer = Find(layout.card, "Footer") as RectTransform;
        Inside(layout.card, layout.card.parent as RectTransform, "Settings card leaves the viewport.");
        Check(footer != null && !Bounds(page, layout.card).Overlaps(Bounds(footer, layout.card)), "Gameplay page overlaps the footer.");
    }

    private static Rect Bounds(RectTransform child, RectTransform owner)
    {
        var corners = new Vector3[4]; child.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 point = owner.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Inside(RectTransform child, RectTransform owner, string message)
    {
        Check(child != null && owner != null, message);
        Rect actual = Bounds(child, owner), expected = owner.rect;
        Check(actual.xMin >= expected.xMin - .2f && actual.xMax <= expected.xMax + .2f &&
            actual.yMin >= expected.yMin - .2f && actual.yMax <= expected.yMax + .2f, message);
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }

    private static void Release(RenderTexture texture)
    {
        if (texture == null) return;
        texture.Release(); Object.DestroyImmediate(texture);
    }

    private static Transform Find(Transform current, string name)
    {
        if (current == null) return null;
        if (current.name == name) return current;
        foreach (Transform child in current) { Transform match = Find(child, name); if (match != null) return match; }
        return null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
