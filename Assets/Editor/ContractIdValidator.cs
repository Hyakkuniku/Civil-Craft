#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class ContractIdValidator : IPreprocessBuildWithReport
{
    private const string MenuPath = "Tools/Civil Craft/Validate Contract IDs";
    private static readonly Regex ValidIdPattern =
        new Regex("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    public int callbackOrder => 0;

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        List<string> errors = CollectErrors(out int contractCount);
        if (errors.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Contract IDs Valid",
                $"Validated {contractCount} contract(s). Every contract has a unique stable ID.",
                "OK");
            return;
        }

        string details = string.Join("\n\n", errors);
        Debug.LogError("[Contract IDs] Validation failed.\n" + details);
        EditorUtility.DisplayDialog("Contract ID Validation Failed", details, "OK");
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        List<string> errors = CollectErrors(out _);
        if (errors.Count == 0) return;

        throw new BuildFailedException(
            "Contract ID validation failed:\n\n" + string.Join("\n\n", errors));
    }

    private static List<string> CollectErrors(out int contractCount)
    {
        string[] guids = AssetDatabase.FindAssets("t:ContractSO", new[] { "Assets" });
        contractCount = guids.Length;
        List<string> errors = new List<string>();
        Dictionary<string, string> pathsById =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ContractSO contract = AssetDatabase.LoadAssetAtPath<ContractSO>(path);
            if (contract == null) continue;

            if (string.IsNullOrWhiteSpace(contract.contractID))
            {
                errors.Add("Missing Contract ID:\n- " + path);
                continue;
            }

            string contractId = contract.contractID.Trim();
            if (!ValidIdPattern.IsMatch(contractId))
            {
                errors.Add(
                    $"Invalid Contract ID '{contract.contractID}':\n- {path}\n" +
                    "Use only letters, numbers, underscores, or hyphens, with no spaces.");
                continue;
            }

            if (pathsById.TryGetValue(contractId, out string existingPath))
            {
                errors.Add(
                    $"Duplicate Contract ID '{contractId}':\n" +
                    $"- {existingPath}\n" +
                    $"- {path}");
                continue;
            }

            pathsById.Add(contractId, path);
        }

        return errors;
    }
}
#endif
