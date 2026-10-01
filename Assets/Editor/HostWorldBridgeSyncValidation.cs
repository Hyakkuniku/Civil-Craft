using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated geometry and native collision checks. Never loads a saved game or changes an authored scene.</summary>
public static class HostWorldBridgeSyncValidation
{
    private const string RequestPath = "Temp/host-world-bridge-validation.request";
    private const string ReportPath = "Temp/HostWorldBridgeSyncValidation.txt";
    private delegate void ReceiveBridgePacket(PlayerRef connectionPlayer, int id, int revision, ReadOnlySpan<byte> data);

    [InitializeOnLoadMethod]
    private static void RunRequestedCheck()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(RequestPath))
            {
                File.Delete(RequestPath);
                Validate();
            }
        };
    }

    [MenuItem("Civil Craft/Multiplayer/Validate Host World Bridge Sync")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before running the isolated bridge sync checks.");
            return;
        }
        var report = new StringBuilder();
        Scene scene = EditorSceneManager.NewPreviewScene();
        HostWorldBridgeVisual visual = null;
        BridgeMaterialSO material = null;
        PhysicMaterial surfaceMaterial = null;
        try
        {
            var fixture = new GameObject("Bridge Sync Validation (Temporary)");
            SceneManager.MoveGameObjectToScene(fixture, scene);
            fixture.SetActive(false);
            var site = Child("Validation Site", fixture.transform).AddComponent<BuildLocation>();
            GameObject sourceMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourceMesh.transform.SetParent(fixture.transform, false);
            material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
            material.name = "Validation Road";
            material.segmentPrefab = sourceMesh;
            material.pierCapPrefab = sourceMesh;
            surfaceMaterial = new PhysicMaterial("Validation Surface")
            {
                staticFriction = 0.7f, dynamicFriction = 0.5f, bounciness = 0.1f,
                frictionCombine = PhysicMaterialCombine.Maximum, bounceCombine = PhysicMaterialCombine.Minimum
            };
            Bar bar = Child("Committed Road", fixture.transform).AddComponent<Bar>();
            bar.materialData = material;
            bar.transform.SetPositionAndRotation(new Vector3(7, 12, -5), Quaternion.Euler(0, 25, 8));
            bar.transform.localScale = new Vector3(2, 0.8f, 1.5f);
            GameObject segment = Child("VisualSegment_0", bar.transform);
            segment.transform.localPosition = new Vector3(0, 0.1f, 0.3f);
            segment.transform.localRotation = Quaternion.Euler(0, 12, 0);
            segment.transform.localScale = new Vector3(0.5f, 0.2f, 2);
            GameObject cap = Child("PierCap", bar.transform);
            cap.transform.localPosition = new Vector3(0, 2, 0);
            var box = bar.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(3, 0.24f, 5);
            box.center = new Vector3(0, -0.1f, 0);
            box.sharedMaterial = surfaceMaterial;
            bar.gameObject.layer = 8;
            var sphere = Child("Sphere Surface", bar.transform).AddComponent<SphereCollider>();
            sphere.radius = 0.35f;
            sphere.center = new Vector3(0.1f, 0.2f, 0);
            var capsule = Child("Capsule Surface", bar.transform).AddComponent<CapsuleCollider>();
            capsule.radius = 0.25f; capsule.height = 1.5f; capsule.direction = 2;
            var disabled = Child("Disabled Surface", bar.transform).AddComponent<BoxCollider>();
            disabled.enabled = false;
            var trigger = Child("Story Trigger", bar.transform).AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            var hidden = Child("Hidden Surface", bar.transform);
            hidden.AddComponent<BoxCollider>(); hidden.SetActive(false);
            site.bakedBars.Add(bar);

            HostWorldBridgeSnapshot captured = FusionHostWorldBridgeSync.CaptureLocation(site);
            Require(captured.Bars.Count == 1 && captured.Bars[0].Parts.Count == 2,
                "Capture includes the committed segment and pier cap.", report);
            Require(captured.Bars[0].Colliders.Count == 3,
                "Capture copies enabled solid colliders, excludes triggers, disabled and hidden children.", report);
            byte[] packet = HostWorldBridgeSnapshotCodec.Encode(captured);
            HostWorldBridgeSnapshot decoded = HostWorldBridgeSnapshotCodec.Decode(packet);
            Require(HostWorldBridgeSnapshotCodec.SameBytes(packet, HostWorldBridgeSnapshotCodec.Encode(decoded)),
                "Geometry round trip preserves all fields; unchanged geometry produces identical bytes.", report);
            ValidateHostReceiver(fixture.transform, packet, report);
            var catalog = new Dictionary<string, BridgeMaterialSO> { { material.Id, material } };
            visual = new HostWorldBridgeVisual(decoded, catalog, null, fixture.transform);
            Transform copyBar = visual.Root.transform.GetChild(0);
            Require(SamePose(copyBar, bar.transform), "Guest bar has the exact host world position, rotation and scale.", report);
            Transform copySegment = copyBar.GetChild(0);
            Require((copySegment.localPosition - segment.transform.localPosition).sqrMagnitude < 0.000001f &&
                Quaternion.Angle(copySegment.localRotation, segment.transform.localRotation) < 0.001f &&
                (copySegment.localScale - segment.transform.localScale).sqrMagnitude < 0.000001f,
                "Guest uses the material's actual mesh with the exact host segment transform.", report);
            Require(copySegment.GetComponent<MeshFilter>().sharedMesh == sourceMesh.GetComponent<MeshFilter>().sharedMesh,
                "Mesh assets are reused instead of creating placeholder geometry.", report);
            Collider[] colliders = visual.Root.GetComponentsInChildren<Collider>(true);
            Require(colliders.Length == 3 && colliders[0] is BoxCollider && colliders[1] is SphereCollider &&
                colliders[2] is CapsuleCollider, "Solid box, sphere and capsule collision is reconstructed.", report);
            var copyBox = (BoxCollider)colliders[0];
            Require(SamePose(copyBox.transform, box.transform) && copyBox.size == box.size &&
                copyBox.center == box.center && copyBox.gameObject.layer == box.gameObject.layer &&
                copyBox.sharedMaterial.staticFriction == surfaceMaterial.staticFriction &&
                copyBox.sharedMaterial.dynamicFriction == surfaceMaterial.dynamicFriction &&
                copyBox.sharedMaterial.bounciness == surfaceMaterial.bounciness &&
                copyBox.sharedMaterial.frictionCombine == surfaceMaterial.frictionCombine &&
                copyBox.sharedMaterial.bounceCombine == surfaceMaterial.bounceCombine,
                "Road collision preserves dimensions, world pose, layer, friction and bounce.", report);
            Require(visual.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                visual.Root.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                visual.Root.GetComponentsInChildren<Joint>(true).Length == 0,
                "Guest copy has no gameplay/save scripts, Rigidbody or physics joints.", report);
            // Activate only the guest copy in the preview physics scene. The
            // fixture remains inactive; no authored gameplay components run.
            visual.Root.transform.SetParent(null, true);
            visual.Root.SetActive(true);
            Physics.SyncTransforms();
            Vector3 roadCenter = copyBox.transform.TransformPoint(copyBox.center);
            var roadRay = new Ray(roadCenter + copyBox.transform.up * 10f, -copyBox.transform.up);
            Require(copyBox.Raycast(roadRay, out RaycastHit roadHit, 20f) &&
                Vector3.Distance(roadHit.point, roadCenter + copyBox.transform.up *
                    (copyBox.size.y * Mathf.Abs(copyBox.transform.lossyScale.y) * 0.5f)) < 0.001f,
                "Native physics raycast hits the guest road at its exact host surface height.", report);

            BridgeMaterialSO[] projectMaterials = Resources.LoadAll<BridgeMaterialSO>(string.Empty);
            var projectIds = new HashSet<string>();
            Require(projectMaterials.Length > 0, "Bridge materials are available through Resources in builds.", report);
            foreach (BridgeMaterialSO projectMaterial in projectMaterials)
            {
                Require(projectIds.Add(projectMaterial.Id), "Unique runtime material ID: " + projectMaterial.Id, report);
                Require(projectMaterial.segmentPrefab != null &&
                    projectMaterial.segmentPrefab.GetComponentsInChildren<MeshRenderer>(true).Length > 0,
                    "Static mesh is available for material: " + projectMaterial.Id, report);
            }

            var large = new HostWorldBridgeSnapshot { Name = "Large Completed Bridge" };
            for (int i = 0; i < 160; i++) large.Bars.Add(captured.Bars[0]);
            byte[] largePacket = HostWorldBridgeSnapshotCodec.Encode(large);
            Require(HostWorldBridgeSnapshotCodec.Decode(largePacket).Bars.Count == 160,
                $"160 bars survive serialization without the avatar's 96-bar limit ({largePacket.Length} bytes).", report);
            Require(HostWorldBridgeSnapshotCodec.Decode(HostWorldBridgeSnapshotCodec.Encode(new HostWorldBridgeSnapshot()))
                .Bars.Count == 0, "Empty snapshots represent deletion without retaining stale bars.", report);
            Require(HostWorldBridgeSnapshotCodec.IsNewerRevision(12, 11) &&
                !HostWorldBridgeSnapshotCodec.IsNewerRevision(11, 12) &&
                !HostWorldBridgeSnapshotCodec.IsNewerRevision(12, 12) &&
                HostWorldBridgeSnapshotCodec.IsNewerRevision(int.MinValue, int.MaxValue),
                "Late/duplicate revisions are rejected; revision wrap remains ordered.", report);
            byte[] shortPacket = new byte[packet.Length - 1];
            Array.Copy(packet, shortPacket, shortPacket.Length);
            RequireRejected(shortPacket, "Truncated packet is rejected before rendering.", report);
            byte[] invalidVersion = (byte[])packet.Clone(); invalidVersion[0] = 100;
            RequireRejected(invalidVersion, "Unsupported protocol version is rejected.", report);
            byte[] trailingData = new byte[packet.Length + 1]; Array.Copy(packet, trailingData, packet.Length);
            RequireRejected(trailingData, "Unexpected trailing data is rejected.", report);
            byte[] excessiveCount = HostWorldBridgeSnapshotCodec.Encode(new HostWorldBridgeSnapshot());
            Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, excessiveCount, 6, 4);
            RequireRejected(excessiveCount, "Excessive member count is rejected before allocation.", report);
            visual.Dispose(); visual = null;
            Require(colliders[0] == null && colliders[1] == null && colliders[2] == null,
                "Disposing a replacement removes its collision and visuals.", report);
            report.AppendLine("PASS: isolated editor checks complete. Live Photon and two-device walking still require a play test.");
            Debug.Log("[Fusion bridges validation] PASS. Report: " + ReportPath);
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL: " + exception);
            Debug.LogError("[Fusion bridges validation] " + exception);
        }
        finally
        {
            visual?.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
            if (material != null) Object.DestroyImmediate(material);
            if (surfaceMaterial != null) Object.DestroyImmediate(surfaceMaterial);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, report.ToString());
        }
    }

    private static GameObject Child(string name, Transform parent)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void ValidateHostReceiver(Transform fixture, byte[] packet, StringBuilder report)
    {
        // The installed SDK registers a client's server connection using that
        // client's own PlayerRef. Exercise the exact guest receiver with this
        // callback metadata instead of assuming it reports the host's player.
        var sync = Child("Guest Bridge Receiver", fixture).AddComponent<FusionHostWorldBridgeSync>();
        MethodInfo method = typeof(FusionHostWorldBridgeSync).GetMethod("ReceiveHostGeometry",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var receive = (ReceiveBridgePacket)Delegate.CreateDelegate(typeof(ReceiveBridgePacket), sync, method);
        var snapshots = (IDictionary)typeof(FusionHostWorldBridgeSync).GetField("guestLocations",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sync);
        PlayerRef guestPlayer = PlayerRef.FromIndex(1);
        receive(guestPlayer, 1, 2, packet);
        Require(snapshots.Count == 1 && CachedSnapshot(snapshots, 1).Bars.Count == 1,
            "Guest accepts host transfer whose connection PlayerRef is its own local player ID (regression).", report);
        byte[] empty = HostWorldBridgeSnapshotCodec.Encode(new HostWorldBridgeSnapshot());
        receive(guestPlayer, 1, 1, empty);
        receive(guestPlayer, 1, 2, empty);
        Require(CachedSnapshot(snapshots, 1).Bars.Count == 1,
            "Receiver ignores older and duplicate revisions after accepting the host bridge.", report);
        receive(guestPlayer, 1, 3, empty);
        Require(CachedSnapshot(snapshots, 1).Bars.Count == 0,
            "Receiver accepts the next committed replacement/deletion from the host connection.", report);
        receive(guestPlayer, 2, 1, packet);
        receive(guestPlayer, 0, 0, packet);
        Require(snapshots.Count == 2 && CachedSnapshot(snapshots, 2).Bars.Count == 1,
            "Receiver caches multiple locations independently and never renders request packets.", report);
    }

    private static HostWorldBridgeSnapshot CachedSnapshot(IDictionary snapshots, int id)
    {
        object location = snapshots[id];
        return (HostWorldBridgeSnapshot)location.GetType().GetField("Snapshot").GetValue(location);
    }
    private static bool SamePose(Transform left, Transform right) =>
        (left.position - right.position).sqrMagnitude < 0.000001f &&
        Quaternion.Angle(left.rotation, right.rotation) < 0.001f &&
        (left.lossyScale - right.lossyScale).sqrMagnitude < 0.000001f;
    private static void Require(bool value, string message, StringBuilder report)
    {
        if (!value) throw new InvalidOperationException(message);
        report.AppendLine("PASS: " + message);
    }
    private static void RequireRejected(byte[] packet, string message, StringBuilder report)
    {
        try { HostWorldBridgeSnapshotCodec.Decode(packet); }
        catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException)
        { report.AppendLine("PASS: " + message); return; }
        throw new InvalidOperationException(message);
    }
}
