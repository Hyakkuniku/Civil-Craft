using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>Per-report approval of the completion action. Never caches an earlier
/// report's approval or opens the leaderboard, saves a run, or changes rewards.</summary>
public sealed class CompletionLeaderboardVisibility
{
    private MonoBehaviour owner;
    private Button button;
    private GameObject report;
    private ContractSO requestedContract;
    private string requestedContractId;
    private Coroutine routine;
    private UnityWebRequest request;
    private int generation;
    private bool verified;

    public void Begin(MonoBehaviour host, Button action, GameObject completionReport, ContractSO contract, bool boardAvailable)
    {
        Cancel();
        owner = host;
        button = action;
        report = completionReport;
        requestedContract = contract;
        requestedContractId = contract != null ? contract.ContractID : null;
        SetVisible(false);
        if (!boardAvailable || owner == null || !owner.isActiveAndEnabled || button == null || report == null ||
            !Eligible(contract)) return;
        // Both completion paths prepare their action just before activating the
        // report. Require an open report when applying the asynchronous result.
        routine = owner.StartCoroutine(Fetch(generation));
    }

    public bool CanOpen(ContractSO contract) => verified && owner != null && owner.isActiveAndEnabled &&
        report != null && report.activeSelf && button != null && requestedContract == contract && Eligible(contract) &&
        string.Equals(requestedContractId, contract.ContractID, StringComparison.Ordinal);

    public void Cancel()
    {
        ++generation;
        verified = false;
        SetVisible(false);
        UnityWebRequest pending = request;
        request = null;
        if (pending != null) pending.Abort();
        if (routine != null && owner != null) owner.StopCoroutine(routine);
        routine = null;
        if (pending != null) pending.Dispose();
        requestedContract = null;
        requestedContractId = null;
    }

    private static bool Eligible(ContractSO contract) => contract != null && !contract.hideFromLeaderboard &&
        !string.IsNullOrWhiteSpace(contract.ContractID) &&
        !contract.ContractID.StartsWith(ChallengeBuildWorkspace.ContractPrefix, StringComparison.Ordinal);

    private void SetVisible(bool visible)
    {
        if (button == null) return;
        button.interactable = visible;
        button.gameObject.SetActive(visible);
    }

    private IEnumerator Fetch(int expectedGeneration)
    {
        UnityWebRequest current = null;
        LeaderboardContractVisibility.BoundedDownloadHandler handler = null;
        UnityWebRequestAsyncOperation operation;
        try
        {
            current = LeaderboardContractVisibility.CreateRequest(out handler);
            request = current;
            operation = current.SendWebRequest();
        }
        catch (Exception)
        {
            if (current != null) current.Dispose(); else if (handler != null) handler.Dispose();
            if (request == current) request = null;
            yield break; // Unknown availability is not approval.
        }
        try
        {
            yield return operation;
            if (expectedGeneration != generation || owner == null || !owner.isActiveAndEnabled ||
                report == null || !report.activeSelf || !Eligible(requestedContract) ||
                !string.Equals(requestedContractId, requestedContract.ContractID, StringComparison.Ordinal)) yield break;
            verified = current.result == UnityWebRequest.Result.Success && current.responseCode == 200 &&
                !handler.Overflowed && LeaderboardContractVisibility.TryParse(handler.Body, out HashSet<string> allowed) &&
                allowed.Contains(requestedContractId);
            SetVisible(verified);
        }
        finally
        {
            // Cancellation already aborts/disposes and detaches the request.
            if (request == current) { request = null; current.Dispose(); }
            if (expectedGeneration == generation) routine = null;
        }
    }
}
