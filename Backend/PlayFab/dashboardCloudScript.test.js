// Run with: node Backend/PlayFab/dashboardCloudScript.test.js
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "dashboardCloudScript.js"), "utf8");
const userDataCalls = [];
const statisticCalls = [];
const sandbox = {
    handlers: {},
    currentPlayerId: "dashboard-player",
    server: {
        UpdateUserData(request) {
            userDataCalls.push(request);
            return {};
        },
        UpdatePlayerStatistics(request) {
            statisticCalls.push(request);
            return {};
        }
    }
};
vm.createContext(sandbox);
vm.runInContext(source, sandbox, { filename: "dashboardCloudScript.js" });

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
assert.equal(userDataCalls.length, 1);
assert.equal(statisticCalls.length, 1);
assert.equal(userDataCalls[0].PlayFabId, sandbox.currentPlayerId);
assert.equal(userDataCalls[0].Permission, "Private");
assert.equal(userDataCalls[0].Data.CurrentLevel, "3");
assert.equal(userDataCalls[0].Data.XP, "450");
assert.equal(userDataCalls[0].Data.AchievementsTotal, "30");
assert.equal(userDataCalls[0].Data.CharacterPortraitFile, "characterPortrait.png");
assert.equal(userDataCalls[0].Data.CharacterPortraitChecksum, "a".repeat(64));
assert.deepEqual(JSON.parse(userDataCalls[0].Data.AchievementProgress),
    JSON.parse(valid.achievementProgress));
assert.match(userDataCalls[0].Data.CharacterSyncedAt,
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
assert.equal(userDataCalls[1].Data.CharacterPortraitFile, undefined);
assert.equal(userDataCalls[1].Data.CharacterPortraitUpdatedAt, undefined);
assert.equal(userDataCalls[1].Data.CharacterPortraitChecksum, undefined);

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

console.log("Civil Craft dashboard CloudScript tests passed.");
