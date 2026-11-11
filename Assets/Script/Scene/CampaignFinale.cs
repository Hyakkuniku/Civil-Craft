using UnityEngine;

[DisallowMultipleComponent]
public sealed class CampaignFinale : MonoBehaviour
{
    [SerializeField] private ContractSO finalContract;
    [SerializeField] private QuizData finalQuiz;
    [SerializeField] private QuizContext quizContext;
    [SerializeField] private GameObject professorBhan;
    [SerializeField] private CinematicDirector endingCinematic;
    [SerializeField] private MainMenuCredits credits;

    private float nextCheck;

    private void Update()
    {
        if (professorBhan == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .5f;

        bool ready = CanStartEnding(PlayerDataManager.Instance);
        if (professorBhan.activeSelf == ready) return;
        professorBhan.SetActive(ready);
    }

    internal bool CanStartEnding(PlayerDataManager data)
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        return data != null && data.CurrentData != null && finalContract != null && finalQuiz != null &&
            endingCinematic != null && quizContext != null && quizContext.IsValid &&
            !(connection != null && connection.IsGuestSaveProtected) &&
            !data.HasPlayedCinematic(endingCinematic.cinematicID) &&
            data.CurrentData.quizProgress != null &&
            data.CurrentData.quizProgress.Exists(entry => entry != null && entry.completed && entry.quizId == finalQuiz.quizId) &&
            data.TryGetQuizProgress(finalQuiz, quizContext, out QuizProgressSaveData progress) && progress.completed &&
            (data.HasContractCompletionRecord(finalContract.ContractID) || data.HasValidSavedBridge(finalContract.ContractID));
    }

    public void FinishConversation()
    {
        PlayerDataManager data = PlayerDataManager.Instance;
        if (professorBhan == null || !professorBhan.activeInHierarchy || !CanStartEnding(data) || endingCinematic.IsPlaying) return;

        string id = finalContract.ContractID;
        if (!data.HasContractCompletionRecord(id))
        {
            var results = LevelCompleteManager.Instance;
            int gold = 0, exp = 0;
            bool hasPayout = results != null && results.TryGetContractPayout(id, out gold, out exp);
            GameWalletPayoutQuote payout = data.ResolveContractPayout(id, gold, exp, hasPayout,
                finalContract.goldReward, finalContract.expReward);
            if (!data.CompleteContract(id, payout.gold, payout.exp))
            {
                Debug.LogWarning("[Campaign finale] The final contract could not be turned in. Speak to Bhan again to retry.", this);
                return;
            }

            results?.MarkContractAsPaid(id);
            ObjectiveTrackerUI.Instance?.ClearObjective(finalContract);
        }

        endingCinematic.PlayCinematic();
    }

    public void RollCredits()
    {
        credits.Open();
    }
}
