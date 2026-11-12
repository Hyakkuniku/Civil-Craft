#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in campaign vehicle audio setup for Canyon Crossing and Town River.</summary>
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
        Scene scene = Canyon();
        foreach (LiveLoadVehicle vehicle in All<LiveLoadVehicle>(scene))
        {
            string models = string.Join(" | ", vehicle.GetComponentsInChildren<MeshFilter>(true)
                .Where(mesh => mesh.sharedMesh != null).Select(mesh => AssetDatabase.GetAssetPath(mesh.sharedMesh)).Distinct());
            var settings = new SerializedObject(vehicle).FindProperty("simulationSound");
            report.AppendLine($"VEHICLE target={Target(vehicle.assignedContract)}, map={vehicle.assignedContract?.contractMap}, contract={vehicle.assignedContract?.ContractID}, name={vehicle.vehicleName}, path={PathOf(vehicle.transform)}");
            report.AppendLine($"  MODELS {models}; currentClip={settings?.FindPropertyRelative("clip")?.objectReferenceValue?.name}");
        }
        foreach (BoatBridgeCrossing boat in All<BoatBridgeCrossing>(scene))
            report.AppendLine($"BOAT target={Target(boat.assignedContract)}, map={boat.assignedContract?.contractMap}, contract={boat.assignedContract?.ContractID}, path={PathOf(boat.transform)}");
    }

    private static string Profile(LiveLoadVehicle vehicle)
    {
        string label = vehicle.vehicleName.ToLowerInvariant();
        if (label.Contains("cart")) return "handcart";
        if (label.Contains("taxi")) return "taxi";
        if (label.Contains("jeep")) return "jeep";
        if (label.Contains("suv")) return "suv";
        if (label.Contains("ford") || label.Contains("pickup")) return "pickup";
        if (label.Contains("delivery")) return "delivery_truck";
        if (label.Contains("cargo")) return "cargo_truck";
        string models = string.Join("|", vehicle.GetComponentsInChildren<MeshFilter>(true)
            .Where(mesh => mesh.sharedMesh != null).Select(mesh => AssetDatabase.GetAssetPath(mesh.sharedMesh))).ToLowerInvariant();
        if (models.Contains("cart_")) return "handcart";
        if (models.Contains("jeep")) return "jeep";
        if (models.Contains("truck1 1") || models.Contains("pickup")) return "pickup";
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
}
#endif
