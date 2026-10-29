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
// Existing clients send the whole Almanac object but know nothing about newer
// optional education/photo fields. Preserve only omitted additions, matched by
// stable region/contract IDs. Explicit null/empty values remain intentional.
function ccDashboardMergeAlmanac(raw) {
    var incoming = JSON.parse(raw);
    var previousResult = server.GetUserData({
        PlayFabId: currentPlayerId, Keys: ["AlmanacProgress"]
    });
    var previousRecord = previousResult && previousResult.Data &&
        previousResult.Data.AlmanacProgress;
    if (!previousRecord || !previousRecord.Value) return raw;
    var previous;
    try { previous = JSON.parse(previousRecord.Value); }
    catch (error) { throw new Error("Stored Almanac projection requires review."); }
    // The legacy website reader also accepts an array of regions. Such records
    // have no optional top-level additions to preserve; allow a current client
    // to replace them with its verified object projection.
    if (Array.isArray(previous)) previous = { regions: previous };
    if (!previous || typeof previous !== "object")
        throw new Error("Stored Almanac projection requires review.");
    var legacyPublisher = incoming.publisherVersion !== 2;
    if (legacyPublisher && previous.publisherVersion === 2)
        incoming.publisherVersion = 2;
    ["lessonRecords", "quizAttempts"].forEach(function (key) {
        if (!Object.prototype.hasOwnProperty.call(incoming, key) &&
            Object.prototype.hasOwnProperty.call(previous, key))
            incoming[key] = previous[key];
    });
    if (Array.isArray(incoming.regions) && Array.isArray(previous.regions))
        incoming.regions.forEach(function (region) {
            if (!region || typeof region.regionId !== "string" ||
                !Array.isArray(region.levels)) return;
            var oldRegions = previous.regions.filter(function (candidate) {
                return candidate && candidate.regionId === region.regionId;
            });
            if (oldRegions.length !== 1 || !Array.isArray(oldRegions[0].levels)) return;
            region.levels.forEach(function (level) {
                if (!level || typeof level.levelId !== "string" ||
                    !level.completion || typeof level.completion !== "object" ||
                    Array.isArray(level.completion)) return;
                var oldLevels = oldRegions[0].levels.filter(function (candidate) {
                    return candidate && candidate.levelId === level.levelId;
                });
                if (oldLevels.length !== 1 || !oldLevels[0].completion) return;
                var priorCompletion = oldLevels[0].completion;
                // Older APKs stamp synchronization time as completedAt. Never
                // let their missing result identity replace a modern turn-in
                // date (including an explicitly unknown historical date).
                if (legacyPublisher &&
                    (previous.publisherVersion === 2 ||
                     (typeof priorCompletion.resultId === "string" &&
                      /^[a-f0-9]{32}$/.test(priorCompletion.resultId)))) {
                    if (Object.prototype.hasOwnProperty.call(priorCompletion, "completedAt"))
                        level.completion.completedAt = priorCompletion.completedAt;
                    else delete level.completion.completedAt;
                }
                if (Object.prototype.hasOwnProperty.call(level.completion, "snapshot") ||
                    !Object.prototype.hasOwnProperty.call(priorCompletion, "snapshot")) return;
                // Archive readers additionally require the completion's resultId
                // to match the snapshot. An old client cannot rebind an old image
                // to another result merely by omitting the new fields.
                level.completion.snapshot = oldLevels[0].completion.snapshot;
            });
        });
    return ccDashboardJson(JSON.stringify(incoming), "almanacProgress", false);
}

// The gateway limit is UTF-8 bytes for the entire argument object, not the
// character count of any individual serialized nested projection.
function ccDashboardArgumentBytes(args) {
    var text = JSON.stringify(args);
    var bytes = 0;
    for (var i = 0; i < text.length; i++) {
        var code = text.charCodeAt(i);
        if (code < 128) bytes++;
        else if (code < 2048) bytes += 2;
        else if (code >= 55296 && code <= 56319 &&
                 i + 1 < text.length && text.charCodeAt(i + 1) >= 56320 &&
                 text.charCodeAt(i + 1) <= 57343) { bytes += 4; i++; }
        else bytes += 3;
    }
    return bytes;
}

