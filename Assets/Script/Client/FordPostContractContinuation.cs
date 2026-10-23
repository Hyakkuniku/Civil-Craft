using System.Collections;
using UnityEngine;

/// <summary>Resumes already-collected Ford test saves without awarding anything again.</summary>
[DisallowMultipleComponent]
public sealed class FordPostContractContinuation : MonoBehaviour
{
    [SerializeField] private NPCProgressionManager npc = null;
    [SerializeField] private ContractSO contract = null;
    [SerializeField] private string reviewPhaseId = "FordReview";

    /// <summary>Continue only the situation tour that still belongs to this NPC phase.</summary>
    public void ContinueAfterSituationCinematic()
    {
        if (!isActiveAndEnabled || npc == null || !npc.isActiveAndEnabled || ProtectedVisit() ||
            npc.ProgressionSaveId != "FordNPC" || npc.IsTravelling ||
            npc.CurrentPhase == null || npc.CurrentPhase.phaseId != "ExplainSituation") return;
        npc.MoveToPhaseById("LessonIntroduction");
    }

    private IEnumerator Start()
    {
        yield return null;
        while (isActiveAndEnabled && npc != null && npc.isActiveAndEnabled)
        {
            if (ProtectedVisit() || contract == null || npc.ProgressionSaveId != "FordNPC") yield break;
            PlayerDataManager data = PlayerDataManager.Instance;
            if (data == null || data.CurrentData == null) { yield return null; continue; }
            if (!data.HasContractCompletionRecord(contract.ContractID)) yield break;
            NPCProgressionPhase phase = npc.CurrentPhase;
            if (phase == null || !IsPreReviewPhase(phase.phaseId) || !HasReviewPhase()) yield break;

            // Defer to normal completion/arrival and never open over another UI owner.
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

    private static bool IsPreReviewPhase(string id) => id == "MeetingFord" ||
        id == "ExplainSituation" || id == "LessonIntroduction" || id == "ProjectBriefing";

    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        (FusionConnectionManager.Instance.IsGuestInHostWorld || FusionConnectionManager.Instance.IsGuestSaveProtected);
}
