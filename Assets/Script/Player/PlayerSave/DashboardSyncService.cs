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
    private sealed class DashboardCosmetic
    {
        public string itemId;
        public string name;
    }

    [Serializable]
    private sealed class DashboardCompletion
    {
        public string completedAt;
        public int score;
        public string bridgeTypeId;
        public string achievementId;
        public string completionScreenshotUrl;
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
        public List<DashboardRegion> regions = new List<DashboardRegion>();
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
        public string characterPortraitFile;
        public string characterPortraitUpdatedAt;
        public string characterPortraitChecksum;
        public int totalScore;
        public int bridgesCompleted;
        public int challengesCompleted;
        public int bestSingleBuildScore;
    }

    private static readonly HashSet<string> HeadwearIds = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "EngineeringHardHat", "Accessory_SmallCap", "Accessory_LargeCap"
    };

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
            Time.realtimeSinceStartup < nextAttemptTime ||
            !PlayFabClientAPI.IsClientLoggedIn())
            return;

        Publish(BuildPayload(dataManager, DateTime.UtcNow));
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
        inFlight = true;
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
            { "characterPortraitFile", payload.characterPortraitFile },
            { "characterPortraitUpdatedAt", payload.characterPortraitUpdatedAt },
            { "characterPortraitChecksum", payload.characterPortraitChecksum },
            { "totalScore", payload.totalScore },
            { "bridgesCompleted", payload.bridgesCompleted },
            { "challengesCompleted", payload.challengesCompleted },
            { "bestSingleBuildScore", payload.bestSingleBuildScore }
        };

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
                !response.TryGetValue("accepted", out object accepted) ||
                !(accepted is bool acceptedValue) || !acceptedValue)
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

    private bool IsCurrentSession(int submittedSession, string submittedAccount)
    {
        return this != null && cloudSave != null &&
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

    private static DashboardPayload BuildPayload(PlayerDataManager manager, DateTime synchronizedAt)
    {
        PlayerData data = manager.CurrentData;
        var almanac = BuildAlmanac(manager, synchronizedAt);
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
                if (FindBridge(data, contract.ContractID) != null) storyCompleted++;
            }
        }

        var map = new DashboardMapProgress
        {
            overallPercent = Percent(completedLevels, totalLevels),
            storyPercent = Percent(storyCompleted, storyTotal),
            currentRegion = currentRegion,
            regions = almanac.regions
        };

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
                totalScore += Mathf.Max(0, score);
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
            almanacProgress = JsonUtility.ToJson(almanac),
            characterPortraitFile = data.characterPortraitFileName ?? string.Empty,
            characterPortraitUpdatedAt = data.characterPortraitUpdatedAtUtc ?? string.Empty,
            characterPortraitChecksum = data.characterPortraitChecksum ?? string.Empty,
            totalScore = totalScore,
            bridgesCompleted = Mathf.Max(0, data.lifetimeBridgesBuilt),
            challengesCompleted = Mathf.Max(0, data.lifetimeContractsCompleted),
            bestSingleBuildScore = Mathf.Max(0, bestScore)
        };
    }

    private static DashboardAlmanacProgress BuildAlmanac(
        PlayerDataManager manager, DateTime synchronizedAt)
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
                regionId = MapId(map),
                name = MapName(map)
            };
            int regionCompleted = 0;
            for (int index = 0; index < contracts.Count; index++)
            {
                ContractSO contract = contracts[index];
                SavedBridgeData bridge = FindBridge(data, contract.ContractID);
                bool completed = bridge != null;
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
                    string bridgeType = InferBridgeType(bridge);
                    discoveredTypes.Add(bridgeType);
                    level.engineeringConceptIds = ConceptsForBridge(bridgeType);
                    level.completion = new DashboardCompletion
                    {
                        completedAt = string.IsNullOrWhiteSpace(bridge.completedAtUtc)
                            ? synchronizedAt.ToString("o") : bridge.completedAtUtc,
                        score = bridge.bestStarResult != null
                            ? Mathf.Max(1, bridge.bestStarResult.Stars)
                            : Mathf.Max(1, bridge.bestStars),
                        bridgeTypeId = bridgeType
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
        return result;
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
        loadout.NormalizeAccessories();
        var slots = new List<KeyValuePair<string, DashboardCosmetic>>();
        AddCosmetic(slots, "hair", loadout.hairID);
        AddCosmetic(slots, "top", loadout.shirtID);
        AddCosmetic(slots, "pants", loadout.pantsID);
        AddCosmetic(slots, "shoes", loadout.shoesID);

        foreach (string id in loadout.GetAccessoryIDs())
        {
            if (HeadwearIds.Contains(id)) AddCosmetic(slots, "helmet", id);
            else if (string.Equals(id, "Accessory_SafetyVest",
                         StringComparison.OrdinalIgnoreCase))
                AddCosmetic(slots, "vest", id);
            else AddCosmetic(slots, "accessory", id);
        }

        var json = new StringBuilder("{");
        for (int i = 0; i < slots.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append('"').Append(slots[i].Key).Append("\":")
                .Append(JsonUtility.ToJson(slots[i].Value));
        }
        return json.Append('}').ToString();
    }

    private static void AddCosmetic(List<KeyValuePair<string, DashboardCosmetic>> slots,
        string slot, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) ||
            string.Equals(itemId, "Accessory_None", StringComparison.OrdinalIgnoreCase)) return;
        int existing = slots.FindIndex(pair => pair.Key == slot);
        var item = new DashboardCosmetic
        {
            itemId = itemId.Trim(),
            name = CosmeticName(itemId)
        };
        if (existing >= 0) slots[existing] = new KeyValuePair<string, DashboardCosmetic>(slot, item);
        else slots.Add(new KeyValuePair<string, DashboardCosmetic>(slot, item));
    }

    private static string CosmeticName(string itemId)
    {
        foreach (CosmeticDefinition definition in
                 Resources.FindObjectsOfTypeAll<CosmeticDefinition>())
            if (definition != null && string.Equals(definition.PermanentID, itemId,
                    StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(definition.displayName)
                    ? Humanize(itemId) : definition.displayName.Trim();
        return Humanize(itemId);
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

    private static string MapId(ContractSO.ContractMap map)
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
            payload.almanacProgress, payload.characterPortraitFile,
            payload.characterPortraitUpdatedAt, payload.characterPortraitChecksum,
            payload.totalScore.ToString(),
            payload.bridgesCompleted.ToString(), payload.challengesCompleted.ToString(),
            payload.bestSingleBuildScore.ToString()
        });
    }
}
