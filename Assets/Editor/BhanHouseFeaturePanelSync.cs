#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BhanHouseFeaturePanelSync
{
    private const string CanyonPath = "Assets/Scenes/CanyonCrossing.unity";
    private const string BhanPath = "Assets/Scenes/BHAN HOUSE.unity";

    private sealed class Replacement
    {
        public GameObject sourceRoot;
        public GameObject oldRoot;
        public GameObject newRoot;
        public readonly List<GameObject> additionalOldRoots = new List<GameObject>();
        public Dictionary<UnityEngine.Object, UnityEngine.Object> sourceToNew;
        public Dictionary<UnityEngine.Object, UnityEngine.Object> oldToNew;
    }

    [MenuItem("Tools/Civil Craft/Sync Canyon Feature Panels To Bhan House")]
    public static void SyncFromMenu()
    {
        SyncScenes();
    }

    [MenuItem("Tools/Civil Craft/Diagnose Bhan Achievement Layout")]
    public static void DiagnoseAchievementLayoutFromMenu()
    {
        Scene original = SceneManager.GetActiveScene();
        Scene canyon = SceneManager.GetSceneByPath(CanyonPath);
        Scene bhan = SceneManager.GetSceneByPath(BhanPath);
        bool openedCanyon = !canyon.IsValid() || !canyon.isLoaded;
        bool openedBhan = !bhan.IsValid() || !bhan.isLoaded;
        if (openedCanyon) canyon = EditorSceneManager.OpenScene(CanyonPath, OpenSceneMode.Additive);
        if (openedBhan) bhan = EditorSceneManager.OpenScene(BhanPath, OpenSceneMode.Additive);

        AchievementUIManager canyonManager = FindComponent<AchievementUIManager>(canyon);
        AchievementUIManager bhanManager = FindComponent<AchievementUIManager>(bhan);
        LogLayout("Canyon", canyonManager != null ? canyonManager.achievementPanel.transform : null);
        LogLayout("Bhan", bhanManager != null ? bhanManager.achievementPanel.transform : null);
        CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
    }

    [MenuItem("Tools/Civil Craft/Repair Bhan Achievement Layout")]
    public static void RepairAchievementLayoutFromMenu()
    {
        Scene original = SceneManager.GetActiveScene();
        Scene canyon = SceneManager.GetSceneByPath(CanyonPath);
        Scene bhan = SceneManager.GetSceneByPath(BhanPath);
        bool openedCanyon = !canyon.IsValid() || !canyon.isLoaded;
        bool openedBhan = !bhan.IsValid() || !bhan.isLoaded;
        if (openedCanyon) canyon = EditorSceneManager.OpenScene(CanyonPath, OpenSceneMode.Additive);
        if (openedBhan) bhan = EditorSceneManager.OpenScene(BhanPath, OpenSceneMode.Additive);

        AchievementUIManager canyonManager = FindComponent<AchievementUIManager>(canyon);
        AchievementUIManager bhanManager = FindComponent<AchievementUIManager>(bhan);
        if (canyonManager == null || bhanManager == null)
        {
            Debug.LogError("[Bhan UI Layout] Achievement manager missing; layout was not changed.");
            CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
            return;
        }

        EnsureMatchingAchievementParent(canyonManager, bhanManager, bhan);
        bhanManager.achievementPanel.SetActive(false);
        EditorUtility.SetDirty(bhanManager);
        EditorSceneManager.MarkSceneDirty(bhan);
        EditorSceneManager.SaveScene(bhan);
        Debug.Log("[Bhan UI Layout] Achievement panel repaired under the Canyon-matching UtilityPanel.");
        CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
    }

    private static void LogLayout(string label, Transform panel)
    {
        Debug.Log($"[Bhan UI Layout] {label} panel path: {ScenePath(panel)}");
        for (Transform current = panel; current != null; current = current.parent)
        {
            string details = current is RectTransform rect
                ? $"anchors={rect.anchorMin}/{rect.anchorMax}, position={rect.anchoredPosition}, size={rect.sizeDelta}, " +
                  $"localScale={rect.localScale}, lossyScale={rect.lossyScale}"
                : $"localPosition={current.localPosition}, localScale={current.localScale}, lossyScale={current.lossyScale}";
            Canvas canvas = current.GetComponent<Canvas>();
            UnityEngine.UI.CanvasScaler scaler = current.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (canvas != null) details += $", Canvas={canvas.renderMode}, scaleFactor={canvas.scaleFactor}";
            if (scaler != null) details += $", reference={scaler.referenceResolution}, match={scaler.matchWidthOrHeight}";
            Debug.Log($"[Bhan UI Layout] {label}: {current.name}: {details}");
        }
    }

    private static void SyncScenes()
    {
        Scene original = SceneManager.GetActiveScene();
        Scene canyon = SceneManager.GetSceneByPath(CanyonPath);
        Scene bhan = SceneManager.GetSceneByPath(BhanPath);
        bool openedCanyon = !canyon.IsValid() || !canyon.isLoaded;
        bool openedBhan = !bhan.IsValid() || !bhan.isLoaded;

        if (openedCanyon)
            canyon = EditorSceneManager.OpenScene(CanyonPath, OpenSceneMode.Additive);
        if (openedBhan)
            bhan = EditorSceneManager.OpenScene(BhanPath, OpenSceneMode.Additive);

        AlmanacManager canyonAlmanac = FindComponent<AlmanacManager>(canyon);
        AlmanacManager bhanAlmanac = FindComponent<AlmanacManager>(bhan);
        AchievementUIManager canyonAchievements = FindComponent<AchievementUIManager>(canyon);
        AchievementUIManager bhanAchievements = FindComponent<AchievementUIManager>(bhan);
        ObjectiveTrackerUI canyonObjectives = FindComponent<ObjectiveTrackerUI>(canyon);
        ObjectiveTrackerUI bhanObjectives = FindComponent<ObjectiveTrackerUI>(bhan);
        ShopManager canyonShop = FindComponent<ShopManager>(canyon);
        ShopManager bhanShop = FindComponent<ShopManager>(bhan);
        AudioManagerEventRelay canyonAudioRelay = FindComponent<AudioManagerEventRelay>(canyon);
        AudioManagerEventRelay bhanAudioRelay = FindComponent<AudioManagerEventRelay>(bhan);
        GameObject createdAudioRelayRoot = null;

        if (canyonAlmanac == null || bhanAlmanac == null ||
            canyonAchievements == null || bhanAchievements == null ||
            canyonObjectives == null || bhanObjectives == null ||
            canyonShop == null || bhanShop == null)
        {
            Debug.LogError("[Bhan UI Sync] One or more required managers are missing.");
            CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
            return;
        }

        EnsureMatchingAchievementParent(canyonAchievements, bhanAchievements, bhan);

        List<Replacement> replacements = new List<Replacement>
        {
            CloneForReplacement(canyonAlmanac.Panel, bhanAlmanac.Panel, bhan),
            CloneForReplacement(canyonAchievements.achievementPanel, bhanAchievements.achievementPanel, bhan),
            CloneForReplacement(canyonObjectives.trackerPanel, bhanObjectives.trackerPanel, bhan),
            CloneForReplacement(canyonShop.Panel, bhanShop.Panel, bhan)
        };

        Transform canyonAccess = FindTransform(canyon, "AccessButtons");
        Transform bhanAccess = FindTransform(bhan, "AccessButtons");
        Transform bhanShopButton = FindTransform(bhan, "Shop_btn");
        Debug.Log($"[Bhan UI Sync] Access paths - Canyon: {ScenePath(canyonAccess)}, " +
                  $"Bhan: {ScenePath(bhanAccess)}, Bhan Shop: {ScenePath(bhanShopButton)}.");
        if (canyonAccess != null && bhanAccess != null)
            replacements.Add(CloneForReplacement(canyonAccess.gameObject, bhanAccess.gameObject, bhan));
        else if (canyonAccess != null)
        {
            Transform bhanMainCanvas = bhanShopButton != null
                ? bhanShopButton.parent
                : FindTransform(bhan, "MainCanvas");
            if (bhanMainCanvas != null)
                replacements.Add(CloneMissingAccessButtons(canyonAccess, bhanMainCanvas, bhanShopButton, bhan));
        }

        if (replacements.Any(item => item == null))
        {
            Debug.LogError("[Bhan UI Sync] A source or destination feature panel was not found.");
            foreach (Replacement replacement in replacements.Where(item => item != null))
                UnityEngine.Object.DestroyImmediate(replacement.newRoot);
            CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
            return;
        }

        if (canyonAudioRelay != null && bhanAudioRelay == null)
        {
            createdAudioRelayRoot = UnityEngine.Object.Instantiate(canyonAudioRelay.gameObject);
            createdAudioRelayRoot.name = canyonAudioRelay.gameObject.name;
            SceneManager.MoveGameObjectToScene(createdAudioRelayRoot, bhan);
            CopyTransform(canyonAudioRelay.transform, createdAudioRelayRoot.transform);
            bhanAudioRelay = createdAudioRelayRoot.GetComponent<AudioManagerEventRelay>();
        }

        Dictionary<UnityEngine.Object, UnityEngine.Object> sourceToBhan =
            new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        Dictionary<UnityEngine.Object, UnityEngine.Object> oldToNew =
            new Dictionary<UnityEngine.Object, UnityEngine.Object>();

        foreach (Replacement replacement in replacements)
        {
            AddMappings(sourceToBhan, replacement.sourceToNew);
            AddMappings(oldToNew, replacement.oldToNew);
        }

        AddComponentMapping(sourceToBhan, canyonAlmanac, bhanAlmanac);
        AddComponentMapping(sourceToBhan, canyonAchievements, bhanAchievements);
        AddComponentMapping(sourceToBhan, canyonObjectives, bhanObjectives);
        AddComponentMapping(sourceToBhan, canyonShop, bhanShop);
        if (canyonAudioRelay != null && bhanAudioRelay != null)
            AddComponentMapping(sourceToBhan, canyonAudioRelay, bhanAudioRelay);
        AddNamedSceneMappings(canyon, bhan, sourceToBhan);

        CopyManager(canyonAlmanac, bhanAlmanac, sourceToBhan);
        CopyManager(canyonAchievements, bhanAchievements, sourceToBhan);
        CopyManager(canyonObjectives, bhanObjectives, sourceToBhan);
        CopyManager(canyonShop, bhanShop, sourceToBhan);

        // A cloned UnityEvent can still point at the Canyon manager that originally
        // handled it. Remap both those source references and any references to the
        // Bhan objects being replaced before the old hierarchy is removed.
        Dictionary<UnityEngine.Object, UnityEngine.Object> allReferenceMappings =
            new Dictionary<UnityEngine.Object, UnityEngine.Object>(sourceToBhan);
        AddMappings(allReferenceMappings, oldToNew);
        RemapSceneReferences(
            bhan,
            replacements.SelectMany(OldRoots).ToArray(),
            allReferenceMappings);

        if (!ValidateExactCopies(canyon, replacements,
                bhanAlmanac, bhanAchievements, bhanObjectives, bhanShop))
        {
            Debug.LogError("[Bhan UI Sync] Validation failed; Bhan House was not saved.");
            foreach (Replacement replacement in replacements)
                UnityEngine.Object.DestroyImmediate(replacement.newRoot);
            if (createdAudioRelayRoot != null)
                UnityEngine.Object.DestroyImmediate(createdAudioRelayRoot);
            CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
            return;
        }

        foreach (Replacement replacement in replacements)
            foreach (GameObject oldRoot in OldRoots(replacement))
                UnityEngine.Object.DestroyImmediate(oldRoot);

        // Panels must begin closed; their managers open them when requested.
        bhanAlmanac.Panel.SetActive(false);
        bhanAchievements.achievementPanel.SetActive(false);
        bhanObjectives.trackerPanel.SetActive(false);
        bhanShop.Panel.SetActive(false);

        EditorUtility.SetDirty(bhanAlmanac);
        EditorUtility.SetDirty(bhanAchievements);
        EditorUtility.SetDirty(bhanObjectives);
        EditorUtility.SetDirty(bhanShop);
        EditorSceneManager.MarkSceneDirty(bhan);
        EditorSceneManager.SaveScene(bhan);

        CloseTemporaryScenes(original, canyon, bhan, openedCanyon, openedBhan);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bhan UI Sync] Almanac, Achievements, Objectives, Shop, and access buttons now match Canyon Crossing exactly.");
    }

    private static void EnsureMatchingAchievementParent(
        AchievementUIManager sourceManager,
        AchievementUIManager destinationManager,
        Scene destinationScene)
    {
        Transform sourcePanel = sourceManager.achievementPanel.transform;
        Transform destinationPanel = destinationManager.achievementPanel.transform;
        Transform sourceContainer = sourcePanel.parent;
        Transform currentDestinationParent = destinationPanel.parent;
        if (sourceContainer == null || currentDestinationParent == null)
            return;

        Transform destinationContainer = currentDestinationParent.name == sourceContainer.name
            ? currentDestinationParent
            : FindDirectChild(currentDestinationParent, sourceContainer.name);

        if (destinationContainer == null)
        {
            GameObject container = new GameObject(sourceContainer.name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(container, destinationScene);
            destinationContainer = container.transform;
            destinationContainer.SetParent(currentDestinationParent, false);
            destinationContainer.SetSiblingIndex(Mathf.Min(
                sourceContainer.GetSiblingIndex(),
                currentDestinationParent.childCount - 1));
        }

        CopyTransform(sourceContainer, destinationContainer);
        destinationPanel.SetParent(destinationContainer, false);
        CopyTransform(sourcePanel, destinationPanel);
        EditorUtility.SetDirty(destinationContainer);
        EditorUtility.SetDirty(destinationPanel);
    }

    private static Replacement CloneForReplacement(GameObject source, GameObject destination, Scene targetScene)
    {
        if (source == null || destination == null)
            return null;

        GameObject clone = UnityEngine.Object.Instantiate(source);
        clone.name = source.name;
        SceneManager.MoveGameObjectToScene(clone, targetScene);
        Transform destinationParent = destination.transform.parent;
        clone.transform.SetParent(destinationParent, false);
        clone.transform.SetSiblingIndex(destination.transform.GetSiblingIndex());
        Transform[] sourceTransforms = source.GetComponentsInChildren<Transform>(true);
        Transform[] cloneTransforms = clone.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < Mathf.Min(sourceTransforms.Length, cloneTransforms.Length); i++)
            CopyTransform(sourceTransforms[i], cloneTransforms[i]);

        return new Replacement
        {
            sourceRoot = source,
            oldRoot = destination,
            newRoot = clone,
            sourceToNew = BuildExactMap(source.transform, clone.transform),
            oldToNew = BuildPathMap(destination.transform, clone.transform)
        };
    }

    private static Replacement CloneMissingAccessButtons(
        Transform sourceAccess,
        Transform destinationParent,
        Transform existingShopButton,
        Scene targetScene)
    {
        GameObject placeholder = new GameObject("__AccessButtonsPlaceholder", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(placeholder, targetScene);
        placeholder.transform.SetParent(destinationParent, false);
        if (existingShopButton != null && existingShopButton.parent == destinationParent)
            placeholder.transform.SetSiblingIndex(existingShopButton.GetSiblingIndex());

        Replacement replacement = CloneForReplacement(sourceAccess.gameObject, placeholder, targetScene);
        if (replacement == null) return null;

        HashSet<string> sourceButtonNames = new HashSet<string>();
        for (int i = 0; i < sourceAccess.childCount; i++)
            sourceButtonNames.Add(sourceAccess.GetChild(i).name);

        List<Transform> legacyButtons = new List<Transform>();
        for (int i = 0; i < destinationParent.childCount; i++)
        {
            Transform child = destinationParent.GetChild(i);
            if (child != replacement.newRoot.transform && sourceButtonNames.Contains(child.name))
                legacyButtons.Add(child);
        }

        foreach (Transform legacyButton in legacyButtons)
        {
            Transform newButton = FindDirectChild(replacement.newRoot.transform, legacyButton.name);
            if (newButton != null)
                AddMappings(replacement.oldToNew, BuildPathMap(legacyButton, newButton));
            replacement.additionalOldRoots.Add(legacyButton.gameObject);
        }

        return replacement;
    }

    private static IEnumerable<GameObject> OldRoots(Replacement replacement)
    {
        if (replacement.oldRoot != null) yield return replacement.oldRoot;
        foreach (GameObject root in replacement.additionalOldRoots.Where(item => item != null))
            yield return root;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == childName)
                return parent.GetChild(i);
        return null;
    }

    private static bool ValidateExactCopies(
        Scene sourceScene,
        IEnumerable<Replacement> replacements,
        params Component[] destinationManagers)
    {
        bool valid = true;
        int rootsChecked = 0;
        foreach (Replacement replacement in replacements)
        {
            Transform[] source = replacement.sourceRoot.GetComponentsInChildren<Transform>(true);
            Transform[] copy = replacement.newRoot.GetComponentsInChildren<Transform>(true);
            if (source.Length != copy.Length)
            {
                Debug.LogError($"[Bhan UI Sync] {replacement.sourceRoot.name} hierarchy count differs: " +
                               $"Canyon {source.Length}, Bhan {copy.Length}.");
                valid = false;
                continue;
            }

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].name != copy[i].name || !TransformsMatch(source[i], copy[i]))
                {
                    Debug.LogError($"[Bhan UI Sync] Layout mismatch in {replacement.sourceRoot.name} at " +
                                   $"{RelativePath(replacement.sourceRoot.transform, source[i])}.");
                    valid = false;
                    break;
                }
            }

            valid &= !HasReferencesIntoScene(replacement.newRoot.GetComponentsInChildren<Component>(true), sourceScene);
            rootsChecked++;
        }

        valid &= !HasReferencesIntoScene(destinationManagers, sourceScene);
        if (valid)
            Debug.Log($"[Bhan UI Sync] Validation passed for {rootsChecked} exact feature hierarchies; no Canyon scene references remain.");
        return valid;
    }

    private static bool TransformsMatch(Transform source, Transform copy)
    {
        if (source is RectTransform sourceRect && copy is RectTransform copyRect)
        {
            return Approximately(sourceRect.anchorMin, copyRect.anchorMin, 0.0001f) &&
                   Approximately(sourceRect.anchorMax, copyRect.anchorMax, 0.0001f) &&
                   Approximately(sourceRect.anchoredPosition, copyRect.anchoredPosition, 0.01f) &&
                   Approximately(sourceRect.sizeDelta, copyRect.sizeDelta, 0.01f) &&
                   Approximately(sourceRect.pivot, copyRect.pivot, 0.0001f) &&
                   Mathf.Abs(Quaternion.Dot(sourceRect.localRotation, copyRect.localRotation)) > 0.99999f &&
                   Approximately(sourceRect.localScale, copyRect.localScale, 0.0001f);
        }

        return Approximately(source.localPosition, copy.localPosition, 0.001f) &&
               Mathf.Abs(Quaternion.Dot(source.localRotation, copy.localRotation)) > 0.99999f &&
               Approximately(source.localScale, copy.localScale, 0.0001f);
    }

    private static bool Approximately(Vector2 left, Vector2 right, float tolerance)
    {
        return (left - right).sqrMagnitude <= tolerance * tolerance;
    }

    private static bool Approximately(Vector3 left, Vector3 right, float tolerance)
    {
        return (left - right).sqrMagnitude <= tolerance * tolerance;
    }

    private static bool HasReferencesIntoScene(IEnumerable<Component> components, Scene forbiddenScene)
    {
        bool found = false;
        foreach (Component component in components.Where(item => item != null))
        {
            SerializedObject data = new SerializedObject(component);
            SerializedProperty iterator = data.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object value = iterator.objectReferenceValue;
                Scene referencedScene = value is GameObject go
                    ? go.scene
                    : value is Component referencedComponent
                        ? referencedComponent.gameObject.scene
                        : default;
                if (!referencedScene.IsValid() || referencedScene != forbiddenScene)
                    continue;

                Transform referencedTransform = value is GameObject referencedGameObject
                    ? referencedGameObject.transform
                    : ((Component)value).transform;
                Debug.LogError($"[Bhan UI Sync] {component.name}.{iterator.propertyPath} still points into Canyon Crossing: " +
                               $"{value.GetType().Name} at {ScenePath(referencedTransform)}.");
                found = true;
            }
        }
        return found;
    }

    private static Dictionary<UnityEngine.Object, UnityEngine.Object> BuildExactMap(
        Transform sourceRoot,
        Transform cloneRoot)
    {
        Dictionary<UnityEngine.Object, UnityEngine.Object> map =
            new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        Transform[] sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);
        Transform[] cloneTransforms = cloneRoot.GetComponentsInChildren<Transform>(true);
        int count = Mathf.Min(sourceTransforms.Length, cloneTransforms.Length);
        for (int i = 0; i < count; i++)
            MapTransformAndComponents(sourceTransforms[i], cloneTransforms[i], map);
        return map;
    }

    private static Dictionary<UnityEngine.Object, UnityEngine.Object> BuildPathMap(
        Transform oldRoot,
        Transform newRoot)
    {
        Dictionary<UnityEngine.Object, UnityEngine.Object> map =
            new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        Dictionary<string, Queue<Transform>> newPaths = newRoot.GetComponentsInChildren<Transform>(true)
            .GroupBy(item => RelativePath(newRoot, item))
            .ToDictionary(group => group.Key, group => new Queue<Transform>(group));

        foreach (Transform oldTransform in oldRoot.GetComponentsInChildren<Transform>(true))
        {
            string path = RelativePath(oldRoot, oldTransform);
            if (newPaths.TryGetValue(path, out Queue<Transform> candidates) && candidates.Count > 0)
                MapTransformAndComponents(oldTransform, candidates.Dequeue(), map);
        }
        return map;
    }

    private static void MapTransformAndComponents(
        Transform source,
        Transform destination,
        Dictionary<UnityEngine.Object, UnityEngine.Object> map)
    {
        map[source] = destination;
        map[source.gameObject] = destination.gameObject;

        Component[] sourceComponents = source.GetComponents<Component>();
        Component[] destinationComponents = destination.GetComponents<Component>();
        foreach (IGrouping<Type, Component> group in sourceComponents
                     .Where(item => item != null && !(item is Transform))
                     .GroupBy(item => item.GetType()))
        {
            Component[] matchingSource = group.ToArray();
            Component[] matchingDestination = destinationComponents
                .Where(item => item != null && item.GetType() == group.Key)
                .ToArray();
            for (int i = 0; i < Mathf.Min(matchingSource.Length, matchingDestination.Length); i++)
                map[matchingSource[i]] = matchingDestination[i];
        }
    }

    private static void CopyManager(
        Component source,
        Component destination,
        Dictionary<UnityEngine.Object, UnityEngine.Object> sourceToBhan)
    {
        Dictionary<string, UnityEngine.Object> destinationSceneReferences =
            CaptureSceneReferences(destination);
        EditorUtility.CopySerialized(source, destination);

        SerializedObject destinationData = new SerializedObject(destination);
        SerializedProperty iterator = destinationData.GetIterator();
        bool enterChildren = true;
        while (iterator.Next(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference ||
                iterator.propertyPath == "m_Script")
                continue;

            UnityEngine.Object sourceReference = iterator.objectReferenceValue;
            if (sourceReference == null || !IsSceneObject(sourceReference))
                continue;

            if (sourceToBhan.TryGetValue(sourceReference, out UnityEngine.Object mapped))
                iterator.objectReferenceValue = mapped;
            else if (destinationSceneReferences.TryGetValue(iterator.propertyPath, out UnityEngine.Object previous))
                iterator.objectReferenceValue = previous;
            else
                iterator.objectReferenceValue = null;
        }
        destinationData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Dictionary<string, UnityEngine.Object> CaptureSceneReferences(Component component)
    {
        Dictionary<string, UnityEngine.Object> references =
            new Dictionary<string, UnityEngine.Object>();
        SerializedObject data = new SerializedObject(component);
        SerializedProperty iterator = data.GetIterator();
        bool enterChildren = true;
        while (iterator.Next(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                IsSceneObject(iterator.objectReferenceValue))
                references[iterator.propertyPath] = iterator.objectReferenceValue;
        }
        return references;
    }

    private static void AddNamedSceneMappings(
        Scene sourceScene,
        Scene destinationScene,
        Dictionary<UnityEngine.Object, UnityEngine.Object> map)
    {
        Transform[] sourceTransforms = FindComponents<Transform>(sourceScene);
        Transform[] destinationTransforms = FindComponents<Transform>(destinationScene);
        Dictionary<string, Transform[]> destinationByName = destinationTransforms
            .GroupBy(item => item.name)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (Transform source in sourceTransforms)
        {
            if (map.ContainsKey(source) ||
                !destinationByName.TryGetValue(source.name, out Transform[] matches) ||
                matches.Length != 1)
                continue;

            MapTransformAndComponents(source, matches[0], map);
        }
    }

    private static void RemapSceneReferences(
        Scene scene,
        GameObject[] replacedRoots,
        Dictionary<UnityEngine.Object, UnityEngine.Object> map)
    {
        foreach (Component component in FindComponents<Component>(scene))
        {
            if (component == null || replacedRoots.Any(root =>
                    component.transform == root.transform || component.transform.IsChildOf(root.transform)))
                continue;

            SerializedObject data = new SerializedObject(component);
            SerializedProperty iterator = data.GetIterator();
            bool enterChildren = true;
            bool changed = false;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object value = iterator.objectReferenceValue;
                if (value != null && map.TryGetValue(value, out UnityEngine.Object replacement))
                {
                    iterator.objectReferenceValue = replacement;
                    changed = true;
                }
            }
            if (changed) data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AddComponentMapping(
        Dictionary<UnityEngine.Object, UnityEngine.Object> map,
        Component source,
        Component destination)
    {
        map[source] = destination;
        map[source.gameObject] = destination.gameObject;
        map[source.transform] = destination.transform;
    }

    private static void AddMappings(
        Dictionary<UnityEngine.Object, UnityEngine.Object> destination,
        Dictionary<UnityEngine.Object, UnityEngine.Object> additions)
    {
        foreach (KeyValuePair<UnityEngine.Object, UnityEngine.Object> pair in additions)
            destination[pair.Key] = pair.Value;
    }

    private static string RelativePath(Transform root, Transform item)
    {
        if (item == root) return string.Empty;
        List<string> pieces = new List<string>();
        Transform cursor = item;
        while (cursor != null && cursor != root)
        {
            pieces.Add(cursor.name);
            cursor = cursor.parent;
        }
        pieces.Reverse();
        return string.Join("/", pieces);
    }

    private static string ScenePath(Transform item)
    {
        if (item == null) return "<missing>";
        List<string> pieces = new List<string>();
        for (Transform cursor = item; cursor != null; cursor = cursor.parent)
            pieces.Add(cursor.name);
        pieces.Reverse();
        return string.Join("/", pieces);
    }

    private static void CopyTransform(Transform source, Transform destination)
    {
        if (source is RectTransform sourceRect && destination is RectTransform destinationRect)
        {
            destinationRect.anchorMin = sourceRect.anchorMin;
            destinationRect.anchorMax = sourceRect.anchorMax;
            destinationRect.anchoredPosition = sourceRect.anchoredPosition;
            destinationRect.sizeDelta = sourceRect.sizeDelta;
            destinationRect.pivot = sourceRect.pivot;
            destinationRect.localRotation = sourceRect.localRotation;
            destinationRect.localScale = sourceRect.localScale;
            return;
        }

        destination.localPosition = source.localPosition;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
    }

    private static bool IsSceneObject(UnityEngine.Object value)
    {
        if (value is GameObject gameObject) return gameObject.scene.IsValid();
        if (value is Component component) return component.gameObject.scene.IsValid();
        return false;
    }

    private static T FindComponent<T>(Scene scene) where T : Component
    {
        return FindComponents<T>(scene).FirstOrDefault();
    }

    private static T[] FindComponents<T>(Scene scene) where T : Component
    {
        return Resources.FindObjectsOfTypeAll<T>()
            .Where(item => item != null && item.gameObject.scene == scene)
            .ToArray();
    }

    private static Transform FindTransform(Scene scene, string objectName)
    {
        return FindComponents<Transform>(scene).FirstOrDefault(item => item.name == objectName);
    }

    private static void CloseTemporaryScenes(
        Scene original,
        Scene canyon,
        Scene bhan,
        bool openedCanyon,
        bool openedBhan)
    {
        if (original.IsValid() && original.isLoaded)
            SceneManager.SetActiveScene(original);
        if (openedBhan && bhan.IsValid() && bhan.isLoaded)
            EditorSceneManager.CloseScene(bhan, true);
        if (openedCanyon && canyon.IsValid() && canyon.isLoaded)
            EditorSceneManager.CloseScene(canyon, true);
    }
}
#endif
