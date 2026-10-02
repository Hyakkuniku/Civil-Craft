using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
    private static double nextRequestedCheck;
    private delegate void ReceiveBridgePacket(PlayerRef connectionPlayer, int id, int revision, ReadOnlySpan<byte> data);

    [InitializeOnLoadMethod]
    private static void RunRequestedCheck()
    {
        EditorApplication.delayCall += CheckRequestedValidation;
        EditorApplication.update -= CheckRequestedValidation;
        EditorApplication.update += CheckRequestedValidation;
    }

    private static void CheckRequestedValidation()
    {
        if (EditorApplication.timeSinceStartup < nextRequestedCheck) return;
        nextRequestedCheck = EditorApplication.timeSinceStartup + 2;
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(RequestPath))
        {
            File.Delete(RequestPath);
            Validate();
        }
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
            ChallengeUIValidation.Run(fixture.transform, report);
            WorldMultiplayerPanelValidation.Run(fixture.transform, report);
            SessionChatValidation.Run(fixture.transform, report);
            MultiplayerHUDVisibilityValidation.Run(fixture.transform, report);
            ChallengeSubmissionValidation.Run(fixture.transform, report);
            BridgeConstructionValidation.Run(fixture.transform, report);
            ChallengeTestValidation.Run(fixture.transform, report);
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
        // Keep visual roots separate from the inactive manager fixture, just as
        // authored environment roots can be separate from a workbench controller.
        var environment = new GameObject("Isolation Environment (Temporary)");
        SceneManager.MoveGameObjectToScene(environment, fixture.gameObject.scene);
        GameObject selectedRoot = Child("Selected BuildRoot", environment.transform);
        GameObject otherRoot = Child("Other BuildRoot", environment.transform);
        GameObject sharedParent = Child("Inactive Shared Container", environment.transform);
        GameObject sharedRoot = Child("Shared Site Visual", sharedParent.transform);
        sharedParent.SetActive(false);
        selected.buildSiteVisualRoots.Add(selectedRoot);
        selected.buildSiteVisualRoots.Add(sharedRoot);
        other.buildSiteVisualRoots.Add(otherRoot);
        other.buildSiteVisualRoots.Add(sharedRoot);
        selectedRoot.SetActive(false);
        otherRoot.SetActive(true);
        int selectedBars = selected.bakedBars.Count, otherBars = other.bakedBars.Count;
        foreach (BuildLocation[] order in new[] { new[] { selected, other }, new[] { other, selected } })
        {
            using (var visibility = new ChallengeSiteIsolation())
            {
                visibility.ShowOnly(selected, order);
                Require(selectedRoot.activeInHierarchy && sharedRoot.activeInHierarchy && !otherRoot.activeSelf &&
                    selected.gameObject.activeSelf && other.gameObject.activeSelf && !fixture.gameObject.activeSelf,
                    "Chosen visuals, shared roots and inactive containers resolve independently of site iteration order; unrelated roots remain hidden.", report);
                Require(selected.bakedBars.Count == selectedBars && other.bakedBars.Count == otherBars &&
                    !selected.IsRedesigningBridge && !other.IsRedesigningBridge,
                    "Lobby site isolation never enters redesign or changes bridge collections.", report);
            }
            Require(!selectedRoot.activeSelf && otherRoot.activeSelf && !sharedParent.activeSelf && sharedRoot.activeSelf,
                "Leaving the challenge restores exact original root and ancestor visibility.", report);
        }
        ValidateChallengeDecorationVisibility(fixture, selected, selectedRoot, otherRoot, report);
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

    private static void ValidateChallengeDecorationVisibility(Transform fixture, BuildLocation selected,
        GameObject selectedRoot, GameObject otherRoot, StringBuilder report)
    {
        GameManager manager = Child("Decoration Policy Manager", fixture).AddComponent<GameManager>();
        Renderer selectedRenderer = selectedRoot.AddComponent<MeshRenderer>();
        Renderer otherRenderer = otherRoot.AddComponent<MeshRenderer>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GameManager).GetField("decorativeCanyonsToHide", flags).SetValue(manager,
            new List<GameObject> { selectedRoot, otherRoot });
        MethodInfo hide = typeof(GameManager).GetMethod("HideDecorativeCanyons", flags);
        MethodInfo restore = typeof(GameManager).GetMethod("RestoreDecorativeCanyons", flags);
        hide.Invoke(manager, new object[] { selected });
        Require(!selectedRenderer.forceRenderingOff && otherRenderer.forceRenderingOff,
            "Challenge build entry preserves selected site renderers while hiding unrelated decoration.", report);
        restore.Invoke(manager, null);
        Require(!selectedRenderer.forceRenderingOff && !otherRenderer.forceRenderingOff,
            "Challenge decoration visibility restores original renderer states.", report);
        hide.Invoke(manager, new object[] { null });
        Require(selectedRenderer.forceRenderingOff && otherRenderer.forceRenderingOff,
            "Single-player retains its existing decoration-hiding policy.", report);
        restore.Invoke(manager, null);
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
        GameObject siteVisual = Child("Authored Workspace Visual", fixture);
        source.buildSiteVisualRoots.Add(siteVisual);
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
            Require(workspace.Location.buildSiteVisualRoots.Contains(siteVisual) &&
                !ReferenceEquals(workspace.Location.buildSiteVisualRoots, source.buildSiteVisualRoots),
                "Session workspace retains authored visual roots in its own list without changing the source site.", report);
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
/// <summary>Read-only authored-scene checks and disposable UI previews, never saves a scene.</summary>
public static class ChallengeUIValidation
{
    private static double nextRequestCheck;
    [InitializeOnLoadMethod]
    private static void WatchRequestedPreview()
    {
        EditorApplication.update -= RunRequestedPreview;
        EditorApplication.update += RunRequestedPreview;
    }
    private static void RunRequestedPreview()
    {
        if (EditorApplication.timeSinceStartup < nextRequestCheck) return;
        nextRequestCheck = EditorApplication.timeSinceStartup + 2;
        const string request = "Temp/challenge-ui-validation.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || AuthoredResults() == null ||
            !Resources.FindObjectsOfTypeAll<GameObject>().Any(obj => obj.name == "ChallengeLeaveBuildConfirmation" &&
                obj.scene.name == "CanyonCrossing")) return;
        var lobby = Resources.FindObjectsOfTypeAll<MultiplayerChallengeLobbyUI>().FirstOrDefault(ui =>
            ui.gameObject.scene.name == "CanyonCrossing");
        if (lobby == null || typeof(MultiplayerChallengeLobbyUI).GetField("portraitLight", BindingFlags.Instance |
            BindingFlags.NonPublic).GetValue(lobby) == null) return; // Wait for the authored lighting reference to reload.
        var key = (Light)typeof(MultiplayerChallengeLobbyUI).GetField("portraitLight", BindingFlags.Instance |
            BindingFlags.NonPublic).GetValue(lobby);
        var camera = (Camera)typeof(MultiplayerChallengeLobbyUI).GetField("portraitCamera", BindingFlags.Instance |
            BindingFlags.NonPublic).GetValue(lobby);
        if (camera == null || Vector3.Dot(key.transform.forward, camera.transform.forward) < 0.8f) return;
        File.Delete(request);
        HostWorldBridgeSyncValidation.Validate();
    }
    private static GameObject AuthoredResults() => Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(obj =>
        obj.name == "ChallengeResultsPanel" && obj.scene.IsValid() && obj.scene.name == "CanyonCrossing");
    public static void Run(Transform fixture, StringBuilder report)
    {
        string scene = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        Func<string, string> block = id => Regex.Match(scene, @"(?ms)^--- !u!\d+ &" + id + @"\r?\n.*?(?=^--- !u!|\z)").Value;
        string service = block("900000000000000105");
        Check(service.Contains("portraitLight: {fileID: 900000000000010101}"), "Authored portrait key light is not wired.");
        Check(block("900000000000010101").Contains("m_Enabled: 0") &&
            block("900000000000010101").Contains("m_RenderingLayerMask: 128") &&
            block("900000000000010102").Contains("m_RenderingLayers: 128"),
            "Booth key light must be portrait-only and disabled outside its camera pass.");
        foreach (string preset in new[] { "Performant", "Balanced", "HighFidelity" })
            Check(File.ReadAllText("Assets/Settings/URP-" + preset + ".asset").Contains("m_SupportsLightLayers: 1"),
                preset + " does not support portrait light isolation.");
        foreach (Match light in Regex.Matches(scene, @"(?ms)^--- !u!108 &(?<id>\d+)\r?\n.*?(?=^--- !u!|\z)"))
            if (light.Groups["id"].Value != "900000000000010101")
                Check((uint.Parse(Regex.Match(light.Value, @"m_RenderingLayerMask: (\d+)").Groups[1].Value) & 128u) == 0,
                    "A world light includes the reserved Lobby Portrait rendering layer.");
        ValidatePortraitLightingScope(fixture, report);
        foreach (string field in new[] { "submissionStatusBody", "submissionStatusHeader", "submissionConfirmPanel", "confirmSubmitButton",
            "testIntroductionPanel", "testIntroductionTitle", "testIntroductionPlayer", "liveLoadLabel", "leaveBuildConfirmPanel",
            "resultsPanel", "resultsWinner", "resultsRules", "resultsHostName", "resultsGuestName", "resultsHostScore",
            "resultsGuestScore", "resultsHostDetails", "resultsGuestDetails" })
        {
            string id = Regex.Match(service, @"(?m)^  " + field + @": \{fileID: (\d+)\}").Groups[1].Value;
            Check(id.Length > 0 && block(id).Length > 0, "Missing authored presentation reference: " + field);
            if (field == "submissionConfirmPanel" || field == "resultsPanel" || field == "submissionStatusBody" ||
                field == "testIntroductionPanel" || field == "leaveBuildConfirmPanel")
                Check(block(id).Contains("m_IsActive: 0"), field + " appears in single-player.");
        }
        foreach (string method in new[] { "ToggleSubmissionStatus", "ConfirmSubmitBridge", "CancelSubmissionConfirmation", "ConfirmLeaveBuild", "CancelLeaveBuild" })
            Check(scene.Contains("m_MethodName: " + method), "Missing authored button handler: " + method);
        Check(block("900000000000011011").Contains("m_AnchorMin: {x: 0.155, y: 1}") &&
            block("900000000000011011").Contains("m_AnchorMax: {x: 0.375, y: 1}"), "Player status escaped its authored top-left slot.");
        var plain = typeof(MultiplayerChallengeLobbyUI).GetMethod("PlainText", BindingFlags.Static | BindingFlags.NonPublic);
        Check((string)plain.Invoke(null, new object[] { "<color=red>IGN</color>" }) == "‹color=red›IGN‹/color›",
            "Player names can inject rich-text formatting.");
        report.AppendLine("PASS: Authored player dropdown, modal confirmation and separate results references are intact, persistently wired and hidden outside challenges; formatted IGNs are escaped.");
        ValidateControls(fixture, report);
        Check(block("900000000000010098").Contains("m_BackGroundColor: {r: 0, g: 0, b: 0, a: 0}") &&
            block("900000000000010098").Contains("orthographic size: 1.7") &&
            block("900000000000010097").Contains("m_LocalPosition: {x: 0, y: 1.4, z: 4}"),
            "Authored lobby camera lost transparency/full-body headroom.");
        var source = AuthoredResults();
        if (source == null)
        {
            report.AppendLine("NOTE: Reload CanyonCrossing to run the visual previews of the newly authored objects; disk references passed.");
            return;
        }
        var env = new GameObject("Challenge UI Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
        env.layer = 31; // World-space Canvas batching uses the Canvas object's layer.
        SceneManager.MoveGameObjectToScene(env, fixture.gameObject.scene);
        RenderTexture target = null;
        Texture2D picture = null;
        var lobbyTargets = new List<RenderTexture>();
        try
        {
            var canvas = env.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            env.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
            var cameraObj = new GameObject("UI Preview Camera (Temporary)", typeof(Camera));
            cameraObj.transform.SetParent(env.transform, false);
            var camera = cameraObj.GetComponent<Camera>();
            camera.scene = fixture.gameObject.scene;
            camera.orthographic = true; camera.orthographicSize = 540; camera.aspect = 1920f / 1080;
            camera.transform.localPosition = new Vector3(0, 0, -1000);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 2000;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.3f, 0.74f); camera.enabled = false;
            canvas.worldCamera = camera;
            target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
            GameObject preview = Object.Instantiate(source, env.transform, false);
            preview.SetActive(true); SetLayer(preview);
            Text(preview, "Winner").text = "<b><color=#79500F>WINNER: Guest Engineer</color></b>";
            Text(preview, "Scoring Rules").text = "<b>40% COST / 60% STRENGTH</b>  •  Budget <b>₱150,000</b>";
            Text(preview, "Host Results Name").text = "Host Engineer  <size=70%>(HOST)</size>";
            Text(preview, "Guest Results Name").text = "Guest Engineer  <size=70%>(GUEST)</size>";
            Text(preview, "Host Score").text = "<b>0.00</b><size=45%> / 100</size>";
            Text(preview, "Guest Score").text = "<b>29.39</b><size=45%> / 100</size>";
            var details = typeof(MultiplayerChallengeLobbyUI).GetMethod("ResultsDetails", BindingFlags.Static | BindingFlags.NonPublic);
            Text(preview, "Host Test Details").text = (string)details.Invoke(null, new object[] { ChallengeTestOutcome.Collapsed, 64955f, 150000f, 1f });
            Text(preview, "Guest Test Details").text = (string)details.Invoke(null, new object[] { ChallengeTestOutcome.Crossed, 92221f, 150000f, 0.767f });
            Capture(camera, target, "Temp/ChallengeResultsAuthoredPreview.png", ref picture);
            ValidateFit(preview);
            foreach (string who in new[] { "Host", "Guest" }) Text(preview, who + " Results Name").text = new string('W', 32);
            foreach (float budget in new[] { 150000f, 200000f })
            {
                Text(preview, "Winner").text = "<b>WINNER: " + new string('W', 32) + "</b>";
                Text(preview, "Guest Test Details").text = (string)details.Invoke(null,
                    new object[] { ChallengeTestOutcome.Crossed, 92221f, budget, 0.767f });
                Canvas.ForceUpdateCanvases(); ValidateFit(preview);
            }
            Object.DestroyImmediate(preview);
            foreach (string panel in new[] { "ChallengeSubmitConfirmation", "ChallengeSubmissionStatus", "Challenge_UI", "ChallengeTestIntroduction", "ChallengeLeaveBuildConfirmation", "Lobby_UI_Panel", "WorldMultiplayerPanel", "WorldKickConfirmation", "WorldTurnOffConfirmation", "SessionChatPanel" })
            {
                var original = Resources.FindObjectsOfTypeAll<GameObject>().First(obj => obj.name == panel &&
                    obj.scene.IsValid() && obj.scene.name == "CanyonCrossing");
                preview = Object.Instantiate(original, env.transform, false); preview.SetActive(true); SetLayer(preview);
                if (panel == "ChallengeSubmissionStatus")
                {
                    foreach (Transform child in preview.GetComponentsInChildren<Transform>(true))
                        if (child.name == "Player Status Details") child.gameObject.SetActive(true);
                    Text(preview, "Label").text = "<b>PLAYERS  1/2 SUBMITTED  −</b>";
                    preview.GetComponentsInChildren<TMP_Text>(true).First(text => text.gameObject.name == "Label" &&
                        text != Text(preview, "Label")).text = "Host Engineer: <b><color=#376F42>SUBMITTED</color></b>\nGuest Engineer: <b>BUILDING</b>\n\n<b>YOUR BRIDGE IS LOCKED</b>\nWaiting for your opponent.";
                }
                if (panel == "Challenge_UI")
                {
                    Text(preview, "ChallengeTitle").text = "BRIDGE CHALLENGE";
                    Text(preview, "ChallengeMessage").text = "<b>Host Engineer</b> challenges you at <b>Factory Bridge</b>.\nAccept to travel to the lobby.\n<b><color=#98601C>23 seconds remaining.</color></b>";
                    foreach (Transform child in preview.GetComponentsInChildren<Transform>(true))
                        if (child.name == "CancelInvitationButton") child.gameObject.SetActive(false);
                }
                if (panel == "ChallengeTestIntroduction")
                {
                    Text(preview, "Test Introduction Title").text = "BRIDGE TEST 1 / 2";
                    Text(preview, "Test Introduction Player").text = "Guest Engineer";
                }
                if (panel == "Lobby_UI_Panel") RenderLobbyPortraits(preview, env.transform, lobbyTargets, report);
                if (panel == "SessionChatPanel") SessionChatValidation.FormatPreview(preview);
                if (panel == "WorldMultiplayerPanel")
                {
                    Capture(camera, target, "Temp/WorldMultiplayerOfflineAuthoredPreview.png", ref picture);
                    ValidateFit(preview);
                    WorldMultiplayerPanelValidation.FormatOnlinePreview(preview);
                }
                Capture(camera, target, "Temp/" + panel + "AuthoredPreview.png", ref picture);
                ValidateFit(preview);
                if (panel == "ChallengeTestIntroduction")
                {
                    Text(preview, "Test Introduction Player").text = new string('W', 32);
                    ValidateFit(preview);
                }
                if (panel == "ChallengeSubmissionStatus")
                {
                    TMP_Text detail = preview.GetComponentsInChildren<TMP_Text>(true).First(text => text.gameObject.name == "Label" &&
                        text != Text(preview, "Label"));
                    detail.text = new string('W', 32) + ": <b>SUBMITTED</b>\n" + new string('W', 32) +
                        ": <b>BUILDING</b>\n\n<b>YOUR BRIDGE IS LOCKED</b>\nWaiting for your opponent.";
                    ValidateFit(preview);
                }
                Object.DestroyImmediate(preview);
            }
            report.AppendLine("PASS: Actual authored UI renders in isolated previews; result names up to 32 characters, metrics, invitation, confirmation and expanded player status fit their authored rectangles.");
        }
        finally
        {
            if (picture != null) Object.DestroyImmediate(picture);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            foreach (RenderTexture texture in lobbyTargets) { texture.Release(); Object.DestroyImmediate(texture); }
            Object.DestroyImmediate(env);
        }
    }

    private static void RenderLobbyPortraits(GameObject lobby, Transform parent, List<RenderTexture> targets, StringBuilder report)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var authored = Resources.FindObjectsOfTypeAll<MultiplayerChallengeLobbyUI>().First(ui =>
            ui.gameObject.scene.IsValid() && ui.gameObject.scene.name == "CanyonCrossing");
        var sourceCamera = (Camera)typeof(MultiplayerChallengeLobbyUI).GetField("portraitCamera", fields).GetValue(authored);
        GameObject stage = Object.Instantiate(sourceCamera.transform.parent.gameObject, parent, false);
        stage.SetActive(false); // Character clones cannot run source gameplay callbacks.
        var camera = stage.GetComponentInChildren<Camera>(true); camera.enabled = false;
        camera.scene = parent.gameObject.scene;
        var raw = lobby.GetComponentsInChildren<RawImage>(true).First(image => image.name == "CustomizedPlayerPortraits");
        Canvas.ForceUpdateCanvases();
        float aspect = raw.rectTransform.rect.width / raw.rectTransform.rect.height;
        var target = new RenderTexture(768, Mathf.Clamp(Mathf.RoundToInt(768f / aspect), 128, 1024), 16, RenderTextureFormat.ARGB32);
        targets.Add(target); target.Create(); camera.targetTexture = target; camera.aspect = aspect; raw.texture = target;
        var motor = Resources.FindObjectsOfTypeAll<PlayerMotor>().FirstOrDefault(player => player.gameObject.scene.name == "CanyonCrossing" &&
            player.transform.Find("NewCharacterModel") != null);
        if (motor == null) { report.AppendLine("NOTE: No authored player model was loaded for lobby framing preview."); return; }
        Transform source = motor.transform.Find("NewCharacterModel");
        foreach (string field in new[] { "hostPortraitAnchor", "guestPortraitAnchor" })
        {
            var sourceAnchor = (Transform)typeof(MultiplayerChallengeLobbyUI).GetField(field, fields).GetValue(authored);
            Transform anchor = stage.GetComponentsInChildren<Transform>(true).First(t => t.name == sourceAnchor.name);
            GameObject model = Object.Instantiate(source.gameObject, anchor, false);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = source.lossyScale;
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                typeof(MultiplayerChallengeLobbyUI).GetMethod("ConfigurePortraitRenderer", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { renderer });
                renderer.forceRenderingOff = false;
                if (renderer.gameObject.activeSelf) renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                    skinned.forceMatrixRecalculationPerRender = true;
                }
            }
            model.SetActive(true);
        }
        stage.SetActive(true);
        Transform hostAnchor = stage.GetComponentsInChildren<Transform>(true).First(t => t.name ==
            ((Transform)typeof(MultiplayerChallengeLobbyUI).GetField("hostPortraitAnchor", fields).GetValue(authored)).name);
        Transform guestAnchor = stage.GetComponentsInChildren<Transform>(true).First(t => t.name ==
            ((Transform)typeof(MultiplayerChallengeLobbyUI).GetField("guestPortraitAnchor", fields).GetValue(authored)).name);
        var framing = typeof(MultiplayerChallengeLobbyUI).GetMethod("FramePortraitColumns", BindingFlags.Static | BindingFlags.NonPublic);
        framing.Invoke(null, new object[] { camera, hostAnchor, guestAnchor, aspect });
        foreach (Animator animator in stage.GetComponentsInChildren<Animator>())
        {
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Play("Base Layer.idle", 0, 0f); animator.Update(0f);
        }
        // Use the production sun scope; the inactive fixture never subscribes to live callbacks.
        var scopeObject = new GameObject("Portrait Lighting Scope (Temporary)");
        scopeObject.transform.SetParent(parent, false); scopeObject.SetActive(false);
        var scope = scopeObject.AddComponent<MultiplayerChallengeLobbyUI>();
        typeof(MultiplayerChallengeLobbyUI).GetField("portraitCamera", fields).SetValue(scope, camera);
        var keyLight = stage.GetComponentInChildren<Light>(true);
        typeof(MultiplayerChallengeLobbyUI).GetField("portraitLight", fields).SetValue(scope, keyLight);
        var begin = typeof(MultiplayerChallengeLobbyUI).GetMethod("BeginPortraitLighting", fields);
        var end = typeof(MultiplayerChallengeLobbyUI).GetMethod("EndPortraitLighting", fields);
        object[] arguments = { default(UnityEngine.Rendering.ScriptableRenderContext), camera };
        Check(Vector3.Dot(keyLight.transform.forward, camera.transform.forward) > 0.8f,
            "Lobby key light illuminates the backs rather than the camera-facing fronts.");
        Quaternion authoredRotation = keyLight.transform.localRotation;
        Color32[] backlit;
        try
        {
            keyLight.transform.localRotation = new Quaternion(0.408f, -0.235f, 0.109f, 0.875f).normalized;
            begin.Invoke(scope, arguments); RenderPortraitCamera(camera, target); backlit = ReadPortraitPixels(target);
        }
        finally { end.Invoke(scope, arguments); keyLight.transform.localRotation = authoredRotation; }
        try { begin.Invoke(scope, arguments); RenderPortraitCamera(camera, target); }
        finally { end.Invoke(scope, arguments); }
        RenderTexture previous = RenderTexture.active;
        var sample = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; sample.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); sample.Apply();
            File.WriteAllBytes("Temp/ChallengeLobbyPortraitsPreview.png", sample.EncodeToPNG());
            Check(sample.GetPixel(0, 0).a < 0.01f, "Lobby portrait target has an opaque background.");
            Color32[] pixels = sample.GetPixels32(); int left = 0, right = 0;
            float beforeBrightness = PortraitBrightness(backlit), afterBrightness = PortraitBrightness(pixels);
            Check(beforeBrightness > 0f && afterBrightness > beforeBrightness * 1.15f,
                $"Frontal portrait light failed to brighten rendered models: before={beforeBrightness:F3}, after={afterBrightness:F3}.");
            report.AppendLine($"PASS: Rendered portrait brightness improves from {beforeBrightness:F3} (backlit control) to {afterBrightness:F3} (authored frontal key); no world light is enabled by the check.");
            for (int y = 0; y < target.height; y++)
                for (int x = 0; x < target.width; x++)
                {
                    if (pixels[y * target.width + x].a <= 12) continue;
                    // Measure the rendered silhouette rather than BakeMesh's
                    // imported-armature coordinate system/animation bounds.
                    Check(x > 0 && x < target.width - 1 && y > 0 && y < target.height - 1,
                        "Lobby portrait silhouette touches the image edge (cropped head/feet).");
                    if (x < target.width / 2) left++; else right++;
                }
            Check(left > 100 && right > 100, "Lobby render is blank or missing a player.");
            foreach (float narrowAspect in new[] { 1.4f, 2f, 3.5f })
            {
                framing.Invoke(null, new object[] { camera, hostAnchor, guestAnchor, narrowAspect });
                try { begin.Invoke(scope, arguments); RenderPortraitCamera(camera, target); }
                finally { end.Invoke(scope, arguments); }
                Color32[] narrowPixels = ReadPortraitPixels(target);
                int minX = target.width, maxX = -1;
                for (int y = 0; y < target.height; y++)
                    for (int x = 0; x < target.width; x++)
                        if (narrowPixels[y * target.width + x].a > 12)
                        { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
                Check(minX > 0 && maxX < target.width - 1 && maxX > minX,
                    $"Lobby portrait sides are cropped at image aspect {narrowAspect}.");
                Check(Mathf.Abs(camera.WorldToViewportPoint(hostAnchor.position).x - .25f) < .001f &&
                    Mathf.Abs(camera.WorldToViewportPoint(guestAnchor.position).x - .75f) < .001f,
                    "Portrait anchors do not match the two name columns.");
            }
            framing.Invoke(null, new object[] { camera, hostAnchor, guestAnchor, aspect });
            try { begin.Invoke(scope, arguments); RenderPortraitCamera(camera, target); }
            finally { end.Invoke(scope, arguments); }
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(sample); }
        report.AppendLine("PASS: Authored lobby renders both full-body models with transparent RGBA margins; camera/anchors keep both models centred in their columns without side clipping at portrait-image aspects 1.4, 2 and 3.5.");
    }

    private static void RenderPortraitCamera(Camera camera, RenderTexture target)
    {
        // Camera.Render alone does not prove URP lighting works. Use the same
        // pipeline/shader path as Play Mode for this brightness regression.
        var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };
        Check(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request), "URP portrait rendering is unavailable.");
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
    }

    private static Color32[] ReadPortraitPixels(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            return texture.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static float PortraitBrightness(Color32[] pixels)
    {
        double total = 0; int count = 0;
        foreach (Color32 pixel in pixels)
        {
            if (pixel.a < 230) continue;
            total += (0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b) / 255.0; count++;
        }
        Check(count > 100, "Portrait brightness cannot be measured from an empty render.");
        return (float)(total / count);
    }

    private static void ValidatePortraitLightingScope(Transform fixture, StringBuilder report)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var obj = new GameObject("Portrait Lighting Validation (Temporary)"); obj.SetActive(false);
        obj.transform.SetParent(fixture, false);
        var controller = obj.AddComponent<MultiplayerChallengeLobbyUI>();
        var camera = obj.AddComponent<Camera>(); camera.enabled = false;
        var other = new GameObject("World Camera (Temporary)", typeof(Camera)); other.transform.SetParent(obj.transform, false);
        other.GetComponent<Camera>().enabled = false;
        var lightRoot = new GameObject("Portrait Key Validation (Temporary)");
        SceneManager.MoveGameObjectToScene(lightRoot, fixture.gameObject.scene);
        var key = lightRoot.AddComponent<Light>(); key.type = LightType.Directional; key.enabled = false;
        typeof(MultiplayerChallengeLobbyUI).GetField("portraitCamera", fields).SetValue(controller, camera);
        typeof(MultiplayerChallengeLobbyUI).GetField("portraitLight", fields).SetValue(controller, key);
        var begin = typeof(MultiplayerChallengeLobbyUI).GetMethod("BeginPortraitLighting", fields);
        var end = typeof(MultiplayerChallengeLobbyUI).GetMethod("EndPortraitLighting", fields);
        var restore = typeof(MultiplayerChallengeLobbyUI).GetMethod("RestorePortraitLighting", fields);
        Light worldSun = RenderSettings.sun;
        try
        {
            object[] arguments = { default(UnityEngine.Rendering.ScriptableRenderContext), other.GetComponent<Camera>() };
            begin.Invoke(controller, arguments);
            Check(RenderSettings.sun == worldSun && !key.enabled, "World camera inherited portrait lighting.");
            arguments[1] = camera; begin.Invoke(controller, arguments);
            Check(RenderSettings.sun == key && key.enabled, "Portrait camera did not select its authored key light.");
            end.Invoke(controller, new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), other.GetComponent<Camera>() });
            Check(RenderSettings.sun == key, "Another camera prematurely cleared portrait lighting.");
            end.Invoke(controller, arguments);
            Check(RenderSettings.sun == worldSun && !key.enabled, "Portrait render failed to restore the world sun/key visibility.");
            begin.Invoke(controller, arguments); restore.Invoke(controller, null);
            Check(RenderSettings.sun == worldSun && !key.enabled, "Portrait cleanup leaked lighting into the world.");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(obj.transform, false);
            var renderer = cube.GetComponent<Renderer>();
            typeof(MultiplayerChallengeLobbyUI).GetMethod("ConfigurePortraitRenderer", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { renderer });
            Check(renderer.renderingLayerMask == 128 && renderer.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.Off &&
                renderer.reflectionProbeUsage == UnityEngine.Rendering.ReflectionProbeUsage.Off,
                "Portrait copies still receive world light/probe layers.");
            report.AppendLine("PASS: All quality presets support the reserved portrait layer; world lights exclude it; only portrait renderers lose world probes. Portrait camera selects its own main light and restores the world sun/key visibility after rendering or cleanup.");
        }
        finally { restore.Invoke(controller, null); RenderSettings.sun = worldSun; Object.DestroyImmediate(lightRoot); Object.DestroyImmediate(obj); }
    }

    private static void ValidateControls(Transform fixture, StringBuilder report)
    {
        var obj = new GameObject("Challenge Presentation Controls (Temporary)"); obj.transform.SetParent(fixture, false);
        var controller = obj.AddComponent<MultiplayerChallengeLobbyUI>();
        var body = new GameObject("Status Body (Temporary)", typeof(RectTransform)); body.transform.SetParent(obj.transform, false); body.SetActive(false);
        var panel = new GameObject("Confirmation (Temporary)"); panel.transform.SetParent(obj.transform, false); panel.SetActive(false);
        const BindingFlags privateFields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MultiplayerChallengeLobbyUI).GetField("submissionStatusBody", privateFields).SetValue(controller, body);
        typeof(MultiplayerChallengeLobbyUI).GetField("submissionConfirmPanel", privateFields).SetValue(controller, panel);
        FieldInfo owner = typeof(MultiplayerChallengeLobbyUI).GetField("presentationOwner", BindingFlags.Static | BindingFlags.NonPublic);
        object previousOwner = owner.GetValue(null);
        Vector2 size = body.GetComponent<RectTransform>().sizeDelta;
        try
        {
            owner.SetValue(null, controller);
            controller.ToggleSubmissionStatus(); Check(body.activeSelf, "Status did not expand.");
            controller.ToggleSubmissionStatus(); Check(!body.activeSelf && body.GetComponent<RectTransform>().sizeDelta == size,
                "Status did not collapse or modified its authored layout.");
            controller.ConfirmSubmitBridge(); Check(!panel.activeSelf, "An unopened confirmation submitted a bridge.");
            panel.SetActive(true);
            Check(MultiplayerChallengeLobbyUI.IsSubmissionConfirmationOpen, "Open confirmation failed to block editing.");
            controller.CancelSubmissionConfirmation();
            Check(!panel.activeSelf && !MultiplayerChallengeLobbyUI.IsSubmissionConfirmationOpen, "Cancel failed to close the editing gate.");
            panel.SetActive(true);
            typeof(MultiplayerChallengeLobbyUI).GetField("confirmationRevision", privateFields).SetValue(controller, 7);
            controller.ConfirmSubmitBridge();
            Check(!panel.activeSelf, "An expired/unbound confirmation did not close safely without submitting.");
            // Move the isolated controller out of the inactive fixture so this
            // exercises the actual on-screen timer, not only activeSelf.
            controller.enabled = false;
            obj.transform.SetParent(null, false);
            var intro = new GameObject("Test Introduction (Temporary)"); intro.transform.SetParent(obj.transform, false);
            typeof(MultiplayerChallengeLobbyUI).GetField("testIntroductionPanel", privateFields).SetValue(controller, intro);
            var present = typeof(MultiplayerChallengeLobbyUI).GetMethod("PresentTestIntroduction", privateFields);
            var shownAt = typeof(MultiplayerChallengeLobbyUI).GetField("introductionShownAt", privateFields);
            var refresh = typeof(MultiplayerChallengeLobbyUI).GetMethod("RefreshTestPresentation", privateFields);
            var state = new MultiplayerChallengeState { Revision = 8, TestIndex = 1, Phase = MultiplayerChallengePhase.PreparingTest };
            Check(!(bool)present.Invoke(controller, new object[] { state, "Guest Engineer", 3f }) && intro.activeInHierarchy,
                "First test skipped its visible introduction.");
            var elapsed = typeof(MultiplayerChallengeLobbyUI).GetMethod("IntroductionElapsed", BindingFlags.Static | BindingFlags.NonPublic);
            Check(!(bool)elapsed.Invoke(null, new object[] { 10f, 12.99f, 3f }) &&
                (bool)elapsed.Invoke(null, new object[] { 10f, 13f, 3f }) &&
                !(bool)elapsed.Invoke(null, new object[] { -1f, 100f, 3f }), "Introduction did not observe its configured duration.");
            state.TestIndex = 2;
            Check(!(bool)present.Invoke(controller, new object[] { state, "Host Engineer", 3f }), "Second test reused the first introduction timer.");
            state.Phase = MultiplayerChallengePhase.Testing;
            refresh.Invoke(controller, new object[] { state });
            Check(!intro.activeSelf && !(bool)present.Invoke(controller, new object[] { state, "Host Engineer", 3f }),
                "Introduction remained visible during simulation or appeared outside preparation.");
            state.Phase = MultiplayerChallengePhase.PreparingTest;
            obj.SetActive(false);
            shownAt.SetValue(controller, Time.unscaledTime - 10f);
            Check(!(bool)present.Invoke(controller, new object[] { state, "Host Engineer", 3f }), "A hidden Canvas counted as a read introduction.");
            report.AppendLine("PASS: Both tests require their own visible introduction timer; inactive UI cannot release readiness; simulation hides the panel. No physics or reliable-message rates were changed.");
            var leave = new GameObject("Leave Confirmation (Temporary)"); leave.transform.SetParent(obj.transform, false);
            typeof(MultiplayerChallengeLobbyUI).GetField("leaveBuildConfirmPanel", privateFields).SetValue(controller, leave);
            leave.SetActive(true);
            Check(MultiplayerChallengeLobbyUI.IsLeaveConfirmationOpen, "Leave confirmation did not gate editing.");
            controller.CancelLeaveBuild();
            Check(!leave.activeSelf && !MultiplayerChallengeLobbyUI.IsLeaveConfirmationOpen, "Staying failed to release the leave editing gate.");
            leave.SetActive(true);
            typeof(MultiplayerChallengeLobbyUI).GetField("leaveConfirmationRevision", privateFields).SetValue(controller, 8);
            controller.ConfirmLeaveBuild();
            Check(!leave.activeSelf, "An unbound leave confirmation did not close safely.");
            report.AppendLine("PASS: Leave confirmation gates editing; Stay restores it; an unbound confirmation cannot send cancellation. Solo ExitBuildMode still uses its original branch.");
            report.AppendLine("PASS: Player status expands/collapses without changing authored geometry; cancel restores the editing gate; unopened and unbound confirmations cannot submit a bridge.");
        }
        finally { owner.SetValue(null, previousOwner); Object.DestroyImmediate(obj); }
    }

    private static TMP_Text Text(GameObject root, string name) => root.GetComponentsInChildren<TMP_Text>(true).First(t => t.gameObject.name == name);
    private static void SetLayer(GameObject root) { foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31; }
    private static void ValidateFit(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            Check(!text.isTextOverflowing, "Authored UI text overflows: " + text.gameObject.name);
        }
    }
    private static void Capture(Camera camera, RenderTexture target, string path, ref Texture2D image)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            if (image == null) image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            Color32[] pixels = image.GetPixels32(); Color32 first = pixels[0];
            Check(pixels.Any(pixel => !pixel.Equals(first)), "Authored UI preview rendered only the background.");
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; }
    }
    private static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
}
