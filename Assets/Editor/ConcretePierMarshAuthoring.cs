#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Changes only Marsh's existing pier introduction and that contract's pier allowance.</summary>
[InitializeOnLoad]
public static class ConcretePierMarshAuthoring
{
    public const string RequestPath = "Temp/concrete-pier-marsh.request";
    public const string ReportPath = "Logs/concrete-pier-marsh.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string ContractPath = "Assets/BridgeBuilder/Data/Contract/TownRiver/Marsh Contract.asset";
    private const string ContractGuid = "7bd1a236dfb47f54e96257d2d3d48be1";
    private const string ConcretePath = "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset";
    private const string ConcreteGuid = "7fbcf5a7e459d434f970d2c976504d5c";
    private const string WoodPath = "Assets/BridgeBuilder/Data/Resources/Pier.asset";
    private const string WoodGuid = "b98aa9ba752ac28449feebc76f256bf2";
    private const string Description = "A concrete vertical support placed beneath a bridge to help carry loads and transfer them to the ground or foundation.";
    private static double nextPoll;
    private static bool running;

    private sealed class Scope
    {
        public Scene scene;
        public NPCProgressionManager marsh;
        public ContractSO contract;
        public BridgeMaterialSO wood, concrete;
        public string materialProperty, sentenceProperty, allowanceProperty;
        public int allowanceIndex;
        public bool needsMaterial, needsSentence, needsAllowance, needsDescription;
    }

    static ConcretePierMarshAuthoring() { EditorApplication.update += Poll; }
    private static bool Ready => !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1d;
        if (!Ready || !File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        Run(command);
    }

    [MenuItem("Tools/Civil Craft/Story/Marsh/Concrete Pier/1. Inspect (Read Only)")]
    public static void Inspect() => Run("inspect");
    [MenuItem("Tools/Civil Craft/Story/Marsh/Concrete Pier/2. Swap Existing Pier Introduction (Do Not Save Scene)")]
    public static void Apply() => Run("apply");
    [MenuItem("Tools/Civil Craft/Story/Marsh/Concrete Pier/3. Validate (Read Only)")]
    public static void Validate() => Run("validate");

