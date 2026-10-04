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
