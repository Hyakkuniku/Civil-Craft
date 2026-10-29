// Run with: node Backend/PlayFab/dashboardCloudScript.test.js
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const sourceFile = process.argv[2] || "dashboardCloudScript.js";
const source = fs.readFileSync(path.join(__dirname, sourceFile), "utf8");
const userDataCalls = [];
const statisticCalls = [];
const events = [];
let failDataCall = -1;
let failStatistics = false;
let storedAlmanac = null;
let storedQuizReviews = null;
const absoluteStatistics = new Map();
const sandbox = {
    handlers: {},
    currentPlayerId: "dashboard-player",
    server: {
        GetUserData(request) {
            assert.equal(request.PlayFabId, sandbox.currentPlayerId);
            const keys = JSON.parse(JSON.stringify(request.Keys));
            if (keys.length === 1 && keys[0] === "QuizReviewProgress")
                return { Data: storedQuizReviews === null ? {} : {
                    QuizReviewProgress: { Value: storedQuizReviews }
                } };
            assert.deepEqual(keys, ["AlmanacProgress"]);
            return { Data: storedAlmanac === null ? {} : {
                AlmanacProgress: { Value: storedAlmanac }
            } };
        },
        UpdateUserData(request) {
            assert.ok(Object.keys(request.Data).length <= 10,
                "PlayFab rejects more than 10 player-data updates per request");
            assert.equal(request.PlayFabId, sandbox.currentPlayerId);
            assert.equal(request.Permission, "Private");
            userDataCalls.push(request);
            events.push({ kind: "data", request });
            if (userDataCalls.length === failDataCall)
                throw new Error("Simulated data API failure");
            return {};
        },
        UpdatePlayerStatistics(request) {
            statisticCalls.push(request);
            events.push({ kind: "statistics", request });
            if (failStatistics) throw new Error("Simulated statistics API failure");
            // Deliberately simulate a title configured with Sum. Absolute
            // dashboard snapshots must still be safe to retry unchanged.
            for (const stat of request.Statistics)
                absoluteStatistics.set(stat.StatisticName, request.ForceUpdate
                    ? stat.Value : (absoluteStatistics.get(stat.StatisticName) || 0) + stat.Value);
            return {};
        }
    }
};
vm.createContext(sandbox);
vm.runInContext(source, sandbox, { filename: sourceFile });

function projectedData(calls) {
    return Object.assign({}, ...calls.map(call => call.Data));
}

function assertMarkerLast(sequence) {
    assert.equal(sequence[sequence.length - 2].kind, "statistics");
    const last = sequence[sequence.length - 1];
    assert.equal(last.kind, "data");
    assert.deepEqual(Object.keys(last.request.Data), ["CharacterSyncedAt"]);
    assert.ok(sequence.slice(0, -1).every(event =>
        event.kind !== "data" || event.request.Data.CharacterSyncedAt === undefined));
}

const valid = {
    schemaVersion: 1,
    currentLevel: 3,
    xp: 450,
    xpToNextLevel: 600,
    currentRegion: "Canyon Crossing",
    achievementsUnlocked: 2,
    achievementsTotal: 30,
    mapProgress: JSON.stringify({
        overallPercent: 25,
        storyPercent: 20,
        currentRegion: "Canyon Crossing",
        regions: []
    }),
    achievementProgress: JSON.stringify([
        { id: "ACH_001", name: "First Contract", description: "Finish one.",
          unlocked: true, unlockedAt: "2026-10-01T00:00:00.000Z",
          progress: 1, progressTarget: 1 }
    ]),
    equippedCosmetics: JSON.stringify({
        helmet: { itemId: "EngineeringHardHat", name: "Engineering Hard Hat" }
    }),
    almanacProgress: JSON.stringify({
        regions: [], levelsCompleted: 2, levelsTotal: 8, journeyPercent: 25,
        discoveredBridgeTypeIds: ["beam"], discoveredMaterials: ["wood_beam"]
    }),
    characterPortraitFile: "characterPortrait.png",
    characterPortraitUpdatedAt: "2026-10-01T03:30:00.000Z",
    characterPortraitChecksum: "a".repeat(64),
    totalScore: 6,
    bridgesCompleted: 2,
    challengesCompleted: 2,
    bestSingleBuildScore: 3
};

