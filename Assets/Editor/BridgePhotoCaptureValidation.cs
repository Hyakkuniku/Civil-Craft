#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Checks the production photo framing and renders isolated example bridges.</summary>
public static class BridgePhotoCaptureValidation
{
    private const string Output = "Logs/BridgePhotoCapture";

    [MenuItem("Tools/Civil Craft/Validate Bridge Photo Framing")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before validating photo framing.");
        Directory.CreateDirectory(Output);
        Scene active = SceneManager.GetActiveScene();
        var originals = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .ToDictionary(scene => scene, scene => scene.isDirty);
        var report = new StringBuilder();
        Validate(false, report);
        Validate(true, report);
        Require(originals.All(pair => pair.Key.isLoaded && pair.Key.isDirty == pair.Value) &&
            SceneManager.GetActiveScene() == active, "Validation changed a source scene.");
        File.WriteAllText(Output + "/validation.txt", report.ToString());
        Debug.Log("[Bridge Photo] PASS\n" + report);
    }

    private static void Validate(bool wide, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Detached photo fixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        ContractSO contract = ScriptableObject.CreateInstance<ContractSO>();
        Material ground = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        RenderTexture sentinel = null;
        RenderTexture initialActive = RenderTexture.active;
        Texture2D photo = null;
        try
        {
            GameObject Child(string name)
            {
                var item = new GameObject(name);
                item.transform.SetParent(root.transform);
                return item;
            }
            var location = Child("Photo site").AddComponent<BuildLocation>();
            contract.twoWayRoad = wide;
            contract.twoWayRoadWidth = 16f;
            location.activeContract = contract;
            var camera = Child("Photo camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(240, 184, 157, 255);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            location.cinematicCamera = camera;
            var grid = new GameObject("Grid state sentinel", typeof(RectTransform), typeof(Image));
            grid.transform.SetParent(root.transform);
            location.gridImage = grid.GetComponent<Image>();
            var sun = Child("Preview sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.transform.rotation = Quaternion.Euler(45f, -25f, 0f);

            BridgeMaterialSO Material(string name) =>
                AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>($"Assets/BridgeBuilder/Data/Resources/{name}.asset");
            BridgeMaterialSO road = Material(wide ? "Concrete Road" : "Wood Road");
            BridgeMaterialSO beam = Material(wide ? "SteelBeam" : "Beam");
            BridgeMaterialSO pier = Material("Concrete Pier");
            BridgeMaterialSO rope = Material("SteelRope");
            Require(road != null && beam != null && pier != null && rope != null, "Production materials are missing.");
            Point Joint(float x, float y, bool bank = false)
            {
                Point point = Child("Joint").AddComponent<Point>();
                point.transform.position = new Vector3(x, y, 0f);
                point.originalIsAnchor = point.isAnchor = bank;
                point.AssignOwner(location);
                if (bank) location.startingAnchors.Add(point);
                return point;
            }
            Bar Place(Point a, Point b, BridgeMaterialSO material)
            {
                Bar bar = Child(material.name).AddComponent<Bar>();
                bar.AssignOwner(location);
                bar.startPoint = a;
                bar.endPoint = b;
                bar.StartPosition = a.transform.position;
                bar.Initialize(material);
                a.ConnectedBars.Add(bar);
                b.ConnectedBars.Add(bar);
                bar.UpdateCreatingBar(b.transform.position);
                location.bakedBars.Add(bar);
                return bar;
            }
            ground.color = new Color32(183, 126, 99, 255);
            void Bank(float x, float width)
            {
                GameObject bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bank.transform.SetParent(root.transform);
                bank.transform.position = new Vector3(x, -8.1f, 0f);
                bank.transform.localScale = new Vector3(width, 16f, 22f);
                bank.GetComponent<Renderer>().sharedMaterial = ground;
                Object.DestroyImmediate(bank.GetComponent<Collider>());
            }
            float length = wide ? 110f : 70f;
            Bank(-8f, 16f);
            Bank(length + 8f, 16f);
            if (!wide) Bank(35f, 10f);
            Point previous = null, previousTop = null;
            for (int i = 0; i <= (wide ? 11 : 7); i++)
            {
                Point current = Joint(i * 10f, 0f, i == 0 || i == (wide ? 11 : 7));
                if (previous != null && (wide || i != 4))
                {
                    Place(previous, current, road);
                    Point top = Joint(i * 10f - 5f, wide ? 8f : 7f);
                    Place(previous, top, beam);
                    Place(top, current, beam);
                    if (previousTop != null) Place(previousTop, top, beam);
                    previousTop = top;
                }
                else previousTop = null;
                if (wide && (i == 3 || i == 8))
                {
                    Place(Joint(i * 10f, -35f), current, pier);
                    Point tower = Joint(i * 10f, 32f);
                    Place(current, tower, beam);
                    Place(Joint(i == 3 ? 0f : length, 0f), tower, rope);
                    Place(tower, Joint(i == 3 ? 55f : 60f, 14f), rope);
                }
                previous = current;
            }
            // Baked member scripts are disabled in normal play.
            location.bakedBars[0].enabled = false;
            Require(BridgePhotoCapture.TryGetBounds(location, out Bounds bounds), "Bridge bounds are missing.");
            Require(bounds.size.x >= length && (!wide || bounds.min.y < -34f && bounds.max.y > 31f &&
                bounds.size.z >= 15.99f), $"Full spans, piers, towers or deck width were omitted: {bounds.ToString("F3")}.");

            // An unrelated logical bridge piece must never enlarge this photo.
            Bar unrelated = Child("Other site's bridge").AddComponent<Bar>();
            unrelated.startPoint = Joint(2000f, 1500f);
            unrelated.endPoint = Joint(2100f, 1600f);
            unrelated.materialData = road;
            Require(BridgePhotoCapture.TryGetBounds(location, out Bounds checkedBounds) && checkedBounds == bounds,
                "An unrelated bridge changed the framing.");

            foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 2.4f, 1f })
            {
                BridgePhotoCapture.Frame(camera, bounds, aspect);
                foreach (Vector3 corner in BridgePhotoCapture.Corners(bounds))
                {
                    Vector3 viewport = camera.WorldToViewportPoint(corner);
                    Require(viewport.x >= .02f && viewport.x <= .98f && viewport.y >= .02f && viewport.y <= .98f &&
                        viewport.z > camera.nearClipPlane && viewport.z < camera.farClipPlane,
                        "A bridge corner is clipped at aspect " + aspect);
                }
            }
            camera.transform.SetPositionAndRotation(new Vector3(7f, 22f, -58f), Quaternion.Euler(41f, 12f, 0f));
            camera.orthographic = false;
            camera.fieldOfView = 53f;
            camera.aspect = 1.3f;
            camera.rect = new Rect(.1f, .1f, .8f, .8f);
            camera.nearClipPlane = .3f;
            camera.farClipPlane = 800f;
            camera.useOcclusionCulling = true;
            sentinel = new RenderTexture(64, 64, 16);
            camera.targetTexture = sentinel;
            RenderTexture oldActive = RenderTexture.active;
            RenderTexture.active = sentinel;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            Matrix4x4 oldProjection = camera.projectionMatrix;
            photo = BridgePhotoCapture.Capture(location, 1280, 720);
            Require(photo != null && photo.width == 1280 && photo.height == 720, "The real capture failed.");
            Require(camera.transform.position == oldPosition && camera.transform.rotation == oldRotation &&
                !camera.orthographic && camera.fieldOfView == 53f && camera.aspect == 1.3f &&
                camera.nearClipPlane == .3f && camera.farClipPlane == 800f &&
                camera.rect == new Rect(.1f, .1f, .8f, .8f) && camera.useOcclusionCulling &&
                camera.targetTexture == sentinel && RenderTexture.active == sentinel &&
                camera.projectionMatrix == oldProjection && !camera.enabled && location.gridImage.enabled,
                "The capture did not restore its camera, grid or render target.");
            RenderTexture.active = oldActive;
            string name = wide ? "LongWideSteelBridge" : "DoubleWoodTruss";
            File.WriteAllBytes(Output + "/" + name + ".png", photo.EncodeToPNG());
            camera.fieldOfView = 40f;
            Require(camera.projectionMatrix != oldProjection, "The photo left normal camera zoom stuck on a custom projection.");
            Matrix4x4 custom = camera.projectionMatrix;
            custom[0, 2] = .08f;
            camera.projectionMatrix = custom;
            Object.DestroyImmediate(photo);
            photo = BridgePhotoCapture.Capture(location, 960, 540);
            Require(photo != null && camera.projectionMatrix == custom, "The capture lost an authored custom projection.");
            report.AppendLine($"PASS {name}: full geometry in 4 aspect ratios, detached render, camera/grid/target restored, default zoom and custom projection preserved.");
        }
        finally
        {
            RenderTexture.active = initialActive;
            if (photo != null) Object.DestroyImmediate(photo);
            Object.DestroyImmediate(root);
            if (sentinel != null) Object.DestroyImmediate(sentinel);
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(contract);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
#endif
