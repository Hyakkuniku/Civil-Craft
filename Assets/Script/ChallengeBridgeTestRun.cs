using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Disposable host physics graph, manager and vehicle. No world graph, rewards or saves are reused.</summary>
public sealed class ChallengeBridgeTestRun : IDisposable
{
    public GameObject Root { get; private set; }
    public BuildLocation Location { get; private set; }
    public BridgePhysicsManager Physics { get; private set; }
    public LiveLoadVehicle Vehicle { get; private set; }
    public readonly List<Bar> Bars = new List<Bar>();
    public readonly List<Transform> MotionTargets = new List<Transform>();
    private ContractSO contract;
    private float belowRouteTime, stallTime, bestProgress;
    private bool reachedBridge;
    public float Elapsed { get; private set; }
    public float PeakStress => Physics != null ? Mathf.Max(Physics.GetPeakDisplayedBridgeStress(), Physics.HadBrokenPartsThisRun ? 1f : 0f) : 0f;

    public static ChallengeBridgeTestRun Create(ChallengeBridgeSubmission graph, BuildLocation site, ContractSO definition,
        BarCreator creator, BridgePhysicsManager settings, LiveLoadVehicle vehicleSource, float weight, int revision, int index)
    {
        if (graph == null || site == null || definition == null || creator == null || settings == null || vehicleSource == null ||
            definition.liveLoadMode != ContractSO.LiveLoadMode.Vehicle || vehicleSource.startPoint == null || vehicleSource.endPoint == null)
            throw new InvalidOperationException("This challenge needs an authored vehicle with a Start and End route.");
        var run = new ChallengeBridgeTestRun();
        try
        {
            run.Root = new GameObject($"Session Bridge Test {revision}/{index}");
            run.Root.hideFlags = HideFlags.DontSave;
            run.Root.SetActive(false);
            SceneManager.MoveGameObjectToScene(run.Root, site.gameObject.scene);
            run.Location = run.Root.AddComponent<BuildLocation>();
            run.Location.InitializeSessionChallenge(revision, MultiplayerChallengeRules.SiteKey(site));
            run.Location.enabled = false;
            run.Location.locationCamera = site.locationCamera;
            run.contract = Object.Instantiate(definition);
            run.contract.hideFlags = HideFlags.DontSave;
            run.contract.contractID = ChallengeBuildWorkspace.ContractPrefix + "test_" + revision + "_" + index;
            run.contract.isTutorialContract = run.contract.isStoryModeContract = run.contract.countsTowardMapAchievements = false;
            run.contract.autoCollectReward = run.contract.allowVehicleCargo = false;
            run.Location.activeContract = run.contract;
            var catalog = ChallengeBridgeSubmissionRules.CreateMaterialCatalog(definition);
            var anchors = ChallengeBridgeSubmissionRules.GetAnchors(site);
            var points = new List<Point>();
            foreach (var record in graph.Nodes)
            {
                GameObject source = record.Anchor >= 0 ? anchors[record.Anchor].gameObject : creator.pointToInstantiate;
                GameObject obj = Object.Instantiate(source, record.Position, Quaternion.identity, run.Root.transform);
                StripScriptsExcept<Point>(obj);
                Point point = obj.GetComponent<Point>();
                point.ConnectedBars.Clear(); point.Runtime = true;
                point.isAnchor = point.originalIsAnchor = record.Anchor >= 0;
                point.AssignOwner(run.Location, true); point.enabled = true;
                obj.SetActive(true); points.Add(point);
            }
            foreach (Point anchor in site.startingAnchors)
                run.Location.startingAnchors.Add(points[graph.Nodes.FindIndex(node => node.Anchor == anchors.IndexOf(anchor))]);
            foreach (Point anchor in site.endingAnchors)
                run.Location.endingAnchors.Add(points[graph.Nodes.FindIndex(node => node.Anchor == anchors.IndexOf(anchor))]);
            foreach (var record in graph.Bars)
            {
                GameObject obj = Object.Instantiate(creator.barToInstantiate, run.Root.transform);
                obj.SetActive(true);
                Bar bar = obj.GetComponent<Bar>(); bar.enabled = true;
                bar.AssignOwner(run.Location, true); bar.Initialize(catalog[record.Material]);
                bar.startPoint = points[record.Start]; bar.endPoint = points[record.End];
                bar.NormalizeEndpointOrder();
                run.Bars.Add(bar);
            }
            var managerObject = new GameObject("Isolated Test Physics");
            managerObject.transform.SetParent(run.Root.transform, false);
            run.Physics = managerObject.AddComponent<BridgePhysicsManager>();
            // Unity JSON copies the scene's serialized physics/stress settings and
            // material references, not event delegates or runtime/private caches.
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settings), run.Physics);
            vehicleSource.GetSessionTestStartPose(out Vector3 startPosition, out Quaternion startRotation);
            GameObject vehicleObject = Object.Instantiate(vehicleSource.gameObject, startPosition, startRotation, run.Root.transform);
            vehicleObject.transform.localScale = vehicleSource.transform.lossyScale;
            StripScriptsExcept<LiveLoadVehicle>(vehicleObject);
            run.Vehicle = vehicleObject.GetComponent<LiveLoadVehicle>();
            run.Vehicle.enabled = true; run.Vehicle.advancesTutorial = false;
            run.Vehicle.assignedContract = run.contract; run.Vehicle.physicsManager = run.Physics;
            run.Vehicle.vehicleInfoPanel = null;
            run.Vehicle.ConfigureSessionChallengeTest(weight);
            run.Physics.ConfigureSessionChallengeTest(run.Location, run.Vehicle);
            run.HoldBridgeForPreparation();
            vehicleObject.SetActive(true);
            run.Root.SetActive(true); // Awake before Start; the vehicle subscribes on the following frame.
            foreach (var obstacle in vehicleObject.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true)) obstacle.enabled = false;
            return run;
        }
        catch { run.Dispose(); throw; }
    }

    private void HoldBridgeForPreparation()
    {
        // Copied point/bar prefabs can carry dynamic Rigidbody components from
        // an earlier test. No gravity/settling is allowed during the name panel.
        // ActivatePhysics's existing setup/release path owns the later transition.
        foreach (Point point in Root.GetComponentsInChildren<Point>(true)) HoldBody(point.GetComponent<Rigidbody>());
        foreach (Bar bar in Bars) HoldBody(bar.GetComponent<Rigidbody>());
    }
    private static void HoldBody(Rigidbody body)
    {
        if (body == null) return;
        if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        body.isKinematic = true;
    }

    private static void StripScriptsExcept<T>(GameObject obj) where T : MonoBehaviour
    {
        // The clone is inactive: no cargo/story/tutorial Awake or Start may run.
        foreach (MonoBehaviour script in obj.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(script is T)) Object.DestroyImmediate(script);
    }

    // Must run after one frame, once Initialize's discarded prefab children are gone.
    public HostWorldBridgeSnapshot CapturePresentation(string siteKey)
    {
        var snapshot = new HostWorldBridgeSnapshot { Name = siteKey };
        MotionTargets.Clear();
        foreach (Bar bar in Bars)
        {
            var record = new HostWorldBridgeBar { MaterialId = bar.materialData.Id, Pose = HostWorldBridgePose.World(bar.transform) };
            MotionTargets.Add(bar.transform);
            foreach (Transform child in bar.transform)
                if (child.gameObject.activeSelf && (child.name.StartsWith("VisualSegment", StringComparison.Ordinal) || child.name == "PierCap"))
                    record.Parts.Add(new HostWorldBridgePart { IsCap = child.name == "PierCap", Visible = true, Pose = HostWorldBridgePose.Local(child) });
            snapshot.Bars.Add(record);
        }
        MotionTargets.Add(Vehicle.transform);
        foreach (GameObject wheel in Vehicle.wheelObjects) MotionTargets.Add(wheel.transform);
        return snapshot;
    }

    public ChallengeTestOutcome Step(float dt, LevelFailedManager rules, float maximumSeconds)
    {
        if (Physics == null || Vehicle == null || !Vehicle.gameObject.activeInHierarchy) return ChallengeTestOutcome.Fell;
        if (!Physics.isSimulating) return ChallengeTestOutcome.None; // Existing settling ticks are preserved.
        Elapsed += dt;
        float stressLimit = contract.enforceMaxStress ? contract.maxAllowedStress / 100f : 1f;
        if (!BridgePhysicsManager.DebugInvincibleBridge && (Physics.HadBrokenPartsThisRun ||
            Physics.IsContractStressLimitArmed && Physics.peakStressThisRun >= stressLimit))
        { Physics.EnsureVisibleStructuralFailure("Challenge bridge stress limit exceeded"); return ChallengeTestOutcome.Collapsed; }
        if (Physics.TryGetCurrentVehicleRoadState(out _, out float load) && load > 0.01f) reachedBridge = true;
        float fallDistance = rules != null ? rules.maximumVehicleFallDistance : 6f;
        float deathY = rules != null ? rules.deathThreshold : -15f;
        float fallConfirmation = rules != null ? rules.vehicleFallConfirmationDuration : 0.35f;
        bool below = Vehicle.TryGetVerticalRouteDeviation(out float deviation, out float height)
            ? deviation < -Mathf.Max(1f, fallDistance) : height < deathY;
        below |= height < deathY;
        belowRouteTime = below ? belowRouteTime + dt : 0f;
        if (belowRouteTime >= Mathf.Max(0.05f, fallConfirmation)) return ChallengeTestOutcome.Fell;
        if (!below && reachedBridge && Vehicle.HasReachedEnd && HasConnectedRoad()) return ChallengeTestOutcome.Crossed;
        if (reachedBridge && Vehicle.IsDriving && !Vehicle.HasReachedEnd)
        {
            float progress = Vehicle.NormalizedRouteProgress;
            if (progress >= bestProgress + 0.005f) { bestProgress = progress; stallTime = 0f; }
            else stallTime += dt;
            if (stallTime >= Mathf.Max(1f, rules != null ? rules.vehicleStallTimeout : 8f)) return ChallengeTestOutcome.Stalled;
        }
        return Elapsed >= maximumSeconds ? ChallengeTestOutcome.TimeLimit : ChallengeTestOutcome.None;
    }

    private bool HasConnectedRoad()
    {
        var visited = new HashSet<Point>(Location.startingAnchors);
        var queue = new Queue<Point>(Location.startingAnchors);
        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            if (Location.endingAnchors.Contains(point)) return true;
            foreach (Bar bar in point.ConnectedBars)
            {
                if (bar == null || bar.materialData == null || !bar.materialData.isRoad) continue;
                BarStressHandler stress = bar.GetComponent<BarStressHandler>();
                if (stress != null && stress.isBroken) continue;
                Point next = bar.startPoint == point ? bar.endPoint : bar.startPoint;
                if (next != null && visited.Add(next)) queue.Enqueue(next);
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (Physics != null) Physics.StopPhysicsAndReset();
        if (Root != null) { Root.SetActive(false); Destroy(Root); }
        if (contract != null) Destroy(contract);
        Root = null; Location = null; Physics = null; Vehicle = null; contract = null;
        Bars.Clear(); MotionTargets.Clear();
    }
    private static void Destroy(Object obj) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
}
