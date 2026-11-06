#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit asset-only regional material rollout; no scene or player state is changed.</summary>
[InitializeOnLoad]
public static class RegionalContractMaterialAuthoring
{
    private const string Request = "Temp/regional-contract-materials.request";
    private const string Report = "Logs/regional-contract-materials.txt";
    private const string Folder = "Assets/BridgeBuilder/Data/Contract";
    private static double nextCheck;
    private static bool running;
    private static readonly string[] Names = { "Wood Road", "Beam", "Pier", "RopeMaterial", "Concrete Road", "ConcreteBeam", "Concrete Pier", "SteelBeam", "SteelPier", "SteelRope" };
    static RegionalContractMaterialAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (running || !File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request); Run(command);
    }
    [MenuItem("Tools/Civil Craft/Contracts/Regional Materials/Inspect")]
    public static void Inspect() => Run("inspect");
    [MenuItem("Tools/Civil Craft/Contracts/Regional Materials/Apply")]
    public static void Apply() => Run("apply");
    [MenuItem("Tools/Civil Craft/Contracts/Regional Materials/Validate")]
    public static void Validate() => Run("validate");

    private sealed class Plan
    {
        internal ContractSO contract;
        internal string path, originalJson, protectedJson;
        internal List<MaterialAllowance> allowed;
        internal List<BridgeMaterialSO> hidden;
        internal bool changed;
    }
    private static void Run(string command)
    {
        running = true;
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder();
        SceneSetup[] sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        var sceneDirty = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .ToDictionary(scene => scene, scene => scene.isDirty);
        var plans = new List<Plan>();
        bool beganSaving = false;
        try
        {
            Require(command == "inspect" || command == "apply" || command == "validate", "Use inspect, apply or validate.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                "Run in idle Edit Mode after compilation.");
            var materials = Names.ToDictionary(name => name, name => AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/" + name + ".asset"));
            Require(materials.All(pair => pair.Value != null), "A required regional material asset is missing.");
            ContractSO[] contracts = AssetDatabase.FindAssets("t:ContractSO", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<ContractSO>).ToArray();
            Require(contracts.Length == 19, "Expected the 19 audited contracts; found " + contracts.Length + ". Review new or missing assets before applying.");
            if (command == "apply")
                foreach (ContractSO contract in contracts)
                    Require(!EditorUtility.IsDirty(contract), "Unsaved Inspector edits: " + AssetDatabase.GetAssetPath(contract) + ". Save this exact asset before applying; no changes were made.");
            foreach (ContractSO contract in contracts)
            {
                string path = AssetDatabase.GetAssetPath(contract);
                string region = contract.contractMap.ToString();
                Require(path.StartsWith(Folder + "/" + region + "/", StringComparison.Ordinal), "Folder/map mismatch: " + path);
                Require(contract.allowedMaterials == null || contract.allowedMaterials.All(entry => entry != null && entry.material != null && entry.maxPieces >= 0), "Invalid allowance entry: " + path);
                Require(contract.allowedMaterials == null || contract.allowedMaterials.Select(entry => entry.material).Distinct().Count() == contract.allowedMaterials.Count, "Duplicate material allowance: " + path);
                var plan = BuildPlan(contract, path, materials); plans.Add(plan);
                report.AppendLine(region + ": " + contract.name + "; allowed=" + string.Join(", ", plan.allowed.Select(entry => entry.material.GetDisplayName() + " [" + entry.maxPieces + "]")) +
                    "; material changes=" + plan.changed);
            }
            Require(plans.Count(plan => plan.contract.contractMap == ContractSO.ContractMap.CanyonCrossing) == 10 &&
                plans.Count(plan => plan.contract.contractMap == ContractSO.ContractMap.TownRiver) == 4 &&
                plans.Count(plan => plan.contract.contractMap == ContractSO.ContractMap.IndustrialZone) == 5, "The audited regional counts changed.");
            if (command == "apply")
            {
                foreach (Plan plan in plans.Where(plan => plan.changed))
                {
                    Undo.RecordObject(plan.contract, "Apply regional contract material policy");
                    plan.contract.allowedMaterials = plan.allowed;
                    plan.contract.hiddenMaterials = plan.hidden;
                    EditorUtility.SetDirty(plan.contract);
                }
            }
            if (command != "inspect")
                foreach (Plan plan in plans) ValidatePlan(plan, materials);
            if (command == "apply")
            {
                beganSaving = true;
                foreach (Plan plan in plans.Where(plan => plan.changed))
                {
                    AssetDatabase.SaveAssetIfDirty(plan.contract);
                    Require(!EditorUtility.IsDirty(plan.contract), "Unity did not save " + plan.path);
                    report.AppendLine("SAVED: " + plan.path);
                }
            }
            Require(EditorSceneManager.GetSceneManagerSetup().Select(SceneKey).SequenceEqual(sceneSetup.Select(SceneKey)), "The original scene setup changed.");
            foreach (var pair in sceneDirty) Require(pair.Key.isDirty == pair.Value, "Scene dirty state changed: " + pair.Key.path);
            report.AppendLine("PASS: " + (command == "inspect" ? "Read-only regional plan prepared." : "All 19 contracts satisfy regional allowances and hard exclusions; quantity limits and every non-material serialized field are preserved."));
            report.AppendLine("PASS: No scenes opened/saved, source material assets modified, player data/preferences touched, rewards awarded, or contract progression changed.");
            report.AppendLine("RESULT: PASS; command=" + command + "; UTC=" + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception error)
        {
            if (command == "apply" && !beganSaving)
                foreach (Plan plan in plans.Where(plan => plan.changed && EditorUtility.IsDirty(plan.contract)))
                { EditorJsonUtility.FromJsonOverwrite(plan.originalJson, plan.contract); EditorUtility.ClearDirty(plan.contract); }
            report.AppendLine("FAIL: " + error); Debug.LogException(error);
        }
        finally { File.WriteAllText(Report, report.ToString()); running = false; }
    }

    private static Plan BuildPlan(ContractSO contract, string path, Dictionary<string, BridgeMaterialSO> materials)
    {
        var allowed = (contract.allowedMaterials ?? new List<MaterialAllowance>()).Select(entry => new MaterialAllowance { material = entry.material, maxPieces = entry.maxPieces }).ToList();
        var hidden = new List<BridgeMaterialSO>(contract.hiddenMaterials ?? new List<BridgeMaterialSO>());
        BridgeMaterialSO[] wood = Names.Take(4).Select(name => materials[name]).ToArray();
        BridgeMaterialSO[] concrete = Names.Skip(4).Take(3).Select(name => materials[name]).ToArray();
        BridgeMaterialSO[] steel = Names.Skip(7).Select(name => materials[name]).ToArray();
        BridgeMaterialSO[] forbidden;
        switch (contract.contractMap)
        {
            case ContractSO.ContractMap.CanyonCrossing:
                forbidden = concrete.Concat(steel).ToArray();
                allowed.RemoveAll(entry => forbidden.Contains(entry.material));
                if (allowed.Count == 0) allowed.AddRange(wood.Select(Allowance));
                break;
            case ContractSO.ContractMap.TownRiver:
                forbidden = steel;
                if (allowed.Count == 0) allowed.AddRange(wood.Concat(concrete).Select(Allowance));
                else foreach (MaterialAllowance entry in allowed)
                {
                    if (entry.material == materials["SteelBeam"]) entry.material = materials["ConcreteBeam"];
                    else if (entry.material == materials["SteelPier"]) entry.material = materials["Concrete Pier"];
                    else if (entry.material == materials["SteelRope"]) entry.material = materials["RopeMaterial"];
                }
                if (!allowed.Any(entry => entry.material == materials["Concrete Pier"])) allowed.Add(Allowance(materials["Concrete Pier"]));
                hidden.RemoveAll(material => material == materials["Concrete Pier"]);
                break;
            case ContractSO.ContractMap.IndustrialZone:
                forbidden = wood.Concat(new[] { materials["ConcreteBeam"], materials["Concrete Pier"] }).ToArray();
                BridgeMaterialSO[] required = steel.Concat(new[] { materials["Concrete Road"] }).ToArray();
                allowed.RemoveAll(entry => !required.Contains(entry.material));
                foreach (BridgeMaterialSO material in required)
                    if (!allowed.Any(entry => entry.material == material)) allowed.Add(Allowance(material));
                break;
            default: throw new InvalidOperationException("Unknown contract region: " + path);
        }
        Require(allowed.Select(entry => entry.material).Distinct().Count() == allowed.Count, "Mapped allowances would duplicate a material in " + path + ". Review quantity limits manually.");
        foreach (BridgeMaterialSO material in forbidden) if (!hidden.Contains(material)) hidden.Add(material);
        return new Plan { contract = contract, path = path, originalJson = EditorJsonUtility.ToJson(contract), protectedJson = ProtectedJson(contract), allowed = allowed, hidden = hidden,
            changed = !SameAllowances(contract.allowedMaterials, allowed) || !(contract.hiddenMaterials ?? new List<BridgeMaterialSO>()).SequenceEqual(hidden) };
    }

    private static void ValidatePlan(Plan plan, Dictionary<string, BridgeMaterialSO> materials)
    {
        ContractSO contract = plan.contract;
        Require(SameAllowances(contract.allowedMaterials, plan.allowed) && (contract.hiddenMaterials ?? new List<BridgeMaterialSO>()).SequenceEqual(plan.hidden), "Regional material lists do not match the policy: " + plan.path);
        Require(ProtectedJson(contract) == plan.protectedJson, "A non-material serialized field changed: " + plan.path);
        foreach (var pair in materials)
        {
            BridgeMaterialSO material = pair.Value;
            bool forbidden = contract.contractMap == ContractSO.ContractMap.CanyonCrossing ? pair.Key.StartsWith("Concrete", StringComparison.Ordinal) || pair.Key.StartsWith("Steel", StringComparison.Ordinal) :
                contract.contractMap == ContractSO.ContractMap.TownRiver ? pair.Key.StartsWith("Steel", StringComparison.Ordinal) :
                pair.Key != "Concrete Road" && !pair.Key.StartsWith("Steel", StringComparison.Ordinal);
            if (forbidden)
                Require(contract.IsMaterialHidden(material) && !material.CanUseInContract(contract) && !contract.allowedMaterials.Any(entry => entry.material == material),
                    "A forbidden material can bypass regional exclusion: " + plan.path + "/" + pair.Key + ". Refresh the hard-hidden material runtime guard.");
        }
        if (contract.contractMap == ContractSO.ContractMap.TownRiver)
            Require(contract.allowedMaterials.Any(entry => entry.material == materials["Concrete Pier"]) && !contract.IsMaterialHidden(materials["Concrete Pier"]) && materials["Concrete Pier"].CanUseInContract(contract), "Town River Concrete Pier is not enabled: " + plan.path);
        if (contract.contractMap == ContractSO.ContractMap.IndustrialZone)
            foreach (string name in new[] { "SteelBeam", "SteelPier", "SteelRope", "Concrete Road" })
                Require(contract.allowedMaterials.Any(entry => entry.material == materials[name]) && !contract.IsMaterialHidden(materials[name]) && materials[name].CanUseInContract(contract), "Industrial material is not enabled: " + plan.path + "/" + name);
        string saved = File.ReadAllText(plan.path);
        Require(!string.IsNullOrWhiteSpace(saved), "Saved contract asset is missing: " + plan.path);
    }
    private static MaterialAllowance Allowance(BridgeMaterialSO material) => new MaterialAllowance { material = material, maxPieces = 0 };
    private static bool SameAllowances(List<MaterialAllowance> a, List<MaterialAllowance> b) => a != null && a.Count == b.Count && a.Select((entry, index) => entry != null && entry.material == b[index].material && entry.maxPieces == b[index].maxPieces).All(value => value);
    private static string ProtectedJson(ContractSO contract) => Regex.Replace(Regex.Replace(EditorJsonUtility.ToJson(contract), "\"allowedMaterials\"\\s*:\\s*\\[[^\\]]*\\]", "\"allowedMaterials\":[]"), "\"hiddenMaterials\"\\s*:\\s*\\[[^\\]]*\\]", "\"hiddenMaterials\":[]");
    private static string SceneKey(SceneSetup setup) => setup.path + ":" + setup.isLoaded + ":" + setup.isActive;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
