#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

internal static class SteelMaterialValidation
{
    private const string Request = "Temp/steel-materials-validation.request";
    private const string Report = "Logs/steel-materials-validation.txt";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Tolerance = .015f;

    [InitializeOnLoadMethod]
    private static void WatchRequest() => EditorApplication.update += CheckRequest;

    private static void CheckRequest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Materials/Steel/Validate Imported Materials")]
    public static void Validate()
    {
        var results = new List<string>();
        Scene scene = default;
        ContractSO testContract = null;
        try
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && GameManager.Instance == null,
                "Run in Edit Mode with no gameplay session.");
            scene = EditorSceneManager.NewPreviewScene();
            testContract = ScriptableObject.CreateInstance<ContractSO>();
            BridgeMaterialSO beam = Load("SteelBeam");
            BridgeMaterialSO rope = Load("SteelRope");
            BridgeMaterialSO pier = Load("SteelPier");
            BridgeMaterialSO woodRoad = Load("Wood Road");
            BridgeMaterialSO concreteRoad = Load("Concrete Road");
            BridgeMaterialSO supportRope = Load("RopeMaterial");
            CheckBalance(beam, "Steel Beam", 900f, 60f, 20f, 1200000f, 2600000f);
            CheckBalance(rope, "Steel Rope", 400f, 5f, 70f, 450000f, 0f);
            Require(!beam.isRope && !beam.useSpring && rope.isRope && rope.useSpring &&
                rope.spring == 300000f && rope.damper == 2000f, "Wrong steel beam/rope joint settings.");
            CheckSavedIdentity(beam, rope, pier);
            results.Add("PASS: permanent IDs, Resources save-name lookup, introduction assets and requested numeric presets.");

            CheckContractPermissions(scene, testContract, beam, rope, pier);
            results.Add("PASS: steel selection, placement and clipboard gates match explicit allowances. The temporary contract allows each entry independently.");

            CheckOnePiecePier(scene, pier, woodRoad);
            CheckOnePiecePier(scene, pier, concreteRoad);
            results.Add("PASS: Steel Pier spans both full road widths and seats beneath flat, sloped and displaced road meshes while transferring compression to its base.");

            foreach (Vector3 end in new[] { new Vector3(8f, 0f), new Vector3(8f, 3f), new Vector3(-8f, 3f) })
            {
                Bar wood = Draw(scene, woodRoad, Vector3.zero, end);
                Bar concrete = Draw(scene, concreteRoad, Vector3.zero, end);
                float roadDepth = Mathf.Max(RoadHalfDepth(wood), RoadHalfDepth(concrete));
                CheckPlacedBar(Draw(scene, beam, Vector3.zero, end), beam, end.magnitude, roadDepth);
                CheckPlacedBar(Draw(scene, rope, Vector3.zero, end), rope, end.magnitude, roadDepth);
            }
            CheckDrawingLimit(scene, beam);
            CheckDrawingLimit(scene, rope);
            results.Add("PASS: flat, uphill and reversed members reach their joints, clear both road decks, charge paired cost and use the 20 m/70 m draw limits.");

            CheckSolver(scene, beam, rope, concreteRoad);
            CheckRopeVisual(scene, rope, new Color(.5f, .5f, .5f, 1f), true);
            CheckRopeVisual(scene, supportRope, new Color(.72f, .43f, .25f, 1f), false);
            results.Add("PASS: production truss/cable analysis, steel-gray simulation lines, slack sag, and legacy brown Support Rope.");

