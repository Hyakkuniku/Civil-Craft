using System.Collections;
using UnityEngine;

/// <summary>
/// Resumes Marsh saves made before the post-contract review phase existed.
/// Normal turn-ins still use NPCProgressionManager's existing completion event.
/// </summary>
[DisallowMultipleComponent]
public sealed class MarshPostContractContinuation : MonoBehaviour
{
    [SerializeField] private NPCProgressionManager npc;
    [SerializeField] private ContractSO contract;
    [SerializeField] private string reviewPhaseId = "MarshReview";

    private IEnumerator Start()
    {
        // Let both NPCProgressionManager.Start and PlayerDataManager restore
        // their state before inspecting it. Never write a completion or reward.
        yield return null;
        while (isActiveAndEnabled && npc != null && npc.isActiveAndEnabled)
        {
            if (ProtectedVisit() || contract == null || npc.ProgressionSaveId != "MarshNPC") yield break;
            PlayerDataManager data = PlayerDataManager.Instance;
            if (data == null || data.CurrentData == null) { yield return null; continue; }
            if (!data.HasContractCompletionRecord(contract.ContractID)) yield break;
            NPCProgressionPhase phase = npc.CurrentPhase;
            if (phase == null || !IsPreReviewPhase(phase.phaseId)) yield break;
            if (!HasReviewPhase()) yield break;

            // A fresh turn-in may already be travelling to the appended phase.
            // Wait rather than cancelling/restarting that normal continuation.
            bool busy = npc.IsTravelling || DialogueManager.IsAnyDialogueActive ||
                Time.timeScale <= 0f ||
                (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel) ||
                (GameManager.Instance != null && (GameManager.Instance.IsTransitioning ||
                    GameManager.Instance.CurrentState != GameManager.GameState.Normal));
            if (busy) { yield return null; continue; }

            npc.MoveToPhaseById(reviewPhaseId);
            yield break;
        }
    }

    private bool HasReviewPhase()
    {
        if (string.IsNullOrWhiteSpace(reviewPhaseId) || npc.AuthoredPhases == null) return false;
        int matches = 0;
        foreach (NPCProgressionPhase phase in npc.AuthoredPhases)
            if (phase != null && phase.phaseId == reviewPhaseId && phase.contract == null) matches++;
        return matches == 1;
    }

    private static bool IsPreReviewPhase(string id) => id == "MeetingMarsh" ||
        id == "ExplainSituation" || id == "Material Introduction" || id == "ProjectBriefing";

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        (FusionConnectionManager.Instance.IsGuestInHostWorld || FusionConnectionManager.Instance.IsGuestSaveProtected);
}