    private static void Run(string command)
    {
        if (running) return;
        if (!Ready) { Write("NOT_RUN\nStop Play Mode, finish imports/compilation and close Prefab Mode. No changes made.\n"); return; }
        running = true;
        var report = new StringBuilder("Narrow Marsh Concrete Pier update: " + command + "\n");
        try
        {
            Require(command == "inspect" || command == "apply" || command == "validate", "Unknown Marsh Concrete Pier request.");
            Scope scope = InspectScope(report);
            if (command == "apply") ApplyScope(scope, report);
            else if (command == "validate") RequireUpdated(scope);
            report.AppendLine(command == "inspect" ? "INSPECTED: no scene or asset writes." : "PASS: requested narrow scope checked.");
            report.AppendLine("NEEDS_SAVE: " + (scope.scene.isDirty ? "CanyonCrossing; save manually with your existing edits." : "none"));
            report.AppendLine("Existing player saves were not migrated. Wood Pier is not hidden and retains its legacy purchase behavior.");
            report.AppendLine("No full story migration, phase/step ID changes, numeric balance edits, player data calls, or scene auto-save.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); Debug.LogException(error); }
        finally { Write(report.ToString()); running = false; }
    }

    private static Scope InspectScope(StringBuilder report)
    {
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode. This tool never opens or switches user scenes.");
        NPCProgressionManager[] marshes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NPCProgressionManager>(true))
            .Where(npc => npc.ProgressionSaveId == "MarshNPC").ToArray();
        Require(marshes.Length == 1, "Expected exactly one existing MarshNPC in loaded CanyonCrossing.");
        var scope = new Scope { scene = scene, marsh = marshes[0], contract = Load<ContractSO>(ContractPath, ContractGuid),
            wood = Load<BridgeMaterialSO>(WoodPath, WoodGuid), concrete = Load<BridgeMaterialSO>(ConcretePath, ConcreteGuid) };
        Require(scope.contract.ContractID == "MarshID" && scope.contract.clientName == "Marsh", "Marsh's existing contract identity/owner changed.");
        Require(scope.contract.hiddenMaterials == null || !scope.contract.hiddenMaterials.Contains(scope.concrete),
            "Marsh explicitly hides Concrete Pier. That custom hidden-material list was not changed.");
        Require(scope.concrete.isPier && !scope.concrete.isRoad && !scope.concrete.isRope && !scope.concrete.isDualBeam &&
            scope.concrete.segmentPrefab != null && scope.concrete.pierCapPrefab != null,
            "Configure and verify the existing Concrete Pier models first.");
        var concreteData = new SerializedObject(scope.concrete);
        SerializedProperty gate = concreteData.FindProperty("requiresExplicitContractAllowance");
        Require(gate != null && gate.boolValue, "Concrete Pier must retain explicit contract allowance gating.");
        var npcData = new SerializedObject(scope.marsh);
        SerializedProperty phases = npcData.FindProperty("phases");
        Require(phases != null && phases.isArray, "Marsh's existing phase list is unavailable.");
        var phaseIndices = new List<int>();
        for (int index = 0; index < phases.arraySize; index++)
            if (phases.GetArrayElementAtIndex(index).FindPropertyRelative("phaseId").stringValue == "Material Introduction") phaseIndices.Add(index);
        Require(phaseIndices.Count == 1, "Expected one exact Material Introduction phase; no substitute phase was selected.");
        SerializedProperty sequence = phases.GetArrayElementAtIndex(phaseIndices[0]).FindPropertyRelative("optionalDialogueSequence");
        Require(sequence != null && sequence.isArray && sequence.arraySize >= 7, "Marsh's ordered introduction differs from the existing sequence.");
        SerializedProperty dialogueStep = sequence.GetArrayElementAtIndex(5), materialStep = sequence.GetArrayElementAtIndex(6);
        Require(dialogueStep.FindPropertyRelative("stepType").enumValueIndex == (int)NPCOptionalDialogueStep.StepType.Dialogue &&
            materialStep.FindPropertyRelative("stepType").enumValueIndex == (int)NPCOptionalDialogueStep.StepType.MaterialUnlock,
            "Existing introduction indices5/6 are not the expected dialogue/material steps.");
        SerializedProperty material = materialStep.FindPropertyRelative("material");
        Require(material.objectReferenceValue == scope.wood || material.objectReferenceValue == scope.concrete,
            "Marsh's exact material step references an unexpected material; it was not overwritten.");
        scope.materialProperty = material.propertyPath;
        scope.needsMaterial = material.objectReferenceValue == scope.wood;
        SerializedProperty sentences = dialogueStep.FindPropertyRelative("dialogue").FindPropertyRelative("sentences");
        Require(sentences != null && sentences.isArray && sentences.arraySize > 0,
            "The existing pier introduction has no first sentence.");
        SerializedProperty sentence = sentences.GetArrayElementAtIndex(0);
        int oldCount = Count(sentence.stringValue, Em("Pier")), newCount = Count(sentence.stringValue, Em("Concrete Pier"));
        Require((oldCount == 1 && newCount == 0) || (oldCount == 0 && newCount == 1),
            "The existing first pier sentence must contain exactly one expected emphasized Pier/Concrete Pier phrase.");
        scope.sentenceProperty = sentence.propertyPath;
        scope.needsSentence = oldCount == 1;
        var contractData = new SerializedObject(scope.contract);
        SerializedProperty allowances = contractData.FindProperty("allowedMaterials");
        Require(allowances != null && allowances.isArray, "Marsh's existing material allowances are missing.");
        var relevant = new List<int>();
        for (int index = 0; index < allowances.arraySize; index++)
        {
            Object allowed = allowances.GetArrayElementAtIndex(index).FindPropertyRelative("material").objectReferenceValue;
            if (allowed == scope.wood || allowed == scope.concrete) relevant.Add(index);
        }
        Require(relevant.Count == 1, "Expected exactly one Wood/Concrete Pier allowance. Missing or ambiguous entries were not replaced.");
        scope.allowanceIndex = relevant[0];
        SerializedProperty allowance = allowances.GetArrayElementAtIndex(scope.allowanceIndex).FindPropertyRelative("material");
        scope.allowanceProperty = allowance.propertyPath;
        scope.needsAllowance = allowance.objectReferenceValue == scope.wood;
        scope.needsDescription = string.IsNullOrWhiteSpace(scope.concrete.introductionDescription);
        report.AppendLine("Protected serialized-field snapshots: Marsh=" + SnapshotFields(scope.marsh, scope.materialProperty, scope.sentenceProperty).Count +
            ", contract=" + SnapshotFields(scope.contract, scope.allowanceProperty).Count +
            ", material=" + SnapshotFields(scope.concrete, scope.needsDescription ? "introductionDescription" : string.Empty).Count + ".");
        report.AppendLine("Existing Marsh phase index=" + phaseIndices[0] + ", dialogue step=5, material step=6; allowance index=" + scope.allowanceIndex +
            ", maxPieces=" + allowances.GetArrayElementAtIndex(scope.allowanceIndex).FindPropertyRelative("maxPieces").intValue + ".");
        report.AppendLine("Pending replacements: sentence=" + scope.needsSentence + ", introduction asset=" + scope.needsMaterial +
            ", allowance=" + scope.needsAllowance + ", optional blank material description=" + scope.needsDescription + ".");
        report.AppendLine("Loaded Canyon dirty before command=" + scene.isDirty + "; existing unsaved scene edits are allowed and preserved.");
        return scope;
    }

    private static void ApplyScope(Scope scope, StringBuilder report)
    {
        if (!scope.needsMaterial && !scope.needsSentence && !scope.needsAllowance && !scope.needsDescription)
        { RequireUpdated(scope); report.AppendLine("ALREADY_APPLIED: read-only reapply; no asset writes or extra Undo entries."); return; }
        Require(!EditorUtility.IsDirty(scope.contract) && !EditorUtility.IsDirty(scope.concrete),
            "Marsh Contract or Concrete Pier has unsaved Inspector edits. Save those exact assets before continuing; no existing dirty asset was auto-saved.");
        Scene[] scenes = LoadedScenes();
        int active = SceneManager.GetActiveScene().handle;
        bool[] dirtyBefore = scenes.Select(scene => scene.isDirty).ToArray();
        Dictionary<Object, string> otherObjects = SnapshotSceneObjects(scenes, scope.marsh);
        Dictionary<string, string> marshFields = SnapshotFields(scope.marsh, scope.materialProperty, scope.sentenceProperty);
        Dictionary<string, string> contractFields = SnapshotFields(scope.contract, scope.allowanceProperty);
        Dictionary<string, string> concreteFields = SnapshotFields(scope.concrete,
            scope.needsDescription ? "introductionDescription" : string.Empty);
        Dictionary<string, string> protectedFiles = SnapshotProtectedFiles();
        string contractGuidBefore = AssetDatabase.AssetPathToGUID(ContractPath), concreteGuidBefore = AssetDatabase.AssetPathToGUID(ConcretePath);
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-ConcretePierMarsh-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { ContractPath, ContractPath + ".meta", ConcretePath, ConcretePath + ".meta" })
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
        File.WriteAllText(Path.Combine(backup, "Marsh-native-before.json"), EditorJsonUtility.ToJson(scope.marsh, true));
        report.AppendLine("Recoverable target asset/meta and native Marsh snapshot backup: " + backup);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Use Concrete Pier in Marsh's existing introduction");
        bool assetsSaved = false;
        try
        {
            if (scope.needsMaterial || scope.needsSentence)
            {
                Undo.RecordObject(scope.marsh, "Swap Marsh's existing pier introduction");
                var data = new SerializedObject(scope.marsh);
                if (scope.needsMaterial) data.FindProperty(scope.materialProperty).objectReferenceValue = scope.concrete;
                if (scope.needsSentence)
                {
                    SerializedProperty sentence = data.FindProperty(scope.sentenceProperty);
                    sentence.stringValue = sentence.stringValue.Replace(Em("Pier"), Em("Concrete Pier"));
                }
                data.ApplyModifiedProperties();
                if (PrefabUtility.IsPartOfPrefabInstance(scope.marsh)) PrefabUtility.RecordPrefabInstancePropertyModifications(scope.marsh);
            }
            if (scope.needsAllowance)
            {
                Undo.RecordObject(scope.contract, "Swap Marsh's existing pier allowance");
                var data = new SerializedObject(scope.contract);
                data.FindProperty(scope.allowanceProperty).objectReferenceValue = scope.concrete;
                data.ApplyModifiedProperties();
            }
            if (scope.needsDescription)
            {
                Undo.RecordObject(scope.concrete, "Describe Concrete Pier without balance changes");
                var data = new SerializedObject(scope.concrete);
                data.FindProperty("introductionDescription").stringValue = Description;
                data.ApplyModifiedProperties();
            }
            RequireUpdated(scope);
            VerifyScope(scope, otherObjects, marshFields, contractFields, concreteFields, protectedFiles, scenes, dirtyBefore, active);
            if (scope.needsAllowance) { AssetDatabase.SaveAssetIfDirty(scope.contract); assetsSaved = true; }
            if (scope.needsDescription) { AssetDatabase.SaveAssetIfDirty(scope.concrete); assetsSaved = true; }
            Require(AssetDatabase.AssetPathToGUID(ContractPath) == contractGuidBefore && AssetDatabase.AssetPathToGUID(ConcretePath) == concreteGuidBefore,
                "An intended target asset GUID changed.");
            VerifyScope(scope, otherObjects, marshFields, contractFields, concreteFields, protectedFiles, scenes, dirtyBefore, active);
            if (scope.needsMaterial || scope.needsSentence) EditorSceneManager.MarkSceneDirty(scope.scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("APPLIED: only the existing pier material pointer, emphasized name and one allowance pointer changed; maxPieces, all numbers and all other fields retained.");
            report.AppendLine("Only the exact changed Marsh Contract/Concrete Pier assets were saved. CanyonCrossing remains open for your manual scene save.");
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            report.AppendLine(assetsSaved ?
                "Stopped after a target asset save. The tool's Undo group was reverted in memory; inspect the recoverable asset backup before saving further changes." :
                "Stopped before asset saving; the tool's narrow Undo group was reverted. Existing user scene edits were not reset or saved.");
            throw;
        }
    }

    private static void VerifyScope(Scope scope, Dictionary<Object, string> objects,
        Dictionary<string, string> marsh, Dictionary<string, string> contract, Dictionary<string, string> concrete,
        Dictionary<string, string> files, Scene[] scenes, bool[] dirty, int active)
    {
        RequireFields(marsh, SnapshotFields(scope.marsh, scope.materialProperty, scope.sentenceProperty), "An unrelated Marsh field/ID/step changed.");
        RequireFields(contract, SnapshotFields(scope.contract, scope.allowanceProperty), "A Marsh contract field other than the one material pointer changed.");
        RequireFields(concrete, SnapshotFields(scope.concrete, scope.needsDescription ? "introductionDescription" : string.Empty),
            "A Concrete Pier field other than its previously blank description changed.");
        Dictionary<Object, string> after = SnapshotSceneObjects(scenes, scope.marsh);
        Require(objects.Count == after.Count && objects.All(entry => entry.Key != null && after.TryGetValue(entry.Key, out string text) && text == entry.Value),
            "An unrelated native scene object/component changed. Other NPCs, UI, cameras and transforms were not authorized targets.");
        foreach (var entry in files) Require(Hash(entry.Key) == entry.Value, "Protected asset/meta/source changed: " + entry.Key);
        Scene[] current = LoadedScenes();
        Require(current.Select(scene => scene.handle).SequenceEqual(scenes.Select(scene => scene.handle)) && SceneManager.GetActiveScene().handle == active,
            "Loaded/active scene setup changed.");
        for (int index = 0; index < scenes.Length; index++)
            if (scenes[index] != scope.scene) Require(scenes[index].isDirty == dirty[index], "An unrelated scene's dirty flag changed.");
    }

    private static void RequireUpdated(Scope scope)
    {
        var npc = new SerializedObject(scope.marsh);
        Require(npc.FindProperty(scope.materialProperty).objectReferenceValue == scope.concrete &&
            Count(npc.FindProperty(scope.sentenceProperty).stringValue, Em("Concrete Pier")) == 1 &&
            Count(npc.FindProperty(scope.sentenceProperty).stringValue, Em("Pier")) == 0,
            "Marsh's exact introduction is not yet using Concrete Pier.");
        Require(new SerializedObject(scope.contract).FindProperty(scope.allowanceProperty).objectReferenceValue == scope.concrete,
            "Marsh's existing allowance entry is not yet using Concrete Pier.");
        Require(scope.concrete.CanUseInContract(scope.contract) && !scope.contract.IsMaterialHidden(scope.concrete),
            "The explicit Marsh allowance must make Concrete Pier usable without changing the hidden-material list.");
    }

    private static Dictionary<Object, string> SnapshotSceneObjects(IEnumerable<Scene> scenes, NPCProgressionManager except)
    {
        var result = new Dictionary<Object, string>();
        foreach (Scene scene in scenes)
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    result[transform.gameObject] = EditorJsonUtility.ToJson(transform.gameObject);
                    foreach (Component component in transform.GetComponents<Component>())
                        if (component != null && component != except) result[component] = EditorJsonUtility.ToJson(component);
                }
        return result;
    }

