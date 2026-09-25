using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>A Fusion proxy for the existing locally controlled scene Player.</summary>
public sealed class FusionMultiplayerAvatar : NetworkBehaviour
{
    private const float PoseSendInterval = 1f / 15f;
    private const float AppearanceCheckInterval = 0.5f;
    private const float BridgeNodeSyncInterval = 0.2f;
    private const float LiveLoadSyncInterval = 0.05f;
    private const int MaxBridgeNodes = 64;
    private const int MaxBridgeBars = 96;
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");
    private static readonly int SprintParameter = Animator.StringToHash("IsSprinting");
    private static readonly int GroundedParameter = Animator.StringToHash("IsGrounded");
    private static readonly int JumpParameter = Animator.StringToHash("Jump");

    [Networked] private Vector3 Position { get; set; }
    [Networked] private Quaternion Rotation { get; set; }
    [Networked] private Vector3 Scale { get; set; }
    [Networked] private bool PoseReady { get; set; }
    [Networked] private float MoveSpeed { get; set; }
    [Networked] private bool Sprinting { get; set; }
    [Networked] private bool Grounded { get; set; }
    [Networked] private uint JumpSequence { get; set; }
    [Networked, Capacity(2048)] private string Appearance { get; set; }
    [Networked] private bool IsBridgeBuilder { get; set; }
    [Networked] private int HostBridgeNodeCount { get; set; }
    [Networked, Capacity(MaxBridgeNodes)] private NetworkArray<Vector3> HostBridgeNodes => default;
    [Networked] private int HostBridgeBarCount { get; set; }
    [Networked, Capacity(MaxBridgeBars)] private NetworkArray<BridgeBarSnapshot> HostBridgeBars => default;
    [Networked] private bool HostLiveLoadVisible { get; set; }
    [Networked] private bool HostLiveLoadSimulating { get; set; }
    [Networked] private int HostLiveLoadContractHash { get; set; }
    [Networked] private int HostLiveLoadNameHash { get; set; }
    [Networked] private Vector3 HostLiveLoadPosition { get; set; }
    [Networked] private Quaternion HostLiveLoadRotation { get; set; }
    [Networked] private SessionCompletionSnapshot HostCompletion { get; set; }
    [Networked] private int HostCompletionRevision { get; set; }

    public struct BridgeBarSnapshot : INetworkStruct
    {
        public Vector3 Start;
        public Vector3 End;
        public int MaterialHash;
        public int ContractHash;
        public int Committed;
        public Vector3 RoadColliderSize;
        public Vector3 RoadColliderCenter;
    }

    public struct SessionCompletionSnapshot : INetworkStruct
    {
        public int Visible;
        public int ContractHash;
        public int StarFlags;
        public float Cost;
        public float Budget;
        public float PeakStress;
        public float CostTarget;
        public float StressTarget;
    }

    private PlayerMotor sceneMotor;
    private PlayerCosmetics sceneCosmetics;
    private Animator sceneAnimator;
    private GameObject visualContainer;
    private Animator remoteAnimator;
    private List<CosmeticModelBinding> remoteBindings;
    private List<CosmeticItem> remoteHats;
    private string lastPublishedAppearance;
    private string lastAppliedAppearance;
    private uint localJumpSequence;
    private uint lastObservedJumpSequence;
    private float nextPoseSendTime;
    private float nextAppearanceCheckTime;
    private bool localSpawnAdjusted;
    private float nextBridgeNodeSyncTime;
    private float nextLiveLoadSyncTime;
    private GameObject remoteBridgeNodeRoot;
    private Material remoteBridgeNodeMaterial;
    private Material remoteBridgeNodeAppearanceMaterial;
    private Vector3 remoteBridgeNodeScale = Vector3.one * 0.28f;
    private readonly List<Transform> remoteBridgeNodeMarkers = new List<Transform>();
    private GameObject remoteBridgeBarRoot;
    private readonly List<BridgeBarVisual> remoteBridgeBars = new List<BridgeBarVisual>();
    private Dictionary<int, BridgeMaterialSO> bridgeMaterialsByHash;
    private readonly HashSet<int> missingBridgeMaterialHashes = new HashSet<int>();
    private LiveLoadVehicle remoteLiveLoad;
    private bool remoteLiveLoadWasActive;
    private bool remoteLiveLoadBehaviourEnabled;
    private bool remoteLiveLoadInspectionAvailable;
    private Vector3 remoteLiveLoadOriginalPosition;
    private Quaternion remoteLiveLoadOriginalRotation;
    private Rigidbody[] remoteLiveLoadBodies;
    private bool[] remoteLiveLoadBodyWasKinematic;
    private Collider[] remoteLiveLoadColliders;
    private bool[] remoteLiveLoadColliderWasEnabled;
    private UnityEngine.AI.NavMeshObstacle[] remoteLiveLoadObstacles;
    private bool[] remoteLiveLoadObstacleWasEnabled;
    private SessionCompletionSnapshot pendingCompletion;
    private bool pendingCompletionDirty;
    private int lastObservedCompletionRevision;

    public override void Spawned()
    {
        if (HasInputAuthority)
            PlayerCosmetics.LoadoutChanged += PublishAppearance;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        PlayerCosmetics.LoadoutChanged -= PublishAppearance;
        BindSceneMotor(null);
        DestroyRemoteVisual();
        DestroyRemoteBridgeNodes();
        DestroyRemoteBridgeBars();
        RestoreRemoteLiveLoad();
        if (LevelCompleteManager.Instance != null)
            LevelCompleteManager.Instance.HideGuestSessionCompletion();
    }

