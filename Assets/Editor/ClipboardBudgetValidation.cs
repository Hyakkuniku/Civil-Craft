#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in, cost-only clipboard regression. Never opens UI, advances tutorials or saves scenes.</summary>
public static class ClipboardBudgetValidation
{
    private const string RequestPath = "Temp/ClipboardBudgetValidation.request";
    private const string ReportPath = "Temp/ClipboardBudgetValidation.txt";
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool SafeToRun() => !running && !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath) || !SafeToRun()) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Clipboard Preview Budget")]
    public static void Validate()
    {
        if (!SafeToRun())
        {
            Debug.LogWarning("[Clipboard budget] Stay in idle Edit Mode outside Prefab Mode.");
            return;
        }
        running = true;
        var report = new StringBuilder("RUNNING: Isolated copied-preview / committed-construction budget regression.\n");
        var originals = new Dictionary<int, SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            originals.Add(scene.handle, new SceneState(scene));
        }
        var singletonState = new Dictionary<Type, object>();
        foreach (Type type in new[] { typeof(ClipboardManager), typeof(BuildUIController), typeof(CommandManager),
            typeof(GameManager), typeof(BuildTutorialDirector), typeof(TutorialManager), typeof(AudioManager) })
            singletonState.Add(type, ReadSingleton(type));
        var originalPoints = new List<Point>(Point.AllPoints);
        bool touchEnabled = EnhancedTouchSupport.enabled;
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        Scene preview = default;
        BridgeMaterialSO material = null;
        bool passed = false;
        Directory.CreateDirectory("Temp");
        File.WriteAllText(ReportPath, report.ToString());
        try
        {
            preview = EditorSceneManager.NewPreviewScene();
            GameObject root = NewObject("Clipboard budget fixture", preview, null, true);
            GameObject controls = NewObject("Inert controls (never enabled)", preview, root.transform, false);
            BarCreator creator = controls.AddComponent<BarCreator>();
            ClipboardManager clipboard = controls.AddComponent<ClipboardManager>();
            BuildUIController ui = controls.AddComponent<BuildUIController>();
            ui.barCreator = creator;
            SetField(clipboard, "barCreator", creator); // Do not invoke Awake or any real Copy/Stamp UI path.
            BuildLocation location = NewObject("Inert owned site", preview, root.transform, false).AddComponent<BuildLocation>();
            creator.barParent = NewObject("Fixture bars", preview, root.transform, true).transform;
            creator.pointParent = NewObject("Fixture points", preview, root.transform, true).transform;
            creator.barToInstantiate = NewObject("Inactive bar template", preview, controls.transform, false);
            creator.barToInstantiate.AddComponent<Bar>();
            creator.pointToInstantiate = NewObject("Inactive point template", preview, controls.transform, false);
            creator.pointToInstantiate.AddComponent<Point>();
            material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
            material.name = "Budget regression material (temporary)";
            material.hideFlags = HideFlags.HideAndDontSave;
            material.costPerMeter = 125f;
            material.isDualBeam = true;
            material.segmentPrefab = null; // No real prefab assets, nested Bars or deferred child destruction.
            const float length = 4f;
            float singleCost = length * material.costPerMeter * 2f;
            Bar original = Commit(creator, location, material, length, "Original construction");
            Check(!original.IsConstructionPreview && original.enabled, "Original bar is not normal construction.");
            ExpectCost(ui, location, new[] { original }, singleCost, "Original bridge");

            SetField(clipboard, "copiedRelativePoints", new List<Vector3> { Vector3.zero, Vector3.right * length });
            SetField(clipboard, "copiedBars", new List<CopiedBarInfo>
            {
                new CopiedBarInfo { startIdx = 0, endIdx = 1, mat = material }
            });
            Call(clipboard, "CreatePasteGhosts"); // Its initial destroy loop is empty: no Edit Mode Destroy call.
            var ghosts = GetField<List<Bar>>(clipboard, "ghostPasteBars");
            var ghostPoints = GetField<List<GameObject>>(clipboard, "ghostPastePoints");
            Check(ghosts.Count == 1 && ghostPoints.Count == 2, "Actual clipboard preview creation did not create copied members/endpoints.");
            Bar ghost = ghosts[0];
            ghost.AssignOwner(location);
            ghost.currentLength = length;
            ghost.StartPosition = Vector3.up;
            ghost.EndPosition = Vector3.up + Vector3.right * length;
            ghost.gameObject.SetActive(true);
            foreach (GameObject point in ghostPoints) point.SetActive(true);
            Check(ghost.IsConstructionPreview && !ghost.enabled && ghost.materialData == material,
                "CreatePasteGhosts did not mark/disable the preview before material initialization.");
            Check(!BridgeConstructionReceipt.IsLogicalMember(ghost) && BridgeConstructionReceipt.ConstructionCost(ghost) == 0f,
                "Held copy remains a logical paid construction member.");
            Check(Mathf.Approximately(ghost.GetCost(), singleCost), "Preview potential-placement cost was lost.");
            Check(!ghostPoints[0].GetComponent<Point>().enabled && !ghostPoints[1].GetComponent<Point>().enabled &&
                !Point.AllPoints.Contains(ghostPoints[0].GetComponent<Point>()) && !Point.AllPoints.Contains(ghostPoints[1].GetComponent<Point>()),
                "Clipboard preview endpoints pollute the live construction-point registry.");
            ExpectCost(ui, location, new[] { original, ghost }, singleCost, "Copy without paste");
            ExpectReceipt(location, new[] { original, ghost }, singleCost, true, "Disabled preview receipt");
            // Identity, not merely MonoBehaviour.enabled, must exclude a preview.
            ghost.enabled = true;
            for (int i = 0; i < 3; i++)
            {
                ghost.transform.position = new Vector3(10f + i, 5f + i, 0f);
                ghost.StartPosition = ghost.transform.position;
                ghost.EndPosition = ghost.StartPosition + Vector3.right * (length + i);
                ghost.currentLength = length + i;
                ExpectCost(ui, location, new[] { original, ghost }, singleCost, "Moving/re-enabled held copy");
                ExpectReceipt(location, new[] { original, ghost, ghost }, singleCost, false, "Active preview receipt");
                Check(ghost.GetCost() > 0f && BridgeConstructionReceipt.ConstructionCost(ghost) == 0f,
                    "Moving the preview changed its construction identity or potential price.");
            }
            ghost.enabled = false;
            report.AppendLine("PASS: Actual CreatePasteGhosts marks copied bars before Initialize; copying/moving/re-enabling the held visual costs nothing. Potential Bar.GetCost remains positive; both ordinary and includeDisabled receipts exclude previews; preview endpoints stay unregistered.");

            Bar placed = Commit(creator, location, material, length, "Fresh committed paste");
            var candidates = new[] { original, ghost, placed, placed, ghost };
            Check(!placed.IsConstructionPreview && placed.enabled && Mathf.Approximately(placed.GetCost(), singleCost),
                "Fresh same-template committed bar inherited preview state or lost its price.");
            ExpectCost(ui, location, candidates, singleCost * 2f, "Committed paste with held repeat preview");
            ExpectReceipt(location, candidates, singleCost * 2f, true, "Committed paste receipt");
            var history = new HistoryAction { isBuildEvent = true };
            history.affectedObjects.Add(placed.gameObject);
            history.Undo();
            ExpectCost(ui, location, candidates, singleCost, "Undo pasted bridge");
            ExpectReceipt(location, candidates, singleCost, true, "Undo receipt");
            history.Redo();
            ExpectCost(ui, location, candidates, singleCost * 2f, "Redo pasted bridge");
            ExpectReceipt(location, candidates, singleCost * 2f, true, "Redo receipt");
            // Runtime cancellation clears bookkeeping immediately, while
            // Unity Destroy removes GameObjects only at frame end. Reproduce
            // that interval without calling forbidden Destroy in Edit Mode.
            ghosts.Clear();
            ghostPoints.Clear();
            clipboard.isPasteMode = false;
            Check(ghost != null && ghost.gameObject.activeInHierarchy, "Cancellation interval fixture lost the still-live ghost.");
            ExpectCost(ui, location, candidates, singleCost * 2f, "Canceled preview before deferred destruction");
            ExpectReceipt(location, candidates, singleCost * 2f, true, "Canceled preview receipt");
            history.Undo();
            history.Redo();
            ExpectCost(ui, location, candidates, singleCost * 2f, "Repeated undo/redo after cancellation");
            report.AppendLine("PASS: A fresh committed paste is charged exactly once even with duplicate candidates/held repeat preview. HistoryAction Undo refunds and Redo recharges only committed construction. Canceled-but-still-live preview objects remain free before deferred destruction.");
            passed = true;
        }
        catch (Exception exception)
        {
            if (exception is TargetInvocationException invocation && invocation.InnerException != null) exception = invocation.InnerException;
            report.AppendLine("FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally
        {
            try
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (material != null) Object.DestroyImmediate(material);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selection;
                Selection.activeObject = activeSelection;
                Check(EnhancedTouchSupport.enabled == touchEnabled, "EnhancedTouch enable state changed.");
                Check(Point.AllPoints.Count == originalPoints.Count, "Point registry count changed.");
                for (int i = 0; i < originalPoints.Count; i++) Check(Point.AllPoints[i] == originalPoints[i], "Point registry contents/order changed.");
                foreach (KeyValuePair<Type, object> singleton in singletonState)
                    Check(ReferenceEquals(ReadSingleton(singleton.Key), singleton.Value), "Live singleton changed: " + singleton.Key.Name);
                Check(SceneManager.sceneCount == originals.Count, "Authored loaded scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    Check(originals.TryGetValue(scene.handle, out SceneState state), "An authored scene was replaced.");
                    state.Verify(scene);
                }
                report.AppendLine("PASS: Real singleton references, Point registry, touch state, loaded scenes/roots/dirty flags, active scene and selection preserved; all fixture resources removed. No UI/tutorial/audio callbacks, source assets, scene saves or Play Mode.");
            }
            catch (Exception exception)
            {
                passed = false;
                report.AppendLine("FAIL isolation/cleanup: " + exception);
                Debug.LogException(exception);
            }
            running = false;
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(ReportPath, report.ToString());
            if (passed) Debug.Log("[Clipboard budget] PASS. Report: " + ReportPath);
        }
    }

    private static Bar Commit(BarCreator creator, BuildLocation location, BridgeMaterialSO material, float length, string name)
    {
        GameObject obj = Object.Instantiate(creator.barToInstantiate, creator.barParent);
        obj.name = name;
        Bar bar = obj.GetComponent<Bar>();
        bar.Initialize(material);
        bar.AssignOwner(location);
        bar.currentLength = length;
        bar.StartPosition = Vector3.zero;
        bar.EndPosition = Vector3.right * length;
        obj.SetActive(true);
        return bar;
    }

    private static void ExpectCost(BuildUIController ui, BuildLocation location, IEnumerable<Bar> bars, float expected, string step)
    {
        MethodInfo calculate = typeof(BuildUIController).GetMethod("RecalculateStaticBridge", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(bool), typeof(BuildLocation), typeof(IEnumerable<Bar>) }, null);
        Check(calculate != null, "Cost-only Build UI recalculation API missing.");
        calculate.Invoke(ui, new object[] { false, location, bars });
        Check(Mathf.Approximately(ui.GetTotalCost(), expected), step + " charged " + ui.GetTotalCost() + ", expected " + expected + ".");
    }

    private static void ExpectReceipt(BuildLocation location, IEnumerable<Bar> bars, float expected, bool includeDisabled, string step)
    {
        BridgeConstructionReceipt receipt = BridgeConstructionReceipt.Capture(location, bars, null, includeDisabled);
        Check(Mathf.Approximately(receipt.TotalCost, expected), step + " charged " + receipt.TotalCost + ", expected " + expected + ".");
    }

    private static GameObject NewObject(string name, Scene scene, Transform parent, bool active)
    {
        var value = new GameObject(name);
        value.SetActive(active);
        SceneManager.MoveGameObjectToScene(value, scene);
        if (parent != null) value.transform.SetParent(parent, false);
        return value;
    }

    private static object ReadSingleton(Type type)
    {
        PropertyInfo property = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property != null) return property.GetValue(null);
        FieldInfo field = type.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Check(field != null, "Singleton accessor missing: " + type.Name);
        return field.GetValue(null);
    }

    private static void Call(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Check(method != null, "Clipboard method missing: " + name);
        method.Invoke(target, null);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Check(field != null, "Clipboard field missing: " + name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Check(field != null, "Clipboard field missing: " + name);
        return (T)field.GetValue(target);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class SceneState
    {
        private readonly bool dirty, loaded;
        private readonly string path;
        private readonly HashSet<int> roots = new HashSet<int>();
        public SceneState(Scene scene)
        {
            dirty = scene.isDirty; loaded = scene.isLoaded; path = scene.path;
            foreach (GameObject root in scene.GetRootGameObjects()) roots.Add(root.GetInstanceID());
        }
        public void Verify(Scene scene)
        {
            var current = new HashSet<int>();
            foreach (GameObject root in scene.GetRootGameObjects()) current.Add(root.GetInstanceID());
            Check(dirty == scene.isDirty && loaded == scene.isLoaded && path == scene.path && roots.SetEquals(current), "Authored scene changed: " + scene.name);
        }
    }
}
#endif
