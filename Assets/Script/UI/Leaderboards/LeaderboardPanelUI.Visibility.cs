using System;
using System.Collections;
using System.Collections.Generic;
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
        LeaderboardContractVisibility.BoundedDownloadHandler handler = null;
        UnityWebRequestAsyncOperation operation = null;
        try
        {
            request = LeaderboardContractVisibility.CreateRequest(out handler);
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

}
