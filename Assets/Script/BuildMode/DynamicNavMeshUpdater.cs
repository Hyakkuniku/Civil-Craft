using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Queues asynchronous updates of an existing NavMeshSurface after a player-built
/// bridge is finalized, loaded, replaced, or deleted.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshSurface))]
public sealed class DynamicNavMeshUpdater : MonoBehaviour
{
    public static DynamicNavMeshUpdater Instance { get; private set; }

    [Header("Surface")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [Tooltip("A surface should normally be baked in the Editor. Enabling this fallback performs a synchronous first build and may hitch.")]
    [SerializeField] private bool buildSynchronouslyIfDataIsMissing;
    [Tooltip("Uses the bridge's simple BoxColliders instead of detailed render meshes, preventing holes and jagged decorative outlines.")]
    [SerializeField] private bool forcePhysicsColliderGeometry = true;

    [Header("Bridge Geometry")]
    [Tooltip("Layer included by the NavMeshSurface for finalized road bars.")]
    [SerializeField] private string walkableBridgeLayer = "Bridge";
    [Tooltip("Automatically marks road bars walkable and excludes trusses/piers.")]
    [SerializeField] private bool configureBakedBars = true;
    [Tooltip("Also update after saved baked bridges finish loading at scene start.")]
    [SerializeField] private bool updateSavedBridgesOnStart = true;
    [Tooltip("Minimum thickness of the simple collider used as the NavMesh road surface.")]
    [Min(0.02f)] [SerializeField] private float minimumRoadColliderThickness = 0.12f;
    [Tooltip("Extra length added at both ends collectively so adjacent road bars overlap instead of forming voxel gaps.")]
    [Min(0f)] [SerializeField] private float roadColliderSeamOverlap = 0.2f;
    [Tooltip("Minimum walkable width. This should remain comfortably wider than twice the baked agent radius.")]
    [Min(0.1f)] [SerializeField] private float minimumRoadColliderWidth = 2f;

    [Header("Diagnostics")]
    [SerializeField] private bool validateRoadCoverageAfterUpdate = true;
    [Min(0.05f)] [SerializeField] private float roadProbeDistance = 0.5f;

    [Header("Update Scheduling")]
    [Tooltip("Small realtime delay coalesces several bake/delete requests into one update.")]
    [Min(0f)] [SerializeField] private float updateDelay = 0.1f;

    [Header("Events")]
    public UnityEvent onNavMeshUpdateStarted;
    public UnityEvent onNavMeshUpdateCompleted;
    public UnityEvent onNavMeshUpdateFailed;

    private Coroutine updateRoutine;
    private bool updateRequested;
    private bool asyncUpdateInProgress;

    public bool IsUpdating => asyncUpdateInProgress;
    public bool HasPendingOrRunningUpdate => updateRequested || updateRoutine != null || asyncUpdateInProgress;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DynamicNavMeshUpdater] Duplicate updater disabled.", this);
            enabled = false;
            return;
        }

        Instance = this;
        if (navMeshSurface == null) navMeshSurface = GetComponent<NavMeshSurface>();
        if (forcePhysicsColliderGeometry && navMeshSurface != null)
            navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    }

    private IEnumerator Start()
    {
        if (!updateSavedBridgesOnStart) yield break;

        // BuildLocation.Start loads saved bridge instances. Waiting one frame makes
        // sure their bars and colliders exist before source collection begins.
        yield return null;

        bool foundBakedBridge = false;
        BuildLocation[] locations = Resources.FindObjectsOfTypeAll<BuildLocation>();
        foreach (BuildLocation location in locations)
        {
            if (!IsLoadedSceneObject(location) || location.bakedBars == null ||
                location.bakedBars.Count == 0) continue;

            foundBakedBridge = true;
            PrepareBakedBridge(location);
        }

        if (foundBakedBridge) RequestUpdate();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Inspector-callable update that scans all currently loaded BuildLocations.
    /// </summary>
    public void UpdateWalkableNavMesh()
    {
        BuildLocation[] locations = Resources.FindObjectsOfTypeAll<BuildLocation>();
        foreach (BuildLocation location in locations)
        {
            if (!IsLoadedSceneObject(location)) continue;
            PrepareBakedBridge(location);
        }

        RequestUpdate();
    }

    /// <summary>Efficient code path when the BuildLocation is already known.</summary>
    public void UpdateWalkableNavMeshForLocation(BuildLocation buildLocation)
    {
        PrepareBakedBridge(buildLocation);
        RequestUpdate();
    }

    private void PrepareBakedBridge(BuildLocation buildLocation)
    {
        if (!configureBakedBars || buildLocation == null || buildLocation.bakedBars == null) return;

        int bridgeLayer = LayerMask.NameToLayer(walkableBridgeLayer);
        if (bridgeLayer < 0)
        {
            Debug.LogError($"[DynamicNavMeshUpdater] Layer '{walkableBridgeLayer}' does not exist.", this);
            return;
        }

        int walkableArea = NavMesh.GetAreaFromName("Walkable");
        if (walkableArea < 0) walkableArea = 0;

        foreach (Bar bar in buildLocation.bakedBars)
        {
            if (bar == null || bar.materialData == null) continue;

            bool isWalkableRoad = bar.materialData.isRoad;
            NavMeshModifier modifier = bar.GetComponent<NavMeshModifier>();
            if (modifier == null) modifier = bar.gameObject.AddComponent<NavMeshModifier>();

            modifier.applyToChildren = true;
            modifier.ignoreFromBuild = !isWalkableRoad;
            modifier.overrideArea = isWalkableRoad;
            if (isWalkableRoad) modifier.area = walkableArea;

            if (!isWalkableRoad) continue;

            SetLayerRecursively(bar.transform, bridgeLayer);
            EnsureContinuousRoadCollider(bar);

            if (bar.GetComponentInChildren<Collider>(true) == null)
            {
                Debug.LogWarning(
                    $"[DynamicNavMeshUpdater] Walkable road '{bar.name}' has no collider. " +
                    "Bake with Physics Colliders enabled before updating the NavMesh.",
                    bar);
            }
        }

        Physics.SyncTransforms();
    }

    private void RequestUpdate()
    {
        if (!isActiveAndEnabled) return;

        updateRequested = true;
        if (updateRoutine == null) updateRoutine = StartCoroutine(ProcessUpdateRequests());
    }

    private IEnumerator ProcessUpdateRequests()
    {
        while (updateRequested)
        {
            updateRequested = false;

            if (updateDelay > 0f)
                yield return new WaitForSecondsRealtime(updateDelay);
            else
                yield return null;

            if (navMeshSurface == null)
            {
                Debug.LogError("[DynamicNavMeshUpdater] NavMeshSurface is not assigned.", this);
                onNavMeshUpdateFailed?.Invoke();
                continue;
            }

            if (navMeshSurface.navMeshData == null)
            {
                if (!buildSynchronouslyIfDataIsMissing)
                {
                    Debug.LogError(
                        "[DynamicNavMeshUpdater] The NavMeshSurface has no baked data. " +
                        "Bake it once in the Editor, or enable the synchronous fallback.",
                        navMeshSurface);
                    onNavMeshUpdateFailed?.Invoke();
                    continue;
                }

                onNavMeshUpdateStarted?.Invoke();
                navMeshSurface.BuildNavMesh();
                if (navMeshSurface.navMeshData == null)
                {
                    onNavMeshUpdateFailed?.Invoke();
                    continue;
                }

                if (validateRoadCoverageAfterUpdate) ValidateRoadCoverage();
                onNavMeshUpdateCompleted?.Invoke();
                continue;
            }

            onNavMeshUpdateStarted?.Invoke();
            asyncUpdateInProgress = true;
            AsyncOperation operation = navMeshSurface.UpdateNavMesh(navMeshSurface.navMeshData);

            if (operation == null)
            {
                asyncUpdateInProgress = false;
                onNavMeshUpdateFailed?.Invoke();
                continue;
            }

            yield return operation;
            asyncUpdateInProgress = false;
            if (validateRoadCoverageAfterUpdate) ValidateRoadCoverage();
            onNavMeshUpdateCompleted?.Invoke();
        }

        updateRoutine = null;
    }

    private static bool IsLoadedSceneObject(Component component)
    {
        return component != null && component.gameObject.scene.IsValid() &&
               component.gameObject.scene.isLoaded;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private void EnsureContinuousRoadCollider(Bar bar)
    {
        if (bar == null || bar.startPoint == null || bar.endPoint == null) return;

        BoxCollider[] colliders = bar.GetComponents<BoxCollider>();
        BoxCollider surfaceCollider = colliders.Length > 0
            ? colliders[0]
            : bar.gameObject.AddComponent<BoxCollider>();

        float length = Vector3.Distance(
            bar.startPoint.transform.position,
            bar.endPoint.transform.position);

        Vector3 size = surfaceCollider.size;
        Vector3 center = surfaceCollider.center;
        float originalTop = center.y + size.y * 0.5f;
        size.x = Mathf.Max(0.05f, length + roadColliderSeamOverlap);
        size.y = Mathf.Max(size.y, minimumRoadColliderThickness);
        size.z = Mathf.Max(size.z, minimumRoadColliderWidth);
        surfaceCollider.size = size;

        // Overlap both neighboring ends equally while preserving the original
        // top surface height, so the generated NavMesh does not float upward.
        center.x = 0f;
        center.y = originalTop - size.y * 0.5f;
        center.z = 0f;
        surfaceCollider.center = center;
        surfaceCollider.enabled = true;
        surfaceCollider.isTrigger = false;
    }

    private void ValidateRoadCoverage()
    {
        int bridgeLayer = LayerMask.NameToLayer(walkableBridgeLayer);
        int areaMask = NavMesh.AllAreas;
        int failedProbeCount = 0;

        BuildLocation[] locations = Resources.FindObjectsOfTypeAll<BuildLocation>();
        foreach (BuildLocation location in locations)
        {
            if (!IsLoadedSceneObject(location) || location.bakedBars == null) continue;

            foreach (Bar bar in location.bakedBars)
            {
                if (bar == null || bar.materialData == null || !bar.materialData.isRoad ||
                    bar.startPoint == null || bar.endPoint == null) continue;

                if (bridgeLayer >= 0 && bar.gameObject.layer != bridgeLayer) continue;

                Vector3 start = bar.startPoint.transform.position;
                Vector3 end = bar.endPoint.transform.position;
                Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);

                if (!ProbeRoadPoint(start, areaMask) ||
                    !ProbeRoadPoint(midpoint, areaMask) ||
                    !ProbeRoadPoint(end, areaMask))
                {
                    failedProbeCount++;
                    Debug.LogWarning(
                        $"[DynamicNavMeshUpdater] NavMesh does not fully cover road bar '{bar.name}' at " +
                        $"build location '{location.name}'. Check Surface volume, voxel size, slope, and layer mask.",
                        bar);
                }
            }
        }

        if (failedProbeCount == 0)
            Debug.Log("[DynamicNavMeshUpdater] Runtime bridge NavMesh coverage probes passed.", this);
    }

    private bool ProbeRoadPoint(Vector3 worldPoint, int areaMask)
    {
        if (!NavMesh.SamplePosition(worldPoint, out NavMeshHit hit, roadProbeDistance, areaMask))
            return false;

        return Mathf.Abs(hit.position.y - worldPoint.y) <= roadProbeDistance;
    }
}
