using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch-only host/guest check in a private, disposable Photon room. Uses no player sign-in or saves.</summary>
[InitializeOnLoad]
public static class TwoWayRoadNetworkValidation
{
    private const string Pending = "CivilCraft.TwoWayRoadNetworkValidation.Pending";
    private const string Report = "Logs/two-way-road-network.txt";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    static TwoWayRoadNetworkValidation() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this private session check in a batch Editor.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "RUNNING: private two-peer road width validation.\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static async void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.EraseBool(Pending);
        NetworkRunner host = null, guest = null;
        string outcome;
        bool passed = false;
        try
        {
            host = new GameObject("Private road test host").AddComponent<NetworkRunner>();
            guest = new GameObject("Private road test guest").AddComponent<NetworkRunner>();
            string room = "CCW" + Guid.NewGuid().ToString("N");
            var settings = (FusionAppSettings)typeof(FusionConnectionManager)
                .GetMethod("BuildPhotonSettings", PrivateStatic).Invoke(null, null);
            settings.AppVersion += "-validation";
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55)))
            {
                await StartPeer(host, GameMode.Host, room, settings, deadline.Token);
                await StartPeer(guest, GameMode.Client, room, settings, deadline.Token);
                host.SessionInfo.IsOpen = false;
                await CheckReplication(host, guest, deadline.Token);
            }
            outcome = "PASS: a private Fusion host and guest received the same 16 m and 19 m road meshes, " +
                "committed colliders, fitted pier meshes, scaled receipt rates and totals, and frozen challenge width.";
            passed = true;
        }
        catch (Exception error) { outcome = "FAIL: " + error; }
        finally
        {
            await StopPeer(guest);
            await StopPeer(host);
        }
        File.WriteAllText(Report, outcome + "\n");
        Debug.Log(outcome);
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static async Task StartPeer(NetworkRunner runner, GameMode mode, string room,
        FusionAppSettings settings, CancellationToken cancellation)
    {
        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = room,
            PlayerCount = 2,
            IsOpen = true,
            IsVisible = false,
            EnableClientSessionCreation = false,
            SceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>(),
            CustomPhotonAppSettings = settings,
            StartGameCancellationToken = cancellation
        });
        Require(result.Ok, $"Private {mode} startup failed ({result.ShutdownReason}).");
    }

    private static async Task StopPeer(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning) return;
        try { await Task.WhenAny(runner.Shutdown(), Task.Delay(4000)); }
        catch (Exception error) { Debug.LogWarning("Private Fusion cleanup: " + error.Message); }
    }

    private static async Task CheckReplication(NetworkRunner host, NetworkRunner guest, CancellationToken cancellation)
    {
        BridgeMaterialSO road = Resources.Load<BridgeMaterialSO>("Concrete Road");
        BridgeMaterialSO concretePier = Resources.Load<BridgeMaterialSO>("Concrete Pier");
        BridgeMaterialSO steelPier = Resources.Load<BridgeMaterialSO>("SteelPier");
        var prefab = Resources.Load<GameObject>("FusionMultiplayerAvatar")?.GetComponent<NetworkObject>();
        Require(road != null && concretePier != null && steelPier != null && prefab != null,
            "The road, piers, or production network avatar are missing.");
        var physics = new GameObject("Private road test physics").AddComponent<BridgePhysicsManager>();
        physics.enabled = false;
        Bar[] roads = { CreateRoad(road, physics, "wide-16", 16f, 0f), CreateRoad(road, physics, "wide-19", 19f, 30f) };
        Bar[] piers = { CreatePier(concretePier, roads[1], roads[1].startPoint), CreatePier(steelPier, roads[1], roads[1].endPoint) };
        int memberCount = roads.Length + piers.Length;

        var hostObject = host.Spawn(prefab);
        Require(hostObject != null, "The host could not spawn its production avatar.");
        var publisher = hostObject.GetComponent<FusionMultiplayerAvatar>();
        var avatar = typeof(FusionMultiplayerAvatar);
        avatar.GetProperty("IsBridgeBuilder", PrivateInstance).SetValue(publisher, true);
        avatar.GetProperty(nameof(FusionMultiplayerAvatar.ChallengeState)).SetValue(publisher,
            new MultiplayerChallengeState { Revision = 17, WideRoadWidth = 19f });
        avatar.GetMethod("PublishHostBridgeBars", PrivateInstance).Invoke(publisher, null);

        var barCount = avatar.GetProperty("HostBridgeBarCount", PrivateInstance);
        FusionMultiplayerAvatar receiver = null;
        while (!cancellation.IsCancellationRequested)
        {
            if (guest.TryFindObject(hostObject.Id, out NetworkObject guestObject))
            {
                receiver = guestObject.GetComponent<FusionMultiplayerAvatar>();
                if (receiver.ChallengeState.Revision == 17 && (int)barCount.GetValue(receiver) == memberCount) break;
            }
            await Task.Delay(20, cancellation);
        }
        cancellation.ThrowIfCancellationRequested();
        Require(receiver != null && receiver.ChallengeState.WideRoadWidth == 19f,
            "The challenge's host-frozen width did not arrive on the guest.");

        avatar.GetMethod("UpdateRemoteBridgeBars", PrivateInstance).Invoke(receiver, null);
        var guestRoot = (GameObject)avatar.GetField("remoteBridgeBarRoot", PrivateInstance).GetValue(receiver);
        Require(guestRoot != null && guestRoot.transform.childCount == memberCount, "The guest did not build every member visual.");
        foreach (Bar hostRoad in roads)
        {
            Transform guestRoad = guestRoot.transform.Cast<Transform>().Single(child =>
                Mathf.Abs(child.position.z - hostRoad.transform.position.z) < .01f && child.GetComponent<BoxCollider>() != null);
            Bounds guestBounds = MeshBounds(guestRoad);
            var hostCollider = hostRoad.GetComponent<BoxCollider>();
            var guestCollider = guestRoad.GetComponent<BoxCollider>();
            Require(Mathf.Abs(guestBounds.size.z - hostRoad.WideRoadWidth) < .02f && guestCollider != null &&
                guestCollider.size == hostCollider.size && guestCollider.center == hostCollider.center,
                $"Guest width or committed collider differs for the {hostRoad.WideRoadWidth} m road.");
            CheckReceipt(receiver, hostRoad);
        }
        foreach (Bar hostPier in piers)
        {
            Transform guestPier = guestRoot.transform.Cast<Transform>().Single(child =>
                Mathf.Abs(child.position.x - hostPier.transform.position.x) < .01f && child.GetComponent<BoxCollider>() == null);
            Bounds hostBounds = MeshBounds(hostPier.transform);
            Bounds guestBounds = MeshBounds(guestPier);
            Require(Vector3.Distance(hostBounds.min, guestBounds.min) < .02f &&
                Vector3.Distance(hostBounds.max, guestBounds.max) < .02f,
                $"Guest fitted pier differs for {hostPier.materialData.GetDisplayName()}.");
        }
    }

    private static Bar CreateRoad(BridgeMaterialSO material, BridgePhysicsManager physics, string id, float width, float z)
    {
        var root = new GameObject("Private road " + id);
        root.SetActive(false);
        var site = root.AddComponent<BuildLocation>();
        site.enabled = false;
        var contract = ScriptableObject.CreateInstance<ContractSO>();
        contract.contractID = id;
        contract.twoWayRoad = true;
        contract.twoWayRoadWidth = width;
        site.activeContract = contract;

        var start = Child<Point>(root.transform, "Start");
        var end = Child<Point>(root.transform, "End");
        start.transform.position = new Vector3(0f, 2f, z);
        end.transform.position = new Vector3(8f, width == 16f ? 2f : 4f, z);
        var bar = Child<Bar>(root.transform, "Road");
        bar.AssignOwner(site);
        bar.Initialize(material);
        bar.startPoint = start;
        bar.endPoint = end;
        bar.NormalizeEndpointOrder();
        typeof(BridgePhysicsManager).GetMethod("EnsurePermanentBakedRoadCollider", PrivateInstance)
            .Invoke(physics, new object[] { bar });
        site.bakedBars.Add(bar);
        root.SetActive(true);
        return bar;
    }

    private static Bar CreatePier(BridgeMaterialSO material, Bar road, Point upper)
    {
        BuildLocation site = road.OwnerLocation;
        var lower = Child<Point>(site.transform, "Pier base");
        lower.transform.position = upper.transform.position - 6f * Vector3.up;
        var bar = Child<Bar>(site.transform, material.GetDisplayName());
        bar.AssignOwner(site);
        bar.Initialize(material);
        bar.startPoint = lower;
        bar.endPoint = upper;
        bar.NormalizeEndpointOrder();
        bar.RefreshPierCapSeating();
        site.bakedBars.Add(bar);
        return bar;
    }

    private static Bounds MeshBounds(Transform root) => root.GetComponentsInChildren<MeshRenderer>()
        .Select(renderer => renderer.bounds)
        .Aggregate((bounds, next) => { bounds.Encapsulate(next); return bounds; });

    private static void CheckReceipt(FusionMultiplayerAvatar guest, Bar hostRoad)
    {
        var row = new GameObject("Receipt template", typeof(RectTransform), typeof(ReceiptRowUI));
        var fields = row.GetComponent<ReceiptRowUI>();
        fields.priceText = ReceiptLabel(row.transform, "Price");
        fields.totalText = ReceiptLabel(row.transform, "Amount");
        fields.materialNameText = ReceiptLabel(row.transform, "Material");
        var receipt = new GameObject("Guest receipt", typeof(RectTransform));
        string contractId = hostRoad.OwnerLocation.activeContract.ContractID;
        int hash = FusionMultiplayerAvatar.StableHash(contractId);
        int count = guest.PopulateGuestReceiptRows(receipt.transform, row, hash);
        Require(count == hostRoad.OwnerLocation.bakedBars.Count, $"Guest receipt for {contractId} has {count} rows.");
        var actual = receipt.GetComponentsInChildren<ReceiptRowUI>().Single(item =>
            item.materialNameText.text == hostRoad.materialData.GetDisplayName());
        var expected = BridgeConstructionReceipt.Capture(hostRoad.OwnerLocation, new[] { hostRoad }, includeDisabled: true);
        Require(actual.priceText.text == $"₱{expected.GetCostPerMeter(hostRoad.materialData):N0}/m" &&
            actual.totalText.text == $"= ₱{expected.TotalCost:N0}", $"Guest rates and total differ for {contractId}.");
    }

    private static T Child<T>(Transform parent, string name) where T : Component
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.AddComponent<T>();
    }

    private static TextMeshProUGUI ReceiptLabel(Transform parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        child.transform.SetParent(parent, false);
        return child.GetComponent<TextMeshProUGUI>();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
