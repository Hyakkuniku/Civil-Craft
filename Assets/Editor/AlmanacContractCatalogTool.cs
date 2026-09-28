#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps both story scenes' Almanac lists limited to the authored contract folder.
/// This is an explicit menu action, not an OnValidate repair that rewrites manual edits.
/// </summary>
public static class AlmanacContractCatalogTool
{
    private const string ContractFolder = "Assets/BridgeBuilder/Data/Contract";
    private const string CanyonScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string BhanScenePath = "Assets/Scenes/BHAN HOUSE.unity";
    private const string MenuPath = "Tools/Civil Craft/Almanac/Fill Contracts From Contract Folder";

    [MenuItem(MenuPath)]
    public static void FillFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Fill Almanac Contracts",
                "Exit Play Mode before updating scene-authored Almanac lists.", "OK");
            return;
        }

        if (!TryLoadContracts(out List<ContractSO> discovered)) return;

        // Never silently save unrelated, unsaved scene edits when this tool runs.
        foreach (string path in new[] { CanyonScenePath, BhanScenePath })
        {
            Scene loaded = SceneManager.GetSceneByPath(path);
            if (!loaded.IsValid() || !loaded.isLoaded || !loaded.isDirty) continue;
            EditorUtility.DisplayDialog("Fill Almanac Contracts",
                $"Save your changes to {loaded.name} before running this tool. " +
                "No Almanac lists were changed.", "OK");
            return;
        }

        Scene previousActive = SceneManager.GetActiveScene();
        List<Scene> openedScenes = new List<Scene>(2);
        try
        {
            Scene canyon = GetOrOpenScene(CanyonScenePath, openedScenes);
            Scene bhan = GetOrOpenScene(BhanScenePath, openedScenes);
            List<ContractAlmanacTab> canyonTabs = FindTabs(canyon);
            List<ContractAlmanacTab> bhanTabs = FindTabs(bhan);
            if (canyonTabs.Count == 0 || bhanTabs.Count == 0)
                throw new InvalidOperationException(
                    "Both Canyon Crossing and Bhan House must contain a ContractAlmanacTab.");

            List<ContractSO> ordered = PreserveCanyonOrder(canyonTabs, discovered);
            int canyonUpdated = UpdateScene(canyon, canyonTabs, ordered);
            int bhanUpdated = UpdateScene(bhan, bhanTabs, ordered);

            Debug.Log($"[Almanac Contracts] Found {ordered.Count} ContractSO assets under " +
                $"{ContractFolder}. Updated {canyonUpdated} Canyon tab(s) and " +
                $"{bhanUpdated} Bhan House tab(s).");
            EditorUtility.DisplayDialog("Almanac Contracts Updated",
                $"Registered {ordered.Count} contracts from {ContractFolder} (including subfolders).\n\n" +
                $"Canyon Crossing tabs updated: {canyonUpdated}\n" +
                $"Bhan House tabs updated: {bhanUpdated}", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Fill Almanac Contracts Failed",
                exception.Message, "OK");
        }
        finally
        {
            for (int i = openedScenes.Count - 1; i >= 0; i--)
            {
                Scene scene = openedScenes[i];
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }

            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }
    }

    private static bool TryLoadContracts(out List<ContractSO> contracts)
    {
        contracts = new List<ContractSO>();
        if (!AssetDatabase.IsValidFolder(ContractFolder))
        {
            EditorUtility.DisplayDialog("Fill Almanac Contracts",
                $"Contract folder not found: {ContractFolder}", "OK");
            return false;
        }

        string[] guids = AssetDatabase.FindAssets("t:ContractSO", new[] { ContractFolder });
        List<string> paths = new List<string>(guids.Length);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(ContractFolder + "/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            ContractSO contract = AssetDatabase.LoadAssetAtPath<ContractSO>(path);
            if (contract == null) continue;
            if (string.IsNullOrWhiteSpace(contract.contractID) || !ids.Add(contract.ContractID))
            {
                EditorUtility.DisplayDialog("Fill Almanac Contracts",
                    $"Missing or duplicate Contract ID at {path}. Fix the contract ID first; " +
                    "no Almanac lists were changed.", "OK");
                return false;
            }
            contracts.Add(contract);
        }

        if (contracts.Count > 0) return true;
        EditorUtility.DisplayDialog("Fill Almanac Contracts",
            $"No ContractSO assets were found under {ContractFolder}. " +
            "The existing Almanac lists were not cleared.", "OK");
        return false;
    }

    private static Scene GetOrOpenScene(string path, List<Scene> openedScenes)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        if (scene.IsValid() && scene.isLoaded) return scene;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Could not open " + path);
        openedScenes.Add(scene);
        return scene;
    }

    private static List<ContractAlmanacTab> FindTabs(Scene scene)
    {
        List<ContractAlmanacTab> tabs = new List<ContractAlmanacTab>();
        foreach (GameObject root in scene.GetRootGameObjects())
            tabs.AddRange(root.GetComponentsInChildren<ContractAlmanacTab>(true));
        return tabs;
    }

    private static List<ContractSO> PreserveCanyonOrder(
        List<ContractAlmanacTab> canyonTabs, List<ContractSO> discovered)
    {
        // The wired page is the canonical order; inactive template copies may only
        // contain the first two tutorial contracts.
        ContractAlmanacTab source = null;
        foreach (ContractAlmanacTab tab in canyonTabs)
        {
            if (tab.titleText == null || tab.snapshotImage == null) continue;
            int count = tab.allGameContracts != null ? tab.allGameContracts.Count : 0;
            int sourceCount = source != null && source.allGameContracts != null
                ? source.allGameContracts.Count : 0;
            if (source == null || count > sourceCount)
                source = tab;
        }
        if (source == null) source = canyonTabs[0];

        HashSet<ContractSO> allowed = new HashSet<ContractSO>(discovered);
        HashSet<ContractSO> added = new HashSet<ContractSO>();
        List<ContractSO> ordered = new List<ContractSO>(discovered.Count);
        if (source.allGameContracts != null)
        {
            foreach (ContractSO contract in source.allGameContracts)
                if (contract != null && allowed.Contains(contract) && added.Add(contract))
                    ordered.Add(contract);
        }
        foreach (ContractSO contract in discovered)
            if (added.Add(contract)) ordered.Add(contract);
        return ordered;
    }

    private static int UpdateScene(Scene scene, List<ContractAlmanacTab> tabs,
        List<ContractSO> contracts)
    {
        int updated = 0;
        foreach (ContractAlmanacTab tab in tabs)
        {
            if (ListsMatch(tab.allGameContracts, contracts)) continue;
            Undo.RecordObject(tab, "Fill Almanac Contracts");
            tab.allGameContracts = new List<ContractSO>(contracts);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tab);
            EditorUtility.SetDirty(tab);
            updated++;
        }

        if (updated == 0) return 0;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + scene.path);
        return updated;
    }

    private static bool ListsMatch(List<ContractSO> current, List<ContractSO> expected)
    {
        if (current == null || current.Count != expected.Count) return false;
        for (int i = 0; i < expected.Count; i++)
            if (current[i] != expected[i]) return false;
        return true;
    }
}
#endif
