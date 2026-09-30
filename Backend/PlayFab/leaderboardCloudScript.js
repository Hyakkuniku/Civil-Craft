// Civil Craft - legacy PlayFab CloudScript (server-side, not a Unity asset).
// Merge this handler into the title's existing CloudScript revision before upload.
// Both statistic definitions must use Max aggregation; a larger packed value is
// a better bridge. Do not enable client-side statistic writes.
//
// These are the current playable ContractSO IDs and the FNV-1a names produced
// by LeaderboardScoreCodec. Add a new contract here before enabling its online
// leaderboard. Unknown IDs are deliberately rejected rather than accepted from
// client-supplied statistic names.
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

    // A successful CloudScript call confirms receipt, not authoritative proof
    // that the client-side bridge simulation was genuine. A verified competitive
    // board requires server-side replay/validation of the submitted bridge.
    return { accepted: true, updated: updates.length };
};
