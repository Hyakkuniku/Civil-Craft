using TMPro;
using UnityEngine;
using UnityEngine.UI;

// All objects, positions, fonts, colors and button handlers are authored in the
// scene. This controller only changes content, visibility and existing input gates.
public sealed partial class MultiplayerChallengeLobbyUI
{
    [Header("Authored collapsible player status")]
    [SerializeField] private GameObject submissionStatusBody;
    [SerializeField] private TMP_Text submissionStatusHeader;
    [Header("Authored submission confirmation")]
    [SerializeField] private GameObject submissionConfirmPanel;
    [SerializeField] private Button confirmSubmitButton;
    [Header("Authored challenge results")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text resultsWinner;
    [SerializeField] private TMP_Text resultsRules;
    [SerializeField] private TMP_Text resultsHostName;
    [SerializeField] private TMP_Text resultsGuestName;
    [SerializeField] private TMP_Text resultsHostScore;
    [SerializeField] private TMP_Text resultsGuestScore;
    [SerializeField] private TMP_Text resultsHostDetails;
    [SerializeField] private TMP_Text resultsGuestDetails;

    private static MultiplayerChallengeLobbyUI presentationOwner;
    private bool statusExpanded;
    private string lastSubmissionNotice;
    private int confirmationRevision = int.MinValue;
    public static bool IsSubmissionConfirmationOpen => presentationOwner != null &&
        presentationOwner.submissionConfirmPanel != null && presentationOwner.submissionConfirmPanel.activeSelf;

    private void InitializeAuthoredPresentation()
    {
        presentationOwner = this;
        ResetAuthoredPresentation();
        if (resultsPanel != null) resultsPanel.SetActive(false);
    }

    public void ToggleSubmissionStatus()
    {
        statusExpanded = !statusExpanded;
        if (submissionStatusBody != null) submissionStatusBody.SetActive(statusExpanded);
        if (host != null && boundRunner != null) RefreshSubmissionControls(host.ChallengeState);
    }

    private void OpenSubmissionConfirmation()
    {
        if (submissionConfirmPanel == null || host == null) return;
        confirmationRevision = host.ChallengeState.Revision;
        submissionConfirmPanel.SetActive(true);
        if (confirmSubmitButton != null) confirmSubmitButton.interactable = true;
        buildWorkspace?.SetEditingEnabled(false);
        lastBuildEditingEnabled = false;
    }

    public void CancelSubmissionConfirmation()
    {
        if (submissionConfirmPanel != null) submissionConfirmPanel.SetActive(false);
        confirmationRevision = int.MinValue;
        lastBuildEditingEnabled = null; // Restore editing through the existing authoritative gate.
    }

    public void ConfirmSubmitBridge()
    {
        if (!IsSubmissionConfirmationOpen || confirmationRevision == int.MinValue) return;
        int revision = confirmationRevision;
        CancelSubmissionConfirmation();
        if (host == null || boundRunner == null || buildWorkspace == null || GameManager.Instance == null ||
            host.ChallengeState.Revision != revision || !GameManager.Instance.CanEditSessionChallengeBuild) return;
        boundRunner.GetComponent<FusionChallengeSubmissionSync>()?.SubmitLocalBridge(buildWorkspace);
        RefreshSubmissionControls(host.ChallengeState);
    }

    private void RefreshBuildCardPresentation(MultiplayerChallengeState state, bool submitted, bool sending, string notice)
    {
        if (IsSubmissionConfirmationOpen && (state.Revision != confirmationRevision ||
            state.Phase != MultiplayerChallengePhase.Building || submitted || sending)) CancelSubmissionConfirmation();
        // A new placement/session prompt must be visible even if the player had
        // collapsed the card. They can collapse it again; don't force it every frame.
        if (notice != lastSubmissionNotice)
        {
            lastSubmissionNotice = notice;
            if (!submitted && !sending && !string.IsNullOrEmpty(notice)) statusExpanded = true;
        }
        int accepted = (state.HostSubmitted ? 1 : 0) + (state.GuestSubmitted ? 1 : 0);
        string title = MultiplayerChallengeRules.IsTestPhase(state.Phase) ? $"TEST {state.TestIndex} / 2" :
            sending ? "SUBMITTING..." : $"PLAYERS  {accepted}/2 SUBMITTED";
        if (submissionStatusHeader != null) submissionStatusHeader.text = title + (statusExpanded ? "  −" : "  +");
        if (submissionStatusBody != null && submissionStatusBody.activeSelf != statusExpanded)
            submissionStatusBody.SetActive(statusExpanded);
    }

    private void ResetAuthoredPresentation()
    {
        CancelSubmissionConfirmation();
        statusExpanded = false;
        lastSubmissionNotice = null;
        if (submissionStatusBody != null) submissionStatusBody.SetActive(false);
    }

    internal static string PlainText(string value) => (value ?? string.Empty).Replace('<', '‹').Replace('>', '›');
    internal static string Emphasis(string value, string color = "49301D") => $"<b><color=#{color}>{PlainText(value)}</color></b>";
    private static string BuildStatus(bool submitted) => submitted ? Emphasis("SUBMITTED", "376F42") : Emphasis("BUILDING", "98601C");

    private void RefreshResultsPresentation(MultiplayerChallengeState state, string hostPlayer, string guestPlayer)
    {
        string winner = state.Winner == ChallengeWinner.Host ? "WINNER: " + hostPlayer :
            state.Winner == ChallengeWinner.Guest ? "WINNER: " + guestPlayer :
            state.Winner == ChallengeWinner.Draw ? "DRAW — equal scores" : "NO WINNER — neither bridge qualified";
        if (resultsWinner != null) resultsWinner.text = Emphasis(winner, "79500F");
        if (resultsRules != null) resultsRules.text = $"<b>40% COST / 60% STRENGTH</b>  •  Budget {Emphasis("₱" + state.ChallengeBudget.ToString("N0"))}";
        if (resultsHostName != null) resultsHostName.text = PlainText(hostPlayer) + "  <size=70%>(HOST)</size>";
        if (resultsGuestName != null) resultsGuestName.text = PlainText(guestPlayer) + "  <size=70%>(GUEST)</size>";
        if (resultsHostScore != null) resultsHostScore.text = ScoreLabel(state.HostScoreHundredths);
        if (resultsGuestScore != null) resultsGuestScore.text = ScoreLabel(state.GuestScoreHundredths);
        if (resultsHostDetails != null) resultsHostDetails.text = ResultsDetails(state.HostTestOutcome, state.HostSubmittedCost,
            state.ChallengeBudget, state.HostPeakStress);
        if (resultsGuestDetails != null) resultsGuestDetails.text = ResultsDetails(state.GuestTestOutcome, state.GuestSubmittedCost,
            state.ChallengeBudget, state.GuestPeakStress);
    }

    internal static string ScoreLabel(int hundredths) => $"<b>{hundredths / 100.0:0.00}</b><size=45%> / 100</size>";
    internal static string ResultsDetails(ChallengeTestOutcome outcome, float cost, float budget, float peak)
    {
        var score = ChallengeCompetitionScoring.Grade(outcome, cost, budget, peak);
        string status = outcome == ChallengeTestOutcome.Crossed && !score.Qualified ? "OVER BUDGET — NOT QUALIFIED" : TestOutcomeLabel(outcome);
        return Emphasis(status, score.Qualified ? "376F42" : "9B3F2F") +
            $"\n\nCost used   {Emphasis("₱" + cost.ToString("N0"))}\nPeak stress   {Emphasis((peak * 100f).ToString("0.0") + "%")}\n\n" +
            (score.Qualified ? $"Cost efficiency   <b>{score.CostPercent:0.00}%</b>\nStrength   <b>{score.StrengthPercent:0.00}%</b>" : "Not eligible to win");
    }
}
