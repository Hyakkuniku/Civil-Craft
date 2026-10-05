// Run with: node Backend/PlayFab/multiplayerCloudScript.test.js
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "multiplayerCloudScript.js"), "utf8");
const mergedSource = fs.readFileSync(path.join(__dirname, "mergedCloudScript.dev.js"), "utf8");
const prefix = "CC_MP_MATCH_";
const names = ["CC_MP_Wins", "CC_MP_Losses", "CC_MP_Draws"];
const matchId = number => number.toString(16).padStart(32, "0");
const plain = value => JSON.parse(JSON.stringify(value));
const result = (number, outcome = "win", opponentId = "opponent-player") => ({
    matchId: matchId(number), outcome, opponentId
});
const apiError = (name, code) => ({ apiErrorInfo: { apiError: { error: name, errorCode: code } } });

function createHarness(script) {
    const receipts = new Map();
    const statistics = new Map();
    const calls = [];
    const hooks = {};
    const sandbox = { handlers: {}, currentPlayerId: "test-player", server: {} };
    function dataFor(playerId) {
        if (!receipts.has(playerId)) receipts.set(playerId, {});
        return receipts.get(playerId);
    }
    function statsFor(playerId) {
        if (!statistics.has(playerId)) statistics.set(playerId, new Map());
        return statistics.get(playerId);
    }
    function call(name, request) {
        calls.push({ name, request: plain(request) });
    }
    sandbox.server.GetUserInternalData = request => {
        call("GetUserInternalData", request);
        assert.equal(request.PlayFabId, sandbox.currentPlayerId,
            "Receipt reads must only access the authenticated caller");
        if (hooks.getData) return hooks.getData(request);
        const stored = dataFor(request.PlayFabId);
        const data = {};
        for (const key of request.Keys || Object.keys(stored)) {
            if (Object.hasOwn(stored, key)) data[key] = plain(stored[key]);
        }
        return { Data: data };
    };
    sandbox.server.UpdateUserInternalData = request => {
        call("UpdateUserInternalData", request);
        assert.equal(request.PlayFabId, sandbox.currentPlayerId);
        assert.equal(request.KeysToRemove, undefined, "Receipts must never be evicted");
        assert.equal(Object.keys(request.Data).length, 1, "Each match has its own key");
        if (hooks.beforeReceiptWrite) hooks.beforeReceiptWrite(request);
        for (const [key, value] of Object.entries(request.Data)) {
            assert.ok(key.startsWith(prefix));
            assert.equal(typeof value, "string");
            dataFor(request.PlayFabId)[key] = { Value: value };
        }
        if (hooks.afterReceiptWrite) hooks.afterReceiptWrite(request);
        return {};
    };
    function applyStatistic(request, statistic) {
        const values = statsFor(request.PlayFabId);
        // Model title definitions configured as Maximum, not replacement or Sum.
        values.set(statistic.StatisticName,
            Math.max(values.get(statistic.StatisticName) || 0, statistic.Value));
    }
    sandbox.server.UpdatePlayerStatistics = request => {
        call("UpdatePlayerStatistics", request);
        assert.equal(request.PlayFabId, sandbox.currentPlayerId,
            "No match result may write the opponent's totals");
        assert.equal(request.ForceUpdate, false);
        assert.deepEqual(plain(request.Statistics).map(stat => stat.StatisticName), names);
        if (hooks.updateStatistics) return hooks.updateStatistics(request, applyStatistic);
        for (const stat of request.Statistics) applyStatistic(request, stat);
        return {};
    };
    sandbox.server.GetPlayerStatistics = request => {
        call("GetPlayerStatistics", request);
        assert.deepEqual(plain(request.StatisticNames), names);
        if (hooks.getStatistics) return hooks.getStatistics(request);
        const values = statsFor(request.PlayFabId);
        return { Statistics: names.filter(name => values.has(name))
            .map(name => ({ StatisticName: name, Value: values.get(name) })) };
    };
    sandbox.server.GetLeaderboard = request => {
        call("GetLeaderboard", request);
        assert.deepEqual(plain(request), {
            StatisticName: "CC_MP_Wins", StartPosition: 0, MaxResultsCount: 15
        });
        if (hooks.getLeaderboard) return hooks.getLeaderboard(request);
        return { Leaderboard: [] };
    };
    vm.createContext(sandbox);
    vm.runInContext(script, sandbox);
    return { sandbox, calls, hooks, dataFor, statsFor,
        submit: args => sandbox.handlers.submitMultiplayerResultV1(args, {}),
        read: args => sandbox.handlers.getMultiplayerLeaderboardV1(args, {}) };
}