const result = sandbox.handlers.syncDashboardV1(valid, {});
assert.equal(result.accepted, true);
assert.equal(userDataCalls.length, 3);
assert.equal(statisticCalls.length, 1);
assert.deepEqual(userDataCalls.map(call => Object.keys(call.Data).length), [10, 5, 1]);
assertMarkerLast(events);
const published = projectedData(userDataCalls);
assert.equal(userDataCalls[0].PlayFabId, sandbox.currentPlayerId);
assert.equal(userDataCalls[0].Permission, "Private");
assert.equal(published.CurrentLevel, "3");
assert.equal(published.XP, "450");
assert.equal(published.AchievementsTotal, "30");
assert.equal(published.CharacterPortraitFile, "characterPortrait.png");
assert.equal(published.CharacterPortraitChecksum, "a".repeat(64));
assert.deepEqual(JSON.parse(published.AchievementProgress),
    JSON.parse(valid.achievementProgress));
assert.match(published.CharacterSyncedAt,
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/);
assert.deepEqual(JSON.parse(JSON.stringify(statisticCalls[0].Statistics)), [
    { StatisticName: "TotalScore", Value: 6 },
    { StatisticName: "BridgesCompleted", Value: 2 },
    { StatisticName: "ChallengesCompleted", Value: 2 },
    { StatisticName: "BestSingleBuildScore", Value: 3 }
]);
assert.equal(statisticCalls[0].ForceUpdate, true);

const olderClientPayload = { ...valid };
delete olderClientPayload.characterPortraitFile;
delete olderClientPayload.characterPortraitUpdatedAt;
delete olderClientPayload.characterPortraitChecksum;
const olderClientResult = sandbox.handlers.syncDashboardV1(olderClientPayload, {});
assert.equal(olderClientResult.accepted, true);
assert.equal(userDataCalls.length, 6);
assert.deepEqual(userDataCalls.slice(3).map(call => Object.keys(call.Data).length), [10, 2, 1]);
const olderData = projectedData(userDataCalls.slice(3));
assert.equal(olderData.CharacterPortraitFile, undefined);
assert.equal(olderData.CharacterPortraitUpdatedAt, undefined);
assert.equal(olderData.CharacterPortraitChecksum, undefined);
assertMarkerLast(events.slice(4));

// A failed batch or statistic write is not accepted and cannot publish a new
// freshness marker. Retrying sends the complete projection, not only leftovers.
let eventStart = events.length;
failDataCall = userDataCalls.length + 2;
assert.throws(() => sandbox.handlers.syncDashboardV1(valid, {}), /data API failure/);
assert.equal(events.slice(eventStart).length, 2);
assert.equal(projectedData(userDataCalls.slice(-2)).CharacterSyncedAt, undefined);
failDataCall = -1;
eventStart = events.length;
assert.equal(sandbox.handlers.syncDashboardV1(valid, {}).accepted, true);
assertMarkerLast(events.slice(eventStart));
eventStart = events.length;
failStatistics = true;
assert.throws(() => sandbox.handlers.syncDashboardV1(valid, {}), /statistics API failure/);
assert.equal(events.slice(eventStart).length, 3);
assert.ok(events.slice(eventStart).every(event =>
    event.kind !== "data" || event.request.Data.CharacterSyncedAt === undefined));
failStatistics = false;
eventStart = events.length;
sandbox.currentPlayerId = "second-dashboard-player";
assert.equal(sandbox.handlers.syncDashboardV1(valid, {}).accepted, true);
assertMarkerLast(events.slice(eventStart));
assert.ok(events.slice(eventStart).every(event =>
    event.request.PlayFabId === "second-dashboard-player"));

