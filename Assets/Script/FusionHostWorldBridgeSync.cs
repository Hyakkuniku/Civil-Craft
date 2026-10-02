using System;
using System.Collections.Generic;
using System.IO;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Completed Canyon bridges, sent by the host only when their geometry changes.</summary>
[DisallowMultipleComponent]
public sealed class FusionHostWorldBridgeSync : MonoBehaviour
{
    internal const int MessageTag = 0x43434252;
    [SerializeField, Min(0.25f)] private float captureInterval = 1f;
    [SerializeField] private bool logBridgeDiagnostics;
    private NetworkRunner runner;
    private bool sceneReady, requestedInitialState;
    private float nextCaptureTime, nextDiagnosticTime;
    private int nextLocationId;
    private int packetsSent, bytesSent, packetsReceived, bytesReceived;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool loggedHostTransfer, loggedGuestReceive, loggedGuestRender;
#endif
    private readonly Dictionary<BuildLocation, int> locationIds = new Dictionary<BuildLocation, int>();
    private readonly Dictionary<int, HostLocation> hostLocations = new Dictionary<int, HostLocation>();
    private readonly HashSet<PlayerRef> pendingInitialPlayers = new HashSet<PlayerRef>();
    private readonly Dictionary<PlayerRef, float> lastRequestTimes = new Dictionary<PlayerRef, float>();
    private readonly Dictionary<PlayerRef, Dictionary<int, int>> sentRevisions =
        new Dictionary<PlayerRef, Dictionary<int, int>>();
    private readonly Dictionary<int, GuestLocation> guestLocations = new Dictionary<int, GuestLocation>();
    private readonly HashSet<int> pendingRender = new HashSet<int>();
    private readonly Dictionary<string, BridgeMaterialSO> materials = new Dictionary<string, BridgeMaterialSO>();
    private readonly HashSet<string> missingMaterials = new HashSet<string>();
    private GameObject guestRoot;
    private GameObject pointPrefab;
    private string challengeSiteName;
    private bool challengeBridgesHidden;
    private readonly Dictionary<GameObject, bool> bridgeVisibilityBeforeChallenge = new Dictionary<GameObject, bool>();

    public void SetChallengeSiteVisibility(string siteName, bool hideAllBridges = false)
    {
        if (siteName == null)
        {
            foreach (var entry in bridgeVisibilityBeforeChallenge)
                if (entry.Key != null) entry.Key.SetActive(entry.Value);
            bridgeVisibilityBeforeChallenge.Clear();
        }
        challengeSiteName = siteName;
        challengeBridgesHidden = siteName != null && hideAllBridges;
        if (siteName == null) return;
        foreach (GuestLocation location in guestLocations.Values)
        {
            if (location.Visual == null) continue;
            GameObject root = location.Visual.Root;
            if (!bridgeVisibilityBeforeChallenge.ContainsKey(root)) bridgeVisibilityBeforeChallenge.Add(root, root.activeSelf);
            root.SetActive(!challengeBridgesHidden && location.Snapshot.Name == siteName);
        }
    }

    private sealed class HostLocation
    {
        public BuildLocation Source;
        public byte[] Packet;
        public int Revision;
    }
    private sealed class GuestLocation
    {
        public int Revision;
        public HostWorldBridgeSnapshot Snapshot;
        public HostWorldBridgeVisual Visual;
    }

    private void Awake()
    {
        runner = GetComponent<NetworkRunner>();
        LevelCompleteManager.BridgeSavedAtLocation += OnBridgeSaved;
    }

    private void OnBridgeSaved(ContractSO contract, BuildLocation location) => nextCaptureTime = 0f;

    public void OnSceneLoading() { sceneReady = false; }
    public void OnSceneReady()
    {
        sceneReady = SceneManager.GetActiveScene().name == FusionConnectionManager.HostWorldSceneName;
        nextCaptureTime = 0f;
        requestedInitialState = false;
    }

    public void OnPlayerJoined(PlayerRef player)
    {
        if (runner != null && runner.IsServer && player != runner.LocalPlayer)
            pendingInitialPlayers.Add(player);
    }

    public void OnPlayerLeft(PlayerRef player)
    {
        pendingInitialPlayers.Remove(player);
        lastRequestTimes.Remove(player);
        sentRevisions.Remove(player);
    }

    private void Update()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (runner == null || !runner.IsRunning || connection == null || connection.Runner != runner ||
            SceneManager.GetActiveScene().name != FusionConnectionManager.HostWorldSceneName)
        {
            ClearVisuals();
            return;
        }
        if (!sceneReady || connection.IsNetworkSceneLoading) return;

