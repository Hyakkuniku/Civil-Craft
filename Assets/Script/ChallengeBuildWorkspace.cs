using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>A local, disposable competition graph. Never reuses world anchors or saved bridge lists.</summary>
public sealed class ChallengeBuildWorkspace : IDisposable
{
    public const string ContractPrefix = "__session_challenge__";
    public GameObject Root { get; private set; }
    public BuildLocation Location { get; private set; }
    public BarCreator Creator { get; private set; }
    public Transform PointRoot { get; private set; }
    public Transform BarRoot { get; private set; }
    private ContractSO contract;
    private Transform originalPointParent, originalBarParent;
    private bool originalCreatorEnabled;
    private CommandManager commands;
    private bool historyScoped;
    private ClipboardManager clipboard;
    private bool clipboardScoped;
    private readonly Dictionary<GameObject, bool> worldVisibility = new Dictionary<GameObject, bool>();

    public static ContractSO ResolveContract(BuildLocation source, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (source != null && source.activeContract != null && source.activeContract.ContractID == key) return source.activeContract;
        PlayerDataManager data = PlayerDataManager.Instance;
        if (data != null && data.allGameContracts != null)
            foreach (ContractSO candidate in data.allGameContracts)
                if (candidate != null && candidate.ContractID == key) return candidate;
        foreach (ContractSO candidate in Resources.FindObjectsOfTypeAll<ContractSO>())
            if (candidate != null && candidate.ContractID == key) return candidate;
        return null;
    }

    public static ChallengeBuildWorkspace Create(BuildLocation source, ContractSO definition, BarCreator creator,
        int revision, int playerId)
    {
        if (source == null || definition == null || creator == null || creator.pointToInstantiate == null ||
            creator.barToInstantiate == null || source.startingAnchors.Count == 0 || source.endingAnchors.Count == 0)
            throw new InvalidOperationException("The selected site needs a contract, construction prefabs and both sets of anchors.");
        var workspace = new ChallengeBuildWorkspace();
        try
        {
            workspace.Creator = creator;
            workspace.originalPointParent = creator.pointParent;
            workspace.originalBarParent = creator.barParent;
            workspace.originalCreatorEnabled = creator.enabled;
            workspace.Root = new GameObject($"Challenge Build — Player {playerId} — Session {revision}");
            workspace.Root.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(workspace.Root, source.gameObject.scene);
            workspace.Root.SetActive(false);
            workspace.Location = workspace.Root.AddComponent<BuildLocation>();
            BuildLocation location = workspace.Location;
            location.InitializeSessionChallenge(revision, MultiplayerChallengeRules.SiteKey(source));
            location.enabled = false; // No interaction/story Update or Start on this proxy.
            workspace.contract = Object.Instantiate(definition);
            workspace.contract.name = definition.name + " (Session Only)";
            workspace.contract.hideFlags = HideFlags.DontSave;
            workspace.contract.contractID = ContractPrefix + revision + "_" + playerId;
            workspace.contract.isTutorialContract = false;
            workspace.contract.isTimeAttack = false;
            workspace.contract.isStoryModeContract = false;
            workspace.contract.countsTowardMapAchievements = false;
            workspace.contract.autoCollectReward = false;
            workspace.contract.hiddenTools = new List<BuildModeTool>(definition.hiddenTools ?? new List<BuildModeTool>());
            workspace.contract.hiddenTools.Remove(BuildModeTool.ExitBuildMode);
            // Testing/scoring is a later milestone; editing uses a fresh scoped clipboard.
            if (!workspace.contract.hiddenTools.Contains(BuildModeTool.Simulate)) workspace.contract.hiddenTools.Add(BuildModeTool.Simulate);
            location.activeContract = workspace.contract;
            location.locationCamera = source.locationCamera;
            location.gridImage = source.gridImage;
            // Keep the authored environment metadata on the runtime proxy, so
            // build-mode decoration hiding cannot suppress this selected site.
            location.buildSiteVisualRoots = new List<GameObject>(source.buildSiteVisualRoots ?? new List<GameObject>());
            if (!location.buildSiteVisualRoots.Exists(root => root != null)) location.buildSiteVisualRoots.Add(source.gameObject);
            location.cameraPositionOffset = source.GetDesiredCameraPosition(); // Root is at world origin.
            location.cameraLookAtOffset = source.transform.position + source.cameraLookAtOffset;
            var points = new GameObject("Own Nodes").transform;
            points.SetParent(workspace.Root.transform, false);
            var bars = new GameObject("Own Bars").transform;
            bars.SetParent(workspace.Root.transform, false);
            workspace.PointRoot = points;
            workspace.BarRoot = bars;
            var copies = new Dictionary<Point, Point>();
            foreach (Point anchor in source.startingAnchors)
                location.startingAnchors.Add(workspace.CopyAnchor(anchor, points, copies));
            foreach (Point anchor in source.endingAnchors)
                location.endingAnchors.Add(workspace.CopyAnchor(anchor, points, copies));
            // Hide every old graph, including inactive drafts, so selection, undo,
            // cost scans and snapping cannot reach another bridge through global registries.
            foreach (GameObject sceneRoot in source.gameObject.scene.GetRootGameObjects())
            {
                if (sceneRoot == workspace.Root) continue;
                foreach (Point point in sceneRoot.GetComponentsInChildren<Point>(true))
                    workspace.HideWorldObject(point.gameObject);
                foreach (Bar bar in sceneRoot.GetComponentsInChildren<Bar>(true))
                    workspace.HideWorldObject(bar.gameObject);
            }
            creator.CancelAllModes();
            creator.SetActiveMaterial(null);
            creator.pointParent = points;
            creator.barParent = bars;
            creator.enabled = false;
            workspace.EnsureSessionHistory();
            workspace.Root.SetActive(true);
            return workspace;
        }
        catch { workspace.Dispose(); throw; }
    }

