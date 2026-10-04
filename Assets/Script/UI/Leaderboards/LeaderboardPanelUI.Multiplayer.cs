using System;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class LeaderboardPanelUI
{
    [Header("Authored Multiplayer Board")]
    [SerializeField] private Button storyBoardButton;
    [SerializeField] private Button multiplayerBoardButton;
    [SerializeField] private Button refreshBoardButton;
    [SerializeField] private GameObject storyColumnHeader;
    [SerializeField] private GameObject multiplayerColumnHeader;
    [SerializeField] private GameObject multiplayerGuide;
    [SerializeField] private ScrollRect multiplayerScroll;
    [SerializeField] private MultiplayerLeaderboardRowUI[] multiplayerRows;
    [SerializeField] private TMP_Text boardFooter;
    private bool multiplayerSelected;

    [Serializable] private sealed class MultiplayerEntry
    {
        public int rank;
        public string playerId;
        public string displayName;
        public int wins;
        public int losses;
        public int draws;
    }
    [Serializable] private sealed class MultiplayerTotals { public int wins; public int losses; public int draws; }
    [Serializable] private sealed class MultiplayerBoard
    {
        public MultiplayerEntry[] entries;
        public MultiplayerTotals personal;
    }

    private void InitializeMultiplayerBoard()
    {
        if (storyBoardButton != null) storyBoardButton.onClick.AddListener(SelectStoryBoard);
        if (multiplayerBoardButton != null) multiplayerBoardButton.onClick.AddListener(SelectMultiplayerBoard);
        if (refreshBoardButton != null) refreshBoardButton.onClick.AddListener(RefreshRankings);
        ApplyBoardVisibility();
    }
    private void RemoveMultiplayerBoardListeners()
    {
        if (storyBoardButton != null) storyBoardButton.onClick.RemoveListener(SelectStoryBoard);
        if (multiplayerBoardButton != null) multiplayerBoardButton.onClick.RemoveListener(SelectMultiplayerBoard);
        if (refreshBoardButton != null) refreshBoardButton.onClick.RemoveListener(RefreshRankings);
    }
    public void SelectStoryBoard() { multiplayerSelected = false; Refresh(); }
    public void SelectMultiplayerBoard() { multiplayerSelected = true; Refresh(); }
    public void RefreshRankings() { if (panel != null && panel.activeSelf) Refresh(); }

    private void ApplyBoardVisibility()
    {
        if (contractDropdown != null) contractDropdown.gameObject.SetActive(!multiplayerSelected);
        if (efficientToggle != null) efficientToggle.gameObject.SetActive(!multiplayerSelected);
        if (strongestToggle != null) strongestToggle.gameObject.SetActive(!multiplayerSelected);
        if (storyColumnHeader != null) storyColumnHeader.SetActive(!multiplayerSelected);
        if (leaderboardScroll != null) leaderboardScroll.gameObject.SetActive(!multiplayerSelected);
        if (leaderboardScroll != null && leaderboardScroll.verticalScrollbar != null)
            leaderboardScroll.verticalScrollbar.gameObject.SetActive(!multiplayerSelected);
        if (multiplayerColumnHeader != null) multiplayerColumnHeader.SetActive(multiplayerSelected);
        if (multiplayerScroll != null) multiplayerScroll.gameObject.SetActive(multiplayerSelected);
        if (multiplayerScroll != null && multiplayerScroll.verticalScrollbar != null)
            multiplayerScroll.verticalScrollbar.gameObject.SetActive(multiplayerSelected);
        if (multiplayerGuide != null) multiplayerGuide.SetActive(multiplayerSelected);
        Color active = new Color32(239, 166, 49, 255), inactive = new Color32(239, 226, 204, 255);
        if (storyBoardButton != null && storyBoardButton.targetGraphic != null)
            storyBoardButton.targetGraphic.color = multiplayerSelected ? inactive : active;
        if (multiplayerBoardButton != null && multiplayerBoardButton.targetGraphic != null)
            multiplayerBoardButton.targetGraphic.color = multiplayerSelected ? active : inactive;
        if (boardFooter != null) boardFooter.text = multiplayerSelected
            ? "WIN RATE = WINS ÷ (WINS + LOSSES)  •  DRAWS EXCLUDED"
            : "Top 15 successful saved bridges";
    }

    private void ClearMultiplayerRows()
    {
        if (multiplayerRows == null) return;
        foreach (var row in multiplayerRows) if (row != null) row.gameObject.SetActive(false);
    }

    private void RefreshMultiplayer(int generation)
    {
        if (descriptionText != null) descriptionText.text = "MULTIPLAYER  •  Ranked by total wins";
        if (personalBestText != null) personalBestText.text = "YOUR RECORD  —  Loading...";
        string account = MultiplayerResultReportingService.AuthenticatedAccountId;
        if (string.IsNullOrEmpty(account))
        {
            SetStatus("Sign in to see multiplayer rankings and save your record.");
            if (personalBestText != null) personalBestText.text = "Guest accounts are not ranked";
            return;
        }
        SetStatus("Loading multiplayer top 15...");
        var callerContext = new PlayFabAuthenticationContext(); callerContext.CopyFrom(PlayFabSettings.staticPlayer);
        PlayFabClientAPI.ExecuteCloudScript(new ExecuteCloudScriptRequest {
            FunctionName = "getMultiplayerLeaderboardV1", GeneratePlayStreamEvent = false, AuthenticationContext = callerContext
        }, result =>
        {
            if (!IsCurrentMultiplayerRequest(generation, account)) return;
            if (result.Error != null || result.FunctionResult == null)
            { MultiplayerLoadFailed("Multiplayer rankings are not available yet. Please try again later."); return; }
            try
            {
                var board = JsonUtility.FromJson<MultiplayerBoard>(PlayFabSimpleJson.SerializeObject(result.FunctionResult));
                if (board == null || board.personal == null || board.entries == null)
                    throw new InvalidOperationException("Incomplete multiplayer leaderboard response.");
                int shown = 0;
                foreach (var entry in board.entries)
                {
                    if (shown >= TopCount || multiplayerRows == null || shown >= multiplayerRows.Length) break;
                    if (entry == null || entry.rank <= 0 || entry.wins < 0 || entry.losses < 0) continue;
                    var row = multiplayerRows[shown++];
                    if (row == null) continue;
                    row.gameObject.SetActive(true);
                    row.Bind(entry.rank, entry.displayName, entry.wins, entry.losses, entry.playerId == account);
                }
                var own = board.personal;
                if (personalBestText != null) personalBestText.text =
                    $"YOUR RECORD  •  {own.wins:N0} WINS  /  {own.losses:N0} LOSSES  •  {MultiplayerLeaderboardRowUI.FormatWinRate(own.wins, own.losses)} WIN RATE";
                SetStatus(shown == 0 ? "No multiplayer results yet. Complete a challenge to join the board." : string.Empty);
                if (multiplayerScroll != null) multiplayerScroll.verticalNormalizedPosition = 1f;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Multiplayer leaderboard] " + error.Message, this);
                MultiplayerLoadFailed("Could not read multiplayer rankings. Please try again later.");
            }
        }, error =>
        {
            if (!IsCurrentMultiplayerRequest(generation, account)) return;
            Debug.LogWarning("[Multiplayer leaderboard] " + error.ErrorMessage, this);
            MultiplayerLoadFailed("Rankings unavailable. Please try again later.");
        });
    }
    private bool IsCurrentMultiplayerRequest(int generation, string account) => this != null &&
        generation == requestGeneration && multiplayerSelected && panel != null && panel.activeSelf &&
        MultiplayerResultReportingService.AuthenticatedAccountId == account;
    private void MultiplayerLoadFailed(string message)
    {
        ClearMultiplayerRows(); SetStatus(message);
        if (personalBestText != null) personalBestText.text = "YOUR RECORD  —  Unavailable";
    }
}
