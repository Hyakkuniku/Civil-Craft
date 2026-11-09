using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Detached eligibility/distribution checks and a read-only authored-map report.</summary>
public static class RandomChallengeValidation
{
    public const string ReportPath = "Logs/random-challenge-sites.txt";
    private static int assertions;

    [MenuItem("Tools/Civil Craft/Multiplayer/Validate Random Challenge Destinations")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before detached checks.");
        var report = new StringBuilder(); assertions = 0;
        try
        {
            ValidateFixtures(report);
            AuditMap(report);
            report.AppendLine("PASS: " + assertions + " detached selection, eligibility and source-preservation assertions.");
            report.AppendLine("No player saves, progression, live session, original vehicle, contract asset or authored scene was modified.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            throw;
        }
        finally
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, report.ToString());
        }
        Debug.Log(report.ToString());
    }

    private static void ValidateFixtures(StringBuilder report)
    {
        Scene original = SceneManager.GetActiveScene();
        Scene fixture = EditorSceneManager.NewPreviewScene();
        fixture.name = FusionConnectionManager.HostWorldSceneName;
        var assets = new List<Object>();
        try
        {
            string prefix = "Detached Random " + Guid.NewGuid().ToString("N");
            GameObject root = New(fixture, prefix);
            GameObject segment = New(fixture, "Material visual", root.transform);
            BridgeMaterialSO road = ScriptableObject.CreateInstance<BridgeMaterialSO>();
            road.name = prefix + " Road"; road.isRoad = true; road.segmentPrefab = segment; assets.Add(road);
            BridgeMaterialSO beam = ScriptableObject.CreateInstance<BridgeMaterialSO>();
            beam.name = prefix + " Beam"; beam.segmentPrefab = segment; assets.Add(beam);
            ContractSO contract = ScriptableObject.CreateInstance<ContractSO>(); assets.Add(contract);
            contract.name = prefix + " Contract"; contract.contractID = "random_fixture_" + Guid.NewGuid().ToString("N");
            contract.budget = 100000; contract.liveLoadWeight = 5000;
            contract.liveLoadMode = ContractSO.LiveLoadMode.Vehicle; contract.winCondition = ContractSO.WinCondition.FinishLine;
            contract.allowedMaterials = new List<MaterialAllowance> { new MaterialAllowance { material = road }, new MaterialAllowance { material = beam } };
            var sites = new List<BuildLocation>();
            for (int i = 0; i < 3; i++)
            {
                ContractSO siteContract = i == 0 ? contract : Object.Instantiate(contract);
                if (i != 0)
                {
                    siteContract.contractID += "_" + i;
                    assets.Add(siteContract);
                }
                sites.Add(CreateSite(fixture, root.transform, i, siteContract));
            }
            var player = New(fixture, "Detached player", root.transform).AddComponent<CharacterController>();
            player.radius = .3f; player.height = 1.8f; player.center = Vector3.up * .9f;
            player.transform.position = new Vector3(-100, 5, -100);
            Physics.SyncTransforms();
            foreach (BuildLocation site in sites)
                Check(RandomChallengeSiteSelector.IsValidSite(site, out string reason), "Valid unfinished site rejected: " + reason);
            BuildLocation selected;
            Check(RandomChallengeSiteSelector.TrySelect(sites.ToArray(), player, out selected, out string message) && sites.Contains(selected),
                "Safe valid sites must be selectable: " + message);
            Check(RandomChallengeSiteSelector.TryGetLandings(selected, player, out Vector3 host, out Vector3 guest) &&
                Vector3.Distance(host, guest) >= player.radius * 2f, "Both challenge players need distinct safe landings.");
            Check(!RandomChallengeSiteSelector.TrySelect(sites.ToArray(), null, out _, out _), "Missing travel player must reject.");
            Check(!RandomChallengeSiteSelector.TrySelect(Array.Empty<BuildLocation>(), player, out _, out _), "Empty maps must reject.");
            Check(!RandomChallengeSiteSelector.IsValidSite(null, out _), "Missing site must reject.");
            ValidateMutualCompletion(sites.ToArray(), player, root.transform);

            BuildLocation sample = sites[0];
            string sourceBefore = EditorJsonUtility.ToJson(sample);
            string contractBefore = EditorJsonUtility.ToJson(contract);
            contract.isTutorialContract = true;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Tutorial contracts must not enter the random pool.");
            contract.isTutorialContract = false;
            contract.isTimeAttack = true;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Time attacks must not enter the vehicle challenge pool.");
            contract.isTimeAttack = false;
            contract.winCondition = ContractSO.WinCondition.Timer;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Unsupported win conditions must reject.");
            contract.winCondition = ContractSO.WinCondition.FinishLine;
            contract.liveLoadMode = ContractSO.LiveLoadMode.PlayerCarriedCargo;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Player-carried cargo must reject.");
            contract.liveLoadMode = ContractSO.LiveLoadMode.Vehicle;
            contract.budget = float.NaN;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Nonfinite budget must reject.");
            contract.budget = 100000;
            float originalWidth = contract.twoWayRoadWidth;
            contract.twoWayRoad = true; contract.twoWayRoadWidth = -1f;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Invalid road width must reject.");
            contract.twoWayRoad = false; contract.twoWayRoadWidth = originalWidth;
            sample.endingAnchors.Add(sample.startingAnchors[0]);
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Duplicated anchors must reject.");
            sample.endingAnchors.RemoveAt(sample.endingAnchors.Count - 1);
            Point savedAnchor = sample.endingAnchors[0]; sample.endingAnchors[0] = null;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Missing anchors must reject.");
            sample.endingAnchors[0] = savedAnchor;
            Camera savedCamera = sample.locationCamera; sample.locationCamera = null;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Missing build camera must reject.");
            sample.locationCamera = savedCamera;
            savedCamera.gameObject.SetActive(false);
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "An inactive camera object would produce a black challenge view.");
            savedCamera.gameObject.SetActive(true);
            sample.gridImage.gameObject.SetActive(false);
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "An inactive grid object must reject.");
            sample.gridImage.gameObject.SetActive(true);
            Transform ground = sample.transform.Find("Landing ground");
            savedCamera.transform.SetParent(root.transform, true);
            sample.gridImage.transform.SetParent(root.transform, true);
            ground.SetParent(root.transform, true);
            sample.gameObject.SetActive(false);
            Check(RandomChallengeSiteSelector.IsValidSite(sample, out message), "An inactive workbench with separately active views and geometry remains valid: " + message);
            Check(RandomChallengeSiteSelector.TrySelect(new[] { sample }, player, out selected, out message) && selected == sample,
                "Inactive unfinished workbenches can still be selected without activation: " + message);
            Check(!sample.gameObject.activeSelf, "Selecting an inactive workbench must not activate its story scripts.");
            sample.gameObject.SetActive(true);
            savedCamera.transform.SetParent(sample.transform, true);
            sample.gridImage.transform.SetParent(sample.transform, true);
            ground.SetParent(sample.transform, true);
            string otherName = sites[1].name; sites[1].name = sample.name;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "Ambiguous hierarchy paths must reject.");
            sites[1].name = otherName;
            LiveLoadVehicle vehicle = ChallengeTestDescriptor.SelectVehicle(sample, contract);
            Vector3 end = vehicle.endPoint.position; vehicle.endPoint.position = vehicle.startPoint.position;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "A route unable to cross must reject.");
            vehicle.endPoint.position = end;
            Vector3 position = vehicle.transform.position; vehicle.transform.position = sample.startingAnchors[0].transform.position;
            Check(!RandomChallengeSiteSelector.IsValidSite(sample, out _), "A vehicle starting on the bridge must reject.");
            vehicle.transform.position = position;
            Check(RandomChallengeSiteSelector.IsValidSite(sample, out message), "Restored source must remain eligible: " + message);
            Check(EditorJsonUtility.ToJson(sample) == sourceBefore && EditorJsonUtility.ToJson(contract) == contractBefore,
                "Read-only selection must not change site or contract configuration.");

            // Contract locking can clear activeContract at Start. The authored
            // definition survives; selecting it must not accept a story contract.
            MethodInfo awake = typeof(BuildLocation).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake.Invoke(sample, null);
            sample.activeContract = null;
            Check(sample.ChallengeContractDefinition == contract && RandomChallengeSiteSelector.IsValidSite(sample, out message),
                "A story-locked site's preserved authored definition must remain challenge eligible: " + message);
            Check(sample.activeContract == null, "Challenge preflight must not unlock or assign the story contract.");

            ContractSO.VehicleTestMode originalMode = contract.vehicleTestMode;
            List<ContractLiveLoadEntry> originalEntries = contract.liveLoadTests;
            List<ContractLiveLoadBinding> originalBindings = sample.liveLoadVehicles;
            var second = New(fixture, "Second temporary test vehicle", sample.transform).AddComponent<LiveLoadVehicle>();
            second.transform.position = vehicle.transform.position - Vector3.right * 5f;
            second.gameObject.AddComponent<BoxCollider>().size = new Vector3(2, 1, 1);
            second.assignedContract = contract; second.physicsManager = vehicle.physicsManager;
            second.startPoint = New(fixture, "Second route start", second.transform).transform; second.startPoint.position = second.transform.position;
            second.endPoint = New(fixture, "Second route end", second.transform).transform; second.endPoint.position = vehicle.endPoint.position;
            try
            {
                contract.vehicleTestMode = ContractSO.VehicleTestMode.Together;
                contract.liveLoadTests = new List<ContractLiveLoadEntry> {
                    new ContractLiveLoadEntry { entryId = "fixture_first", displayName = "First truck", emptyWeightKg = 5000 },
                    new ContractLiveLoadEntry { entryId = "fixture_second", displayName = "Second truck", emptyWeightKg = 7000, departureDelay = 1f }
                };
                sample.liveLoadVehicles = new List<ContractLiveLoadBinding> {
                    new ContractLiveLoadBinding { entryId = "fixture_first", vehicle = vehicle },
                    new ContractLiveLoadBinding { entryId = "fixture_second", vehicle = second }
                };
                Check(LiveLoadTestRoster.TryCreate(sample, contract, out LiveLoadTestRoster roster, out message, false) && roster.Participants.Count == 2,
                    "Locked multi-load sites must support read-only roster validation: " + message);
                Check(!LiveLoadTestRoster.TryCreate(sample, contract, out _, out _, true),
                    "Story simulation must still require the active story contract.");
                MultiplayerChallengeState invitation = default;
                Check(ChallengeLiveLoadRoster.TryFreeze(ref invitation, sample, contract, out message) && invitation.LiveLoadCount == 2 &&
                    Mathf.Approximately(invitation.LiveLoad0.WeightKg, 5000f) && Mathf.Approximately(invitation.LiveLoad1.WeightKg, 7000f),
                    "A locked site's invitation must freeze both vehicles and their masses: " + message);
                Check(RandomChallengeSiteSelector.IsValidSite(sample, out message), "A valid unfinished multi-load crossing must enter the random pool: " + message);
                Check(sample.activeContract == null, "Freezing the multi-load invitation must not assign/unlock the story contract.");
            }
            finally
            {
                contract.vehicleTestMode = originalMode; contract.liveLoadTests = originalEntries; sample.liveLoadVehicles = originalBindings;
                Object.DestroyImmediate(second.gameObject);
            }
            sample.activeContract = contract;

            MethodInfo chooser = typeof(RandomChallengeSiteSelector).GetMethod("ChooseUniformly", BindingFlags.Static | BindingFlags.NonPublic);
            var random = new System.Random(14871);
            Func<int, int> next = random.Next;
            int[] counts = new int[sites.Count];
            for (int run = 0; run < 12000; run++)
            {
                BuildLocation choice = (BuildLocation)chooser.Invoke(null, new object[] { sites, next });
                counts[sites.IndexOf(choice)]++;
            }
            Check(counts.All(count => count > 3600 && count < 4400), "Reservoir selection is biased by map enumeration order.");
            report.AppendLine("PASS: Deterministic distribution over three eligible locations: " + string.Join(", ", counts) + " / 12000.");
            report.AppendLine("PASS: Detached fixtures cover unfinished/locked sites, setup rejection, safe host/guest landings and read-only selection.");
        }
        finally
        {
            foreach (Object asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            EditorSceneManager.ClosePreviewScene(fixture);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
    }

    private static void ValidateMutualCompletion(BuildLocation[] sites, CharacterController player, Transform parent)
    {
        ContractSO[] contracts = sites.Select(site => site.ChallengeContractDefinition).ToArray();
        ChallengeCompletionCatalog catalog = ChallengeCompletionCatalog.Create(contracts.Select(contract => contract.ContractID));
        GameObject hostObject = New(parent.gameObject.scene, "Personal host save (detached)", parent);
        GameObject guestObject = New(parent.gameObject.scene, "Personal guest save (detached)", parent);
        hostObject.SetActive(false); guestObject.SetActive(false);
        var host = hostObject.AddComponent<PlayerDataManager>();
        var guest = guestObject.AddComponent<PlayerDataManager>();
        host.allGameContracts = contracts.ToList(); guest.allGameContracts = contracts.ToList();
        var hostData = new PlayerData(); var guestData = new PlayerData();
        FieldInfo dataField = typeof(PlayerDataManager).GetField("<CurrentData>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        dataField.SetValue(host, hostData); dataField.SetValue(guest, guestData);
        string first = contracts[0].ContractID, second = contracts[1].ContractID;
        hostData.completedContracts.Add(first); hostData.completedContracts.Add(second);
        hostData.savedBridges.Add(CompletedBridge(first)); hostData.savedBridges.Add(CompletedBridge(second));
        guestData.completedContracts.Add(first); // An incomplete record has no personal saved bridge.
        guestData.completedContracts.Add(second); guestData.savedBridges.Add(CompletedBridge(second));
        string hostBefore = JsonUtility.ToJson(hostData), guestBefore = JsonUtility.ToJson(guestData);
        catalog.Capture(host.IsContractCompleted, out ulong hostLow, out ulong hostHigh);
        catalog.Capture(guest.IsContractCompleted, out ulong guestLow, out ulong guestHigh);
        Predicate<BuildLocation> shared = site => catalog.HasCompletion(site.ChallengeContractDefinition.ContractID,
            catalog.Fingerprint, true, hostLow, hostHigh) && catalog.HasCompletion(site.ChallengeContractDefinition.ContractID,
            catalog.Fingerprint, true, guestLow, guestHigh);
        Check(!guest.IsContractCompleted(first), "A host bridge or incomplete record must not count as the guest's personal completion.");
        Check(RandomChallengeSiteSelector.TrySelect(sites, player, out BuildLocation chosen, out string message, shared) && chosen == sites[1],
            "The random pool must contain only crossings finished by both players: " + message);
        Check(JsonUtility.ToJson(hostData) == hostBefore && JsonUtility.ToJson(guestData) == guestBefore,
            "Checking both personal saves must not mutate either save.");
        Check(!RandomChallengeSiteSelector.TrySelect(sites, player, out _, out message, site => false) && message.Contains("both players"),
            "An empty shared-completion pool must give a clear message.");
        guestData.savedBridges.Add(CompletedBridge(first));
        catalog.Capture(guest.IsContractCompleted, out guestLow, out guestHigh);
        Check(shared(sites[0]) && shared(sites[1]), "A newly completed personal bridge must join the shared pool after refresh.");
        guestData.savedBridges.RemoveAll(bridge => bridge.contractId == second);
        catalog.Capture(guest.IsContractCompleted, out guestLow, out guestHigh);
        Check(!shared(sites[1]) && shared(sites[0]), "A missing saved bridge must leave the shared pool after refresh.");
        Check(!catalog.HasCompletion(first, catalog.Fingerprint, false, guestLow, guestHigh), "Unready completion data must fail closed.");
        Check(!catalog.HasCompletion(first, "different client catalog", true, guestLow, guestHigh), "Mismatched client catalogs must fail closed.");
    }

    private static SavedBridgeData CompletedBridge(string id) => new SavedBridgeData
    {
        contractId = id,
        points = new List<SavedPointData> {
            new SavedPointData { index = 0, position = new SerializableVector3(Vector3.zero), isAnchor = true },
            new SavedPointData { index = 1, position = new SerializableVector3(Vector3.right * 20f), isAnchor = true }
        },
        bars = new List<SavedBarData> { new SavedBarData { startPointIndex = 0, endPointIndex = 1, materialName = "FixtureRoad" } }
    };

    private static BuildLocation CreateSite(Scene scene, Transform parent, int index, ContractSO definition)
    {
        GameObject root = New(scene, "Crossing " + index, parent);
        // Keep default-physics Editor queries away from any other loaded world.
        root.transform.position = new Vector3(-50000f + index * 100f, 0f, -50000f);
        BuildLocation site = root.AddComponent<BuildLocation>(); site.activeContract = definition;
        GameObject camera = New(scene, "Build camera", root.transform); site.locationCamera = camera.AddComponent<Camera>(); site.locationCamera.enabled = false;
        GameObject grid = New(scene, "Grid", root.transform, typeof(RectTransform), typeof(CanvasRenderer)); site.gridImage = grid.AddComponent<Image>();
        Point left = New(scene, "Left", root.transform).AddComponent<Point>(); left.transform.position = root.transform.position;
        Point right = New(scene, "Right", root.transform).AddComponent<Point>(); right.transform.position = root.transform.position + Vector3.right * 20;
        site.startingAnchors.Add(left); site.endingAnchors.Add(right);
        GameObject landing = New(scene, "Landing", root.transform); landing.transform.position = root.transform.position + new Vector3(-10, 1, 10);
        site.fastTravelTarget = landing.transform;
        GameObject floor = New(scene, "Landing ground", root.transform);
        floor.transform.position = root.transform.position + new Vector3(-10, -.5f, 10);
        floor.AddComponent<BoxCollider>().size = new Vector3(30, 1, 30);
        var physics = New(scene, "Physics", root.transform).AddComponent<BridgePhysicsManager>();
        var vehicle = New(scene, "Vehicle", root.transform).AddComponent<LiveLoadVehicle>();
        vehicle.transform.position = root.transform.position + new Vector3(-5, 1, 0);
        vehicle.gameObject.AddComponent<BoxCollider>().size = new Vector3(2, 1, 1);
        vehicle.assignedContract = definition; vehicle.physicsManager = physics;
        vehicle.startPoint = New(scene, "Start", root.transform).transform; vehicle.startPoint.position = vehicle.transform.position;
        vehicle.endPoint = New(scene, "End", root.transform).transform; vehicle.endPoint.position = root.transform.position + new Vector3(25, 1, 0);
        return site;
    }

    private static void AuditMap(StringBuilder report)
    {
        const string path = "Assets/Scenes/CanyonCrossing.unity";
        if (!File.Exists(path)) throw new InvalidOperationException("The authored host-world scene is missing.");
        Scene current = SceneManager.GetSceneByPath(path);
        bool opened = !current.IsValid() || !current.isLoaded;
        bool listed = EditorSceneManager.GetSceneManagerSetup().Any(item => item.path == path);
        Scene active = SceneManager.GetActiveScene();
        if (opened) current = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            BuildLocation[] sites = current.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BuildLocation>(true))
                .OrderBy(MultiplayerChallengeRules.SiteKey, StringComparer.Ordinal).ToArray();
            CharacterController player = current.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerMotor>(true))
                .Select(motor => motor.GetComponent<CharacterController>()).FirstOrDefault(controller => controller != null);
            int eligible = 0, eligibleWithLandings = 0;
            report.AppendLine("AUTHORED STRUCTURAL SETUP AUDIT (both players' personal completion is checked separately online):");
            foreach (BuildLocation site in sites)
            {
                bool valid = RandomChallengeSiteSelector.IsValidSite(site, out string reason);
                if (valid) eligible++;
                bool landings = valid && player != null && RandomChallengeSiteSelector.TryGetLandings(site, player, out _, out _);
                if (landings) eligibleWithLandings++;
                report.AppendLine((valid ? "ELIGIBLE: " : "EXCLUDED: ") + MultiplayerChallengeRules.SiteKey(site) +
                    " | " + (site.ChallengeContractDefinition != null ? site.ChallengeContractDefinition.DisplayName : "No authored contract") +
                    (valid ? player == null ? " | landing check unavailable: no authored player controller" :
                        landings ? " | both landing spaces clear" : " | no clear host/guest landing pair" : " | " + reason));
            }
            report.AppendLine("Structurally ready authored setups: " + eligible + " / " + sites.Length + ".");
            report.AppendLine(player != null ? "Eligible with actual authored player dimensions and both clear landings: " + eligibleWithLandings + "." :
                "Actual landing pool not checked: no authored player controller was present.");
            Check(eligible > 0, "No authored production location passed setup eligibility.");
            if (player != null) Check(eligibleWithLandings > 0, "No production crossing has safe landings for both players.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(current, !listed);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static GameObject New(Scene scene, string name, Transform parent = null, params Type[] components)
    {
        GameObject item = new GameObject(name, components); SceneManager.MoveGameObjectToScene(item, scene);
        if (parent != null) item.transform.SetParent(parent, false);
        return item;
    }
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
}
