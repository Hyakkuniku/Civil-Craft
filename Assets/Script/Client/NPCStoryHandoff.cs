using System;
using UnityEngine;

/// <summary>Repeats a story handoff only while its receiving NPC still needs it.</summary>
[AddComponentMenu("Civil Craft/NPC Story Handoff")]
[DisallowMultipleComponent]
public sealed class NPCStoryHandoff : MonoBehaviour
{
    [Tooltip("Existing NPC receiving this handoff. Hidden or unspawned NPCs are never targeted.")]
    public NPCProgressionManager targetNPC;
    [Tooltip("Exact phase from which this handoff may move the receiving NPC forward.")]
    public string eligiblePhaseId = "Go to Shopkeeper";
    [Tooltip("Exact later phase at which the receiving NPC waits for the player.")]
    public string destinationPhaseId = "CollectMap";
    [Tooltip("Navigation tutorial shown only while this handoff is still relevant.")]
    public TutorialSequence returnTutorial;
    [Tooltip("Existing permanent feature that proves this handoff is finished.")]
    public string completionFeatureId = "minimap";

    // Assign to the sending NPC's repeatable dialogue-finished event. No new
    // completion flag is needed: the receiving phase and feature are authoritative.
    public void TryHandoff()
    {
        PlayerDataManager data = PlayerDataManager.Instance;
        TutorialManager tutorials = TutorialManager.Instance;
        if (!isActiveAndEnabled || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded ||
            data == null || data.CurrentData == null || tutorials == null || returnTutorial == null ||
            string.IsNullOrWhiteSpace(completionFeatureId)) return;

        if (!IsTargetPresent() || data.IsFeatureUnlocked(completionFeatureId))
        {
            CancelObsoleteOwnedGuide(tutorials);
            return;
        }

        if (string.IsNullOrWhiteSpace(eligiblePhaseId) || string.IsNullOrWhiteSpace(destinationPhaseId) ||
            string.Equals(eligiblePhaseId, destinationPhaseId, StringComparison.Ordinal)) return;

        var phases = targetNPC.AuthoredPhases;
        if (phases == null) return;
        int eligibleIndex = -1, destinationIndex = -1;
        for (int index = 0; index < phases.Count; index++)
        {
            NPCProgressionPhase phase = phases[index];
            if (phase == null) continue;
            if (string.Equals(phase.phaseId, eligiblePhaseId, StringComparison.Ordinal))
            {
                if (eligibleIndex >= 0) return;
                eligibleIndex = index;
            }
            if (string.Equals(phase.phaseId, destinationPhaseId, StringComparison.Ordinal))
            {
                if (destinationIndex >= 0) return;
                destinationIndex = index;
            }
        }
        if (eligibleIndex < 0 || destinationIndex <= eligibleIndex) return;

        NPCProgressionPhase current = targetNPC.CurrentPhase;
        bool needsMove = ReferenceEquals(current, phases[eligibleIndex]);
        if (!needsMove && !ReferenceEquals(current, phases[destinationIndex]))
        {
            CancelObsoleteOwnedGuide(tutorials);
            return;
        }
        if (targetNPC.IsTravelling) return;

        if (needsMove) targetNPC.MoveToPhaseById(destinationPhaseId);

        // A normal move commits its save immediately but activates the new phase
        // on arrival. The guide may follow that first journey; repeat calls during
        // travel return above and never restart it. Immediate arrival callbacks
        // can hide/advance the NPC, in which case no obsolete route is started.
        if (!IsTargetPresent() || PlayerDataManager.Instance != data || data.CurrentData == null ||
            data.IsFeatureUnlocked(completionFeatureId) ||
            (!ReferenceEquals(targetNPC.CurrentPhase, phases[eligibleIndex]) &&
             !ReferenceEquals(targetNPC.CurrentPhase, phases[destinationIndex])))
        {
            // A player-manager replacement belongs to a different session; only
            // the old manager's still-owned route can be cancelled here.
            if (TutorialManager.Instance == tutorials) CancelObsoleteOwnedGuide(tutorials);
            return;
        }

        if (TutorialManager.Instance == tutorials && !tutorials.IsPlayingSequence(returnTutorial))
            returnTutorial.StartTutorial();
    }

    private void CancelObsoleteOwnedGuide(TutorialManager tutorials)
    {
        if (returnTutorial != null && tutorials.IsPlayingSequence(returnTutorial))
            tutorials.CancelTutorialWithoutCompletion(returnTutorial);
    }

    private bool IsTargetPresent()
    {
        return targetNPC != null && targetNPC.isActiveAndEnabled &&
            targetNPC.gameObject.activeInHierarchy && targetNPC.gameObject.scene.IsValid() &&
            targetNPC.gameObject.scene.isLoaded &&
            NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(targetNPC.transform);
    }
}
