using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Batch-only Play Mode check using a disposable scene and the authored tutorial vehicle route.</summary>
[InitializeOnLoad]
public static class TwoWayRoadPlayModeValidation
{
    private const string Pending = "CivilCraft.TwoWayRoadPlayModeValidation.Pending";
    private const string Report = "Logs/two-way-road-play-mode.txt";
    private const string VehiclePath = "Assets/Elements/Vehicles/Prefab/FirstLiveLoad.prefab";
    private static ChallengeBridgeTestRun run;
    private static bool started;
    private static double deadline;
    private static float previousFixedTime;
    private static float originalHalfWidth;
    private static float farthestWheelFromCenter;
    private static bool loadedOnWideLane;

    static TwoWayRoadPlayModeValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this isolated check in a separate batch Editor.");
        File.WriteAllText(Report, "RUNNING: Tutorial vehicle prefab route on the 16 m concrete deck.\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.EraseBool(Pending);
        try
        {
            Prepare();
            deadline = EditorApplication.timeSinceStartup + 85;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Complete(false, error.ToString()); }
    }

    private static void Prepare()
    {
        var root = new GameObject("Temporary two-way road route");
        root.SetActive(false);
        var contract = ScriptableObject.CreateInstance<ContractSO>();
        contract.name = "Two-Way Road Play Mode fixture";
        contract.liveLoadMode = ContractSO.LiveLoadMode.Vehicle;
        contract.twoWayRoad = true;
        contract.twoWayRoadWidth = 16f;
        var material = Resources.Load<BridgeMaterialSO>("Concrete Road");
        Require(material != null, "Concrete Road asset is missing.");
        contract.allowedMaterials.Add(new MaterialAllowance { material = material });
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePath);
        Require(prefab != null, "Tutorial vehicle prefab is missing.");
        var source = Object.Instantiate(prefab, root.transform).GetComponentInChildren<LiveLoadVehicle>(true);
        Require(source != null && source.startPoint != null && source.endPoint != null && source.wheelObjects.Length > 0,
            "The vehicle requires authored wheels and both waypoints.");
        var site = Child<BuildLocation>(root.transform, "Fixture site");
        site.activeContract = contract;
        var creator = Child<BarCreator>(root.transform, "Fixture creator");
        creator.pointToInstantiate = Child<Point>(root.transform, "Point template").gameObject;
        creator.barToInstantiate = Child<Bar>(root.transform, "Bar template").gameObject;
        var settings = Child<BridgePhysicsManager>(root.transform, "Fixture settings");
        float midpoint = (source.startPoint.position.x + source.endPoint.position.x) * .5f;
        float roadZ = source.transform.position.z - 5.5f;
        float surfaceY = source.wheelObjects.Min(wheel => wheel.transform.position.y) - source.wheelRadius;
        var start = Child<Point>(root.transform, "West anchor");
        var end = Child<Point>(root.transform, "East anchor");
        start.transform.position = new Vector3(midpoint - 4f, surfaceY - .025f, roadZ);
        end.transform.position = new Vector3(midpoint + 4f, surfaceY - .025f, roadZ);
        site.startingAnchors.Add(start);
        site.endingAnchors.Add(end);

