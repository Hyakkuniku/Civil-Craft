using System;
using System.Collections.Generic;
using System.Text;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Publishes a small, read-only projection of the account save for the Civil
/// Craft website. The encrypted entity-file save remains the source used by the
/// game; the website never receives bridge geometry, currency, or private save
/// internals.
/// </summary>
[DisallowMultipleComponent]
public sealed class DashboardSyncService : MonoBehaviour
{
    private const string FunctionName = "syncDashboardV1";
    private const float SaveDebounceSeconds = 5f;
    private const float RetrySeconds = 30f;

    [Serializable]
    private sealed class DashboardAchievement
    {
        public string id;
        public string name;
        public string description;
        public bool unlocked;
        public string unlockedAt;
        public int progress;
        public int progressTarget;
    }

    [Serializable]
    private sealed class DashboardCompletion
    {
        public string playerId;
        public string levelId;
        public string regionId;
        public string resultId;
        public string status = "success";
        public string completedAt;
        public int score;
        public bool scoreKnown;
        public string bridgeTypeId;
        public BridgePhotoSnapshot snapshot;
    }

    [Serializable]
    private sealed class DashboardLesson
    {
        public string lessonId;
        public string state = "discovered";
    }

    [Serializable]
    private sealed class DashboardEquipment
    {
        public List<string> accessoryIDs = new List<string>();
        public string hairID = string.Empty;
        public string shirtID = string.Empty;
        public string pantsID = string.Empty;
        public string shoesID = string.Empty;
    }

    [Serializable]
    private sealed class DashboardQuizAttempt
    {
        public string quizId;
        public string attemptId;
        public string title;
        public string status = "completed";
        public int score;
        public int totalQuestions;
        public string completedAt;
    }

    [Serializable]
    private sealed class DashboardQuizReview
    {
        public string quizId;
        public string attemptId;
        public string contentFingerprint;
        public List<int> selectedIndices;
    }

    [Serializable]
    private sealed class DashboardLevel
    {
        public string levelId;
        public int order;
        public string levelName;
        public string status;
        public List<string> engineeringConceptIds = new List<string>();
        public DashboardCompletion completion;
    }

    [Serializable]
    private sealed class DashboardRegion
    {
        public string regionId;
        public string name;
        public string status;
        public List<DashboardLevel> levels = new List<DashboardLevel>();
    }

    [Serializable]
    private sealed class DashboardMapProgress
    {
        public int overallPercent;
        public int storyPercent;
        public string currentRegion;
        public List<DashboardMapRegion> regions = new List<DashboardMapRegion>();
    }

    [Serializable]
    private sealed class DashboardMapRegion
    {
        public string id;
        public string name;
        public bool completed;
        public int missionsCompleted;
        public int missionsTotal;
    }

    [Serializable]
    private sealed class DashboardAlmanacProgress
    {
        public List<DashboardRegion> regions = new List<DashboardRegion>();
        public int levelsCompleted;
        public int levelsTotal;
        public int journeyPercent;
        public List<string> discoveredBridgeTypeIds = new List<string>();
        public List<string> discoveredMaterials = new List<string>();
        public List<DashboardLesson> lessonRecords = new List<DashboardLesson>();
        public List<DashboardQuizAttempt> quizAttempts = new List<DashboardQuizAttempt>();
    }

    private sealed class DashboardPayload
    {
        public int schemaVersion;
        public int currentLevel;
        public int xp;
        public int xpToNextLevel;
        public string currentRegion;
        public int achievementsUnlocked;
        public int achievementsTotal;
        public string mapProgress;
        public string achievementProgress;
        public string equippedCosmetics;
        public string almanacProgress;
        public string quizReviewProgress;
        public string characterPortraitFile;
        public string characterPortraitUpdatedAt;
        public string characterPortraitChecksum;
        public int totalScore;
        public int bridgesCompleted;
        public int challengesCompleted;
        public int bestSingleBuildScore;
    }

    private PlayerDataManager dataManager;
    private CloudSaveManager cloudSave;
    private bool dirty = true;
    private bool inFlight;
    private float nextAttemptTime;
    private int revision;
    private string lastPublishedSignature;
    private int observedSessionGeneration = int.MinValue;
    private string observedAccountId;

    public bool IsSyncing => inFlight;
    public string LastSyncError { get; private set; }

    private void Awake()
    {
        dataManager = GetComponent<PlayerDataManager>();
        cloudSave = GetComponent<CloudSaveManager>();
        if (dataManager != null)
            dataManager.OnSaveCommitted += RequestSync;
        nextAttemptTime = Time.realtimeSinceStartup;
    }

    private void OnDestroy()
    {
        if (dataManager != null)
            dataManager.OnSaveCommitted -= RequestSync;
    }

