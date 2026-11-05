#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks wide-road joints on temporary objects without changing open scenes.</summary>
internal static class WidePointVisibilityValidation
{
    private const string Request = "Temp/wide-point-visibility-validation.request";
    private const string Report = "Logs/wide-point-visibility-validation.txt";

    [InitializeOnLoadMethod]
    private static void Watch() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            !File.Exists(Request)) return;
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Build/Validate Wide Road Points")]
    public static void Validate()
    {
        var results = new List<string>();
        Scene scene = EditorSceneManager.NewPreviewScene();
        ContractSO contract = ScriptableObject.CreateInstance<ContractSO>();
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BridgeBuilder/Prefabs/Point 1.prefab");
            Require(prefab != null, "Point prefab is missing.");
            var location = NewObject(scene, "Point width fixture").AddComponent<BuildLocation>();
            location.enabled = false;
            location.activeContract = contract;
            var pointObject = Object.Instantiate(prefab, new Vector3(8f, 3f, 7f), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(pointObject, scene);
            var point = pointObject.GetComponent<Point>();
            point.AssignOwner(location, true);
            pointObject.SetActive(true);
            Renderer visual = point.GetComponentInChildren<Renderer>();
            // Moving an ExecuteInEditMode point into the preview can call its OnDisable.
            visual.enabled = true;
            Bounds standardBounds = visual.bounds;
            Vector3 rootPosition = point.transform.position, rootScale = point.transform.localScale;
            Quaternion rootRotation = point.transform.rotation;
            int colliders = point.GetComponentsInChildren<Collider>().Length;
            int bodies = point.GetComponentsInChildren<Rigidbody>().Length;
            var materials = new[] { Load("Beam"), Load("ConcreteBeam"), Load("SteelBeam"), Load("SteelRope") };
            contract.allowedMaterials = materials.Select(material => new MaterialAllowance { material = material }).ToList();
            var camera = NewObject(scene, "Point width fixture camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 16f / 9f;
            camera.pixelRect = new Rect(0f, 0f, 1280f, 720f);
            camera.transform.position = rootPosition + Vector3.back * 40f;
            camera.transform.LookAt(rootPosition);
            contract.twoWayRoad = true;
            foreach (float width in new[] { 16f, 19f, 24f })
            {
                contract.twoWayRoadWidth = width;
                point.AssignOwner(location);
                Bounds wide = visual.bounds;
                float expectedDepth = BridgeWidthGeometry.GetPointVisualDepth(contract);
                Require(Mathf.Abs(wide.size.z - Mathf.Max(expectedDepth, standardBounds.size.z)) < .01f &&
                    Mathf.Abs(wide.center.z - rootPosition.z) < .01f, "Point depth or center does not match the contract.");
                Require(Mathf.Abs(wide.size.x - standardBounds.size.x) < .001f && Mathf.Abs(wide.size.y - standardBounds.size.y) < .001f,
                    "Point diameter changed with road width.");
                foreach (BridgeMaterialSO material in materials)
                {
                    Bounds shape = BridgeWidthGeometry.GetPrefabBounds(material.segmentPrefab);
                    float outer = BridgeWidthGeometry.GetSideOffset(material, width) + Mathf.Max(Mathf.Abs(shape.min.z), Mathf.Abs(shape.max.z));
                    Require(wide.extents.z > outer + .15f, "Point cap is hidden by the paired supports.");
                }
                Vector2 screenCenter = camera.WorldToScreenPoint(rootPosition);
                Require(BridgePointScreenGeometry.TryGetDistance(camera, visual, screenCenter, out float hit) && hit < .01f,
                    "The extended point cannot be tapped from the side camera.");
                camera.transform.position = rootPosition + new Vector3(-15f, 12f, -38f);
                camera.transform.LookAt(rootPosition);
                Vector2 tip = camera.WorldToScreenPoint(new Vector3(wide.center.x, wide.center.y, wide.min.z + .05f));
                Require(BridgePointScreenGeometry.TryGetDistance(camera, visual, tip, out hit) && hit < .01f,
                    "The extended cap cannot start a gesture in the slanted camera.");
                Require(BridgePointScreenGeometry.TryGetPlaneDistance(camera, visual, tip, rootPosition.z, out float endDistance) && endDistance > 8f,
                    "The extended depth made destination snapping overly broad.");
                camera.transform.position = rootPosition + Vector3.back * 40f;
                camera.transform.LookAt(rootPosition);
                for (int repeat = 0; repeat < 5; repeat++) point.RefreshWidthVisual();
                Require(Vector3.Distance(visual.bounds.size, wide.size) < .001f, "Point depth accumulates on repeated refresh.");
                var cloneObject = Object.Instantiate(point.gameObject);
                SceneManager.MoveGameObjectToScene(cloneObject, scene);
                var clone = cloneObject.GetComponent<Point>();
                clone.AssignOwner(location, true);
                Require(Vector3.Distance(clone.GetComponentInChildren<Renderer>().bounds.size, wide.size) < .001f,
                    "Cloned or reconstructed points do not retain their contract width.");
                contract.twoWayRoad = false;
                clone.AssignOwner(location, true);
                Require(Vector3.Distance(clone.GetComponentInChildren<Renderer>().bounds.size, standardBounds.size) < .001f,
                    "A cloned wide point cannot restore its original dimensions.");
                contract.twoWayRoad = true;
                results.Add($"PASS: {width:0} m road uses a {wide.size.z:F3} m joint, with exposed caps, preserved diameter, and touch geometry.");
            }
            contract.twoWayRoad = false;
            point.AssignOwner(location);
            Require(Vector3.Distance(visual.bounds.size, standardBounds.size) < .001f && point.transform.position == rootPosition &&
                point.transform.localScale == rootScale && point.transform.rotation == rootRotation &&
                point.GetComponentsInChildren<Collider>().Length == colliders && point.GetComponentsInChildren<Rigidbody>().Length == bodies,
                "Logical point, legacy geometry, or physics changed.");
            results.Add("PASS: original point dimensions restore when Two-Way Road is off; graph coordinates and physics components are preserved.");
            WidePointGuestValidation.Validate(scene);
            results.Add("PASS: guest marker sizing follows connected host members and resets for legacy or unrelated joints.");
        }
        catch (Exception error) { results.Add("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(contract);
            Directory.CreateDirectory("Logs");
            File.WriteAllLines(Report, results);
            Debug.Log(string.Join("\n", results));
        }
    }

    private static BridgeMaterialSO Load(string name) => Resources.Load<BridgeMaterialSO>(name)
        ?? throw new InvalidOperationException("Missing material " + name);
    private static GameObject NewObject(Scene scene, string name)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        return obj;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