    private static Dictionary<string, string> SnapshotFields(Object value, params string[] ignored)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        SerializedProperty property = new SerializedObject(value).GetIterator();
        bool enterChildren = true;
        while (property.Next(enterChildren))
        {
            // Strings/object references are atomic values below. Unity's raw
            // iterator can expose a string's internal array/characters; those
            // are not independent story fields and duplicate the captured text.
            enterChildren = property.propertyType == SerializedPropertyType.Generic;
            if (ignored.Any(path => !string.IsNullOrEmpty(path) &&
                (property.propertyPath == path || property.propertyPath.StartsWith(path + ".", StringComparison.Ordinal))))
            { enterChildren = false; continue; }
            string text;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Generic: text = property.type; break;
                case SerializedPropertyType.Integer: text = property.longValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum: text = property.intValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: text = property.boolValue.ToString(); break;
                case SerializedPropertyType.Float: text = property.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.String: text = property.stringValue; break;
                case SerializedPropertyType.ObjectReference: text = property.objectReferenceInstanceIDValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Color: text = property.colorValue.ToString("R"); break;
                case SerializedPropertyType.Vector2: text = property.vector2Value.ToString("R"); break;
                case SerializedPropertyType.Vector3: text = property.vector3Value.ToString("R"); break;
                case SerializedPropertyType.Vector4: text = property.vector4Value.ToString("R"); break;
                case SerializedPropertyType.Quaternion: text = property.quaternionValue.ToString("R"); break;
                default: throw new InvalidOperationException("Unsupported exact field comparison: " + property.propertyPath + " / " + property.propertyType + ". No changes were authorized.");
            }
            result.Add(property.propertyPath, text);
        }
        return result;
    }
    private static void RequireFields(Dictionary<string, string> before, Dictionary<string, string> after, string message)
    {
        string[] changed = before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
            .Where(path => !before.TryGetValue(path, out string oldValue) || !after.TryGetValue(path, out string newValue) || oldValue != newValue).ToArray();
        if (changed.Length == 0) return;
        var detail = new StringBuilder(message + " Protected field differences=" + changed.Length +
            "; before count=" + before.Count + ", after count=" + after.Count + ".\n");
        foreach (string path in changed.Take(20))
            detail.AppendLine(path + ": BEFORE=" + (before.TryGetValue(path, out string oldValue) ? DiagnosticValue(oldValue) : "<missing>") +
                " AFTER=" + (after.TryGetValue(path, out string newValue) ? DiagnosticValue(newValue) : "<missing>"));
        if (changed.Length > 20) detail.AppendLine("Additional differing paths: " + string.Join(", ", changed.Skip(20)));
        throw new InvalidOperationException(detail.ToString());
    }
    private static string DiagnosticValue(string value) => value == null ? "<null>" :
        "\"" + value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"") + "\"";

    private static Dictionary<string, string> SnapshotProtectedFiles()
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:ContractSO", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path != ContractPath) files.Add(path);
            files.Add(path + ".meta");
        }
        string[] sources = { ConcretePath, WoodPath,
            "Assets/BridgeBuilder/Prefabs/materials/map2/SLAB_LOW.Bottom.prefab",
            "Assets/BridgeBuilder/Prefabs/materials/map2/SLAB_LOW.Top.prefab",
            "Assets/BridgeBuilder/Prefabs/materials/map2/Concreate_Pie2_V2.fbx",
            "Assets/BridgeBuilder/Prefabs/materials/map2/ConcretePier_Shaft.prefab",
            "Assets/BridgeBuilder/Prefabs/materials/map2/ConcretePier_Cap.prefab",
            "Assets/Elements/UI/bm_ui/concrete pier2 selected.png",
            "Assets/Elements/UI/bm_ui/concrete pier2 not selected.png",
            "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab" };
        foreach (string path in AssetDatabase.GetDependencies(sources, true).Concat(sources).Distinct(StringComparer.Ordinal))
            if (path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                if (path != ConcretePath && path != ContractPath) files.Add(path);
                files.Add(path + ".meta");
            }
        return files.OrderBy(path => path, StringComparer.Ordinal).ToDictionary(path => path, Hash, StringComparer.Ordinal);
    }
    private static string Hash(string path)
    {
        Require(File.Exists(path), "Missing protected asset/meta: " + path);
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
    }
    private static Scene[] LoadedScenes()
    {
        var scenes = new List<Scene>();
        for (int index = 0; index < SceneManager.sceneCount; index++) scenes.Add(SceneManager.GetSceneAt(index));
        return scenes.ToArray();
    }
    private static T Load<T>(string path, string guid) where T : Object
    {
        Require(AssetDatabase.AssetPathToGUID(path) == guid, "Existing asset GUID differs: " + path);
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, "Missing/unimported existing asset: " + path);
        return asset;
    }
    private static int Count(string text, string value)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
        return count;
    }
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report); }
}
#endif
