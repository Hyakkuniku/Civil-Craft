#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in vehicle audio setup for every game scene and reusable vehicle prefab.</summary>
[InitializeOnLoad]
internal static class CampaignVehicleAudioAuthoring
{
    private const string Request = "Temp/campaign-vehicle-audio.request";
    private const string Report = "Logs/campaign-vehicle-audio.txt";
    internal const string AudioFolder = "Assets/Elements/SFX/Vehicles/CampaignLoops";
    static CampaignVehicleAudioAuthoring() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try
        {
            var report = new StringBuilder("Campaign vehicle audio\n");
            if (command == "inspect") Inspect(report);
            else if (command == "inspect-all") InspectAll(report);
            else if (command == "apply-all") ApplyAll(report);
            else if (command == "validate-all") ValidateAll(report);
            else if (command == "apply") Apply(report);
            else if (command == "validate") Validate(Canyon(), report);
            else throw new InvalidOperationException("Unknown campaign vehicle audio command.");
            File.WriteAllText(Report, report.ToString());
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    private static bool Target(ContractSO contract) => contract != null &&
        (contract.contractMap == ContractSO.ContractMap.CanyonCrossing || contract.contractMap == ContractSO.ContractMap.TownRiver);
    private static T[] All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
    private static Scene Canyon()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CanyonCrossing first.");
        return scene;
    }

    private static void Inspect(StringBuilder report)
    {
        Inspect(Canyon(), report);
    }

    private static void Inspect(Scene scene, StringBuilder report)
    {
        report.AppendLine("SCENE " + scene.name);
        foreach (LiveLoadVehicle vehicle in All<LiveLoadVehicle>(scene))
        {
            string models = string.Join(" | ", vehicle.GetComponentsInChildren<MeshFilter>(true)
                .Where(mesh => mesh.sharedMesh != null).Select(mesh => AssetDatabase.GetAssetPath(mesh.sharedMesh)).Distinct());
            var settings = new SerializedObject(vehicle).FindProperty("simulationSound");
            report.AppendLine($"VEHICLE target={Target(vehicle.assignedContract)}, map={vehicle.assignedContract?.contractMap}, contract={vehicle.assignedContract?.ContractID}, name={vehicle.vehicleName}, path={PathOf(vehicle.transform)}");
            report.AppendLine($"  MODELS {models}; currentClip={settings?.FindPropertyRelative("clip")?.objectReferenceValue?.name}");
        }
        foreach (BoatBridgeCrossing boat in All<BoatBridgeCrossing>(scene))
            report.AppendLine($"BOAT target={Target(boat.assignedContract)}, map={boat.assignedContract?.contractMap}, contract={boat.assignedContract?.ContractID}, path={PathOf(boat.transform)}, clip={new SerializedObject(boat).FindProperty("simulationSound").FindPropertyRelative("clip").objectReferenceValue?.name}");
        foreach (MeshFilter mesh in All<MeshFilter>(scene))
        {
            string path = AssetDatabase.GetAssetPath(mesh.sharedMesh);
            if (!path.StartsWith("Assets/Elements/Vehicles/", StringComparison.Ordinal) ||
                mesh.GetComponentInParent<LiveLoadVehicle>(true) != null || mesh.GetComponentInParent<BoatBridgeCrossing>(true) != null) continue;
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(mesh.gameObject);
            if (root == null || root.GetComponentInChildren<MeshFilter>(true) != mesh) continue;
            report.AppendLine("DISPLAY " + PathOf(root.transform) + " model=" + path + "; scripts=" +
                string.Join(",", root.GetComponentsInParent<MonoBehaviour>(true).Where(component => component != null).Select(component => component.GetType().Name)));
        }
    }