    public void RequestSync()
    {
        dirty = true;
        revision++;
        nextAttemptTime = Time.realtimeSinceStartup + SaveDebounceSeconds;
    }

    private void Update()
    {
        ObserveAccountSession();
        if (!dirty || inFlight || dataManager == null || dataManager.CurrentData == null ||
            cloudSave == null || !cloudSave.CanPublishDashboard ||
            !string.Equals(dataManager.ActiveAccountId, cloudSave.ActiveAccountId, StringComparison.Ordinal) ||
            Time.realtimeSinceStartup < nextAttemptTime ||
            !PlayFabClientAPI.IsClientLoggedIn())
            return;

        try { Publish(BuildPayload(dataManager)); }
        catch (Exception)
        {
            // Never log projection bodies, tickets or provider payloads.
            inFlight = false;
            ScheduleRetry("The saved dashboard projection could not be serialized safely.");
        }
    }

    private void ObserveAccountSession()
    {
        if (cloudSave == null ||
            (observedSessionGeneration == cloudSave.SessionGeneration &&
             string.Equals(observedAccountId, cloudSave.ActiveAccountId,
                 StringComparison.Ordinal))) return;

        observedSessionGeneration = cloudSave.SessionGeneration;
        observedAccountId = cloudSave.ActiveAccountId;
        // The same progress still needs publishing when a different account or
        // a new login session selects it. Previous callbacks must not mark this
        // session clean or interfere with its next request.
        lastPublishedSignature = null;
        LastSyncError = null;
        inFlight = false;
        dirty = true;
        revision++;
        nextAttemptTime = Time.realtimeSinceStartup;
    }

    private void Publish(DashboardPayload payload)
    {
        string signature = PayloadSignature(payload);
        if (signature == lastPublishedSignature)
        {
            dirty = false;
            return;
        }

        int submittedRevision = revision;
        int submittedSession = cloudSave.SessionGeneration;
        string submittedAccount = cloudSave.ActiveAccountId;
        var parameters = new Dictionary<string, object>
        {
            { "schemaVersion", payload.schemaVersion },
            { "currentLevel", payload.currentLevel },
            { "xp", payload.xp },
            { "xpToNextLevel", payload.xpToNextLevel },
            { "currentRegion", payload.currentRegion },
            { "achievementsUnlocked", payload.achievementsUnlocked },
            { "achievementsTotal", payload.achievementsTotal },
            { "mapProgress", payload.mapProgress },
            { "achievementProgress", payload.achievementProgress },
            { "equippedCosmetics", payload.equippedCosmetics },
            { "almanacProgress", payload.almanacProgress },
            { "quizReviewProgress", payload.quizReviewProgress },
            { "characterPortraitFile", payload.characterPortraitFile },
            { "characterPortraitUpdatedAt", payload.characterPortraitUpdatedAt },
            { "characterPortraitChecksum", payload.characterPortraitChecksum },
            { "totalScore", payload.totalScore },
            { "bridgesCompleted", payload.bridgesCompleted },
            { "challengesCompleted", payload.challengesCompleted },
            { "bestSingleBuildScore", payload.bestSingleBuildScore }
        };

        if (!ProjectionFits(payload, parameters))
        {
            ScheduleRetry("The dashboard projection exceeds its supported size. No records were truncated or sent.");
            return;
        }
        inFlight = true;

        PlayFabClientAPI.ExecuteCloudScript(new ExecuteCloudScriptRequest
        {
            FunctionName = FunctionName,
            FunctionParameter = parameters,
            GeneratePlayStreamEvent = false
        }, result =>
        {
            if (!IsCurrentSession(submittedSession, submittedAccount)) return;
            inFlight = false;
            if (result == null || result.Error != null)
            {
                ScheduleRetry(DescribeCloudScriptFailure(result));
                return;
            }
            if (!(result.FunctionResult is IDictionary<string, object> response) ||
                !GameDashboardRecords.Acknowledged(response) ||
                (payload.quizReviewProgress != null && !GameDashboardRecords.AcknowledgedQuizReview(response)))
            {
                ScheduleRetry("CloudScript did not acknowledge the dashboard projection " +
                    "(revision " + result.Revision + ").");
                return;
            }

            lastPublishedSignature = signature;
            LastSyncError = null;
            dirty = revision != submittedRevision;
            nextAttemptTime = Time.realtimeSinceStartup + SaveDebounceSeconds;
            Debug.Log("[DashboardSync] Player dashboard projection published.", this);
        }, error =>
        {
            if (!IsCurrentSession(submittedSession, submittedAccount)) return;
            inFlight = false;
            // Error identifiers are sufficient to diagnose gateway failures
            // without printing request bodies, session tickets, or URLs.
            ScheduleRetry("PlayFab request failed: " + error.Error + ".");
        });
    }