            CheckWideRoads(scene, testContract, beam, rope, pier, woodRoad, concreteRoad);
            results.Add("PASS: 16 m and custom roads, scaled receipts and Rigidbody mass, side clearances, pier fit, road colliders, saved bridge reconstruction, approach warnings, and host-width challenge/guest geometry.");

        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (testContract != null) Object.DestroyImmediate(testContract);
            Directory.CreateDirectory("Logs");
            File.WriteAllLines(Report, results);
            Debug.Log(string.Join("\n", results));
        }
    }

    private static BridgeMaterialSO Load(string assetName)
    {
        BridgeMaterialSO material = Resources.Load<BridgeMaterialSO>(assetName);
        Require(material != null, "Missing imported Resources material " + assetName + ".");
        return material;
    }

    private static void CheckWideRoads(Scene scene, ContractSO contract, BridgeMaterialSO beam, BridgeMaterialSO rope,
        BridgeMaterialSO pier, params BridgeMaterialSO[] roads)
    {
        Require(!contract.twoWayRoad && contract.twoWayRoadWidth == 16f && contract.GetWideRoadWidth() == 0f &&
            !ContractSO.IsValidTwoWayRoadWidth(float.NaN) && !ContractSO.IsValidTwoWayRoadWidth(7f) &&
            !ContractSO.IsValidTwoWayRoadWidth(float.PositiveInfinity), "Unexpected width defaults or validation.");
        var site = NewObject(scene, "Wide road fixture").AddComponent<BuildLocation>();
        site.enabled = false;
        site.activeContract = contract;
        var physics = NewObject(scene, "Wide road fixture physics").AddComponent<BridgePhysicsManager>();
        var setupCollider = typeof(BridgePhysicsManager).GetMethod("ApplyPhysicsToBar", PrivateInstance);
        var bakeCollider = typeof(BridgePhysicsManager).GetMethod("EnsurePermanentBakedRoadCollider", PrivateInstance);
        foreach (BridgeMaterialSO road in roads)
        {
            Vector3 start = new Vector3(0f, 10f, 0f);
            Vector3 end = start + new Vector3(8f, 0f, 0f);
            contract.twoWayRoad = false;
            Bar standard = Draw(scene, road, start, end, site);
            float nativeWidth = GetRoadBounds(standard.gameObject).size.z;
            Require(standard.WideRoadWidth == 0f && standard.PlacedMassPerMeter == road.massPerMeter &&
                Mathf.Abs(standard.GetCost() - (end - start).magnitude * road.costPerMeter) < .02f, "Legacy road rates changed.");

            contract.twoWayRoad = true;
            foreach (float width in new[] { 16f, 19f })
            {
                end.y = width == 16f ? start.y : start.y + 2f;
                contract.twoWayRoadWidth = width;
                Point upper = Node(scene, start, true), far = Node(scene, end, true);
                upper.AssignOwner(site); far.AssignOwner(site);
                Bar wide = Draw(scene, road, start, end, site);
                wide.startPoint = upper; wide.endPoint = far; wide.NormalizeEndpointOrder();
                float length = Vector3.Distance(start, end);
                float price = road.costPerMeter * width / nativeWidth;
                float mass = road.massPerMeter * width / nativeWidth;
                Bounds wideBounds = GetRoadBounds(wide.gameObject);
                Require(Mathf.Abs(wideBounds.size.z - width) < Tolerance && Mathf.Abs(wideBounds.center.z) < Tolerance &&
                    wide.RoadColliderWidth == width &&
                    Mathf.Abs(wide.GetCost() - length * price) < .1f && Mathf.Abs(wide.PlacedMassPerMeter - mass) < .02f,
                    $"{road.GetDisplayName()} at {width} m: mesh {wideBounds.size.z:F3} m at Z {wideBounds.center.z:F3}, " +
                    $"collider {wide.RoadColliderWidth:F3} m, cost {wide.GetCost():F2}/{length * price:F2}, " +
                    $"mass per metre {wide.PlacedMassPerMeter:F3}/{mass:F3} (native width {nativeWidth:F3} m).");
                var receipt = BridgeConstructionReceipt.Capture(site, new[] { wide });
                Require(Mathf.Abs(receipt.TotalCost - wide.GetCost()) < .05f &&
                    Mathf.Abs(receipt.GetCostPerMeter(road) - price) < .02f, "Receipt must use the placed road rate.");
                setupCollider.Invoke(physics, new object[] { wide });
                Require(wide.GetComponent<BoxCollider>().size.z == width &&
                    Mathf.Abs(wide.GetComponent<Rigidbody>().mass - mass * length) < .1f, "Simulation deck collider or mass differs.");
                bakeCollider.Invoke(physics, new object[] { wide });
                Require(wide.GetComponent<BoxCollider>().size.z == width, "Baked deck collider lost its contract width.");
                CheckWideRoadReplica(site, road, wide, start, end);
                CheckWideRoadReload(scene, contract, road, wide, start, end);
                site.startingAnchors.Add(upper);
                site.endingAnchors.Add(far);
                var graph = new ChallengeBridgeSubmission();
                graph.Nodes.Add(new ChallengeSubmittedNode { Position = start, Anchor = 0 });
                graph.Nodes.Add(new ChallengeSubmittedNode { Position = end, Anchor = 1 });
                graph.Bars.Add(new ChallengeSubmittedBar { Start = 0, End = 1, Material = road.Id });
                contract.twoWayRoad = false;
                var error = ChallengeBridgeSubmissionRules.Validate(graph, site, contract,
                    new Dictionary<string, BridgeMaterialSO> { [road.Id] = road }, 0f, out float challengeCost,
                    wideRoadWidthOverride: width);
                contract.twoWayRoad = true;
                site.startingAnchors.Clear();
                site.endingAnchors.Clear();
                Require(error == ChallengeSubmissionError.None && Mathf.Abs(challengeCost - receipt.TotalCost) < .05f,
                    "Challenge pricing did not keep the host's frozen width.");

                foreach (BridgeMaterialSO side in new[] { beam, rope })
                {
                    Bar member = Draw(scene, side, start, end, site);
                    foreach (MeshRenderer renderer in member.GetComponentsInChildren<MeshRenderer>())
                        Require(Mathf.Min(Mathf.Abs(renderer.bounds.min.z), Mathf.Abs(renderer.bounds.max.z)) > width * .5f,
                            "Paired support enters the driving lane.");
                    Require(member.PlacedMassPerMeter == side.GetPlacedMassPerMeter(), "Side support mass changed.");
                }
                foreach (BridgeMaterialSO column in new[] { pier, Load("Concrete Pier") })
                {
                    Bar support = Draw(scene, column, Vector3.zero, start, site);
                    support.startPoint = Node(scene, Vector3.zero); support.endPoint = upper;
                    support.NormalizeEndpointOrder();
                    MeshRenderer top = support.GetComponentsInChildren<MeshRenderer>().OrderByDescending(r => r.bounds.size.z).First();
                    Require(Mathf.Abs(top.bounds.size.z - width) < Tolerance && top.bounds.max.y < start.y + .25f,
                        "Pier bearing does not reach both deck edges or is above the deck.");
                    CheckWidePierReplica(site, support);
                }
            }
        }

        contract.twoWayRoadWidth = 16f;
        var aligner = site.gameObject.AddComponent<BridgeAbutmentAligner>();
        var approaches = new List<BridgeAbutmentAligner.Abutment>();
        for (int end = 0; end < 2; end++)
        {
            float x = end * 8f;
            Point anchor = Node(scene, new Vector3(x, 0f, 0f), true);
            (end == 0 ? site.startingAnchors : site.endingAnchors).Add(anchor);
            BoxCollider surface = NewObject(scene, "Approach fixture").AddComponent<BoxCollider>();
            surface.transform.position = new Vector3(x, -.225f, 0f);
            surface.size = new Vector3(2f, .5f, 16f);
            approaches.Add(new BridgeAbutmentAligner.Abutment { anchor = anchor, approachCollider = surface });
        }
        typeof(BridgeAbutmentAligner).GetField("abutments", PrivateInstance).SetValue(aligner, approaches);
        Require(MultiLoadBuildLocationEditor.CheckWideApproaches(site, contract, out string report), report);
        var narrow = (BoxCollider)approaches[0].approachCollider;
        narrow.size = new Vector3(2f, .5f, 10f);
        Require(!MultiLoadBuildLocationEditor.CheckWideApproaches(site, contract, out _), "Narrow approach should be flagged.");
    }

    private static void CheckWideRoadReplica(BuildLocation site, BridgeMaterialSO road, Bar wide, Vector3 start, Vector3 end)
    {
        Type replicaType = typeof(FusionMultiplayerAvatar).GetNestedType("BridgeBarVisual", BindingFlags.NonPublic);
        object replica = Activator.CreateInstance(replicaType, road, 1, site.transform, wide.WideRoadWidth);
        var replicaRoot = (GameObject)replicaType.GetField("Root").GetValue(replica);
        replicaType.GetMethod("SetEndpoints").Invoke(replica, new object[] { start, end, Vector4.zero });
        var collider = wide.GetComponent<BoxCollider>();
        var snapshot = new FusionMultiplayerAvatar.BridgeBarSnapshot
        {
            Committed = 1, WideRoadWidth = wide.WideRoadWidth, ShapeOffset = collider.center, ShapeSize = collider.size
        };
        replicaType.GetMethod("SetRoadCollider").Invoke(replica, new object[] { snapshot });
        Bounds bounds = GetRoadBounds(replicaRoot);
        Require(Mathf.Abs(bounds.size.z - wide.WideRoadWidth) < Tolerance && replicaRoot.GetComponent<BoxCollider>().size == collider.size,
            "Guest road geometry or collision differs from the host.");
    }

    private static void CheckWidePierReplica(BuildLocation site, Bar pier)
    {
        Type replicaType = typeof(FusionMultiplayerAvatar).GetNestedType("BridgeBarVisual", BindingFlags.NonPublic);
        object replica = Activator.CreateInstance(replicaType, pier.materialData, 1, site.transform, pier.WideRoadWidth);
        var replicaRoot = (GameObject)replicaType.GetField("Root").GetValue(replica);
        var captureSeat = typeof(Bar).GetMethod("CaptureWidePierSeat", PrivateInstance);
        var seat = (Vector4)captureSeat.Invoke(pier, null);
        replicaType.GetMethod("SetEndpoints").Invoke(replica,
            new object[] { pier.startPoint.transform.position, pier.endPoint.transform.position, seat });
        Require(seat.y > 0f, "Host did not capture the wide pier seat.");
        ComparePart("VisualSegment", "Bridge Segment");
        if (pier.materialData.pierCapPrefab != null) ComparePart("PierCap", "Pier Cap");

        void ComparePart(string hostName, string guestName)
        {
            Bounds host = GetRoadBounds(pier.transform.Find(hostName).gameObject);
            Bounds guest = GetRoadBounds(replicaRoot.transform.Find(guestName).gameObject);
            Require(Vector3.Distance(host.center, guest.center) < Tolerance && Vector3.Distance(host.size, guest.size) < Tolerance,
                $"Guest {pier.materialData.GetDisplayName()} {guestName} differs from the host seat. " +
                $"Host center/size {host.center:F3}/{host.size:F3}, guest {guest.center:F3}/{guest.size:F3}.");
        }
    }

    private static Bounds GetRoadBounds(GameObject root)
    {
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void CheckWideRoadReload(Scene scene, ContractSO contract, BridgeMaterialSO road, Bar wide,
        Vector3 start, Vector3 end)
    {
        var site = NewObject(scene, "Reloaded road fixture").AddComponent<BuildLocation>();
        site.enabled = false;
        site.activeContract = contract;
        var creator = NewObject(scene, "Reload fixture creator").AddComponent<BarCreator>();
        creator.enabled = false;
        creator.barParent = site.transform;
        creator.pointParent = site.transform;
        creator.barToInstantiate = NewObject(scene, "Reload fixture bar");
        creator.barToInstantiate.AddComponent<Bar>();
        creator.pointToInstantiate = Node(scene, Vector3.zero).gameObject;
        var saved = new SavedBridgeData();
        saved.points.Add(new SavedPointData { index = 0, position = new SerializableVector3(start) });
        saved.points.Add(new SavedPointData { index = 1, position = new SerializableVector3(end) });
        saved.bars.Add(new SavedBarData { startPointIndex = 0, endPointIndex = 1, materialName = road.name });
        SavedBridgeData reloaded = JsonUtility.FromJson<SavedBridgeData>(JsonUtility.ToJson(saved));
        var reconstruct = typeof(BuildLocation).GetMethod("ReconstructSavedBridge", PrivateInstance);
        Require((bool)reconstruct.Invoke(site, new object[] { reloaded, creator }), "Failed to reconstruct the saved road.");
        Bar restored = site.bakedBars.Single();
        Require(Mathf.Abs(GetRoadBounds(restored.gameObject).size.z - wide.WideRoadWidth) < Tolerance &&
            restored.GetComponent<BoxCollider>().size.z == wide.WideRoadWidth &&
            Mathf.Abs(restored.GetCost() - wide.GetCost()) < .05f, "Reload changed the road width, collider, or price.");
    }

    private static void CheckBalance(BridgeMaterialSO material, string label, float cost, float mass, float length,
        float tension, float compression)
    {
        Require(material.GetDisplayName() == label && material.Id == material.name &&
            material.materialIcon != null && material.PopupImage != null && material.AlmanacImage != null &&
            !string.IsNullOrWhiteSpace(material.introductionDescription) && !string.IsNullOrWhiteSpace(material.almanacDescription),
            label + " is missing player-facing content or its save key.");
        Require(material.isDualBeam && material.requiresExplicitContractAllowance && !material.isPier && !material.isRoad &&
            Mathf.Approximately(material.zOffset, 4.40f) && material.costPerMeter == cost && material.massPerMeter == mass &&
            material.GetPlacedMassPerMeter() == mass * 2 && material.maxLength == length &&
            material.maxTension == tension && material.maxCompression == compression, label + " balance differs from the approved preset.");
        Require(material.segmentPrefab != null && material.segmentPrefab.GetComponentsInChildren<MeshFilter>(true).Length == 1 &&
            material.segmentPrefab.GetComponentsInChildren<Collider>(true).Length == 0, label + " requires one visual mesh and no authored colliders.");
    }

    private static void CheckSavedIdentity(params BridgeMaterialSO[] materials)
    {
        var saved = new SavedBridgeData();
        foreach (BridgeMaterialSO material in materials)
            saved.bars.Add(new SavedBarData { startPointIndex = 0, endPointIndex = 1, materialName = material.name });
        SavedBridgeData reloaded = JsonUtility.FromJson<SavedBridgeData>(JsonUtility.ToJson(saved));
        BridgeMaterialSO[] resources = Resources.LoadAll<BridgeMaterialSO>(string.Empty);
        var catalog = ChallengeBridgeSubmissionRules.CreateMaterialCatalog(null);
        for (int index = 0; index < reloaded.bars.Count; index++)
        {
            string key = reloaded.bars[index].materialName;
            BridgeMaterialSO expected = materials[index];
            Require(key == expected.Id && resources.Single(material => material.name == key) == expected &&
                catalog.TryGetValue(key, out BridgeMaterialSO resolved) && resolved == expected,
                "Saved steel key did not resolve uniquely after serialization: " + key);
        }
    }

    private static void CheckContractPermissions(Scene scene, ContractSO testContract, params BridgeMaterialSO[] materials)
    {
        GameObject context = NewObject(scene, "Detached material gate context");
        context.SetActive(false);
        var manager = context.AddComponent<GameManager>();
        var clipboard = context.AddComponent<ClipboardManager>();
        var setManager = typeof(GameManager).GetProperty(nameof(GameManager.Instance)).GetSetMethod(true);
        var setContract = typeof(GameManager).GetProperty(nameof(GameManager.CurrentContract)).GetSetMethod(true);
        var placement = typeof(BarCreator).GetMethod("HasContractMaterialOptIn", BindingFlags.Static | BindingFlags.NonPublic);
        var paste = typeof(ClipboardManager).GetMethod("HasValidCopiedContent", PrivateInstance);
        var copiedBars = (List<CopiedBarInfo>)typeof(ClipboardManager).GetField("copiedBars", PrivateInstance).GetValue(clipboard);
        var copiedPoints = (List<Vector3>)typeof(ClipboardManager).GetField("copiedRelativePoints", PrivateInstance).GetValue(clipboard);
        copiedPoints.AddRange(new[] { Vector3.zero, Vector3.right });
        copiedBars.Add(new CopiedBarInfo { startIdx = 0, endIdx = 1 });
        GameManager previous = GameManager.Instance;
        try
        {
            setManager.Invoke(null, new object[] { manager });
            var contracts = AssetDatabase.FindAssets("t:ContractSO", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ContractSO>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(contract => contract != null).ToArray();
            Require(contracts.Length > 0, "No current contracts were found for the opt-in regression.");
            foreach (BridgeMaterialSO material in materials)
            {
                foreach (ContractSO contract in contracts)
                {
                    bool allowed = contract.allowedMaterials != null && contract.allowedMaterials.Any(entry => entry?.material == material);
                    bool hidden = contract.hiddenMaterials != null && contract.hiddenMaterials.Contains(material);
                    Check(contract, material, allowed && !hidden);
                }
                Check(testContract, material, false);
                testContract.allowedMaterials.Add(new MaterialAllowance { material = material });
                foreach (BridgeMaterialSO other in materials) Check(testContract, other, other == material);
                testContract.hiddenMaterials.Add(material);
                Check(testContract, material, false);
                testContract.allowedMaterials.Clear();
                testContract.hiddenMaterials.Clear();
            }
        }
        finally { setManager.Invoke(null, new object[] { previous }); }

        void Check(ContractSO contract, BridgeMaterialSO material, bool expected)
        {
            setContract.Invoke(manager, new object[] { contract });
            copiedBars[0].mat = material;
            Require(contract.IsMaterialHidden(material) != expected &&
                (bool)placement.Invoke(null, new object[] { material }) == expected &&
                (bool)paste.Invoke(clipboard, null) == expected, contract.name + " steel allowance differs for " + material.Id + ".");
        }
    }

    private static void CheckOnePiecePier(Scene scene, BridgeMaterialSO material, BridgeMaterialSO roadMaterial)
    {
        Require(material.isPier && !material.isDualBeam && !material.isRoad && !material.isRope &&
            material.pierCapPrefab == null && material.requiresExplicitContractAllowance &&
            material.materialIcon != null && !string.IsNullOrWhiteSpace(material.almanacDescription) &&
            material.costPerMeter == 1100f && material.GetPlacedMassPerMeter() == 170f && material.maxLength == 35f &&
            material.maxTension == 750000f && material.maxCompression == 3100000f && material.pierBucklingReferenceLength == 12f &&
            material.GetCompressionLimit(25f) < material.GetCompressionLimit(12f), "Invalid Steel Pier setup or buckling values.");
        var getRoad = typeof(Bar).GetMethod("TryPierSeatingRoad", PrivateInstance);
        foreach (float slopeRise in new[] { 0f, 3f, -3f })
        {
            Point ground = Node(scene, Vector3.zero);
            Point top = Node(scene, Vector3.up * 10f);
            Point far = Node(scene, new Vector3(8f, 10f + slopeRise), true);
            Bar pier = Draw(scene, material, ground.transform.position, top.transform.position);
            pier.startPoint = ground;
            pier.endPoint = top;
            pier.NormalizeEndpointOrder();
            MeshFilter shaft = pier.GetComponentsInChildren<MeshFilter>().Single();
            var surface = shaft.GetComponent<MeshRenderer>().sharedMaterial;
            Require(UsesUrpShader(surface), "Steel Pier needs a URP material.");
            Require(Mathf.Abs(shaft.GetComponent<Renderer>().bounds.max.y - 10f) < Tolerance,
                "Steel Pier must meet the upper node before a road is connected.");

            Bar road = Draw(scene, roadMaterial, top.transform.position, far.transform.position);
            road.startPoint = top;
            road.endPoint = far;
            road.NormalizeEndpointOrder();
            pier.RefreshPierCapSeating();
            Bounds bounds = shaft.GetComponent<Renderer>().bounds;
            float roadHalfDepth = RoadHalfDepth(road);
            Require(bounds.size.x < 2f && bounds.min.z <= -roadHalfDepth + Tolerance &&
                bounds.max.z >= roadHalfDepth - Tolerance,
                $"Steel Pier must span the full {roadMaterial.GetDisplayName()} width across the bridge. Pier {bounds.size.z:F3} m, deck {2f * roadHalfDepth:F3} m.");
            object[] args = { road, top, top.transform.position, 0f, 0f, 0f, 0f, 0f };
            Require((bool)getRoad.Invoke(pier, args), "Steel Pier must overlap the connected road footprint.");
            float slope = (float)args[3];
            float edge = slope >= 0f ? Mathf.Max(bounds.min.x, (float)args[6]) : Mathf.Min(bounds.max.x, (float)args[7]);
            float lowestDeck = top.transform.position.y + slope * edge + (float)args[4] / (float)args[5];
            Require(Mathf.Abs(bounds.max.y - lowestDeck) < Tolerance && Mathf.Abs(bounds.min.y) < Tolerance &&
                Mathf.Abs(pier.GetCost() - 11000f) < .05f && top.transform.position.y == 10f && ground.transform.position.y == 0f,
                $"Steel Pier seating on {roadMaterial.GetDisplayName()} rise {slopeRise:F1}. Mesh top {bounds.max.y:F4}, base {bounds.min.y:F4}, " +
                $"expected underside {lowestDeck:F4}, measured underside {LowestRenderedRoadUnderside(road, bounds):F4}, cost {pier.GetCost():F2}.");

            var result = DeterministicBridgeStressSolver.Analyze(new[] { ground, top, far }, new[] { pier, road }, 3000f, false, 11);
            Require(result != null && result.TryGetStress(pier, 5, out _, out float compression, out bool tension) &&
                compression > 0f && !tension, "Steel Pier must carry compression into its foundation.");

            if (slopeRise == 0f)
            {
                // Settling can move the road and pier bodies independently of their nodes.
                Vector3 roadPosition = road.transform.position;
                Quaternion roadRotation = road.transform.rotation;
                Vector3 pierPosition = pier.transform.position;
                road.transform.SetPositionAndRotation(roadPosition - Vector3.up * .20f,
                    Quaternion.AngleAxis(-4f, Vector3.forward) * roadRotation);
                pier.transform.position = pierPosition + Vector3.up * .15f;
                pier.RefreshPierCapSeating();
                Bounds displacedPier = shaft.GetComponent<Renderer>().bounds;
                float clearance = LowestRenderedRoadUnderside(road, displacedPier) - displacedPier.max.y;
                Require(clearance >= .001f && clearance <= .025f &&
                    top.transform.position.y == 10f && ground.transform.position.y == 0f,
                    $"Steel Pier must stay just below the displaced {roadMaterial.GetDisplayName()} surface. Clearance {clearance:F4} m.");
                road.transform.SetPositionAndRotation(roadPosition, roadRotation);
                pier.transform.position = pierPosition;
            }

            road.gameObject.SetActive(false);
            pier.RefreshPierCapSeating();
            Require(Mathf.Abs(shaft.GetComponent<Renderer>().bounds.max.y - 10f) < Tolerance,
                "The original pier height must be restored when the road is removed.");
        }
    }

    private static float LowestRenderedRoadUnderside(Bar road, Bounds pier)
    {
        float underside = float.PositiveInfinity;
        foreach (MeshFilter mesh in road.GetComponentsInChildren<MeshFilter>())
        {
            Bounds bounds = TransformMeshBounds(mesh, road.transform.worldToLocalMatrix);
            Vector3 first = road.transform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.min.z));
            Vector3 last = road.transform.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, bounds.max.z));
            float minX = Mathf.Max(first.x, pier.min.x);
            float maxX = Mathf.Min(last.x, pier.max.x);
            if (maxX <= minX || last.z <= pier.min.z || first.z >= pier.max.z) continue;
            float firstHeight = Mathf.Lerp(first.y, last.y, Mathf.InverseLerp(first.x, last.x, minX));
            float lastHeight = Mathf.Lerp(first.y, last.y, Mathf.InverseLerp(first.x, last.x, maxX));
            underside = Mathf.Min(underside, firstHeight, lastHeight);
        }
        return underside;
    }

    private static void CheckPlacedBar(Bar bar, BridgeMaterialSO material, float length, float roadDepth)
    {
        MeshFilter[] meshes = bar.GetComponentsInChildren<MeshFilter>();
        Require(meshes.Length == 2 && Mathf.Abs(bar.GetCost() - length * material.costPerMeter * 2) < .05f,
            material.Id + " must render and price a paired placement.");
        foreach (MeshFilter mesh in meshes)
        {
            Bounds bounds = TransformMeshBounds(mesh, bar.transform.worldToLocalMatrix);
            Require(Mathf.Abs(bounds.min.x + length * .5f) < Tolerance &&
                Mathf.Abs(bounds.max.x - length * .5f) < Tolerance && Mathf.Abs(bounds.center.y) < Tolerance,
                material.Id + " mesh does not meet the centered joint endpoints.");
            Require(bounds.size.z <= .60f + Tolerance && Mathf.Abs(Mathf.Abs(bounds.center.z) - 4.40f) < Tolerance &&
                Mathf.Abs(bounds.center.z) - bounds.extents.z >= roadDepth + .02f,
                material.Id + " mesh encroaches on the road deck or is misplaced across the bridge.");
            Material[] materials = mesh.GetComponent<MeshRenderer>().sharedMaterials;
            Require(materials.Length > 0 && materials.All(UsesUrpShader),
                material.Id + " requires a URP material. Found " +
                string.Join(", ", materials.Select(mat => mat != null && mat.shader != null ? mat.shader.name : "<missing>")) + ".");
        }
        Require(meshes[0].GetComponent<Renderer>().bounds.center.z * meshes[1].GetComponent<Renderer>().bounds.center.z < 0f,
            material.Id + " must place one visual segment on each side of the bridge.");
    }

    private static bool UsesUrpShader(Material material) => material != null && material.shader != null &&
        material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);

    private static void CheckDrawingLimit(Scene scene, BridgeMaterialSO material)
    {
        GameObject context = NewObject(scene, "Detached steel endpoint context");
        context.SetActive(false);
        var creator = context.AddComponent<BarCreator>();
        Vector3 start = new Vector3(0f, 20000f);
        creator.activeMaterial = material;
        creator.currentBar = Draw(scene, material, start, start + Vector3.right);
        creator.currentStartPoint = Node(scene, start);
        var resolve = typeof(BarCreator).GetMethods(PrivateInstance)
            .Single(method => method.Name == "ResolveDrawingEndpoint" && method.GetParameters().Length == 6);
        object[] arguments = { start + Vector3.right * (material.maxLength + 10f), null, Vector3.zero, null, null, null };
        resolve.Invoke(creator, arguments);
        Require(Mathf.Abs(Vector3.Distance(start, (Vector3)arguments[3]) - material.maxLength) < Tolerance,
            material.Id + " does not clamp the drawing endpoint at its approved length.");
    }

    private static void CheckSolver(Scene scene, BridgeMaterialSO beam, BridgeMaterialSO rope, BridgeMaterialSO road)
    {
        Point left = Node(scene, new Vector3(-8f, 0f), true);
        Point right = Node(scene, new Vector3(8f, 0f), true);
        Point deck = Node(scene, Vector3.zero);
        Point apex = Node(scene, new Vector3(0f, 5f));
        Point bottom = Node(scene, new Vector3(0f, -4f), true);
        var points = new[] { left, right, deck, apex, bottom };
        Bar compression = Member(left, apex, beam);
        Bar brace = Member(apex, deck, beam);
        var bars = new List<Bar> { Member(left, deck, road), Member(deck, right, road),
            compression, Member(right, apex, beam), brace };
        var truss = DeterministicBridgeStressSolver.Analyze(points, bars, 3000f, false, 11);
        Require(truss != null && truss.IsStructurallyStable && truss.PeakStructuralStress < 1f &&
            truss.TryGetStress(compression, 5, out _, out float compressionRatio, out bool isTension) &&
            compressionRatio > 0f && !isTension &&
            truss.TryGetStress(brace, 5, out _, out float tensionRatio, out isTension) && tensionRatio > 0f && isTension,
            "Steel truss must support the fixture with positive beam tension and compression.");

        bars.Remove(brace);
        Bar cable = Member(apex, deck, rope);
        Bar slack = Member(bottom, deck, rope);
        bars.Add(cable);
        bars.Add(slack);
        var supported = DeterministicBridgeStressSolver.Analyze(points, bars, 3000f, false, 11);
        Require(supported != null && supported.IsStructurallyStable && supported.PeakStructuralStress < 1f &&
            supported.TryGetStress(cable, 5, out _, out float cableRatio, out isTension) && cableRatio > 0f && isTension &&
            supported.TryGetStress(slack, 5, out float shownSlack, out float slackRatio, out _) &&
            shownSlack == 0f && slackRatio == 0f, "Steel cable must carry tension and release its slack side.");

        Bar Member(Point a, Point b, BridgeMaterialSO material)
        {
            Bar member = Draw(scene, material, a.transform.position, b.transform.position);
            member.startPoint = a;
            member.endPoint = b;
            return member;
        }
    }

    private static void CheckRopeVisual(Scene scene, BridgeMaterialSO material, Color expected, bool steel)
    {
        Vector3 end = Vector3.right * 5f;
        Bar bar = Draw(scene, material, Vector3.zero, end);
        bar.startPoint = Node(scene, Vector3.zero);
        bar.endPoint = Node(scene, end);
        bar.SetRopeSimulationVisual(true, 7f);
        LineRenderer[] lines = bar.GetComponentsInChildren<LineRenderer>();
        Require(lines.Length == (material.isDualBeam ? 2 : 1) &&
            lines.All(line => line.enabled && line.GetPosition(line.positionCount / 2).y < -.1f),
            material.Id + " must draw its sagging simulation strand(s).");
        foreach (LineRenderer line in lines)
        {
            Color tint = line.sharedMaterial.GetColor("_BaseColor");
            bool correct = steel
                ? tint.a >= .99f && Mathf.Max(tint.r, tint.g, tint.b) - Mathf.Min(tint.r, tint.g, tint.b) < .18f && tint.grayscale >= .25f
                : Vector4.Distance(tint, expected) < .002f;
            Require(correct, material.Id + " simulation tint is incorrect: " + tint + ".");
        }
        bar.SetRopeSimulationVisual(false);
        Require(lines.All(line => !line.enabled) && bar.GetComponentsInChildren<MeshRenderer>().All(mesh => mesh.enabled),
            material.Id + " must restore its mesh when leaving simulation.");
    }

    private static Bounds TransformMeshBounds(MeshFilter mesh, Matrix4x4 toTarget)
    {
        Bounds source = mesh.sharedMesh.bounds;
        Matrix4x4 transform = toTarget * mesh.transform.localToWorldMatrix;
        var result = new Bounds(transform.MultiplyPoint3x4(source.min), Vector3.zero);
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    result.Encapsulate(transform.MultiplyPoint3x4(new Vector3(x == 0 ? source.min.x : source.max.x,
                        y == 0 ? source.min.y : source.max.y, z == 0 ? source.min.z : source.max.z)));
        return result;
    }

    private static float RoadHalfDepth(Bar road) => road.GetComponentsInChildren<MeshFilter>()
        .Max(mesh => { Bounds bounds = TransformMeshBounds(mesh, Matrix4x4.identity);
            return Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z)); });

    private static Bar Draw(Scene scene, BridgeMaterialSO material, Vector3 start, Vector3 end, BuildLocation owner = null)
    {
        Bar bar = NewObject(scene, material.Id).AddComponent<Bar>();
        bar.AssignOwner(owner);
        bar.Initialize(material);
        bar.StartPosition = start;
        bar.UpdateCreatingBar(end);
        return bar;
    }

    private static Point Node(Scene scene, Vector3 position, bool anchor = false)
    {
        GameObject obj = NewObject(scene, "Steel fixture node");
        obj.transform.position = position;
        Point point = obj.AddComponent<Point>();
        point.originalIsAnchor = point.isAnchor = anchor;
        return point;
    }

    private static GameObject NewObject(Scene scene, string name)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        return obj;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