// Old installed clients must not erase new optional records or silently choose
// among ambiguous duplicate stable IDs. All values here are synthetic fixtures.
const photo = { storage: "archive-v1", fileName: "bridge-photos-v1.zip",
    contractId: "ShopKeeper", regionId: "canyon-crossing", resultId: "a".repeat(32),
    entryName: "bridge-" + "a".repeat(32) + ".png", sha256: "b".repeat(64) };
storedAlmanac = JSON.stringify({
    publisherVersion: 2,
    lessonRecords: [{ lessonId: "LESSON002", state: "discovered" }],
    quizAttempts: [{ quizId: "fixture-quiz", attemptId: "fixture-result", title: "Fixture",
        status: "completed", score: 1, totalQuestions: 2 }],
    regions: [{ regionId: "canyon-crossing", levels: [{ levelId: "ShopKeeper",
        completion: { resultId: "a".repeat(32), completedAt: "2026-10-01T12:00:00Z", snapshot: photo } }] }]
});
const olderAlmanac = { regions: [{ regionId: "canyon-crossing", levels: [
    { levelId: "ShopKeeper", completion: { score: 2, completedAt: "2026-10-29T12:00:00Z" } }
] }] };
eventStart = events.length;
assert.equal(sandbox.handlers.syncDashboardV1({ ...valid,
    almanacProgress: JSON.stringify(olderAlmanac) }, {}).accepted, true);
let merged = JSON.parse(projectedData(userDataCalls.slice(-3)).AlmanacProgress);
assert.deepEqual(merged.lessonRecords, JSON.parse(storedAlmanac).lessonRecords);
assert.deepEqual(merged.quizAttempts, JSON.parse(storedAlmanac).quizAttempts);
assert.deepEqual(merged.regions[0].levels[0].completion.snapshot, photo);
assert.equal(merged.regions[0].levels[0].completion.resultId, undefined,
    "Omitting a result identity must not manufacture a binding to an old photo");
assert.equal(merged.regions[0].levels[0].completion.completedAt, "2026-10-01T12:00:00Z",
    "An older client cannot substitute sync time for a genuine turn-in date");
storedAlmanac = JSON.stringify(merged);
sandbox.handlers.syncDashboardV1({ ...valid, almanacProgress: JSON.stringify(olderAlmanac) }, {});
merged = JSON.parse(projectedData(userDataCalls.slice(-3)).AlmanacProgress);
assert.equal(merged.regions[0].levels[0].completion.completedAt, "2026-10-01T12:00:00Z",
    "Repeated older-client syncs preserve modern date provenance after result fields were omitted");
const undatedModern = JSON.parse(storedAlmanac);
delete undatedModern.regions[0].levels[0].completion.completedAt;
storedAlmanac = JSON.stringify(undatedModern);
sandbox.handlers.syncDashboardV1({ ...valid, almanacProgress: JSON.stringify(olderAlmanac) }, {});
merged = JSON.parse(projectedData(userDataCalls.slice(-3)).AlmanacProgress);
assert.equal(merged.regions[0].levels[0].completion.completedAt, undefined,
    "An unknown historical completion date must not become an older client's sync timestamp");

const deliberateEmpty = { ...olderAlmanac, lessonRecords: [], quizAttempts: [],
    regions: [{ regionId: "canyon-crossing", levels: [{ levelId: "ShopKeeper",
        completion: { resultId: "c".repeat(32), snapshot: null } }] }] };
sandbox.handlers.syncDashboardV1({ ...valid, almanacProgress: JSON.stringify(deliberateEmpty) }, {});
merged = JSON.parse(projectedData(userDataCalls.slice(-3)).AlmanacProgress);
assert.deepEqual(merged.lessonRecords, []);
assert.deepEqual(merged.quizAttempts, []);
assert.equal(merged.regions[0].levels[0].completion.snapshot, null);

