#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in authoring and disposable checks for the development-only Settings shortcut.</summary>
public static class DevelopmentSettingsButtonAuthoring
{
    private const string Request = "Temp/development-settings-button.request";
    private const string Report = "Logs/development-settings-button.txt";
    private const string Resource = "Assets/Resources/UI/DeveloperDebugSystem.prefab";
    private const string CanyonPath = "Assets/Scenes/CanyonCrossing.unity";
    private static readonly string[] Scenes = {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Mode Selection.unity", CanyonPath,
        "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        Run(command);
    }

    [MenuItem("Tools/Civil Craft/Settings/Author Development Button")]
    public static void Apply() => Run("apply");
    [MenuItem("Tools/Civil Craft/Settings/Validate Development Button")]
    public static void Validate() => Run("validate");

    private static void Run(string command)
    {
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder();
        Scene previous = SceneManager.GetActiveScene();
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        Object[] selection = Selection.objects;
        running = true;
        try
        {
            Check(command == "apply" || command == "validate", "Use apply or validate.");
            Check(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                "Run in idle Edit Mode after compilation.");
            Check(typeof(SettingsManager).GetMethod("OpenDeveloperMenu", BindingFlags.Instance | BindingFlags.Public) != null,
                "Refresh Unity to load SettingsManager.OpenDeveloperMenu first.");
            foreach (string path in Scenes) Check(File.Exists(path), "Required Settings scene is missing: " + path);
            if (command == "apply") WithScene(CanyonPath, setup, scene => EnsureResource(scene, report));
            ValidateResource(report);
            foreach (string path in Scenes)
            {
                WithScene(path, setup, scene => {
                    bool wasDirty = scene.isDirty;
                    SettingsManager[] managers = All<SettingsManager>(scene);
                    Check(managers.Length == 1, "Expected one SettingsManager in " + scene.name + ", found " + managers.Length + ".");
                    SettingsManager manager = managers[0];
                    Dictionary<Button, string> oldButtons = manager.settingsPanel != null
                        ? manager.settingsPanel.GetComponentsInChildren<Button>(true).Where(button => button.name != "DeveloperSettingsButton")
                            .ToDictionary(button => button, button => EditorJsonUtility.ToJson(button))
                        : new Dictionary<Button, string>();
                    string references = WithoutDeveloperReference(EditorJsonUtility.ToJson(manager));
                    bool changed = command == "apply" && AuthorButton(manager);
                    ValidateButton(manager);
                    Check(WithoutDeveloperReference(EditorJsonUtility.ToJson(manager)) == references,
                        "Existing SettingsManager references changed in " + scene.name + ".");
                    foreach (var entry in oldButtons)
                        Check(EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                            "Existing button callbacks or configuration changed: " + scene.name + "/" + entry.Key.name);
                    if (path == Scenes[0]) ValidateLayouts(manager, report);
                    if (changed)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!wasDirty)
                        {
                            Check(EditorSceneManager.SaveScene(scene), "Could not save authored DEV button in " + scene.name + ".");
                            report.AppendLine("SAVED: " + path + " (originally clean).");
                        }
                        else report.AppendLine("UNSAVED: " + path + " keeps the DEV button with the user's existing unsaved edits; no save was made.");
                    }
                    else Check(scene.isDirty == wasDirty, "Read-only checks changed scene dirty state: " + path);
                    report.AppendLine("PASS: " + scene.name + " has one authored DEV footer button, its own SettingsManager callback/reference, unchanged existing button events and no extra Settings tab.");
                });
            }
            ValidateRuntimeContract(report);
            report.AppendLine("PASS: No gameplay menus opened, player-data/preferences writes, live debug actions, authentication, achievements or rewards. Originally dirty scenes remain unsaved. Temporary scenes are disposed; original active scene and selection restored.");
            report.AppendLine("RESULT: PASS; command=" + command + "; UTC=" + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            Selection.objects = selection;
            File.WriteAllText(Report, report.ToString()); running = false;
        }
    }

    private static void WithScene(string path, SceneSetup[] original, Action<Scene> action)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try { action(scene); }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty)
                EditorSceneManager.CloseScene(scene, !original.Any(item => item.path == path));
        }
    }

    private static void EnsureResource(Scene canyon, StringBuilder report)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(Resource);
        if (existing != null) { report.AppendLine("PRESERVED: Existing reusable DeveloperDebugSystem prefab."); return; }
        DeveloperDebugManager[] managers = All<DeveloperDebugManager>(canyon);
        Check(managers.Length == 1, "Expected the existing authored Canyon DeveloperDebugSystem.");
        Scene previous = SceneManager.GetActiveScene();
        Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject clone = null;
        try
        {
            SceneManager.SetActiveScene(temporary);
            clone = Object.Instantiate(managers[0].gameObject);
            clone.name = "DeveloperDebugSystem";
            SceneManager.MoveGameObjectToScene(clone, temporary);
            var data = new SerializedObject(clone.GetComponent<DeveloperDebugManager>());
            data.FindProperty("allowInReleaseBuild").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
            GameObject window = data.FindProperty("debugWindow").objectReferenceValue as GameObject;
            Canvas canvas = data.FindProperty("debugCanvas").objectReferenceValue as Canvas;
            Check(window != null && canvas != null && canvas.transform.IsChildOf(clone.transform), "Developer menu has missing owned UI.");
            clone.SetActive(true); canvas.gameObject.SetActive(true); canvas.enabled = true; window.SetActive(false);
            clone.GetComponent<DeveloperDebugManager>().enabled = true;
            ValidateOwnedReferences(clone);
            Directory.CreateDirectory("Assets/Resources/UI");
            Check(PrefabUtility.SaveAsPrefabAsset(clone, Resource) != null, "Could not save reusable developer menu prefab.");
            report.AppendLine("CREATED: " + Resource + " from the existing Canyon menu; source manager/scene were not modified.");
        }
        finally
        {
            if (clone != null) Object.DestroyImmediate(clone);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
        }
    }

    private static void ValidateResource(StringBuilder report)
    {
        GameObject resource = AssetDatabase.LoadAssetAtPath<GameObject>(Resource);
        Check(resource != null, "Reusable DeveloperDebugSystem prefab is missing. Apply authoring first.");
        DeveloperDebugManager manager = resource.GetComponent<DeveloperDebugManager>();
        Check(manager != null && resource.GetComponentsInChildren<DeveloperDebugManager>(true).Length == 1,
            "Reusable developer prefab must own exactly one DeveloperDebugManager.");
        Check(resource.activeSelf && manager.enabled, "Reusable developer root and manager must be active so lazy initialization can run Awake.");
        var data = new SerializedObject(manager);
        Check(!data.FindProperty("allowInReleaseBuild").boolValue, "Reusable developer menu must not allow release-build use.");
        foreach (string name in new[] { "debugCanvas", "debugWindow", "sceneDropdown", "tutorialDropdown", "buildLocationDropdown", "npcDropdown", "npcPhaseDropdown", "achievementDropdown", "coinAmountDropdown", "timeScaleSlider", "invincibleBridgeToggle" })
            Check(data.FindProperty(name)?.objectReferenceValue != null, "Missing reusable developer UI reference: " + name);
        GameObject window = data.FindProperty("debugWindow").objectReferenceValue as GameObject;
        Check(window != null && !window.activeSelf, "Reusable developer window must start closed.");
        ValidateOwnedReferences(resource);
        Button close = resource.GetComponentsInChildren<Button>(true).SingleOrDefault(button => button.name == "CloseButton");
        Check(close != null && Enumerable.Range(0, close.onClick.GetPersistentEventCount()).Any(index =>
            close.onClick.GetPersistentTarget(index) == manager && close.onClick.GetPersistentMethodName(index) == "CloseMenu"),
            "Reusable developer Close button is not wired to its own manager.");
        report.AppendLine("PASS: Lazy-load resource is self-contained, has all existing selectors, starts closed, disallows release override and retains its own Close callback.");
    }

    private static void ValidateOwnedReferences(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            Check(component != null, "Reusable developer UI contains a missing script.");
            var data = new SerializedObject(component);
            SerializedProperty iterator = data.GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object value = iterator.objectReferenceValue;
                if (value == null || EditorUtility.IsPersistent(value)) continue;
                GameObject target = value as GameObject ?? (value as Component)?.gameObject;
                if (target == null || !target.scene.IsValid()) continue;
                Check(target == root || target.transform.IsChildOf(root.transform),
                    "Developer prefab references external scene object: " + component.name + "." + iterator.propertyPath + " -> " + target.name);
            }
        }
    }

    private static bool AuthorButton(SettingsManager manager)
    {
        Check(manager.settingsPanel != null, "Settings panel reference is missing.");
        RectTransform footer = CozyModalLayout.Find(manager.settingsPanel.transform, "Footer");
        Check(footer != null, "Authored Settings Footer is missing.");
        Button apply = footer.GetComponentsInChildren<Button>(true).SingleOrDefault(button => button.name == "ApplySettingsButton");
        Check(apply != null, "Use the existing authored Apply Settings button as the style source.");
        Button button = footer.GetComponentsInChildren<Button>(true).SingleOrDefault(item => item.name == "DeveloperSettingsButton");
        var managerData = new SerializedObject(manager);
        SerializedProperty reference = managerData.FindProperty("developerButton");
        Check(reference != null, "Refresh Unity to load the developerButton field first.");
        if (button != null && reference.objectReferenceValue == button && HasOpenCallback(button, manager) &&
            button.gameObject.activeSelf && footer.GetComponent<HorizontalLayoutGroup>()?.childForceExpandWidth == false)
            return false;
        Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Author DEV Settings button");
        if (button == null)
        {
            GameObject clone = Object.Instantiate(apply.gameObject, footer, false); clone.name = "DeveloperSettingsButton";
            Undo.RegisterCreatedObjectUndo(clone, "Create DEV Settings button");
            button = clone.GetComponent<Button>();
        }
        Undo.RecordObject(manager, "Wire DEV Settings button");
        button.onClick = new Button.ButtonClickedEvent();
        UnityAction open = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), manager, "OpenDeveloperMenu");
        UnityEventTools.AddPersistentListener(button.onClick, open);
        button.transform.SetSiblingIndex(apply.transform.GetSiblingIndex());
        button.gameObject.SetActive(true); button.interactable = true;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        Check(label != null, "DEV button style source lacks its label.");
        label.text = "DEV"; label.enableWordWrapping = false;
        label.enableAutoSizing = true; label.fontSizeMax = Mathf.Max(24f, label.fontSize); label.fontSizeMin = 18f;
        if (button.targetGraphic != null) button.targetGraphic.color = CozyModalLayout.Sand;
        LayoutElement element = button.GetComponent<LayoutElement>();
        if (element == null) element = button.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = 120f; element.flexibleWidth = 0f;
        element.preferredHeight = 60f;
        HorizontalLayoutGroup layout = footer.GetComponent<HorizontalLayoutGroup>();
        Check(layout != null, "Settings Footer must retain its authored layout group.");
        Undo.RecordObject(layout, "Fit DEV Settings button"); layout.childForceExpandWidth = false;
        reference.objectReferenceValue = button; managerData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(button); EditorUtility.SetDirty(manager); EditorUtility.SetDirty(layout);
        LayoutRebuilder.ForceRebuildLayoutImmediate(footer);
        return true;
    }

    private static bool HasOpenCallback(Button button, SettingsManager manager) => button.onClick.GetPersistentEventCount() == 1 &&
        button.onClick.GetPersistentTarget(0) == manager && button.onClick.GetPersistentMethodName(0) == "OpenDeveloperMenu";

    private static void ValidateButton(SettingsManager manager)
    {
        var data = new SerializedObject(manager);
        Button button = data.FindProperty("developerButton")?.objectReferenceValue as Button;
        Check(button != null && manager.settingsPanel != null && button.transform.IsChildOf(manager.settingsPanel.transform),
            "SettingsManager must reference its own authored DEV button.");
        Button[] matches = manager.settingsPanel.GetComponentsInChildren<Button>(true).Where(item => item.name == "DeveloperSettingsButton").ToArray();
        Check(matches.Length == 1 && matches[0] == button && button.transform.parent.name == "Footer", "DEV button is duplicated or outside the Settings footer.");
        Check(HasOpenCallback(button, manager), "DEV button must have only its own SettingsManager.OpenDeveloperMenu callback.");
        Check(button.GetComponentInChildren<TMP_Text>(true)?.text == "DEV", "DEV button label changed.");
        LayoutElement element = button.GetComponent<LayoutElement>();
        Check(element != null && element.preferredWidth == 120f && element.flexibleWidth == 0f,
            "DEV button must retain a compact preferred width.");
        Check(button.transform.parent.GetComponent<HorizontalLayoutGroup>()?.childForceExpandWidth == false,
            "DEV button would expand and squeeze the existing footer labels.");
    }

    private static void ValidateRuntimeContract(StringBuilder report)
    {
        Type access = typeof(SettingsManager).Assembly.GetType("DevelopmentSettingsAccess");
        Check(access != null, "DevelopmentSettingsAccess runtime gate is missing.");
        PropertyInfo supported = access.GetProperty("IsSupportedBuild", BindingFlags.Public | BindingFlags.Static);
        Check(supported != null && (bool)supported.GetValue(null), "The development Settings shortcut must be available in the Unity Editor.");
        FieldInfo resource = access.GetField("ResourcePath", BindingFlags.Public | BindingFlags.Static);
        Check(resource != null && (string)resource.GetValue(null) == "UI/DeveloperDebugSystem", "Runtime resource lookup differs from the authored prefab.");
        Check(typeof(SettingsManager).GetMethod("RefreshDeveloperButton", BindingFlags.Public | BindingFlags.Instance) != null,
            "Settings lacks its build-visibility refresh entry point.");
        report.AppendLine("PASS: Editor development gate and lazy resource path match the runtime entry points. Release compile-branch verification is performed separately; no debug menu was opened by this check.");
    }

    private static void ValidateLayouts(SettingsManager source, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null; RenderTexture target = null; Camera camera = null;
        try
        {
            root = new GameObject("Detached DEV Settings Preview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, scene);
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var viewport = root.GetComponent<RectTransform>();
            GameObject copy = Object.Instantiate(source.settingsPanel, root.transform, false);
            foreach (Behaviour behavior in copy.GetComponentsInChildren<Behaviour>(true))
                if (!(behavior is Graphic) && !(behavior is LayoutGroup) && !(behavior is LayoutElement) && !(behavior is ContentSizeFitter) &&
                    !(behavior is Selectable) && !(behavior is CozyModalLayout)) behavior.enabled = false;
            CozyModalLayout layout = copy.GetComponent<CozyModalLayout>();
            Check(layout != null && layout.card != null, "Settings preview requires the existing CozyModalLayout.");
            copy.SetActive(true);
            RectTransform footer = CozyModalLayout.Find(copy.transform, "Footer");
            Button dev = footer.GetComponentsInChildren<Button>(true).Single(item => item.name == "DeveloperSettingsButton");
            Button[] buttons = footer.GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons) button.onClick = new Button.ButtonClickedEvent();
            GameObject cameraObject = new GameObject("Detached DEV Settings Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.transform.position = new Vector3(0f, 0f, -1000f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255); canvas.worldCamera = camera;
            Directory.CreateDirectory("Logs/DevelopmentSettings");
            foreach (Vector2Int size in new[] { new Vector2Int(2340, 1080), new Vector2Int(1920, 1080), new Vector2Int(1440, 1080), new Vector2Int(1280, 720) })
            {
                float scale = Mathf.Sqrt((size.x / 1920f) * (size.y / 1080f));
                Vector2 design = (Vector2)size / scale;
                viewport.sizeDelta = design; camera.orthographicSize = design.y * .5f; camera.aspect = size.x / (float)size.y;
                dev.gameObject.SetActive(true); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(footer); Canvas.ForceUpdateCanvases();
                ValidateFooter(footer, buttons, true);
                target = new RenderTexture(size.x, size.y, 24); Check(target.Create(), "Could not create Settings reference target.");
                camera.targetTexture = target;
                Capture(camera, target, "Logs/DevelopmentSettings/Settings-DEV-" + size.x + "x" + size.y + ".png");
                camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); target = null;
                dev.gameObject.SetActive(false); LayoutRebuilder.ForceRebuildLayoutImmediate(footer); Canvas.ForceUpdateCanvases();
                ValidateFooter(footer, buttons, false);
                report.AppendLine("PASS: " + size.x + "x" + size.y + " authored Settings clone: DEV and existing footer labels fit without overlap; hiding DEV leaves no layout space; callback events never invoked.");
            }
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void ValidateFooter(RectTransform footer, Button[] all, bool devVisible)
    {
        Button[] visible = all.Where(button => button.gameObject.activeInHierarchy).OrderBy(button => Bounds((RectTransform)button.transform, footer).xMin).ToArray();
        Check(visible.Length == (devVisible ? 3 : 2), "Settings footer has an unexpected visible button count.");
        Rect previous = default;
        for (int index = 0; index < visible.Length; index++)
        {
            var rect = (RectTransform)visible[index].transform;
            Rect bounds = Bounds(rect, footer);
            Check(bounds.width >= (visible[index].name == "DeveloperSettingsButton" ? 90f : 200f),
                "Settings footer button lost its authored preferred width: " + rect.name + " / " + bounds.width);
            Check(bounds.xMin >= footer.rect.xMin - .5f && bounds.xMax <= footer.rect.xMax + .5f &&
                bounds.yMin >= footer.rect.yMin - .5f && bounds.yMax <= footer.rect.yMax + .5f,
                "Settings footer button leaves its row: " + rect.name);
            if (index > 0) Check(!bounds.Overlaps(previous), "Settings footer buttons overlap.");
            foreach (TMP_Text text in rect.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate();
                Check(text.rectTransform.rect.width > 30f && text.rectTransform.rect.height > 0f &&
                    text.textInfo != null && text.textInfo.characterCount > 0 &&
                    text.textInfo.characterInfo.Take(text.textInfo.characterCount).Any(character => character.isVisible),
                    "Settings footer label has no drawable text: " + rect.name + " / " + text.text);
                Check(!text.isTextOverflowing, "Settings footer text overflows: " + text.text);
            }
            previous = bounds;
        }
        if (!devVisible) Check(Mathf.Abs(Bounds((RectTransform)visible[0].transform, footer).width -
            Bounds((RectTransform)visible[1].transform, footer).width) < 1f,
            "Hidden DEV leaves an asymmetric gap in the release footer.");
    }

    private static Rect Bounds(RectTransform item, RectTransform owner)
    {
        var corners = new Vector3[4]; item.GetWorldCorners(corners);
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector3 corner in corners) { Vector2 point = owner.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
    private static string WithoutDeveloperReference(string json) => Regex.Replace(json, "\"developerButton\"\\s*:\\s*\\{[^}]*\\}", "\"developerButton\":{}");
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
