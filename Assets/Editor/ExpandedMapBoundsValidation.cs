#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Read-only map navigation checks in a disposable scene, then the saved campaign.</summary>
public static class ExpandedMapBoundsValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] values) =>
        target.GetType().GetMethod(method, Private).Invoke(target, values);
    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }

    [MenuItem("Tools/Civil Craft/Map/Validate Expanded Map Bounds")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before validating map navigation.");
        var report = new StringBuilder();
        Scene active = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture render = null;
        try
        {
            var player = new GameObject("Bounds fixture player");
            SceneManager.MoveGameObjectToScene(player, preview);
            CampaignEdgeGuards guards = player.AddComponent<CampaignEdgeGuards>();
            var perimeter = new GameObject("Fixture perimeter");
            SceneManager.MoveGameObjectToScene(perimeter, preview);
            perimeter.layer = CampaignEdgeGuards.LayerIndex;
            perimeter.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            BoxCollider collider = perimeter.AddComponent<BoxCollider>();
            collider.size = new Vector3(120f, 5f, 200f);
            Set(guards, "guardRoot", perimeter.transform);
            Require(guards.TryGetMapBounds(out Bounds footprint) &&
                Mathf.Abs(footprint.size.x - 200f) < .001f && Mathf.Abs(footprint.size.z - 120f) < .001f,
                "Rotated guard geometry did not produce the correct map perimeter.");
            collider.enabled = false;
            perimeter.SetActive(false);
            Require(guards.TryGetMapBounds(out Bounds inactive) && inactive == footprint,
                "Turning player edge protection off removed the map limits.");

            var canvasObject = new GameObject("Map fixture Canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            var panel = new GameObject("Map panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(canvasObject.transform);
            panel.sizeDelta = new Vector2(600f, 300f);
            var image = new GameObject("Map image", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(panel);
            image.rectTransform.sizeDelta = panel.sizeDelta;
            var cameraObject = new GameObject("Map navigation camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 100f, 0f), Quaternion.Euler(90f, 0f, 0f));
            render = new RenderTexture(1200, 600, 16);
            camera.targetTexture = render;
            ExpandedMinimapController map = cameraObject.AddComponent<ExpandedMinimapController>();
            map.enabled = false;
            Set(map, "minimapCamera", camera);
            Set(map, "minimapPanel", panel);
            Set(map, "mapImage", image);
            Set(map, "boundsPadding", 0f);
            Set(map, "isExpanded", true);
            Call(map, "BuildWorldBoundsAndFraming");
            Call(map, "SetZoom", 20f);
            Call(map, "SetCameraCenterClamped", new Vector3(10000f, 0f, 10000f));
            Require(Vector3.Distance(camera.transform.position, new Vector3(60f, 100f, 40f)) < .001f,
                "The viewport escaped the top/right perimeter.");
            Call(map, "PanByCanvasDelta", new Vector2(100000f, 100000f));
            Require(Vector3.Distance(camera.transform.position, new Vector3(-60f, 100f, -40f)) < .001f,
                "Dragging escaped the bottom/left perimeter.");
            for (int i = 0; i < 20; i++) map.ZoomOut();
            Require(Mathf.Abs(camera.orthographicSize - 60f) < .001f, "Zoom-out exceeded the full-map overview.");
            Call(map, "SetCameraCenterClamped", new Vector3(10000f, 0f, -10000f));
            Require(camera.transform.position == new Vector3(0f, 100f, 0f),
                "An overview larger than one axis must remain centered.");
            float overviewZoom = camera.orthographicSize;
            Set(map, "isAnimating", true);
            map.ZoomIn();
            Require(camera.orthographicSize == overviewZoom, "Zoom interrupted a map transition.");
            Set(map, "isAnimating", false);
            Set(map, "frameBuildLocationsWhenOpened", false);
            Set(map, "compactCameraPosition", new Vector3(9000f, 100f, -9000f));
            Set(map, "compactOrthographicSize", 9000f);
            Call(map, "BuildWorldBoundsAndFraming");
            Require((Vector3)typeof(ExpandedMinimapController).GetField("framedCenter", Private).GetValue(map) ==
                new Vector3(0f, 100f, 0f), "Opening from an old out-of-bounds pose did not clamp the destination.");
            for (int i = 0; i < 20; i++) map.ZoomIn();
            Require(camera.orthographicSize == 3f, "Minimum map zoom changed.");
            Call(map, "SetCameraCenterClamped", new Vector3(10000f, 0f, 10000f));
            Require(Vector3.Distance(camera.transform.position, new Vector3(94f, 100f, 57f)) < .001f,
                "Zooming in must allow navigation close to the map edge.");
            Set(map, "constrainExpandedMap", false);
            Call(map, "SetCameraCenterClamped", new Vector3(500f, 100f, 500f));
            Require(camera.transform.position == new Vector3(500f, 100f, 500f), "The Inspector override did not work.");
            report.AppendLine("PASS: rotated and disabled guard geometry, viewport-aware corner limits, drag, zoom buttons, overview cap, opening pose, transition gate, near-edge zoom and Inspector override.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if (render != null) Object.DestroyImmediate(render);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
        ValidateCampaign(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/expanded-map-bounds-validation.txt", report.ToString());
        Debug.Log("[Expanded Map Bounds] PASS\n" + report);
    }

    private static void ValidateCampaign(StringBuilder report)
    {
        Scene active = SceneManager.GetActiveScene();
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        bool opened = !source.IsValid() || !source.isLoaded;
        if (opened) source = EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", OpenSceneMode.Additive);
        bool dirty = source.isDirty;
        try
        {
            CampaignEdgeGuards[] providers = source.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CampaignEdgeGuards>(true)).ToArray();
            Require(providers.Length > 0, "The campaign's authored edge guards are missing.");
            Bounds union = default;
            bool found = false;
            foreach (CampaignEdgeGuards provider in providers)
            {
                if (!provider.TryGetMapBounds(out Bounds bounds)) continue;
                if (found) union.Encapsulate(bounds);
                else { union = bounds; found = true; }
            }
            Require(found && union.min.x < 0f && union.max.x > 3300f && union.size.z > 200f,
                "The perimeter does not cover Canyon Crossing, Town River and Ironhaven.");
            Require(source.isDirty == dirty, "Bounds measurement edited the campaign scene.");
            report.AppendLine("PASS: saved campaign perimeter covers all three regions: " + union.ToString("F2"));
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(source, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
#endif