const ambiguous = JSON.parse(storedAlmanac);
ambiguous.regions.push(ambiguous.regions[0]);
storedAlmanac = JSON.stringify(ambiguous);
sandbox.handlers.syncDashboardV1({ ...valid, almanacProgress: JSON.stringify(olderAlmanac) }, {});
merged = JSON.parse(projectedData(userDataCalls.slice(-3)).AlmanacProgress);
assert.equal(merged.regions[0].levels[0].completion.snapshot, undefined);

storedAlmanac = JSON.stringify([{ regionId: "canyon-crossing", levels: [] }]);
assert.equal(sandbox.handlers.syncDashboardV1(valid, {}).accepted, true,
    "A supported legacy array-format Almanac can be replaced by a verified object sync");

let beforeRejected = userDataCalls.length;
storedAlmanac = "invalid stored JSON";
assert.throws(() => sandbox.handlers.syncDashboardV1(valid, {}), /requires review/);
assert.equal(userDataCalls.length, beforeRejected);
storedAlmanac = JSON.stringify({ quizAttempts: ["x".repeat(9999)] });
assert.throws(() => sandbox.handlers.syncDashboardV1(valid, {}), /Invalid dashboard field/);
assert.equal(userDataCalls.length, beforeRejected);
storedAlmanac = null;
assert.equal(sandbox.ccDashboardArgumentBytes({ e: "\u{1f600}", c: "\u754c" }),
    Buffer.byteLength(JSON.stringify({ e: "\u{1f600}", c: "\u754c" }), "utf8"));
assert.throws(() => sandbox.handlers.syncDashboardV1({ ...valid,
    extra: "\u754c".repeat(10000) }, {}), /gateway limit/);
assert.equal(userDataCalls.length, beforeRejected);
sandbox.handlers.syncDashboardV1(valid, {});
sandbox.handlers.syncDashboardV1(valid, {});
for (const stat of statisticCalls[0].Statistics)
    assert.equal(absoluteStatistics.get(stat.StatisticName), stat.Value,
        "Retries must replace snapshots even with simulated Sum aggregation");

const callCount = userDataCalls.length;
assert.throws(() => sandbox.handlers.syncDashboardV1({ ...valid, schemaVersion: 2 }, {}));
assert.throws(() => sandbox.handlers.syncDashboardV1({ ...valid, xp: -1 }, {}));
assert.throws(() => sandbox.handlers.syncDashboardV1({
    ...valid, achievementsUnlocked: 31
}, {}));
assert.throws(() => sandbox.handlers.syncDashboardV1({
    ...valid, achievementProgress: "{}"
}, {}));
assert.throws(() => sandbox.handlers.syncDashboardV1({
    ...valid, almanacProgress: "not-json"
}, {}));
assert.throws(() => sandbox.handlers.syncDashboardV1({
    ...valid, characterPortraitFile: "other.png"
}, {}));
assert.throws(() => sandbox.handlers.syncDashboardV1({
    ...valid, characterPortraitChecksum: "not-a-checksum"
}, {}));
assert.equal(userDataCalls.length, callCount);

// Review selections remain private, owner-bound, optional for old clients and
// immutable within one retained submission. No additional Entity File is used.
const quizAggregate = { quizId: "fixture-quiz", attemptId: "fixture-attempt",
    title: "Fixture Quiz", status: "completed", score: 1, totalQuestions: 2 };
const quizReview = { quizId: quizAggregate.quizId, attemptId: quizAggregate.attemptId,
    contentFingerprint: "a".repeat(64), selectedIndices: [0, 1] };
const reviewArgs = { ...valid, almanacProgress: JSON.stringify({ regions: [], quizAttempts: [quizAggregate] }),
    quizReviewProgress: JSON.stringify({ schemaVersion: 1, reviews: [quizReview] }) };