// Optional compact answer selections are separate from the nearly-full Almanac
// value. Question text and answer keys are resolved only by the authenticated
// website reader against a retained, exact-version server catalog.
function ccDashboardReadQuizReviews(raw) {
    ccDashboardJson(raw, "quizReviewProgress", false);
    var document = JSON.parse(raw);
    if (document.schemaVersion !== 1 || !Array.isArray(document.reviews) ||
        document.reviews.length > 100 ||
        Object.keys(document).some(function (key) { return key !== "schemaVersion" && key !== "reviews"; }))
        throw new Error("Invalid quiz review projection.");
    var quizIds = Object.create(null);
    document.reviews.forEach(function (review) {
        if (!review || typeof review !== "object" || Array.isArray(review) ||
            Object.keys(review).some(function (key) {
                return ["quizId", "attemptId", "contentFingerprint", "selectedIndices"].indexOf(key) < 0;
            })) throw new Error("Invalid quiz review record.");
        ccDashboardText(review.quizId, "quizId", 200);
        ccDashboardText(review.attemptId, "attemptId", 200);
        if (!review.quizId.trim() || !review.attemptId.trim() ||
            Object.prototype.hasOwnProperty.call(quizIds, review.quizId) ||
            typeof review.contentFingerprint !== "string" ||
            !/^[a-f0-9]{64}$/.test(review.contentFingerprint) ||
            !Array.isArray(review.selectedIndices) || review.selectedIndices.length < 1 ||
            review.selectedIndices.length > 500)
            throw new Error("Invalid quiz review record.");
        quizIds[review.quizId] = true;
        review.selectedIndices.forEach(function (choice) {
            ccDashboardInteger(choice, "selectedIndex", 0, 19);
        });
    });
    return document;
}
function ccDashboardQuizReviewMatches(review, attempts) {
    var matches = attempts.filter(function (attempt) {
        return attempt && attempt.quizId === review.quizId;
    });
    return matches.length === 1 && matches[0].attemptId === review.attemptId &&
        matches[0].status === "completed" &&
        matches[0].totalQuestions === review.selectedIndices.length &&
        typeof matches[0].score === "number" && isFinite(matches[0].score) &&
        Math.floor(matches[0].score) === matches[0].score &&
        matches[0].score >= 0 && matches[0].score <= matches[0].totalQuestions;
}
function ccDashboardMergeQuizReviews(raw, almanacRaw) {
    var incoming = ccDashboardReadQuizReviews(raw);
    var almanac = JSON.parse(almanacRaw);
    var attempts = Array.isArray(almanac.quizAttempts) ? almanac.quizAttempts : [];
    incoming.reviews.forEach(function (review) {
        if (!ccDashboardQuizReviewMatches(review, attempts))
            throw new Error("Quiz review does not match its submitted aggregate.");
    });
    var stored = server.GetUserData({ PlayFabId: currentPlayerId, Keys: ["QuizReviewProgress"] });
    var record = stored && stored.Data && stored.Data.QuizReviewProgress;
    if (record && record.Value) {
        var previous = ccDashboardReadQuizReviews(record.Value);
        previous.reviews.forEach(function (review) {
            if (!ccDashboardQuizReviewMatches(review, attempts)) return;
            var current = incoming.reviews.filter(function (candidate) { return candidate.quizId === review.quizId; });
            if (!current.length) incoming.reviews.push(review);
            else if (current[0].contentFingerprint !== review.contentFingerprint ||
                JSON.stringify(current[0].selectedIndices) !== JSON.stringify(review.selectedIndices))
                throw new Error("Submitted quiz review cannot change.");
        });
    }
    incoming.reviews.sort(function (left, right) {
        return left.quizId < right.quizId ? -1 : left.quizId > right.quizId ? 1 : 0;
    });
    var merged = JSON.stringify(incoming);
    ccDashboardReadQuizReviews(merged);
    return merged;
}

handlers.syncDashboardV1 = function (args, context) {
    if (typeof currentPlayerId !== "string" || !currentPlayerId)
        throw new Error("A signed-in player is required.");
    if (!args || ccDashboardInteger(args.schemaVersion, "schemaVersion", 1, 1) !== 1)
        throw new Error("Unsupported dashboard schema.");
    if (ccDashboardArgumentBytes(args) > 30000)
        throw new Error("Dashboard arguments exceed the 30000-byte gateway limit.");
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
    data.AlmanacProgress = ccDashboardMergeAlmanac(data.AlmanacProgress);
    if (args.quizReviewProgress !== undefined)
        data.QuizReviewProgress = ccDashboardMergeQuizReviews(
            args.quizReviewProgress, data.AlmanacProgress);
    ccDashboardWritePrivateData(data);
    server.UpdatePlayerStatistics({
        PlayFabId: currentPlayerId,
        Statistics: [
            { StatisticName: "TotalScore", Value: totalScore },
            { StatisticName: "BridgesCompleted", Value: bridgesCompleted },
            { StatisticName: "ChallengesCompleted", Value: challengesCompleted },
            { StatisticName: "BestSingleBuildScore", Value: bestSingleBuildScore }
        ],
        // These four values are absolute dashboard snapshots, not deltas or
        // CC_E_/CC_S_/multiplayer leaderboard submissions. Replacing them makes
        // retries safe even if a title definition was configured as Sum.
        ForceUpdate: true
    });
    // Publish the completion marker only after all data and statistics succeed.
    // A failed batch must propagate so the client retries the entire projection.
    // Batches are not a transaction; this marker is a freshness signal only.
    ccDashboardWritePrivateData({ CharacterSyncedAt: new Date().toISOString() });
    var acknowledgment = { accepted: true, schemaVersion: 1 };
    if (args.quizReviewProgress !== undefined) acknowledgment.quizReviewSchemaVersion = 1;
    return acknowledgment;
};