        if (runner.IsServer)
        {
            if (Time.unscaledTime >= nextCaptureTime || pendingInitialPlayers.Count > 0)
            {
                nextCaptureTime = Time.unscaledTime + Mathf.Max(0.25f, captureInterval);
                CaptureHostWorld();
                foreach (PlayerRef player in pendingInitialPlayers)
                    foreach (KeyValuePair<int, HostLocation> entry in hostLocations)
                        SendLocation(player, entry.Key, entry.Value);
                pendingInitialPlayers.Clear();
            }
        }
        else
        {
            if (!requestedInitialState)
            {
                // This also covers joining before the host's slower scene load
                // completes: the host queues the request until its world is ready.
                runner.SendReliableDataToServer(ReliableKey.FromInts(MessageTag,
                    HostWorldBridgeSnapshotCodec.Version, 0, 0), new byte[] { 1 });
                requestedInitialState = true;
            }
            ApplyPendingGeometry();
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (logBridgeDiagnostics && Time.unscaledTime >= nextDiagnosticTime)
        {
            nextDiagnosticTime = Time.unscaledTime + 5f;
            Debug.Log($"[Fusion bridges] host={runner.IsServer} locations=" +
                $"{(runner.IsServer ? hostLocations.Count : guestLocations.Count)} " +
                $"sent={packetsSent}/{bytesSent}B received={packetsReceived}/{bytesReceived}B", this);
            packetsSent = bytesSent = packetsReceived = bytesReceived = 0;
        }
#endif
    }

    private void CaptureHostWorld()
    {
        // Challenge visibility is temporary. Do not publish hidden saved-world
        // colliders or a private player's draft as changes to the completed world.
        if (MultiplayerChallengeLobbyUI.IsChallengeActive ||
            (GameManager.Instance != null && GameManager.Instance.IsSessionChallengeBuild)) return;
        foreach (BuildLocation location in FindObjectsOfType<BuildLocation>(true))
        {
            if (location == null || location.IsSessionChallengeLocation || location.gameObject.scene != SceneManager.GetActiveScene()) continue;
            if (!locationIds.TryGetValue(location, out int id))
            {
                id = ++nextLocationId;
                locationIds.Add(location, id);
                hostLocations.Add(id, new HostLocation { Source = location });
            }

            // A redesign edits the original objects in place. Retain the last
            // committed packet instead of publishing a draft, failed test or
            // temporarily hidden geometry as the host's completed world.
            if (location.IsRedesigningBridge || (GameManager.Instance != null &&
                GameManager.Instance.CurrentState != GameManager.GameState.Normal &&
                GameManager.Instance.ActiveBuildLocation == location)) continue;

            try { PublishIfChanged(id, HostWorldBridgeSnapshotCodec.Encode(CaptureLocation(location))); }
            catch (InvalidDataException exception)
            {
                Debug.LogError($"[Fusion bridges] Cannot synchronize '{location.name}': {exception.Message}", this);
            }
        }
        foreach (KeyValuePair<int, HostLocation> entry in hostLocations)
            if (entry.Value.Source == null && entry.Value.Packet != null)
                PublishIfChanged(entry.Key, HostWorldBridgeSnapshotCodec.Encode(new HostWorldBridgeSnapshot()));
    }

    private void PublishIfChanged(int id, byte[] packet)
    {
        HostLocation location = hostLocations[id];
        if (HostWorldBridgeSnapshotCodec.SameBytes(location.Packet, packet)) return;
        location.Packet = packet;
        location.Revision = unchecked(location.Revision + 1);
        foreach (PlayerRef player in runner.ActivePlayers)
            if (player != runner.LocalPlayer) SendLocation(player, id, location);
    }