    private static bool ProjectionFits(DashboardPayload payload, Dictionary<string, object> parameters)
    {
        foreach (string projection in new[] { payload.mapProgress, payload.achievementProgress,
            payload.equippedCosmetics, payload.almanacProgress, payload.quizReviewProgress })
            if (projection == null || projection.Length > 10000) return false;
        var serializer = PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer);
        string encoded = serializer.SerializeObject(parameters);
        return encoded != null && Encoding.UTF8.GetByteCount(encoded) <= 30000;
    }

    private bool IsCurrentSession(int submittedSession, string submittedAccount)
    {
        return this != null && cloudSave != null && dataManager != null &&
            string.Equals(dataManager.ActiveAccountId, submittedAccount, StringComparison.Ordinal) &&
            submittedSession == cloudSave.SessionGeneration &&
            string.Equals(submittedAccount, cloudSave.ActiveAccountId,
                StringComparison.Ordinal);
    }

    private static string DescribeCloudScriptFailure(ExecuteCloudScriptResult result)
    {
        if (result == null) return "PlayFab returned no dashboard result.";
        var message = new StringBuilder("CloudScript revision ")
            .Append(result.Revision).Append(" failed: ")
            .Append(DiagnosticIdentifier(result.Error?.Error));
        if (result.Logs != null)
        {
            foreach (LogStatement entry in result.Logs)
            {
                if (entry == null || !string.Equals(entry.Level, "Error",
                        StringComparison.OrdinalIgnoreCase) ||
                    !(entry.Data is IDictionary<string, object> details)) continue;

                // PlayFab API error logs can contain the entire request. Read
                // only API/error identifiers and the numerical error code.
                if (DiagnosticMember(details, "apiErrorInfo") is
                    IDictionary<string, object> apiErrorInfo) details = apiErrorInfo;
                object api = DiagnosticMember(details, "api") ??
                    DiagnosticMember(details, "apiName");
                object failure = DiagnosticMember(details, "apiError") ??
                    DiagnosticMember(details, "error");
                var apiError = failure as IDictionary<string, object>;
                object errorName = apiError != null
                    ? DiagnosticMember(apiError, "error") : failure;
                object errorCode = DiagnosticMember(apiError ?? details, "errorCode");
                string apiName = DiagnosticIdentifier(api);
                string apiErrorName = DiagnosticIdentifier(errorName);
                bool hasErrorCode = errorCode is byte || errorCode is short ||
                    errorCode is int || errorCode is long || errorCode is float ||
                    errorCode is double || errorCode is decimal;
                if (apiName.Length == 0 && apiErrorName.Length == 0 && !hasErrorCode) continue;
                message.Append("; API ").Append(apiName.Length > 0 ? apiName : "unknown")
                    .Append(": ").Append(apiErrorName.Length > 0 ? apiErrorName : "error");
                if (hasErrorCode)
                    message.Append(" (").Append(errorCode).Append(')');
            }
        }
        return message.Append('.').ToString();
    }

    private static object DiagnosticMember(IDictionary<string, object> data, string key)
    {
        if (data == null) return null;
        foreach (KeyValuePair<string, object> item in data)
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                return item.Value;
        return null;
    }

    private static string DiagnosticIdentifier(object value)
    {
        if (!(value is string text) || text.Length == 0 || text.Length > 96)
            return string.Empty;
        foreach (char character in text)
            if (!char.IsLetterOrDigit(character) && character != '_' &&
                character != '/' && character != '-' && character != '.')
                return string.Empty;
        return text;
    }

    private void ScheduleRetry(string message)
    {
        LastSyncError = message;
        dirty = true;
        nextAttemptTime = Time.realtimeSinceStartup + RetrySeconds;
        Debug.LogWarning("[DashboardSync] " + message + " Sync will retry.", this);
    }

    private static DashboardPayload BuildPayload(PlayerDataManager manager)
    {
        PlayerData data = manager.CurrentData;
        List<QuizAggregateResult> quizResults = LatestQuizResults(data);
        var almanac = BuildAlmanacProjection(manager, quizResults);
        string currentRegion = ResolveCurrentRegion(data, almanac.regions);
        int totalLevels = almanac.levelsTotal;
        int completedLevels = almanac.levelsCompleted;
        int storyTotal = 0;
        int storyCompleted = 0;

        if (manager.allGameContracts != null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContractSO contract in manager.allGameContracts)
            {
                if (!IsDashboardContract(contract) || !contract.isStoryModeContract ||
                    !seen.Add(contract.ContractID))
                    continue;
                storyTotal++;
                if (manager.HasContractCompletionRecord(contract.ContractID)) storyCompleted++;
            }
        }

        var map = new DashboardMapProgress
        {
            overallPercent = Percent(completedLevels, totalLevels),
            storyPercent = Percent(storyCompleted, storyTotal),
            currentRegion = currentRegion,
        };
        foreach (DashboardRegion region in almanac.regions)
            map.regions.Add(new DashboardMapRegion { id = region.regionId, name = region.name,
                completed = region.status == "completed", missionsTotal = region.levels.Count,
                missionsCompleted = region.levels.FindAll(level => level.status == "completed").Count });

        int totalScore = 0;
        int bestScore = 0;
        if (data.savedBridges != null)
        {
            foreach (SavedBridgeData bridge in data.savedBridges)
            {
                if (bridge == null) continue;
                int score = bridge.bestStarResult != null
                    ? bridge.bestStarResult.Stars
                    : Mathf.Max(bridge.bestStars, 1);
                totalScore = checked(totalScore + Mathf.Max(0, score));
                bestScore = Mathf.Max(bestScore, score);
            }
        }

        int nextThreshold = NextLevelThreshold(data.exp);
        string achievementProgress = BuildAchievementJson(
            manager, out int achievementsUnlocked, out int achievementsTotal);
        return new DashboardPayload
        {
            schemaVersion = 1,
            currentLevel = CurrentLevel(data.exp),
            xp = Mathf.Max(0, data.exp),
            xpToNextLevel = nextThreshold > 0 ? nextThreshold : Mathf.Max(1, data.exp),
            currentRegion = currentRegion,
            achievementsUnlocked = achievementsUnlocked,
            achievementsTotal = achievementsTotal,
            mapProgress = JsonUtility.ToJson(map),
            achievementProgress = achievementProgress,
            equippedCosmetics = BuildCosmeticsJson(data.cosmeticLoadout),
            almanacProgress = AlmanacJson(almanac),
            quizReviewProgress = BuildQuizReviewJson(data, quizResults),
            characterPortraitFile = data.characterPortraitFileName ?? string.Empty,
            characterPortraitUpdatedAt = data.characterPortraitUpdatedAtUtc ?? string.Empty,
            characterPortraitChecksum = data.characterPortraitChecksum ?? string.Empty,
            totalScore = totalScore,
            bridgesCompleted = Mathf.Max(0, data.lifetimeBridgesBuilt),
            challengesCompleted = Mathf.Max(0, data.lifetimeContractsCompleted),
            bestSingleBuildScore = Mathf.Max(0, bestScore)
        };
    }

    private static DashboardAlmanacProgress BuildAlmanac(PlayerDataManager manager)
    {
        return BuildAlmanacProjection(manager, LatestQuizResults(manager.CurrentData));
    }

    private static DashboardAlmanacProgress BuildAlmanacProjection(
        PlayerDataManager manager, List<QuizAggregateResult> quizResults)
    {
        PlayerData data = manager.CurrentData;
        var result = new DashboardAlmanacProgress();
        var discoveredTypes = new HashSet<string>(StringComparer.Ordinal);
        bool currentAssigned = false;

        foreach (ContractSO.ContractMap map in Enum.GetValues(typeof(ContractSO.ContractMap)))
        {
            var contracts = new List<ContractSO>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (manager.allGameContracts != null)
            {
                foreach (ContractSO contract in manager.allGameContracts)
                    if (IsDashboardContract(contract) && contract.contractMap == map &&
                        seen.Add(contract.ContractID))
                        contracts.Add(contract);
            }
            contracts.Sort((left, right) =>
                string.Compare(left.ContractID, right.ContractID,
                    StringComparison.OrdinalIgnoreCase));
            if (contracts.Count == 0) continue;

            var region = new DashboardRegion
            {
                regionId = RegionId(map),
                name = MapName(map)
            };
            int regionCompleted = 0;
            for (int index = 0; index < contracts.Count; index++)
            {
                ContractSO contract = contracts[index];
                SavedBridgeData bridge = FindBridge(data, contract.ContractID);
                bool completed = manager.HasContractCompletionRecord(contract.ContractID);
                bool current = !completed && !currentAssigned &&
                    (MatchesActiveObjective(data, contract) || !HasLaterCurrentCandidate(data, contracts, index));
                if (current) currentAssigned = true;

                var level = new DashboardLevel
                {
                    levelId = contract.ContractID,
                    order = index + 1,
                    levelName = string.IsNullOrWhiteSpace(contract.name)
                        ? Humanize(contract.ContractID) : contract.name,
                    status = completed ? "completed" : current ? "current" : "locked"
                };
                if (completed)
                {
                    regionCompleted++;
                    result.levelsCompleted++;
                    string bridgeType = bridge != null ? InferBridgeType(bridge) : string.Empty;
                    if (bridgeType.Length > 0) discoveredTypes.Add(bridgeType);
                    level.engineeringConceptIds = bridgeType.Length > 0 ? ConceptsForBridge(bridgeType) : new List<string>();
                    bool scoreKnown = bridge?.latestStarResult != null && bridge.latestStarResult.completed;
                    level.completion = new DashboardCompletion
                    {
                        playerId = manager.ActiveAccountId,
                        levelId = contract.ContractID,
                        regionId = region.regionId,
                        resultId = bridge?.resultId ?? string.Empty,
                        completedAt = CompletionDate(data, contract.ContractID),
                        score = scoreKnown ? bridge.latestStarResult.Stars : 0,
                        scoreKnown = scoreKnown,
                        bridgeTypeId = bridgeType,
                        snapshot = bridge != null && GameDashboardRecords.ValidSnapshot(bridge.snapshot,
                            contract.ContractID, bridge.resultId) && bridge.snapshot.regionId == region.regionId
                            ? bridge.snapshot : null
                    };
                }
                region.levels.Add(level);
                result.levelsTotal++;
            }

            region.status = regionCompleted >= contracts.Count
                ? "completed"
                : region.levels.Exists(level => level.status == "current") || regionCompleted > 0
                    ? "current" : "locked";
            result.regions.Add(region);
        }

        result.journeyPercent = Percent(result.levelsCompleted, result.levelsTotal);
        result.discoveredBridgeTypeIds.AddRange(discoveredTypes);
        result.discoveredBridgeTypeIds.Sort(StringComparer.Ordinal);
        result.discoveredMaterials = MapDiscoveredMaterials(data.discoveredMaterialIds);
        if (data.unlockedLessonIds != null)
        {
            var lessons = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string id in data.unlockedLessonIds)
                if (!string.IsNullOrWhiteSpace(id)) lessons.Add(id.Trim());
            foreach (string id in lessons) result.lessonRecords.Add(new DashboardLesson { lessonId = id });
        }
        result.quizAttempts = BuildQuizResults(quizResults);
        return result;
    }

    private static string AlmanacJson(DashboardAlmanacProgress almanac)
    {
        // Omit optional provenance/review fields, not records. Repeating the
        // entire media tree in MapProgress or null fields for every completion
        // exceeds the established 10k nested/30k argument limits today.
        var regions = new List<object>();
        foreach (DashboardRegion region in almanac.regions)
        {
            var levels = new List<object>();
            foreach (DashboardLevel level in region.levels)
            {
                var row = new Dictionary<string, object> { { "levelId", level.levelId }, { "order", level.order },
                    { "levelName", level.levelName }, { "status", level.status },
                    { "engineeringConceptIds", level.engineeringConceptIds } };
                DashboardCompletion c = level.completion;
                if (c != null)
                {
                    var completion = new Dictionary<string, object> {
                        { "status", c.status }, { "bridgeTypeId", c.bridgeTypeId }, { "snapshot", null } };
                    // The projection is private and the endpoint authenticates
                    // its owner. playerId and the owning level/region IDs are
                    // optional; stable parent IDs and strict snapshot IDs bind
                    // every completion/image without repeating that ID twice.
                    if (!string.IsNullOrEmpty(c.resultId)) completion["resultId"] = c.resultId;
                    if (!string.IsNullOrEmpty(c.completedAt)) completion["completedAt"] = c.completedAt;
                    if (c.scoreKnown) completion["score"] = c.score;
                    else completion["scoreKnown"] = false;
                    if (c.snapshot != null)
                        completion["snapshot"] = new Dictionary<string, object> {
                            { "storage", c.snapshot.storage }, { "fileName", c.snapshot.fileName },
                            { "contractId", c.snapshot.contractId }, { "regionId", c.snapshot.regionId },
                            { "resultId", c.snapshot.resultId }, { "entryName", c.snapshot.entryName },
                            { "sha256", c.snapshot.sha256 }
                        };
                    row["completion"] = completion;
                }
                levels.Add(row);
            }
            regions.Add(new Dictionary<string, object> { { "regionId", region.regionId }, { "name", region.name },
                { "status", region.status }, { "levels", levels } });
        }
        var projection = new Dictionary<string, object> {
            { "publisherVersion", 2 }, { "regions", regions }, { "levelsCompleted", almanac.levelsCompleted }, { "levelsTotal", almanac.levelsTotal },
            { "journeyPercent", almanac.journeyPercent }, { "discoveredBridgeTypeIds", almanac.discoveredBridgeTypeIds },
            { "discoveredMaterials", almanac.discoveredMaterials }, { "lessonRecords", almanac.lessonRecords },
            { "quizAttempts", almanac.quizAttempts }
        };
        return PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer).SerializeObject(projection);
    }

    private static string CompletionDate(PlayerData data, string contractId)
    {
        ContractCompletionRecord matching = null;
        if (data.contractCompletionRecords != null)
            foreach (ContractCompletionRecord record in data.contractCompletionRecords)
                if (record != null && record.contractId == contractId)
                {
                    if (matching != null) return string.Empty;
                    matching = record;
                }
        return matching?.completedAtUtc ?? string.Empty;
    }

    private static List<QuizAggregateResult> LatestQuizResults(PlayerData data)
    {
        var latest = new Dictionary<string, QuizAggregateResult>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (data.quizResults != null)
        {
            foreach (QuizAggregateResult result in data.quizResults)
                if (result != null && !string.IsNullOrWhiteSpace(result.quizId))
                    counts[result.quizId] = counts.TryGetValue(result.quizId, out int count) ? count + 1 : 1;
            foreach (QuizAggregateResult result in data.quizResults)
                if (GameDashboardRecords.ValidQuizResult(result) && counts[result.quizId] == 1)
                    latest[result.quizId] = result;
        }
        var progressCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (data.quizProgress != null)
            foreach (QuizProgressSaveData progress in data.quizProgress)
                if (progress != null && progress.submitted && !string.IsNullOrWhiteSpace(progress.quizId))
                    progressCounts[progress.quizId] = progressCounts.TryGetValue(progress.quizId, out int count) ? count + 1 : 1;
        QuizData[] definitions = Resources.FindObjectsOfTypeAll<QuizData>();
        // Legacy records contain no timestamp/result snapshot. Only a matching,
        // fully retained definition can establish their real aggregate score.
        if (data.quizProgress != null)
            foreach (QuizProgressSaveData progress in data.quizProgress)
            {
                if (progress == null || !progress.submitted || string.IsNullOrWhiteSpace(progress.quizId) ||
                    latest.ContainsKey(progress.quizId) || counts.ContainsKey(progress.quizId) ||
                    progressCounts[progress.quizId] != 1) continue;
                QuizData match = null;
                bool ambiguous = false;
                foreach (QuizData quiz in definitions)
                    if (quiz != null && quiz.quizId?.Trim() == progress.quizId &&
                        quiz.ContentFingerprint == progress.contentFingerprint)
                    {
                        if (match != null) { ambiguous = true; break; }
                        match = quiz;
                    }
                if (!ambiguous && GameDashboardRecords.TryQuizResult(match, progress,
                    GameDashboardRecords.LegacyQuizResultId(progress), string.Empty, out QuizAggregateResult aggregate))
                    latest[aggregate.quizId] = aggregate;
            }
        var ordered = new List<QuizAggregateResult>(latest.Values);
        ordered.Sort((left, right) => string.Compare(left.quizId, right.quizId, StringComparison.Ordinal));
        return ordered;
    }

    private static List<DashboardQuizAttempt> BuildQuizResults(List<QuizAggregateResult> ordered)
    {
        var projection = new List<DashboardQuizAttempt>();
        foreach (QuizAggregateResult result in ordered)
            projection.Add(new DashboardQuizAttempt { quizId = result.quizId, attemptId = result.attemptId,
                title = result.title, score = result.score, totalQuestions = result.totalQuestions,
                completedAt = result.completedAt ?? string.Empty });
        return projection;
    }

    private static string BuildQuizReviewJson(PlayerData data, List<QuizAggregateResult> results)
    {
        var reviews = new List<DashboardQuizReview>();
        QuizData[] definitions = Resources.FindObjectsOfTypeAll<QuizData>();
        foreach (QuizAggregateResult result in results)
        {
            QuizAggregateResult review = result;
            // New submissions retain the compact canonical answers with the
            // aggregate. Older saves may recover only their exact retained
            // context and quiz version, never a different/later attempt.
            if (result.selectedIndices == null)
            {
                QuizProgressSaveData progress = null;
                bool ambiguousProgress = false;
                if (data.quizProgress != null)
                    foreach (QuizProgressSaveData candidate in data.quizProgress)
                        if (candidate != null && candidate.submitted &&
                            candidate.quizId == result.quizId && candidate.progressKey == result.progressKey &&
                            candidate.contentFingerprint == result.contentFingerprint)
                        {
                            if (progress != null) { ambiguousProgress = true; break; }
                            progress = candidate;
                        }
                if (progress == null || ambiguousProgress) continue;
                QuizData definition = null;
                bool ambiguousDefinition = false;
                foreach (QuizData candidate in definitions)
                    if (candidate != null && candidate.quizId?.Trim() == result.quizId &&
                        candidate.ContentFingerprint == result.contentFingerprint)
                    {
                        if (definition != null) { ambiguousDefinition = true; break; }
                        definition = candidate;
                    }
                if (ambiguousDefinition || !GameDashboardRecords.TryQuizResult(definition, progress,
                    result.attemptId, result.completedAt, out review) ||
                    review.title != result.title || review.score != result.score ||
                    review.totalQuestions != result.totalQuestions) continue;
            }
            if (!GameDashboardRecords.ValidQuizReview(review)) continue;
            reviews.Add(new DashboardQuizReview {
                quizId = result.quizId, attemptId = result.attemptId,
                contentFingerprint = review.contentFingerprint,
                selectedIndices = new List<int>(review.selectedIndices)
            });
        }
        return PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer)
            .SerializeObject(new Dictionary<string, object> { { "schemaVersion", 1 }, { "reviews", reviews } });
    }

    private static bool HasLaterCurrentCandidate(
        PlayerData data, List<ContractSO> contracts, int currentIndex)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.activeObjectiveKey)) return false;
        for (int i = currentIndex + 1; i < contracts.Count; i++)
            if (contracts[i] != null && contracts[i].MatchesIdentifier(data.activeObjectiveKey))
                return true;
        return false;
    }

    private static bool MatchesActiveObjective(PlayerData data, ContractSO contract)
    {
        return data != null && contract != null &&
               !string.IsNullOrWhiteSpace(data.activeObjectiveKey) &&
               contract.MatchesIdentifier(data.activeObjectiveKey);
    }

    private static bool IsDashboardContract(ContractSO contract)
    {
        return contract != null && !contract.hideFromLeaderboard &&
               !string.IsNullOrWhiteSpace(contract.ContractID);
    }

    private static SavedBridgeData FindBridge(PlayerData data, string contractId)
    {
        if (data?.savedBridges == null || string.IsNullOrWhiteSpace(contractId)) return null;
        return data.savedBridges.Find(bridge => bridge != null &&
            string.Equals(bridge.contractId?.Trim(), contractId.Trim(),
                StringComparison.Ordinal));
    }

    private static string BuildAchievementJson(
        PlayerDataManager manager, out int unlockedCount, out int totalCount)
    {
        var achievements = new List<DashboardAchievement>();
        PlayerData data = manager.CurrentData;
        unlockedCount = 0;
        totalCount = 0;
        if (manager.allGameAchievements != null)
        {
            var sorted = new List<AchievementSO>(manager.allGameAchievements);
            sorted.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.achievementID));
            sorted.Sort((left, right) => string.Compare(left.achievementID,
                right.achievementID, StringComparison.OrdinalIgnoreCase));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AchievementSO achievement in sorted)
            {
                if (!seen.Add(achievement.achievementID)) continue;
                bool unlocked = data.unlockedAchievements != null &&
                                data.unlockedAchievements.Contains(achievement.achievementID);
                totalCount++;
                if (unlocked) unlockedCount++;
                int target = Mathf.Max(0, manager.GetAchievementTarget(achievement));
                int progress = Mathf.Max(0, manager.GetAchievementProgress(achievement));
                if (target > 0) progress = Mathf.Min(progress, target);
                achievements.Add(new DashboardAchievement
                {
                    id = achievement.achievementID,
                    name = achievement.hideDetailsUntilUnlocked && !unlocked
                        ? "???" : achievement.achievementName,
                    description = achievement.hideDetailsUntilUnlocked && !unlocked
                        ? "Complete more Civil Craft projects to reveal this achievement."
                        : achievement.description,
                    unlocked = unlocked,
                    unlockedAt = unlocked ? FindAchievementTimestamp(data,
                        achievement.achievementID) : string.Empty,
                    progress = progress,
                    progressTarget = target
                });
            }
        }
        return ToJsonArray(achievements);
    }

    private static string FindAchievementTimestamp(PlayerData data, string achievementId)
    {
        if (data?.achievementUnlocks == null) return string.Empty;
        AchievementUnlockData record = data.achievementUnlocks.Find(item => item != null &&
            string.Equals(item.achievementId, achievementId, StringComparison.Ordinal));
        return record?.unlockedAtUtc ?? string.Empty;
    }

    private static string BuildCosmeticsJson(CosmeticLoadoutData loadout)
    {
        if (loadout == null) return "{}";
        // Never normalize/mutate the live save while reading it for a website.
        CosmeticLoadoutData copy = loadout.Clone();
        var equipment = new DashboardEquipment {
            hairID = copy.hairID ?? string.Empty, shirtID = copy.shirtID ?? string.Empty,
            pantsID = copy.pantsID ?? string.Empty, shoesID = copy.shoesID ?? string.Empty
        };
        foreach (string id in copy.GetAccessoryIDs()) equipment.accessoryIDs.Add(id);
        return JsonUtility.ToJson(equipment);
    }

    private static List<string> MapDiscoveredMaterials(List<string> materialIds)
    {
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        if (materialIds != null)
            foreach (string id in materialIds)
            {
                string dashboardId = MapMaterial(id);
                if (!string.IsNullOrEmpty(dashboardId)) mapped.Add(dashboardId);
            }
        var result = new List<string>(mapped);
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string MapMaterial(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        switch (id.Trim().ToLowerInvariant())
        {
            case "beam": return "wood_beam";
            case "pier": return "wood_support";
            case "ropematerial":
            case "rope": return "rope";
            case "wood road": return "wood_road";
            case "concrete road": return "concrete_road";
            case "concretebeam": return "concrete_member";
            default: return string.Empty;
        }
    }

    private static string InferBridgeType(SavedBridgeData bridge)
    {
        if (bridge?.bars != null)
            foreach (SavedBarData bar in bridge.bars)
                if (bar != null && !string.IsNullOrWhiteSpace(bar.materialName) &&
                    (bar.materialName.IndexOf("rope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     bar.materialName.IndexOf("cable", StringComparison.OrdinalIgnoreCase) >= 0))
                    return "suspension";

        int bars = bridge?.bars?.Count ?? 0;
        int points = bridge?.points?.Count ?? 0;
        return bars >= points + 1 ? "truss" : "beam";
    }

    private static List<string> ConceptsForBridge(string bridgeType)
    {
        switch (bridgeType)
        {
            case "suspension":
                return new List<string> { "tension", "compression", "load-distribution" };
            case "truss":
                return new List<string> { "tension", "compression", "load-distribution", "stability" };
            case "arch":
                return new List<string> { "compression", "load-distribution", "support" };
            default:
                return new List<string> { "load", "bending", "support" };
        }
    }

    private static string ResolveCurrentRegion(PlayerData data, List<DashboardRegion> regions)
    {
        if (regions != null)
        {
            DashboardRegion current = regions.Find(region => region.status == "current");
            if (current != null) return current.name;
            DashboardRegion last = regions.FindLast(region => region.status == "completed");
            if (last != null) return last.name;
        }
        return string.IsNullOrWhiteSpace(data?.lastSavedScene)
            ? string.Empty : Humanize(data.lastSavedScene);
    }

    private static int CurrentLevel(int xp)
    {
        if (xp < 100) return 1;
        if (xp < 300) return 2;
        if (xp < 600) return 3;
        if (xp < 1000) return 4;
        return 5;
    }

    private static int NextLevelThreshold(int xp)
    {
        if (xp < 100) return 100;
        if (xp < 300) return 300;
        if (xp < 600) return 600;
        if (xp < 1000) return 1000;
        return -1;
    }

    private static int Percent(int completed, int total)
    {
        return total <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(completed * 100f / total), 0, 100);
    }

    public static string RegionId(ContractSO.ContractMap map)
    {
        switch (map)
        {
            case ContractSO.ContractMap.CanyonCrossing: return "canyon-crossing";
            case ContractSO.ContractMap.TownRiver: return "town-river";
            case ContractSO.ContractMap.IndustrialZone: return "industrial-zone";
            default: return map.ToString().ToLowerInvariant();
        }
    }

    private static string MapName(ContractSO.ContractMap map)
    {
        switch (map)
        {
            case ContractSO.ContractMap.CanyonCrossing: return "Canyon Crossing";
            case ContractSO.ContractMap.TownRiver: return "Town River";
            case ContractSO.ContractMap.IndustrialZone: return "Industrial Zone";
            default: return Humanize(map.ToString());
        }
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string source = value.Trim().Replace('_', ' ').Replace('-', ' ');
        var result = new StringBuilder();
        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];
            if (i > 0 && char.IsUpper(current) &&
                char.IsLetterOrDigit(source[i - 1]) && !char.IsUpper(source[i - 1]))
                result.Append(' ');
            result.Append(current);
        }
        return result.ToString().Trim();
    }

    private static string ToJsonArray<T>(List<T> items)
    {
        var json = new StringBuilder("[");
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append(JsonUtility.ToJson(items[i]));
        }
        return json.Append(']').ToString();
    }

    private static string PayloadSignature(DashboardPayload payload)
    {
        return string.Join("\n", new[]
        {
            payload.schemaVersion.ToString(), payload.currentLevel.ToString(),
            payload.xp.ToString(), payload.xpToNextLevel.ToString(), payload.currentRegion,
            payload.achievementsUnlocked.ToString(), payload.achievementsTotal.ToString(),
            payload.mapProgress, payload.achievementProgress, payload.equippedCosmetics,
            payload.almanacProgress, payload.quizReviewProgress, payload.characterPortraitFile,
            payload.characterPortraitUpdatedAt, payload.characterPortraitChecksum,
            payload.totalScore.ToString(),
            payload.bridgesCompleted.ToString(), payload.challengesCompleted.ToString(),
            payload.bestSingleBuildScore.ToString()
        });
    }
}
