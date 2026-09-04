using System; 
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)] 
public class BridgePhysicsManager : MonoBehaviour
{
    /// <summary>Development-menu override. Normal gameplay must leave this false.</summary>
    public static bool DebugInvincibleBridge { get; set; }

    public event Action OnSettlePhaseStarted;
    public event Action OnSimulationStarted;
    public event Action OnSimulationStopped;

    [Header("Physics Settings")]
    public float barColliderThickness = 0.2f;
    public int physicsSolverIterations = 40; 
    public int settleFramesAmount = 60;

    [Header("Finalized Road Collision")]
    [Tooltip("Permanent physical thickness of a saved road. This collider supports CharacterControllers even if NavMesh rebuilding fails.")]
    [Min(0.02f)] [SerializeField] private float bakedRoadColliderThickness = 0.12f;
    [Tooltip("Permanent physical width of a saved road. Keep this wider than the player's CharacterController diameter.")]
    [Min(0.1f)] [SerializeField] private float bakedRoadColliderWidth = 2.4f;
    [Tooltip("Collective endpoint overlap used to prevent physical seams between neighboring saved road bars.")]
    [Min(0f)] [SerializeField] private float bakedRoadColliderSeamOverlap = 0.2f;
    [Tooltip("Local height of the road's visible top surface before the permanent collider is thickened.")]
    [SerializeField] private float bakedRoadVisualTop = 0.025f;

    [Header("Stress Sampling")]
    [Tooltip("Number of fixed-physics samples used by the current-stress display. This is a rolling average, never a stored maximum.")]
    [Min(1)] public int stressSmoothingFrames = 10;
    [Tooltip("Ignores tiny endpoint-length changes when deciding whether a bar is in tension or compression.")]
    [Min(0f)] public float stressDirectionDeadZone = 0.0005f;
    [Tooltip("Shows load added after the dead-load settling phase. Failure and peak-stress checks still use total structural stress.")]
    public bool displayLiveLoadStressOnly = true;
    [Tooltip("Snaps force readings back to their settled dead-load value inside this relative tolerance, removing PhysX resting jitter.")]
    [Range(0f, 0.25f)] public float deadLoadReturnTolerance = 0.02f;
    [Tooltip("Minimum force tolerance, in Newtons, used when returning to the settled dead-load value.")]
    [Min(0f)] public float deadLoadReturnToleranceNewtons = 1f;

    [Header("Stress Visualizer Colors")]
    public bool enableVisualizer = true;
    public Color warningColor = Color.yellow;
    public Color criticalColor = Color.red;
    public Color brokenColor = Color.black;

    [HideInInspector] public bool isSimulating = false;
    [HideInInspector] public bool lockStressTracking = false; 
    
    [HideInInspector] public List<BarStressHandler> activeStressHandlers = new List<BarStressHandler>();
    [HideInInspector] public float peakStressThisRun = 0f;

    private HashSet<Point> simPoints = new HashSet<Point>();
    private HashSet<Bar> simBars = new HashSet<Bar>();

    private List<Point> deterministicPoints = new List<Point>();
    private List<Bar> deterministicBars = new List<Bar>();

    private bool pendingSimulationStart = false;
    private bool needsPhysicsRelease = false; 
    private int currentSettleFrame = 0;

    private PhysicMaterial sharedRoadPhysicsMat;
    private bool deterministicPhysicsOverridesApplied;
    private bool previousAutoSyncTransforms;
    private int previousSolverIterations;
    private int previousSolverVelocityIterations;

    // --- Deterministic Spatial Comparers ---
    private class SpatialPointComparer : IComparer<Point>
    {
        public int Compare(Point a, Point b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            Vector3 posA = a.transform.position;
            Vector3 posB = b.transform.position;

            float tolerance = 0.001f;

            if (Mathf.Abs(posA.x - posB.x) > tolerance) return posA.x.CompareTo(posB.x);
            if (Mathf.Abs(posA.y - posB.y) > tolerance) return posA.y.CompareTo(posB.y);
            if (Mathf.Abs(posA.z - posB.z) > tolerance) return posA.z.CompareTo(posB.z);

            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        }
    }

    private class SpatialBarComparer : IComparer<Bar>
    {
        private SpatialPointComparer pointComparer = new SpatialPointComparer();

        public int Compare(Bar a, Bar b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            Point a1 = a.startPoint;
            Point a2 = a.endPoint;
            if (pointComparer.Compare(a1, a2) > 0) { a1 = a.endPoint; a2 = a.startPoint; }

            Point b1 = b.startPoint;
            Point b2 = b.endPoint;
            if (pointComparer.Compare(b1, b2) > 0) { b1 = b.endPoint; b2 = b.startPoint; }

            int p1Compare = pointComparer.Compare(a1, b1);
            if (p1Compare != 0) return p1Compare;

            int p2Compare = pointComparer.Compare(a2, b2);
            if (p2Compare != 0) return p2Compare;

            if (a.materialData != null && b.materialData != null)
            {
                int matCompare = string.Compare(a.materialData.name, b.materialData.name, StringComparison.Ordinal);
                if (matCompare != 0) return matCompare;
            }

            return a.GetInstanceID().CompareTo(b.GetInstanceID()); 
        }
    }

