#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in direction check using the live-load motor in a disposable physics world.</summary>
[InitializeOnLoad]
internal static class MillsHeavyEquipmentDirectionValidation
{
    private const string Request = "Temp/mills-crane-direction.request";
    private const string Report = "Logs/mills-crane-direction.txt";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static MillsHeavyEquipmentDirectionValidation() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        // A requested physical check runs only in Edit Mode, never on the game world.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { Run(command); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    private static void Run(string command)
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing before requesting this check.");
        var vehicle = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true))
            .Single(value => value.assignedContract != null && value.assignedContract.ContractID == "MillsMainContract");
        var report = new StringBuilder("Mills crane motor direction\n");
        string sourceState = EditorJsonUtility.ToJson(vehicle);
        bool sourceDirty = scene.isDirty;
        report.AppendLine("Vehicle: " + vehicle.name + "; configured spin axis: " + vehicle.spinAxis);
        report.AppendLine($"Chassis position={vehicle.transform.position:F4}, rotation={vehicle.transform.eulerAngles:F3}, scale={vehicle.transform.lossyScale:F3}; route={vehicle.startPoint.position:F3} -> {vehicle.endPoint.position:F3}");
        if (command == "render")
        {
            RenderVisual(vehicle, report);
            File.WriteAllText(Report, report.ToString());
            return;
        }
        if (command == "startup" || command == "approach")
        {
            ProbeStartup(vehicle, report, command == "approach");
            File.WriteAllText(Report, report.ToString());
            return;
        }
        float forwardAxis = Probe(vehicle, Vector3.forward, 1f);
        float backwardAxis = Probe(vehicle, Vector3.back, 1f);
        report.AppendLine($"+Z axle, route +X: displacement {forwardAxis:F4}m");
        report.AppendLine($"-Z axle, route +X: displacement {backwardAxis:F4}m");
        if (command == "probe")
        {
            report.AppendLine("PROBE COMPLETE: source scene and source vehicle were not simulated or modified.");
            File.WriteAllText(Report, report.ToString());
            return;
        }
        Require(command == "validate", "Unknown direction-check command.");
        Require(forwardAxis > 1f && backwardAxis > 1f, "Both axle signs must propel toward +X.");
        foreach (Vector3 axis in new[] { Vector3.forward, Vector3.back })
        {
            float reverseRoute = Probe(vehicle, axis, -1f);
            Require(reverseRoute < -1f, "Reverse route motor must propel toward -X for either axle sign.");
            report.AppendLine($"PASS axle {axis}, route -X: displacement {reverseRoute:F4}m");
        }
        foreach (float direction in new[] { 1f, -1f })
        {
            float rotated = Probe(vehicle, Vector3.forward, direction, true);
            Require(rotated * direction > 1f, "A reversed chassis pose must preserve the route direction.");
            report.AppendLine($"PASS 180-degree chassis, route {direction:+0;-0}X: displacement {rotated:F4}m");
        }
        Require(ProbeStartup(vehicle, report, true) > 1f, "The authored crane must advance on its real approach.");
        Require(sourceState == EditorJsonUtility.ToJson(vehicle) && scene.isDirty == sourceDirty,
            "Direction validation changed the source vehicle or scene dirty state.");
        report.AppendLine("PASS: six axle/route/rotation combinations plus actual source startup on copied Mills approach geometry.");
        report.AppendLine("PASS: source scene, authored route, crane placement and source vehicle unchanged.");
        File.WriteAllText(Report, report.ToString());
    }

    private static float Probe(LiveLoadVehicle source, Vector3 spinAxis, float routeDirection, bool rotated = false)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        Scene active = SceneManager.GetActiveScene();
        PhysicMaterial grip = null;
        try
        {
            PhysicsScene physics = preview.GetPhysicsScene();
            Require(physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene) &&
                !physics.Equals(source.gameObject.scene.GetPhysicsScene()), "Fixture must have independent physics.");
            grip = new PhysicMaterial("Mills direction fixture grip")
            { dynamicFriction = 1f, staticFriction = 1f, bounciness = 0f, frictionCombine = PhysicMaterialCombine.Maximum };
            var ground = New(preview, "Flat approach reference");
            ground.transform.position = new Vector3(0, -.5f, 0);
            var floor = ground.AddComponent<BoxCollider>(); floor.size = new Vector3(100, 1, 20); floor.sharedMaterial = grip;
            var wheelGeometry = source.wheelObjects.Select(wheel => new {
                wheel,
                center = source.transform.InverseTransformPoint(WheelCenter(wheel)),
                radius = (float)Call(source, "ResolveWheelRadius", wheel.GetComponentInChildren<Renderer>()) }).ToArray();
            var root = New(preview, "Detached Mills chassis");
            root.transform.position = Vector3.up * (.03f - wheelGeometry.Min(wheel => wheel.center.y - wheel.radius));
            root.transform.rotation = source.transform.rotation * (rotated ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
            var body = root.AddComponent<Rigidbody>();
            body.mass = source.vehicleMass; body.sleepThreshold = 0f;
            body.constraints = RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationZ;
            body.centerOfMass = Vector3.up * source.centerOfMassOffset;
            body.solverIterations = 12; body.solverVelocityIterations = 4;
            var chassis = root.AddComponent<BoxCollider>();
            var authored = source.GetComponent<BoxCollider>();
            Require(authored != null, "Mills chassis collider is missing.");
            chassis.center = authored.center; chassis.size = authored.size;
            var driver = root.AddComponent<LiveLoadVehicle>();
            driver.enabled = false;
            driver.spinAxis = spinAxis; driver.maxSpeed = source.maxSpeed;
            driver.wheelRadius = source.wheelRadius; driver.engineTorque = source.engineTorque;
            driver.wheelMass = source.wheelMass;
            Set(driver, "rb", body); Set(driver, "isDriving", true);
            Set(driver, "authoredStartPosition", root.transform.position);
            Set(driver, "authoredStartRotation", root.transform.rotation);
            var start = New(preview, "Detached start"); start.transform.position = root.transform.position;
            var end = New(preview, "Detached end"); end.transform.position = root.transform.position + Vector3.right * routeDirection * 40f;
            driver.startPoint = start.transform; driver.endPoint = end.transform;
            Call(driver, "CaptureAuthoredStartPointPose");
            var wheels = (IList)typeof(LiveLoadVehicle).GetField("wheels", PrivateInstance).GetValue(driver);
            Type wheelType = typeof(LiveLoadVehicle).GetNestedType("WheelData", BindingFlags.NonPublic);
            foreach (var geometry in wheelGeometry)
            {
                GameObject sourceWheel = geometry.wheel;
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                SceneManager.MoveGameObjectToScene(wheel, preview);
                wheel.name = "Detached " + sourceWheel.name;
                wheel.transform.SetParent(root.transform, false);
                wheel.transform.localPosition = geometry.center;
                wheel.GetComponent<Renderer>().enabled = false;
                var sphere = wheel.GetComponent<SphereCollider>(); sphere.radius = geometry.radius; sphere.sharedMaterial = grip;
                Physics.IgnoreCollision(chassis, sphere, true);
                var wheelBody = wheel.AddComponent<Rigidbody>(); wheelBody.mass = source.wheelMass;
                wheelBody.sleepThreshold = 0f; wheelBody.maxAngularVelocity = 50f;
                wheelBody.solverIterations = 12; wheelBody.solverVelocityIterations = 4;
                var hinge = wheel.AddComponent<HingeJoint>(); hinge.connectedBody = body;
                hinge.axis = (Vector3)Call(driver, "ResolveWheelAxleAxis", wheel, wheel.transform);
                hinge.anchor = Vector3.zero; hinge.autoConfigureConnectedAnchor = false;
                hinge.connectedAnchor = wheel.transform.localPosition;
                hinge.enableCollision = false;
                hinge.motor = new JointMotor { force = source.engineTorque, freeSpin = false };
                object data = Activator.CreateInstance(wheelType);
                wheelType.GetField("physObj").SetValue(data, wheel);
                wheelType.GetField("rb").SetValue(data, wheelBody);
                wheelType.GetField("hinge").SetValue(data, hinge);
                wheelType.GetField("axleAxis").SetValue(data, hinge.axis);
                wheelType.GetField("radius").SetValue(data, geometry.radius);
                wheels.Add(data);
            }
            Physics.SyncTransforms();
            for (int step = 0; step < 30; step++) physics.Simulate(.02f);
            float from = body.position.x;
            for (int step = 0; step < 150; step++)
            {
                Call(driver, "FixedUpdate");
                physics.Simulate(.02f);
            }
            return body.position.x - from;
        }
        finally
        {
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            if (grip != null) UnityEngine.Object.DestroyImmediate(grip);
        }
    }

    private static float ProbeStartup(LiveLoadVehicle source, StringBuilder report, bool actualApproach)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        PhysicMaterial grip = null;
        try
        {
            var physics = preview.GetPhysicsScene();
            Require(!physics.Equals(Physics.defaultPhysicsScene), "Startup fixture needs independent physics.");
            var copy = UnityEngine.Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(copy, preview);
            // Edit Mode normally protects imported prefab children from being
            // reparented. Only this disposable clone is unpacked for runtime Awake.
            foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                    PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            copy.SetActive(true);
            var driver = copy.GetComponent<LiveLoadVehicle>();
            driver.enabled = false; driver.vehicleInfoPanel = null;
            Set(driver, "configureNPCObstacle", false);
            Set(driver, "sessionTestWeight", source.vehicleMass);
            var starts = driver.wheelObjects.Select(WheelCenter).ToArray();
            Call(driver, "Awake");
            driver.physicsManager = null;
            var wheels = (IList)typeof(LiveLoadVehicle).GetField("wheels", PrivateInstance).GetValue(driver);
            Type wheelType = typeof(LiveLoadVehicle).GetNestedType("WheelData", BindingFlags.NonPublic);
            for (int i = 0; i < wheels.Count; i++)
            {
                var axle = (GameObject)wheelType.GetField("physObj").GetValue(wheels[i]);
                var axis = (Vector3)wheelType.GetField("axleAxis").GetValue(wheels[i]);
                float radius = (float)wheelType.GetField("radius").GetValue(wheels[i]);
                Require(Vector3.Distance(axle.transform.position, starts[i]) < .01f && radius > .05f,
                    "Runtime wheel axle must be centered on the visible tire with a valid radius.");
                report.AppendLine($"Startup axle {i}: position={axle.transform.position:F4}, expected={starts[i]:F4}, localAxis={axis:F3}, worldAxis={axle.transform.TransformDirection(axis):F3}");
                report.AppendLine($"  Physical tire radius: {radius:F4}m");
            }
            grip = new PhysicMaterial("Mills startup fixture road")
            { dynamicFriction = 1, staticFriction = 1, bounciness = 0, frictionCombine = PhysicMaterialCombine.Maximum };
            if (actualApproach)
            {
                Bounds region = new Bounds(source.transform.position + Vector3.right * 3, new Vector3(32, 16, 14));
                var colliders = source.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Collider>(true))
                    .Where(value => value.enabled && value.gameObject.activeInHierarchy && !value.isTrigger &&
                        value.attachedRigidbody == null && value.GetComponentInParent<LiveLoadVehicle>() == null && value.bounds.Intersects(region)).ToArray();
                var copyCollider = typeof(IndustrialRoadSeamPhysicsCheck).GetMethod("CopyCollider", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (Collider collider in colliders)
                {
                    var copied = (Collider)copyCollider.Invoke(null, new object[] { collider, preview });
                    copied.gameObject.layer = collider.gameObject.layer;
                    report.AppendLine("APPROACH COLLIDER: " + collider.name + "; " + collider.bounds);
                }
                Require(colliders.Length > 0, "No actual approach geometry found.");
                var site = MillsHeavyEquipmentSiteAuthoring.FindLocation(source.gameObject.scene);
                var anchors = MillsHeavyEquipmentSiteAuthoring.GetRoadAnchors(site);
                var deck = New(preview, "Rigid direction-test bridge");
                deck.transform.position = (anchors.start.transform.position + anchors.end.transform.position) * .5f - Vector3.up * .075f;
                var surface = deck.AddComponent<BoxCollider>();
                surface.size = new Vector3(anchors.end.transform.position.x - anchors.start.transform.position.x + .6f, .15f, 16);
                surface.sharedMaterial = grip;
            }
            else
            {
                var ground = New(preview, "Flat approach reference");
                ground.transform.position = new Vector3(source.transform.position.x, source.transform.position.y - .51f, source.transform.position.z);
                var box = ground.AddComponent<BoxCollider>(); box.size = new Vector3(100, 1, 20); box.sharedMaterial = grip;
            }
            Call(driver, "BuildWheelPhysics");
            Call(driver, "StartDrivingForSimulation");
            Physics.SyncTransforms();
            var body = copy.GetComponent<Rigidbody>();
            Vector3 from = body.position;
            for (int step = 0; step < 150; step++)
            {
                Call(driver, "FixedUpdate"); physics.Simulate(.02f);
            }
            report.AppendLine($"ACTUAL STARTUP: displacement={body.position-from:F4}, velocity={body.velocity:F4}, bodyConstraints={body.constraints}");
            report.AppendLine("PROBE COMPLETE: exact cloned source Awake, wheel setup, drive start and FixedUpdate; source scene unchanged.");
            return body.position.x - from.x;
        }
        finally
        {
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (grip != null) UnityEngine.Object.DestroyImmediate(grip);
        }
    }

    private static void RenderVisual(LiveLoadVehicle vehicle, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture prior = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            Transform visual = vehicle.transform.Find("Crane1 1 Visual") ?? vehicle.transform.Find("Temporary Mobile Crane Visual");
            Require(visual != null, "Missing crane visual.");
            report.AppendLine($"Visual world rotation={visual.eulerAngles:F3}, scale={visual.lossyScale:F3}");
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null) continue;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount && sub < renderer.sharedMaterials.Length; sub++)
                {
                    string name = renderer.sharedMaterials[sub] != null ? renderer.sharedMaterials[sub].name : "none";
                    if (name.Contains("Headlight") || name.Contains("Taillight") || name.Contains("WindowGlass"))
                        report.AppendLine($"{name}: chassis-local center {vehicle.transform.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.GetSubMesh(sub).bounds.center)):F3}");
                }
            }
            GameObject copy = UnityEngine.Object.Instantiate(visual.gameObject);
            SceneManager.MoveGameObjectToScene(copy, preview); copy.SetActive(true);
            Renderer[] renderers = copy.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera = New(preview, "Crane side camera").AddComponent<Camera>();
            camera.enabled = false; camera.scene = preview; camera.orthographic = true;
            camera.orthographicSize = bounds.extents.x / 1.65f;
            camera.transform.position = bounds.center + new Vector3(0, .45f, -22);
            camera.transform.LookAt(bounds.center);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.72f, .77f, .80f);
            var light = New(preview, "Crane key light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            target = new RenderTexture(1200, 700, 24); target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            image = new Texture2D(1200, 700, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1200, 700), 0, 0); image.Apply();
            File.WriteAllBytes("Temp/mills-crane-side.png", image.EncodeToPNG());
            report.AppendLine("RENDERED: Temp/mills-crane-side.png; world +X is to the right.");
        }
        finally
        {
            RenderTexture.active = prior;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }

    private static object Call(LiveLoadVehicle target, string method, params object[] arguments) =>
        typeof(LiveLoadVehicle).GetMethod(method, PrivateInstance).Invoke(target, arguments);
    private static void Set(LiveLoadVehicle target, string field, object value) =>
        typeof(LiveLoadVehicle).GetField(field, PrivateInstance).SetValue(target, value);
    private static Vector3 WheelCenter(GameObject wheel)
    {
        Renderer tire = wheel.GetComponentInChildren<Renderer>();
        return tire != null && tire.enabled ? tire.bounds.center : wheel.transform.position;
    }
    private static GameObject New(Scene scene, string name)
    { var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); return value; }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
#endif