let beforeReview = userDataCalls.length;
const reviewAck = sandbox.handlers.syncDashboardV1(reviewArgs, {});
assert.equal(reviewAck.quizReviewSchemaVersion, 1);
assert.equal(projectedData(userDataCalls.slice(beforeReview)).QuizReviewProgress, reviewArgs.quizReviewProgress);
assert.deepEqual(userDataCalls.slice(beforeReview).map(call => Object.keys(call.Data).length), [10, 6, 1]);
assertMarkerLast(events.slice(-4));
storedQuizReviews = reviewArgs.quizReviewProgress;
beforeReview = userDataCalls.length;
sandbox.handlers.syncDashboardV1(valid, {});
assert.equal(projectedData(userDataCalls.slice(beforeReview)).QuizReviewProgress, undefined,
    "Old clients must not clear or rewrite review selections they cannot publish");
beforeReview = userDataCalls.length;
sandbox.handlers.syncDashboardV1({ ...reviewArgs,
    quizReviewProgress: JSON.stringify({ schemaVersion: 1, reviews: [] }) }, {});
assert.deepEqual(JSON.parse(projectedData(userDataCalls.slice(beforeReview)).QuizReviewProgress).reviews, [quizReview],
    "Missing local review evidence preserves a previously published identical submission");
beforeReview = userDataCalls.length;
assert.throws(() => sandbox.handlers.syncDashboardV1({ ...reviewArgs,
    quizReviewProgress: JSON.stringify({ schemaVersion: 1, reviews: [{ ...quizReview, selectedIndices: [1, 0] }] }) }, {}),
    /cannot change/);
assert.equal(userDataCalls.length, beforeReview);
beforeReview = userDataCalls.length;
sandbox.handlers.syncDashboardV1({ ...reviewArgs,
    almanacProgress: JSON.stringify({ regions: [], quizAttempts: [{ ...quizAggregate, attemptId: "new-attempt" }] }),
    quizReviewProgress: JSON.stringify({ schemaVersion: 1, reviews: [] }) }, {});
assert.deepEqual(JSON.parse(projectedData(userDataCalls.slice(beforeReview)).QuizReviewProgress).reviews, [],
    "A new submitted aggregate must not inherit the previous submission's answers");
storedQuizReviews = null;
for (const bad of [
    null, "not-json", {}, { schemaVersion: 2, reviews: [] },
    { schemaVersion: 1, reviews: [quizReview, quizReview] },
    { schemaVersion: 1, reviews: [{ ...quizReview, attemptId: "foreign-attempt" }] },
    { schemaVersion: 1, reviews: [{ ...quizReview, selectedIndices: [0] }] },
    { schemaVersion: 1, reviews: [{ ...quizReview, selectedIndices: [0, 20] }] },
    { schemaVersion: 1, reviews: [{ ...quizReview, selectedIndices: [0, 0.5] }] },
    { schemaVersion: 1, reviews: [{ ...quizReview, contentFingerprint: "wrong" }] },
    { schemaVersion: 1, reviews: [{ ...quizReview, progressKey: "private-save-internal" }] },
    { schemaVersion: 1, reviews: [], answerKey: "not allowed" }
]) {
    beforeReview = userDataCalls.length;
    assert.throws(() => sandbox.handlers.syncDashboardV1({ ...reviewArgs,
        quizReviewProgress: typeof bad === "string" ? bad : JSON.stringify(bad) }, {}));
    assert.equal(userDataCalls.length, beforeReview, "Invalid reviews must fail before any player write");
}
storedQuizReviews = "malformed prior review";
beforeReview = userDataCalls.length;
assert.throws(() => sandbox.handlers.syncDashboardV1(reviewArgs, {}));
assert.equal(userDataCalls.length, beforeReview);
storedQuizReviews = null;

sandbox.currentPlayerId = "";
assert.throws(() => sandbox.handlers.syncDashboardV1(valid, {}));
assert.equal(userDataCalls.length, beforeReview);

console.log("Civil Craft dashboard CloudScript tests passed: " + sourceFile);
