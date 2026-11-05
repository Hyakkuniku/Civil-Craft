#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>Authors Clark's visibility gate inside the caller's scene-only Undo transaction.</summary>
internal static class ClarkHandoffAuthoring
{
    private const string ClarkId = "Clark";
    private const string RonanId = "RonanNpc";
    private const string RonanContractId = "RonanMainContract";
    private const string RonanReviewPhase = "RonanReview";
    private const string RonanFinalPhase = "Resolved";

    internal static void Preflight(Scene scene, NPCProgressionManager clark) => FindCheckedGate(scene, clark, out _);

    internal static void Apply(Scene scene, NPCProgressionManager clark)
    {
        NPCSpawnerCondition gate = FindCheckedGate(scene, clark, out ContractSO contract);
        if (gate == null)
        {
            gate = Undo.AddComponent<NPCSpawnerCondition>(clark.gameObject);
            Undo.RecordObject(gate, "Gate Clark after Ronan's review");
            gate.npcRoot = clark.gameObject;
            gate.useMultipleConditions = true;
            gate.matchMode = NPCSpawnerCondition.MatchMode.All;
            gate.conditions = new List<NPCSpawnerCondition.Condition>
            {
                new NPCSpawnerCondition.Condition
                {
                    kind = NPCSpawnerCondition.ConditionKind.ContractCompleted,
                    contract = contract
                },
                new NPCSpawnerCondition.Condition
                {
                    kind = NPCSpawnerCondition.ConditionKind.NPCAtPhase,
                    id = RonanId,
                    phaseId = RonanFinalPhase
                }
            };
            PrefabUtility.RecordPrefabInstancePropertyModifications(gate);
            EditorUtility.SetDirty(gate);
        }

        if (clark.gameObject.activeSelf)
        {
            Undo.RecordObject(clark.gameObject, "Start Clark hidden until Ronan's review is complete");
            clark.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(clark.gameObject);
        }
    }

    internal static void Validate(Scene scene, NPCProgressionManager clark)
    {
        NPCSpawnerCondition gate = FindCheckedGate(scene, clark, out _);
        if (gate == null || clark.gameObject.activeSelf)
            throw new InvalidOperationException("Clark must start hidden and wait for Ronan's completed contract and resolved review phase.");
    }

    private static ContractSO ResolvePrerequisite(Scene scene, NPCProgressionManager clark)
    {
        if (!scene.IsValid() || !scene.isLoaded || clark == null || clark.gameObject.scene != scene || clark.ProgressionSaveId != ClarkId)
            throw new InvalidOperationException("Find the existing Clark NPC with progression ID 'Clark' in the loaded CanyonCrossing scene.");

        NPCProgressionManager ronan = All<NPCProgressionManager>(scene).SingleOrDefault(npc => npc.ProgressionSaveId == RonanId);
        if (ronan == null || ronan.AuthoredPhases == null)
            throw new InvalidOperationException("Ronan's existing CanyonCrossing story was not found.");

        var phases = ronan.AuthoredPhases;
        ContractSO contract = phases.Select(phase => phase?.contract).SingleOrDefault(asset => asset != null && asset.ContractID == RonanContractId);
        NPCProgressionPhase review = phases.SingleOrDefault(phase => phase?.phaseId == RonanReviewPhase);
        NPCProgressionPhase resolved = phases.SingleOrDefault(phase => phase?.phaseId == RonanFinalPhase);
        if (contract == null || review == null || resolved == null ||
            review.optionalDialogueSequence == null ||
            !review.optionalDialogueSequence.Any(step => step?.stepType == NPCOptionalDialogueStep.StepType.Quiz && step.quiz != null) ||
            review.onDialogueFinishedPhaseMove == null || review.onDialogueFinishedPhaseMove.npc != ronan ||
            review.onDialogueFinishedPhaseMove.phaseId != RonanFinalPhase)
            throw new InvalidOperationException("Ronan's contract and Shear review must lead to his existing Resolved phase before Clark can be gated.");
        return contract;
    }

    private static NPCSpawnerCondition FindCheckedGate(Scene scene, NPCProgressionManager clark, out ContractSO contract)
    {
        contract = ResolvePrerequisite(scene, clark);
        NPCSpawnerCondition existingGate = All<NPCSpawnerCondition>(scene).SingleOrDefault(gate =>
        {
            var target = gate.npcRoot != null ? gate.npcRoot.transform : gate.transform;
            return target == clark.transform || target.IsChildOf(clark.transform) || clark.transform.IsChildOf(target);
        });
        if (existingGate != null && !HasExpectedRules(existingGate, clark, contract))
            throw new InvalidOperationException("Clark already has different visibility rules. Review those rules before applying Freight Passage.");
        return existingGate;
    }

    private static bool HasExpectedRules(NPCSpawnerCondition gate, NPCProgressionManager clark, ContractSO contract) =>
        gate.enabled && gate.gameObject == clark.gameObject && gate.npcRoot == clark.gameObject &&
        gate.useMultipleConditions && gate.matchMode == NPCSpawnerCondition.MatchMode.All && gate.updateAutomatically &&
        !gate.hideWhenMatched && !gate.waitForManualSpawn && !gate.persistManualOverride && !gate.useDespawnConditions &&
        gate.conditions != null && gate.conditions.Count == 2 && gate.conditions.All(rule => rule != null && rule.mustBeTrue) &&
        gate.conditions.Any(rule => rule.kind == NPCSpawnerCondition.ConditionKind.ContractCompleted && rule.contract == contract) &&
        gate.conditions.Any(rule => rule.kind == NPCSpawnerCondition.ConditionKind.NPCAtPhase && rule.id == RonanId && rule.phaseId == RonanFinalPhase);

    private static IEnumerable<T> All<T>(Scene scene) where T : UnityEngine.Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
}
#endif
