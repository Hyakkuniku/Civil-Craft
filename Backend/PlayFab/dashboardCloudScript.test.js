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
const sandbox = {
    handlers: {},
    currentPlayerId: "dashboard-player",
    server: {
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
assert.equal(statisticCalls[0].ForceUpdate, false);

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

sandbox.currentPlayerId = "";
assert.throws(() => sandbox.handlers.syncDashboardV1(valid, {}));
assert.equal(userDataCalls.length, callCount);

console.log("Civil Craft dashboard CloudScript tests passed: " + sourceFile);