    private void OnDestroy()
    {
        PlayerCosmetics.LoadoutChanged -= PublishAppearance;
        BindSceneMotor(null);
        DestroyRemoteBridgeNodes();
        DestroyRemoteBridgeBars();
        RestoreRemoteLiveLoad();
        if (LevelCompleteManager.Instance != null)
            LevelCompleteManager.Instance.HideGuestSessionCompletion();
    }

    private void Update()
    {
        if (Runner == null || !Runner.IsRunning) return;
        if (SceneManager.GetActiveScene().name != "Multiplayer")
        {
            BindSceneMotor(null);
            sceneCosmetics = null;
            sceneAnimator = null;
            localSpawnAdjusted = false;
            DestroyRemoteVisual();
            DestroyRemoteBridgeNodes();
            DestroyRemoteBridgeBars();
            RestoreRemoteLiveLoad();
            if (LevelCompleteManager.Instance != null)
                LevelCompleteManager.Instance.HideGuestSessionCompletion();
            return;
        }

        if (sceneMotor == null) FindScenePlayer();
        if (HasInputAuthority)
        {
            if (!localSpawnAdjusted) OffsetJoiningPlayerSpawn();
            if (Time.unscaledTime >= nextAppearanceCheckTime)
            {
                nextAppearanceCheckTime = Time.unscaledTime + AppearanceCheckInterval;
                if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
                    PublishAppearance(PlayerDataManager.Instance.GetCosmeticLoadoutCopy());
            }
            return;
        }

        UpdateRemoteBridgeNodes();
        UpdateRemoteBridgeBars();
        UpdateRemoteLiveLoad();
        UpdateRemoteCompletion();
        if (!PoseReady) return;
        UpdateRemotePose();
        if (visualContainer == null) CreateRemoteVisual();
        ApplyRemoteAppearance();
        ApplyRemoteAnimation();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasInputAuthority && HasStateAuthority && Runner.IsServer && pendingCompletionDirty)
        {
            HostCompletion = pendingCompletion;
            HostCompletionRevision++;
            pendingCompletionDirty = false;
        }

        if (HasInputAuthority && HasStateAuthority && Runner.IsServer &&
            SceneManager.GetActiveScene().name == "Multiplayer" &&
            Time.unscaledTime >= nextLiveLoadSyncTime)
        {
            nextLiveLoadSyncTime = Time.unscaledTime + LiveLoadSyncInterval;
            PublishHostLiveLoad();
        }

        if (HasInputAuthority && HasStateAuthority && Runner.IsServer &&
            SceneManager.GetActiveScene().name == "Multiplayer" &&
            Time.unscaledTime >= nextBridgeNodeSyncTime)
        {
            if (!IsBridgeBuilder) IsBridgeBuilder = true;
            nextBridgeNodeSyncTime = Time.unscaledTime + BridgeNodeSyncInterval;
            PublishHostBridgeNodes();
            PublishHostBridgeBars();
        }

        if (!HasInputAuthority || sceneMotor == null ||
            SceneManager.GetActiveScene().name != "Multiplayer" ||
            Time.unscaledTime < nextPoseSendTime)
            return;

        nextPoseSendTime = Time.unscaledTime + PoseSendInterval;
        Transform player = sceneMotor.transform;
        float speed = sceneAnimator != null ? sceneAnimator.GetFloat(SpeedParameter) : 0f;
        bool sprint = sceneAnimator != null && sceneAnimator.GetBool(SprintParameter);
        bool ground = sceneAnimator == null || sceneAnimator.GetBool(GroundedParameter);

        if (HasStateAuthority)
            SetPose(player.position, player.rotation, player.localScale,
                speed, sprint, ground, localJumpSequence);
        else
            RPC_PublishPose(player.position, player.rotation, player.localScale,
                speed, sprint, ground, localJumpSequence);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
    private void RPC_PublishPose(Vector3 newPosition, Quaternion newRotation, Vector3 newScale,
        float speed, bool sprint, bool ground, uint jump)
    {
        SetPose(newPosition, newRotation, newScale, speed, sprint, ground, jump);
    }

    private void SetPose(Vector3 newPosition, Quaternion newRotation, Vector3 newScale,
        float speed, bool sprint, bool ground, uint jump)
    {
        Position = newPosition;
        Rotation = newRotation;
        Scale = newScale;
        MoveSpeed = speed;
        Sprinting = sprint;
        Grounded = ground;
        JumpSequence = jump;
        PoseReady = true;
    }

    private void PublishHostBridgeNodes()
    {
        BuildLocation activeLocation = GameManager.Instance != null &&
            GameManager.Instance.CurrentState != GameManager.GameState.Normal
            ? GameManager.Instance.ActiveBuildLocation : null;
        BuildLocation[] locations = FindObjectsOfType<BuildLocation>();
        int count = 0;
        foreach (Point point in Point.AllPoints)
        {
            if (point == null || !point.gameObject.activeInHierarchy || point.IsScenePlacedAnchor)
                continue;

            bool belongsToVisibleBridge = false;
            foreach (BuildLocation location in locations)
            {
                if (location == null || point.OwnerLocation != location) continue;
                if (location == activeLocation || location.bakedPoints.Contains(point))
                    belongsToVisibleBridge = true;
            }
            if (!belongsToVisibleBridge) continue;

            bool hasPlacedBar = false;
            foreach (Bar bar in point.ConnectedBars)
                if (bar != null && bar.gameObject.activeInHierarchy)
                {
                    hasPlacedBar = true;
                    break;
                }
            if (!hasPlacedBar) continue;
            if (count == MaxBridgeNodes) break;

            Vector3 position = point.transform.position;
            if (HostBridgeNodes[count] != position)
                HostBridgeNodes.Set(count, position);
            count++;
        }

        if (HostBridgeNodeCount != count) HostBridgeNodeCount = count;
    }

