using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>Exercises the production completion action gate with doubled engine/HTTP boundaries.</summary>
internal static class CompletionVisibilityChecks
{
    private static Action<bool, string> check;
    private static string Feed(params string[] ids) => "{\"schemaVersion\":1,\"contractIds\":[" +
        string.Join(",", ids.Select(id => "\"" + id + "\"")) + "]}";
    private static ContractSO Contract(string id, bool hidden = false) =>
        new() { contractID = id, name = id, hideFromLeaderboard = hidden };

    private sealed class Fixture
    {
        public readonly CompletionLeaderboardVisibility Gate = new();
        public readonly MonoBehaviour Owner = new();
        public readonly Button Button = new();
        public readonly GameObject Report = new();
        public readonly ContractSO Contract;
        public IEnumerator Iterator => Owner.Coroutines.Last().Iterator;
        public UnityWebRequest Request => UnityWebRequest.Requests.Last();
        public Fixture(string id = "ShopKeeper", bool reportActive = true)
        {
            UnityWebRequest.Requests.Clear();
            Contract = CompletionVisibilityChecks.Contract(id);
            Report.SetActive(reportActive);
            Button.gameObject.SetActive(true);
        }
        public void Begin(bool boardAvailable = true) => Gate.Begin(Owner, Button, Report, Contract, boardAvailable);
        public void Respond(string body, long status = 200,
            UnityWebRequest.Result result = UnityWebRequest.Result.Success) =>
            RespondTo(Iterator, Request, body, status, result);
        public void CheckHidden(string message) => check(!Button.gameObject.activeSelf &&
            !Button.interactable && !Gate.CanOpen(Contract), message);
        public void CheckVisible(string message) => check(Button.gameObject.activeSelf &&
            Button.interactable && Gate.CanOpen(Contract), message);
    }

    public static void Run(Action<bool, string> assert)
    {
        check = assert;
        RemoteAndLocalPolicy();
        ErrorsStayClosed();
        StaleAndChangedReports();
        LifecycleAndNestedPanels();
        CompletionManagerSourceHooks();
    }

    private static void RespondTo(IEnumerator iterator, UnityWebRequest request, string body,
        long status = 200, UnityWebRequest.Result result = UnityWebRequest.Result.Success)
    {
        request.Respond(body, status, result);
        check(!iterator.MoveNext(), "Completion visibility request finishes after response");
    }

    private static void RemoteAndLocalPolicy()
    {
        var allowed = new Fixture();
        check(!allowed.Gate.CanOpen(allowed.Contract), "Completion cannot open before a verified request");
        allowed.Begin();
        allowed.CheckHidden("Completion action is hidden and noninteractive while public policy is loading");
        check(UnityWebRequest.Requests.Count == 1, "Completion opening makes one public policy request");
        var request = allowed.Request;
        check(request.url == LeaderboardContractVisibility.FeedUrl && request.method == "GET" &&
            request.timeout == 10 && request.redirectLimit == 0,
            "Completion request shares fixed public endpoint, bounded timeout and no redirects");
        check(request.Headers.Count == 1 && request.Headers["Accept"] == "application/json",
            "Completion request sends neither authentication nor player identity");
        allowed.Respond(Feed("ShopKeeper"));
        allowed.CheckVisible("Website-approved completion reveals the authored leaderboard action");
        check(request.Disposed, "Completed visibility request is disposed");
        check(!allowed.Gate.CanOpen(Contract("ShopKeeper")),
            "A different contract object cannot reuse this report's approval even with the same ID");

        foreach (string body in new[] { Feed("ReedsContract"), Feed() })
        {
            var hidden = new Fixture(); hidden.Begin(); hidden.Respond(body);
            hidden.CheckHidden("A remotely hidden completion has no leaderboard action, including empty administrator selection");
        }

        foreach (string id in new[] { "CHALLENGE_TEST", "", " ", null })
        {
            var invalid = new Fixture(id); invalid.Begin();
            invalid.CheckHidden("Challenge, empty and missing contract IDs cannot expose completion leaderboards");
            check(UnityWebRequest.Requests.Count == 0, "Locally invalid completion does not request a public policy");
        }
        var locallyHidden = new Fixture(); locallyHidden.Contract.hideFromLeaderboard = true;
        locallyHidden.Begin(); locallyHidden.CheckHidden("Local hiding still overrides website approval");
        check(UnityWebRequest.Requests.Count == 0, "Locally hidden completion does not request a public policy");
        var unsupported = new Fixture("MarshID"); unsupported.Begin();
        if (UnityWebRequest.Requests.Count != 0) unsupported.Respond(Feed("ShopKeeper"));
        unsupported.CheckHidden("A local contract outside the website catalog cannot inherit another contract's approval");

        var noBoard = new Fixture(); noBoard.Begin(false); noBoard.CheckHidden("Missing authored leaderboard keeps the completion action hidden");
        check(UnityWebRequest.Requests.Count == 0, "Missing leaderboard does not start unnecessary public fetch");
        var noContract = new Fixture();
        noContract.Gate.Begin(noContract.Owner, noContract.Button, noContract.Report, null, true);
        noContract.CheckHidden("Missing captured report contract is rejected");
        check(UnityWebRequest.Requests.Count == 0, "Missing contract does not start public fetch");

        var beforeActivation = new Fixture(reportActive: false); beforeActivation.Begin();
        check(UnityWebRequest.Requests.Count == 1, "Completion can begin policy lookup before its report activates");
        beforeActivation.Report.SetActive(true); beforeActivation.Respond(Feed("ShopKeeper"));
        beforeActivation.CheckVisible("Report activated after request start receives its exact approved action");
    }

