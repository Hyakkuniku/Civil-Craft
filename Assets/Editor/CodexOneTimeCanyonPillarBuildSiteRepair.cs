using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class CodexOneTimeCanyonPillarBuildSiteRepair
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string BankPrefabPath = "Assets/Prefabs/BridgeSites/CanyonPillarBridgeBank.prefab";
    private const string SitePrefabPath = "Assets/Prefabs/BridgeSites/CanyonPillarBuildSite.prefab";
    private const string CompleteMessage = "[CodexCanyonPillarRepair] COMPLETE";
    private static bool attempted;

    static CodexOneTimeCanyonPillarBuildSiteRepair()
    {
        EditorApplication.delayCall += TryRepair;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryRepair;
    }

    private static void TryRepair()
    {
        if (attempted) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[CodexCanyonPillarRepair] Waiting for Edit Mode.");
            return;
        }

        attempted = true;
        try
        {
            Repair();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("[CodexCanyonPillarRepair] FAILED");
        }
    }

    private static void Repair()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForRepair = !scene.IsValid() || !scene.isLoaded;
        if (openedForRepair)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject[] sites = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(item => item.name == "CanyonPillarBuildSite")
            .Select(item => item.gameObject)
            .Distinct()
            .ToArray();
        if (sites.Length != 1)
            throw new InvalidOperationException($"Expected exactly one CanyonPillarBuildSite in {ScenePath}, found {sites.Length}.");

        GameObject site = sites[0];
        Transform banks = RequireChild(site.transform, "Banks");
        Transform anchors = site.transform.Find("Anchors") ?? site.transform.Find("Achors");
        if (anchors == null) throw new InvalidOperationException("The site has no Anchors/Achors group.");
        anchors.name = "Anchors";

        Transform deckReference = RequireChild(site.transform, "DeckHeightReference");
        Transform siteObjects = RequireChild(site.transform, "SiteObjects");
        Point leftAnchor = RequirePoint(anchors, "LeftAnchor");
        Point rightAnchor = RequirePoint(anchors, "RightAnchor");
        Transform leftBank = RequireChild(banks, "LeftBank");
        Transform oldRightBank = RequireChild(banks, "RightBank");

        Vector3 routeDirection = rightAnchor.transform.position - leftAnchor.transform.position;
        routeDirection.y = 0f;
        if (routeDirection.sqrMagnitude < 0.0001f)
            throw new InvalidOperationException("The two anchors do not define a horizontal bridge direction.");
        routeDirection.Normalize();

        Vector3 edgeSocketLocalPosition = leftBank.InverseTransformPoint(leftAnchor.transform.position);
        Vector3 localForward = leftBank.InverseTransformDirection(routeDirection).normalized;
        Vector3 localUp = leftBank.InverseTransformDirection(Vector3.up).normalized;
        Quaternion socketLocalRotation = Quaternion.LookRotation(localForward, localUp);
        ConfigureBankPrefabSockets(edgeSocketLocalPosition, socketLocalRotation, localForward);

        GameObject bankAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BankPrefabPath);
        if (bankAsset == null) throw new InvalidOperationException($"Could not load {BankPrefabPath}.");

        string leftSourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(leftBank.gameObject);
        if (leftSourcePath != BankPrefabPath)
            throw new InvalidOperationException($"LeftBank must be an instance of {BankPrefabPath}, but uses '{leftSourcePath}'.");

        Transform[] unexpectedRightChildren = oldRightBank.Cast<Transform>()
            .Where(child => child.name != "Canyon_Pillar3 (1)" &&
                            child.name != "BridgeEdgeSocket" &&
                            child.name != "VehicleApproachSocket")
            .ToArray();
        if (unexpectedRightChildren.Length > 0)
            throw new InvalidOperationException("RightBank contains additional user objects and was not replaced: " +
                                                string.Join(", ", unexpectedRightChildren.Select(child => child.name)));

        GameObject newRightBankObject = (GameObject)PrefabUtility.InstantiatePrefab(bankAsset, banks);
        Undo.RegisterCreatedObjectUndo(newRightBankObject, "Repair Canyon Pillar Build Site");
        newRightBankObject.name = "RightBank";
        Transform newRightBank = newRightBankObject.transform;
        newRightBank.localScale = Vector3.one;
        newRightBank.rotation = leftBank.rotation * Quaternion.Euler(0f, 180f, 0f);
        newRightBank.position = oldRightBank.position;

        Transform rightSocket = RequireChild(newRightBank, "BridgeEdgeSocket");
        newRightBank.position += rightAnchor.transform.position - rightSocket.position;

        BuildLocation location = site.GetComponentInChildren<BuildLocation>(true);
        if (location == null) throw new InvalidOperationException("The site has no BuildLocation component.");

        Undo.RecordObject(location, "Repair Canyon Pillar Build Site");
        location.startingAnchors = new List<Point> { leftAnchor };
        location.endingAnchors = new List<Point> { rightAnchor };
        location.buildSiteVisualRoots = new List<GameObject> { leftBank.gameObject, newRightBankObject };

        ConfigureAnchor(leftAnchor, location);
        ConfigureAnchor(rightAnchor, location);

        Undo.RecordObject(deckReference, "Repair Canyon Pillar Build Site");
        Vector3 deckPosition = (leftAnchor.transform.position + rightAnchor.transform.position) * 0.5f;
        deckPosition.y = leftAnchor.transform.position.y;
        deckReference.position = deckPosition;

        ResetGroupingTransformWithoutMovingChildren(siteObjects);
        ConfigureAbutmentAligner(location, leftAnchor);

        Undo.DestroyObjectImmediate(oldRightBank.gameObject);
        EditorUtility.SetDirty(site);
        EditorUtility.SetDirty(location);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        SaveReusableSitePrefab(site);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ValidateResult(site, location, leftAnchor, rightAnchor, leftBank, newRightBank);
        Debug.Log(CompleteMessage +
                  $" | anchors={Vector3.Distance(leftAnchor.transform.position, rightAnchor.transform.position):F3} units" +
                  $" | right bank={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(newRightBankObject)}" +
                  $" | site prefab={SitePrefabPath}");

        if (openedForRepair)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static void ConfigureBankPrefabSockets(
        Vector3 edgePosition,
        Quaternion edgeRotation,
        Vector3 localForward)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(BankPrefabPath);
        try
        {
            Transform edgeSocket = RequireChild(contents.transform, "BridgeEdgeSocket");
            Transform approachSocket = RequireChild(contents.transform, "VehicleApproachSocket");
            contents.transform.localPosition = Vector3.zero;
            contents.transform.localRotation = Quaternion.identity;
            contents.transform.localScale = Vector3.one;
            edgeSocket.localPosition = edgePosition;
            edgeSocket.localRotation = edgeRotation;
            edgeSocket.localScale = Vector3.one;
            approachSocket.localPosition = edgePosition - localForward * 0.75f;
            approachSocket.localRotation = edgeRotation;
            approachSocket.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(contents, BankPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ConfigureAnchor(Point anchor, BuildLocation owner)
    {
        Undo.RecordObjects(new UnityEngine.Object[] { anchor, anchor.transform }, "Repair Canyon Pillar Anchors");
        anchor.Runtime = false;
        anchor.isAnchor = true;
        anchor.originalIsAnchor = true;
        anchor.AssignOwner(owner, true);
        anchor.UpdateMaterial();

        AnchorEdgeSnap snap = anchor.GetComponent<AnchorEdgeSnap>();
        if (snap == null) snap = Undo.AddComponent<AnchorEdgeSnap>(anchor.gameObject);
        SerializedObject snapObject = new SerializedObject(snap);
        snapObject.FindProperty("autoSnapWhenMoved").boolValue = false;
        snapObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(anchor);
        EditorUtility.SetDirty(snap);
    }

    private static void ConfigureAbutmentAligner(BuildLocation location, Point referenceAnchor)
    {
        BridgeAbutmentAligner aligner = location.GetComponent<BridgeAbutmentAligner>();
        if (aligner == null) aligner = Undo.AddComponent<BridgeAbutmentAligner>(location.gameObject);
        SerializedObject alignerObject = new SerializedObject(aligner);
        alignerObject.FindProperty("referenceAnchor").objectReferenceValue = referenceAnchor;
        alignerObject.FindProperty("buildLocation").objectReferenceValue = location;
        alignerObject.FindProperty("alignAnchorsToTutorialGhosts").boolValue = false;
        alignerObject.FindProperty("abutments").arraySize = 0;
        alignerObject.ApplyModifiedPropertiesWithoutUndo();
        aligner.RemoveGeneratedApproaches();
        EditorUtility.SetDirty(aligner);
    }

    private static void ResetGroupingTransformWithoutMovingChildren(Transform group)
    {
        Transform[] children = group.Cast<Transform>().ToArray();
        Vector3[] positions = children.Select(child => child.position).ToArray();
        Quaternion[] rotations = children.Select(child => child.rotation).ToArray();
        Vector3[] scales = children.Select(child => child.lossyScale).ToArray();

        Undo.RecordObject(group, "Reset SiteObjects Group");
        group.localPosition = Vector3.zero;
        group.localRotation = Quaternion.identity;
        group.localScale = Vector3.one;

        for (int index = 0; index < children.Length; index++)
        {
            Undo.RecordObject(children[index], "Preserve Site Object Placement");
            children[index].SetPositionAndRotation(positions[index], rotations[index]);
            Transform parent = children[index].parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            children[index].localScale = new Vector3(
                SafeDivide(scales[index].x, parentScale.x),
                SafeDivide(scales[index].y, parentScale.y),
                SafeDivide(scales[index].z, parentScale.z));
        }
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.000001f ? value / divisor : value;
    }

    private static void SaveReusableSitePrefab(GameObject site)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(site, SitePrefabPath, out bool success);
        if (!success || saved == null)
            throw new InvalidOperationException($"Failed to save {SitePrefabPath}.");

        GameObject contents = PrefabUtility.LoadPrefabContents(SitePrefabPath);
        try
        {
            contents.transform.localPosition = Vector3.zero;
            contents.transform.localRotation = Quaternion.identity;
            contents.transform.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(contents, SitePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ValidateResult(
        GameObject site,
        BuildLocation location,
        Point leftAnchor,
        Point rightAnchor,
        Transform leftBank,
        Transform rightBank)
    {
        if (site.transform.Find("Anchors") == null || site.transform.Find("Achors") != null)
            throw new InvalidOperationException("The Anchors group was not repaired.");
        if (location.startingAnchors.Count != 1 || location.startingAnchors[0] != leftAnchor ||
            location.endingAnchors.Count != 1 || location.endingAnchors[0] != rightAnchor)
            throw new InvalidOperationException("BuildLocation anchor references are incorrect after repair.");
        if (location.buildSiteVisualRoots.Count != 2 ||
            location.buildSiteVisualRoots[0] != leftBank.gameObject ||
            location.buildSiteVisualRoots[1] != rightBank.gameObject)
            throw new InvalidOperationException("BuildLocation bank references are incorrect after repair.");
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(leftBank.gameObject) != BankPrefabPath ||
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rightBank.gameObject) != BankPrefabPath)
            throw new InvalidOperationException("Both banks are not instances of the canonical bank prefab.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SitePrefabPath) == null)
            throw new InvalidOperationException("The reusable site prefab was not created.");
    }

    private static Transform RequireChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
            throw new InvalidOperationException($"'{parent.name}' has no direct child named '{childName}'.");
        return child;
    }

    private static Point RequirePoint(Transform parent, string childName)
    {
        Transform child = RequireChild(parent, childName);
        Point point = child.GetComponent<Point>();
        if (point == null) throw new InvalidOperationException($"'{childName}' has no Point component.");
        return point;
    }
}
