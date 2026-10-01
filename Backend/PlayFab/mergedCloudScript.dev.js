// Civil Craft Development title, based on live legacy revision 3.
// Existing handlers are preserved; the leaderboard handler is appended below.

handlers.helloWorld = function (args, context) {
    var message = "Hello " + currentPlayerId + "!";
    log.info(message);
    var inputValue = null;
    if (args && args.inputValue)
        inputValue = args.inputValue;
    log.debug("helloWorld:", { input: args.inputValue });
    return { messageValue: message };
};

handlers.makeAPICall = function (args, context) {
    var request = {
        PlayFabId: currentPlayerId, Statistics: [{
            StatisticName: "Level",
            Value: 2
        }]
    };
    var playerStatResult = server.UpdatePlayerStatistics(request);
};

handlers.makeEntityAPICall = function (args, context) {
    var entityProfile = context.currentEntity;
    var apiResult = entity.SetObjects({
        Entity: entityProfile.Entity,
        Objects: [{
            ObjectName: "obj1",
            DataObject: {
                foo: "some server computed value",
                prop1: args.prop1
            }
        }]
    });
    return {
        profile: entityProfile,
        setResult: apiResult.SetResults[0].SetResult
    };
};

handlers.makeHTTPRequest = function (args, context) {
    var headers = { "X-MyCustomHeader": "Some Value" };
    var body = {
        input: args,
        userId: currentPlayerId,
        mode: "foobar"
    };
    var url = "http://httpbin.org/status/200";
    var content = JSON.stringify(body);
    var httpMethod = "post";
    var contentType = "application/json";
    var response = http.request(url, httpMethod, content, contentType, headers);
    return { responseContent: response };
};

handlers.handlePlayStreamEventAndProfile = function (args, context) {
    var psEvent = context.playStreamEvent;
    var profile = context.playerProfile;
    var content = JSON.stringify({ user: profile.PlayerId, event: psEvent.EventName });
    var response = http.request('https://httpbin.org/status/200', 'post', content, 'application/json', null);
    return { externalAPIResponse: response };
};

handlers.completedLevel = function (args, context) {
    var level = args.levelName;
    var monstersKilled = args.monstersKilled;
    var updateUserDataResult = server.UpdateUserInternalData({
        PlayFabId: currentPlayerId,
        Data: { lastLevelCompleted: level }
    });
    log.debug("Set lastLevelCompleted for player " + currentPlayerId + " to " + level);
    var request = {
        PlayFabId: currentPlayerId, Statistics: [{
            StatisticName: "level_monster_kills",
            Value: monstersKilled
        }]
    };
    server.UpdatePlayerStatistics(request);
    log.debug("Updated level_monster_kills stat for player " + currentPlayerId + " to " + monstersKilled);
};

handlers.updatePlayerMove = function (args) {
    var validMove = processPlayerMove(args);
    return { validMove: validMove };
};

function processPlayerMove(playerMove) {
    var now = Date.now();
    var playerMoveCooldownInSeconds = 15;
    var playerData = server.GetUserInternalData({
        PlayFabId: currentPlayerId,
        Keys: ["last_move_timestamp"]
    });
    var lastMoveTimestampSetting = playerData.Data["last_move_timestamp"];
    if (lastMoveTimestampSetting) {
        var lastMoveTime = Date.parse(lastMoveTimestampSetting.Value);
        var timeSinceLastMoveInSeconds = (now - lastMoveTime) / 1000;
        log.debug("lastMoveTime: " + lastMoveTime + " now: " + now + " timeSinceLastMoveInSeconds: " + timeSinceLastMoveInSeconds);
        if (timeSinceLastMoveInSeconds < playerMoveCooldownInSeconds) {
            log.error("Invalid move - time since last move: " + timeSinceLastMoveInSeconds + "s less than minimum of " + playerMoveCooldownInSeconds + "s.");
            return false;
        }
    }
    var playerStats = server.GetPlayerStatistics({
        PlayFabId: currentPlayerId
    }).Statistics;
    var movesMade = 0;
    for (var i = 0; i < playerStats.length; i++)
        if (playerStats[i].StatisticName === "")
            movesMade = playerStats[i].Value;
    movesMade += 1;
    var request = {
        PlayFabId: currentPlayerId, Statistics: [{
            StatisticName: "movesMade",
            Value: movesMade
        }]
    };
    server.UpdatePlayerStatistics(request);
    server.UpdateUserInternalData({
        PlayFabId: currentPlayerId,
        Data: {
            last_move_timestamp: new Date(now).toUTCString(),
            last_move: JSON.stringify(playerMove)
        }
    });
    return true;
}

handlers.unlockHighSkillContent = function (args, context) {
    var playerStatUpdatedEvent = context.playStreamEvent;
    var request = {
        PlayFabId: currentPlayerId,
        Data: {
            "HighSkillContent": "true",
            "XPAtHighSkillUnlock": playerStatUpdatedEvent.StatisticValue.toString()
        }
    };
    var playerInternalData = server.UpdateUserInternalData(request);
    log.info('Unlocked HighSkillContent for ' + context.playerProfile.DisplayName);
    return { profile: context.playerProfile };
};

