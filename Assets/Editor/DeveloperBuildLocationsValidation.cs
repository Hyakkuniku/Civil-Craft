#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DeveloperBuildLocationsValidation
{
    public static void AuditCampaign()
    {
        const string path = "Assets/Scenes/CanyonCrossing.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var sites = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BuildLocation>(true))
                .Where(site => !site.IsSessionChallengeLocation).ToArray();
            Require(sites.Length > 0, "Campaign has no build locations.");
            string[] rows = sites.Select(site => site.name + " | " +
                (site.ChallengeContractDefinition != null ? site.ChallengeContractDefinition.ContractID : "MISSING CONTRACT") +
                " | arrival: " + (site.fastTravelTarget != null ? site.fastTravelTarget.name : "legacy fallback")).ToArray();
            Directory.CreateDirectory("Logs/DeveloperBuildLocations");
            File.WriteAllLines("Logs/DeveloperBuildLocations/Campaign-Sites.txt", rows);
            int configured = sites.Count(site => site.ChallengeContractDefinition != null);
            Require(configured > 0, "Campaign has no configured build locations.");
            Debug.Log("[Developer Build Locations] Campaign audit PASS: " + configured + " configured sites; " +
                (sites.Length - configured) + " without contracts will be skipped; " +
                sites.Count(site => site.fastTravelTarget != null) + " authored expanded-map arrivals. Nothing saved.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    [MenuItem("Tools/Civil Craft/Settings/Validate Build Location Developer Actions")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before detached validation.");
        const string output = "Logs/DeveloperBuildLocations";
        Directory.CreateDirectory(output);
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var prefab = Resources.Load<GameObject>(DevelopmentSettingsAccess.ResourcePath);
            Require(prefab != null, "The shared development menu is missing.");
            GameObject copy = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(copy, preview);
            DeveloperDebugManager manager = copy.GetComponent<DeveloperDebugManager>();
            var serialized = new SerializedObject(manager);
            var canvas = (Canvas)serialized.FindProperty("debugCanvas").objectReferenceValue;
            var window = (GameObject)serialized.FindProperty("debugWindow").objectReferenceValue;
            Require(!serialized.FindProperty("allowInReleaseBuild").boolValue, "Shared menu permits release builds.");
            window.SetActive(true);
            manager.RefreshDebugLayout();
            manager.RefreshDebugLayout();
            Button[] buttons = copy.GetComponentsInChildren<Button>(true)
                .Where(button => button.name == "CompleteAllBuildLocationsButton").ToArray();
            Require(buttons.Length == 1 && buttons[0].gameObject.activeInHierarchy && buttons[0].interactable,
                "Repeated menu initialization must retain one usable completion button.");
            Require(buttons[0].transform.parent.name == "ContractActionsRow", "Button is outside Contract Actions.");
            var cameraObject = new GameObject("Dev actions preview camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.orthographic = true;
            camera.useOcclusionCulling = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(40, 47, 60, 255);
            camera.transform.position = new Vector3(0, 0, -20);
            canvas.renderMode = RenderMode.WorldSpace;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.sizeDelta = new Vector2(1920, 1080);
            canvasRect.localScale = Vector3.one * .01f;
            canvasRect.position = Vector3.zero;
            RectTransform panel = window.transform.Find("ScreenBlocker/DebugPanel") as RectTransform;
            RectTransform content = panel.Find("ScrollView/Viewport/Content") as RectTransform;
            ScrollRect scroll = content.GetComponentInParent<ScrollRect>();
            foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1440, 1080) })
            {
                // Inspect the action at phone and tablet aspect ratios.
                // Refresh the actual layout before bringing its row into the viewport.
                manager.RefreshDebugLayout();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                var row = (RectTransform)buttons[0].transform.parent;
                Vector2 position = content.anchoredPosition;
                position.y = Mathf.Max(0, -row.anchoredPosition.y - 120);
                content.anchoredPosition = position;
                if (scroll != null) scroll.velocity = Vector2.zero;
                TMP_Text caption = buttons[0].GetComponentInChildren<TMP_Text>(true);
                caption.ForceMeshUpdate();
                Require(!caption.isTextOverflowing && buttons[0].GetComponent<RectTransform>().rect.height >= 60,
                    "Completion caption clips or the tap area is too small.");
                camera.orthographicSize = 5.6f;
                camera.transform.position = new Vector3(panel.position.x, panel.position.y, -20);
                Capture(camera, (int)size.x, (int)size.y, output + "/Dev-Actions-" + (int)size.x + ".png");
            }
            File.WriteAllText(output + "/Validation.txt", "PASS: native compilation, shared development-only menu, one completion action after repeated setup, readable caption and full tap area. Preview copies only; no scenes or player files saved.");
            Debug.Log("[Developer Build Locations] PASS");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void Require(bool passed, string message)
    { if (!passed) throw new InvalidOperationException(message); }

    private static void Capture(Camera camera, int width, int height, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(width, height, 24);
        target.Create();
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }
}
#endif
