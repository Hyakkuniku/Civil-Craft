using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Sends a successful, locally saved run to the PlayFab CloudScript publisher.
/// This has no scene object or per-frame work. It never writes player statistics
/// directly from the client.
/// </summary>
public static class LeaderboardSubmissionService
{
    private const string FunctionName = "submitBridgeRunV1";

    public static void SubmitSavedRun(ContractSO contract, float cost, float peakStress)
    {
        if (contract == null || contract.hideFromLeaderboard ||
            !PlayFabClientAPI.IsClientLoggedIn() ||
            (PlayFabAuthManager.Instance != null && PlayFabAuthManager.Instance.IsGuestSelected))
            return;

        if (!LeaderboardScoreCodec.TryEncode(cost, peakStress, false, out _) ||
            !LeaderboardScoreCodec.TryEncode(cost, peakStress, true, out _))
        {
            Debug.LogWarning("[Leaderboards] Saved run is outside the online score range.");
            return;
        }

        var payload = new Dictionary<string, object> {
            { "contractId", contract.ContractID },
            { "cost", Mathf.RoundToInt(cost) },
            { "stressTenths", Mathf.RoundToInt(peakStress * 10f) }
        };
        PlayFabClientAPI.ExecuteCloudScript(new ExecuteCloudScriptRequest {
            FunctionName = FunctionName,
            FunctionParameter = payload,
            GeneratePlayStreamEvent = false
        }, result => {
            if (result == null)
                Debug.LogWarning("[Leaderboards] Submission returned no result.");
            else if (result.Error != null)
                Debug.LogWarning("[Leaderboards] Submission was rejected: " + result.Error.Message);
            else if (result.FunctionResult is IDictionary<string, object> response &&
                     response.TryGetValue("accepted", out object accepted) &&
                     accepted is bool wasAccepted && wasAccepted)
                Debug.Log("[Leaderboards] Saved bridge run accepted for contract '" +
                          contract.ContractID + "' (server API requests: " +
                          result.APIRequestsIssued + ").");
            else
                Debug.LogWarning("[Leaderboards] PlayFab returned no accepted result for contract '" +
                                 contract.ContractID + "'.");
        }, error => {
            Debug.LogWarning("[Leaderboards] Could not submit saved run: " + error.ErrorMessage);
        });
    }
}
