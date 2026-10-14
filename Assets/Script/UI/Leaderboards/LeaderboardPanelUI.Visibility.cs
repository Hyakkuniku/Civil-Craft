using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class LeaderboardPanelUI
{
    private HashSet<string> visibleContractIds;
    private bool contractVisibilityLoading, completedContractUnavailable;
    private string contractVisibilityError;
    private ContractSO requestedCompletionContract;
    private int visibilityGeneration;
    private Coroutine visibilityRoutine;
    private UnityWebRequest visibilityRequest;
    private bool StorySelectorReady => !multiplayerSelected && !contractVisibilityLoading &&
        visibleContractIds != null && displayedContracts.Count > 0;

    private void OnDisable() { CancelPendingRequest(); }

    private void BeginContractVisibilityRequest()
    {
        string previousId = SelectedContract != null ? SelectedContract.ContractID : null;
        CancelContractVisibilityRequest();
        int generation = visibilityGeneration;
        visibleContractIds = null;
        contractVisibilityLoading = true;
        contractVisibilityError = null;
        completedContractUnavailable = false;
        displayedContracts.Clear();
        if (contractDropdown != null) { contractDropdown.ClearOptions(); contractDropdown.interactable = false; }
        Refresh();
        visibilityRoutine = StartCoroutine(FetchContractVisibility(generation, previousId));
    }

    private bool ContractVisibilityReady()
    {
        if (contractVisibilityLoading || visibleContractIds == null || completedContractUnavailable)
        {
            if (personalBestText != null) personalBestText.text = string.Empty;
            if (descriptionText != null) descriptionText.text = "Story contract leaderboards";
            SetStatus(contractVisibilityLoading ? "Loading leaderboard contracts..." : completedContractUnavailable
                ? "Leaderboard display is disabled for this contract."
                : contractVisibilityError ?? "Leaderboard contracts are unavailable. Refresh to try again.");
            return false;
        }
        return true;
    }

    private IEnumerator FetchContractVisibility(int generation, string previousId)
    {
        UnityWebRequest request = null;
        BoundedContractVisibilityDownload handler = null;
        UnityWebRequestAsyncOperation operation = null;
        try
        {
            handler = new BoundedContractVisibilityDownload();
            request = new UnityWebRequest(LeaderboardContractVisibility.FeedUrl, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = handler;
            request.timeout = LeaderboardContractVisibility.TimeoutSeconds;
            request.redirectLimit = 0;
            request.SetRequestHeader("Accept", "application/json");
            visibilityRequest = request;
            operation = request.SendWebRequest();
        }
        catch (Exception)
        {
            if (request != null) request.Dispose(); else if (handler != null) handler.Dispose();
            visibilityRequest = null;
            VisibilityFailed(generation);
            yield break;
        }
        try
        {
            yield return operation;
            if (!IsCurrentVisibilityRequest(generation)) yield break;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200 || handler.Overflowed ||
                !LeaderboardContractVisibility.TryParse(handler.Body, out HashSet<string> allowed))
            { VisibilityFailed(generation); yield break; }

            visibleContractIds = allowed;
            contractVisibilityLoading = false;
            PopulateContracts(requestedCompletionContract);
            if (requestedCompletionContract != null)
                completedContractUnavailable = SelectedContract == null || !string.Equals(
                    SelectedContract.ContractID, requestedCompletionContract.ContractID, StringComparison.Ordinal);
            else if (previousId != null && contractDropdown != null)
            {
                int selected = displayedContracts.FindIndex(contract => contract.ContractID == previousId);
                if (selected >= 0) contractDropdown.SetValueWithoutNotify(selected);
                contractDropdown.RefreshShownValue();
            }
            // Multiplayer owns its current PlayFab request and UI while selected.
            // A slower public contract response must not restart or clear that board.
            if (!multiplayerSelected) Refresh();
        }
        finally
        {
            if (visibilityRequest == request) visibilityRequest = null;
            request.Dispose();
            if (generation == visibilityGeneration) visibilityRoutine = null;
        }
    }

    private bool IsCurrentVisibilityRequest(int generation) => this != null && generation == visibilityGeneration &&
        panel != null && panel.activeSelf;

    private void VisibilityFailed(int generation)
    {
        if (!IsCurrentVisibilityRequest(generation)) return;
        contractVisibilityLoading = false;
        visibleContractIds = null;
        contractVisibilityError = "Leaderboard contracts are unavailable. Refresh to try again.";
        if (!multiplayerSelected) Refresh();
    }

    private void CancelContractVisibilityRequest()
    {
        ++visibilityGeneration;
        if (visibilityRequest != null) visibilityRequest.Abort();
        if (visibilityRoutine != null) StopCoroutine(visibilityRoutine);
        visibilityRoutine = null;
        if (visibilityRequest != null) visibilityRequest.Dispose();
        visibilityRequest = null;
    }

    private sealed class BoundedContractVisibilityDownload : DownloadHandlerScript
    {
        private readonly MemoryStream received = new MemoryStream();
        public bool Overflowed { get; private set; }
        public string Body => Encoding.UTF8.GetString(received.ToArray());
        public BoundedContractVisibilityDownload() : base(new byte[1024]) { }
        protected override bool ReceiveData(byte[] data, int length)
        {
            if (data == null || length < 0 || length > data.Length ||
                received.Length + length > LeaderboardContractVisibility.MaxResponseBytes)
            { Overflowed = true; return false; }
            if (length > 0) received.Write(data, 0, length);
            return true;
        }
    }
}
