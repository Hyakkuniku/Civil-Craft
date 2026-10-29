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