    private static void ErrorsStayClosed()
    {
        foreach (string body in new[] { "{}", "null", "[]", "not JSON", Feed("unknown"),
            Feed("ShopKeeper", "ShopKeeper"), new string('x', 4097),
            "{\"schemaVersion\":1,\"contractIds\":[\"ShopKeeper\"]} trailing" })
        {
            var fixture = new Fixture(); fixture.Begin(); fixture.Respond(body);
            fixture.CheckHidden("Malformed, unsupported, duplicated or oversized public evidence never reveals completion action");
        }
        foreach (var outcome in new[] {
            (0L, UnityWebRequest.Result.ConnectionError),
            (500L, UnityWebRequest.Result.ProtocolError),
            (302L, UnityWebRequest.Result.ProtocolError),
            (204L, UnityWebRequest.Result.Success),
            (200L, UnityWebRequest.Result.DataProcessingError) })
        {
            var fixture = new Fixture(); fixture.Begin(); fixture.Respond(Feed("ShopKeeper"), outcome.Item1, outcome.Item2);
            fixture.CheckHidden("Timeout, connectivity, non-200 HTTP and processing failures keep completion action hidden");
            check(fixture.Request.Disposed, "Failed request releases its network resources");
        }
        var retry = new Fixture(); retry.Begin(); retry.Respond(Feed("ShopKeeper"));
        retry.CheckVisible("Approved first report is initially visible");
        retry.Begin(); retry.CheckHidden("New check immediately clears earlier report approval");
        retry.Respond("{}", 503, UnityWebRequest.Result.ProtocolError);
        retry.CheckHidden("A later failed check never falls back to a previous verified approval");
    }

    private static void StaleAndChangedReports()
    {
        var replaced = new Fixture(); replaced.Begin();
        var olderRequest = replaced.Request; var olderIterator = replaced.Iterator;
        var target = Contract("ReedsContract");
        replaced.Gate.Begin(replaced.Owner, replaced.Button, replaced.Report, target, true);
        check(olderRequest.Aborted && olderRequest.Disposed, "Replacing completion report cancels the earlier request");
        RespondTo(olderIterator, olderRequest, Feed("ShopKeeper"));
        check(!replaced.Button.gameObject.activeSelf && !replaced.Gate.CanOpen(target),
            "Late earlier-contract response cannot expose a new report's action");
        replaced.Respond(Feed("ReedsContract"));
        check(replaced.Button.gameObject.activeSelf && replaced.Button.interactable && replaced.Gate.CanOpen(target) &&
            !replaced.Gate.CanOpen(replaced.Contract), "Only current exact completed contract receives approval");

        var changedId = new Fixture(); changedId.Begin(); changedId.Contract.contractID = "ReedsContract";
        changedId.Respond(Feed("ShopKeeper", "ReedsContract"));
        changedId.CheckHidden("Changing the same asset's ID during fetch invalidates captured report identity");

        var changedFlag = new Fixture(); changedFlag.Begin(); changedFlag.Contract.hideFromLeaderboard = true;
        changedFlag.Respond(Feed("ShopKeeper")); changedFlag.CheckHidden("Local hiding changed during fetch cannot be overridden by remote approval");

        var afterApproval = new Fixture(); afterApproval.Begin(); afterApproval.Respond(Feed("ShopKeeper"));
        afterApproval.Contract.hideFromLeaderboard = true;
        check(!afterApproval.Gate.CanOpen(afterApproval.Contract), "Direct open remains guarded if local eligibility changes after approval");
        afterApproval.Contract.hideFromLeaderboard = false; afterApproval.Contract.contractID = "ReedsContract";
        check(!afterApproval.Gate.CanOpen(afterApproval.Contract), "Verified report cannot be repurposed by changing its contract ID later");
    }