// Civil Craft development multiplayer leaderboard for legacy CloudScript.
// Append this complete source to the preserved title revision before upload.
// CC_MP_Wins/Losses/Draws MUST use Maximum aggregation and manual reset.
// This records client-reported outcomes, not a trusted server simulation.

var ccMultiplayerReceiptPrefix = "CC_MP_MATCH_";
var ccMultiplayerStatisticNames = ["CC_MP_Wins", "CC_MP_Losses", "CC_MP_Draws"];
var ccMultiplayerMaxInt = 2147483647;

function ccMultiplayerRequirePlayer() {
    if (typeof currentPlayerId !== "string" || !currentPlayerId.trim())
        throw new Error("A signed-in player is required.");
    return currentPlayerId;
}

function ccMultiplayerInteger(value) {
    return typeof value === "number" && isFinite(value) &&
        Math.floor(value) === value && value >= 0 && value <= ccMultiplayerMaxInt;
}

function ccMultiplayerValidateResult(args, playerId) {
    if (!args || typeof args.matchId !== "string" ||
        !/^[a-f0-9]{32}$/i.test(args.matchId))
        throw new Error("Invalid multiplayer match ID; use a unique 32-hex GUID.");
    if (typeof args.opponentId !== "string" ||
        !/^[^\s]{1,128}$/.test(args.opponentId) ||
        args.opponentId.toUpperCase() === playerId.toUpperCase())
        throw new Error("A different signed-in opponent is required.");
    if (args.outcome !== "win" && args.outcome !== "loss" && args.outcome !== "draw")
        throw new Error("Invalid multiplayer outcome.");
    return {
        schemaVersion: 1,
        matchId: args.matchId.toLowerCase(),
        opponentId: args.opponentId,
        outcome: args.outcome
    };
}

function ccMultiplayerReadReceipt(key, record, playerId) {
    var receipt;
    try { receipt = JSON.parse(record.Value); }
    catch (error) { throw new Error("Malformed multiplayer match receipt: " + key + "."); }
    if (!receipt || receipt.schemaVersion !== 1 ||
        key !== ccMultiplayerReceiptPrefix + receipt.matchId)
        throw new Error("Invalid multiplayer match receipt: " + key + ".");
    var validated = ccMultiplayerValidateResult(receipt, playerId);
    if (validated.matchId !== receipt.matchId)
        throw new Error("Non-canonical multiplayer match receipt: " + key + ".");
    return validated;
}

function ccMultiplayerSameReceipt(first, second) {
    return first.matchId === second.matchId && first.opponentId === second.opponentId &&
        first.outcome === second.outcome;
}

function ccMultiplayerIsStatisticNotFound(error) {
    // Classic CloudScript wraps Server API errors in apiErrorInfo.apiError.
    var apiError = error && error.apiErrorInfo && error.apiErrorInfo.apiError;
    if (!apiError) apiError = error;
    return !!apiError &&
        (apiError.error === "StatisticNotFound" || apiError.errorCode === 1195);
}

function ccMultiplayerReadTotals(playerId) {
    var totals = { wins: 0, losses: 0, draws: 0 };
    var response;
    try {
        response = server.GetPlayerStatistics({
            PlayFabId: playerId,
            StatisticNames: ccMultiplayerStatisticNames
        });
    } catch (error) {
        if (ccMultiplayerIsStatisticNotFound(error)) return totals;
        throw error;
    }
    var statistics = response && response.Statistics ? response.Statistics : [];
    for (var i = 0; i < statistics.length; i++) {
        var stat = statistics[i];
        var field = stat.StatisticName === "CC_MP_Wins" ? "wins" :
            stat.StatisticName === "CC_MP_Losses" ? "losses" :
            stat.StatisticName === "CC_MP_Draws" ? "draws" : "";
        if (!field) continue;
        if (!ccMultiplayerInteger(stat.Value))
            throw new Error("Invalid multiplayer statistic: " + stat.StatisticName + ".");
        totals[field] = stat.Value;
    }
    return totals;
}

