#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BridgeDeformationValidation
{
    private const string Version = "CivilCraft.Bridge.Deformation.v1";
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
}
#endif
