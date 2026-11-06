using System;
using System.IO;
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

/// <summary>Detached reference renders of the authored card and Pause artwork at actual device aspect ratios.</summary>
public static class NotificationPausePlacementPreviews
{
    private const string Request = "Temp/notification-pause-preview.request";
    private const string Folder = "Logs/NotificationPausePreviews";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static double nextCheck;

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
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            BuildPipeline.isBuildingPlayer || !File.Exists(Request)) return;
        File.Delete(Request);
        Capture();
    }

    [MenuItem("Tools/Civil Craft/Preview Notification Below Pause")]
    public static void Capture()
    {
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run notification reference previews in Edit Mode.");
            foreach (Vector2Int size in new[] { new Vector2Int(2340, 1080), new Vector2Int(1920, 1080), new Vector2Int(1440, 1080) })
                Render(size, report);
            report.AppendLine("PASS: Detached reference renders use the actual 760x164 authored card, authored Pause sprite, matching CanvasScaler calculation and GameplayPauseButtonLayout.Apply. Fixed placement is derived from measured camera-space bounds and padded Pause touch bounds. Runtime FIFO/modal validation is separate. No source scenes, saves, player data or assets modified.");
            File.WriteAllText("Logs/notification-pause-preview.txt", report.ToString());
        }
        catch (Exception error)
        {
            File.WriteAllText("Logs/notification-pause-preview.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    private static void Render(Vector2Int size, StringBuilder report)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/AchievementPopupCanvas.prefab");
        GameObject pauseSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/MobilePauseButton.prefab");
        if (source == null || pauseSource == null) throw new InvalidOperationException("Authored notification or Pause prefab is missing.");
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active, target = null;
        Texture2D pixels = null;
        Camera camera = null;
        try
        {
            var cameraRoot = New(scene, "Detached notification reference camera");
            camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.orthographicSize = size.y * .005f;
            camera.aspect = size.x / (float)size.y;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(111, 164, 184, 255);
            camera.nearClipPlane = .05f; camera.farClipPlane = 100f;
            camera.allowHDR = false; camera.allowMSAA = false;
            var cameraData = cameraRoot.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false; cameraData.volumeLayerMask = 0;
            target = new RenderTexture(size.x, size.y, 24) { hideFlags = HideFlags.HideAndDontSave };
            if (!target.Create()) throw new InvalidOperationException("Could not create reference render target.");
            camera.targetTexture = target;

            CanvasScaler sourceScaler = source.GetComponent<CanvasScaler>();
            Vector2 reference = sourceScaler.referenceResolution;
            float scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(size.x / reference.x, 2f),
                Mathf.Log(size.y / reference.y, 2f), sourceScaler.matchWidthOrHeight));
            var root = New(scene, "Detached notification world canvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)root.transform).sizeDelta = new Vector2(size.x, size.y) / scale;
            root.transform.localScale = Vector3.one * (.01f * scale);

            GameObject prototype = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(prototype, scene);
            var controller = prototype.GetComponent<AchievementPopupNotification>();
            Call(controller, "ResolveVisualReferences");
            Call(controller, "Populate", Sample());
            var sourceCard = (GameObject)Field(controller, "popupRoot");
            GameObject card = Object.Instantiate(sourceCard, root.transform, false);
            Object.DestroyImmediate(prototype);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = Vector2.one * .5f;
            cardRect.localRotation = Quaternion.identity; cardRect.localScale = Vector3.one;
            card.GetComponent<CanvasGroup>().alpha = 1f;
            card.SetActive(true);

            var pause = New(scene, "Detached actual Pause artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pause.transform.SetParent(root.transform, false);
            Image pauseGraphic = pause.GetComponent<Image>(), sourceGraphic = pauseSource.GetComponent<Image>();
            pauseGraphic.sprite = sourceGraphic.sprite; pauseGraphic.color = sourceGraphic.color;
            pauseGraphic.type = sourceGraphic.type; pauseGraphic.preserveAspect = sourceGraphic.preserveAspect;
            RectTransform pauseRect = (RectTransform)pause.transform;
            GameplayPauseButtonLayout.Apply(pauseRect, scale, new Vector2(size.x, size.y),
                new Rect(0, 0, size.x, size.y), size.x > 2000 ? 400f : 240f, true);
            Canvas.ForceUpdateCanvases();
            Check(UIReservedRegionLayout.TryGetScreenRect(cardRect, out Rect cardBounds), "Card bounds cannot be measured.");
            Check(UIReservedRegionLayout.TryGetScreenRect(pauseRect, out Rect pauseBounds), "Pause bounds cannot be measured.");
            float touchBelow = Mathf.Max(0f, -pauseGraphic.raycastPadding.y) * pauseBounds.height / pauseRect.rect.height;
            float gap = 12f * scale;
            Vector2 desired = new Vector2(pauseBounds.center.x, pauseBounds.yMin - touchBelow - gap - cardBounds.height * .5f);
            Check(UIReservedRegionLayout.TrySetScreenCenter(cardRect, desired), "Card could not be aligned below Pause.");
            Canvas.ForceUpdateCanvases();
            Check(UIReservedRegionLayout.TryGetScreenRect(cardRect, out cardBounds), "Placed card cannot be measured.");
            Check(cardBounds.xMin >= 16f * scale && cardBounds.xMax <= size.x - 16f * scale && cardBounds.yMin >= 16f * scale,
                "Actual authored card escapes device safe bounds.");
            Check(Mathf.Abs(cardBounds.yMax - (pauseBounds.yMin - touchBelow - gap)) < 1f &&
                Mathf.Abs(cardBounds.center.x - pauseBounds.center.x) < 1f, "Preview fixed alignment changed.");

            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); pixels.Apply();
            string path = Folder + "/notification-pause-" + size.x + "x" + size.y + ".png";
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            report.AppendLine("PASS: " + path + "; card=" + cardBounds + "; Pause=" + pauseBounds + "; canvas scale=" + scale);
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static object Sample()
    {
        Type type = typeof(AchievementPopupNotification).GetNestedType("PopupRequest", BindingFlags.NonPublic);
        object request = Activator.CreateInstance(type);
        type.GetField("title").SetValue(request, "Builder's Field Book");
        type.GetField("detail").SetValue(request, "New lesson added to your Almanac");
        type.GetField("kind").SetValue(request, Enum.Parse(type.GetField("kind").FieldType, "Almanac"));
        return request;
    }

    private static GameObject New(Scene scene, string name, params Type[] components)
    {
        var obj = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(obj, scene); return obj;
    }
    private static object Field(object obj, string name) => obj.GetType().GetField(name, Fields).GetValue(obj);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Fields).Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
