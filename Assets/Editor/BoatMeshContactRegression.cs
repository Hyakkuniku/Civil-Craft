using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporary collider fixtures live in a preview scene; no gameplay objects or
// authored scene data are changed by this diagnostic.
public static class BoatMeshContactRegression
{
    [MenuItem("Tools/Civil Craft/Diagnostics/Test Boat Mesh Contact")]
    public static void Run()
    {
        var report = new StringBuilder();
        Scene preview = EditorSceneManager.NewPreviewScene();
        Mesh testMesh = null;
        try
        {
            var meshObject = CreateFixture("Boat mesh probe", preview);
            var boxObject = CreateFixture("Bridge box probe", preview);
            var meshCollider = meshObject.AddComponent<MeshCollider>();
            var box = boxObject.AddComponent<BoxCollider>();
            testMesh = new Mesh { name = "Boat contact regression cube" };
            testMesh.vertices = new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            testMesh.triangles = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7,
                0,1,5, 0,5,4, 3,7,6, 3,6,2, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
            testMesh.RecalculateBounds();
            meshCollider.sharedMesh = testMesh;
            box.size = Vector3.one * .2f;
            box.transform.position = new Vector3(.5f, 0f, 0f);

            bool enabledContact = Contact(box, meshCollider, Vector3.zero);
            meshCollider.enabled = false;
            bool disabledContact = Contact(box, meshCollider, Vector3.zero);
            meshCollider.excludeLayers = ~0;
            meshCollider.includeLayers = 0;
            meshCollider.layerOverridePriority = int.MaxValue;
            meshCollider.enabled = true;
            bool excludedContact = Contact(box, meshCollider, Vector3.zero);
            bool separated = Contact(box, meshCollider, Vector3.up * 10f);
            report.AppendLine($"Primitive fixture: enabled={enabledContact}, disabled={disabledContact}, contactsExcluded={excludedContact}, separated={separated}");
            if (!enabledContact || !excludedContact || separated)
                throw new InvalidOperationException("Mesh contact fixture failed.");

            foreach (var boat in UnityEngine.Object.FindObjectsOfType<BoatBridgeCrossing>(true))
            {
                if (!boat.gameObject.scene.IsValid() || boat.startPoint == null || boat.endPoint == null) continue;
                int enabledHits = 0, disabledHits = 0;
                foreach (var source in boat.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (source.sharedMesh == null) continue;
                    var probe = CreateFixture("Boat mesh route probe", preview).AddComponent<MeshCollider>();
                    probe.excludeLayers = ~0;
                    probe.includeLayers = 0;
                    probe.layerOverridePriority = int.MaxValue;
                    probe.sharedMesh = source.sharedMesh;
                    probe.convex = source.convex;
                    probe.transform.localScale = source.transform.lossyScale;
                    Quaternion rotationDelta = boat.startPoint.rotation * Quaternion.Inverse(boat.transform.rotation);
                    Vector3 relativePosition = rotationDelta * (source.transform.position - boat.transform.position);
                    probe.transform.SetPositionAndRotation(boat.startPoint.position + relativePosition,
                        rotationDelta * source.transform.rotation);
                    // A test road spans the actual imported hull at the middle
                    // of its configured route. The fixture is in the preview
                    // scene, not in the user's bridge or save data.
                    Bounds meshBounds = WorldBounds(probe);
                    Vector3 halfway = (boat.endPoint.position - boat.startPoint.position) * .5f;
                    box.transform.position = meshBounds.center + halfway;
                    box.size = new Vector3(Mathf.Max(1f, meshBounds.size.x * 1.2f), .2f, .5f);
                    int meshEnabledHits = 0, meshDisabledHits = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        probe.enabled = pass == 0;
                        for (int sample = 0; sample <= 1000; sample++)
                        {
                            Vector3 position = Vector3.Lerp(boat.startPoint.position, boat.endPoint.position,
                                sample / 1000f) + relativePosition;
                            bool hit = Contact(box, probe, position);
                            if (hit && pass == 0) meshEnabledHits++;
                            if (hit && pass == 1) meshDisabledHits++;
                        }
                    }
                    enabledHits += meshEnabledHits;
                    disabledHits += meshDisabledHits;
                    report.AppendLine($"Scene mesh {source.name}: scale={source.transform.lossyScale}, enabled contacts={meshEnabledHits}, disabled contacts={meshDisabledHits}");
                }
                report.AppendLine($"Boat {boat.name}: enabled route contacts={enabledHits}, disabled route contacts={disabledHits}");
                if (enabledHits == 0 || disabledHits != 0)
                    throw new InvalidOperationException("Imported boat route fixture failed.");
            }
            report.AppendLine("PASS: isolated mesh query regression.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if (testMesh != null) UnityEngine.Object.DestroyImmediate(testMesh);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/BoatMeshContactRegression.txt", report.ToString());
            Debug.Log("[BoatMeshContactRegression] " + report);
        }
    }

    private static GameObject CreateFixture(string name, Scene scene)
    {
        var fixture = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(fixture, scene);
        return fixture;
    }

    private static bool Contact(Collider bridge, MeshCollider boat, Vector3 boatPosition)
    {
        return Physics.ComputePenetration(bridge, bridge.transform.position, bridge.transform.rotation,
            boat, boatPosition, boat.transform.rotation, out _, out _);
    }

    private static Bounds WorldBounds(MeshCollider collider)
    {
        Bounds local = collider.sharedMesh.bounds;
        Bounds world = new Bounds(collider.transform.TransformPoint(local.min), Vector3.zero);
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
            world.Encapsulate(collider.transform.TransformPoint(new Vector3(
                x == 0 ? local.min.x : local.max.x,
                y == 0 ? local.min.y : local.max.y,
                z == 0 ? local.min.z : local.max.z)));
        return world;
    }
}