    private static void LifecycleAndNestedPanels()
    {
        var closed = new Fixture(); closed.Begin();
        var cancelledRequest = closed.Request; var cancelledIterator = closed.Iterator;
        closed.Gate.Cancel(); closed.Gate.Cancel();
        closed.CheckHidden("Cancellation is safe repeatedly and immediately removes approval");
        check(cancelledRequest.Aborted && cancelledRequest.Disposed, "Cancellation aborts and disposes pending HTTP request");
        RespondTo(cancelledIterator, cancelledRequest, Feed("ShopKeeper"));
        closed.CheckHidden("Cancelled completion cannot be re-enabled by a late response");
        closed.Begin(); closed.Respond(Feed("ShopKeeper")); closed.CheckVisible("A fresh report can approve normally after cancellation");
        closed.Gate.Cancel(); closed.CheckHidden("Cancelling an already successful report also removes its action");

        var inactiveReport = new Fixture(); inactiveReport.Begin(); inactiveReport.Report.SetActive(false);
        inactiveReport.Respond(Feed("ShopKeeper")); inactiveReport.CheckHidden("Response cannot reveal an action on a report already closed");

        var inactiveOwner = new Fixture(); inactiveOwner.Begin(); inactiveOwner.Owner.isActiveAndEnabled = false;
        inactiveOwner.Respond(Feed("ShopKeeper")); inactiveOwner.CheckHidden("A disabled owner cannot apply a late verified response");
        var initiallyDisabled = new Fixture(); initiallyDisabled.Owner.isActiveAndEnabled = false;
        initiallyDisabled.Begin(); initiallyDisabled.CheckHidden("Disabled owner cannot start completion approval");
        check(UnityWebRequest.Requests.Count == 0, "Initially disabled owner starts no HTTP request");

        var nested = new Fixture(); nested.Begin(); nested.Respond(Feed("ShopKeeper"));
        nested.Report.activeInHierarchy = false;
        check(nested.Gate.CanOpen(nested.Contract), "Nested hierarchy hiding preserves approval; scene manager owns top-panel interaction gate");
        nested.Report.activeInHierarchy = true;
        nested.CheckVisible("Temporarily obscuring report under nested UI does not lose exact-contract approval");
        nested.Report.SetActive(false);
        check(!nested.Gate.CanOpen(nested.Contract), "Closed report cannot invoke leaderboard directly even if approval existed");

        var missingOwner = new Fixture();
        missingOwner.Gate.Begin(null, missingOwner.Button, missingOwner.Report, missingOwner.Contract, true);
        missingOwner.CheckHidden("Missing coroutine owner fails closed");
        var missingReport = new Fixture();
        missingReport.Gate.Begin(missingReport.Owner, missingReport.Button, null, missingReport.Contract, true);
        missingReport.CheckHidden("Missing report fails closed");
        var missingButton = new Fixture();
        missingButton.Gate.Begin(missingButton.Owner, null, missingButton.Report, missingButton.Contract, true);
        check(!missingButton.Gate.CanOpen(missingButton.Contract) && UnityWebRequest.Requests.Count == 0,
            "Missing authored button never enables direct access or starts HTTP");
    }

