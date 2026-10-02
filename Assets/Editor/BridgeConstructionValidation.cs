using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BridgeConstructionValidation
{
    public static void Run(Transform fixture, StringBuilder report)
    {
        var definition = ScriptableObject.CreateInstance<ContractSO>(); definition.budget = 1000;
        var road = ScriptableObject.CreateInstance<BridgeMaterialSO>(); road.name = "Receipt Road"; road.costPerMeter = 10; road.maxLength = 6;
        var dual = ScriptableObject.CreateInstance<BridgeMaterialSO>(); dual.name = "Receipt Dual"; dual.costPerMeter = 20; dual.maxLength = 6; dual.isDualBeam = true;
        BuildLocation site = Child("Receipt Source Site", fixture).AddComponent<BuildLocation>(); site.activeContract = definition;
        Point start = Child("Receipt Source Start", fixture).AddComponent<Point>();
        Point end = Child("Receipt Source End", fixture).AddComponent<Point>();
        start.transform.position = new Vector3(30, 3, -8); end.transform.position = new Vector3(33, 3, -8);
        start.Runtime = end.Runtime = false; start.originalIsAnchor = end.originalIsAnchor = start.isAnchor = end.isAnchor = true;
        site.startingAnchors.Add(start); site.endingAnchors.Add(end);
        BarCreator creator = Child("Receipt Creator", fixture).AddComponent<BarCreator>();
        creator.pointToInstantiate = Child("Receipt Point Prefab", fixture); creator.pointToInstantiate.AddComponent<Point>();
        creator.barToInstantiate = Child("Receipt Bar Prefab", fixture); creator.barToInstantiate.AddComponent<Bar>();
        BuildUIController ui = Child("Receipt Isolated UI", fixture).AddComponent<BuildUIController>(); ui.barCreator = creator;
        var recalculate = typeof(BuildUIController).GetMethod("RecalculateStaticBridge", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool), typeof(BuildLocation), typeof(IEnumerable<Bar>) }, null);
        var uiInstance = typeof(BuildUIController).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousUI = uiInstance.GetValue(null);
        ChallengeBuildWorkspace workspace = null;
        try
        {
            workspace = ChallengeBuildWorkspace.Create(site, definition, creator, 31, 2);
            Point a = workspace.Location.startingAnchors[0], b = workspace.Location.endingAnchors[0];
            Bar first = Member("Receipt Logical Road", workspace.BarRoot, workspace.Location, a, b, road);
            Bar second = Member("Receipt Logical Dual", workspace.BarRoot, workspace.Location, a, b, dual);
            // Reproduce the nested gameplay Bar shipped in material mesh prefabs.
            Bar visual = Member("VisualSegment", first.transform, workspace.Location, a, b, road);
            Bar undone = Member("Receipt Undone", workspace.BarRoot, workspace.Location, a, b, road); undone.gameObject.SetActive(false);
            BuildLocation other = Child("Other Receipt Site", fixture).AddComponent<BuildLocation>();
            Bar foreign = Member("Other Saved Bridge", Child("Other Bridge Container", workspace.Root.transform).transform, other, a, b, road);
            Bar preview = Member("Receipt Preview", workspace.BarRoot, workspace.Location, a, b, road);
            creator.currentBar = preview;
            typeof(BarCreator).GetField("barCreationStarted", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(creator, true);
            Bar[] candidates = workspace.Root.GetComponentsInChildren<Bar>(true);
            var receipt = BridgeConstructionReceipt.Capture(workspace.Location, candidates, preview);
            Check(receipt.TotalCost == 150 && receipt.MaterialMeters[road] == 3 && receipt.MaterialMeters[dual] == 6,
                "Receipt counted visual/foreign/undone/preview members or lost the dual-beam multiplier.");
            // Preview-scene objects are excluded from Unity's global search.
            recalculate.Invoke(ui, new object[] { false, workspace.Location, candidates });
            Check(ui.GetTotalCost() == 150, "HUD budget differs from the scoped receipt.");
            // Exercise the same budget scanner with a regular, non-session location.
            first.AssignOwner(site, true); second.AssignOwner(site, true); visual.AssignOwner(site, true);
            recalculate.Invoke(ui, new object[] { false, site, candidates });
            Check(ui.GetTotalCost() == 150, "Single-player budget still counts nested visual Bar components.");
            first.AssignOwner(workspace.Location, true); second.AssignOwner(workspace.Location, true); visual.AssignOwner(workspace.Location, true);
            creator.currentBar = null;
            typeof(BarCreator).GetField("barCreationStarted", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(creator, false);
            Object.DestroyImmediate(preview.gameObject);
            var graph = ChallengeBridgeSubmissionRules.Capture(workspace);
            Check(ChallengeBridgeSubmissionRules.Validate(graph, site, definition,
                new Dictionary<string, BridgeMaterialSO> { { road.Id, road }, { dual.Id, dual } }, -10, out float hostCost) == ChallengeSubmissionError.None &&
                hostCost == receipt.TotalCost, "HUD/receipt prices differ from authoritative submission prices.");
            b.transform.position += Vector3.right; first.currentLength = 9999;
            Check(receipt.TotalCost == 150 && BridgeConstructionReceipt.ConstructionCost(first) == 40,
                "Construction receipt changed with deformed geometry, or edit costs use stale cached length.");
            b.transform.position -= Vector3.right;
            var stars = ContractStarResult.Grade(true, receipt.TotalCost, 200, 0.85f, 20, 60, false, true);
            Check(stars.efficient && stars.Stars == 3 && !ContractStarResult.Grade(true, 171, 200, 0.85f, 20, 60, false, true).efficient,
                "Correct budget did not earn its efficiency star, or the 85% rule changed.");
            report.AppendLine("PASS: Solo and multiplayer HUD charge one 3m road (30) + one 3m dual beam (120) = 150; nested visuals, other sites, previews and undone bars add zero. Host submission agrees.");
            report.AppendLine("PASS: Receipt cost remains fixed through deformation; moving endpoints updates edit cost; a 150/200 completion earns the existing 85%-budget star, while 171/200 does not.");

            uiInstance.SetValue(null, ui); // Keep deletion notifications off the user's authored UI.
            var deletion = new HistoryAction { isBuildEvent = false };
            creator.DeletePoint(a, deletion);
            Check(a.gameObject.activeSelf && first.gameObject.activeSelf && deletion.affectedObjects.Count == 0,
                "Direct anchor deletion changed an anchor or attached members.");
            typeof(Bar).GetMethod("ClearLegacyPierAnchorPromotion", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { a });
            Check(a.Runtime && a.originalIsAnchor && a.IsScenePlacedAnchor && a.IsPermanentAnchor,
                "Pier normalization demoted a runtime-authored challenge anchor.");
            a.originalIsAnchor = false;
            Check(a.IsScenePlacedAnchor && a.IsPermanentAnchor, "Anchor protection relies only on a mutable legacy flag.");
            a.originalIsAnchor = true;
            a.ConnectedBars.Clear(); b.ConnectedBars.Clear(); // Model OnDisable removing the last incident member.
            creator.DeleteBar(first, deletion); creator.DeleteBar(second, deletion);
            Check(a.gameObject.activeSelf && b.gameObject.activeSelf && deletion.affectedObjects.Count == 2 &&
                !deletion.affectedObjects.Contains(a.gameObject) && !deletion.affectedObjects.Contains(b.gameObject),
                "Orphan cleanup put challenge anchors in deletion history.");
            deletion.Undo(); deletion.Redo();
            Check(a.gameObject.activeSelf && b.gameObject.activeSelf, "Undo/Redo toggled a protected challenge anchor.");
            Point editable = Child("Receipt Editable Node", workspace.PointRoot).AddComponent<Point>(); editable.AssignOwner(workspace.Location, true);
            var nodeDeletion = new HistoryAction(); creator.DeletePoint(editable, nodeDeletion);
            Check(!editable.gameObject.activeSelf && nodeDeletion.affectedObjects.Count == 1,
                "Anchor protection also blocked deletion of player-created nodes.");
            report.AppendLine("PASS: Session anchors survive direct erasing, last-member orphan cleanup, pier normalization and Undo/Redo; ordinary player-created nodes remain deletable.");
        }
        finally
        {
            creator.currentBar = null;
            typeof(BarCreator).GetField("barCreationStarted", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(creator, false);
            uiInstance.SetValue(null, previousUI);
            workspace?.Dispose(); Object.DestroyImmediate(definition); Object.DestroyImmediate(road); Object.DestroyImmediate(dual);
        }
    }
    private static Bar Member(string name, Transform parent, BuildLocation location, Point start, Point end, BridgeMaterialSO material)
    {
        Bar bar = Child(name, parent).AddComponent<Bar>(); bar.materialData = material; bar.AssignOwner(location, true);
        bar.startPoint = start; bar.endPoint = end; bar.currentLength = 999; bar.StartPosition = start.transform.position; bar.EndPosition = end.transform.position;
        start.ConnectedBars.Add(bar); end.ConnectedBars.Add(bar); return bar;
    }
    private static GameObject Child(string name, Transform parent) { var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