handlers.RoomCreated = function (args) {
    log.debug("Room Created - Game: " + args.GameId + " MaxPlayers: " + args.CreateOptions.MaxPlayers);
};
handlers.RoomJoined = function (args) {
    log.debug("Room Joined - Game: " + args.GameId + " PlayFabId: " + args.UserId);
};
handlers.RoomLeft = function (args) {
    log.debug("Room Left - Game: " + args.GameId + " PlayFabId: " + args.UserId);
};
handlers.RoomClosed = function (args) {
    log.debug("Room Closed - Game: " + args.GameId);
};
handlers.RoomPropertyUpdated = function (args) {
    log.debug("Room Property Updated - Game: " + args.GameId);
};
handlers.RoomEventRaised = function (args) {
    var eventData = args.Data;
    log.debug("Event Raised - Game: " + args.GameId + " Event Type: " + eventData.eventType);
    switch (eventData.eventType) {
        case "playerMove":
            processPlayerMove(eventData);
            break;
        default:
            break;
    }
};

// New leaderboard publisher (same implementation as leaderboardCloudScript.js).
var ccLeaderboardContracts = {
    "ShopKeeper": "24A0506A7A79A0DB",
    "TUT_CONTRACT1": "6C19CA5B77A6FF90",
    "ReedsContract": "7AD47CC6EE895E44",
    "VancesContract": "C29D1DB10DD4940D",
    "TUT_CONTRACT2": "6C19CD5B77A704A9",
    "ReedSideInspection": "6CC5505F08AAC340",
    "VanceSideRealignment": "0FCFDEF8DF951749",
    "SilasMainContract": "B57A013AC08B9A90",
    "MainContractSilas": "DD46490E01484D5A"
};
var ccLeaderboardMaxCost = 1000000;
var ccLeaderboardMaxStressTenths = 1000;
var ccLeaderboardMaxInt = 2147483647;
function ccLeaderboardIsInteger(value, minimum, maximum) {
    return typeof value === "number" && isFinite(value) &&
        Math.floor(value) === value && value >= minimum && value <= maximum;
}
function ccLeaderboardScore(cost, stressTenths, strongest) {
    var packed = strongest
        ? stressTenths * (ccLeaderboardMaxCost + 1) + cost
        : cost * (ccLeaderboardMaxStressTenths + 1) + stressTenths;
    if (packed > ccLeaderboardMaxInt) throw new Error("Score exceeds statistic range.");
    return ccLeaderboardMaxInt - packed;
}
handlers.submitBridgeRunV1 = function (args, context) {
    if (typeof currentPlayerId !== "string" || !currentPlayerId)
        throw new Error("A signed-in player is required.");
    if (!args || typeof args.contractId !== "string" ||
        !Object.prototype.hasOwnProperty.call(ccLeaderboardContracts, args.contractId))
        throw new Error("Unknown leaderboard contract.");
    if (!ccLeaderboardIsInteger(args.cost, 0, ccLeaderboardMaxCost) ||
        !ccLeaderboardIsInteger(args.stressTenths, 0, ccLeaderboardMaxStressTenths))
        throw new Error("Invalid leaderboard measurements.");

    var suffix = ccLeaderboardContracts[args.contractId];
    var efficientName = "CC_E_" + suffix;
    var strongestName = "CC_S_" + suffix;
    var efficientScore = ccLeaderboardScore(args.cost, args.stressTenths, false);
    var strongestScore = ccLeaderboardScore(args.cost, args.stressTenths, true);
    var existing = server.GetPlayerStatistics({
        PlayFabId: currentPlayerId,
        StatisticNames: [efficientName, strongestName]
    });
    var current = {};
    var statistics = existing && existing.Statistics ? existing.Statistics : [];
    for (var i = 0; i < statistics.length; i++)
        current[statistics[i].StatisticName] = statistics[i].Value;

    var updates = [];
    if (!Object.prototype.hasOwnProperty.call(current, efficientName) ||
        efficientScore > current[efficientName])
        updates.push({ StatisticName: efficientName, Value: efficientScore });
    if (!Object.prototype.hasOwnProperty.call(current, strongestName) ||
        strongestScore > current[strongestName])
        updates.push({ StatisticName: strongestName, Value: strongestScore });
    if (updates.length > 0)
        server.UpdatePlayerStatistics({
            PlayFabId: currentPlayerId,
            Statistics: updates,
            ForceUpdate: false
        });
    return { accepted: true, updated: updates.length };
};

// Dashboard projection publisher (same implementation as dashboardCloudScript.js).
var ccDashboardMaxJsonLength = 10000;
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
        AlmanacProgress: ccDashboardJson(args.almanacProgress, "almanacProgress", false),
        CharacterSyncedAt: new Date().toISOString()
    };
    server.UpdateUserData({
        PlayFabId: currentPlayerId,
        Data: data,
        Permission: "Private"
    });
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
    return { accepted: true, schemaVersion: 1 };
};