function verifyImplementation(script) {
    const h = createHarness(script);
    const first = h.submit({ ...result(1), playerId: "spoofed-player", wins: 999 });
    assert.deepEqual(plain(first), {
        accepted: true, duplicate: false, matchId: matchId(1),
        personal: { wins: 1, losses: 0, draws: 0 }
    });
    assert.equal(h.statsFor("test-player").get("CC_MP_Wins"), 1);
    assert.equal(h.calls.some(call => call.name === "GetPlayerStatistics"), false,
        "Submissions must derive totals from receipts, never statistic-counter RMW");
    h.submit(result(2, "loss"));
    const third = h.submit(result(3, "draw"));
    assert.deepEqual(plain(third.personal), { wins: 1, losses: 1, draws: 1 });
    const storedReceipt = h.dataFor("test-player")[prefix + matchId(1)].Value;
    const writes = h.calls.filter(call => call.name === "UpdateUserInternalData").length;
    const duplicate = h.submit(result(1));
    assert.equal(duplicate.duplicate, true);
    assert.deepEqual(plain(duplicate.personal), { wins: 1, losses: 1, draws: 1 });
    assert.equal(h.dataFor("test-player")[prefix + matchId(1)].Value, storedReceipt);
    assert.equal(h.calls.filter(call => call.name === "UpdateUserInternalData").length, writes);
    const statsCalls = h.calls.filter(call => call.name === "UpdatePlayerStatistics").length;
    assert.throws(() => h.submit(result(1, "loss")), /Conflicting/);
    assert.throws(() => h.submit(result(1, "win", "different-opponent")), /Conflicting/);
    assert.equal(h.calls.filter(call => call.name === "UpdateUserInternalData").length, writes);
    assert.equal(h.calls.filter(call => call.name === "UpdatePlayerStatistics").length, statsCalls);

    const canonical = h.submit({ ...result(10), matchId: matchId(10).toUpperCase() });
    assert.equal(canonical.matchId, matchId(10));
    assert.equal(h.submit(result(10)).duplicate, true);

    const callsBeforeInvalid = h.calls.length;
    for (const invalid of [null, {}, { ...result(4), matchId: "not-a-guid" },
        { ...result(4), matchId: "x".repeat(32) },
        { ...result(4), matchId: "0".repeat(31) },
        { ...result(4), opponentId: "" }, { ...result(4), opponentId: " " },
        { ...result(4), opponentId: " test-player" },
        { ...result(4), opponentId: "TEST-PLAYER" },
        { ...result(4), opponentId: 123 },
        { ...result(4), outcome: "WIN" }, { ...result(4), outcome: "invalid" }])
        assert.throws(() => h.submit(invalid));
    assert.equal(h.calls.length, callsBeforeInvalid, "Malformed payloads must make zero API calls");
    for (const playerId of ["", "   ", null, undefined]) {
        h.sandbox.currentPlayerId = playerId;
        assert.throws(() => h.submit(result(4)), /signed-in/);
        assert.throws(() => h.read({}), /signed-in/);
    }
    assert.equal(h.calls.length, callsBeforeInvalid, "Unauthenticated callers make zero API calls");
    h.sandbox.currentPlayerId = "test-player";

    const retry = createHarness(script);
    retry.submit(result(1, "loss"));
    retry.hooks.updateStatistics = (request, apply) => {
        apply(request, request.Statistics[0]);
        throw apiError("InternalServerError", 1110);
    };
    assert.throws(() => retry.submit(result(2, "draw")));
    assert.ok(retry.dataFor("test-player")[prefix + matchId(2)],
        "Receipt must survive a partial statistic failure");
    assert.equal(retry.statsFor("test-player").get("CC_MP_Draws"), 0);
    delete retry.hooks.updateStatistics;
    const healed = retry.submit(result(2, "draw"));
    assert.equal(healed.duplicate, true);
    assert.deepEqual(plain(healed.personal), { wins: 0, losses: 1, draws: 1 });
    assert.equal(retry.statsFor("test-player").get("CC_MP_Draws"), 1);
    assert.equal(retry.calls.filter(call => call.name === "UpdateUserInternalData").length, 2);

    const writeFailure = createHarness(script);
    writeFailure.hooks.beforeReceiptWrite = () => { throw new Error("storage limit"); };
    assert.throws(() => writeFailure.submit(result(1)), /storage limit/);
    assert.equal(writeFailure.calls.some(call => call.name === "UpdatePlayerStatistics"), false);
    assert.equal(Object.keys(writeFailure.dataFor("test-player")).length, 0);
    delete writeFailure.hooks.beforeReceiptWrite;
    writeFailure.hooks.afterReceiptWrite = () => { throw new Error("lost write response"); };
    assert.throws(() => writeFailure.submit(result(1)), /lost write response/);
    delete writeFailure.hooks.afterReceiptWrite;
    assert.equal(writeFailure.submit(result(1)).duplicate, true);
    assert.equal(writeFailure.statsFor("test-player").get("CC_MP_Wins"), 1);

    const concurrent = createHarness(script);
    let overlapped = false;
    concurrent.hooks.updateStatistics = (request, apply) => {
        if (!overlapped) {
            overlapped = true;
            // A second request commits a new GUID and publishes the newer totals
            // while the first request still holds its older ledger snapshot.
            concurrent.submit(result(2));
        }
        for (const stat of request.Statistics) apply(request, stat);
        return {};
    };
    concurrent.submit(result(1));
    assert.equal(Object.keys(concurrent.dataFor("test-player")).length, 2);
    assert.equal(concurrent.statsFor("test-player").get("CC_MP_Wins"), 2,
        "A late older update must not lower totals under Maximum aggregation");
    const snapshots = concurrent.calls.filter(call => call.name === "UpdatePlayerStatistics")
        .map(call => call.request.Statistics[0].Value);
    assert.deepEqual(snapshots, [1, 2]);
    assert.equal(concurrent.submit(result(1)).personal.wins, 2);

    const many = createHarness(script);
    for (let i = 1; i <= 321; i++) {
        const receipt = { schemaVersion: 1, ...result(i) };
        many.dataFor("test-player")[prefix + receipt.matchId] = { Value: JSON.stringify(receipt) };
    }
    many.dataFor("test-player").unrelatedInternalSetting = { Value: "not-json" };
    assert.equal(many.submit(result(322)).personal.wins, 322,
        "All receipts must be counted, including beyond an arbitrary recent-match window");
    assert.equal(Object.keys(many.dataFor("test-player")).length, 323);
    const fullReads = many.calls.filter(call => call.name === "GetUserInternalData" &&
        !Object.hasOwn(call.request, "Keys"));
    assert.equal(fullReads.length, 1, "Totals require an unfiltered ledger read");

    const corrupt = createHarness(script);
    corrupt.dataFor("test-player")[prefix + matchId(1)] = { Value: "invalid-json" };
    assert.throws(() => corrupt.submit(result(1)), /Malformed/);
    assert.equal(corrupt.calls.some(call => call.name === "UpdateUserInternalData"), false);
    assert.throws(() => corrupt.submit(result(2)), /Malformed/);
    assert.equal(corrupt.calls.some(call => call.name === "UpdatePlayerStatistics"), false,
        "Malformed ledger entries must fail visibly, not silently lower totals");
    corrupt.dataFor("test-player")[prefix + matchId(1)] = {
        Value: JSON.stringify({ schemaVersion: 1, ...result(3) })
    };
    assert.throws(() => corrupt.submit(result(2)), /Invalid multiplayer match receipt/);
    const staleRead = createHarness(script);
    staleRead.hooks.getData = () => ({ Data: {} });
    assert.throws(() => staleRead.submit(result(1)), /could not be confirmed/);
    assert.equal(staleRead.calls.some(call => call.name === "UpdatePlayerStatistics"), false);

    const twoPlayers = createHarness(script);
    twoPlayers.submit(result(1, "win", "second-player"));
    twoPlayers.sandbox.currentPlayerId = "second-player";
    twoPlayers.submit(result(1, "loss", "test-player"));
    assert.equal(twoPlayers.statsFor("test-player").get("CC_MP_Wins"), 1);
    assert.equal(twoPlayers.statsFor("second-player").get("CC_MP_Losses"), 1);
    assert.equal(Object.keys(twoPlayers.dataFor("test-player")).length, 1);
    assert.equal(Object.keys(twoPlayers.dataFor("second-player")).length, 1);

    const leaderboard = createHarness(script);
    leaderboard.statsFor("test-player").set("CC_MP_Wins", 2);
    leaderboard.statsFor("test-player").set("CC_MP_Losses", 1);
    leaderboard.statsFor("test-player").set("CC_MP_Draws", 4);
    leaderboard.statsFor("top-player").set("CC_MP_Wins", 12);
    leaderboard.statsFor("top-player").set("CC_MP_Losses", 3);
    leaderboard.statsFor("top-player").set("CC_MP_Draws", 7);
    leaderboard.hooks.getLeaderboard = () => ({ Leaderboard: [
        { PlayFabId: "top-player", Position: 0, DisplayName: "Top Engineer", StatValue: 10 },
        { PlayFabId: "test-player", Position: 1, DisplayName: "", StatValue: 2 }
    ] });
    assert.deepEqual(plain(leaderboard.read({ playerId: "spoofed-player" })), {
        entries: [
            { rank: 1, playerId: "top-player", displayName: "Top Engineer", wins: 12, losses: 3, draws: 7 },
            { rank: 2, playerId: "test-player", displayName: "Engineer", wins: 2, losses: 1, draws: 4 }
        ],
        personal: { wins: 2, losses: 1, draws: 4 }
    });
    assert.equal(leaderboard.calls.filter(call => call.name === "GetPlayerStatistics").length, 2);
    assert.equal(leaderboard.calls.some(call => call.name.startsWith("Update")), false,
        "Reading a board must never modify scores or receipts");
    const coloredName = "<#BF40BF>.dev_hyakkimaru";
    leaderboard.hooks.getLeaderboard = () => ({ Leaderboard: [
        { PlayFabId: "top-player", Position: 0, DisplayName: coloredName }
    ] });
    assert.equal(leaderboard.read({}).entries[0].displayName, coloredName,
        "PlayFab name hex color tags must reach the client unchanged");
    leaderboard.hooks.getLeaderboard = () => ({ Leaderboard: Array.from({ length: 15 }, (_, i) => ({
        PlayFabId: "player-" + i, Position: i, DisplayName: "Engineer " + i
    })) });
    assert.equal(leaderboard.read({}).entries.length, 15);
    delete leaderboard.hooks.getLeaderboard;
    assert.deepEqual(plain(leaderboard.read({})), {
        entries: [], personal: { wins: 2, losses: 1, draws: 4 }
    }, "Personal totals must still be returned when the caller is outside the Top 15");

    const absent = createHarness(script);
    absent.hooks.getLeaderboard = () => { throw apiError("StatisticNotFound", 1195); };
    absent.hooks.getStatistics = () => { throw apiError("StatisticNotFound", 1195); };
    assert.deepEqual(plain(absent.read({})), {
        entries: [], personal: { wins: 0, losses: 0, draws: 0 }
    });
    absent.hooks.getLeaderboard = () => { throw { errorCode: 1195 }; };
    absent.hooks.getStatistics = () => { throw { error: "StatisticNotFound" }; };
    assert.equal(absent.read({}).personal.wins, 0);
    absent.hooks.getLeaderboard = () => { throw apiError("ServiceUnavailable", 1123); };
    assert.throws(() => absent.read({}), error => error.apiErrorInfo.apiError.error === "ServiceUnavailable");
    absent.hooks.getLeaderboard = () => ({ Leaderboard: [] });
    absent.hooks.getStatistics = () => { throw apiError("APIRequestLimitExceeded", 1345); };
    assert.throws(() => absent.read({}), error => error.apiErrorInfo.apiError.error === "APIRequestLimitExceeded");
    absent.hooks.getStatistics = () => { throw new Error("StatisticNotFound is only message text"); };
    assert.throws(() => absent.read({}), /only message text/,
        "Generic errors must not be mistaken for an empty leaderboard");
    absent.hooks.getStatistics = () => ({ Statistics: [{ StatisticName: "CC_MP_Wins", Value: -1 }] });
    assert.throws(() => absent.read({}), /Invalid multiplayer statistic/);
}

verifyImplementation(source);
verifyImplementation(mergedSource);
assert.ok(mergedSource.replace(/\r\n/g, "\n").endsWith(source.replace(/\r\n/g, "\n")),
    "Merged revision must append the exact multiplayer feature source");
const handlers = createHarness(mergedSource).sandbox.handlers;
assert.deepEqual(Object.keys(handlers).sort(), [
    "helloWorld", "makeAPICall", "makeEntityAPICall", "makeHTTPRequest",
    "handlePlayStreamEventAndProfile", "completedLevel", "updatePlayerMove",
    "unlockHighSkillContent", "RoomCreated", "RoomJoined", "RoomLeft",
    "RoomClosed", "RoomPropertyUpdated", "RoomEventRaised", "submitBridgeRunV1",
    "syncDashboardV1", "submitMultiplayerResultV1", "getMultiplayerLeaderboardV1"
].sort(), "Merged revision must preserve every existing handler");

console.log("Civil Craft multiplayer CloudScript tests passed (feature + merged source).");