    // The large scene manager is not replaced with a misleading engine double.
    // These are explicit source-wiring checks only; native lifecycle remains a Unity test.
    private static void CompletionManagerSourceHooks()
    {
        DirectoryInfo root = new(AppContext.BaseDirectory);
        const string relative = "Assets/Script/Level/LevelCompleteManager.cs";
        while (root != null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        check(root != null, "Production completion manager source is available for integration wiring checks");
        string source = File.ReadAllText(Path.Combine(root.FullName, relative));
        foreach (string signature in new[] { "private void OnDestroy()", "public void ResetCompletionState()", "public void ClosePanel()", "private void OnDisable()" })
            check(MethodText(source, signature).Contains("completionLeaderboardVisibility.Cancel()", StringComparison.Ordinal),
                "Completion manager cancels pending approval in " + signature);

        string complete = MethodText(source, "public void CompleteLevelForVehicle(");
        check(complete.Contains("activeContract = currentContract", StringComparison.Ordinal) &&
            complete.Contains("StartCoroutine(TakeSnapshotAndShowUIRoutine(", StringComparison.Ordinal) &&
            complete.IndexOf("activeContract =", StringComparison.Ordinal) <
            complete.IndexOf("StartCoroutine(TakeSnapshotAndShowUIRoutine(", StringComparison.Ordinal),
            "Regular completion captures report contract before starting snapshot/report coroutine");
        foreach (string signature in new[] { "private IEnumerator TakeSnapshotAndShowUIRoutine(", "public void ShowGuestSessionCompletion(" })
        {
            string method = MethodText(source, signature);
            check(method.Contains("UpdateCompletedLocationLeaderboardButton()", StringComparison.Ordinal),
                "Regular and guest completion report starts exact-contract visibility check: " + signature);
            check(method.Contains("levelCompletePanel.SetActive(true)", StringComparison.Ordinal) &&
                method.IndexOf("UpdateCompletedLocationLeaderboardButton()", StringComparison.Ordinal) <
                method.IndexOf("levelCompletePanel.SetActive(true)", StringComparison.Ordinal),
                "Regular and guest report start approval before showing authored UI: " + signature);
        }
        string guest = MethodText(source, "public void ShowGuestSessionCompletion(");
        check(guest.Contains("activeContract = location.activeContract", StringComparison.Ordinal) &&
            guest.IndexOf("activeContract = location.activeContract", StringComparison.Ordinal) <
            guest.IndexOf("UpdateCompletedLocationLeaderboardButton()", StringComparison.Ordinal),
            "Guest completion captures the exact session result contract before requesting approval");
        string update = MethodText(source, "private void UpdateCompletedLocationLeaderboardButton()");
        check(update.Contains("completionLeaderboardVisibility.Begin(this, completedLocationLeaderboardButton,", StringComparison.Ordinal) &&
            update.Contains("levelCompletePanel, activeContract, completedLocationLeaderboard != null)", StringComparison.Ordinal),
            "Visibility gate receives the authored action, report, captured contract and board availability");
        string open = MethodText(source, "public void OpenCompletedLocationLeaderboard()");
        check(open.Contains("!levelCompletePanel.activeInHierarchy", StringComparison.Ordinal) &&
            open.Contains("!completionLeaderboardVisibility.CanOpen(activeContract)", StringComparison.Ordinal) &&
            open.Contains("IsFirstCompletionTutorialBlockingActions()", StringComparison.Ordinal),
            "Public open action retains report, tutorial and verified-contract guards");
        check(open.Contains("completedLocationLeaderboard.OpenForContract(activeContract)", StringComparison.Ordinal),
            "Verified completion action still opens the exact captured contract through fresh board validation");
    }

    private static string MethodText(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        check(start >= 0, "Expected production integration method exists: " + signature);
        int bodyStart = source.IndexOf('{', start);
        int expressionStart = source.IndexOf("=>", start, StringComparison.Ordinal);
        if (expressionStart >= 0 && expressionStart < bodyStart)
            return source.Substring(start, source.IndexOf(';', expressionStart) - start + 1);
        int depth = 0;
        for (int index = bodyStart; index < source.Length; ++index)
        {
            if (source[index] == '{') ++depth;
            else if (source[index] == '}' && --depth == 0) return source.Substring(start, index - start + 1);
        }
        throw new InvalidOperationException("Could not find integration method body: " + signature);
    }
}
