#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Runs actual bridge rigidbodies in an isolated local PhysX scene.</summary>
[InitializeOnLoad]
public static class ConcretePhysicsValidation
{
    private const string Version = "CivilCraft.Concrete.Physics.v2";
    static ConcretePhysicsValidation()
    {
        EditorApplication.delayCall += TryValidate;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += TryValidate;
        };
    }
    private static void TryValidate()
    {
        // Local-physics SceneManager.CreateScene is only supported in Play Mode.
        // The test scene is isolated and destroyed before the next game update.
        if (!EditorApplication.isPlaying || SessionState.GetBool(Version, false)) return;
        if (EditorApplication.isUpdating || EditorApplication.isCompiling)
        { EditorApplication.delayCall += TryValidate; return; }
        if (Validate()) SessionState.SetBool(Version, true);
    }
    [MenuItem("Tools/Civil Craft/Validate Concrete Physics")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) Validate();
        else Debug.Log("[Concrete Physics] Enter Play Mode to run the isolated PhysX validation.");
    }
    private static bool Validate()
    {
        try
        {
            RunTruss(false);
            RunTruss(true);
            return true;
        }
        catch (Exception e) { Debug.LogError("[Concrete Physics] " + e); return false; }
    }
    private static void Call(BridgePhysicsManager manager, string method, params object[] args)
    {
        typeof(BridgePhysicsManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, args);
    }
    private static void RunTruss(bool conditioned)
    {
        BridgeMaterialSO road = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Concrete Road.asset");
        BridgeMaterialSO beam = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset");
        if (road == null || beam == null) throw new InvalidOperationException("Materials missing.");
        Scene scene = SceneManager.CreateScene("Concrete Physics Validation", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        GameObject root = new GameObject("Isolated Concrete Truss");
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            BridgePhysicsManager manager = root.AddComponent<BridgePhysicsManager>();
            manager.enabled = false;
            List<Point> points = new List<Point>();
            List<Bar> bars = new List<Bar>();
            Point MakePoint(float x, float y, bool anchor)
            {
                GameObject go = new GameObject("Test Joint"); go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(x, y, 0);
                Point p = go.AddComponent<Point>(); p.originalIsAnchor = p.isAnchor = anchor;
                points.Add(p); return p;
            }
            void MakeBar(Point a, Point b, BridgeMaterialSO material)
            {
                GameObject go = new GameObject("Test Bar"); go.transform.SetParent(root.transform);
                Bar bar = go.AddComponent<Bar>(); bar.startPoint = a; bar.endPoint = b;
                bar.StartPosition = a.transform.position; bar.Initialize(material); bar.UpdateCreatingBar(b.transform.position);
                a.ConnectedBars.Add(bar); b.ConnectedBars.Add(bar); bars.Add(bar);
                Call(manager, "ApplyPhysicsToBar", bar);
            }
            Point[] lower = new Point[6], upper = new Point[5];
            for (int i = 0; i < lower.Length; i++) lower[i] = MakePoint(i * 10f, 0, i == 0 || i == 5);
            for (int i = 0; i < upper.Length; i++)
            {
                upper[i] = MakePoint(i * 10f + 5f, 8, false);
                MakeBar(lower[i], lower[i + 1], road);
                MakeBar(lower[i], upper[i], beam); MakeBar(upper[i], lower[i + 1], beam);
                if (i > 0) MakeBar(upper[i - 1], upper[i], beam);
            }
            Call(manager, "SetupDirectConnections", bars, points);
            Call(manager, "ResolveAdjacentCollisions", bars);
            List<Joint> joints = new List<Joint>(root.GetComponentsInChildren<Joint>());
            List<Rigidbody> bodies = new List<Rigidbody>(root.GetComponentsInChildren<Rigidbody>());
            foreach (Joint joint in joints)
                if (!conditioned) joint.massScale = joint.connectedMassScale = 1f;
            // Anchors must start coincident and masses must remain authored.
            foreach (Joint joint in joints)
                if (Vector3.Distance(joint.transform.TransformPoint(joint.anchor), joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) > .001f)
                    throw new InvalidOperationException("Joint starts with mismatched anchors.");
            foreach (Rigidbody body in bodies)
            {
                Point p = body.GetComponent<Point>();
                body.isKinematic = p != null && p.isAnchor;
                body.useGravity = !body.isKinematic;
                body.ResetInertiaTensor();
            }
            Physics.SyncTransforms();
            PhysicsScene physics = scene.GetPhysicsScene();
            float maxGap = 0f, maxSpeed = 0f;
            for (int frame = 0; frame < 300; frame++)
            {
                physics.Simulate(.02f);
                foreach (Joint joint in joints)
                    maxGap = Mathf.Max(maxGap, Vector3.Distance(joint.transform.TransformPoint(joint.anchor), joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)));
                foreach (Rigidbody body in bodies)
                    if (!body.isKinematic) maxSpeed = Mathf.Max(maxSpeed, body.velocity.magnitude);
            }
            Debug.Log($"[Concrete Physics] {(conditioned ? "Conditioned" : "Legacy")} 50m truss: max joint separation={maxGap:0.000}m, max speed={maxSpeed:0.00}m/s over 6s.");
            if (conditioned && (maxGap > .3f || maxSpeed > 5f || float.IsNaN(maxGap)))
                throw new InvalidOperationException("Conditioned concrete truss still unstable.");
        }
        finally { Object.DestroyImmediate(root); SceneManager.UnloadSceneAsync(scene); }
    }
}
#endif
