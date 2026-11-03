#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Native inactive-source, invitation fairness and parking checks on temporary objects only.</summary>
public static class MultiLoadNativeValidation
{
    public const string ReportPath = "Logs/multi-load-native.txt";
    [MenuItem("Tools/Civil Craft/Multiple Live Loads/5. Validate Native Roster and Parking (Read Only)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup(); Scene active = SceneManager.GetActiveScene();
        Object[] selected = Selection.objects; Object selection = Selection.activeObject;
        var sources = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(scene => scene.isLoaded)
            .ToDictionary(scene => scene, scene => new { scene.isDirty, Roots = scene.GetRootGameObjects() });
        PlayerDataManager manager = PlayerDataManager.Instance; PlayerData data = manager != null ? manager.CurrentData : null;
        string player = data != null ? JsonUtility.ToJson(data) : null;
        var report = new StringBuilder("Native multiple-load inactive-source, frozen invitation and collider restoration checks\n");
        int checks = 0;
        try
        {
            foreach (ContractSO.VehicleTestMode mode in new[] { ContractSO.VehicleTestMode.Sequential, ContractSO.VehicleTestMode.Together })
                foreach (int count in new[] { 2, 3, 4 }) ValidateFixture(mode, count, report, ref checks);
            report.Insert(0, "PASS\n"); report.AppendLine(checks + " native assertions passed. No live physics run, game callback, reward or player save was invoked.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selected; Selection.activeObject = selection;
            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            bool safe = setup.Length == after.Length && setup.Zip(after, (a, b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(value => value) &&
                sources.All(pair => pair.Key.IsValid() && pair.Key.isLoaded && pair.Key.isDirty == pair.Value.isDirty && pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Roots)) &&
                manager == PlayerDataManager.Instance && (data == null || ReferenceEquals(data, manager.CurrentData) && JsonUtility.ToJson(data) == player);
            if (!safe) report.Insert(0, "FAILED SAFETY: source scene or player state changed.\n");
            else report.AppendLine("PASS: original scene setup, source roots/dirty flags, selection and player data preserved.");
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            if (report.ToString().StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(report.ToString()); else Debug.LogWarning(report.ToString());
        }
    }
    private static void ValidateFixture(ContractSO.VehicleTestMode mode, int count, StringBuilder report, ref int checks)
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); ContractSO contract = null;
        try
        {
            GameObject root = Owned(scene, "Native Roster " + mode + count); root.SetActive(false);
            BuildLocation site = root.AddComponent<BuildLocation>(); BridgePhysicsManager physics = root.AddComponent<BridgePhysicsManager>();
            contract = ScriptableObject.CreateInstance<ContractSO>(); contract.hideFlags = HideFlags.HideAndDontSave;
            contract.contractID = "native-roster-" + mode + count; contract.vehicleTestMode = mode; site.activeContract = contract;
            Point startAnchor = Owned(scene, "Start Anchor", root.transform).AddComponent<Point>(); startAnchor.transform.position = Vector3.zero;
            Point endAnchor = Owned(scene, "End Anchor", root.transform).AddComponent<Point>(); endAnchor.transform.position = new Vector3(10f, 0f, 0f);
            site.startingAnchors.Add(startAnchor); site.endingAnchors.Add(endAnchor);
            var vehicles = new List<LiveLoadVehicle>(); var solids = new List<BoxCollider>(); var triggers = new List<BoxCollider>();
            var disabled = new List<BoxCollider>(); var cargoes = new List<CargoItem>();
            for (int index = 0; index < count; index++)
            {
                var entry = new ContractLiveLoadEntry { entryId = "native-entry-" + index, displayName = "Vehicle " + (index + 1), emptyWeightKg = 1000f * (index + 1), departureDelay = index * 2f };
                contract.liveLoadTests.Add(entry);
                GameObject actor = Owned(scene, "Vehicle " + index, root.transform); actor.transform.position = new Vector3(-3f - index * 3f, 0f, 0f);
                BoxCollider solid = actor.AddComponent<BoxCollider>(); solid.size = new Vector3(2f, 1f, 1f);
                GameObject wheelObject = Owned(scene, "Small wheel", actor.transform);
                wheelObject.AddComponent<SphereCollider>().radius = .1f;
                BoxCollider trigger = actor.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                BoxCollider off = actor.AddComponent<BoxCollider>(); off.enabled = false;
                LiveLoadVehicle vehicle = actor.AddComponent<LiveLoadVehicle>(); vehicle.assignedContract = contract; vehicle.physicsManager = physics;
                vehicle.wheelObjects = new[] { wheelObject };
                vehicle.startPoint = Owned(scene, "Start " + index, root.transform).transform; vehicle.startPoint.position = actor.transform.position;
                vehicle.endPoint = Owned(scene, "End " + index, root.transform).transform; vehicle.endPoint.position = new Vector3(13f + index * 2f, 0f, 0f);
                site.liveLoadVehicles.Add(new ContractLiveLoadBinding { entryId = entry.entryId, vehicle = vehicle }); vehicle.BindMultiLoadEntry(site, entry.entryId);
                GameObject slotObject = Owned(scene, "Loaded cargo slot", actor.transform); VehicleCargoSlot slot = slotObject.AddComponent<VehicleCargoSlot>();
                slotObject.GetComponent<BoxCollider>().isTrigger = true; slot.vehicle = vehicle; slot.cargoSocket = slot.transform;
                CargoItem cargo = Owned(scene, "Authored loaded cargo", slot.transform).AddComponent<CargoItem>(); cargo.cargoWeight = 250f; slot.acceptedCargo = cargo;
                vehicles.Add(vehicle); solids.Add(solid); triggers.Add(trigger); disabled.Add(off); cargoes.Add(cargo);
            }
            Check(vehicles.All(vehicle => !vehicle.gameObject.activeInHierarchy), "Hidden source actors must stay inactive.", ref checks);
            var state = new MultiplayerChallengeState();
            Check(ChallengeLiveLoadRoster.TryFreeze(ref state, site, contract, out string reason), "Hidden completed-site invitation failed: " + reason, ref checks);
            Check(state.LiveLoadCount == count && state.LiveLoadMode == mode, "Invitation lost roster count or mode.", ref checks);
            for (int index = 0; index < count; index++)
            {
                Check(vehicles[index].TryGetMultiLoadStartBounds(out Bounds bounds) && Mathf.Approximately(bounds.size.x, 2f), "Full hidden chassis bounds must include overhang beyond the small wheel.", ref checks);
                Check(Mathf.Approximately(ChallengeLiveLoadRoster.Entry(state, index).WeightKg, 1000f * (index + 1) + 250f), "Payload was omitted or counted twice.", ref checks);
                contract.liveLoadTests[index].emptyWeightKg += 50000f; contract.liveLoadTests[index].departureDelay += 20f; cargoes[index].cargoWeight = 9000f;
            }
            contract.vehicleTestMode = mode == ContractSO.VehicleTestMode.Sequential ? ContractSO.VehicleTestMode.Together : ContractSO.VehicleTestMode.Sequential;
            LiveLoadTestRoster host = ChallengeLiveLoadRoster.FromState(state, site, contract);
            LiveLoadTestRoster guest = ChallengeLiveLoadRoster.FromState(state, site, contract);
            Check(host.Mode == mode && guest.Mode == mode, "Later contract edits changed the invited crossing mode.", ref checks);
            for (int index = 0; index < count; index++)
            {
                Check(host.Participants[index].WeightKg == guest.Participants[index].WeightKg && host.Participants[index].WeightKg == 1000f * (index + 1) + 250f,
                    "Submitted designs do not share the frozen cargo mass.", ref checks);
                Check(host.Participants[index].DepartureDelay == index * 2f && host.Participants[index].EntryId == "native-entry-" + index,
                    "Later edits changed invitation timing or identity.", ref checks);
            }
            var session = new LiveLoadTestSession(physics, host); session.Prepare();
            for (int index = 0; index < count; index++)
            {
                Check(vehicles[index].TotalTestWeight == host.Participants[index].WeightKg, "Native vehicle did not use its frozen test mass.", ref checks);
                vehicles[index].ParkForMultiLoadTest(session);
                Check(!solids[index].enabled && triggers[index].enabled && !disabled[index].enabled, "Passed parking must disable solids only.", ref checks);
            }
            session.Cancel(); session.Cancel();
            for (int index = 0; index < count; index++)
                Check(solids[index].enabled && triggers[index].enabled && !disabled[index].enabled && !vehicles[index].gameObject.activeInHierarchy,
                    "Cancellation did not restore exact collider or active flags.", ref checks);
            report.AppendLine("PASS: " + mode + "; " + count + " hidden vehicles; actual invitation freeze/reload, payloads, per-vehicle mass and exact parking restoration.");
        }
        finally { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); if (contract != null) Object.DestroyImmediate(contract); }
    }
    private static GameObject Owned(Scene scene, string name, Transform parent = null)
    { var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); if (parent != null) value.transform.SetParent(parent, false); return value; }
    private static void Check(bool condition, string message, ref int checks) { checks++; if (!condition) throw new InvalidOperationException(message); }
}
#endif
