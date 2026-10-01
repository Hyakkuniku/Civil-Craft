using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Isolated geometry, collision and guest travel checks. Never loads a saved game or changes an authored scene.</summary>
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
            ValidateGuestTravelPolicy(site, report);
            ValidatePlayerMapMarkers(fixture.transform, report);

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

    private static void ValidatePlayerMapMarkers(Transform fixture, StringBuilder report)
    {
        MethodInfo normalize = typeof(FusionMultiplayerAvatar).GetMethod("NormalizeMapPlayerName",
            BindingFlags.Static | BindingFlags.NonPublic);
        Func<string, string> name = value => (string)normalize.Invoke(null, new object[] { value });
        Require(name("  Hyakk  ") == "Hyakk" && name(null) == "Player" && name("\r\n\t") == "Player",
            "IGN synchronization trims names and safely handles missing/control-only names.", report);
        Require(name(new string('A', 40)).Length == 32 &&
            name(new string('A', 31) + char.ConvertFromUtf32(0x1F600)).Length == 31,
            "IGN capacity is bounded without splitting a UTF-16 surrogate pair.", report);

        var map = Child("Player Map Validation", fixture).AddComponent<ExpandedMinimapController>();
        var camera = Child("Player Map Camera", fixture).AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 10f;
        camera.aspect = 2f;
        camera.transform.SetPositionAndRotation(new Vector3(0, 50, 0), Quaternion.Euler(90, 0, 0));
        var layerObject = new GameObject("Map Marker Layer", typeof(RectTransform));
        layerObject.transform.SetParent(fixture, false);
        RectTransform layer = layerObject.GetComponent<RectTransform>();
        layer.sizeDelta = new Vector2(800, 400);
        Type mapType = typeof(ExpandedMinimapController);
        mapType.GetField("minimapCamera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, camera);
        mapType.GetField("markerLayer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, layer);
        mapType.GetField("uiFont", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, TMP_Settings.defaultFontAsset);
        MethodInfo factory = mapType.GetMethod("CreatePlayerMapMarker", BindingFlags.Instance | BindingFlags.NonPublic);
        Color remoteColor = new Color(0.72f, 0.38f, 0.95f, 1f);
        object[] arguments = { "ValidationRemotePlayer", "", remoteColor, null, null };
        var root = (RectTransform)factory.Invoke(map, arguments);
        var arrow = (RectTransform)arguments[3];
        var label = (TMP_Text)arguments[4];
        MethodInfo updateLabel = mapType.GetMethod("UpdateRemotePlayerMapLabel", BindingFlags.Static | BindingFlags.NonPublic);
        updateLabel.Invoke(null, new object[] { label, "Hyakk" });
        Require(label.text == "Hyakk" && !label.richText && label.font == TMP_Settings.defaultFontAsset &&
            root.GetComponentInChildren<PlayerMapArrowGraphic>(true).color != remoteColor &&
            arrow.Find("Fill").GetComponent<PlayerMapArrowGraphic>().color == remoteColor,
            "Remote marker uses the IGN, existing map font/arrow style and a distinct color.", report);
        updateLabel.Invoke(null, new object[] { label, "<b>Literal Player Name</b>" });
        Require(label.text == "<b>Literal Player Name</b>" && !label.richText && !label.enableWordWrapping &&
            ((RectTransform)label.rectTransform.parent).sizeDelta.x <= 250f,
            "IGN is plain text (not TMP markup) and the nameplate width stays bounded.", report);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            Require(!graphic.raycastTarget, "Player marker graphic does not intercept map dragging: " + graphic.name, report);

        MethodInfo project = mapType.GetMethod("ProjectPlayerMarker", BindingFlags.Instance | BindingFlags.NonPublic);
        project.Invoke(map, new object[] { root, arrow, layer.rect, new Vector3(5, 0, 0), Vector3.forward });
        Require(root.gameObject.activeSelf && Vector2.Distance(root.anchoredPosition, new Vector2(100, 0)) < 0.01f &&
            Quaternion.Angle(arrow.localRotation, Quaternion.identity) < 0.01f,
            "Native camera projection places the remote player at its world position and facing direction.", report);
        project.Invoke(map, new object[] { root, arrow, layer.rect, new Vector3(5, 0, 0), Vector3.right });
        Require(Quaternion.Angle(arrow.localRotation, Quaternion.Euler(0, 0, -90)) < 0.01f,
            "Map arrow follows remote facing direction while the marker stays fixed at its position.", report);
        project.Invoke(map, new object[] { root, arrow, layer.rect, new Vector3(50, 0, 0), Vector3.forward });
        Require(!root.gameObject.activeSelf, "Remote marker outside the current map view is hidden instead of mislocated.", report);
        project.Invoke(map, new object[] { root, arrow, layer.rect, new Vector3(0, 60, 0), Vector3.forward });
        Require(!root.gameObject.activeSelf, "Remote marker behind the map camera is hidden.", report);

        var avatar = Child("Unspawned Map Avatar", fixture).AddComponent<FusionMultiplayerAvatar>();
        Require(!avatar.TryGetMapPose(out _, out _) && avatar.MapPlayerName == "Player",
            "Uninitialized/despawned avatars cannot produce a phantom map marker at the origin.", report);
        mapType.GetField("playerMarker", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, root);
        mapType.GetField("playerMarkerArrow", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, arrow);
        mapType.GetMethod("UpdatePlayerMarker", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(map, new object[] { layer.rect });
        Require(!root.gameObject.activeSelf, "Local player marker remains hidden on the compact map.", report);

        // Exercise peer cleanup without starting Photon or mutating the live runner.
        IDictionary peers = (IDictionary)mapType.GetField("remotePlayerMarkers", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(map);
        Type viewType = mapType.GetNestedType("RemotePlayerMarker", BindingFlags.NonPublic);
        object view = Activator.CreateInstance(viewType, true);
        viewType.GetField("Root").SetValue(view, root);
        viewType.GetField("Arrow").SetValue(view, arrow);
        viewType.GetField("Label").SetValue(view, label);
        peers.Add(PlayerRef.FromIndex(1), view);
        mapType.GetMethod("ClearRemotePlayerMarkers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, null);
        Require(peers.Count == 0 && root == null, "Closing/leaving a session removes remote player marker objects.", report);
    }

    private static void ValidateGuestTravelPolicy(BuildLocation location, StringBuilder report)
    {
        MethodInfo policy = typeof(ExpandedMinimapController).GetMethod("CanFastTravelToLocation",
            BindingFlags.Static | BindingFlags.NonPublic);
        var committed = new List<Bar>(location.bakedBars);
        ContractSO originalContract = location.activeContract;
        var contract = ScriptableObject.CreateInstance<ContractSO>();
        contract.name = "Validation Guest Contract";
        contract.contractID = "VALIDATION_GUEST_TRAVEL";
        var otherContract = ScriptableObject.CreateInstance<ContractSO>();
        otherContract.name = "Validation Other Contract";
        otherContract.contractID = "VALIDATION_OTHER_TRAVEL";
        var data = Child("Isolated Guest Progress", location.transform.parent).AddComponent<PlayerDataManager>();
        var progress = new PlayerData();
        typeof(PlayerDataManager).GetProperty("CurrentData").SetValue(data, progress);
        data.allGameContracts = new List<ContractSO> { contract, otherContract };
        var contracts = new List<ContractSO> { contract };
        Func<bool> guestAllowed = () => (bool)policy.Invoke(null, new object[] { location, true, data, contracts });
        try
        {
            location.bakedBars.Clear();
            Require(!guestAllowed(), "New guest progress cannot fast travel to an unaccepted location.", report);
            location.activeContract = contract;
            location.bakedBars.AddRange(committed);
            Require(!guestAllowed(), "Scene/host contract and baked bridge do not grant guest travel access.", report);
            location.activeContract = null;
            location.bakedBars.Clear();
            Require(!(bool)policy.Invoke(null, new object[] { location, false, null, null }),
                "Host/single-player still navigates rather than teleporting to unfinished locations.", report);
            Require(!(bool)policy.Invoke(null, new object[] { null, true, data, contracts }) &&
                !(bool)policy.Invoke(null, new object[] { location, true, null, contracts }),
                "Guest travel rejects a missing destination.", report);

            progress.activeQuests.Add(new TrackedTask { contractName = contract.ContractID });
            Require(guestAllowed(), "Guest's own accepted unfinished contract unlocks its travel destination.", report);
            progress.activeQuests[0].contractName = contract.name;
            Require(guestAllowed(), "Legacy asset-name quest identifiers retain guest travel access.", report);
            progress.activeQuests.Clear();
            progress.completedContracts.Add(contract.ContractID);
            Require(guestAllowed(), "Guest's own completion record allows travel without loading a guest bridge.", report);
            progress.lockedContractIds.Add(contract.ContractID);
            Require(!guestAllowed(), "Guest's failure lock blocks travel even with an older completion record.", report);
            progress.lockedContractIds.Clear();
            progress.completedContracts.Clear();
            progress.completedContracts.Add(otherContract.ContractID);
            Require(!guestAllowed(), "Progress for another contract does not unlock this destination.", report);
            progress.completedContracts.Clear();
            var saved = new SavedBridgeData { contractId = contract.ContractID };
            saved.points.Add(new SavedPointData { index = 0, position = new SerializableVector3(Vector3.zero) });
            saved.points.Add(new SavedPointData { index = 1, position = new SerializableVector3(Vector3.right) });
            saved.bars.Add(new SavedBarData { startPointIndex = 0, endPointIndex = 1, materialName = "ValidationRoad" });
            progress.savedBridges.Add(saved);
            string before = JsonUtility.ToJson(progress);
            Require(guestAllowed(), "Guest's valid saved bridge grants travel before contract turn-in.", report);
            Require(before == JsonUtility.ToJson(progress), "Travel lookup does not alter guest save data.", report);
            saved.bars.Clear();
            Require(!guestAllowed(), "Invalid saved bridge does not grant travel access.", report);

            // Read inactive NPC metadata, including phases not currently selected.
            var npc = Child("Disabled Phase Catalog", location.transform.parent).AddComponent<NPCProgressionManager>();
            npc.enabled = false;
            var otherSite = Child("Other Guest Site", location.transform.parent).AddComponent<BuildLocation>();
            typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(npc, new List<NPCProgressionPhase> {
                    new NPCProgressionPhase { contract = otherContract, targetBuildLocation = otherSite },
                    new NPCProgressionPhase { contract = contract, targetBuildLocation = location }
                });
            var map = Child("Isolated Guest Map", location.transform.parent).AddComponent<ExpandedMinimapController>();
            int initialPhaseIndex = npc.CurrentPhaseIndex;
            MethodInfo catalog = typeof(ExpandedMinimapController).GetMethod("GetGuestLocationContracts",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var mapped = (IReadOnlyList<ContractSO>)catalog.Invoke(map, new object[] { location });
            Require(mapped != null && mapped.Count == 1 && mapped[0] == contract && !npc.enabled && npc.CurrentPhaseIndex == initialPhaseIndex,
                "Inactive NPC's later phase maps to the correct site without activating or advancing it.", report);
            progress.activeQuests.Add(new TrackedTask { contractName = contract.ContractID });
            Require((bool)policy.Invoke(null, new object[] { location, true, data, mapped }),
                "Guest travel works when the scene site's activeContract is null.", report);
            var mappedOther = (IReadOnlyList<ContractSO>)catalog.Invoke(map, new object[] { otherSite });
            Require(!(bool)policy.Invoke(null, new object[] { otherSite, true, data, mappedOther }),
                "Cached scene catalog keeps permissions separate between build locations.", report);

            location.bakedBars.AddRange(committed);
            Require((bool)policy.Invoke(null, new object[] { location, false, null, null }),
                "Host/single-player keeps fast travel to completed bridges.", report);
        }
        finally
        {
            location.bakedBars.Clear();
            location.bakedBars.AddRange(committed);
            location.activeContract = originalContract;
            Object.DestroyImmediate(contract);
            Object.DestroyImmediate(otherContract);
        }
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
