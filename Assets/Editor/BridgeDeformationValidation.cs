#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BridgeDeformationValidation
{
    private const string Version = "CivilCraft.Bridge.Deformation.v3";
    static BridgeDeformationValidation()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryRun;
        };
    }
    private static void TryRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Version, false)) return;
        if (EditorApplication.isUpdating || EditorApplication.isCompiling)
        { EditorApplication.delayCall += TryRun; return; }
        if (Validate()) SessionState.SetBool(Version, true);
    }
    [MenuItem("Tools/Civil Craft/Validate Bridge Deformation Damage")]
    public static void Run() { if (!EditorApplication.isPlayingOrWillChangePlaymode) Validate(); }
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static bool Validate()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("Deformation Regression");
        SceneManager.MoveGameObjectToScene(root, scene);
        BridgeMaterialSO material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        bool originalInvincible = BridgePhysicsManager.DebugInvincibleBridge;
        try
        {
            BridgePhysicsManager.DebugInvincibleBridge = false;
            BridgePhysicsManager manager = root.AddComponent<BridgePhysicsManager>(); manager.enabled = false;
            Point P(float x, float y = 0)
            {
                GameObject go = new GameObject("Test Point"); go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(x,y,0);
                Point p = go.AddComponent<Point>(); p.preSimPos = p.transform.position; return p;
            }
            Point a = P(0), b = P(10), c = P(20);
            Bar B(Point start, Point end)
            {
                GameObject go = new GameObject("Test Bar"); go.transform.SetParent(root.transform);
                Bar bar = go.AddComponent<Bar>(); bar.materialData = material; bar.startPoint = start; bar.endPoint = end;
                start.ConnectedBars.Add(bar); end.ConnectedBars.Add(bar); return bar;
            }
            material.maxTension = 32000; material.maxCompression = 100000;
            Bar tested = B(a,b);
            BarStressHandler handler = tested.gameObject.AddComponent<BarStressHandler>();
            void Reset() { handler.Setup(manager,material,a,b); handler.BeginTracking(); handler.ApplyDeterministicStress(.2f,.2f,true); }
            Reset();
            b.transform.position = new Vector3(10.6f,0,0);
            handler.EvaluateDeformationDamage(.1f);
            Check(!handler.isBroken && handler.currentStructuralStressPercent < 1f,"Transient distortion forced a capacity failure.");
            b.transform.position = new Vector3(10,0,0);
            handler.EvaluateDeformationDamage(.1f);
            Check(Mathf.Approximately(handler.currentStressPercent,.2f),"Recovered distortion left stale warning stress.");
            b.transform.position = new Vector3(10.6f,0,0);
            handler.EvaluateDeformationDamage(.2f); Check(!handler.isBroken,"Damage timer did not reset after recovery.");
            handler.EvaluateDeformationDamage(.2f);
            Check(handler.isBroken && handler.FailureSource == "sustained physical deformation", "Sustained solid extension did not detach member.");
            b.transform.position = new Vector3(10,0,0); Reset();
            Check(!handler.isBroken && Mathf.Approximately(handler.currentStressPercent,.2f), "Retry did not reset damage.");
            BridgePhysicsManager.DebugInvincibleBridge = true;
            b.transform.position = new Vector3(11,0,0); handler.EvaluateDeformationDamage(1f);
            Check(!handler.isBroken,"Development invincibility ignored.");
            BridgePhysicsManager.DebugInvincibleBridge = false;
            b.transform.position = new Vector3(10,0,0);
            material.isRope = true; material.spring = 5000; Reset();
            b.transform.position = new Vector3(8,0,0); handler.EvaluateDeformationDamage(1f);
            Check(!handler.isBroken,"Slack rope broke in compression.");
            b.transform.position = new Vector3(17,0,0); handler.EvaluateDeformationDamage(.4f);
            Check(handler.isBroken,"Rope exceeded tension capacity without snapping.");
            b.transform.position = new Vector3(10,0,0);
            material.isRope = false; material.isRoad = true;
            Bar neighbor = B(b,c); Reset();
            c.transform.position = new Vector3(20,.2f,0); handler.EvaluateDeformationDamage(1f);
            Check(!handler.isBroken,"Mild road flex broke a member.");
            c.transform.position = new Vector3(20,4,0); handler.EvaluateDeformationDamage(.4f);
            Check(handler.isBroken && handler.FailureCause.Contains("Road joint folded"),"Folded deck did not physically break.");
            // No LevelFailedManager is instantiated or called: breakage is member-driven.
            Check(typeof(BridgePhysicsManager).GetField("remainingMemberAnalysisDirty",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager).Equals(true),"Break did not invalidate the intact load-path sample.");
            CheckSupportedRoadBending(root.transform, scene.GetPhysicsScene(), "Wood Road", 100, 10000);
            CheckSupportedRoadBending(root.transform, scene.GetPhysicsScene(), "Concrete Road", 200, 40000);
            CheckSupportedRoadBending(root.transform, scene.GetPhysicsScene(), "Concrete Road", 300, 20000, .4f);
            Debug.Log("[Bridge Deformation] PASS: transient recovery, confirmation delay, physical detachment, retry reset, invincibility, slack/overstretched rope, mild/folded roads, and load-path invalidation. No automatic unstable-layout failure.");
            return true;
        }
        catch (Exception e) { Debug.LogError("[Bridge Deformation] " + e); return false; }
        finally
        {
            BridgePhysicsManager.DebugInvincibleBridge = originalInvincible;
            Object.DestroyImmediate(root); Object.DestroyImmediate(material); EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckSupportedRoadBending(Transform parent, PhysicsScene physics, string materialName,
        float origin, float overloadKg, float trussAxialRatio = 0f)
    {
        BridgeMaterialSO Material(string name) =>
            AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>($"Assets/BridgeBuilder/Data/Resources/{name}.asset");
        BridgeMaterialSO road = Material(materialName), pier = Material("Concrete Pier");
        Check(road != null && pier != null, "Production road or pier asset is missing.");
        Check(physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene), "The test needs isolated physics.");
        var container = new GameObject($"Supported {materialName}");
        container.transform.SetParent(parent);
        BridgePhysicsManager manager = container.AddComponent<BridgePhysicsManager>();
        manager.enabled = false;
        void Call(string name, params object[] args) =>
            typeof(BridgePhysicsManager).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, args);

        var points = new List<Point>();
        var bars = new List<Bar>();
        Point JointAt(float x, float y, bool bank = false)
        {
            var joint = new GameObject("Test joint");
            joint.transform.SetParent(container.transform);
            joint.transform.position = new Vector3(origin + x, y, 0);
            Point point = joint.AddComponent<Point>();
            point.isAnchor = point.originalIsAnchor = bank;
            points.Add(point);
            return point;
        }
        Bar Place(Point a, Point b, BridgeMaterialSO material)
        {
            var go = new GameObject(material.name);
            go.transform.SetParent(container.transform);
            Bar bar = go.AddComponent<Bar>();
            bar.startPoint = a;
            bar.endPoint = b;
            bar.StartPosition = a.transform.position;
            bar.Initialize(material);
            bar.UpdateCreatingBar(b.transform.position);
            a.ConnectedBars.Add(bar);
            b.ConnectedBars.Add(bar);
            bars.Add(bar);
            Call("ApplyPhysicsToBar", bar);
            return bar;
        }

        Point left = JointAt(0, 0, true), aTop = JointAt(10, 0), bTop = JointAt(20, 0), right = JointAt(30, 0, true);
        Place(left, aTop, road);
        Bar middle = Place(aTop, bTop, road);
        Place(bTop, right, road);
        Place(JointAt(10, -25), aTop, pier);
        Place(JointAt(20, -25), bTop, pier);
        Call("SetupDirectConnections", bars, points);
        Call("ResolveAdjacentCollisions", bars);
        Check(middle.GetComponents<Joint>().Length == 2, "The middle panel needs a real attachment at both piers.");
        Rigidbody body = middle.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        Physics.SyncTransforms();

        var prepared = DeterministicBridgeStressSolver.Prepare(points, bars, false);
        BarStressHandler handler = middle.GetComponent<BarStressHandler>();
        handler.BeginTracking();
        void ApplyLoad(float kg, float addedAxial = 0f)
        {
            var result = prepared.Evaluate(new[] { new DeterministicBridgeStressSolver.PointLoad(origin + 15, kg) });
            Check(result.IsStructurallyStable && result.Samples[0].UnsupportedRoad == null, "The panel should have support.");
            Check(result.TryGetSimulationStress(middle, 0, 1f, out float displayed, out float structural,
                out bool tension, out float axial, out float bending) && bending > 0f, "The loaded panel must report bending stress.");
            if (addedAxial > 0f)
                Check(axial + addedAxial < 1f && bending < 1f && structural + addedAxial >= 1f,
                    "Both demands must fit their individual limits while exceeding the combined limit.");
            handler.ApplyDeterministicStress(displayed + addedAxial, structural + addedAxial, tension,
                axialRatio: axial + addedAxial, bendingRatio: bending);
        }
        ApplyLoad(5000);
        Check(!handler.isBroken && middle.GetComponents<Joint>().Length == 2, "Ordinary traffic must leave the panel intact.");
        ApplyLoad(overloadKg, trussAxialRatio);
        string expectedCause = trussAxialRatio > 0f ? "Combined bending and tension" : "Bending";
        Check(handler.isBroken && handler.FailureCause == expectedCause && middle.GetComponents<Joint>().Length == 0,
            "An overloaded panel must release both joints.");
        float height = body.position.y;
        for (int i = 0; i < 40; i++) physics.Simulate(.02f);
        float drop = height - body.position.y;
        Check(drop > 1f && bars[3].GetComponents<Joint>().Length == 2 && bars[4].GetComponents<Joint>().Length == 2,
            $"{materialName} must drop while both piers remain attached. Drop={drop:F2} m.");
        Debug.Log($"[Road Bending] PASS: {materialName} carried 5000 kg, then detached at {overloadKg:0} kg from {expectedCause} and dropped {drop:F2} m.");
    }
}
#endif
