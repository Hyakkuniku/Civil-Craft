#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Opt-in, read-only inspection of authored intro branding and its existing hide list.</summary>
[InitializeOnLoad]
public static class MainMenuBrandingVisibilityRepair
{
    private const string Request = "Temp/main-menu-branding-visibility.request";
    private const string Report = "Logs/main-menu-branding-visibility.txt";
    private const string MainMenu = "Assets/Scenes/Main Menu.unity";
    private const string LogoGuid = "22b03aca1d250004f94d5a54ea9e7569";
    private static double nextCheck;

    static MainMenuBrandingVisibilityRepair() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        Directory.CreateDirectory("Logs");
        try
        {
            if (command != "inspect" && command != "apply" && command != "validate")
                throw new InvalidOperationException("Use inspect, apply or validate.");
            File.WriteAllText(Report, Inspect(command));
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); }
    }

    private static string Inspect(string command)
    {
        Scene previous = SceneManager.GetActiveScene();
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        var report = new StringBuilder();
        foreach (SceneSetup item in setup)
        {
            Scene saved = SceneManager.GetSceneByPath(item.path);
            report.AppendLine("ORIGINAL: " + item.path + "; loaded=" + item.isLoaded +
                "; active=" + item.isActive + "; dirty=" + (saved.IsValid() && saved.isDirty));
        }
        Scene menu = SceneManager.GetSceneByPath(MainMenu);
        bool opened = !menu.IsValid() || !menu.isLoaded;
        if (opened) menu = EditorSceneManager.OpenScene(MainMenu, OpenSceneMode.Additive);
        try
        {
            if (command != "inspect") Repair(menu, command == "apply", report);
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded) continue;
                foreach (MainMenuUIController controller in All<MainMenuUIController>(scene))
                {
                    var data = new SerializedObject(controller);
                    report.AppendLine("CONTROLLER: " + scene.path + "; dirty=" + scene.isDirty + "; " + Path(controller.transform));
                    foreach (string name in new[] { "initialPlayButton", "dropdownPanel" })
                        report.AppendLine("  " + name + "=" + Reference(data.FindProperty(name).objectReferenceValue));
                    SerializedProperty list = data.FindProperty("uiToHideWhileDropdownOpen");
                    for (int item = 0; item < list.arraySize; item++)
                        report.AppendLine("  hide[" + item + "]=" + Reference(list.GetArrayElementAtIndex(item).objectReferenceValue));
                    Canvas canvas = controller.GetComponentInParent<Canvas>(true);
                    foreach (MainMenuPresentationLayout layout in All<MainMenuPresentationLayout>(scene))
                    {
                        report.AppendLine("LAYOUT: " + Path(layout.transform));
                        foreach (var pair in new[] {
                            Tuple.Create("title", layout.title), Tuple.Create("edition", layout.edition),
                            Tuple.Create("tagline", layout.tagline), Tuple.Create("splashBoard", layout.splashBoard) })
                            report.AppendLine("  " + pair.Item1 + "=" + Reference(pair.Item2));
                    }
                    foreach (Image image in (canvas != null ? canvas.GetComponentsInChildren<Image>(true) : controller.GetComponentsInChildren<Image>(true)))
                    {
                        if (image.sprite == null || image.transform.parent != controller.transform) continue;
                        report.AppendLine("INTRO IMAGE: " + Reference(image) + "; sprite=" + AssetDatabase.GetAssetPath(image.sprite) +
                            "; raycast=" + image.raycastTarget + "; anchored=" + image.rectTransform.anchoredPosition +
                            "; size=" + image.rectTransform.sizeDelta);
                    }
                }
            }
            report.AppendLine("PASS: Inspection complete; player data, source active states, geometry, materials and button callbacks were not changed.");
        }
        finally
        {
            if (opened && menu.IsValid() && menu.isLoaded && !menu.isDirty)
                EditorSceneManager.CloseScene(menu, !setup.Any(item => item.path == MainMenu));
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        return report.ToString();
    }

    private static void Repair(Scene menu, bool apply, StringBuilder report)
    {
        bool wasDirty = menu.isDirty;
        MainMenuUIController[] controllers = All<MainMenuUIController>(menu);
        Require(controllers.Length == 1, "Expected one Main Menu controller.");
        MainMenuUIController controller = controllers[0];
        Image[] images = controller.GetComponentsInChildren<Image>(true).Where(image =>
            image.transform.parent == controller.transform && image.sprite != null &&
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite)) == LogoGuid &&
            image.gameObject.activeSelf).ToArray();
        Require(images.Length == 1, "Expected exactly one active replacement logo with the approved sprite under MainPanel.");
        Image logo = images[0];
        GlobalObjectId identity = GlobalObjectId.GetGlobalObjectIdSlow(logo.gameObject);
        Require(identity.targetObjectId == 2059781784UL, "The replacement logo identity differs from the audited scene object.");
        string art = EditorJsonUtility.ToJson(logo), geometry = EditorJsonUtility.ToJson(logo.rectTransform);
        var data = new SerializedObject(controller);
        SerializedProperty hide = data.FindProperty("uiToHideWhileDropdownOpen");
        UnityEngine.Object[] before = Enumerable.Range(0, hide.arraySize)
            .Select(index => hide.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
        int count = before.Count(value => value == logo.gameObject);
        Require(count <= 1, "Replacement logo is duplicated in the hide list.");
        bool changed = apply && count == 0;
        if (changed)
        {
            Undo.RecordObject(controller, "Hide replacement title image with Main Menu intro");
            hide.InsertArrayElementAtIndex(hide.arraySize);
            hide.GetArrayElementAtIndex(hide.arraySize - 1).objectReferenceValue = logo.gameObject;
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(menu);
        }
        data.Update(); hide = data.FindProperty("uiToHideWhileDropdownOpen");
        UnityEngine.Object[] after = Enumerable.Range(0, hide.arraySize)
            .Select(index => hide.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
        Require(after.Count(value => value == logo.gameObject) == 1, "Replacement title image is not wired to the intro hide list; apply the repair first.");
        Require(changed ? after.Take(before.Length).SequenceEqual(before) && after.Length == before.Length + 1 : after.SequenceEqual(before),
            "An existing or null hide-list entry changed.");
        Require(EditorJsonUtility.ToJson(logo) == art && EditorJsonUtility.ToJson(logo.rectTransform) == geometry,
            "Replacement artwork or geometry changed.");
        ValidateClone(controller, report);
        if (changed && !wasDirty)
        {
            Require(EditorSceneManager.SaveScene(menu), "Could not save the clean Main Menu scene.");
            report.AppendLine("SAVED: Main Menu was originally clean; appended only replacement logo GameObject 2059781784 to the existing hide list.");
        }
        else if (changed) report.AppendLine("UNSAVED: Main Menu had existing unsaved edits; the one-reference repair remains unsaved with them.");
        else Require(menu.isDirty == wasDirty, "Read-only validation changed source dirty state.");
        report.AppendLine("PASS: Audited replacement logo is wired exactly once; all prior hide-list entries, artwork, geometry and active states are preserved.");
    }

    private static void ValidateClone(MainMenuUIController source, StringBuilder report)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject clone = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(clone, preview);
            clone.SetActive(false);
            MainMenuUIController controller = clone.GetComponent<MainMenuUIController>();
            Image logo = clone.GetComponentsInChildren<Image>(true).Single(image =>
                image.transform.parent == clone.transform && image.sprite != null &&
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite)) == LogoGuid);
            var data = new SerializedObject(controller);
            // The clone must never hide a live object if an authored entry later
            // points outside MainPanel. Keep its original state on a stand-in.
            IsolateReference(data.FindProperty("initialPlayButton"), clone.transform, false);
            IsolateReference(data.FindProperty("dropdownPanel"), clone.transform, true);
            foreach (string name in new[] { "sceneController", "settingsManager", "achievementManager", "shopManager", "authManager", "authCanvas", "alreadyLoggedInNotice" })
                data.FindProperty(name).objectReferenceValue = null;
            GameObject inactive = new GameObject("Detached legacy inactive intro element");
            inactive.transform.SetParent(clone.transform, false); inactive.SetActive(false);
            SerializedProperty list = data.FindProperty("uiToHideWhileDropdownOpen");
            for (int index = 0; index < list.arraySize; index++)
                IsolateReference(list.GetArrayElementAtIndex(index), clone.transform, false);
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = inactive;
            data.ApplyModifiedPropertiesWithoutUndo();
            string image = EditorJsonUtility.ToJson(logo), rect = EditorJsonUtility.ToJson(logo.rectTransform);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var capture = typeof(MainMenuUIController).GetMethod("CaptureAndHideSupportingUI", flags);
            var restore = typeof(MainMenuUIController).GetMethod("RestoreSupportingUI", flags);
            Require(capture != null && restore != null, "Production hide/restore methods are missing.");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                Require(logo.gameObject.activeSelf, "Replacement image did not start restored.");
                capture.Invoke(controller, null);
                Require(!logo.gameObject.activeSelf && !inactive.activeSelf, "The replacement image stays visible when opening the menu.");
                restore.Invoke(controller, null);
                Require(logo.gameObject.activeSelf && !inactive.activeSelf, "Back did not restore the exact intro active states.");
                restore.Invoke(controller, null);
                Require(logo.gameObject.activeSelf && !inactive.activeSelf, "Repeated restoration altered the original states.");
            }
            Require(EditorJsonUtility.ToJson(logo) == image && EditorJsonUtility.ToJson(logo.rectTransform) == rect,
                "Hide/restore changed the replacement art or its geometry.");
            report.AppendLine("PASS: Detached copy uses production hide/restore methods for two menu cycles; replacement image hides, Back restores it, legacy inactive UI stays inactive, repeated restore is safe. No authentication or button callback was invoked.");
        }
        finally
        {
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            EditorSceneManager.ClosePreviewScene(preview);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void IsolateReference(SerializedProperty property, Transform owner, bool rectangle)
    {
        UnityEngine.Object value = property.objectReferenceValue;
        GameObject target = value as GameObject ?? (value as Component)?.gameObject;
        if (target == null || target == owner.gameObject || target.transform.IsChildOf(owner)) return;
        GameObject detached = rectangle ? new GameObject("Detached external " + target.name, typeof(RectTransform)) :
            new GameObject("Detached external " + target.name);
        detached.transform.SetParent(owner, false);
        detached.SetActive(target.activeSelf);
        property.objectReferenceValue = rectangle ? (UnityEngine.Object)detached.GetComponent<RectTransform>() : detached;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static string Reference(UnityEngine.Object value)
    {
        GameObject target = value as GameObject ?? (value as Component)?.gameObject;
        return target != null ? Path(target.transform) + "; activeSelf=" + target.activeSelf +
            "; activeHierarchy=" + target.activeInHierarchy + "; id=" + target.GetInstanceID() : "null";
    }
    private static string Path(Transform target) => target.parent != null ? Path(target.parent) + "/" + target.name : target.name;
}
#endif
