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

[InitializeOnLoad]
internal static class IndustrialAnchorRoadAuthoring
{
    private const string Request = "Temp/industrial-anchor-road.request";
    private const string Report = "Logs/industrial-anchor-road.txt";
    static IndustrialAnchorRoadAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (command == "inspect") Inspect(); else if (command == "apply") Apply(); else if (command == "validate") Validate(); else throw new InvalidOperationException("Use inspect, apply or validate."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static BuildLocation[] Sites(Scene scene) => new[] { ClarkFreightSiteAuthoring.FindLocation(scene), ClarkOffsetSiteAuthoring.FindLocation(scene), MillsHeavyEquipmentSiteAuthoring.FindLocation(scene), MillsWorkersCrossingSiteAuthoring.FindLocation(scene), MillsFinalSiteAuthoring.FindLocation(scene) };
    private static string PathOf(Transform value) => value == null ? "none" : value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
    private static Transform SiteRoot(BuildLocation site)
    {
        for (Transform value = site.transform; value != null; value = value.parent) if (value.name == "CanyonPillarBuildSite") return value;
        throw new InvalidOperationException("No exact CanyonPillarBuildSite root for " + site.name);
    }
    private static ContractSO Contract(Scene scene, BuildLocation site) => All<NPCProgressionManager>(scene).SelectMany(npc => npc.AuthoredPhases).Single(phase => phase.targetBuildLocation == site && phase.contract != null).contract;
    private sealed class Join
    {
        public BuildLocation site;
        public Point anchor;
        public Collider bank;
        public float landDirection, top, width, edge;
        public Vector3 original, bankShift;
        public float[] lanes;
    }
    private static Scene Canyon()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Open CanyonCrossing in Edit Mode.");
        return scene;
    }
    private static bool Sample(Collider bank, Vector3 point, out RaycastHit hit) => bank.Raycast(new Ray(point + Vector3.up * 2f, Vector3.down), out hit, 4f) && hit.normal.y > .99f;
    private static Vector3 WheelCenter(GameObject wheel)
    {
        var renderer = wheel.GetComponentInChildren<Renderer>(true);
        return renderer != null && renderer.enabled ? renderer.bounds.center : wheel.transform.position;
    }
    private static float[] Lanes(Scene scene, BuildLocation site, Point anchor, float width)
    {
        // Cover the travelled lane area and each actual wheel track. The cliff
        // corners outside the traffic lanes are intentionally chamfered scenery.
        var lanes = new List<float> { -6f, -3f, 0f, 3f, 6f };
        var contract = Contract(scene, site);
        foreach (var vehicle in All<LiveLoadVehicle>(scene).Where(vehicle => vehicle.assignedContract == contract))
        {
            vehicle.GetMultiLoadRoutePoses(out Vector3 start, out _);
            foreach (var wheel in vehicle.wheelObjects)
            {
                lanes.Add(WheelCenter(wheel).z - anchor.transform.position.z);
                lanes.Add(WheelCenter(wheel).z + start.z - vehicle.transform.position.z - anchor.transform.position.z);
            }
        }
        return lanes.Where(offset => Mathf.Abs(offset) < width * .5f).Select(value => Mathf.Round(value * 1000f) / 1000f).Distinct().OrderBy(value => value).ToArray();
    }
    private static float Edge(Join join)
    {
        float worst = -2f;
        foreach (float lane in join.lanes)
        {
            bool IsTop(float distance) => Sample(join.bank, join.anchor.transform.position + new Vector3(join.landDirection * distance, 0f, lane), out RaycastHit hit) && Mathf.Abs(hit.point.y - join.top) < .005f;
            if (!IsTop(1f)) throw new InvalidOperationException($"No flat road surface inside {PathOf(join.bank.transform)} at lane {lane:F3}.");
            float outside = -2f, inside = 1f;
            if (IsTop(outside)) { worst = Mathf.Max(worst, outside); continue; }
            for (int i = 0; i < 18; i++) { float middle = (outside + inside) * .5f; if (IsTop(middle)) inside = middle; else outside = middle; }
            worst = Mathf.Max(worst, inside);
        }
        return worst;
    }
    private static List<Join> Measure(Scene scene)
    {
        Physics.SyncTransforms(); var joins = new List<Join>();
        foreach (BuildLocation site in Sites(scene))
        {
            if (site.bakedBars.Count != 0 || site.bakedPoints.Count != 0) throw new InvalidOperationException("Remove/review the scene-authored built bridge before adjusting " + site.name);
            Transform root = SiteRoot(site); var contract = Contract(scene, site);
            foreach (string side in new[] { "Left", "Right" })
            {
                var anchor = (side == "Left" ? site.startingAnchors : site.endingAnchors).Single(point => point != null && point.name == side + "Anchor");
                if (!anchor.originalIsAnchor || anchor.Runtime) throw new InvalidOperationException("Use the permanent " + side + " road anchor.");
                Transform bankRoot = root.Find("Banks/" + side + "Bank");
                var bank = bankRoot.GetComponentsInChildren<MeshCollider>(true).Single(col => col.name == "Canyon_Pillar3 (1)" && col.enabled && col.gameObject.activeInHierarchy);
                var join = new Join { site = site, anchor = anchor, bank = bank, original = anchor.transform.position, landDirection = side == "Left" ? -1 : 1, width = contract.GetWideRoadWidth() };
                join.lanes = Lanes(scene, site, anchor, join.width);
                var heights = new List<float>();
                foreach (float lane in join.lanes)
                    foreach (float distance in new[] { .6f, 1f, 2f })
                    {
                        if (!Sample(bank, join.original + new Vector3(join.landDirection * distance, 0f, lane), out RaycastHit hit)) throw new InvalidOperationException($"Missing flat bank top: {site.name} {side} lane {lane:F3}.");
                        heights.Add(hit.point.y);
                    }
                if (heights.Max() - heights.Min() > .002f) throw new InvalidOperationException("The bank top is uneven; review its mesh instead of hiding a ramp beneath it: " + PathOf(bank.transform));
                join.top = heights.Average(); join.edge = Edge(join);
                float shift = Mathf.Min(0f, -.15f - join.edge);
                join.bankShift = Vector3.right * (join.landDirection * shift);
                if (Mathf.Abs(join.top - .025f - join.original.y) > .3f || join.bankShift.magnitude > .6f) throw new InvalidOperationException("The required correction exceeds the audited Industrial Zone range: " + site.name);
                joins.Add(join);
            }
        }
        return joins;
    }
    private static void Mark(UnityEngine.Object value) { EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
    [MenuItem("Tools/Civil Craft/Build Sites/Industrial Zone/Align Road Joins")]
    public static void Apply()
    {
        Scene scene = Canyon(); var joins = Measure(scene); bool wasDirty = scene.isDirty;
        string backup = "LocalBackups/IndustrialJoins-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Align Industrial Zone road joins"); var report = new StringBuilder();
        try
        {
            Undo.RecordObjects(joins.SelectMany(join => new UnityEngine.Object[] { join.anchor.transform, join.bank.transform }).Distinct().ToArray(), "Align permanent anchors and cliff edges");
            // Calculate every target first: Mills's final RightAnchor is nested
            // beneath LeftAnchor, so setting its world pose last avoids double movement.
            foreach (Join join in joins)
            {
                Vector3 position = join.original; position.y = join.top - .025f;
                join.anchor.transform.position = position; join.bank.transform.position += join.bankShift;
                Mark(join.anchor.transform); Mark(join.bank.transform);
                report.AppendLine($"CORRECTED {join.site.name} {join.anchor.name}: anchor Y {join.original.y:F5} -> {position.y:F5}; cliff-edge X adjustment {join.bankShift.x:F4}m; X/Z and contract span retained.");
            }
            foreach (BuildLocation site in Sites(scene))
            {
                Undo.RecordObject(site, "Keep legacy bridge endpoints connected to corrected road anchors");
                var siteData = new SerializedObject(site);
                siteData.FindProperty("savedRoadAnchorVerticalTolerance").floatValue = .25f;
                siteData.ApplyModifiedProperties(); Mark(site);
                var aligner = site.GetComponent<BridgeAbutmentAligner>(); if (aligner == null) throw new InvalidOperationException("Missing existing abutment helper at " + site.name);
                Undo.RecordObject(aligner, "Author road join references"); var data = new SerializedObject(aligner); var sides = joins.Where(join => join.site == site).ToArray();
                data.FindProperty("referenceAnchor").objectReferenceValue = sides[0].anchor; data.FindProperty("buildLocation").objectReferenceValue = site;
                data.FindProperty("roadSurfaceAboveAnchor").floatValue = .025f; data.FindProperty("alignAnchorsToTutorialGhosts").boolValue = false;
                var list = data.FindProperty("abutments"); list.arraySize = 2;
                for (int i = 0; i < 2; i++)
                {
                    var entry = list.GetArrayElementAtIndex(i); var join = sides[i];
                    entry.FindPropertyRelative("anchor").objectReferenceValue = join.anchor; entry.FindPropertyRelative("approachCollider").objectReferenceValue = join.bank;
                    entry.FindPropertyRelative("movableRoot").objectReferenceValue = join.bank.transform;
                    entry.FindPropertyRelative("surfaceProbeOffset").vector3Value = Vector3.right * join.landDirection;
                    entry.FindPropertyRelative("additionalSurfaceOffset").floatValue = join.top - sides[0].top;
                }
                data.ApplyModifiedProperties(); Mark(aligner);
            }
            Physics.SyncTransforms(); Check(scene, report, true);
            Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene);
            bool saved = !wasDirty && EditorSceneManager.SaveScene(scene); Undo.CollapseUndoOperations(group);
            report.AppendLine(saved ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: existing scene edits remain unsaved."); report.AppendLine("Backup: " + backup); File.WriteAllText(Report, report.ToString());
        }
        catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
    }
    [MenuItem("Tools/Civil Craft/Build Sites/Industrial Zone/Validate Road Joins")]
    public static void Validate() { var scene = Canyon(); var report = new StringBuilder(); Check(scene, report, true); report.AppendLine("Scene dirty=" + scene.isDirty); File.WriteAllText(Report, report.ToString()); }
    private static void Check(Scene scene, StringBuilder report, bool physics)
    {
        foreach (Join join in Measure(scene))
        {
            if (Mathf.Abs(join.top - join.anchor.transform.position.y - .025f) > .002f || join.edge > -.14f) throw new InvalidOperationException("Unaligned road join: " + join.site.name + " " + join.anchor.name);
            foreach (float lane in join.lanes)
                foreach (float distance in new[] { -.1f, 0f, .1f, .3f, 1f })
                {
                    var point = join.anchor.transform.position + new Vector3(join.landDirection * distance, 0f, lane);
                    if (!Sample(join.bank, point, out RaycastHit bankHit) || Mathf.Abs(bankHit.point.y - join.top) > .002f) throw new InvalidOperationException("Gap or step at " + join.site.name + " " + join.anchor.name + " lane " + lane);
                    foreach (var hit in Physics.RaycastAll(point + Vector3.up, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider.gameObject.scene == scene && hit.collider.GetComponentInParent<Point>() == null && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null && hit.normal.y > .5f && hit.point.y > join.top + .002f) throw new InvalidOperationException("A second collider protrudes over the road join: " + PathOf(hit.collider.transform));
                }
            if (physics) RunWheelChecks(scene, join, report);
            report.AppendLine($"PASS {join.site.name} {join.anchor.name}: {join.lanes.Length} lane/width samples, height error <2mm, bank overlap {-join.edge:F3}m, road top Y={join.top:F5}.");
        }
        MillsFinalStoryAuthoring.Validate();
        report.AppendLine("PASS all five Industrial Zone spans, contract/live-load bindings and earlier story targets preserved.");
    }
    private static void RunWheelChecks(Scene scene, Join join, StringBuilder report)
    {
        var tested = new HashSet<string>(); var contract = Contract(scene, join.site);
        var colliders = All<Collider>(scene);
        foreach (var vehicle in All<LiveLoadVehicle>(scene).Where(vehicle => vehicle.assignedContract == contract))
        {
            vehicle.GetMultiLoadRoutePoses(out Vector3 start, out _);
            foreach (var wheel in vehicle.wheelObjects)
            {
                float lane = WheelCenter(wheel).z + start.z - vehicle.transform.position.z;
                string key = Mathf.RoundToInt(lane * 1000f) + ":" + Mathf.RoundToInt(vehicle.wheelRadius * 1000f);
                if (!tested.Add(key)) continue;
                IndustrialRoadSeamPhysicsCheck.Validate(scene, colliders, join.anchor.transform.position, join.top, lane, join.landDirection, vehicle.wheelRadius, report);
            }
        }
        if (tested.Count == 0) throw new InvalidOperationException("No vehicle wheel tracks for " + join.site.name);
    }
    [MenuItem("Tools/Civil Craft/Build Sites/Industrial Zone/Inspect Road Joins")]
    public static void Inspect()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CanyonCrossing in Edit Mode.");
        Physics.SyncTransforms(); var report = new StringBuilder($"INDUSTRIAL JOINS scene dirty={scene.isDirty}\n");
        var manager = All<BridgePhysicsManager>(scene).Single(value => value.isActiveAndEnabled); var settings = new SerializedObject(manager);
        foreach (string key in new[] { "bakedRoadVisualTop", "bakedRoadColliderThickness", "bakedRoadColliderSeamOverlap", "simulatedRoadColliderSeamOverlap" }) report.AppendLine(key + "=" + settings.FindProperty(key).floatValue.ToString("F5"));
        foreach (BuildLocation site in Sites(scene))
        {
            Transform root = SiteRoot(site); ContractSO contract = Contract(scene, site);
            report.AppendLine($"\nSITE {PathOf(site.transform)} contract={contract.ContractID} root={PathOf(root)} width={contract.GetWideRoadWidth():F3} bakedBars={site.bakedBars.Count}");
            foreach (var col in root.GetComponentsInChildren<Collider>(true).Where(col => !col.isTrigger && col.GetComponentInParent<Point>() == null))
                report.AppendLine($"COLLIDER {PathOf(col.transform)} {col.GetType().Name} enabled={col.enabled} active={col.gameObject.activeInHierarchy} bounds={col.bounds} scale={col.transform.lossyScale:F4} rotation={col.transform.eulerAngles:F3}");
            foreach (var aligner in site.GetComponents<BridgeAbutmentAligner>()) report.AppendLine($"ALIGNER reference={PathOf(aligner.ReferenceAnchor?.transform)} abutments={aligner.Abutments.Count} validation={aligner.ValidateAlignment(out string detail)} {detail}");
            foreach (Point anchor in site.startingAnchors.Concat(site.endingAnchors).Where(point => point != null))
            {
                report.AppendLine($"ANCHOR {PathOf(anchor.transform)} at {anchor.transform.position:F5} permanent={anchor.originalIsAnchor} runtime={anchor.Runtime}");
                if (anchor.name != "LeftAnchor" && anchor.name != "RightAnchor") continue;
                float direction = anchor.name == "LeftAnchor" ? -1f : 1f;
                Transform bank = root.Find("Banks/" + (direction < 0 ? "LeftBank" : "RightBank"));
                var snap = anchor.GetComponent<AnchorEdgeSnap>();
                if (snap != null) report.AppendLine($"EDGE {snap.TryPreviewEdge(.15f, bank, Vector3.right, out Vector3 edge, out Collider surface, out string edgeDetail)} {edge:F5} collider={PathOf(surface?.transform)} {edgeDetail}");
                foreach (float z in new[] { -6f, -3f, 0f, 3f, 6f })
                    foreach (float distance in new[] { -1f, -.3f, 0f, .1f, .5f, 1f, 3f, 6f })
                    {
                        Vector3 position = anchor.transform.position + new Vector3(direction * distance, 0f, z);
                        var hits = Physics.RaycastAll(position + Vector3.up * 2f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore)
                            .Where(hit => hit.collider.gameObject.scene == scene && hit.normal.y > .5f && hit.collider.GetComponentInParent<Point>() == null && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null).OrderByDescending(hit => hit.point.y).ToArray();
                        report.AppendLine($"SAMPLE {anchor.name} landDistance={distance:F2} laneOffset={z:F2} top=" + (hits.Length == 0 ? "none" : $"{hits[0].point.y:F5} delta={hits[0].point.y - anchor.transform.position.y - .025f:F5} {PathOf(hits[0].collider.transform)} normal={hits[0].normal:F3}"));
                    }
            }
            foreach (var vehicle in All<LiveLoadVehicle>(scene).Where(vehicle => vehicle.assignedContract == contract)) report.AppendLine($"VEHICLE {PathOf(vehicle.transform)} at {vehicle.transform.position:F3} radius={vehicle.wheelRadius:F3} start={vehicle.startPoint?.position:F3} end={vehicle.endPoint?.position:F3}");
        }
        File.WriteAllText(Report, report.ToString());
    }
}
#endif
