using System.Collections;
using UnityEngine;

/// <summary>Resumes Ronan's already-collected saves without granting rewards again.</summary>
[DisallowMultipleComponent]
public sealed class RonanPostContractContinuation : MonoBehaviour
{
    [SerializeField] private NPCProgressionManager npc = null;
    [SerializeField] private ContractSO contract = null;
    [SerializeField] private string reviewPhaseId = "RonanReview";

    /// <summary>Only the current situation tour may continue into Ronan's lesson.</summary>
    public void ContinueAfterSituationCinematic()
    {
        if (!isActiveAndEnabled || npc == null || !npc.isActiveAndEnabled || ProtectedVisit() ||
            npc.ProgressionSaveId != "RonanNpc" || npc.IsTravelling ||
            npc.CurrentPhase == null || npc.CurrentPhase.phaseId != "ExplainSituation") return;
        npc.MoveToPhaseById("LessonIntroduction");
    }

    private IEnumerator Start()
    {
        yield return null;
        while (isActiveAndEnabled && npc != null && npc.isActiveAndEnabled)
        {
            if (ProtectedVisit() || contract == null || npc.ProgressionSaveId != "RonanNpc") yield break;
            PlayerDataManager data = PlayerDataManager.Instance;
            if (data == null || data.CurrentData == null) { yield return null; continue; }
            if (!data.HasContractCompletionRecord(contract.ContractID)) yield break;
            NPCProgressionPhase phase = npc.CurrentPhase;
            if (phase == null || !IsPreReviewPhase(phase.phaseId) || !HasReviewPhase()) yield break;

            // Normal collection still owns rewards and phase advancement. Only old
            // completed saves need this deferred review, after other UI owners finish.
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

    private static bool IsPreReviewPhase(string id) => id == "MeetingRonan" ||
        id == "ExplainSituation" || id == "LessonIntroduction" || id == "ProjectBriefing";

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        (FusionConnectionManager.Instance.IsGuestInHostWorld || FusionConnectionManager.Instance.IsGuestSaveProtected);
}