    private void SendLocation(PlayerRef player, int id, HostLocation location)
    {
        if (location.Packet == null || !IsRemotePlayer(player)) return;
        if (!sentRevisions.TryGetValue(player, out Dictionary<int, int> revisions))
        {
            revisions = new Dictionary<int, int>();
            sentRevisions.Add(player, revisions);
        }
        if (revisions.TryGetValue(id, out int previous) && previous == location.Revision) return;
        runner.SendReliableDataToPlayer(player, ReliableKey.FromInts(MessageTag,
            HostWorldBridgeSnapshotCodec.Version, id, location.Revision), location.Packet);
        revisions[id] = location.Revision;
        packetsSent++;
        bytesSent += location.Packet.Length;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!loggedHostTransfer && location.Source != null && location.Source.bakedBars.Count > 0)
        {
            loggedHostTransfer = true;
            Debug.Log($"[Fusion bridges] Sent completed bridge '{location.Source.name}' to {player}: " +
                $"bars={location.Source.bakedBars.Count} bytes={location.Packet.Length}", this);
        }
#endif
    }

    private bool IsRemotePlayer(PlayerRef player)
    {
        if (player == runner.LocalPlayer || player == PlayerRef.None) return false;
        foreach (PlayerRef active in runner.ActivePlayers) if (active == player) return true;
        return false;
    }

    public void OnReliableData(PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        if (runner == null || !runner.IsRunning) return;
        key.GetInts(out int tag, out int version, out int id, out int revision);
        if (tag != MessageTag || version != HostWorldBridgeSnapshotCodec.Version) return;
        if (runner.IsServer)
        {
            // Guests may request a copy, but cannot publish or edit host geometry.
            if (id != 0 || data.Length != 1 || data[0] != 1 || !IsRemotePlayer(player)) return;
            if (lastRequestTimes.TryGetValue(player, out float previous) && Time.unscaledTime - previous < 2f)
                return;
            lastRequestTimes[player] = Time.unscaledTime;
            pendingInitialPlayers.Add(player);
            return;
        }
        ReceiveHostGeometry(player, id, revision, data);
    }

    private void ReceiveHostGeometry(PlayerRef connectionPlayer, int id, int revision, ReadOnlySpan<byte> data)
    {
        // In this Fusion SDK, OnReliableData uses Connection2Player. On a
        // client the SERVER connection is registered with the CLIENT's local
        // PlayerRef, so a host transfer legitimately reports our own player ID.
        // Filtering connectionPlayer == runner.LocalPlayer drops every bridge.
        // This receiver is called only from the live Host/Client guest path;
        // the server branch above still rejects guest-authored geometry.
        if (id <= 0) return;
        if (guestLocations.TryGetValue(id, out GuestLocation existing) &&
            !HostWorldBridgeSnapshotCodec.IsNewerRevision(revision, existing.Revision)) return;
        try
        {
            HostWorldBridgeSnapshot snapshot = HostWorldBridgeSnapshotCodec.Decode(data);
            if (existing == null)
            {
                existing = new GuestLocation();
                guestLocations.Add(id, existing);
            }
            existing.Revision = revision;
            existing.Snapshot = snapshot;
            pendingRender.Add(id);
            packetsReceived++;
            bytesReceived += data.Length;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!loggedGuestReceive && snapshot.Bars.Count > 0)
            {
                loggedGuestReceive = true;
                Debug.Log($"[Fusion bridges] Received host bridge '{snapshot.Name}': " +
                    $"bars={snapshot.Bars.Count} connectionPlayer={connectionPlayer} revision={revision}", this);
            }
#endif
        }
        catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException)
        {
            Debug.LogWarning($"[Fusion bridges] Ignored invalid host bridge packet: {exception.Message}", this);
        }
    }

    private void ApplyPendingGeometry()
    {
        if (pendingRender.Count == 0) return;
        if (guestRoot == null)
        {
            guestRoot = new GameObject("Host World Bridges (Read Only)");
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Multiplayer")
                {
                    guestRoot.transform.SetParent(root.transform, false);
                    break;
                }
        }
        LoadMaterials();
        if (pointPrefab == null)
        {
            BarCreator creator = FindObjectOfType<BarCreator>(true);
            if (creator != null) pointPrefab = creator.pointToInstantiate;
        }
        bool geometryChanged = false;
        foreach (int id in new List<int>(pendingRender))
        {
            GuestLocation location = guestLocations[id];
            bool available = true;
            foreach (HostWorldBridgeBar bar in location.Snapshot.Bars)
                if (!materials.ContainsKey(bar.MaterialId))
                {
                    available = false;
                    if (missingMaterials.Add(bar.MaterialId))
                        Debug.LogWarning($"[Fusion bridges] Host material '{bar.MaterialId}' is missing on this guest build.", this);
                }
            if (!available) continue;
            // Construct a replacement off-screen, then switch once the complete
            // packet has arrived. Partial reliable transfers never remove a road.
            HostWorldBridgeVisual replacement = new HostWorldBridgeVisual(location.Snapshot,
                materials, pointPrefab, guestRoot.transform);
            location.Visual?.Dispose();
            location.Visual = replacement;
            if (challengeSiteName != null) bridgeVisibilityBeforeChallenge[replacement.Root] = true;
            replacement.Root.SetActive(!challengeBridgesHidden && (challengeSiteName == null || location.Snapshot.Name == challengeSiteName));
            pendingRender.Remove(id);
            geometryChanged = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!loggedGuestRender && location.Snapshot.Bars.Count > 0)
            {
                loggedGuestRender = true;
                Debug.Log($"[Fusion bridges] Rendered host bridge '{location.Snapshot.Name}': " +
                    $"bars={location.Snapshot.Bars.Count} active={replacement.Root.activeInHierarchy}", this);
            }
#endif
        }
        if (geometryChanged) Physics.SyncTransforms();
    }

    private void LoadMaterials()
    {
        if (materials.Count > 0 && missingMaterials.Count == 0) return;
        foreach (BridgeMaterialSO material in Resources.LoadAll<BridgeMaterialSO>(string.Empty)) RegisterMaterial(material);
        foreach (BridgeMaterialSO material in Resources.FindObjectsOfTypeAll<BridgeMaterialSO>()) RegisterMaterial(material);
    }
    private void RegisterMaterial(BridgeMaterialSO material)
    {
        if (material == null) return;
        materials[material.Id] = material;
        missingMaterials.Remove(material.Id);
    }

    public static HostWorldBridgeSnapshot CaptureLocation(BuildLocation location)
    {
        // The existing name slot carries an authored site path. Two workbenches
        // with the same leaf name must not both remain visible in a challenge.
        var snapshot = new HostWorldBridgeSnapshot { Name = MultiplayerChallengeRules.SiteKey(location) };
        foreach (Bar bar in location.bakedBars)
        {
            if (bar == null || bar.materialData == null) continue;
            var member = new HostWorldBridgeBar
            {
                MaterialId = bar.materialData.Id, Pose = HostWorldBridgePose.World(bar.transform)
            };
            foreach (Transform child in bar.transform)
            {
                bool isCap = child.name == "PierCap";
                if (!isCap && !child.name.StartsWith("VisualSegment", StringComparison.Ordinal)) continue;
                member.Parts.Add(new HostWorldBridgePart
                {
                    IsCap = isCap, Visible = child.gameObject.activeSelf, Pose = HostWorldBridgePose.Local(child)
                });
            }
            CaptureColliders(bar.transform, member.Colliders);
            snapshot.Bars.Add(member);
        }
        foreach (Point point in location.bakedPoints)
        {
            if (point == null || location.startingAnchors.Contains(point) || location.endingAnchors.Contains(point)) continue;
            bool visible = false;
            foreach (Renderer renderer in point.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled) { visible = true; break; }
            var node = new HostWorldBridgeNode
            {
                Pose = HostWorldBridgePose.World(point.transform), Visible = visible,
                IsAnchor = point.originalIsAnchor || point.isAnchor
            };
            CaptureColliders(point.transform, node.Colliders);
            if (node.Visible || node.Colliders.Count > 0) snapshot.Nodes.Add(node);
        }
        return snapshot;
    }

    private static void CaptureColliders(Transform root, List<HostWorldBridgeCollider> destination)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger) continue;
            bool visibleChild = true;
            for (Transform child = collider.transform; child != root; child = child.parent)
                if (!child.gameObject.activeSelf) { visibleChild = false; break; }
            if (!visibleChild) continue;
            var record = new HostWorldBridgeCollider
            {
                Pose = HostWorldBridgePose.World(collider.transform), Layer = collider.gameObject.layer
            };
            if (collider is BoxCollider box)
            { record.Kind = HostWorldBridgeCollider.Box; record.Center = box.center; record.Size = box.size; }
            else if (collider is SphereCollider sphere)
            { record.Kind = HostWorldBridgeCollider.Sphere; record.Center = sphere.center; record.Radius = sphere.radius; }
            else if (collider is CapsuleCollider capsule)
            {
                record.Kind = HostWorldBridgeCollider.Capsule; record.Center = capsule.center;
                record.Radius = capsule.radius; record.Height = capsule.height; record.Direction = capsule.direction;
            }
            else continue;
            PhysicMaterial material = collider.sharedMaterial;
            record.HasMaterial = material != null;
            if (material != null)
            {
                record.StaticFriction = material.staticFriction; record.DynamicFriction = material.dynamicFriction;
                record.Bounciness = material.bounciness; record.FrictionCombine = (int)material.frictionCombine;
                record.BounceCombine = (int)material.bounceCombine;
            }
            destination.Add(record);
        }
    }

    private void ClearVisuals()
    {
        SetChallengeSiteVisibility(null);
        if (guestRoot == null) return;
        foreach (GuestLocation location in guestLocations.Values)
        { location.Visual?.Dispose(); location.Visual = null; }
        guestRoot.SetActive(false);
        Destroy(guestRoot);
        guestRoot = null;
    }
    private void OnDestroy()
    {
        LevelCompleteManager.BridgeSavedAtLocation -= OnBridgeSaved;
        ClearVisuals();
    }
}
