// Run with: node Backend/PlayFab/leaderboardCloudScript.test.js
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "leaderboardCloudScript.js"), "utf8");
const values = new Map();
const calls = [];
const sandbox = {
    handlers: {},
    currentPlayerId: "test-player",
    server: {
        GetPlayerStatistics(request) {
            assert.equal(request.PlayFabId, sandbox.currentPlayerId);
            return {
                Statistics: request.StatisticNames.filter(name => values.has(name))
                    .map(name => ({ StatisticName: name, Value: values.get(name) }))
            };
        },
        UpdatePlayerStatistics(request) {
            assert.equal(request.PlayFabId, sandbox.currentPlayerId);
            assert.equal(request.ForceUpdate, false);
            calls.push(request);
            for (const stat of request.Statistics) values.set(stat.StatisticName, stat.Value);
            return {};
        }
    }
};
vm.createContext(sandbox);
vm.runInContext(source, sandbox, { filename: "leaderboardCloudScript.js" });

function hashLikeUnity(id) {
    let hash = 14695981039346656037n;
    for (let i = 0; i < id.length; i++) {
        hash ^= BigInt(id.charCodeAt(i));
        hash = (hash * 1099511628211n) & 0xffffffffffffffffn;
    }
    return hash.toString(16).toUpperCase().padStart(16, "0");
}

for (const [id, suffix] of Object.entries(sandbox.ccLeaderboardContracts))
    assert.equal(suffix, hashLikeUnity(id), `Statistic name mismatch for ${id}`);

function contractAssetIds(folder) {
    const ids = [];
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
        const fullPath = path.join(folder, entry.name);
        if (entry.isDirectory()) ids.push(...contractAssetIds(fullPath));
        else if (entry.name.endsWith(".asset")) {
            const asset = fs.readFileSync(fullPath, "utf8");
            if (/^  hideFromLeaderboard: 1$/m.test(asset)) continue;
            const id = /^  contractID: (.+)$/m.exec(asset);
            assert.ok(id, `Missing stable contract ID: ${fullPath}`);
            ids.push(id[1].trim());
        }
    }
    return ids;
}
const contractFolder = path.join(__dirname, "..", "..", "Assets", "BridgeBuilder", "Data", "Contract");
assert.deepEqual(Object.keys(sandbox.ccLeaderboardContracts).sort(), contractAssetIds(contractFolder).sort(),
    "CloudScript allowlist must match playable contract assets");

const submit = sandbox.handlers.submitBridgeRunV1;
const contractId = "TUT_CONTRACT1";
const suffix = sandbox.ccLeaderboardContracts[contractId];
const efficientName = `CC_E_${suffix}`;
const strongestName = `CC_S_${suffix}`;
const first = submit({ contractId, cost: 100, stressTenths: 200 });
assert.equal(first.accepted, true);
assert.equal(first.updated, 2);
assert.equal(values.get(efficientName), 2147483647 - (100 * 1001 + 200));
assert.equal(values.get(strongestName), 2147483647 - (200 * 1000001 + 100));

const duplicate = submit({ contractId, cost: 100, stressTenths: 200 });
assert.equal(duplicate.updated, 0);
assert.equal(calls.length, 1);

const efficientOnly = submit({ contractId, cost: 90, stressTenths: 250 });
assert.equal(efficientOnly.updated, 1);
assert.equal(calls[1].Statistics[0].StatisticName, efficientName);

const strongestOnly = submit({ contractId, cost: 110, stressTenths: 180 });
assert.equal(strongestOnly.updated, 1);
assert.equal(calls[2].Statistics[0].StatisticName, strongestName);

const beforeReject = calls.length;
assert.throws(() => submit({ contractId: "123", cost: 1, stressTenths: 1 }));
assert.throws(() => submit({ contractId, cost: 1.5, stressTenths: 1 }));
assert.throws(() => submit({ contractId, cost: 1, stressTenths: 1001 }));
assert.equal(calls.length, beforeReject);

sandbox.currentPlayerId = "";
assert.throws(() => submit({ contractId, cost: 1, stressTenths: 1 }));
assert.equal(calls.length, beforeReject);

const mergedSource = fs.readFileSync(path.join(__dirname, "mergedCloudScript.dev.js"), "utf8");
const mergedSandbox = {
    handlers: {},
    currentPlayerId: "test-player",
    server: sandbox.server
};
vm.createContext(mergedSandbox);
vm.runInContext(mergedSource, mergedSandbox, { filename: "mergedCloudScript.dev.js" });
assert.deepEqual(Object.keys(mergedSandbox.handlers).sort(), [
    "helloWorld", "makeAPICall", "makeEntityAPICall", "makeHTTPRequest",
    "handlePlayStreamEventAndProfile", "completedLevel", "updatePlayerMove",
    "unlockHighSkillContent", "RoomCreated", "RoomJoined", "RoomLeft",
    "RoomClosed", "RoomPropertyUpdated", "RoomEventRaised", "submitBridgeRunV1"
].sort(), "Merged revision must preserve every existing live handler");
assert.deepEqual(Object.keys(mergedSandbox.ccLeaderboardContracts).sort(),
    Object.keys(sandbox.ccLeaderboardContracts).sort());
assert.equal(mergedSandbox.ccLeaderboardScore(100, 200, false),
    sandbox.ccLeaderboardScore(100, 200, false));
assert.equal(mergedSandbox.ccLeaderboardScore(100, 200, true),
    sandbox.ccLeaderboardScore(100, 200, true));
sandbox.currentPlayerId = "test-player";
const mergedFirst = mergedSandbox.handlers.submitBridgeRunV1({
    contractId: "ShopKeeper", cost: 250, stressTenths: 100
});
assert.equal(mergedFirst.accepted, true);
assert.equal(mergedFirst.updated, 2);
assert.equal(values.get("CC_E_24A0506A7A79A0DB"),
    2147483647 - (250 * 1001 + 100));
assert.equal(values.get("CC_S_24A0506A7A79A0DB"),
    2147483647 - (100 * 1000001 + 250));
assert.throws(() => mergedSandbox.handlers.submitBridgeRunV1({
    contractId: "UNKNOWN", cost: 1, stressTenths: 1
}));

console.log("Civil Craft leaderboard CloudScript tests passed.");