        var graph = new ChallengeBridgeSubmission();
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = start.transform.position, Anchor = 0 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = end.transform.position, Anchor = 1 });
        graph.Bars.Add(new ChallengeSubmittedBar { Start = 0, End = 1, Material = material.Id });
        originalHalfWidth = BridgeWidthGeometry.GetPrefabBounds(material.segmentPrefab).extents.z;
        run = ChallengeBridgeTestRun.Create(graph, site, contract, creator, settings, source, source.vehicleMass, 1, 1);
        Bar deck = run.Bars.Single();
        Require(deck.RoadColliderWidth == contract.twoWayRoadWidth, "Contract deck width was lost at start.");
        Physics.SyncTransforms();
        var axles = run.Vehicle.wheelObjects.Select(wheel => wheel.transform.parent.GetComponent<SphereCollider>()).ToArray();
        Require(axles.All(axle => axle != null), "The test vehicle did not initialize all axle colliders.");
        surfaceY = axles.Min(axle => axle.bounds.min.y) - .025f;
        float outsideWheelZ = axles.Max(axle => axle.bounds.max.z);
        float insideWheelZ = axles.Min(axle => axle.bounds.min.z);
        roadZ = outsideWheelZ - 7.5f;
        Require(insideWheelZ >= roadZ - 8f, "The wheel envelope does not fit the test lane.");
        deck.startPoint.transform.position = new Vector3(midpoint - 4f, surfaceY - .025f, roadZ);
        deck.endPoint.transform.position = new Vector3(midpoint + 4f, surfaceY - .025f, roadZ);
        deck.NormalizeEndpointOrder();
        AddApproach(midpoint - 104f, surfaceY, roadZ, contract.twoWayRoadWidth);
        AddApproach(midpoint + 104f, surfaceY, roadZ, contract.twoWayRoadWidth);
        Debug.Log($"Two-way fixture: vehicle {run.Vehicle.transform.position:F2}, waypoints " +
            $"{run.Vehicle.startPoint.position:F2} → {run.Vehicle.endPoint.position:F2}; deck center {deck.transform.position:F2}, " +
            $"wheel Z envelope {insideWheelZ:F2}..{outsideWheelZ:F2}, surface Y {surfaceY:F2}.");
        Time.captureFramerate = 50;
    }

    private static void Tick()
    {
        try
        {
            if (!started)
            {
                started = true;
                previousFixedTime = Time.fixedTime;
                run.Physics.ActivatePhysics();
                Require(run.Bars.Single().GetComponent<BoxCollider>().size.z == 16f, "The live deck collider is not 16 m wide.");
                return;
            }

            if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Play Mode route check timed out.");
            float dt = Time.fixedTime - previousFixedTime;
            if (dt <= 0f) return;
            previousFixedTime = Time.fixedTime;
            Bar road = run.Bars.Single();
            if (Mathf.Abs(run.Vehicle.transform.position.x - road.transform.position.x) < 3f &&
                run.Physics.TryGetCurrentVehicleRoadState(out _, out float load) && load > .01f)
            {
                farthestWheelFromCenter = run.Vehicle.wheelObjects.Max(wheel =>
                    Mathf.Abs(wheel.transform.position.z - road.transform.position.z));
                loadedOnWideLane |= farthestWheelFromCenter > originalHalfWidth && farthestWheelFromCenter < 8f;
            }
            var outcome = run.Step(dt, null, 60f);
            if (outcome == ChallengeTestOutcome.None) return;
            bool passed = outcome == ChallengeTestOutcome.Crossed && loadedOnWideLane;
            Complete(passed, $"Tutorial vehicle route outcome {outcome} after {run.Elapsed:F2} simulation seconds. " +
                $"Vehicle load engaged outside the standard deck: {loadedOnWideLane}; outer wheel offset {farthestWheelFromCenter:F2} m; " +
                $"standard half-width {originalHalfWidth:F2} m, test half-width 8 m; peak stress {run.PeakStress:P1}; " +
                $"final vehicle position {run.Vehicle.transform.position:F2}, deck position {road.transform.position:F2}.");
        }
        catch (Exception error) { Complete(false, error.ToString()); }
    }

    private static void Complete(bool passed, string message)
    {
        EditorApplication.update -= Tick;
        var result = (passed ? "PASS: " : "FAIL: ") + message;
        File.WriteAllText(Report, result + Environment.NewLine);
        Debug.Log(result);
        run?.Dispose();
        Time.captureFramerate = 0;
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static void AddApproach(float x, float topY, float z, float width)
    {
        var approach = new GameObject("Temporary approach");
        approach.transform.position = new Vector3(x, topY - .5f, z);
        approach.layer = LayerMask.NameToLayer("Ground");
        approach.AddComponent<BoxCollider>().size = new Vector3(200f, 1f, width);
    }

    private static T Child<T>(Transform parent, string name) where T : Component
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj.AddComponent<T>();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
