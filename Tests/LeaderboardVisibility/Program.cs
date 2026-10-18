using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using PlayFab;
using PlayFab.ClientModels;

internal static class Program
{
    private static int checks;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string Feed(params string[] ids) => "{\"schemaVersion\":1,\"contractIds\":[" +
        string.Join(",", ids.Select(id => "\"" + id + "\"")) + "]}";
    private static int Main()
    {
        try
        {
            Parser(); Opening(); CompletionSelection(); FailuresAndMultiplayer(); CloseAndReopen(); ManualRefresh();
            CompletionVisibilityChecks.Run(Check);
            Console.WriteLine($"LeaderboardVisibility: {checks} checks passed (production parser/controller/completion action gate; engine, HTTP and PlayFab boundaries doubled; no live requests or player saves).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    private static ContractSO Contract(string id, string name = null, bool hidden = false) => new() { contractID = id, name = name ?? id, hideFromLeaderboard = hidden };
    private static LeaderboardPanelUI Board(params ContractSO[] contracts)
    {
        UnityWebRequest.Requests.Clear(); PlayFabClientAPI.Leaderboards.Clear(); PlayFabClientAPI.LeaderboardSuccess.Clear(); PlayFabClientAPI.Multiplayer.Clear();
        var board = new LeaderboardPanelUI();
        Set(board, "panel", new GameObject());
        Set(board, "contractDropdown", new TMP_Dropdown());
        Set(board, "statusText", new TMP_Text()); Set(board, "descriptionText", new TMP_Text()); Set(board, "personalBestText", new TMP_Text());
        Set(board, "efficientToggle", new Toggle { isOn = true }); Set(board, "strongestToggle", new Toggle());
        Set(board, "contracts", contracts.ToList());
        Call(board, "Awake");
        return board;
    }
    private static string Status(LeaderboardPanelUI board) => Get<TMP_Text>(board, "statusText").text;
    private static TMP_Dropdown Dropdown(LeaderboardPanelUI board) => Get<TMP_Dropdown>(board, "contractDropdown");
    private static ContractSO Selected(LeaderboardPanelUI board) => (ContractSO)board.GetType().GetProperty("SelectedContract", Fields).GetValue(board);
    private static IEnumerator ActiveFetch(LeaderboardPanelUI board) => Get<Coroutine>(board, "visibilityRoutine").Iterator;
    private static void Respond(LeaderboardPanelUI board, string body, long code = 200, UnityWebRequest.Result result = UnityWebRequest.Result.Success)
    {
        var iterator = ActiveFetch(board);
        UnityWebRequest.Requests.Last().Respond(body, code, result);
        Check(!iterator.MoveNext(), "Feed coroutine completes after response");
    }
    private static void Parser()
    {
        Check(LeaderboardContractVisibility.TryParse(Feed(), out var empty) && empty.Count == 0, "Empty administrator selection is valid and stays empty");
        Check(LeaderboardContractVisibility.TryParse(Feed("ShopKeeper", "TUT_CONTRACT1"), out var two) && two.SetEquals(new[] { "ShopKeeper", "TUT_CONTRACT1" }), "Supported IDs parse");
        foreach (string invalid in new[] {
            "", "{}", "null", "[]", "{\"schemaVersion\":2,\"contractIds\":[]}",
            "{\"schemaVersion\":\"1\",\"contractIds\":[]}", "{\"schemaVersion\":1.0,\"contractIds\":[]}",
            "{\"schemaVersion\":1,\"contractIds\":null}", "{\"schemaVersion\":1,\"contractIds\":[null]}",
            Feed("unknown"), Feed("shopkeeper"), Feed("ShopKeeper", "ShopKeeper"),
            Feed("ShopKeeper") + " trailing", "{\"schemaVersion\":1,\"schemaVersion\":1,\"contractIds\":[]}",
            "{\"schemaVersion\":1,\"contractIds\":[],}", new string(' ', 4097) + Feed()
        }) Check(!LeaderboardContractVisibility.TryParse(invalid, out var rejected) && rejected == null, "Malformed/unsupported feed fails closed");
        var supported = new[] { "ShopKeeper", "TUT_CONTRACT1", "ReedsContract", "VancesContract", "TUT_CONTRACT2", "ReedSideInspection", "VanceSideRealignment", "SilasMainContract", "MainContractSilas" };
        Check(LeaderboardContractVisibility.TryParse(Feed(supported), out var all) && all.Count == 9, "All current website IDs supported");
    }
    private static void Opening()
    {
        var allowed = Contract("ShopKeeper", "Shop");
        var board = Board(allowed, Contract("TUT_CONTRACT1"), Contract("VancesContract", hidden: true), Contract("CHALLENGE_QA"));
        board.Open();
        Check(Dropdown(board).options.Count == 0 && !Dropdown(board).interactable, "No unapproved choices while fetching");
        Check(Status(board).Contains("Loading leaderboard contracts") && PlayFabClientAPI.Leaderboards.Count == 0, "No story request before verified policy");
        var request = UnityWebRequest.Requests.Single();
        Check(request.url == LeaderboardContractVisibility.FeedUrl && request.method == "GET" && request.timeout == 10 && request.redirectLimit == 0, "Public feed uses fixed HTTPS endpoint, bounded timeout, no redirects");
        Check(request.Headers.Count == 1 && request.Headers["Accept"] == "application/json", "Public feed sends no player credentials");
        Respond(board, Feed("ShopKeeper", "VancesContract"));
        Check(Dropdown(board).options.Count == 1 && Selected(board) == allowed && Dropdown(board).interactable, "Remote allowlist intersects local hiding");
        Check(PlayFabClientAPI.Leaderboards.Count == 1 && PlayFabClientAPI.Leaderboards[0].StatisticName == "ShopKeeper_COST", "Only selected approved story board requests rankings");
        Call(board, "OnRankingChanged", true);
        Check(UnityWebRequest.Requests.Count == 1, "Ranking/category changes reuse this opening policy");
    }
    private static void CompletionSelection()
    {
        var first = Contract("ShopKeeper", "Same name"); var target = Contract("ReedsContract", "Same name");
        var board = Board(first);
        Check((bool)Call(board, "TrySelectContract", Contract("COMPLETION_QA_TARGET", "QA")), "Offline exact selection supports synthetic authoring IDs");
        Check(UnityWebRequest.Requests.Count == 0 && PlayFabClientAPI.Leaderboards.Count == 0 && !Get<GameObject>(board, "panel").activeSelf, "Offline authoring never opens panel or performs network");
        Check(!(bool)Call(board, "TrySelectContract", Contract("VancesContract", hidden: true)) && !(bool)Call(board, "TrySelectContract", Contract("CHALLENGE_QA")), "Local hidden and challenge completion rejected");
        Check(board.OpenForContract(target), "Completed contract missing from local authored catalog can open");
        Respond(board, Feed("ShopKeeper"));
        Check(PlayFabClientAPI.Leaderboards.Count == 0 && Status(board).Contains("disabled for this contract"), "Hidden completion never falls back to unrelated approved board");
        Call(board, "OnSelectionChanged", 0);
        Check(PlayFabClientAPI.Leaderboards.Count == 1 && PlayFabClientAPI.Leaderboards.Last().StatisticName == "ShopKeeper_COST", "Only explicit user selection can change away from a hidden completed contract");
        PlayFabClientAPI.Leaderboards.Clear();
        board.CloseImmediately();
        Check(board.OpenForContract(target), "A new opening rechecks previously hidden completed contract");
        Respond(board, Feed("ReedsContract", "ShopKeeper"));
        Check(Selected(board) == target && PlayFabClientAPI.Leaderboards.Single().StatisticName == "ReedsContract_COST", "Allowed completed contract selected by exact ID rather than display name");
    }
    private static void FailuresAndMultiplayer()
    {
        foreach (var body in new[] { "{}", Feed(), new string('x', 5000) })
        {
            var board = Board(Contract("ShopKeeper")); board.Open(); Respond(board, body);
            Check(PlayFabClientAPI.Leaderboards.Count == 0 && Dropdown(board).options.Count == 0 && !Dropdown(board).interactable, "Invalid/empty/oversized feed never shows default story boards");
            Check(Status(board).Contains(body == Feed() ? "No contracts selected" : "unavailable"), "Empty and unavailable states are distinguished");
            board.SelectMultiplayerBoard();
            Check(PlayFabClientAPI.Multiplayer.Count == 1 && Status(board).Contains("Loading multiplayer"), "Multiplayer remains available independent of story visibility");
        }
        var pending = Board(Contract("ShopKeeper")); pending.Open(); pending.SelectMultiplayerBoard();
        Respond(pending, "{}", 503, UnityWebRequest.Result.ProtocolError);
        Check(PlayFabClientAPI.Multiplayer.Count == 1 && Status(pending).Contains("Loading multiplayer"), "Slower failed policy does not clear or duplicate multiplayer load");
        pending.SelectStoryBoard(); Check(Status(pending).Contains("unavailable"), "Returning to story shows policy failure");
        var parallel = Board(Contract("ShopKeeper")); parallel.Open(); parallel.SelectMultiplayerBoard();
        Respond(parallel, Feed("ShopKeeper"));
        Check(PlayFabClientAPI.Multiplayer.Count == 1 && PlayFabClientAPI.Leaderboards.Count == 0 && Status(parallel).Contains("Loading multiplayer"), "Slower successful policy does not clear or restart multiplayer");
        parallel.SelectStoryBoard();
        Check(PlayFabClientAPI.Leaderboards.Count == 1, "Returning to story uses this opening's verified policy");
        var offline = Board(Contract("ShopKeeper")); offline.Open();
        Respond(offline, Feed("ShopKeeper"), 0, UnityWebRequest.Result.ConnectionError);
        Check(PlayFabClientAPI.Leaderboards.Count == 0 && Status(offline).Contains("unavailable"), "Connection failure does not allow stale story choices");
    }
    private static void CloseAndReopen()
    {
        var board = Board(Contract("ShopKeeper"), Contract("ReedsContract")); board.Open();
        IEnumerator old = ActiveFetch(board); UnityWebRequest oldRequest = UnityWebRequest.Requests.Last();
        board.CloseImmediately();
        Check(oldRequest.Aborted && oldRequest.Disposed, "Closing cancels public request");
        board.Open(); oldRequest.Respond(Feed("ShopKeeper")); old.MoveNext();
        Check(PlayFabClientAPI.Leaderboards.Count == 0 && Dropdown(board).options.Count == 0, "Late earlier-opening policy ignored after reopen");
        Respond(board, Feed("ReedsContract"));
        Check(Selected(board).ContractID == "ReedsContract", "Current opening applies its own policy");
        Call(board, "OnDisable");
        Check(UnityWebRequest.Requests.Last().Disposed, "Disabling controller cancels public policy lifecycle");
        Action<GetLeaderboardResult> lateRank = PlayFabClientAPI.LeaderboardSuccess.Last();
        board.CloseImmediately(); lateRank(new GetLeaderboardResult { Leaderboard = new() });
        Check(!Get<GameObject>(board, "panel").activeSelf, "Late leaderboard result cannot reopen closed panel");
    }
    private static void ManualRefresh()
    {
        var board = Board(Contract("ShopKeeper")); board.Open(); Respond(board, Feed("ShopKeeper"));
        board.RefreshRankings();
        Check(UnityWebRequest.Requests.Count == 2 && Dropdown(board).options.Count == 0 && !Dropdown(board).interactable, "Manual refresh clears old policy before fetching once");
        PlayFabClientAPI.LeaderboardSuccess.Last()(new GetLeaderboardResult { Leaderboard = new() });
        Check(Status(board).Contains("Loading leaderboard contracts"), "Late old story response cannot replace public policy loading state");
        Respond(board, Feed());
        Check(PlayFabClientAPI.Leaderboards.Count == 1 && Dropdown(board).options.Count == 0, "Admin clearing selection removes board without requesting old statistics");
        board.CloseImmediately(); board.RefreshRankings();
        Check(UnityWebRequest.Requests.Count == 2, "Manual refresh while closed does nothing");
    }
}