    private Point CopyAnchor(Point source, Transform parent, Dictionary<Point, Point> copies)
    {
        if (source == null) throw new InvalidOperationException("A challenge anchor reference is missing.");
        if (copies.TryGetValue(source, out Point existing)) return existing;
        GameObject clone = Object.Instantiate(source.gameObject, source.transform.position, source.transform.rotation, parent);
        clone.name = "Own Anchor — " + source.name;
        clone.transform.localScale = source.transform.lossyScale;
        foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = script is Point;
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        Point point = clone.GetComponent<Point>();
        point.ConnectedBars.Clear();
        point.Runtime = true;
        point.isAnchor = point.originalIsAnchor = true;
        point.isSelected = point.isAnchorHighlighted = false;
        point.AssignOwner(Location, true);
        point.enabled = true;
        clone.SetActive(true);
        copies.Add(source, point);
        return point;
    }

    private void HideWorldObject(GameObject target)
    {
        if (worldVisibility.ContainsKey(target)) return;
        worldVisibility.Add(target, target.activeSelf);
        target.SetActive(false);
    }

    public void SetEditingEnabled(bool enabled)
    {
        EnsureSessionHistory(); // A command manager may awaken with the authored Build Canvas.
        if (Creator != null) Creator.enabled = enabled;
    }

    private void EnsureSessionHistory()
    {
        if (!historyScoped && CommandManager.Instance != null)
        {
            commands = CommandManager.Instance;
            commands.BeginSessionHistory();
            historyScoped = true;
        }
        if (!clipboardScoped && ClipboardManager.Instance != null)
        {
            clipboard = ClipboardManager.Instance;
            clipboard.BeginSessionClipboard();
            clipboardScoped = true;
        }
    }

    public void Dispose()
    {
        if (Creator != null)
        {
            Creator.CancelAllModes();
            Creator.pointParent = originalPointParent;
            Creator.barParent = originalBarParent;
            Creator.enabled = originalCreatorEnabled;
        }
        if (historyScoped && commands != null) commands.EndSessionHistory();
        if (clipboardScoped && clipboard != null) clipboard.EndSessionClipboard();
        historyScoped = false;
        clipboardScoped = false;
        if (Root != null) { Root.SetActive(false); DestroyTemporary(Root); }
        foreach (var state in worldVisibility) if (state.Key != null) state.Key.SetActive(state.Value);
        worldVisibility.Clear();
        if (contract != null) DestroyTemporary(contract);
        Root = null;
        Location = null;
        Creator = null;
        PointRoot = BarRoot = null;
        contract = null;
    }

    private static void DestroyTemporary(Object target)
    {
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }
}
