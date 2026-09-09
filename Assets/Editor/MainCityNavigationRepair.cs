using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Explicit repair for the saved Main City decoration collection, not the terrain prefabs.
public static class MainCityNavigationRepair
{
    [MenuItem("Tools/Civil Craft/Fix Main City Decoration Navigation")]
    public static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Exit Play Mode before repairing Main City navigation."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "CanyonCrossing") return;
        Transform city = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "MAIN CITY") city = t;
        if (city == null) { Debug.LogWarning("MAIN CITY not found."); return; }
        int layer = LayerMask.NameToLayer("Environment");
        if (layer < 0) throw new InvalidOperationException("Environment layer is missing.");
        int count = 0;
        var buildings = new HashSet<Transform>();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Repair Main City navigation");
        foreach (var filter in city.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
            // Only authored buildings/solid props. Do not classify terrain, roads or path overlays.
            bool decoration = false;
            Transform building = null;
            for (Transform t = filter.transform; t != null && t != city; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("medieval") || n.Contains("factory") || n.Contains("shop_") ||
                    n.Contains("well_") || n.Contains("crate") || n.Contains("barrel")) decoration = true;
                if (n.Contains("medieval") || n.Contains("factory") || n.Contains("interior_ud"))
                { building = t; decoration = true; }
            }
            if (!decoration || filter.GetComponentInParent<Rigidbody>() != null) continue;
            if (building != null) buildings.Add(building);
            Undo.RecordObject(filter.gameObject, "Include decoration in navigation");
            filter.gameObject.layer = layer;
            // Match interior_ud: a real mesh collider, not merely any collider
            // (an existing small box/interaction collider is not the building shell).
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null) collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
            Undo.RecordObject(collider, "Match interior house mesh collision");
            collider.sharedMesh = filter.sharedMesh;
            collider.enabled = true;
            collider.isTrigger = false;
            collider.convex = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            var modifier = filter.GetComponent<NavMeshModifier>();
            if (modifier != null)
            {
                Undo.RecordObject(modifier, "Use collider-based bake like interior house");
                modifier.overrideArea = false;
                modifier.ignoreFromBuild = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(modifier);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter.gameObject);
            count++;
        }
        int footprints = 0;
        foreach (Transform building in buildings)
        {
            Transform oldFootprint = building.Find("Navigation Footprint (Not Walkable)");
            if (oldFootprint != null)
            {
                Undo.RecordObject(oldFootprint.gameObject, "Disable generated footprint");
                oldFootprint.gameObject.SetActive(false);
            }
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[MainCityNavigation] Matched interior_ud collision on {count} Main City meshes. Generated footprints disabled, not deleted. Save and rebake the world Surface. NPC progression was not changed.", city);
    }

    // Retained for reference only; the repair above now matches the user's mesh-collider setup.
    private static void LegacyFootprints(HashSet<Transform> buildings, int layer)
    {
        int footprints = 0;
        foreach (Transform building in buildings)
        {
            bool found = false;
            Bounds bounds = new Bounds();
            foreach (var renderer in building.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) continue;
            const string volumeName = "Navigation Footprint (Not Walkable)";
            Transform footprint = building.Find(volumeName);
            if (footprint == null)
            {
                var go = new GameObject(volumeName);
                Undo.RegisterCreatedObjectUndo(go, "Add building navigation footprint");
                footprint = go.transform;
                Undo.SetTransformParent(footprint, building, "Parent navigation footprint");
            }
            Undo.RecordObject(footprint, "Position navigation footprint");
            footprint.position = bounds.center;
            footprint.rotation = Quaternion.identity;
            // Account for the imported models' large, nonuniform scale.
            footprint.localScale = Vector3.one;
            Vector3 scale = footprint.lossyScale;
            var volume = footprint.GetComponent<NavMeshModifierVolume>();
            if (volume == null) volume = Undo.AddComponent<NavMeshModifierVolume>(footprint.gameObject);
            Undo.RecordObject(footprint.gameObject, "Set footprint layer");
            footprint.gameObject.layer = layer;
            Undo.RecordObject(volume, "Size building exclusion");
            volume.area = NavMesh.GetAreaFromName("Not Walkable");
            // Extend below the visual base so the ground beneath an open-bottom mesh is excluded.
            volume.center = new Vector3(0, -2 / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 0);
            volume.size = new Vector3(bounds.size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                (bounds.size.y + 4) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            EditorUtility.SetDirty(volume);
            footprints++;
        }
    }
}
