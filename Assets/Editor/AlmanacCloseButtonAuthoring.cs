#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, scoped authoring of the two scenes' existing Almanac close controls.</summary>
[InitializeOnLoad]
public static class AlmanacCloseButtonAuthoring
{
    public const string RequestPath = "Temp/almanac-close-authoring.request";
    public const string ReportPath = "Logs/almanac-close-authoring.txt";
    public const string SpritePath = "Assets/Elements/UI/AlmanacCloseRounded.png";
    private static readonly string[] Paths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    static AlmanacCloseButtonAuthoring() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        bool verify = File.ReadAllText(RequestPath).Trim().Equals("verify", StringComparison.OrdinalIgnoreCase);
        File.Delete(RequestPath);
        Run(verify);
    }

    [MenuItem("Tools/Civil Craft/Almanac/Apply CLOSE to Both Scenes")]
    public static void ApplyBothScenes() { Run(false); }

    [MenuItem("Tools/Civil Craft/Almanac/Verify Authored CLOSE in Both Scenes (Read Only)")]
    public static void VerifyBothScenes() { Run(true); }

    private sealed class Target
    {
        public Scene Scene;
        public bool Opened, InitiallyDirty, Applied, Saved;
        public string DiskHash;
        public AlmanacManager Book;
        public Button Button;
    }

    private static void Run(bool verify)
    {
        if (Busy) return;
        running = true;
        Scene originalActive = SceneManager.GetActiveScene();
        Object[] originalSelection = Selection.objects;
        Object originalSelected = Selection.activeObject;
        Scene[] originalScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        var unrelated = originalScenes.Where(scene => !Paths.Contains(scene.path)).ToDictionary(scene => scene,
            scene => new { scene.isDirty, Roots = scene.GetRootGameObjects(), Hash = File.Exists(scene.path) ? Hash(scene.path) : null });
        var targets = new List<Target>();
        var report = new StringBuilder(verify ? "Verify saved/authored Almanac CLOSE controls\n" : "Author existing Almanac CLOSE controls\n");
        bool success = false;
        int undo = -1;
        try
        {
            // Finish both scene/font/button preflights before changing either control.
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF");
            Require(font != null && AssetDatabase.Contains(font), "The persistent gameplay font is missing; no controls changed.");
            foreach (string path in Paths)
            {
                Require(File.Exists(path), "Required scene is missing: " + path);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var target = new Target { Scene = scene, Opened = opened, InitiallyDirty = scene.isDirty, DiskHash = Hash(path) };
                targets.Add(target);
                AlmanacManager[] books = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AlmanacManager>(true)).ToArray();
                Require(books.Length == 1 && books[0].Panel != null, "Expected the unique authored Almanac in " + path);
                target.Book = books[0];
                Button[] buttons = target.Book.Panel.GetComponentsInChildren<Button>(true).Where(button =>
                    Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index =>
                        button.onClick.GetPersistentTarget(index) == target.Book &&
                        button.onClick.GetPersistentMethodName(index) == "CloseAlmanac")).ToArray();
                Require(buttons.Length == 1 && buttons[0].transform is RectTransform, "Expected the existing unique CloseAlmanac Button in " + path);
                target.Button = buttons[0];
                Require(target.Button.GetComponent<Image>() != null && target.Button.targetGraphic == target.Button.GetComponent<Image>(),
                    "Unexpected close target graphic in " + path + "; nothing substituted.");
                report.AppendLine("PREFLIGHT: " + scene.name + "; " + (opened ? "opened additively" : "already open") +
                    "; dirty before authoring=" + target.InitiallyDirty + ".");
            }
            Sprite sprite = verify ? AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath) : EnsurePersistentSprite();
            Require(sprite != null && AssetDatabase.GetAssetPath(sprite) == SpritePath, "The owned persistent CLOSE sprite is missing.");
            string backup = null;
            if (!verify)
            {
                backup = "Temp/AlmanacCloseAuthoringBackup/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(backup);
                foreach (Target target in targets) File.Copy(target.Scene.path, backup + "/" + Path.GetFileName(target.Scene.path), false);
                report.AppendLine("BACKUP: " + backup + "; scene files copied before modification.");
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author Almanac CLOSE buttons");
            }
            foreach (Target target in targets)
            {
                if (!verify) Apply(target, font, sprite);
                Validate(target.Button, font, sprite);
                if (!verify)
                {
                    // Opening a scene may itself invoke existing OnValidate code. Its
                    // resulting dirty state is preserved just like unsaved user edits.
                    if (target.InitiallyDirty)
                        report.AppendLine("WAIT SAVE: " + target.Scene.name + " styled in Edit Mode; its pre-existing unsaved changes were not auto-saved.");
                    else
                    {
                        Require(Hash(target.Scene.path) == target.DiskHash, "Scene file changed externally before save: " + target.Scene.path);
                        Require(EditorSceneManager.SaveScene(target.Scene), "Could not save " + target.Scene.path);
                        target.Saved = true;
                        report.AppendLine("SAVED: " + target.Scene.name + "; existing Button/RectTransform/callback/tutorial references retained.");
                    }
                }
                else report.AppendLine("PASS: " + target.Scene.name + "; actual authored CLOSE label, persistent sprite, styling, and existing callback are present.");
            }
            DateTime previousPreview = File.Exists(AlmanacCloseButtonValidation.ReportPath)
                ? File.GetLastWriteTimeUtc(AlmanacCloseButtonValidation.ReportPath) : DateTime.MinValue;
            AlmanacCloseButtonValidation.Run();
            Require(File.Exists(AlmanacCloseButtonValidation.ReportPath) &&
                File.GetLastWriteTimeUtc(AlmanacCloseButtonValidation.ReportPath) > previousPreview &&
                File.ReadAllText(AlmanacCloseButtonValidation.ReportPath).StartsWith("PASS", StringComparison.Ordinal),
                "The detached native previews did not pass; see " + AlmanacCloseButtonValidation.ReportPath);
            report.AppendLine("PASS: detached phone/tablet rendering and tap checks ran while both authored scenes were open. " + AlmanacCloseButtonValidation.ReportPath);
            if (undo >= 0) Undo.CollapseUndoOperations(undo);
            success = true;
            report.Insert(0, targets.Any(target => !verify && target.InitiallyDirty) ? "WAIT SAVE\n" : "PASS\n");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            // Never restore a saved scene setup over dirty scenes. Close only scenes
            // opened here whose state is clean, or which we successfully saved.
            foreach (Target target in targets.AsEnumerable().Reverse())
                if (target.Opened && target.Scene.IsValid() && target.Scene.isLoaded)
                {
                    if (!target.Scene.isDirty && (!target.Applied || target.Saved || verify)) EditorSceneManager.CloseScene(target.Scene, true);
                    else report.AppendLine("LEFT OPEN: " + target.Scene.name + "; unsaved changes retained.");
                }
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            Selection.objects = originalSelection; Selection.activeObject = originalSelected;
            try
            {
                Require(originalScenes.All(scene => scene.IsValid() && scene.isLoaded), "An originally open scene was closed.");
                Require(unrelated.All(pair => pair.Key.isDirty == pair.Value.isDirty &&
                    pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Roots) &&
                    (pair.Value.Hash == null || Hash(pair.Key.path) == pair.Value.Hash)), "An unrelated scene changed.");
                report.AppendLine("PASS: original active scene/selection and unrelated scenes preserved; no live UI, tutorial, player-data or close callbacks invoked.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); success = false; }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            running = false;
            if (success) Debug.Log("[Almanac CLOSE authoring] " + report);
            else Debug.LogWarning("[Almanac CLOSE authoring] See " + ReportPath + " for the stopped operation.");
        }
    }

    private static void Apply(Target target, TMP_FontAsset font, Sprite sprite)
    {
        Button button = target.Button;
        var rect = (RectTransform)button.transform;
        Transform parent = rect.parent;
        RectTransform wrapper = parent as RectTransform;
        bool wrapped = wrapper != null && wrapper.GetComponent<RawImage>() != null && wrapper.childCount == 1 &&
            rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one && wrapper.anchorMin == wrapper.anchorMax &&
            wrapper.sizeDelta.x > 0 && wrapper.sizeDelta.y > 0 && wrapper.sizeDelta.x <= 320 && wrapper.sizeDelta.y <= 200;
        Transform scope = wrapped ? parent : rect;
        var previous = new HashSet<Object>(scope.GetComponentsInChildren<Component>(true).Cast<Object>());
        var active = target.Book.Panel.GetComponentsInChildren<Transform>(true).Append(target.Book.transform)
            .Append(target.Book.Panel.transform.parent).Where(item => item != null).Distinct().ToDictionary(item => item.gameObject, item => item.gameObject.activeSelf);
        var callbacks = target.Book.Panel.GetComponentsInChildren<Button>(true).ToDictionary(item => item, item => JsonUtility.ToJson(item.onClick));
        var tutorials = target.Scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true))
            .ToDictionary(item => item, item => EditorJsonUtility.ToJson(item));
        string bookJson = EditorJsonUtility.ToJson(target.Book);
        int buttonId = button.GetInstanceID(), rectId = rect.GetInstanceID();
        Graphic graphic = button.targetGraphic;
        Undo.RegisterFullObjectHierarchyUndo(scope.gameObject, "Style existing Almanac CLOSE");
        target.Applied = true;
        RectTransform control = AlmanacCloseButtonStyle.Apply(button, font);
        Image border = button.GetComponent<Image>(); border.sprite = sprite;
        Image face = button.transform.Find("Cream Face").GetComponent<Image>(); face.sprite = sprite;
        foreach (Transform item in scope.GetComponentsInChildren<Transform>(true))
        {
            bool newObject = !previous.Contains(item);
            if (newObject) Undo.RegisterCreatedObjectUndo(item.gameObject, "Create Almanac CLOSE artwork");
            foreach (Component component in item.GetComponents<Component>())
            {
                if (component == null) continue;
                if (!newObject && !previous.Contains(component)) Undo.RegisterCreatedObjectUndo(component, "Create Almanac CLOSE component");
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            if (PrefabUtility.IsPartOfPrefabInstance(item.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
        }
        Require(button.GetInstanceID() == buttonId && rect.GetInstanceID() == rectId && rect.parent == parent && button.targetGraphic == graphic,
            "Styling replaced the existing close Button/RectTransform/target graphic or changed its parent.");
        Require(callbacks.All(pair => JsonUtility.ToJson(pair.Key.onClick) == pair.Value) &&
            tutorials.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value) &&
            bookJson == EditorJsonUtility.ToJson(target.Book), "A button callback, tutorial reference, or Almanac manager changed.");
        Require(active.All(pair => pair.Key != null && pair.Key.activeSelf == pair.Value), "A book/panel startup active state changed.");
        Require(control != null && control == scope, "Unexpected close control wrapper selected.");
        EditorSceneManager.MarkSceneDirty(target.Scene);
    }

    private static void Validate(Button button, TMP_FontAsset font, Sprite sprite)
    {
        RectTransform rect = (RectTransform)button.transform;
        RectTransform control = rect.parent != null && rect.parent.GetComponent<RawImage>() != null && rect.parent.childCount == 1
            ? (RectTransform)rect.parent : rect;
        TMP_Text label = rect.Find("Label")?.GetComponent<TMP_Text>();
        Image border = button.GetComponent<Image>(), face = rect.Find("Cream Face")?.GetComponent<Image>();
        Require(label != null && label.text == "CLOSE" && label.font == font && label.gameObject.activeSelf && !label.raycastTarget,
            "The actual authored CLOSE label/font is missing.");
        Require(border != null && face != null && border.sprite == sprite && face.sprite == sprite &&
            AssetDatabase.Contains(sprite) && (sprite.hideFlags & HideFlags.DontSave) == 0 &&
            border.type == Image.Type.Sliced && face.type == Image.Type.Sliced && border.raycastTarget && !face.raycastTarget,
            "CLOSE must use the persistent sliced artwork and root tap target.");
        Require(control.anchorMin == Vector2.one && control.anchorMax == Vector2.one && control.pivot == Vector2.one &&
            control.sizeDelta == new Vector2(190, 76) && control.anchoredPosition == new Vector2(-18, -12) &&
            Quaternion.Angle(rect.localRotation, Quaternion.identity) < .01f && rect.localScale == Vector3.one,
            "The authored CLOSE geometry differs from its in-game SKIP-style control.");
        RawImage oval = control.GetComponent<RawImage>();
        Require(oval == null || (!oval.enabled && !oval.raycastTarget), "The old oval is still visible or intercepts taps.");
        Require(button.GetComponent<TutorialHoldToSkip>() == null, "CLOSE must remain a normal tap action.");
    }

    private static Sprite EnsurePersistentSprite()
    {
        // Same alpha shape as DialogueManager.GetSkipRoundedSprite. A real imported
        // sprite keeps Edit Mode artwork stable across scene/Editor reloads.
        const int width = 128, height = 52;
        const float radius = 8f;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        byte[] png;
        try
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + .5f - width * .5f) - (width * .5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + .5f - height * .5f) - (height * .5f - radius), 0f);
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + .5f) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            texture.SetPixels32(pixels); texture.Apply(false, false); png = texture.EncodeToPNG();
        }
        finally { Object.DestroyImmediate(texture); }
        if (File.Exists(SpritePath)) Require(File.ReadAllBytes(SpritePath).SequenceEqual(png), "Unexpected artwork occupies " + SpritePath + "; nothing overwritten.");
        else File.WriteAllBytes(SpritePath, png);
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        Require(importer != null, "CLOSE artwork could not be imported.");
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100; importer.spriteBorder = new Vector4(9, 9, 9, 9);
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None; importer.alphaIsTransparency = true;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings); importer.SaveAndReimport();
        Sprite result = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        Require(result != null && result.rect.width == width && result.rect.height == height &&
            result.border == new Vector4(9, 9, 9, 9) && Mathf.Abs(result.pixelsPerUnit - 100) < .01f,
            "Persistent CLOSE sprite import settings are incorrect.");
        return result;
    }

    private static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
