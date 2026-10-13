#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.SceneManagement;

/// <summary>Replaces the movement writer only; existing joystick art and HUD remain authored.</summary>
[InitializeOnLoad]
public static class MobileJoystickAuthoring
{
    private const string RuntimeScript = "Assets/Script/Player/MobileMovementJoystick.cs";
    private const string LegacyGuid = "e9d677f1681015749b15c436eec6d880";
    private const string MovementPath = "<Gamepad>/leftStick";
    private const string RequestPath = "Temp/mobile-joystick-authoring.request";
    private const string ReportPath = "Temp/mobile-joystick-authoring-report.txt";
    private const string ManagersPrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    public static readonly string[] RequiredScenes = {
        "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };

    private static bool running;
    private static double nextRequestCheck;

    static MobileJoystickAuthoring()
    {
        EditorApplication.delayCall += TryAuthor;
        EditorApplication.update += CheckRequest;
        EditorSceneManager.sceneOpened += SceneOpened;
    }

    private static bool Busy => running || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
        BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void SceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (!running && RequiredScenes.Contains(scene.path))
            EditorApplication.delayCall += TryAuthor;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextRequestCheck) return;
        nextRequestCheck = EditorApplication.timeSinceStartup + 1d;
        if (!Busy && File.Exists(RequestPath)) AuthorAll();
    }

    private static void TryAuthor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || running || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAuthor;
            return;
        }
        AuthorAll();
    }

    [MenuItem("Tools/Civil Craft/Author Fixed and Floating Joysticks")]
    public static void AuthorAll()
    {
        if (Busy) return;
        string runtimeGuid = AssetDatabase.AssetPathToGUID(RuntimeScript);
        if (string.IsNullOrEmpty(runtimeGuid))
        {
            Debug.LogWarning("[Mobile Joystick] Wait for the movement joystick script to finish importing.");
            return;
        }

        running = true;
        Scene previousActive = SceneManager.GetActiveScene();
        var report = new StringBuilder("Fixed/floating movement joystick scene authoring\n");
        bool succeeded = true;
        try
        {
            succeeded &= AuthorSourcePrefab(runtimeGuid, report);
            foreach (string path in RequiredScenes)
            {
                if (!File.Exists(path))
                {
                    succeeded = false;
                    report.AppendLine("FAIL " + path + ": saved scene is missing.");
                    continue;
                }

                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened && CollectSavedSceneErrors(File.ReadAllText(path), runtimeGuid).Count == 0)
                {
                    report.AppendLine("PASS " + path + ": already authored; not opened or rewritten.");
                    continue;
                }

                try
                {
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    bool wasDirty = scene.isDirty;
                    bool changed = EnsureScene(scene);
                    var loadedErrors = CollectLoadedSceneErrors(scene);
                    if (loadedErrors.Count > 0)
                    {
                        succeeded = false;
                        foreach (string error in loadedErrors) report.AppendLine("FAIL " + path + ": " + error);
                    }

                    if (changed)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (opened || !wasDirty)
                        {
                            if (!EditorSceneManager.SaveScene(scene))
                            {
                                succeeded = false;
                                report.AppendLine("FAIL " + path + ": Unity could not save the scene.");
                            }
                            else report.AppendLine("SAVED " + path + ": movement component replaced; graphics/buttons preserved.");
                        }
                        else
                        {
                            report.AppendLine("UNSAVED " + path + ": existing dirty scene retained; save manually with existing edits.");
                            Debug.Log("[Mobile Joystick] Updated " + scene.name + " without saving its existing unsaved edits. Save the scene to keep the joystick setting.");
                        }
                    }
                    else report.AppendLine("UNCHANGED " + path + ": loaded joystick already authored.");

                    var savedErrors = CollectSavedSceneErrors(File.ReadAllText(path), runtimeGuid);
                    if (savedErrors.Count > 0) succeeded = false;
                    foreach (string error in savedErrors)
                        report.AppendLine("SAVED CHECK " + path + ": " + error);
                }
                catch (Exception error)
                {
                    succeeded = false;
                    report.AppendLine("FAIL " + path + ": " + error.Message);
                    Debug.LogError("[Mobile Joystick] " + path + ": " + error.Message);
                }
                finally
                {
                    if (opened && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            running = false;
            report.AppendLine(succeeded ? "PASS: all authored scenes and the source template are persisted and valid."
                : "PENDING/FAIL: one or more saved checks still require attention; existing dirty scenes were not auto-saved.");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, report.ToString());
            // This editor-only request marker contains no player data and is consumed once.
            if (File.Exists(RequestPath)) File.Delete(RequestPath);
        }

        if (succeeded) Debug.Log("[Mobile Joystick] Authored scene checks passed. No gameplay, account, or network requests were executed.");
        else Debug.LogWarning("[Mobile Joystick] Saved scene checks are not finished. Review " + ReportPath +
            "; save an already-dirty scene manually with its existing edits. No gameplay or account requests were executed.");
    }

    private static bool EnsureScene(Scene scene)
    {
        bool changed = false;
        foreach (GameObject root in scene.GetRootGameObjects())
            changed |= EnsureRoot(root, false);
        return changed;
    }

    private static bool EnsureRoot(GameObject root, bool allowTemplateWrapper)
    {
        bool changed = false;
        foreach (OnScreenStick legacy in root.GetComponentsInChildren<OnScreenStick>(true))
        {
            if (!string.Equals(legacy.controlPath, MovementPath, StringComparison.Ordinal)) continue;
            RectTransform baseRect = legacy.transform.parent as RectTransform;
            if (allowTemplateWrapper && baseRect != null && baseRect.GetComponent<Canvas>() != null &&
                legacy.name == "joystick")
                baseRect = WrapTemplateHandle(legacy.transform as RectTransform);
            if (baseRect == null || baseRect.name != "Joystick_BG")
                throw new InvalidOperationException("Legacy movement joystick has no authored Joystick_BG parent.");

            MobileMovementJoystick custom = legacy.GetComponent<MobileMovementJoystick>();
            if (custom == null) custom = Undo.AddComponent<MobileMovementJoystick>(legacy.gameObject);
            changed |= Configure(custom, baseRect, legacy.movementRange, legacy.controlPath, legacy.enabled);
            Undo.DestroyObjectImmediate(legacy);
            changed = true;
        }

        foreach (MobileMovementJoystick custom in root.GetComponentsInChildren<MobileMovementJoystick>(true))
        {
            if (!string.Equals(custom.controlPath, MovementPath, StringComparison.Ordinal)) continue;
            RectTransform baseRect = custom.transform.parent as RectTransform;
            if (baseRect == null || baseRect.name != "Joystick_BG") continue;
            changed |= Configure(custom, baseRect, custom.movementRange, MovementPath, custom.enabled);
        }
        return changed;
    }

    private static bool AuthorSourcePrefab(string runtimeGuid, StringBuilder report)
    {
        if (!File.Exists(ManagersPrefabPath))
        {
            report.AppendLine("FAIL " + ManagersPrefabPath + ": source template is missing.");
            return false;
        }
        if (CollectSavedSceneErrors(File.ReadAllText(ManagersPrefabPath), runtimeGuid).Count == 0)
        {
            report.AppendLine("PASS " + ManagersPrefabPath + ": template already authored; not rewritten.");
            return true;
        }
        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(ManagersPrefabPath);
            bool changed = EnsureRoot(prefabRoot, true);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, ManagersPrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Unity could not save the source template.");
                report.AppendLine("SAVED " + ManagersPrefabPath + ": same handle art/layout; RectTransform-only background wrapper.");
            }
            var errors = CollectSavedSceneErrors(File.ReadAllText(ManagersPrefabPath), runtimeGuid);
            foreach (string error in errors) report.AppendLine("FAIL " + ManagersPrefabPath + ": " + error);
            return errors.Count == 0;
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL " + ManagersPrefabPath + ": " + error.Message);
            Debug.LogError("[Mobile Joystick] Source template: " + error.Message);
            return false;
        }
        finally
        {
            if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static RectTransform WrapTemplateHandle(RectTransform handle)
    {
        if (handle == null || handle.parent == null)
            throw new InvalidOperationException("Source joystick has no RectTransform parent.");
        Transform parent = handle.parent;
        int sibling = handle.GetSiblingIndex();
        Vector2 sizeBefore = handle.rect.size;
        Matrix4x4 relativeBefore = Matrix4x4.TRS(handle.localPosition, handle.localRotation, handle.localScale);
        Vector3 parentPosition = parent.localPosition, parentScale = parent.localScale;
        Quaternion parentRotation = parent.localRotation;

        GameObject wrapperObject = new GameObject("Joystick_BG", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(wrapperObject, "Wrap source movement joystick");
        wrapperObject.layer = handle.gameObject.layer;
        RectTransform wrapper = (RectTransform)wrapperObject.transform;
        wrapper.SetParent(parent, false);
        wrapper.SetSiblingIndex(sibling);
        wrapper.anchorMin = handle.anchorMin;
        wrapper.anchorMax = handle.anchorMax;
        wrapper.pivot = handle.pivot;
        wrapper.sizeDelta = handle.sizeDelta;
        wrapper.anchoredPosition3D = handle.anchoredPosition3D;
        wrapper.localRotation = handle.localRotation;
        wrapper.localScale = handle.localScale;

        Undo.RecordObject(handle, "Keep source joystick art placement");
        Undo.SetTransformParent(handle, wrapper, "Wrap source movement joystick");
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = Vector2.one;
        handle.anchoredPosition3D = Vector3.zero;
        handle.sizeDelta = Vector2.zero;
        handle.localRotation = Quaternion.identity;
        handle.localScale = Vector3.one;

        Matrix4x4 relativeAfter = Matrix4x4.TRS(wrapper.localPosition, wrapper.localRotation, wrapper.localScale) *
            Matrix4x4.TRS(handle.localPosition, handle.localRotation, handle.localScale);
        if ((handle.rect.size - sizeBefore).sqrMagnitude > 0.01f || parent.localPosition != parentPosition ||
            parent.localRotation != parentRotation || parent.localScale != parentScale ||
            wrapper.GetSiblingIndex() != sibling)
            throw new InvalidOperationException("Source wrapper would change joystick art or its Canvas layout.");
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (Mathf.Abs(relativeAfter[row, column] - relativeBefore[row, column]) > 0.01f)
                    throw new InvalidOperationException("Source wrapper would change the joystick's original pixel placement.");
        return wrapper;
    }

    private static bool Configure(MobileMovementJoystick custom, RectTransform baseRect,
        float range, string path, bool enabled)
    {
        SerializedObject serialized = new SerializedObject(custom);
        SerializedProperty baseField = serialized.FindProperty("joystickBase");
        SerializedProperty rangeField = serialized.FindProperty("m_MovementRange");
        SerializedProperty pathField = serialized.FindProperty("m_ControlPath");
        if (baseField == null || rangeField == null || pathField == null)
            throw new InvalidOperationException("Runtime joystick fields are not imported yet.");
        bool changed = baseField.objectReferenceValue != baseRect ||
            !Mathf.Approximately(rangeField.floatValue, range) ||
            !string.Equals(pathField.stringValue, path, StringComparison.Ordinal) || custom.enabled != enabled;
        if (!changed) return false;
        Undo.RecordObject(custom, "Configure movement joystick");
        baseField.objectReferenceValue = baseRect;
        rangeField.floatValue = range;
        pathField.stringValue = path;
        serialized.ApplyModifiedProperties();
        custom.enabled = enabled;
        EditorUtility.SetDirty(custom);
        return true;
    }

    private static List<string> CollectLoadedSceneErrors(Scene scene)
    {
        var errors = new List<string>();
        GameObject[] roots = scene.GetRootGameObjects();
        OnScreenStick[] legacy = roots.SelectMany(root => root.GetComponentsInChildren<OnScreenStick>(true))
            .Where(stick => stick.controlPath == MovementPath).ToArray();
        MobileMovementJoystick[] custom = roots.SelectMany(root => root.GetComponentsInChildren<MobileMovementJoystick>(true))
            .Where(stick => stick.controlPath == MovementPath).ToArray();
        if (legacy.Length != 0) errors.Add("A legacy movement writer remains.");
        if (custom.Length != 1) errors.Add("Expected exactly one custom left-stick writer; found " + custom.Length + ".");
        foreach (MobileMovementJoystick stick in custom)
        {
            var serialized = new SerializedObject(stick);
            var baseRect = serialized.FindProperty("joystickBase")?.objectReferenceValue as RectTransform;
            if (baseRect == null || baseRect != stick.transform.parent || baseRect.name != "Joystick_BG")
                errors.Add("Movement writer is not linked to its authored Joystick_BG.");
            if (!Mathf.Approximately(stick.movementRange, 100f)) errors.Add("Authored movement radius changed from 100.");
            if (!stick.enabled) errors.Add("Custom movement writer is disabled.");
        }
        return errors;
    }

    /// <summary>Read-only validation of persisted YAML; does not open or modify scenes.</summary>
    public static List<string> CollectSavedSceneErrors(string yaml, string runtimeGuid)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(yaml) || string.IsNullOrEmpty(runtimeGuid))
        {
            errors.Add("Saved scene or movement script GUID is unavailable.");
            return errors;
        }
        string[] blocks = Regex.Split(yaml, @"(?m)(?=^--- !u!)");
        var byId = new Dictionary<string, string>();
        foreach (string block in blocks)
        {
            Match id = Regex.Match(block, @"^--- !u!\d+ &(-?\d+)");
            if (id.Success) byId[id.Groups[1].Value] = block;
        }
        string[] writers = blocks.Where(block => Regex.IsMatch(block,
            @"(?m)^  m_ControlPath: <Gamepad>/leftStick\s*$")).ToArray();
        if (writers.Length != 1) errors.Add("Expected exactly one serialized left-stick writer; found " + writers.Length + ".");
        foreach (string writer in writers)
        {
            if (writer.Contains("guid: " + LegacyGuid + ",")) errors.Add("Legacy OnScreenStick movement writer remains.");
            if (!writer.Contains("guid: " + runtimeGuid + ","))
            {
                errors.Add("Left-stick writer is not the fixed/floating movement component.");
                continue;
            }
            if (!Regex.IsMatch(writer, @"(?m)^  m_Enabled: 1\s*$")) errors.Add("Custom movement writer is disabled.");
            string range = Regex.Match(writer, @"(?m)^  m_MovementRange: ([^\r\n]+)").Groups[1].Value;
            if (!float.TryParse(range, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) ||
                !Mathf.Approximately(radius, 100f)) errors.Add("Authored movement radius changed from 100.");

            string baseId = Regex.Match(writer, @"(?m)^  joystickBase: \{fileID: (-?\d+)\}").Groups[1].Value;
            if (baseId.Length == 0 || baseId == "0" || !byId.TryGetValue(baseId, out string baseBlock) ||
                !baseBlock.StartsWith("--- !u!224 ", StringComparison.Ordinal))
            {
                errors.Add("Custom movement writer has no persisted background RectTransform reference.");
                continue;
            }
            string baseGameId = ObjectId(baseBlock);
            if (!byId.TryGetValue(baseGameId, out string baseGame) ||
                !Regex.IsMatch(baseGame, @"(?m)^  m_Name: Joystick_BG\s*$"))
                errors.Add("Background reference is not the authored Joystick_BG.");
            string handleGameId = ObjectId(writer);
            string handleRect = blocks.FirstOrDefault(block => block.StartsWith("--- !u!224 ", StringComparison.Ordinal) &&
                ObjectId(block) == handleGameId);
            if (handleRect == null || !Regex.IsMatch(handleRect,
                @"(?m)^  m_Father: \{fileID: " + Regex.Escape(baseId) + @"\}\s*$"))
                errors.Add("Joystick handle is not a child of its referenced background.");
        }
        return errors;
    }

    private static string ObjectId(string block) =>
        Regex.Match(block, @"(?m)^  m_GameObject: \{fileID: (-?\d+)\}").Groups[1].Value;

    [MenuItem("Tools/Civil Craft/Validate Saved Fixed and Floating Joysticks")]
    public static void ValidateSavedScenes()
    {
        string runtimeGuid = AssetDatabase.AssetPathToGUID(RuntimeScript);
        var errors = new List<string>();
        foreach (string path in RequiredScenes)
        {
            if (!File.Exists(path)) errors.Add(path + ": scene is missing.");
            else errors.AddRange(CollectSavedSceneErrors(File.ReadAllText(path), runtimeGuid)
                .Select(error => path + ": " + error));
        }
        if (!File.Exists(ManagersPrefabPath)) errors.Add(ManagersPrefabPath + ": source template is missing.");
        else errors.AddRange(CollectSavedSceneErrors(File.ReadAllText(ManagersPrefabPath), runtimeGuid)
            .Select(error => ManagersPrefabPath + ": " + error));
        if (errors.Count > 0)
            throw new BuildFailedException("Movement joystick scene validation failed:\n" + string.Join("\n", errors));
        Debug.Log("[Mobile Joystick] All three gameplay scenes have one fixed/floating left-stick writer and retain their authored background/radius. Main Menu needs no movement joystick.");
    }
}

internal sealed class MobileJoystickBuildValidation : IPreprocessBuildWithReport
{
    public int callbackOrder => 15;
    public void OnPreprocessBuild(BuildReport report) => MobileJoystickAuthoring.ValidateSavedScenes();
}
#endif
