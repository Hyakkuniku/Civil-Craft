// Civil Craft dashboard projection publisher for legacy PlayFab CloudScript.
// Merge this handler into the active title revision. The Unity client calls the
// function with its signed-in session; PlayFab supplies currentPlayerId and the
// Server API writes the website-readable projection. No developer secret is
// present in the game or browser.

var ccDashboardMaxJsonLength = 10000;
var ccDashboardMaxDataUpdates = 10;

// The title allows at most ten player-data updates in one API request.
// Keep the existing website keys, but send bounded additive batches.
function ccDashboardWritePrivateData(data) {
    var keys = Object.keys(data);
    for (var offset = 0; offset < keys.length; offset += ccDashboardMaxDataUpdates) {
        var batch = {};
        for (var index = offset;
            index < keys.length && index < offset + ccDashboardMaxDataUpdates; index++)
            batch[keys[index]] = data[keys[index]];
        server.UpdateUserData({
            PlayFabId: currentPlayerId,
            Data: batch,
            Permission: "Private"
        });
    }
}

function ccDashboardInteger(value, name, minimum, maximum) {
    if (typeof value !== "number" || !isFinite(value) ||
        Math.floor(value) !== value || value < minimum || value > maximum)
        throw new Error("Invalid dashboard field: " + name + ".");
    return value;
}

function ccDashboardText(value, name, maximumLength) {
    if (typeof value !== "string" || value.length > maximumLength)
        throw new Error("Invalid dashboard field: " + name + ".");
    return value;
}

function ccDashboardOptionalText(value, name, maximumLength) {
    if (value === undefined || value === null) return "";
    return ccDashboardText(value, name, maximumLength);
}

function ccDashboardJson(value, name, expectedArray) {
    ccDashboardText(value, name, ccDashboardMaxJsonLength);
    var parsed;
    try { parsed = JSON.parse(value); }
    catch (error) { throw new Error("Invalid dashboard JSON: " + name + "."); }
    if (expectedArray) {
        if (!Array.isArray(parsed))
            throw new Error("Dashboard field must be an array: " + name + ".");
    } else if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
        throw new Error("Dashboard field must be an object: " + name + ".");
    }
    return value;
}

handlers.syncDashboardV1 = function (args, context) {
    if (typeof currentPlayerId !== "string" || !currentPlayerId)
        throw new Error("A signed-in player is required.");
    if (!args || ccDashboardInteger(args.schemaVersion, "schemaVersion", 1, 1) !== 1)
        throw new Error("Unsupported dashboard schema.");

    var currentLevel = ccDashboardInteger(args.currentLevel, "currentLevel", 1, 1000);
    var xp = ccDashboardInteger(args.xp, "xp", 0, 2147483647);
    var xpToNextLevel = ccDashboardInteger(
        args.xpToNextLevel, "xpToNextLevel", 1, 2147483647);
    var achievementsUnlocked = ccDashboardInteger(
        args.achievementsUnlocked, "achievementsUnlocked", 0, 10000);
    var achievementsTotal = ccDashboardInteger(
        args.achievementsTotal, "achievementsTotal", 0, 10000);
    if (achievementsUnlocked > achievementsTotal)
        throw new Error("Unlocked achievements cannot exceed the total.");

    var totalScore = ccDashboardInteger(args.totalScore, "totalScore", 0, 2147483647);
    var bridgesCompleted = ccDashboardInteger(
        args.bridgesCompleted, "bridgesCompleted", 0, 2147483647);
    var challengesCompleted = ccDashboardInteger(
        args.challengesCompleted, "challengesCompleted", 0, 2147483647);
    var bestSingleBuildScore = ccDashboardInteger(
        args.bestSingleBuildScore, "bestSingleBuildScore", 0, 2147483647);
    var hasPortraitProjection = args.characterPortraitFile !== undefined ||
        args.characterPortraitUpdatedAt !== undefined ||
        args.characterPortraitChecksum !== undefined;
    var portraitFile = ccDashboardOptionalText(
        args.characterPortraitFile, "characterPortraitFile", 100);
    var portraitUpdatedAt = ccDashboardOptionalText(
        args.characterPortraitUpdatedAt, "characterPortraitUpdatedAt", 64);
    var portraitChecksum = ccDashboardOptionalText(
        args.characterPortraitChecksum, "characterPortraitChecksum", 64);
    if (portraitFile && portraitFile !== "characterPortrait.png")
        throw new Error("Unsupported character portrait file.");
    if (portraitChecksum && !/^[a-f0-9]{64}$/.test(portraitChecksum))
        throw new Error("Invalid character portrait checksum.");
    if (!portraitFile && (portraitUpdatedAt || portraitChecksum))
        throw new Error("Character portrait metadata has no file.");

    var data = {
        CurrentLevel: currentLevel.toString(),
        XP: xp.toString(),
        XPToNextLevel: xpToNextLevel.toString(),
        CurrentRegion: ccDashboardText(args.currentRegion, "currentRegion", 100),
        AchievementsUnlocked: achievementsUnlocked.toString(),
        AchievementsTotal: achievementsTotal.toString(),
        BridgesCompleted: bridgesCompleted.toString(),
        ChallengesCompleted: challengesCompleted.toString(),
        MapProgress: ccDashboardJson(args.mapProgress, "mapProgress", false),
        AchievementProgress: ccDashboardJson(
            args.achievementProgress, "achievementProgress", true),
        EquippedCosmetics: ccDashboardJson(
            args.equippedCosmetics, "equippedCosmetics", false),
        AlmanacProgress: ccDashboardJson(args.almanacProgress, "almanacProgress", false)
    };
    // Older installed clients do not send portrait fields. In that case leave
    // any portrait uploaded by a newer build untouched.
    if (hasPortraitProjection) {
        data.CharacterPortraitFile = portraitFile;
        data.CharacterPortraitUpdatedAt = portraitUpdatedAt;
        data.CharacterPortraitChecksum = portraitChecksum;
    }

    ccDashboardWritePrivateData(data);
    server.UpdatePlayerStatistics({
        PlayFabId: currentPlayerId,
        Statistics: [
            { StatisticName: "TotalScore", Value: totalScore },
            { StatisticName: "BridgesCompleted", Value: bridgesCompleted },
            { StatisticName: "ChallengesCompleted", Value: challengesCompleted },
            { StatisticName: "BestSingleBuildScore", Value: bestSingleBuildScore }
        ],
        ForceUpdate: false
    });

    // Publish the completion marker only after all data and statistics succeed.
    // A failed batch must propagate so the client retries the entire projection.
    // Batches are not a transaction; this marker is a freshness signal only.
    ccDashboardWritePrivateData({ CharacterSyncedAt: new Date().toISOString() });

    return { accepted: true, schemaVersion: 1 };
};
