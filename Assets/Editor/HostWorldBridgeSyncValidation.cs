using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
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
            ValidateWaveEmote(fixture.transform, report);
            ValidateChallengeLobby(fixture.transform, report);
            ValidateChallengeReadiness(report);
            ValidateChallengeBuildPolicy(report);
            ValidateChallengeWorkspace(fixture.transform, report);
            ValidateChallengeLanding(fixture.transform, report);

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

    private static void ValidateWaveEmote(Transform fixture, StringBuilder report)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Player/PlayerAnimator.controller");
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Elements/Characters/Waving.anim");
        Require(controller != null && clip != null && clip.length > 0f,
            "Wave emote uses the user's existing PlayerAnimator and Waving animation clip.", report);
        bool waveParameter = false;
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            if (parameter.name == "Wave" && parameter.type == AnimatorControllerParameterType.Bool) waveParameter = true;
        Require(waveParameter && AnimationUtility.GetAnimationClipSettings(clip).loopTime,
            "Wave has an authored toggle parameter and the existing clip loops.", report);
        AnimatorState wave = null;
        AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState state in baseLayer.states) if (state.state.name == "Waving") wave = state.state;
        Require(wave != null && wave.motion == clip, "Waving state references the exact existing animation asset.", report);
        bool entry = false, exit = false, walk = false, sprint = false, jump = false;
        foreach (AnimatorStateTransition transition in baseLayer.anyStateTransitions)
            if (transition.destinationState == wave && !transition.canTransitionToSelf)
                foreach (AnimatorCondition condition in transition.conditions)
                    if (condition.parameter == "Wave" && condition.mode == AnimatorConditionMode.If) entry = true;
        foreach (AnimatorStateTransition transition in wave.transitions)
        {
            if (transition.destinationState == null) continue;
            string destination = transition.destinationState.name;
            if (destination == "idle" && !transition.hasExitTime)
                foreach (AnimatorCondition condition in transition.conditions)
                    if (condition.parameter == "Wave" && condition.mode == AnimatorConditionMode.IfNot) exit = true;
            if (destination == "walk" && !transition.hasExitTime) walk = true;
            if (destination == "Sprint" && !transition.hasExitTime) sprint = true;
            if (destination == "jump" && !transition.hasExitTime) jump = true;
        }
        Require(entry && exit && walk && sprint && jump,
            "Wave loops until toggled off and can be interrupted by walk, sprint or jump.", report);

        // Read only the authored scene YAML: no loading/saving the user's large
        // Canyon scene or replacing their currently open/unsaved Editor scene.
        string sceneText = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        int start = sceneText.IndexOf("--- !u!114 &397172065", StringComparison.Ordinal);
        int end = sceneText.IndexOf("--- !u!", start + 1, StringComparison.Ordinal);
        string button = sceneText.Substring(start, end - start);
        Require(button.Contains("m_TargetAssemblyTypeName: MultiplayerEmoteButton, Assembly-CSharp") &&
                button.Contains("m_MethodName: PlayWave") && button.Contains("m_Target: {fileID: 900000000000000101}") &&
                sceneText.Contains("guid: c748dcf87e824b5ab5807031b2f36541") &&
                sceneText.Contains("button: {fileID: 397172065}"),
            "Existing EmoteButton has a persistent authored On Click target, script and Button reference.", report);
        var ui = Child("Inactive Wave Button", fixture).AddComponent<MultiplayerEmoteButton>();
        // An inactive preview fixture does not run Awake; match the authored
        // scene's explicit Button assignment instead of relying on that callback.
        var uiSerialized = new SerializedObject(ui);
        uiSerialized.FindProperty("button").objectReferenceValue = ui.GetComponent<Button>();
        uiSerialized.ApplyModifiedPropertiesWithoutUndo();
        ui.PlayWave();
        Require(!ui.GetComponent<Button>().interactable,
            "Wave button safely rejects clicks without a running multiplayer owner avatar.", report);

        var rig = new GameObject("Native Wave Animator (Temporary)");
        SceneManager.MoveGameObjectToScene(rig, fixture.gameObject.scene);
        rig.SetActive(false);
        Animator animator = rig.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        try
        {
            rig.SetActive(true);
            animator.Rebind();
            animator.Update(0f);
            animator.SetBool("IsGrounded", true);
            animator.SetFloat("Speed", 0f);
            animator.SetBool("Wave", true);
            for (int i = 0; i < 4; i++) animator.Update(0.05f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Waving"),
                "Native Animator enters Waving after pressing the emote toggle.", report);
            for (int i = 0; i < Mathf.CeilToInt((clip.length * 4f + 0.3f) / 0.05f); i++) animator.Update(0.05f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Waving") &&
                animator.GetCurrentAnimatorStateInfo(0).normalizedTime > 3f,
                "Native wave remains active across multiple complete animation loops.", report);
            animator.SetBool("Wave", false);
            for (int i = 0; i < 4; i++) animator.Update(0.05f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("idle") && rig.transform.position == Vector3.zero &&
                !animator.applyRootMotion,
                "Toggling wave off returns to idle without moving the gameplay root.", report);
            animator.SetBool("Wave", true);
            for (int i = 0; i < 4; i++) animator.Update(0.05f);
            animator.SetFloat("Speed", 0.5f);
            for (int i = 0; i < 4; i++) animator.Update(0.05f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("walk"),
                "Native walking immediately interrupts the wave instead of locking movement.", report);
            animator.SetBool("Wave", false);
            animator.SetFloat("Speed", 0f);
            for (int i = 0; i < 12; i++) animator.Update(0.05f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("idle"),
                "Clearing the movement-interrupted wave does not restart it when walking stops.", report);
        }
        finally { Object.DestroyImmediate(rig); }
    }

    private static void ValidateChallengeLobby(Transform fixture, StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1), outsider = PlayerRef.FromIndex(2);
        var state = new MultiplayerChallengeState { Revision = 7, Guest = guest, Phase = MultiplayerChallengePhase.Invited };
        Require(MultiplayerChallengeRules.CanRespond(state, guest, 7) &&
            !MultiplayerChallengeRules.CanRespond(state, host, 7) &&
            !MultiplayerChallengeRules.CanRespond(state, outsider, 7) &&
            !MultiplayerChallengeRules.CanRespond(state, guest, 6),
            "Challenge accepts only the invited guest's current invitation response.", report);
        state.Phase = MultiplayerChallengePhase.Teleporting;
        Require(!MultiplayerChallengeRules.CanRespond(state, guest, 7) &&
            MultiplayerChallengeRules.CanArrive(state, host, host, 7) &&
            MultiplayerChallengeRules.CanArrive(state, guest, host, 7) &&
            !MultiplayerChallengeRules.CanArrive(state, outsider, host, 7) &&
            !MultiplayerChallengeRules.CanArrive(state, guest, host, 6),
            "Challenge arrival rejects stale revisions and non-participants.", report);
        state.Phase = MultiplayerChallengePhase.Lobby;
        Require(!MultiplayerChallengeRules.CanArrive(state, guest, host, 7),
            "Late arrival messages cannot restart an already-open lobby.", report);

        BuildLocation selected = Child("Selected Challenge Site", fixture).AddComponent<BuildLocation>();
        BuildLocation other = Child("Other Challenge Site", fixture).AddComponent<BuildLocation>();
        GameObject selectedRoot = Child("Selected BuildRoot", selected.transform);
        GameObject otherRoot = Child("Other BuildRoot", other.transform);
        selected.buildSiteVisualRoots.Add(selectedRoot);
        other.buildSiteVisualRoots.Add(otherRoot);
        selectedRoot.SetActive(false);
        otherRoot.SetActive(true);
        int selectedBars = selected.bakedBars.Count, otherBars = other.bakedBars.Count;
        using (var visibility = new ChallengeSiteIsolation())
        {
            visibility.ShowOnly(selected, new[] { selected, other });
            Require(selectedRoot.activeSelf && !otherRoot.activeSelf && selected.gameObject.activeSelf && other.gameObject.activeSelf,
                "Challenge lobby shows only the chosen build root without disabling the site controllers.", report);
            Require(selected.bakedBars.Count == selectedBars && other.bakedBars.Count == otherBars &&
                !selected.IsRedesigningBridge && !other.IsRedesigningBridge,
                "Lobby site isolation never enters redesign or changes bridge collections.", report);
        }
        Require(!selectedRoot.activeSelf && otherRoot.activeSelf,
            "Leaving the challenge restores exact original build-root visibility.", report);
        string key = MultiplayerChallengeRules.SiteKey(selected);
        Require(!string.IsNullOrEmpty(key) && key != MultiplayerChallengeRules.SiteKey(other),
            "Challenge site identifiers distinguish authored locations without runtime instance IDs.", report);
        Require(!MultiplayerChallengeLobbyUI.CanOfferChallenge(selected) && !MultiplayerChallengeLobbyUI.IsChallengeActive,
            "Single-player cannot send multiplayer challenges or enter the challenge lobby.", report);

        string scene = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.Contains("m_Name: Challenge_UI") && scene.Contains("m_Name: Lobby_UI_Panel") &&
            scene.Contains("guid: 64996c73e45b4b9cba62a18d5cb74b47") && scene.Contains("m_Name: CustomizedPlayerPortraits"),
            "Challenge and lobby controls are authored in the existing Canyon scene, including the character portrait display.", report);
        foreach (string method in new[] { "SendChallenge", "AcceptChallenge", "DeclineChallenge", "CancelOrClose", "ToggleReady" })
            Require(scene.Contains("m_MethodName: " + method), "Authored challenge button calls " + method + ".", report);
        int readyStart = scene.IndexOf("--- !u!114 &900000000000010067", StringComparison.Ordinal);
        int readyEnd = scene.IndexOf("--- !u!", readyStart + 1, StringComparison.Ordinal);
        string ready = scene.Substring(readyStart, readyEnd - readyStart);
        Require(ready.Contains("m_Interactable: 0") && ready.Contains("m_MethodName: ToggleReady") &&
            ready.Contains("m_Target: {fileID: 900000000000000105}") &&
            scene.Contains("readyButtonLabel: {fileID: 900000000000010071}"),
            "Authored Ready button targets the lobby service, has its authored label, and starts disabled outside a challenge.", report);
    }

    private static void ValidateChallengeReadiness(StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1), outsider = PlayerRef.FromIndex(2);
        int checks = 0;
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        foreach (PlayerRef player in new[] { host, guest, outsider })
        foreach (int revision in new[] { 6, 7, 8 })
        foreach (bool requested in new[] { false, true })
        for (int arrived = 0; arrived < 4; arrived++)
        for (int ready = 0; ready < 4; ready++)
        {
            var state = new MultiplayerChallengeState
            {
                Revision = 7, Guest = guest, Phase = phase,
                HostArrived = (arrived & 1) != 0, GuestArrived = (arrived & 2) != 0,
                HostReady = (ready & 1) != 0, GuestReady = (ready & 2) != 0
            };
            bool allowed = revision == 7 && arrived == 3 && player != outsider &&
                (phase == MultiplayerChallengePhase.Lobby || (phase == MultiplayerChallengePhase.Countdown && !requested));
            bool current = player == host ? (ready & 1) != 0 : (ready & 2) != 0;
            bool changed = allowed && current != requested;
            if (MultiplayerChallengeRules.CanSetReady(state, player, host, revision, requested) != allowed ||
                MultiplayerChallengeRules.TrySetReady(ref state, player, host, revision, requested) != changed)
                throw new InvalidOperationException($"Ready gate failed: {phase}/{player}/rev={revision}/arrived={arrived}/ready={ready}/request={requested}");
            bool expectedHost = changed && player == host ? requested : (ready & 1) != 0;
            bool expectedGuest = changed && player == guest ? requested : (ready & 2) != 0;
            MultiplayerChallengePhase expectedPhase = changed
                ? (expectedHost && expectedGuest ? MultiplayerChallengePhase.Countdown : MultiplayerChallengePhase.Lobby) : phase;
            if ((bool)state.HostReady != expectedHost || (bool)state.GuestReady != expectedGuest || state.Phase != expectedPhase ||
                state.Revision != 7 || state.Guest != guest || (bool)state.HostArrived != ((arrived & 1) != 0) ||
                (bool)state.GuestArrived != ((arrived & 2) != 0))
                throw new InvalidOperationException("Ready request changed the other player, arrival, revision or wrong phase.");
            checks += 2;
        }
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        foreach (bool expired in new[] { false, true })
        for (int arrived = 0; arrived < 4; arrived++)
        for (int ready = 0; ready < 4; ready++)
        {
            var state = new MultiplayerChallengeState
            {
                Revision = 7, Guest = guest, Phase = phase,
                HostArrived = (arrived & 1) != 0, GuestArrived = (arrived & 2) != 0,
                HostReady = (ready & 1) != 0, GuestReady = (ready & 2) != 0
            };
            bool finished = phase == MultiplayerChallengePhase.Countdown && expired && arrived == 3 && ready == 3;
            if (MultiplayerChallengeRules.TryFinishCountdown(ref state, expired) != finished ||
                state.Phase != (finished ? MultiplayerChallengePhase.ReadyToBuild : phase))
                throw new InvalidOperationException("Countdown advanced before deadline/both arrivals/both readiness, or restarted a finished challenge.");
            checks += 2;
        }
        Require(checks == Enum.GetValues(typeof(MultiplayerChallengePhase)).Length * 640,
            $"{checks:N0} readiness/phase assertions pass across both owners, outsiders, revisions, arrival and ready combinations.", report);
        var cycle = new MultiplayerChallengeState
        { Revision = 7, Guest = guest, Phase = MultiplayerChallengePhase.Lobby, HostArrived = true, GuestArrived = true };
        Require(MultiplayerChallengeRules.TrySetReady(ref cycle, guest, host, 7, true) &&
            cycle.Phase == MultiplayerChallengePhase.Lobby && !cycle.HostReady && cycle.GuestReady,
            "Guest Ready alone cannot start the countdown or ready the host.", report);
        Require(MultiplayerChallengeRules.TrySetReady(ref cycle, host, host, 7, true) &&
            cycle.Phase == MultiplayerChallengePhase.Countdown && !MultiplayerChallengeRules.TryFinishCountdown(ref cycle, false),
            "Both Ready enters a countdown, not an immediate build/start.", report);
        Require(!MultiplayerChallengeRules.TrySetReady(ref cycle, guest, host, 7, true),
            "Duplicate Ready messages do not restart or extend the countdown.", report);
        Require(MultiplayerChallengeRules.TrySetReady(ref cycle, guest, host, 7, false) &&
            cycle.Phase == MultiplayerChallengePhase.Lobby && cycle.HostReady && !cycle.GuestReady && !cycle.Deadline.IsRunning,
            "Unready cancels the countdown, preserves the other owner's readiness, and returns to waiting.", report);
        Require(MultiplayerChallengeRules.TrySetReady(ref cycle, guest, host, 7, true) &&
            MultiplayerChallengeRules.TryFinishCountdown(ref cycle, true) && cycle.Phase == MultiplayerChallengePhase.ReadyToBuild &&
            !cycle.Deadline.IsRunning && !MultiplayerChallengeRules.TryFinishCountdown(ref cycle, true) &&
            !MultiplayerChallengeRules.TrySetReady(ref cycle, guest, host, 7, false),
            "A fresh countdown finishes once; delayed Unready cannot reopen the completed lobby.", report);
    }

    private static void ValidateChallengeBuildPolicy(StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1), outsider = PlayerRef.FromIndex(2);
        int checks = 0;
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        foreach (PlayerRef player in new[] { host, guest, outsider })
        foreach (int revision in new[] { 6, 7, 8 })
        for (int arrived = 0; arrived < 4; arrived++)
        for (int ready = 0; ready < 4; ready++)
        for (int prepared = 0; prepared < 4; prepared++)
        {
            var state = new MultiplayerChallengeState
            {
                Revision = 7, Guest = guest, Phase = phase,
                HostArrived = (arrived & 1) != 0, GuestArrived = (arrived & 2) != 0,
                HostReady = (ready & 1) != 0, GuestReady = (ready & 2) != 0,
                HostBuilding = (prepared & 1) != 0, GuestBuilding = (prepared & 2) != 0
            };
            bool allowed = phase == MultiplayerChallengePhase.ReadyToBuild && player != outsider &&
                revision == 7 && arrived == 3 && ready == 3;
            bool wasPrepared = player == host ? (prepared & 1) != 0 : (prepared & 2) != 0;
            bool changed = allowed && !wasPrepared;
            if (MultiplayerChallengeRules.CanPrepareBuild(state, player, host, revision) != allowed ||
                MultiplayerChallengeRules.TryPrepareBuild(ref state, player, host, revision) != changed)
                throw new InvalidOperationException("Build acknowledgement accepted a stale, unready or unauthorized participant.");
            bool hostPrepared = changed && player == host || (prepared & 1) != 0;
            bool guestPrepared = changed && player == guest || (prepared & 2) != 0;
            if ((bool)state.HostBuilding != hostPrepared || (bool)state.GuestBuilding != guestPrepared ||
                state.Phase != (changed && hostPrepared && guestPrepared ? MultiplayerChallengePhase.Building : phase))
                throw new InvalidOperationException("Build started before both independent workspaces were acknowledged.");
            checks += 2;
        }
        Require(checks == Enum.GetValues(typeof(MultiplayerChallengePhase)).Length * 1152,
            $"{checks:N0} preparation/owner assertions pass; both build acknowledgements are required before editing.", report);
    }

    private static void ValidateChallengeWorkspace(Transform fixture, StringBuilder report)
    {
        ContractSO definition = ScriptableObject.CreateInstance<ContractSO>();
        definition.name = "Workspace Validation Contract";
        definition.contractID = "VALIDATION_REAL_WORLD";
        definition.budget = 1234;
        definition.isTutorialContract = definition.isTimeAttack = definition.autoCollectReward = true;
        definition.hiddenTools.Add(BuildModeTool.ExitBuildMode);
        BuildLocation source = Child("Workspace Source Site", fixture).AddComponent<BuildLocation>();
        source.activeContract = definition;
        Point start = Child("World Start", fixture).AddComponent<Point>();
        Point end = Child("World End", fixture).AddComponent<Point>();
        start.transform.position = new Vector3(20, 3, -8);
        end.transform.position = new Vector3(35, 3, -8);
        start.isAnchor = end.isAnchor = true;
        start.AssignOwner(source); end.AssignOwner(source);
        source.startingAnchors.Add(start); source.endingAnchors.Add(end);
        Bar saved = Child("World Saved Bar", fixture).AddComponent<Bar>();
        saved.enabled = false;
        saved.startPoint = start; saved.endPoint = end; saved.AssignOwner(source);
        start.ConnectedBars.Add(saved); end.ConnectedBars.Add(saved);
        source.bakedBars.Add(saved); source.bakedPoints.Add(start); source.bakedPoints.Add(end);
        Vector3 savedStart = start.transform.position, savedEnd = end.transform.position;
        BarCreator creator = Child("Workspace Creator", fixture).AddComponent<BarCreator>();
        creator.pointToInstantiate = Child("Workspace Node Prefab", fixture);
        creator.pointToInstantiate.AddComponent<Point>();
        creator.barToInstantiate = Child("Workspace Bar Prefab", fixture);
        creator.barToInstantiate.AddComponent<Bar>();
        Transform previousPoints = creator.pointParent = Child("World Nodes Parent", fixture).transform;
        Transform previousBars = creator.barParent = Child("World Bars Parent", fixture).transform;
        var commands = Child("Workspace Undo Manager", fixture).AddComponent<CommandManager>();
        FieldInfo singleton = typeof(CommandManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        CommandManager previousCommands = CommandManager.Instance;
        singleton.SetValue(null, commands);
        FieldInfo undoField = typeof(CommandManager).GetField("undoStack", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo redoField = typeof(CommandManager).GetField("redoStack", BindingFlags.Instance | BindingFlags.NonPublic);
        object worldUndo = undoField.GetValue(commands), worldRedo = redoField.GetValue(commands);
        var clipboard = creator.gameObject.AddComponent<ClipboardManager>();
        typeof(ClipboardManager).GetField("barCreator", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(clipboard, creator);
        FieldInfo clipboardSingleton = typeof(ClipboardManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        ClipboardManager previousClipboard = ClipboardManager.Instance;
        clipboardSingleton.SetValue(null, clipboard);
        FieldInfo copiedPointsField = typeof(ClipboardManager).GetField("copiedRelativePoints", BindingFlags.Instance | BindingFlags.NonPublic);
        object worldClipboard = copiedPointsField.GetValue(clipboard);
        ((List<Vector3>)worldClipboard).Add(Vector3.right * 5);
        var remembered = new HistoryAction { isBuildEvent = true };
        remembered.affectedObjects.Add(saved.gameObject);
        ((Stack<HistoryAction>)worldRedo).Push(remembered);
        ChallengeBuildWorkspace workspace = null;
        try
        {
            workspace = ChallengeBuildWorkspace.Create(source, definition, creator, 7, 2);
            Require(workspace.Location != source && workspace.Location.IsSessionChallengeLocation &&
                workspace.Location.SessionChallengeRevision == 7 && workspace.Location.bakedBars.Count == 0 &&
                workspace.Location.bakedPoints.Count == 0 && workspace.Root.GetComponentsInChildren<Bar>(true).Length == 0,
                "Competition starts empty in a dedicated runtime location, not the world's baked lists.", report);
            Point ownStart = workspace.Location.startingAnchors[0], ownEnd = workspace.Location.endingAnchors[0];
            Require(ownStart != start && ownEnd != end && ownStart.transform.position == savedStart && ownEnd.transform.position == savedEnd &&
                ownStart.OwnerLocation == workspace.Location && ownEnd.OwnerLocation == workspace.Location &&
                ownStart.ConnectedBars.Count == 0 && ownEnd.ConnectedBars.Count == 0 &&
                start.ConnectedBars.Contains(saved) && end.ConnectedBars.Contains(saved),
                "Copied anchors match the challenge span but share no graph links or ownership with the saved bridge.", report);
            Require(!saved.gameObject.activeSelf && !start.gameObject.activeSelf && !end.gameObject.activeSelf &&
                creator.pointParent.IsChildOf(workspace.Root.transform) && creator.barParent.IsChildOf(workspace.Root.transform) && !creator.enabled,
                "Saved geometry is hidden and builder parents are private; input is gated pending both acknowledgements." +
                $" (bar={saved.gameObject.activeSelf}, start={start.gameObject.activeSelf}, end={end.gameObject.activeSelf}, " +
                $"pointsPrivate={creator.pointParent.IsChildOf(workspace.Root.transform)}, barsPrivate={creator.barParent.IsChildOf(workspace.Root.transform)}, creatorEnabled={creator.enabled})", report);
            ContractSO session = workspace.Location.activeContract;
            Require(session != definition && session.budget == definition.budget && !session.isTutorialContract &&
                !session.isTimeAttack && !session.autoCollectReward && !session.countsTowardMapAchievements &&
                session.ContractID.StartsWith(ChallengeBuildWorkspace.ContractPrefix) &&
                session.IsToolHidden(BuildModeTool.Simulate) && !session.IsToolHidden(BuildModeTool.Paste) &&
                !session.IsToolHidden(BuildModeTool.ExitBuildMode) && definition.isTutorialContract && definition.isTimeAttack,
                "Runtime contract keeps the same budget, disables testing/rewards/tutorials, and never changes the story asset.", report);
            Require(!ReferenceEquals(undoField.GetValue(commands), worldUndo) &&
                !ReferenceEquals(redoField.GetValue(commands), worldRedo) && ((Stack<HistoryAction>)worldRedo).Count == 1,
                "Competition undo/redo stacks are separate; remembered world redo pieces are not destroyed.", report);
            Require(!ReferenceEquals(copiedPointsField.GetValue(clipboard), worldClipboard) &&
                ((List<Vector3>)copiedPointsField.GetValue(clipboard)).Count == 0 && ((List<Vector3>)worldClipboard).Count == 1,
                "Competition clipboard starts empty; story clipboard data is retained separately.", report);
            Point edited = Child("Own Edited Node", creator.pointParent).AddComponent<Point>();
            edited.AssignOwner(workspace.Location, true);
            edited.transform.position = savedStart + Vector3.right * 3;
            edited.gameObject.SetActive(false); // Delete/Undo in the private graph.
            Require(start.transform.position == savedStart && end.transform.position == savedEnd &&
                source.bakedBars.Count == 1 && source.bakedBars[0] == saved && source.bakedPoints.Count == 2,
                "Editing/deleting a private node cannot move or replace the host's bridge objects or collections.", report);
            var data = Child("Workspace Save Guard", fixture).AddComponent<PlayerDataManager>();
            var physics = Child("Workspace Bake Guard", fixture).AddComponent<BridgePhysicsManager>();
            Require(!workspace.Location.LoadSavedBridge() && !physics.BakeBridge(session) &&
                !data.SaveBridgeData(session.ContractID, new List<Point> { ownStart, ownEnd }, new List<Bar>(), 0, 0),
                "Session contracts cannot load, bake or persist a bridge even through direct public calls.", report);
            workspace.Dispose();
            Require(saved != null && saved.gameObject.activeSelf && start.gameObject.activeSelf && end.gameObject.activeSelf &&
                creator.pointParent == previousPoints && creator.barParent == previousBars && creator.enabled &&
                ReferenceEquals(undoField.GetValue(commands), worldUndo) && ReferenceEquals(redoField.GetValue(commands), worldRedo) &&
                ReferenceEquals(copiedPointsField.GetValue(clipboard), worldClipboard) && ((List<Vector3>)worldClipboard).Count == 1 &&
                ((Stack<HistoryAction>)worldRedo).Count == 1 && start.ConnectedBars.Contains(saved) && end.ConnectedBars.Contains(saved),
                "Disposing restores original visibility, construction parents, graph links and exact world undo/redo stacks.", report);
        }
        finally
        {
            workspace?.Dispose();
            singleton.SetValue(null, previousCommands);
            clipboardSingleton.SetValue(null, previousClipboard);
            Object.DestroyImmediate(definition);
        }
    }

    private static void ValidateChallengeLanding(Transform fixture, StringBuilder report)
    {
        var rig = new GameObject("Challenge Landing Physics (Temporary)");
        SceneManager.MoveGameObjectToScene(rig, fixture.gameObject.scene);
        rig.transform.position = new Vector3(1000, 0, 1000);
        try
        {
            // Gameplay scripts remain under the inactive fixture; only the
            // temporary colliders and travel anchor are active for native physics.
            BuildLocation site = Child("Landing Site", fixture).AddComponent<BuildLocation>();
            site.fastTravelTarget = Child("Fast Travel Target", rig.transform).transform;
            site.fastTravelTarget.localPosition = Vector3.up;
            var controller = Child("Landing Controller", rig.transform).AddComponent<CharacterController>();
            controller.height = 2.93f;
            controller.radius = 0.78f;
            controller.center = new Vector3(0, 0.65f, 0);
            controller.transform.localScale = Vector3.one * 0.6f;
            controller.transform.position = site.fastTravelTarget.position;
            var floor = Child("Default Layer Ground", rig.transform).AddComponent<BoxCollider>();
            floor.size = new Vector3(12, 0.5f, 12);
            floor.center = new Vector3(0, -0.25f, 0);
            Physics.SyncTransforms();
            Require(MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, false, out Vector3 host),
                "Challenge lands at the authored fast-travel anchor on Default-layer ground without any NavMesh.", report);
            float feet = host.y + (controller.center.y - controller.height * 0.5f) * 0.6f;
            Require(Mathf.Abs(feet - 0.05f) < 0.001f &&
                Mathf.Abs(host.x - site.fastTravelTarget.position.x) < 0.001f &&
                Mathf.Abs(host.z - site.fastTravelTarget.position.z) < 0.001f,
                "Landing preserves the target's horizontal position and accounts for scaled controller centre/feet.", report);
            Require(MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, true, out Vector3 guest) &&
                Vector3.Distance(host, guest) >= 1.99f && MultiplayerChallengeLobbyUI.IsAtSite(site, guest),
                "Guest has a nearby distinct supported landing within the host's arrival-validation range.", report);
            var wall = Child("Blocked Landing", rig.transform).AddComponent<BoxCollider>();
            wall.size = new Vector3(12, 4, 12);
            wall.center = new Vector3(0, 2, 0);
            Require(!MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, false, out _) &&
                !MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, true, out _),
                "Landing still rejects obstructed body/headroom for both participants.", report);
            wall.enabled = false;
            floor.enabled = false;
            Require(!MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, false, out _),
                "An unsupported fast-travel target cannot land over empty air.", report);
            floor.enabled = true;
            floor.isTrigger = true;
            Require(!MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, false, out _),
                "A trigger is not accepted as solid landing support.", report);
            floor.isTrigger = false;
            site.navigationTarget = site.fastTravelTarget.gameObject;
            site.fastTravelTarget = null;
            Require(MultiplayerChallengeLobbyUI.TryResolveLanding(site, controller, false, out _),
                "Locations without a fast-travel anchor retain the navigation-target fallback.", report);
        }
        finally { Object.DestroyImmediate(rig); }
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
