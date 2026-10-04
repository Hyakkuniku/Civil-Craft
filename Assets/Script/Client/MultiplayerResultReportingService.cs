using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>Durable account-bound multiplayer receipts, independent of world/story saves.</summary>
public sealed class MultiplayerResultReportingService : MonoBehaviour
{
    public const string FunctionName = "submitMultiplayerResultV1";
    public const string QueueStorageKey = "CivilCraft.MultiplayerResultQueue.v1";
    private const string RecoveryStorageKey = "CivilCraft.MultiplayerResultQueue.v1.recovery";
    private static MultiplayerResultReportingService instance;
    private MultiplayerResultLedger ledger = new MultiplayerResultLedger();
    private readonly HashSet<string> inFlightAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private float nextQueueCheck;

    // Verified against this project's SDK: login populates staticPlayer.PlayFabId
    // from LoginResult.PlayFabId, and IsClientLoggedIn checks its session ticket.
    public static string AuthenticatedAccountId
    {
        get
        {
            PlayFabAuthManager auth = PlayFabAuthManager.Instance;
            bool guest = auth != null ? auth.IsGuestSelected : PlayerPrefs.GetInt("LoginChoice", 0) == 1;
            return MultiplayerResultPolicy.TryAuthenticatedAccountId(PlayFabSettings.staticPlayer.PlayFabId,
                PlayFabClientAPI.IsClientLoggedIn(), guest, out string accountId) ? accountId : string.Empty;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() => EnsureInstance();

    private static MultiplayerResultReportingService EnsureInstance()
    {
        if (instance != null) return instance;
        instance = FindObjectOfType<MultiplayerResultReportingService>();
        if (instance == null)
        {
            var serviceObject = new GameObject("Multiplayer Result Reporting");
            instance = serviceObject.AddComponent<MultiplayerResultReportingService>();
        }
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        LoadLedger();
    }

    public static bool Enqueue(MultiplayerResultReport report)
    {
        string accountId = AuthenticatedAccountId;
        if (!MultiplayerResultPolicy.IsValidReport(report) ||
            !string.Equals(report.accountId, accountId, StringComparison.OrdinalIgnoreCase)) return false;
        MultiplayerResultReportingService service = EnsureInstance();
        if (!service.ledger.TryEnqueue(report, PlayFabSettings.staticSettings.TitleId)) return false;
        service.SaveLedger();
        service.nextQueueCheck = 0f;
        Debug.Log("[MultiplayerResults] Completed match queued.");
        return true;
    }

    private void LoadLedger()
    {
        string serialized = PlayerPrefs.GetString(QueueStorageKey, string.Empty);
        if (string.IsNullOrEmpty(serialized)) return;
        try
        {
            MultiplayerResultLedger saved = JsonUtility.FromJson<MultiplayerResultLedger>(serialized);
            if (saved == null || saved.schemaVersion != 1 || saved.reports == null)
                throw new FormatException();
            ledger = saved;
        }
        catch (Exception)
        {
            // Preserve an unreadable ledger for recovery before starting a new one.
            try { PlayerPrefs.SetString(RecoveryStorageKey, serialized); PlayerPrefs.Save(); }
            catch (Exception) { }
            Debug.LogWarning("[MultiplayerResults] Could not read the saved result queue; a recovery copy was retained.");
        }
    }

    private void SaveLedger()
    {
        try
        {
            PlayerPrefs.SetString(QueueStorageKey, JsonUtility.ToJson(ledger));
            PlayerPrefs.Save();
        }
        catch (Exception)
        {
            Debug.LogWarning("[MultiplayerResults] Could not persist the result queue on this device.");
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextQueueCheck) return;
        nextQueueCheck = Time.unscaledTime + 0.5f;
        string accountId = AuthenticatedAccountId;
        string titleId = PlayFabSettings.staticSettings.TitleId;
        string accountKey = titleId + ":" + accountId;
        if (string.IsNullOrEmpty(accountId) || inFlightAccounts.Contains(accountKey)) return;
        MultiplayerResultReport report = ledger.FindNextEligible(titleId, accountId, DateTime.UtcNow.Ticks);
        if (report != null) Submit(report, accountKey);
    }

    private void Submit(MultiplayerResultReport report, string accountKey)
    {
        // Pin this request to its authenticated caller even if the player logs out
        // or signs in to a different account while the HTTP operation is running.
        var callerContext = new PlayFabAuthenticationContext();
        callerContext.CopyFrom(PlayFabSettings.staticPlayer);
        if (!callerContext.IsClientLoggedIn() ||
            !string.Equals(callerContext.PlayFabId, report.accountId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(AuthenticatedAccountId, report.accountId, StringComparison.OrdinalIgnoreCase)) return;
        inFlightAccounts.Add(accountKey);
        var request = new ExecuteCloudScriptRequest
        {
            FunctionName = FunctionName,
            AuthenticationContext = callerContext,
            FunctionParameter = new Dictionary<string, object>
            {
                { "matchId", report.matchId },
                { "opponentId", report.opponentId },
                { "outcome", report.outcome }
            },
            GeneratePlayStreamEvent = false
        };
        try
        {
            PlayFabClientAPI.ExecuteCloudScript(request, result =>
            {
                inFlightAccounts.Remove(accountKey);
                if (result == null || result.Error != null)
                { ScheduleRetry(report, "script unavailable"); return; }
                if (result.FunctionResult is IDictionary<string, object> response &&
                    ReadBoolean(response, "accepted", out bool accepted))
                {
                    if (accepted)
                    {
                        ledger.MarkDelivered(report);
                        SaveLedger();
                        Debug.Log("[MultiplayerResults] Completed match accepted.");
                        return;
                    }
                    if (ReadBoolean(response, "retryable", out bool retryable) && !retryable)
                    {
                        report.rejected = true;
                        SaveLedger();
                        Debug.LogWarning("[MultiplayerResults] Completed match was rejected by the server.");
                        return;
                    }
                }
                ScheduleRetry(report, "no acknowledgement");
            }, error =>
            {
                inFlightAccounts.Remove(accountKey);
                ScheduleRetry(report, error != null ? error.Error.ToString() : "connection unavailable");
            });
        }
        catch (Exception)
        {
            inFlightAccounts.Remove(accountKey);
            ScheduleRetry(report, "request unavailable");
        }
    }

    private static bool ReadBoolean(IDictionary<string, object> response, string key, out bool value)
    {
        value = false;
        if (!response.TryGetValue(key, out object raw) || !(raw is bool boolean)) return false;
        value = boolean;
        return true;
    }

    private void ScheduleRetry(MultiplayerResultReport report, string reason)
    {
        report.attempts = Math.Min(100, Math.Max(0, report.attempts) + 1);
        int delay = MultiplayerResultPolicy.RetryDelaySeconds(report.attempts);
        report.nextAttemptUtcTicks = DateTime.UtcNow.AddSeconds(delay).Ticks;
        SaveLedger();
        Debug.LogWarning("[MultiplayerResults] Result remains queued; retry in " + delay + "s (" + reason + ").");
    }

    private void OnApplicationPause(bool paused) { if (paused) SaveLedger(); }
    private void OnApplicationQuit() => SaveLedger();
}
