using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns pooled tumbleweeds only inside explicitly assigned areas that are
/// currently inside the gameplay camera frustum and close to the player.
/// </summary>
[DisallowMultipleComponent]
public sealed class TumbleweedSpawner : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private GameObject tumbleweedPrefab;
    [Tooltip("Only these authored areas may spawn tumbleweeds. Empty means spawning is disabled.")]
    [SerializeField] private List<TumbleweedSpawnArea> spawnAreas = new List<TumbleweedSpawnArea>();

    [Header("Visibility")]
    [Tooltip("Uses Camera.main when empty.")]
    [SerializeField] private Camera visibilityCamera;
    [Tooltip("Uses the Player-tagged object when empty.")]
    [SerializeField] private Transform player;
    [Min(0f)] [SerializeField] private float maximumPlayerDistance = 65f;
    [Range(0f, 0.45f)] [SerializeField] private float viewportPadding = 0.03f;
    [Min(1)] [SerializeField] private int groundPointAttempts = 10;
    [Tooltip("Do not create ambience while building, testing cargo, paused, or viewing menus.")]
    [SerializeField] private bool onlyDuringNormalGameplay = true;

    [Header("Interval and Pool")]
    [Min(0.1f)] [SerializeField] private float minimumSpawnInterval = 2f;
    [Min(0.1f)] [SerializeField] private float maximumSpawnInterval = 4f;
    [Min(1)] [SerializeField] private int maximumActiveTumbleweeds = 8;

    [Header("Ground")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [Min(0.1f)] [SerializeField] private float groundProbeHeight = 3f;
    [Min(0.1f)] [SerializeField] private float groundProbeDepth = 8f;
    [SerializeField] private float groundOffset = 0.2f;
    [Min(0.1f)] [SerializeField] private float groundFollowSpeed = 8f;

    [Header("Motion Variation")]
    [SerializeField] private Vector2 speedRange = new Vector2(1.5f, 3f);
    [SerializeField] private Vector2 rollDegreesPerSecondRange = new Vector2(160f, 280f);
    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    [SerializeField] private Vector2 lifetimeRange = new Vector2(8f, 16f);
    [Min(0f)] [SerializeField] private float offscreenRecycleDelay = 1.25f;

    private readonly List<TumbleweedSpawnArea> eligibleAreas = new List<TumbleweedSpawnArea>();
    private readonly List<TumbleweedActor> activeActors = new List<TumbleweedActor>();
    private readonly Queue<TumbleweedActor> pooledActors = new Queue<TumbleweedActor>();
    private float nextSpawnTime;
    private float nextReferenceRefreshTime;

    private void OnEnable()
    {
        ScheduleNextSpawn();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSpawnTime) return;
        ScheduleNextSpawn();

        if (!CanSpawnNow()) return;
        TrySpawnVisibleTumbleweed();
    }

    private bool CanSpawnNow()
    {
        if (tumbleweedPrefab == null || activeActors.Count >= maximumActiveTumbleweeds ||
            spawnAreas == null || spawnAreas.Count == 0 || Time.timeScale <= 0f)
        {
            return false;
        }

        if (onlyDuringNormalGameplay && GameManager.Instance != null &&
            GameManager.Instance.CurrentState != GameManager.GameState.Normal)
        {
            return false;
        }

        RefreshReferencesIfNeeded();
        return visibilityCamera != null && visibilityCamera.isActiveAndEnabled && player != null;
    }

    private void RefreshReferencesIfNeeded()
    {
        if (Time.unscaledTime < nextReferenceRefreshTime &&
            visibilityCamera != null && player != null) return;

        nextReferenceRefreshTime = Time.unscaledTime + 2f;
        if (visibilityCamera == null || !visibilityCamera.isActiveAndEnabled)
            visibilityCamera = Camera.main;
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }
    }

    private void TrySpawnVisibleTumbleweed()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(visibilityCamera);
        eligibleAreas.Clear();
        float totalWeight = 0f;

        foreach (TumbleweedSpawnArea area in spawnAreas)
        {
            if (area == null ||
                !area.IsEligible(planes, player.position, maximumPlayerDistance)) continue;
            eligibleAreas.Add(area);
            totalWeight += area.SpawnWeight;
        }

        while (eligibleAreas.Count > 0)
        {
            TumbleweedSpawnArea area = ChooseWeightedArea(totalWeight);
            if (area == null) return;
            if (area.TryGetVisibleGroundPoint(
                    visibilityCamera,
                    groundLayers,
                    groundProbeHeight,
                    groundProbeDepth,
                    viewportPadding,
                    groundPointAttempts,
                    out Vector3 spawnPoint))
            {
                Spawn(area, spawnPoint);
                return;
            }

            totalWeight -= area.SpawnWeight;
            eligibleAreas.Remove(area);
        }
    }

    private TumbleweedSpawnArea ChooseWeightedArea(float totalWeight)
    {
        if (eligibleAreas.Count == 0 || totalWeight <= 0f) return null;
        float choice = Random.Range(0f, totalWeight);
        foreach (TumbleweedSpawnArea area in eligibleAreas)
        {
            choice -= area.SpawnWeight;
            if (choice <= 0f) return area;
        }

        return eligibleAreas[eligibleAreas.Count - 1];
    }

    private void Spawn(TumbleweedSpawnArea area, Vector3 position)
    {
        TumbleweedActor actor = GetActor();
        if (actor == null) return;

        area.RegisterSpawn();
        activeActors.Add(actor);
        actor.Launch(
            this,
            area,
            visibilityCamera,
            position,
            area.GetTravelDirection(),
            groundLayers,
            RandomRange(speedRange, 0.01f),
            RandomRange(rollDegreesPerSecondRange, 0f),
            RandomRange(scaleRange, 0.05f),
            RandomRange(lifetimeRange, 0.1f),
            groundOffset,
            groundProbeHeight,
            groundProbeDepth,
            groundFollowSpeed,
            offscreenRecycleDelay,
            maximumPlayerDistance > 0f ? maximumPlayerDistance * 1.25f : 0f);
    }

    private TumbleweedActor GetActor()
    {
        while (pooledActors.Count > 0)
        {
            TumbleweedActor pooled = pooledActors.Dequeue();
            if (pooled != null) return pooled;
        }

        GameObject instance = Instantiate(tumbleweedPrefab, transform);
        instance.name = tumbleweedPrefab.name + " (Pooled)";
        TumbleweedActor actor = instance.GetComponent<TumbleweedActor>();
        if (actor == null) actor = instance.AddComponent<TumbleweedActor>();
        instance.SetActive(false);
        return actor;
    }

    internal void Recycle(TumbleweedActor actor, TumbleweedSpawnArea area)
    {
        if (area != null) area.RegisterRecycle();
        if (actor == null) return;
        activeActors.Remove(actor);
        actor.gameObject.SetActive(false);
        actor.transform.SetParent(transform, true);
        pooledActors.Enqueue(actor);
    }

    private void OnDisable()
    {
        while (activeActors.Count > 0)
        {
            int lastIndex = activeActors.Count - 1;
            TumbleweedActor actor = activeActors[lastIndex];
            if (actor == null)
            {
                activeActors.RemoveAt(lastIndex);
                continue;
            }

            Recycle(actor, actor.SourceArea);
        }
    }

    private void ScheduleNextSpawn()
    {
        float minimum = Mathf.Max(0.1f, Mathf.Min(minimumSpawnInterval, maximumSpawnInterval));
        float maximum = Mathf.Max(minimum, Mathf.Max(minimumSpawnInterval, maximumSpawnInterval));
        nextSpawnTime = Time.unscaledTime + Random.Range(minimum, maximum);
    }

    private static float RandomRange(Vector2 range, float minimum)
    {
        float low = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
        float high = Mathf.Max(low, Mathf.Max(range.x, range.y));
        return Random.Range(low, high);
    }

    private void OnValidate()
    {
        minimumSpawnInterval = Mathf.Max(0.1f, minimumSpawnInterval);
        maximumSpawnInterval = Mathf.Max(minimumSpawnInterval, maximumSpawnInterval);
        maximumActiveTumbleweeds = Mathf.Max(1, maximumActiveTumbleweeds);
        groundPointAttempts = Mathf.Max(1, groundPointAttempts);
    }
}