handlers.submitMultiplayerResultV1 = function (args, context) {
    var playerId = ccMultiplayerRequirePlayer();
    var receipt = ccMultiplayerValidateResult(args, playerId);
    var key = ccMultiplayerReceiptPrefix + receipt.matchId;
    var existing = server.GetUserInternalData({ PlayFabId: playerId, Keys: [key] });
    var existingData = existing && existing.Data ? existing.Data : {};
    var duplicate = Object.prototype.hasOwnProperty.call(existingData, key);
    if (duplicate) {
        if (!ccMultiplayerSameReceipt(
            ccMultiplayerReadReceipt(key, existingData[key], playerId), receipt))
            throw new Error("Conflicting multiplayer match receipt.");
    } else {
        var data = {};
        data[key] = JSON.stringify(receipt);
        // A separate key per GUID preserves concurrent DISTINCT match writes.
        // This API has no create-only/CAS operation for same-GUID races.
        server.UpdateUserInternalData({ PlayFabId: playerId, Data: data });
    }

    // Always recompute from the complete durable ledger, including on retries.
    // Do not increment stored counters or evict/truncate old match receipts.
    var ledger = server.GetUserInternalData({ PlayFabId: playerId });
    var ledgerData = ledger && ledger.Data ? ledger.Data : {};
    if (!Object.prototype.hasOwnProperty.call(ledgerData, key) ||
        !ccMultiplayerSameReceipt(
            ccMultiplayerReadReceipt(key, ledgerData[key], playerId), receipt))
        throw new Error("Match receipt could not be confirmed; retry with the same match ID.");
    var totals = { wins: 0, losses: 0, draws: 0 };
    for (var ledgerKey in ledgerData) {
        if (!Object.prototype.hasOwnProperty.call(ledgerData, ledgerKey) ||
            ledgerKey.indexOf(ccMultiplayerReceiptPrefix) !== 0) continue;
        var recorded = ccMultiplayerReadReceipt(ledgerKey, ledgerData[ledgerKey], playerId);
        var field = recorded.outcome === "win" ? "wins" :
            recorded.outcome === "loss" ? "losses" : "draws";
        totals[field]++;
        if (!ccMultiplayerInteger(totals[field]))
            throw new Error("Multiplayer match count exceeds statistic range.");
    }
    // Maximum aggregation prevents an older overlapping snapshot from lowering
    // any total. A failure after saving the receipt is healed by the same retry.
    server.UpdatePlayerStatistics({
        PlayFabId: playerId,
        Statistics: [
            { StatisticName: "CC_MP_Wins", Value: totals.wins },
            { StatisticName: "CC_MP_Losses", Value: totals.losses },
            { StatisticName: "CC_MP_Draws", Value: totals.draws }
        ],
        ForceUpdate: false
    });
    return { accepted: true, duplicate: duplicate, matchId: receipt.matchId, personal: totals };
};

handlers.getMultiplayerLeaderboardV1 = function (args, context) {
    var playerId = ccMultiplayerRequirePlayer();
    var response;
    try {
        response = server.GetLeaderboard({
            StatisticName: "CC_MP_Wins", StartPosition: 0, MaxResultsCount: 15
        });
    } catch (error) {
        if (!ccMultiplayerIsStatisticNotFound(error)) throw error;
        response = { Leaderboard: [] };
    }
    var personal = ccMultiplayerReadTotals(playerId);
    var rows = response && response.Leaderboard ? response.Leaderboard : [];
    var entries = [];
    for (var i = 0; i < rows.length && i < 15; i++) {
        var row = rows[i];
        if (typeof row.PlayFabId !== "string" || !row.PlayFabId ||
            !ccMultiplayerInteger(row.Position))
            throw new Error("Invalid multiplayer leaderboard entry.");
        var totals = row.PlayFabId === playerId ? personal : ccMultiplayerReadTotals(row.PlayFabId);
        entries.push({
            rank: row.Position + 1,
            playerId: row.PlayFabId,
            displayName: typeof row.DisplayName === "string" && row.DisplayName
                ? row.DisplayName : "Engineer",
            wins: totals.wins,
            losses: totals.losses,
            draws: totals.draws
        });
    }
    // The client derives winRate = wins / (wins + losses) * 100; draws excluded.
    return { entries: entries, personal: personal };
};