    private void Awake()
    {
        sharedRoadPhysicsMat = new PhysicMaterial("BridgeRoadGrip");
        sharedRoadPhysicsMat.dynamicFriction = 1f;
        sharedRoadPhysicsMat.staticFriction = 1f;
        sharedRoadPhysicsMat.frictionCombine = PhysicMaterialCombine.Maximum;
        sharedRoadPhysicsMat.bounciness = 0f;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    private void OnDestroy()
    {
        RestoreGlobalPhysicsSettings();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void FixedUpdate()
    {
        if (pendingSimulationStart)
        {
            if (needsPhysicsRelease)
            {
                needsPhysicsRelease = false;
                Physics.SyncTransforms(); 

                foreach (Bar bar in deterministicBars)
                {
                    if (bar != null && !bar.materialData.isPier)
                    {
                        Rigidbody rb = bar.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.useGravity = true;
                            rb.isKinematic = false;
                            rb.WakeUp();
                        }
                    }
                }

                foreach (Point p in deterministicPoints)
                {
                    if (p != null && !p.isAnchor)
                    {
                        Rigidbody rb = p.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.useGravity = true;
                            rb.isKinematic = false;
                            rb.WakeUp();
                        }
                    }
                }

                OnSettlePhaseStarted?.Invoke(); 
            }

            currentSettleFrame++;
            
            if (currentSettleFrame >= settleFramesAmount)
            {
                pendingSimulationStart = false;
                isSimulating = true;
                peakStressThisRun = 0f;
                lockStressTracking = false;
                
                foreach (var handler in activeStressHandlers)
                {
                    if (handler != null) handler.BeginTracking();
                }
                
                OnSimulationStarted?.Invoke(); 
            }
            return; 
        }

        if (isSimulating && !lockStressTracking)
        {
            float currentMax = 0f;
            foreach (var handler in activeStressHandlers)
            {
                if (handler == null) continue;
                
                handler.EvaluateStress(); 
                
                if (handler.isBroken) currentMax = 1f;
                else if (handler.currentStructuralStressPercent > currentMax)
                    currentMax = handler.currentStructuralStressPercent;
            }

            if (currentMax > peakStressThisRun)
            {
                peakStressThisRun = currentMax;
            }
        }
    }

