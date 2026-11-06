#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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

/// <summary>Disposable loading-overlay checks. Never starts a scene load or opens player data.</summary>
[InitializeOnLoad]
public static class LoadingScreenNoCharacterValidation
{
    private const string Request = "Temp/loading-screen-no-character.request";
    private const string Report = "Logs/loading-screen-no-character.txt";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;
    static LoadingScreenNoCharacterValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request); Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Loading Screen Without Character")]
    public static void Validate()
    {
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder();
        Scene original = SceneManager.GetActiveScene(), preview = default;
        LoadingScreenManager singleton = LoadingScreenManager.Instance;
        UnityEngine.Random.State random = UnityEngine.Random.state;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        { Scene scene = SceneManager.GetSceneAt(i); scenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects())); }
        GameObject fixture = null, presentation = null;
        LoadingScreenManager manager = null;
        Sprite generatedSprite = null;
        Texture2D generatedTexture = null;
        RenderTexture generatedPreview = null;
        try
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Run in Edit Mode.");
            LoadingScreenAssets assets = Resources.Load<LoadingScreenAssets>("Loading/LoadingScreenAssets");
            Check(assets != null, "Shared loading assets are missing.");
            SerializedProperty show = new SerializedObject(assets).FindProperty("showPlayerPreview");
            Check(show != null && !show.boolValue, "Refresh Unity and disable the loading player preview first.");
            string assetJson = EditorJsonUtility.ToJson(assets), path = AssetDatabase.GetAssetPath(assets);
            Hash128 assetFile = Hash128.Compute(File.ReadAllText(path));
            preview = EditorSceneManager.NewPreviewScene();
            fixture = new GameObject("Detached loading overlay validation");
            SceneManager.MoveGameObjectToScene(fixture, preview); fixture.SetActive(false);
            manager = fixture.AddComponent<LoadingScreenManager>();
            Set(manager, "assets", assets);
            Set(manager, "loadingFont", Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF"));
            Call(manager, "BuildPresentation");
            presentation = (GameObject)Get(manager, "presentationRoot");
            Check(presentation != null, "Loading presentation is missing.");
            // Only the child Canvas becomes active; the manager stays inactive so
            // Awake/Bootstrap and real scene-loading services cannot run.
            presentation.transform.SetParent(null, false);
            Canvas.ForceUpdateCanvases();
            CheckNoPreview(manager, preview);
            Call(manager, "PreparePlayerPreview");
            CheckNoPreview(manager, preview);
            report.AppendLine("PASS: Shared assets disable the character. BuildPresentation and defensive PreparePlayerPreview create no Running Player, RawImage, model, Animator, preview Camera, Light or RenderTexture.");

            Call(manager, "SetVisible", true, true);
            CanvasGroup group = (CanvasGroup)Get(manager, "canvasGroup");
            Check(presentation.activeSelf && group.alpha == 1f && group.blocksRaycasts && group.interactable,
                "Visible loading overlay does not block input.");
            foreach (float progress in new[] { 0f, .5f, 1f })
            {
                Call(manager, "SetProgress", progress);
                var text = (TMP_Text)Get(manager, "percentageText");
                Image fill = (Image)Get(manager, "progressFill");
                Check(text.text == Mathf.RoundToInt(progress * 100f) + "%", "Loading percentage is incorrect.");
                Check(Mathf.Approximately(fill.rectTransform.anchorMax.x, progress) && fill.enabled == (progress > .001f),
                    "Progress fill is incorrect.");
            }
            Call(manager, "SetLoadingTip");
            Check(!string.IsNullOrWhiteSpace(((TMP_Text)Get(manager, "tipText")).text), "Loading tip is empty.");
            Call(manager, "SetVisible", false, true);
            Check(!presentation.activeSelf && group.alpha == 0f && !group.blocksRaycasts && !group.interactable,
                "Closed loading overlay still blocks input.");
            CheckNoPreview(manager, preview);
            Check(EditorJsonUtility.ToJson(assets) == assetJson && Hash128.Compute(File.ReadAllText(path)) == assetFile,
                "Shared loading asset changed during validation.");
            report.AppendLine("PASS: Progress displays 0%, 50% and 100%; fill anchors and visibility match. Tips remain populated, visible overlay blocks input and closed overlay releases input.");
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            // OnDestroy normally uses delayed Destroy; own generated resources
            // explicitly here because this fixture runs outside Play Mode.
            if (manager != null)
            {
                generatedSprite = Get(manager, "roundedUiSprite") as Sprite;
                generatedTexture = Get(manager, "roundedUiTexture") as Texture2D;
                generatedPreview = Get(manager, "previewTexture") as RenderTexture;
                Set(manager, "roundedUiSprite", null); Set(manager, "roundedUiTexture", null); Set(manager, "previewTexture", null);
            }
            if (presentation != null) Object.DestroyImmediate(presentation);
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (generatedPreview != null) { generatedPreview.Release(); Object.DestroyImmediate(generatedPreview); }
            if (generatedSprite != null) Object.DestroyImmediate(generatedSprite);
            if (generatedTexture != null) Object.DestroyImmediate(generatedTexture);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            UnityEngine.Random.state = random;
            bool unchanged = LoadingScreenManager.Instance == singleton && scenes.Count == SceneManager.sceneCount &&
                scenes.All(item => item.Key.isDirty == item.Value.Item1 && item.Key.GetRootGameObjects().SequenceEqual(item.Value.Item2));
            report.AppendLine(unchanged ? "PASS: Source scenes, dirty flags, loading singleton and random state preserved. No scene load, authentication, player-data/preferences write, asset save or scene save occurred." :
                "FAIL: Original source scene or singleton state changed.");
            File.WriteAllText(Report, report.ToString());
        }
    }

    private static void CheckNoPreview(LoadingScreenManager manager, Scene scene)
    {
        foreach (string name in new[] { "playerImage", "playerRect", "previewCamera", "previewStage", "previewModel", "previewTexture" })
            Check((Object)Get(manager, name) == null, "Unexpected allocated preview field: " + name);
        GameObject[] roots = scene.GetRootGameObjects();
        Check(!roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Any(item => item.name == "Running Player"), "Running Player was created.");
        Check(!roots.SelectMany(root => root.GetComponentsInChildren<Component>(true)).Any(item => item is Camera || item is Light ||
            item is Animator || item is Renderer || item is RawImage), "Unexpected preview rendering component was created.");
    }
    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