    private static string[] VehiclePrefabs()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return prefab != null && (prefab.GetComponentInChildren<LiveLoadVehicle>(true) != null ||
                    prefab.GetComponentInChildren<BoatBridgeCrossing>(true) != null);
            }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static void InspectAll(StringBuilder report)
    {
        foreach (EditorBuildSettingsScene definition in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            Scene scene = SceneManager.GetSceneByPath(definition.path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(definition.path, OpenSceneMode.Additive);
                Inspect(scene, report);
            }
            finally { if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        }
        foreach (string path in VehiclePrefabs())
        {
            report.AppendLine("PREFAB " + path);
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (LiveLoadVehicle vehicle in root.GetComponentsInChildren<LiveLoadVehicle>(true))
                report.AppendLine($"  ROAD {PathOf(vehicle.transform)} name={vehicle.vehicleName}; clip={new SerializedObject(vehicle).FindProperty("simulationSound").FindPropertyRelative("clip").objectReferenceValue?.name}; models=" +
                    string.Join("|", vehicle.GetComponentsInChildren<MeshFilter>(true).Where(mesh => mesh.sharedMesh != null).Select(mesh => AssetDatabase.GetAssetPath(mesh.sharedMesh)).Distinct()));
            foreach (BoatBridgeCrossing boat in root.GetComponentsInChildren<BoatBridgeCrossing>(true))
                report.AppendLine($"  BOAT {PathOf(boat.transform)}; clip={new SerializedObject(boat).FindProperty("simulationSound").FindPropertyRelative("clip").objectReferenceValue?.name}");
        }
    }

    private static string Profile(LiveLoadVehicle vehicle)
    {
        string label = (vehicle.vehicleName ?? string.Empty).ToLowerInvariant();
        string models = string.Join("|", vehicle.GetComponentsInChildren<MeshFilter>(true)
            .Where(mesh => mesh.sharedMesh != null).Select(mesh => AssetDatabase.GetAssetPath(mesh.sharedMesh))).ToLowerInvariant();
        // Recently replaced models can retain an older generic vehicle label.
        // Prefer distinctive meshes over that label and hidden truck chassis.
        if (models.Contains("crane") || label.Contains("crane")) return "mobile_crane";
        if (models.Contains("tanker") || label.Contains("tanker")) return "tanker_truck";
        if (models.Contains("regional truck") || label.Contains("regional")) return "regional_truck";
        if (models.Contains("bus") || label.Contains("bus")) return "worker_bus";
        if (models.Contains("plane") || label.Contains("plane")) return "propeller_plane";
        if (models.Contains("scoot") || label.Contains("scoot")) return "scooter";
        if (models.Contains("service van") || label.Contains("van")) return "service_van";
        if (label.Contains("cart")) return "handcart";
        if (label.Contains("taxi")) return "taxi";
        if (label.Contains("jeep")) return "jeep";
        if (label.Contains("suv")) return "suv";
        if (label.Contains("ford") || label.Contains("pickup")) return "pickup";
        if (label.Contains("delivery")) return "delivery_truck";
        if (label.Contains("cargo")) return "cargo_truck";
        if (models.Contains("cart_")) return "handcart";
        if (models.Contains("taxi")) return "taxi";
        if (models.Contains("jeep")) return "jeep";
        if (models.Contains("suv")) return "suv";
        if (models.Contains("truck1 1") || models.Contains("pickup")) return "pickup";
        if (models.Contains("/truck.fbx") && !PathOf(vehicle.transform).ToLowerInvariant().Contains("cargo")) return "delivery_truck";
        if (models.Contains("truck")) return "cargo_truck";
        throw new InvalidOperationException("Review the audio type for " + PathOf(vehicle.transform));
    }

    private static AudioClip Clip(string profile)
    {
        string action = profile == "handcart" ? "rolling_loop" : "slow_drive_loop";
        string path = AudioFolder + "/" + profile + "_" + action + ".wav";
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("Missing vehicle loop: " + path);
        return clip;
    }

    private static void Assign(Component owner, AudioClip clip, float volume, float pitch)
    {
        Undo.RecordObject(owner, "Assign campaign vehicle audio");
        var data = new SerializedObject(owner);
        var sound = data.FindProperty("simulationSound");
        if (sound == null) throw new InvalidOperationException("Missing audio settings on " + owner.name);
        sound.FindPropertyRelative("clip").objectReferenceValue = clip;
        sound.FindPropertyRelative("output").objectReferenceValue = null;
        sound.FindPropertyRelative("volume").floatValue = volume;
        sound.FindPropertyRelative("pitch").floatValue = pitch;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(owner);
        PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
    }

    private static void Apply(StringBuilder report)
    {
        Scene scene = Canyon();
        var vehicles = All<LiveLoadVehicle>(scene).Where(vehicle => Target(vehicle.assignedContract)).ToArray();
        var boats = All<BoatBridgeCrossing>(scene).Where(boat => Target(boat.assignedContract)).ToArray();
        if (vehicles.Length == 0) throw new InvalidOperationException("No campaign vehicles were found.");
        // Resolve every required clip before changing scene objects.
        foreach (LiveLoadVehicle vehicle in vehicles) Clip(Profile(vehicle));
        if (boats.Length > 0) Clip("boat");
        var outside = All<LiveLoadVehicle>(scene).Where(vehicle => !Target(vehicle.assignedContract))
            .ToDictionary(vehicle => vehicle, vehicle => new SerializedObject(vehicle).FindProperty("simulationSound").FindPropertyRelative("clip").objectReferenceValue);
        string backup = "Temp/CampaignVehicleAudio-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
        File.Copy(scene.path, backup);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Canyon Crossing and Town River vehicle audio");
        try
        {
            foreach (var profile in vehicles.GroupBy(Profile))
            {
                var ordered = profile.OrderBy(vehicle => PathOf(vehicle.transform), StringComparer.Ordinal).ToArray();
                for (int i = 0; i < ordered.Length; i++)
                {
                    // Slightly detune matching engines so paired test vehicles
                    // retain separate voices instead of doubling one waveform.
                    float pitch = ordered.Length > 1 ? (i % 2 == 0 ? .985f : 1.015f) : 1f;
                    Assign(ordered[i], Clip(profile.Key), .75f, pitch);
                }
            }
            foreach (BoatBridgeCrossing boat in boats) Assign(boat, Clip("boat"), .70f, 1f);
            Validate(scene, report);
            foreach (var entry in outside)
                if (new SerializedObject(entry.Key).FindProperty("simulationSound").FindPropertyRelative("clip").objectReferenceValue != entry.Value)
                    throw new InvalidOperationException("A vehicle outside the requested maps was changed.");
            Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save campaign vehicle audio.");
            Undo.CollapseUndoOperations(group);
            report.AppendLine($"SAVED: {vehicles.Length} road vehicles and {boats.Length} boat(s); scene backup {backup}");
            report.AppendLine("PASS: vehicle audio outside Canyon Crossing and Town River is unchanged.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static void Validate(Scene scene, StringBuilder report)
    {
        int roadCount = 0, boatCount = 0;
        foreach (LiveLoadVehicle vehicle in All<LiveLoadVehicle>(scene).Where(vehicle => Target(vehicle.assignedContract)))
        {
            string profile = Profile(vehicle);
            CheckSettings(vehicle, Clip(profile)); roadCount++;
            report.AppendLine($"PASS {vehicle.assignedContract.contractMap}/{vehicle.assignedContract.ContractID}: {PathOf(vehicle.transform)} -> {profile}");
        }
        foreach (BoatBridgeCrossing boat in All<BoatBridgeCrossing>(scene).Where(boat => Target(boat.assignedContract)))
        {
            CheckSettings(boat, Clip("boat")); boatCount++;
            report.AppendLine($"PASS {boat.assignedContract.contractMap}/{boat.assignedContract.ContractID}: {PathOf(boat.transform)} -> boat");
        }
        report.AppendLine($"PASS coverage: {roadCount} road vehicles and {boatCount} crossing boat(s), including inactive story/contract variants.");
    }

    private static void CheckSettings(Component component, AudioClip expected)
    {
        var sound = new SerializedObject(component).FindProperty("simulationSound");
        if (sound.FindPropertyRelative("clip").objectReferenceValue != expected || expected == null ||
            sound.FindPropertyRelative("volume").floatValue <= 0 || sound.FindPropertyRelative("pitch").floatValue <= 0 ||
            expected.channels != 1 || Mathf.Abs(expected.length - 8f) > .001f || expected.loadType != AudioClipLoadType.DecompressOnLoad)
            throw new InvalidOperationException("Invalid/inaudible campaign loop on " + component.name);
    }

    private static void VisitScenes(Action<Scene> action)
    {
        Scene active = SceneManager.GetActiveScene();
        try
        {
            foreach (EditorBuildSettingsScene definition in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                Scene scene = SceneManager.GetSceneByPath(definition.path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    if (opened) scene = EditorSceneManager.OpenScene(definition.path, OpenSceneMode.Additive);
                    action(scene);
                }
                finally { if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    private static void ResolveClips(GameObject root)
    {
        foreach (LiveLoadVehicle vehicle in root.GetComponentsInChildren<LiveLoadVehicle>(true)) Clip(Profile(vehicle));
        if (root.GetComponentInChildren<BoatBridgeCrossing>(true) != null) Clip("boat");
    }

    private static bool NeedsAssignment(Component owner, AudioClip expected)
    {
        var sound = new SerializedObject(owner).FindProperty("simulationSound");
        return sound.FindPropertyRelative("clip").objectReferenceValue != expected ||
            sound.FindPropertyRelative("volume").floatValue <= 0f || sound.FindPropertyRelative("pitch").floatValue <= 0f;
    }

    private static int AssignRoots(GameObject[] roots, bool useUndo)
    {
        int changed = 0;
        foreach (var profile in roots.SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true)).GroupBy(Profile))
        {
            var vehicles = profile.OrderBy(vehicle => PathOf(vehicle.transform), StringComparer.Ordinal).ToArray();
            AudioClip clip = Clip(profile.Key);
            for (int i = 0; i < vehicles.Length; i++)
            {
                LiveLoadVehicle vehicle = vehicles[i];
                if (!NeedsAssignment(vehicle, clip)) continue;
                AssignForAll(vehicle, clip, .75f, vehicles.Length > 1 ? (i % 2 == 0 ? .985f : 1.015f) : 1f, useUndo);
                changed++;
            }
        }
        foreach (BoatBridgeCrossing boat in roots.SelectMany(root => root.GetComponentsInChildren<BoatBridgeCrossing>(true)))
        {
            AudioClip clip = Clip("boat");
            if (!NeedsAssignment(boat, clip)) continue;
            AssignForAll(boat, clip, .70f, 1f, useUndo);
            changed++;
        }
        return changed;
    }

    private static void AssignForAll(Component owner, AudioClip clip, float volume, float pitch, bool useUndo)
    {
        if (useUndo) Undo.RecordObject(owner, "Assign vehicle movement audio");
        var data = new SerializedObject(owner);
        var sound = data.FindProperty("simulationSound");
        sound.FindPropertyRelative("clip").objectReferenceValue = clip;
        sound.FindPropertyRelative("output").objectReferenceValue = null;
        sound.FindPropertyRelative("volume").floatValue = volume;
        sound.FindPropertyRelative("pitch").floatValue = pitch;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(owner);
        if (PrefabUtility.IsPartOfPrefabInstance(owner)) PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
    }

    private static string Backup(string path)
    {
        string folder = "Temp/AllVehicleAudio-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(folder);
        string target = System.IO.Path.Combine(folder, AssetDatabase.AssetPathToGUID(path) + System.IO.Path.GetExtension(path));
        if (!File.Exists(target)) File.Copy(path, target);
        return target;
    }

    private static void ApplyAll(StringBuilder report)
    {
        string[] prefabs = VehiclePrefabs();
        // Check all mappings and imports before saving any asset.
        foreach (string path in prefabs) ResolveClips(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        VisitScenes(scene => { foreach (GameObject root in scene.GetRootGameObjects()) ResolveClips(root); });
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = AssignRoots(new[] { root }, false);
                ValidateRoots(new[] { root }, path, report);
                if (changed == 0) continue;
                string backup = Backup(path);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success) throw new IOException("Could not save vehicle audio on " + path);
                report.AppendLine($"SAVED {path}: {changed} vehicle audio assignments; backup={backup}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        VisitScenes(scene =>
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add all vehicle movement sounds");
            try
            {
                int changed = AssignRoots(roots, true);
                ValidateRoots(roots, scene.name, report);
                if (changed == 0) return;
                string backup = Backup(scene.path);
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save vehicle audio in " + scene.path);
                Undo.CollapseUndoOperations(group);
                report.AppendLine($"SAVED {scene.path}: {changed} vehicle audio assignments; backup={backup}");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        });
        report.AppendLine("PASS all game scenes and reusable vehicle prefabs have movement audio.");
    }

    private static void ValidateRoots(GameObject[] roots, string label, StringBuilder report)
    {
        int roadCount = 0, boatCount = 0;
        foreach (GameObject root in roots)
        {
            foreach (LiveLoadVehicle vehicle in root.GetComponentsInChildren<LiveLoadVehicle>(true))
            {
                string profile = Profile(vehicle);
                CheckSettings(vehicle, Clip(profile));
                report.AppendLine($"PASS {label}/{PathOf(vehicle.transform)} -> {profile}");
                roadCount++;
            }
            foreach (BoatBridgeCrossing boat in root.GetComponentsInChildren<BoatBridgeCrossing>(true))
            {
                CheckSettings(boat, Clip("boat"));
                report.AppendLine($"PASS {label}/{PathOf(boat.transform)} -> boat");
                boatCount++;
            }
        }
        report.AppendLine($"COVERAGE {label}: {roadCount} road vehicles and {boatCount} boats (including inactive variants).");
    }

    private static void ValidateAll(StringBuilder report)
    {
        VisitScenes(scene => ValidateRoots(scene.GetRootGameObjects(), scene.name, report));
        foreach (string path in VehiclePrefabs())
            ValidateRoots(new[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) }, path, report);
        report.AppendLine("PASS all vehicle audio references saved and valid.");
    }
}
#endif