    private void UpdateRemoteBridgeNodes()
    {
        // Only the host avatar publishes nodes. These are visual-only on guests:
        // no Point, Bar, collider, or build ownership is created locally.
        if (!IsBridgeBuilder)
        {
            DestroyRemoteBridgeNodes();
            return;
        }

        int count = Mathf.Clamp(HostBridgeNodeCount, 0, MaxBridgeNodes);
        if (count == 0)
        {
            if (remoteBridgeNodeRoot != null) remoteBridgeNodeRoot.SetActive(false);
            return;
        }

        if (remoteBridgeNodeRoot == null) CreateRemoteBridgeNodeRoot();
        remoteBridgeNodeRoot.SetActive(true);
        while (remoteBridgeNodeMarkers.Count < count)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Host Bridge Node";
            marker.layer = 2; // Ignore Raycast; this is not an editable bridge point.
            marker.transform.SetParent(remoteBridgeNodeRoot.transform, false);
            marker.transform.localScale = remoteBridgeNodeScale;
            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null) Destroy(markerCollider);
            Renderer markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = remoteBridgeNodeAppearanceMaterial;
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
            remoteBridgeNodeMarkers.Add(marker.transform);
        }

        for (int index = 0; index < remoteBridgeNodeMarkers.Count; index++)
        {
            Transform marker = remoteBridgeNodeMarkers[index];
            bool visible = index < count;
            if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
            if (visible) marker.position = HostBridgeNodes[index];
        }
    }

    private void CreateRemoteBridgeNodeRoot()
    {
        remoteBridgeNodeRoot = new GameObject("Opponent Bridge Nodes (Read Only)");
        BarCreator creator = FindObjectOfType<BarCreator>(true);
        Renderer authoredNode = creator != null && creator.pointToInstantiate != null
            ? creator.pointToInstantiate.GetComponentInChildren<Renderer>(true) : null;
        if (authoredNode != null && authoredNode.sharedMaterial != null)
        {
            remoteBridgeNodeAppearanceMaterial = authoredNode.sharedMaterial;
            remoteBridgeNodeScale = creator.pointToInstantiate.transform.localScale;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        if (shader == null) return;

        remoteBridgeNodeMaterial = new Material(shader);
        Color cyan = new Color32(54, 226, 240, 255);
        remoteBridgeNodeMaterial.color = cyan;
        if (remoteBridgeNodeMaterial.HasProperty("_BaseColor"))
            remoteBridgeNodeMaterial.SetColor("_BaseColor", cyan);
        remoteBridgeNodeAppearanceMaterial = remoteBridgeNodeMaterial;
    }

    private void DestroyRemoteBridgeNodes()
    {
        if (remoteBridgeNodeRoot != null) Destroy(remoteBridgeNodeRoot);
        if (remoteBridgeNodeMaterial != null) Destroy(remoteBridgeNodeMaterial);
        remoteBridgeNodeRoot = null;
        remoteBridgeNodeMaterial = null;
        remoteBridgeNodeAppearanceMaterial = null;
        remoteBridgeNodeScale = Vector3.one * 0.28f;
        remoteBridgeNodeMarkers.Clear();
    }

    public static int StableHash(string value)
    {
        // Material/contract identifiers must match across separately built clients.
        unchecked
        {
            uint hash = 2166136261;
            if (value != null)
                foreach (char character in value)
                {
                    hash = (hash ^ character) * 16777619;
                }
            return (int)hash;
        }
    }

    public static FusionMultiplayerAvatar FindLocalHostAvatar()
    {
        foreach (FusionMultiplayerAvatar avatar in FindObjectsOfType<FusionMultiplayerAvatar>())
            if (avatar != null && avatar.Runner != null && avatar.Runner.IsRunning &&
                avatar.Runner.IsServer && avatar.HasStateAuthority && avatar.HasInputAuthority)
                return avatar;
        return null;
    }

    public void QueueHostCompletion(SessionCompletionSnapshot completion)
    {
        if (Runner == null || !Runner.IsRunning || !Runner.IsServer ||
            !HasStateAuthority || !HasInputAuthority ||
            SceneManager.GetActiveScene().name != "Multiplayer") return;
        pendingCompletion = completion;
        pendingCompletionDirty = true;
    }

    private void UpdateRemoteCompletion()
    {
        if (!IsBridgeBuilder || HostCompletionRevision == lastObservedCompletionRevision) return;
        LevelCompleteManager completion = LevelCompleteManager.Instance;
        if (completion == null) return;

        lastObservedCompletionRevision = HostCompletionRevision;
        SessionCompletionSnapshot snapshot = HostCompletion;
        if (snapshot.Visible != 0) completion.ShowGuestSessionCompletion(snapshot, this);
        else completion.HideGuestSessionCompletion();
    }

    private void PublishHostBridgeBars()
    {
        BuildLocation activeLocation = GameManager.Instance != null &&
            GameManager.Instance.CurrentState != GameManager.GameState.Normal
            ? GameManager.Instance.ActiveBuildLocation : null;
        BuildLocation[] locations = FindObjectsOfType<BuildLocation>();
        List<Bar> candidates = new List<Bar>(FindObjectsOfType<Bar>());
        foreach (BuildLocation location in locations)
            if (location != null)
                foreach (Bar baked in location.bakedBars)
                    if (baked != null && !candidates.Contains(baked)) candidates.Add(baked);
        int count = 0;
        foreach (Bar bar in candidates)
        {
            if (bar == null || !bar.gameObject.activeInHierarchy ||
                bar.materialData == null || bar.startPoint == null || bar.endPoint == null)
                continue;

            bool committed = false;
            bool visible = false;
            int contractHash = 0;
            foreach (BuildLocation location in locations)
            {
                if (location == null || !location.Owns(bar)) continue;
                if (location.bakedBars.Contains(bar)) committed = true;
                if (location == activeLocation || committed)
                {
                    visible = true;
                    if (location.activeContract != null)
                        contractHash = StableHash(location.activeContract.ContractID);
                }
            }
            if (!visible) continue;
            if (count >= MaxBridgeBars) break;

            BoxCollider roadCollider = committed && bar.materialData.isRoad
                ? bar.GetComponent<BoxCollider>() : null;
            BridgeBarSnapshot snapshot = new BridgeBarSnapshot
            {
                Start = bar.startPoint.transform.position,
                End = bar.endPoint.transform.position,
                MaterialHash = StableHash(bar.materialData.Id),
                ContractHash = contractHash,
                Committed = committed ? 1 : 0,
                RoadColliderSize = roadCollider != null ? roadCollider.size : Vector3.zero,
                RoadColliderCenter = roadCollider != null ? roadCollider.center : Vector3.zero
            };
            BridgeBarSnapshot previous = HostBridgeBars[count];
            if (previous.Start != snapshot.Start || previous.End != snapshot.End ||
                previous.MaterialHash != snapshot.MaterialHash ||
                previous.ContractHash != snapshot.ContractHash ||
                previous.Committed != snapshot.Committed ||
                previous.RoadColliderSize != snapshot.RoadColliderSize ||
                previous.RoadColliderCenter != snapshot.RoadColliderCenter)
                HostBridgeBars.Set(count, snapshot);
            count++;
        }
        if (HostBridgeBarCount != count) HostBridgeBarCount = count;
    }

    private void UpdateRemoteBridgeBars()
    {
        if (!IsBridgeBuilder)
        {
            DestroyRemoteBridgeBars();
            return;
        }

        int count = Mathf.Clamp(HostBridgeBarCount, 0, MaxBridgeBars);
        if (count == 0)
        {
            if (remoteBridgeBarRoot != null) remoteBridgeBarRoot.SetActive(false);
            return;
        }

        if (remoteBridgeBarRoot == null)
            remoteBridgeBarRoot = new GameObject("Opponent Bridge (Read Only)");
        remoteBridgeBarRoot.SetActive(true);
        if (bridgeMaterialsByHash == null) LoadBridgeMaterials();

        while (remoteBridgeBars.Count < count) remoteBridgeBars.Add(null);
        for (int index = 0; index < remoteBridgeBars.Count; index++)
        {
            if (index >= count)
            {
                if (remoteBridgeBars[index] != null)
                    remoteBridgeBars[index].Root.SetActive(false);
                continue;
            }

            BridgeBarSnapshot snapshot = HostBridgeBars[index];
            BridgeBarVisual visual = remoteBridgeBars[index];
            if (visual == null || visual.MaterialHash != snapshot.MaterialHash)
            {
                if (visual != null) Destroy(visual.Root);
                visual = bridgeMaterialsByHash.TryGetValue(snapshot.MaterialHash, out BridgeMaterialSO material)
                    ? new BridgeBarVisual(material, snapshot.MaterialHash, remoteBridgeBarRoot.transform)
                    : null;
                if (visual == null && missingBridgeMaterialHashes.Add(snapshot.MaterialHash))
                    Debug.LogWarning("[Fusion] Guest could not find a bridge material used by the host. " +
                        "Check that both builds include the same Resources materials.", this);
                remoteBridgeBars[index] = visual;
            }
            if (visual == null) continue;
            visual.Root.SetActive(true);
            visual.SetEndpoints(snapshot.Start, snapshot.End);
            visual.SetRoadCollider(snapshot);
        }
    }

    private void LoadBridgeMaterials()
    {
        bridgeMaterialsByHash = new Dictionary<int, BridgeMaterialSO>();
        foreach (BridgeMaterialSO material in Resources.LoadAll<BridgeMaterialSO>(string.Empty))
            RegisterBridgeMaterial(material);

        // Some materials are referenced directly by a contract rather than
        // stored under Resources; both players have the same authored scene.
        foreach (BuildLocation location in FindObjectsOfType<BuildLocation>(true))
        {
            ContractSO contract = location != null ? location.activeContract : null;
            if (contract == null || contract.allowedMaterials == null) continue;
            foreach (MaterialAllowance allowance in contract.allowedMaterials)
                if (allowance != null) RegisterBridgeMaterial(allowance.material);
        }
    }

    private void RegisterBridgeMaterial(BridgeMaterialSO material)
    {
        if (material == null) return;
        int hash = StableHash(material.Id);
        if (!bridgeMaterialsByHash.ContainsKey(hash)) bridgeMaterialsByHash.Add(hash, material);
    }

    public int PopulateGuestReceiptRows(Transform parent, GameObject rowPrefab, int contractHash)
    {
        if (!IsBridgeBuilder || parent == null || rowPrefab == null) return 0;
        if (bridgeMaterialsByHash == null) LoadBridgeMaterials();
        Dictionary<BridgeMaterialSO, float> usage = new Dictionary<BridgeMaterialSO, float>();
        int count = Mathf.Clamp(HostBridgeBarCount, 0, MaxBridgeBars);
        for (int index = 0; index < count; index++)
        {
            BridgeBarSnapshot bar = HostBridgeBars[index];
            if (bar.ContractHash != contractHash ||
                !bridgeMaterialsByHash.TryGetValue(bar.MaterialHash, out BridgeMaterialSO material))
                continue;
            float length = Vector3.Distance(bar.Start, bar.End) *
                (material.isDualBeam ? 2f : 1f);
            usage[material] = usage.TryGetValue(material, out float previous)
                ? previous + length : length;
        }

        foreach (KeyValuePair<BridgeMaterialSO, float> entry in usage)
        {
            GameObject row = Instantiate(rowPrefab, parent);
            ReceiptRowUI receipt = row.GetComponent<ReceiptRowUI>();
            if (receipt != null) receipt.Setup(entry.Key, entry.Value);
        }
        return usage.Count;
    }

    private void DestroyRemoteBridgeBars()
    {
        if (remoteBridgeBarRoot != null) Destroy(remoteBridgeBarRoot);
        remoteBridgeBarRoot = null;
        remoteBridgeBars.Clear();
        bridgeMaterialsByHash = null;
        missingBridgeMaterialHashes.Clear();
    }

    private sealed class BridgeBarVisual
    {
        public readonly GameObject Root;
        public readonly int MaterialHash;
        private readonly BridgeMaterialSO material;
        private readonly List<Transform> segments = new List<Transform>();
        private readonly List<Vector3> segmentBaseScales = new List<Vector3>();
        private readonly Transform cap;
        private BoxCollider roadCollider;
        private readonly Vector3 capBaseScale;
        private readonly float capTopOffset;
        private readonly float capBottomOffset;
        private readonly float baseLength;

        public BridgeBarVisual(BridgeMaterialSO bridgeMaterial, int hash, Transform parent)
        {
            material = bridgeMaterial;
            MaterialHash = hash;
            Root = new GameObject("Host " + material.GetDisplayName());
            Root.layer = 2;
            Root.transform.SetParent(parent, false);

            int strandCount = material.isDualBeam ? 2 : 1;
            float measuredLength = 1f;
            for (int strand = 0; strand < strandCount; strand++)
            {
                GameObject segment = CreateMeshOnlyCopy(material.segmentPrefab,
                    Root.transform, "Bridge Segment");
                if (segment == null) continue;
                segment.transform.localPosition = new Vector3(0f, 0f,
                    material.isDualBeam ? (strand == 0 ? material.zOffset : -material.zOffset) : 0f);
                Renderer renderer = segment.GetComponentInChildren<Renderer>(true);
                if (renderer != null && strand == 0)
                    measuredLength = material.isPier ? renderer.bounds.size.y : renderer.bounds.size.x;
                segments.Add(segment.transform);
                segmentBaseScales.Add(segment.transform.localScale);
            }
            baseLength = Mathf.Max(0.01f, measuredLength);

            if (material.isPier && material.pierCapPrefab != null)
            {
                GameObject capObject = CreateMeshOnlyCopy(material.pierCapPrefab,
                    Root.transform, "Pier Cap");
                cap = capObject != null ? capObject.transform : null;
                capBaseScale = cap != null ? cap.localScale : Vector3.one;
                Renderer capRenderer = cap != null ? cap.GetComponentInChildren<Renderer>(true) : null;
                capTopOffset = capRenderer != null ? capRenderer.bounds.max.y : 0f;
                capBottomOffset = capRenderer != null ? capRenderer.bounds.min.y : 0f;
            }
        }

        public void SetEndpoints(Vector3 start, Vector3 end)
        {
            if (material.isPier ? start.y > end.y :
                start.x > end.x || (Mathf.Approximately(start.x, end.x) && start.y > end.y))
            {
                Vector3 swap = start;
                start = end;
                end = swap;
            }

            Vector3 direction = end - start;
            direction.z = 0f;
            float length = direction.magnitude;
            if (material.isPier)
            {
                float adjusted = Mathf.Max(0.05f, end.y - capTopOffset + capBottomOffset - start.y);
                Root.transform.SetPositionAndRotation(start + Vector3.up * (adjusted * 0.5f),
                    Quaternion.identity);
                for (int i = 0; i < segments.Count; i++)
                {
                    Vector3 original = segmentBaseScales[i];
                    segments[i].localScale = new Vector3(original.x,
                        original.y * adjusted / baseLength, original.z);
                }
                if (cap != null)
                {
                    cap.localScale = capBaseScale;
                    cap.position = new Vector3(end.x, end.y - capTopOffset, end.z);
                    cap.rotation = Quaternion.identity;
                }
            }
            else
            {
                Vector3 midpoint = (start + end) * 0.5f;
                midpoint.z = start.z;
                Root.transform.SetPositionAndRotation(midpoint,
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
                for (int i = 0; i < segments.Count; i++)
                {
                    Vector3 original = segmentBaseScales[i];
                    segments[i].localScale = new Vector3(original.x * length / baseLength,
                        original.y, original.z);
                }
            }
        }

        public void SetRoadCollider(BridgeBarSnapshot snapshot)
        {
            bool isCommittedRoad = material.isRoad && snapshot.Committed != 0 &&
                snapshot.RoadColliderSize.sqrMagnitude > 0f;
            if (isCommittedRoad && roadCollider == null)
                roadCollider = Root.AddComponent<BoxCollider>();
            if (roadCollider != null)
            {
                roadCollider.enabled = isCommittedRoad;
                if (isCommittedRoad)
                {
                    roadCollider.size = snapshot.RoadColliderSize;
                    roadCollider.center = snapshot.RoadColliderCenter;
                    roadCollider.isTrigger = false;
                }
            }
            int bridgeLayer = LayerMask.NameToLayer("Bridge");
            Root.layer = isCommittedRoad && bridgeLayer >= 0 ? bridgeLayer : 2;
        }

        private static GameObject CreateMeshOnlyCopy(GameObject source, Transform parent, string name)
        {
            if (source == null) return null;
            GameObject copy = new GameObject(name);
            copy.layer = 2;
            copy.transform.SetParent(parent, false);
            copy.transform.localRotation = source.transform.localRotation;
            copy.transform.localScale = source.transform.localScale;

            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            if (sourceFilter != null && sourceRenderer != null)
            {
                copy.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            }
            foreach (Transform child in source.transform)
            {
                GameObject part = UnityEngine.Object.Instantiate(child.gameObject, copy.transform, false);
                SetVisualOnly(part);
            }
            return copy;
        }

        private static void SetVisualOnly(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;
        }
    }

    private void PublishHostLiveLoad()
    {
        LiveLoadVehicle active = null;
        LiveLoadVehicle parkedFallback = null;
        foreach (LiveLoadVehicle vehicle in FindObjectsOfType<LiveLoadVehicle>())
        {
            if (vehicle == null || vehicle.gameObject.scene != gameObject.scene ||
                vehicle.assignedContract == null) continue;

            bool isCurrentContract = GameManager.Instance != null &&
                GameManager.Instance.CurrentContract == vehicle.assignedContract;
            if (isCurrentContract)
            {
                active = vehicle;
                break;
            }
            if (!vehicle.isParkedAtFinish) continue;
            if (parkedFallback == null) parkedFallback = vehicle;
            if (StableHash(vehicle.assignedContract.ContractID) == HostLiveLoadContractHash &&
                StableHash(vehicle.gameObject.name) == HostLiveLoadNameHash)
                active = vehicle;
        }
        if (active == null) active = parkedFallback;

        if (active == null)
        {
            HostLiveLoadVisible = false;
            return;
        }

        HostLiveLoadContractHash = StableHash(active.assignedContract.ContractID);
        HostLiveLoadNameHash = StableHash(active.gameObject.name);
        HostLiveLoadPosition = active.transform.position;
        HostLiveLoadRotation = active.transform.rotation;
        HostLiveLoadSimulating = active.physicsManager != null &&
            active.physicsManager.IsSimulationActive;
        HostLiveLoadVisible = true;
    }

    private void UpdateRemoteLiveLoad()
    {
        if (!IsBridgeBuilder || !HostLiveLoadVisible)
        {
            RestoreRemoteLiveLoad();
            return;
        }

        if (remoteLiveLoad == null ||
            StableHash(remoteLiveLoad.assignedContract != null
                ? remoteLiveLoad.assignedContract.ContractID : null) != HostLiveLoadContractHash ||
            StableHash(remoteLiveLoad.gameObject.name) != HostLiveLoadNameHash)
        {
            RestoreRemoteLiveLoad();
            foreach (LiveLoadVehicle candidate in FindObjectsOfType<LiveLoadVehicle>(true))
            {
                if (candidate == null || candidate.gameObject.scene != gameObject.scene ||
                    candidate.assignedContract == null ||
                    StableHash(candidate.assignedContract.ContractID) != HostLiveLoadContractHash ||
                    StableHash(candidate.gameObject.name) != HostLiveLoadNameHash)
                    continue;
                CaptureRemoteLiveLoad(candidate);
                break;
            }
        }

        if (remoteLiveLoad == null) return;
        SetRemoteLiveLoadInspectionAvailable(!HostLiveLoadSimulating);
        Transform vehicleTransform = remoteLiveLoad.transform;
        if ((vehicleTransform.position - HostLiveLoadPosition).sqrMagnitude > 9f)
            vehicleTransform.SetPositionAndRotation(HostLiveLoadPosition, HostLiveLoadRotation);
        else
        {
            float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);
            vehicleTransform.SetPositionAndRotation(
                Vector3.Lerp(vehicleTransform.position, HostLiveLoadPosition, blend),
                Quaternion.Slerp(vehicleTransform.rotation, HostLiveLoadRotation, blend));
        }
    }

    private void CaptureRemoteLiveLoad(LiveLoadVehicle vehicle)
    {
        remoteLiveLoad = vehicle;
        remoteLiveLoadWasActive = vehicle.gameObject.activeSelf;
        remoteLiveLoadOriginalPosition = vehicle.transform.position;
        remoteLiveLoadOriginalRotation = vehicle.transform.rotation;
        if (!remoteLiveLoadWasActive) vehicle.gameObject.SetActive(true);
        remoteLiveLoadBehaviourEnabled = vehicle.enabled;
        remoteLiveLoadInspectionAvailable = false;
        vehicle.SetRemoteMultiplayerRepresentation(true);
        vehicle.enabled = false;

        remoteLiveLoadBodies = vehicle.GetComponentsInChildren<Rigidbody>(true);
        remoteLiveLoadBodyWasKinematic = new bool[remoteLiveLoadBodies.Length];
        for (int index = 0; index < remoteLiveLoadBodies.Length; index++)
        {
            remoteLiveLoadBodyWasKinematic[index] = remoteLiveLoadBodies[index].isKinematic;
            remoteLiveLoadBodies[index].isKinematic = true;
        }

        remoteLiveLoadColliders = vehicle.GetComponentsInChildren<Collider>(true);
        remoteLiveLoadColliderWasEnabled = new bool[remoteLiveLoadColliders.Length];
        for (int index = 0; index < remoteLiveLoadColliders.Length; index++)
        {
            remoteLiveLoadColliderWasEnabled[index] = remoteLiveLoadColliders[index].enabled;
            remoteLiveLoadColliders[index].enabled = false;
        }

        remoteLiveLoadObstacles = vehicle.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true);
        remoteLiveLoadObstacleWasEnabled = new bool[remoteLiveLoadObstacles.Length];
        for (int index = 0; index < remoteLiveLoadObstacles.Length; index++)
        {
            remoteLiveLoadObstacleWasEnabled[index] = remoteLiveLoadObstacles[index].enabled;
            remoteLiveLoadObstacles[index].enabled = false;
        }
    }

    private void SetRemoteLiveLoadInspectionAvailable(bool available)
    {
        if (remoteLiveLoad == null || remoteLiveLoadInspectionAvailable == available) return;
        remoteLiveLoadInspectionAvailable = available;
        remoteLiveLoad.enabled = available && remoteLiveLoadBehaviourEnabled;
        for (int index = 0; index < remoteLiveLoadColliders.Length; index++)
            if (remoteLiveLoadColliders[index] != null)
                remoteLiveLoadColliders[index].enabled =
                    available && remoteLiveLoadColliderWasEnabled[index];
    }

    private void RestoreRemoteLiveLoad()
    {
        if (remoteLiveLoad != null)
        {
            remoteLiveLoad.transform.SetPositionAndRotation(
                remoteLiveLoadOriginalPosition, remoteLiveLoadOriginalRotation);
            for (int index = 0; index < remoteLiveLoadBodies.Length; index++)
                if (remoteLiveLoadBodies[index] != null)
                    remoteLiveLoadBodies[index].isKinematic = remoteLiveLoadBodyWasKinematic[index];
            for (int index = 0; index < remoteLiveLoadColliders.Length; index++)
                if (remoteLiveLoadColliders[index] != null)
                    remoteLiveLoadColliders[index].enabled = remoteLiveLoadColliderWasEnabled[index];
            for (int index = 0; index < remoteLiveLoadObstacles.Length; index++)
                if (remoteLiveLoadObstacles[index] != null)
                    remoteLiveLoadObstacles[index].enabled = remoteLiveLoadObstacleWasEnabled[index];
            remoteLiveLoad.SetRemoteMultiplayerRepresentation(false);
            remoteLiveLoad.enabled = remoteLiveLoadBehaviourEnabled;
            if (!remoteLiveLoadWasActive) remoteLiveLoad.gameObject.SetActive(false);
        }
        remoteLiveLoad = null;
        remoteLiveLoadBodies = null;
        remoteLiveLoadBodyWasKinematic = null;
        remoteLiveLoadColliders = null;
        remoteLiveLoadColliderWasEnabled = null;
        remoteLiveLoadObstacles = null;
        remoteLiveLoadObstacleWasEnabled = null;
    }

    private void FindScenePlayer()
    {
        PlayerMotor motor = FindObjectOfType<PlayerMotor>(true);
        if (motor == null) return;
        BindSceneMotor(motor);
        sceneCosmetics = motor.GetComponent<PlayerCosmetics>();
        sceneAnimator = motor.playerAnimator;
    }

    private void BindSceneMotor(PlayerMotor motor)
    {
        if (sceneMotor == motor) return;
        if (sceneMotor != null) sceneMotor.Jumped -= OnLocalJump;
        sceneMotor = motor;
        if (sceneMotor != null && HasInputAuthority) sceneMotor.Jumped += OnLocalJump;
    }

    private void OnLocalJump() { localJumpSequence++; }

    private void PublishAppearance(CosmeticLoadoutData loadout)
    {
        if (!HasInputAuthority || loadout == null) return;
        string json = JsonUtility.ToJson(loadout);
        if (json == lastPublishedAppearance) return;
        if (Encoding.UTF8.GetByteCount(json) > 1900)
        {
            Debug.LogWarning("[Fusion] Cosmetic loadout is too large to synchronize.", this);
            return;
        }

        lastPublishedAppearance = json;
        if (HasStateAuthority) Appearance = json;
        else RPC_PublishAppearance(json);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority,
        Channel = RpcChannel.ReliableLargeData)]
    private void RPC_PublishAppearance(string json) { Appearance = json; }

    private void OffsetJoiningPlayerSpawn()
    {
        if (sceneMotor == null) return;
        localSpawnAdjusted = true;
        if (HasStateAuthority) return;

        Transform player = sceneMotor.transform;
        Vector3 origin = player.position;
        Vector3[] directions = { player.right, -player.right, player.forward, -player.forward };
        foreach (Vector3 direction in directions)
        {
            if (!NavMesh.SamplePosition(origin + direction * 3f, out NavMeshHit hit,
                    1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - origin.y) > 1.5f ||
                (hit.position - origin).sqrMagnitude < 4f)
                continue;

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.position = hit.position;
            if (controller != null) controller.enabled = true;
            PlayerLook look = player.GetComponent<PlayerLook>();
            if (look != null) look.SnapToFollowTarget();
            return;
        }
        Debug.LogWarning("[Fusion] No safe second spawn found; players will begin together.", this);
    }

    private void UpdateRemotePose()
    {
        if ((transform.position - Position).sqrMagnitude > 100f)
            transform.SetPositionAndRotation(Position, Rotation);
        else
        {
            float blend = 1f - Mathf.Exp(-15f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, Position, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Rotation, blend);
        }
        transform.localScale = Scale;
    }

    private void CreateRemoteVisual()
    {
        if (sceneMotor == null || sceneCosmetics == null) return;
        Transform sourceVisual = sceneMotor.transform.Find("NewCharacterModel");
        if (sourceVisual == null)
        {
            Debug.LogError("[Fusion] Scene Player has no NewCharacterModel to mirror.", this);
            return;
        }

        visualContainer = new GameObject("Remote Player Appearance");
        visualContainer.transform.SetParent(transform, false);
        visualContainer.SetActive(false);
        GameObject visual = Instantiate(sourceVisual.gameObject, visualContainer.transform, false);
        visual.name = "Remote " + sourceVisual.name;
        visual.transform.localPosition = sourceVisual.localPosition;
        visual.transform.localRotation = sourceVisual.localRotation;
        visual.transform.localScale = sourceVisual.localScale;

        foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Camera camera in visual.GetComponentsInChildren<Camera>(true))
            camera.enabled = false;
        foreach (AudioListener listener in visual.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;

        Dictionary<Transform, Transform> cloneMap = new Dictionary<Transform, Transform>();
        MapClonedTransforms(sourceVisual, visual.transform, cloneMap);
        remoteBindings = CopyBindings(sceneCosmetics.cosmeticBindings, cloneMap);
        remoteHats = CopyHats(sceneCosmetics.hats, cloneMap);
        remoteAnimator = visual.GetComponentInChildren<Animator>(true);
        lastObservedJumpSequence = JumpSequence;
        visualContainer.SetActive(true);
        lastAppliedAppearance = null;
    }

    private static void MapClonedTransforms(Transform source, Transform clone,
        Dictionary<Transform, Transform> map)
    {
        map.Add(source, clone);
        for (int index = 0; index < source.childCount && index < clone.childCount; index++)
            MapClonedTransforms(source.GetChild(index), clone.GetChild(index), map);
    }

    private static List<CosmeticModelBinding> CopyBindings(
        List<CosmeticModelBinding> sourceBindings, Dictionary<Transform, Transform> map)
    {
        List<CosmeticModelBinding> copies = new List<CosmeticModelBinding>();
        foreach (CosmeticModelBinding binding in sourceBindings)
        {
            if (binding == null) continue;
            CosmeticModelBinding copy = new CosmeticModelBinding
            {
                cosmeticID = binding.cosmeticID,
                category = binding.category,
                defaultWhenEmpty = binding.defaultWhenEmpty
            };
            if (binding.models != null)
                foreach (GameObject model in binding.models)
                    if (model != null && map.TryGetValue(model.transform, out Transform cloned))
                        copy.models.Add(cloned.gameObject);
            copies.Add(copy);
        }
        return copies;
    }

    private static List<CosmeticItem> CopyHats(List<CosmeticItem> sourceHats,
        Dictionary<Transform, Transform> map)
    {
        List<CosmeticItem> copies = new List<CosmeticItem>();
        foreach (CosmeticItem hat in sourceHats)
            if (hat != null && hat.cosmeticModel != null &&
                map.TryGetValue(hat.cosmeticModel.transform, out Transform cloned))
                copies.Add(new CosmeticItem { cosmeticID = hat.cosmeticID, cosmeticModel = cloned.gameObject });
        return copies;
    }

    private void ApplyRemoteAppearance()
    {
        if (visualContainer == null || remoteBindings == null) return;
        string json = Appearance;
        if (json == lastAppliedAppearance) return;

        CosmeticLoadoutData loadout;
        try
        {
            loadout = string.IsNullOrEmpty(json)
                ? new CosmeticLoadoutData()
                : JsonUtility.FromJson<CosmeticLoadoutData>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Fusion] Could not read remote appearance: {exception.Message}", this);
            loadout = new CosmeticLoadoutData();
        }

        if (loadout == null) loadout = new CosmeticLoadoutData();
        CosmeticBindingUtility.Apply(remoteBindings, loadout);
        if (remoteHats != null)
            foreach (CosmeticItem hat in remoteHats)
                if (hat.cosmeticModel != null)
                    hat.cosmeticModel.SetActive(loadout.IsAccessoryEquipped(hat.cosmeticID));
        lastAppliedAppearance = json;
    }

    private void ApplyRemoteAnimation()
    {
        if (remoteAnimator == null) return;
        remoteAnimator.SetFloat(SpeedParameter, MoveSpeed);
        remoteAnimator.SetBool(SprintParameter, Sprinting);
        remoteAnimator.SetBool(GroundedParameter, Grounded);
        if (lastObservedJumpSequence != JumpSequence)
        {
            lastObservedJumpSequence = JumpSequence;
            remoteAnimator.SetTrigger(JumpParameter);
        }
    }

    private void DestroyRemoteVisual()
    {
        if (visualContainer != null) Destroy(visualContainer);
        visualContainer = null;
        remoteAnimator = null;
        remoteBindings = null;
        remoteHats = null;
        lastAppliedAppearance = null;
    }
}