    private void GatherActiveBridgeData(out HashSet<Point> outPoints, out HashSet<Bar> outBars)
    {
        outPoints = new HashSet<Point>();
        outBars = new HashSet<Bar>();

        BuildLocation activeLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;

        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf && p.enabled &&
                (activeLocation == null || activeLocation.Owns(p)))
            {
                outPoints.Add(p);
            }
        }

        foreach (Point p in outPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && b.enabled &&
                    (activeLocation == null || activeLocation.Owns(b)))
                {
                    outBars.Add(b);
                }
            }
        }

        foreach (Bar b in outBars)
        {
            if (b.startPoint != null && b.startPoint.enabled) outPoints.Add(b.startPoint);
            if (b.endPoint != null && b.endPoint.enabled) outPoints.Add(b.endPoint);
        }
    }

    private void HandleEnterBuildMode()
    {
        if (isSimulating || pendingSimulationStart) StopPhysicsAndReset();
        else SetNodesVisible(true);
    }

    private void HandleExitBuildMode()
    {
        SetNodesVisible(false);
    }

    private void SetNodesVisible(bool isVisible)
    {
        HashSet<Point> points;
        HashSet<Bar> bars;
        GatherActiveBridgeData(out points, out bars);

        foreach (Point p in Point.AllPoints)
        {
            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = isVisible && p.gameObject.activeSelf && points.Contains(p);
        }
    }

    public void ActivatePhysics()
    {
        if (isSimulating || pendingSimulationStart) return;
        
        activeStressHandlers.Clear(); 
        peakStressThisRun = 0f;
        lockStressTracking = false;

        GatherActiveBridgeData(out simPoints, out simBars);

        deterministicPoints = new List<Point>(simPoints);
        deterministicPoints.Sort(new SpatialPointComparer());

        deterministicBars = new List<Bar>(simBars);
        deterministicBars.Sort(new SpatialBarComparer());

        foreach (Point p in deterministicPoints)
        {
            p.preSimPos = p.transform.position;
            p.preSimRot = p.transform.rotation;
            p.preSimParent = p.transform.parent;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = false;
        }

        foreach (Bar b in deterministicBars)
        {
            // Existing, pasted, redone, and newly drawn bars must all enter
            // simulation with the same endpoint order and transform orientation.
            b.NormalizeEndpointOrder();
            b.preSimPos = b.transform.position;
            b.preSimRot = b.transform.rotation;
        }

        ApplyDeterministicPhysicsSettings();
        Physics.SyncTransforms();

        SetupBarsPhysics(deterministicBars);
        SetupDirectConnections(deterministicBars, deterministicPoints);
        ReleaseUnsupportedRoadJoints(deterministicBars, deterministicPoints);
        ResolveAdjacentCollisions(deterministicBars);
        ResetPhysicsState();

        needsPhysicsRelease = true;
        currentSettleFrame = 0;
        pendingSimulationStart = true;
    }

    public void StopPhysicsAndReset()
    {
        if (!isSimulating && !pendingSimulationStart) return;
        
        isSimulating = false;
        pendingSimulationStart = false;
        OnSimulationStopped?.Invoke(); 

        activeStressHandlers.Clear();

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null) continue;
            foreach (Joint j in bar.GetComponentsInChildren<Joint>()) { j.connectedBody = null; DestroyImmediate(j); }
            foreach (Rigidbody rb in bar.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
        }

        foreach (Point p in deterministicPoints)
        {
            if (p == null) continue;
            foreach (Joint j in p.GetComponentsInChildren<Joint>()) { j.connectedBody = null; DestroyImmediate(j); }
            foreach (Rigidbody rb in p.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
            foreach (CapsuleCollider cc in p.GetComponents<CapsuleCollider>()) DestroyImmediate(cc);
        }

        bool isCurrentlyBuilding = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building;

        foreach (Point p in deterministicPoints)
        {
            if (p == null) continue;
            
            Collider[] cols = p.GetComponentsInChildren<Collider>();
            foreach(var col in cols) col.enabled = true; 

            p.transform.SetParent(p.preSimParent);
            p.transform.position = p.preSimPos;
            p.transform.rotation = p.preSimRot;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = isCurrentlyBuilding && p.gameObject.activeSelf;
        }

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null) continue;
            
            if (bar.materialData != null && bar.materialData.isPier)
            {
                Transform cap = bar.transform.Find("PierCap");
                if (cap != null)
                {
                    Renderer capRend = cap.GetComponentInChildren<Renderer>();
                    if (capRend != null)
                    {
                        BoxCollider bc = capRend.GetComponent<BoxCollider>();
                        if (bc != null) { bc.enabled = false; DestroyImmediate(bc); }
                    }
                }

                foreach (Transform child in bar.transform)
                {
                    if (child.name.StartsWith("VisualSegment"))
                    {
                        Renderer segRend = child.GetComponentInChildren<Renderer>();
                        if (segRend != null)
                        {
                            BoxCollider bc = segRend.GetComponent<BoxCollider>();
                            if (bc != null) { bc.enabled = false; DestroyImmediate(bc); }
                        }
                    }
                }
            }
            else
            {
                BoxCollider[] parentCols = bar.GetComponents<BoxCollider>();
                foreach (BoxCollider c in parentCols) { c.enabled = false; DestroyImmediate(c); }
            }

            BarStressHandler stress = bar.GetComponent<BarStressHandler>();
            if (stress != null) DestroyImmediate(stress);

            bar.transform.position = bar.preSimPos;
            bar.transform.rotation = bar.preSimRot;
            
            if (bar.gameObject.activeSelf && bar.startPoint != null && bar.endPoint != null)
            {
                bar.StartPosition = bar.startPoint.transform.position;
            }
        }

        simPoints.Clear();
        simBars.Clear();
        deterministicPoints.Clear();
        deterministicBars.Clear();

        Physics.SyncTransforms();
        RestoreGlobalPhysicsSettings();
    }

    private void ApplyDeterministicPhysicsSettings()
    {
        if (!deterministicPhysicsOverridesApplied)
        {
            previousAutoSyncTransforms = Physics.autoSyncTransforms;
            previousSolverIterations = Physics.defaultSolverIterations;
            previousSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            deterministicPhysicsOverridesApplied = true;
        }

        // Setup transform changes are synchronized explicitly. Avoiding implicit
        // sync points keeps physics setup independent from render-frame timing.
        Physics.autoSyncTransforms = false;
        Physics.defaultSolverIterations = Mathf.Max(1, physicsSolverIterations);
        Physics.defaultSolverVelocityIterations = 20;
    }

    private void RestoreGlobalPhysicsSettings()
    {
        if (!deterministicPhysicsOverridesApplied) return;

        Physics.autoSyncTransforms = previousAutoSyncTransforms;
        Physics.defaultSolverIterations = previousSolverIterations;
        Physics.defaultSolverVelocityIterations = previousSolverVelocityIterations;
        deterministicPhysicsOverridesApplied = false;
    }

    /// <summary>
    /// Clears dynamic state only from the bridge bodies participating in the
    /// current simulation. Do not replace this with FindObjectsOfType<Rigidbody>:
    /// that would also reset the player, NPCs, vehicles, and world props.
    /// </summary>
    public void ResetPhysicsState()
    {
        HashSet<Rigidbody> simulationBodies = new HashSet<Rigidbody>();

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null) continue;
            foreach (Rigidbody body in bar.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) simulationBodies.Add(body);
        }

        foreach (Point point in deterministicPoints)
        {
            if (point == null) continue;
            foreach (Rigidbody body in point.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) simulationBodies.Add(body);
        }

        foreach (Rigidbody body in simulationBodies)
        {
            // All bridge bodies are deliberately held kinematic until the first
            // controlled fixed tick, so no force can leak into the new run.
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.position = body.transform.position;
            body.rotation = body.transform.rotation;
            body.ResetInertiaTensor();
            body.Sleep();
        }

        Physics.SyncTransforms();
    }

    public bool BakeBridge(ContractSO contract = null)
    {
        HashSet<Point> bakePoints = new HashSet<Point>();
        HashSet<Bar> bakeBars = new HashSet<Bar>();

        BuildLocation targetLoc = null;

        if (contract != null)
        {
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            foreach (var loc in allLocs)
            {
                if (loc.gameObject.scene.name != null && loc.activeContract == contract)
                {
                    targetLoc = loc;
                    break;
                }
            }
        }
        else if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
        {
            targetLoc = GameManager.Instance.ActiveBuildLocation;
        }

        if (targetLoc == null)
        {
            Debug.LogError("[BridgePhysicsManager] Cannot bake: no matching build location was found.", this);
            return false;
        }

        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf && p.enabled && targetLoc.Owns(p))
            {
                bakePoints.Add(p);
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.enabled && targetLoc.Owns(b))
                        bakeBars.Add(b);
                }
            }
        }

        foreach (Point anchor in targetLoc.startingAnchors)
        {
            if (anchor != null) { bakePoints.Add(anchor); Queue<Point> q = new Queue<Point>(); q.Enqueue(anchor); ProcessQueue(q); }
        }
        
        foreach (Point anchor in targetLoc.endingAnchors)
        {
            if (anchor != null && !bakePoints.Contains(anchor)) { bakePoints.Add(anchor); Queue<Point> q = new Queue<Point>(); q.Enqueue(anchor); ProcessQueue(q); }
        }

        void ProcessQueue(Queue<Point> queue)
        {
            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                foreach (Bar b in current.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && targetLoc.Owns(b) &&
                        !bakeBars.Contains(b))
                    {
                        bakeBars.Add(b);
                        Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                        if (neighbor != null && targetLoc.Owns(neighbor) && !bakePoints.Contains(neighbor))
                        {
                            bakePoints.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        foreach(Bar b in targetLoc.bakedBars) { if (b != null) bakeBars.Add(b); }
        foreach(Point p in targetLoc.bakedPoints) { if (p != null) bakePoints.Add(p); }

        if (bakePoints.Count < 2 || bakeBars.Count == 0)
        {
            Debug.LogError(
                $"[BridgePhysicsManager] Refusing to bake '{targetLoc.name}': the captured bridge has " +
                $"{bakePoints.Count} point(s) and {bakeBars.Count} bar(s).", this);
            return false;
        }

        foreach (Bar bar in bakeBars)
        {
            if (bar == null || bar.startPoint == null || bar.endPoint == null ||
                bar.materialData == null || !bakePoints.Contains(bar.startPoint) ||
                !bakePoints.Contains(bar.endPoint))
            {
                Debug.LogError(
                    $"[BridgePhysicsManager] Refusing to bake '{targetLoc.name}': a bar has invalid endpoints or material data.",
                    this);
                return false;
            }
        }

        foreach (Point p in bakePoints)
            if (p != null) p.AssignOwner(targetLoc, true);
        foreach (Bar b in bakeBars)
            if (b != null) b.AssignOwner(targetLoc, true);

        targetLoc.bakedPoints.Clear();
        targetLoc.bakedBars.Clear();

        foreach (Point p in bakePoints)
        {
            if (p == null) continue;
            foreach (Rigidbody rb in p.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true; rb.useGravity = false; rb.velocity = Vector3.zero;
            }
        }
        foreach (Bar b in bakeBars)
        {
            if (b == null) continue;
            foreach (Rigidbody rb in b.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true; rb.useGravity = false; rb.velocity = Vector3.zero;
            }
        }

        foreach (Point p in bakePoints)
        {
            if (p == null) continue;
            foreach (var j in p.GetComponentsInChildren<Joint>()) Destroy(j);
            foreach (var rb in p.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            p.enabled = false; 
            targetLoc.bakedPoints.Add(p); 
        }

        foreach (Bar b in bakeBars)
        {
            if (b == null) continue;
            foreach (var j in b.GetComponentsInChildren<Joint>()) Destroy(j);
            foreach (var rb in b.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            if (b.GetComponent<BarStressHandler>() != null) Destroy(b.GetComponent<BarStressHandler>());
            EnsurePermanentBakedRoadCollider(b);
            b.enabled = false; 
            targetLoc.bakedBars.Add(b); 
        }

        activeStressHandlers.Clear();
        isSimulating = false;
        pendingSimulationStart = false;
        simPoints.Clear();
        simBars.Clear();
        deterministicPoints.Clear();
        deterministicBars.Clear();

        RestoreGlobalPhysicsSettings();
        Physics.SyncTransforms();

        if (DynamicNavMeshUpdater.Instance != null)
            DynamicNavMeshUpdater.Instance.UpdateWalkableNavMeshForLocation(targetLoc);

        OnSimulationStopped?.Invoke();
        return true;
    }

    private void EnsurePermanentBakedRoadCollider(Bar bar)
    {
        if (bar == null || bar.materialData == null || !bar.materialData.isRoad ||
            bar.startPoint == null || bar.endPoint == null) return;

        BoxCollider[] existingColliders = bar.GetComponents<BoxCollider>();
        BoxCollider roadCollider = existingColliders.Length > 0
            ? existingColliders[0]
            : bar.gameObject.AddComponent<BoxCollider>();

        // Remove redundant parent road colliders left by an earlier simulation.
        for (int i = 1; i < existingColliders.Length; i++)
        {
            if (existingColliders[i] != null)
            {
                existingColliders[i].enabled = false;
                Destroy(existingColliders[i]);
            }
        }

        float length = Vector3.Distance(
            bar.startPoint.transform.position,
            bar.endPoint.transform.position);

        roadCollider.size = new Vector3(
            Mathf.Max(0.05f, length + bakedRoadColliderSeamOverlap),
            bakedRoadColliderThickness,
            Mathf.Max(bakedRoadColliderWidth, bar.visualSize.z));
        roadCollider.center = new Vector3(
            0f,
            bakedRoadVisualTop - bakedRoadColliderThickness * 0.5f,
            0f);
        roadCollider.isTrigger = false;
        roadCollider.enabled = true;
        roadCollider.material = sharedRoadPhysicsMat;

        int bridgeLayer = LayerMask.NameToLayer("Bridge");
        if (bridgeLayer >= 0) bar.gameObject.layer = bridgeLayer;
    }

    public float GetMaxBridgeStress()
    {
        float maxStress = 0f;
        foreach (var handler in activeStressHandlers)
        {
            if (handler == null) continue;
            if (handler.isBroken) return 1f; 

            if (handler.currentStressPercent > maxStress)
            {
                maxStress = handler.currentStressPercent;
            }
        }
        return Mathf.Clamp01(maxStress); 
    }

    private void SetupBarsPhysics(List<Bar> activeBars)
    {
        foreach (Bar bar in activeBars)
        {
            if (bar.GetComponent<Rigidbody>() == null) ApplyPhysicsToBar(bar);
        }
    }

    private void ApplyPhysicsToBar(Bar bar)
    {
        bar.NormalizeEndpointOrder();
        Point p1 = bar.startPoint;
        Point p2 = bar.endPoint;
        if (p1 == null || p2 == null || !p1.gameObject.activeSelf || !p2.gameObject.activeSelf)
            return;

        if (!bar.materialData.isRope)
        {
            float length = Vector3.Distance(p1.transform.position, p2.transform.position);
            
            Rigidbody barRb = bar.GetComponent<Rigidbody>();
            if (barRb == null) barRb = bar.gameObject.AddComponent<Rigidbody>();
            
            barRb.isKinematic = true;
            barRb.useGravity = true;
            
            barRb.mass = length * bar.materialData.GetPlacedMassPerMeter();
            barRb.drag = 0.5f;
            barRb.angularDrag = 0.5f;
            barRb.interpolation = RigidbodyInterpolation.Interpolate;
            barRb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            barRb.sleepThreshold = 0f;
            barRb.maxDepenetrationVelocity = 2f;
            barRb.velocity = Vector3.zero;
            barRb.angularVelocity = Vector3.zero;

            BoxCollider[] oldCols = bar.GetComponents<BoxCollider>();
            foreach(var c in oldCols) { c.enabled = false; DestroyImmediate(c); }

            if (bar.materialData.isPier)
            {
                Transform cap = bar.transform.Find("PierCap");
                if (cap != null)
                {
                    Renderer capRend = cap.GetComponentInChildren<Renderer>();
                    if (capRend != null && capRend.gameObject.GetComponent<Collider>() == null)
                    {
                        capRend.gameObject.AddComponent<BoxCollider>();
                    }
                }

                foreach (Transform child in bar.transform)
                {
                    if (child.name.StartsWith("VisualSegment"))
                    {
                        Renderer segRend = child.GetComponentInChildren<Renderer>();
                        if (segRend != null && segRend.gameObject.GetComponent<Collider>() == null)
                        {
                            segRend.gameObject.AddComponent<BoxCollider>();
                        }
                    }
                }
            }
            else
            {
                int spawnCount = bar.materialData.isDualBeam ? 2 : 1;
                for (int i = 0; i < spawnCount; i++)
                {
                    BoxCollider col = bar.gameObject.AddComponent<BoxCollider>();
                    
                    float thickness = bar.materialData.isRoad ? 0.05f : barColliderThickness;
                    float depth = bar.visualSize.z; 

                    if (!bar.materialData.isDualBeam && depth < 2.0f) depth = 2.0f; 
                    else if (bar.materialData.isDualBeam && depth < 0.2f) depth = 0.2f;

                    float zOffsetValue = bar.materialData.isDualBeam ? ((i == 0) ? bar.materialData.zOffset : -bar.materialData.zOffset) : 0f;
                    float physicsLength = length - 0.02f; 
                    
                    col.size = new Vector3(physicsLength, thickness, depth);
                    col.center = new Vector3(0, 0, zOffsetValue);
                    
                    if (bar.materialData.isRoad) col.material = sharedRoadPhysicsMat;
                }
            }
        }

        bool isConnectedToTerrainAnchor = p1.originalIsAnchor || p2.originalIsAnchor;

        if (isConnectedToTerrainAnchor)
        {
            bar.gameObject.layer = LayerMask.NameToLayer("Node"); 
        }
        else
        {
            bar.gameObject.layer = LayerMask.NameToLayer("Bridge"); 
        }

        BarStressHandler stressHandler = bar.GetComponent<BarStressHandler>();
        if (stressHandler == null) stressHandler = bar.gameObject.AddComponent<BarStressHandler>();
        
        stressHandler.Setup(bar.materialData, p1, p2);
        activeStressHandlers.Add(stressHandler);
    }

    private void SetupDirectConnections(List<Bar> activeBars, List<Point> activePoints)
    {
        foreach (Point p in activePoints)
        {
            if (!p.gameObject.activeSelf || p.ConnectedBars.Count == 0) continue; 

            // --- FIX 2: Sort the local Point connections! ---
            // This guarantees floating-point mass math and PhysX Joint Evaluation 
            // happen in the exact same sequence regardless of load/draw order.
            List<Bar> sortedConnectedBars = new List<Bar>(p.ConnectedBars);
            sortedConnectedBars.Sort(new SpatialBarComparer());

            Collider[] oldCols = p.GetComponents<Collider>();
            foreach(var col in oldCols) col.enabled = false; 

            Rigidbody nodeRb = p.GetComponent<Rigidbody>();
            if (nodeRb == null) nodeRb = p.gameObject.AddComponent<Rigidbody>();
            
            nodeRb.isKinematic = true;
            nodeRb.useGravity = !p.isAnchor;
            
            if (!p.isAnchor)
            {
                float calculatedMass = 0.5f;
                
                // USE THE SORTED LIST
                foreach (Bar bar in sortedConnectedBars)
                {
                    if (bar == null || !bar.gameObject.activeSelf) continue; 
                    
                    float len = Vector3.Distance(bar.startPoint.transform.position, bar.endPoint.transform.position);
                    // Solid members already carry their own Rigidbody mass. Ropes have no
                    // Rigidbody, so distribute only their mass equally to the endpoint nodes.
                    if (bar.materialData.isRope)
                        calculatedMass += (len * bar.materialData.GetPlacedMassPerMeter()) * 0.5f;
                }
                
                nodeRb.mass = calculatedMass;
                nodeRb.drag = 0.5f;
                nodeRb.angularDrag = 0.5f;
                nodeRb.interpolation = RigidbodyInterpolation.Interpolate;

                nodeRb.sleepThreshold = 0f;
                nodeRb.maxDepenetrationVelocity = 2f;
            }

            nodeRb.velocity = Vector3.zero;
            nodeRb.angularVelocity = Vector3.zero;

            bool isRoadNode = false;
            float maxZDepth = 2.0f;
            
            // USE THE SORTED LIST
            foreach (Bar bar in sortedConnectedBars)
            {
                if (bar == null || !bar.gameObject.activeSelf) continue; 
                
                if (bar.materialData != null && bar.materialData.isRoad)
                {
                    isRoadNode = true;
                    float barZ = bar.materialData.isDualBeam ? bar.visualSize.z + (bar.materialData.zOffset * 2f) : bar.visualSize.z;
                    if (barZ > maxZDepth) maxZDepth = barZ;
                }
                
                if (!bar.materialData.isRope) AttachJoint(bar.gameObject, nodeRb, bar.materialData, p.transform.position);
            }

            if (isRoadNode)
            {
                CapsuleCollider groutCylinder = p.gameObject.AddComponent<CapsuleCollider>();
                groutCylinder.radius = 0.025f; 
                groutCylinder.height = maxZDepth; 
                groutCylinder.direction = 2; 
                p.gameObject.layer = LayerMask.NameToLayer("Bridge"); 
                
                groutCylinder.material = sharedRoadPhysicsMat;
            }
        }

        foreach (Bar rope in activeBars)
        {
            if (!rope.materialData.isRope) continue;

            Rigidbody rbA = rope.startPoint.GetComponent<Rigidbody>();
            Rigidbody rbB = rope.endPoint.GetComponent<Rigidbody>();

            if (rbA != null && rbB != null)
            {
                SpringJoint ropeSpring = rbA.gameObject.AddComponent<SpringJoint>();
                ropeSpring.connectedBody = rbB;
                ropeSpring.autoConfigureConnectedAnchor = false;
                ropeSpring.enablePreprocessing = false; 

                ropeSpring.anchor = rbA.transform.InverseTransformPoint(rope.startPoint.transform.position);
                ropeSpring.connectedAnchor = rbB.transform.InverseTransformPoint(rope.endPoint.transform.position);

                float length = Vector3.Distance(rope.startPoint.transform.position, rope.endPoint.transform.position);

                ropeSpring.maxDistance = length;
                ropeSpring.minDistance = 0f;
                ropeSpring.spring = rope.materialData.spring > 0 ? rope.materialData.spring : 5000f;
                ropeSpring.damper = rope.materialData.damper > 0 ? rope.materialData.damper : 500f; 

                BarStressHandler stressHandler = rope.GetComponent<BarStressHandler>();
                if (stressHandler != null) stressHandler.SetRopeJoint(ropeSpring);
            }
        } 
    }

    /// <summary>
    /// Road pieces are the driving surface, not a replacement for the supporting
    /// frame. A road joint may remain connected only when that point has a load
    /// path to an anchor through a beam, rope, or pier. A single road piece can
    /// still span between two genuine supports within its existing max length.
    /// </summary>
    private void ReleaseUnsupportedRoadJoints(List<Bar> activeBars, List<Point> activePoints)
    {
        HashSet<Bar> activeBarSet = new HashSet<Bar>(activeBars);
        HashSet<Point> structurallySupportedPoints = new HashSet<Point>();
        Queue<Point> searchQueue = new Queue<Point>();

        foreach (Point point in activePoints)
        {
            if (point == null || !point.gameObject.activeSelf || !point.isAnchor) continue;
            if (structurallySupportedPoints.Add(point)) searchQueue.Enqueue(point);
        }

        // Do not traverse road pieces: otherwise an unsupported road-only chain
        // would incorrectly make every deck joint appear structurally supported.
        while (searchQueue.Count > 0)
        {
            Point current = searchQueue.Dequeue();
            foreach (Bar bar in current.ConnectedBars)
            {
                if (bar == null || !activeBarSet.Contains(bar) || !bar.gameObject.activeSelf ||
                    bar.materialData == null || bar.materialData.isRoad)
                    continue;

                Point neighbor = null;
                if (bar.startPoint == current) neighbor = bar.endPoint;
                else if (bar.endPoint == current) neighbor = bar.startPoint;

                if (neighbor != null && structurallySupportedPoints.Add(neighbor))
                    searchQueue.Enqueue(neighbor);
            }
        }

        int releasedJointCount = 0;
        foreach (Point point in activePoints)
        {
            if (point == null || structurallySupportedPoints.Contains(point)) continue;

            Rigidbody nodeBody = point.GetComponent<Rigidbody>();
            if (nodeBody == null) continue;

            foreach (Bar road in point.ConnectedBars)
            {
                if (road == null || !activeBarSet.Contains(road) || !road.gameObject.activeSelf ||
                    road.materialData == null || !road.materialData.isRoad)
                    continue;

                foreach (Joint joint in road.GetComponents<Joint>())
                {
                    if (joint == null || joint.connectedBody != nodeBody) continue;

                    // Disconnect immediately; Destroy removes the component safely
                    // at the end of the frame before the controlled physics release.
                    joint.connectedBody = null;
                    Destroy(joint);
                    releasedJointCount++;
                }
            }
        }

        if (releasedJointCount > 0)
        {
            Debug.Log(
                $"[BridgePhysicsManager] Released {releasedJointCount} unsupported road joint(s). " +
                "Road joints require a structural load path to an anchor.", this);
        }
    }

    private void AttachJoint(GameObject barObj, Rigidbody targetRb, BridgeMaterialSO mat, Vector3 anchorWorldPosition)
    {
        Rigidbody barRb = barObj != null ? barObj.GetComponent<Rigidbody>() : null;
        if (barRb == null || targetRb == null || mat == null)
            return;

        int jointCount = mat.isDualBeam ? 2 : 1;

        for (int i = 0; i < jointCount; i++)
        {
            float zOffsetValue = mat.isDualBeam ? ((i == 0) ? mat.zOffset : -mat.zOffset) : 0f;
            Vector3 finalAnchorWorld = anchorWorldPosition + new Vector3(0, 0, zOffsetValue);

            if (mat.useSpring)
            {
                SpringJoint spring = barObj.AddComponent<SpringJoint>();
                spring.connectedBody = targetRb;
                spring.enablePreprocessing = false; 
                ConfigureJointAnchors(spring, barRb, targetRb, finalAnchorWorld);
                spring.spring = mat.spring;
                spring.damper = mat.damper;
                spring.minDistance = 0f;
                spring.maxDistance = 0f;
            }
            else
            {
                HingeJoint hinge = barObj.AddComponent<HingeJoint>();
                hinge.connectedBody = targetRb;
                hinge.enablePreprocessing = false; 
                ConfigureJointAnchors(hinge, barRb, targetRb, finalAnchorWorld);
                hinge.axis = barObj.transform.InverseTransformDirection(Vector3.forward).normalized;
            }
        }
    }

    private static void ConfigureJointAnchors(
        Joint joint,
        Rigidbody barBody,
        Rigidbody nodeBody,
        Vector3 worldAnchor)
    {
        joint.autoConfigureConnectedAnchor = false;
        joint.enableCollision = false;
        joint.breakForce = Mathf.Infinity;
        joint.breakTorque = Mathf.Infinity;

        // Both local anchors are derived from the exact same world point. Their
        // world positions therefore coincide before either body is released.
        joint.anchor = barBody.transform.InverseTransformPoint(worldAnchor);
        joint.connectedAnchor = nodeBody.transform.InverseTransformPoint(worldAnchor);
    }

    private void ResolveAdjacentCollisions(List<Bar> activeBars)
    {
        List<Collider> bridgeCols = new List<Collider>();
        foreach(Bar b in activeBars)
        {
            bridgeCols.AddRange(b.GetComponentsInChildren<Collider>());
        }

        for (int i = 0; i < bridgeCols.Count; i++)
        {
            for (int j = i + 1; j < bridgeCols.Count; j++)
            {
                Physics.IgnoreCollision(bridgeCols[i], bridgeCols[j], true);
            }
        }
    }
}
// Note: BarStressHandler remains exactly the same and has been omitted here to save context space, 
// as it was fully provided and correctly updated in our previous message!

public class BarStressHandler : MonoBehaviour
{
    private BridgePhysicsManager manager; 
    private BridgeMaterialSO material;
    private Point p1;
    private Point p2;
    private Bar myBar;
    
    private float restLength;
    private Joint[] joints; 
    private SpringJoint ropeJoint; 
    
    [HideInInspector] public bool isBroken = false;
    [HideInInspector] public float currentStressPercent = 0f;
    [HideInInspector] public float currentStructuralStressPercent = 0f;
    
    private float smoothedForce = 0f;
    private float settledDeadLoadForce = 0f;
    private bool canTrackStress = false; 
    private bool isCurrentlyInTension;

    private Queue<float> forceHistory = new Queue<float>();
    private int smoothingFrames = 10;

    private Renderer[] childRenderers;
    private Color[] originalColors;

    public void Setup(BridgeMaterialSO mat, Point point1, Point point2)
    {
        manager = FindObjectOfType<BridgePhysicsManager>(); 
        material = mat;
        p1 = point1;
        p2 = point2;
        myBar = GetComponent<Bar>();
        smoothingFrames = manager != null ? Mathf.Max(1, manager.stressSmoothingFrames) : 10;
        
        restLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        isCurrentlyInTension = false;

        childRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[childRenderers.Length];
        
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i].material.HasProperty("_Color"))
                originalColors[i] = childRenderers[i].material.color;
            else if (childRenderers[i].material.HasProperty("_BaseColor"))
                originalColors[i] = childRenderers[i].material.GetColor("_BaseColor");
            else
                originalColors[i] = Color.white;
        }
    }

    public void SetRopeJoint(SpringJoint joint)
    {
        ropeJoint = joint;
    }

    public void BeginTracking()
    {
        canTrackStress = true;
        CacheJointsIfNeeded();

        // The bridge has already settled for BridgePhysicsManager.settleFramesAmount
        // fixed steps. Capture that force separately so the UI can show only the
        // vehicle's added live load while failure checks retain the total load.
        settledDeadLoadForce = ReadCurrentForce();
        smoothedForce = settledDeadLoadForce;
        currentStressPercent = 0f;
        currentStructuralStressPercent = 0f;
        
        forceHistory.Clear();
        for (int i = 0; i < smoothingFrames; i++)
            forceHistory.Enqueue(settledDeadLoadForce);
    }

    private void OnDestroy()
    {
        if (childRenderers == null) return;
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i] != null) SetBarColor(originalColors[i], i);
        }
    }

    public void EvaluateStress()
    {
        if (!canTrackStress || isBroken || p1 == null || p2 == null) return;

        if (material.isRope && myBar != null)
        {
            myBar.StartPosition = p1.transform.position;
            myBar.UpdateCreatingBar(p2.transform.position);
        }

        CacheJointsIfNeeded();
        if (!material.isRope && (joints == null || joints.Length == 0)) return;

        float currentLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        float lengthDelta = currentLength - restLength;
        float directionDeadZone = manager != null ? manager.stressDirectionDeadZone : 0.0005f;

        // Retain the previous state inside the dead zone. Without hysteresis,
        // microscopic solver jitter can swap dissimilar tension/compression limits.
        if (lengthDelta > directionDeadZone) isCurrentlyInTension = true;
        else if (lengthDelta < -directionDeadZone) isCurrentlyInTension = false;

        bool isTension = isCurrentlyInTension;
        float maxForceThisFrame = ReadCurrentForce();
        
        Joint breakingJoint = null;
        string breakCause = "";

        forceHistory.Enqueue(maxForceThisFrame);
        if (forceHistory.Count > smoothingFrames) forceHistory.Dequeue();

        float totalForce = 0f;
        foreach (float f in forceHistory) totalForce += f;
        
        float averagedForce = totalForce / forceHistory.Count;

        float relativeTolerance = manager != null ? manager.deadLoadReturnTolerance : 0.02f;
        float minimumTolerance = manager != null ? manager.deadLoadReturnToleranceNewtons : 1f;
        float returnTolerance = Mathf.Max(minimumTolerance, settledDeadLoadForce * relativeTolerance);

        // PhysX resting contacts can fluctuate slightly forever. Snap only values
        // already within a narrow band of the calibrated dead load; real residual
        // deformation or oscillation is deliberately not hidden.
        smoothedForce = Mathf.Abs(averagedForce - settledDeadLoadForce) <= returnTolerance
            ? settledDeadLoadForce
            : averagedForce;

        if (material.isRope)
        {
            if (smoothedForce > material.maxTension)
            {
                breakingJoint = ropeJoint;
                breakCause = "Tension (Rope Snapped)";
            }
        }
        else
        {
            if (isTension && smoothedForce > material.maxTension)
            {
                breakingJoint = joints[0]; 
                breakCause = "Tension (Pulled apart)";
            }
            else if (!isTension && smoothedForce > material.maxCompression)
            {
                breakingJoint = joints[0];
                breakCause = "Compression (Buckled)";
            }
        }

        float stressLimit = isTension ? material.maxTension : material.maxCompression;
        if (stressLimit <= 0f) stressLimit = 1f; 

        float totalStructuralForce = material.isRope && !isTension ? 0f : smoothedForce;
        currentStructuralStressPercent =
            Mathf.Round((totalStructuralForce / stressLimit) * 100f) / 100f;

        if (material.isRope && !isTension) 
        {
            currentStressPercent = 0f; 
        }
        else 
        {
            float displayedForce = manager != null && manager.displayLiveLoadStressOnly
                ? Mathf.Max(0f, smoothedForce - settledDeadLoadForce)
                : smoothedForce;
            float rawPercent = displayedForce / stressLimit;
            currentStressPercent = Mathf.Round(rawPercent * 100f) / 100f;
        }

        if (manager != null && manager.enableVisualizer)
        {
            UpdateStressVisuals();
        }

        if (breakingJoint != null && !isBroken && !BridgePhysicsManager.DebugInvincibleBridge)
        {
            BreakBar(breakCause, smoothedForce, breakingJoint);
        }
    }

    private void CacheJointsIfNeeded()
    {
        if (material != null && !material.isRope && (joints == null || joints.Length == 0))
            joints = GetComponents<Joint>();
    }

    private float ReadCurrentForce()
    {
        if (material == null) return 0f;

        if (material.isRope)
            return ropeJoint != null ? ropeJoint.currentForce.magnitude : 0f;

        float maximumForce = 0f;
        if (joints == null) return maximumForce;

        foreach (Joint joint in joints)
        {
            if (joint == null) continue;
            maximumForce = Mathf.Max(maximumForce, joint.currentForce.magnitude);
        }

        return maximumForce;
    }

    private void UpdateStressVisuals()
    {
        if (childRenderers == null || childRenderers.Length == 0) return;

        for (int i = 0; i < childRenderers.Length; i++)
        {
            Color stressColor;

            if (currentStressPercent < 0.5f)
            {
                stressColor = Color.Lerp(originalColors[i], manager.warningColor, currentStressPercent * 2f);
            }
            else
            {
                stressColor = Color.Lerp(manager.warningColor, manager.criticalColor, (currentStressPercent - 0.5f) * 2f);
            }

            SetBarColor(stressColor, i);
        }
    }

    private void BreakBar(string cause, float force, Joint brokenJoint)
    {
        if (isBroken) return;
        isBroken = true;
        currentStressPercent = 1f;
        currentStructuralStressPercent = 1f;

        if (brokenJoint != null) Destroy(brokenJoint);
        
        for (int i = 0; i < childRenderers.Length; i++) SetBarColor(manager.brokenColor, i);
        
        if (material.isRope && myBar != null)
        {
            myBar.StartPosition = p1.transform.position;
            myBar.UpdateCreatingBar(p1.transform.position + (Vector3.down * restLength));
        }
    }

    private void SetBarColor(Color targetColor, int index)
    {
        if (childRenderers[index] == null) return;

        if (childRenderers[index].material.HasProperty("_Color"))
        {
            childRenderers[index].material.color = targetColor;
        }
        else if (childRenderers[index].material.HasProperty("_BaseColor"))
        {
            childRenderers[index].material.SetColor("_BaseColor", targetColor);
        }
    }
}
