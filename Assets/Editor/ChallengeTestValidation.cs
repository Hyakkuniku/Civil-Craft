using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ChallengeTestValidation
{
    private static double nextPreparationCheck;
    [InitializeOnLoadMethod]
    private static void WatchGuestPreparation()
    {
        EditorApplication.update -= CheckGuestPreparation;
        EditorApplication.update += CheckGuestPreparation;
    }
    private static void CheckGuestPreparation()
    {
        if (EditorApplication.timeSinceStartup < nextPreparationCheck) return;
        nextPreparationCheck = EditorApplication.timeSinceStartup + 2;
        const string request = "Temp/challenge-guest-introduction-order-validation.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool hasScene = false;
        foreach (var ui in Resources.FindObjectsOfTypeAll<MultiplayerChallengeLobbyUI>())
            if (ui.gameObject.scene.name == "CanyonCrossing") { hasScene = true; break; }
        File.Delete(request);
        var report = new StringBuilder();
        try
        {
            ValidateGuestPreparationOrdering(report);
            ValidateChunkTransfer(report);
            ValidatePolicies(report);
            if (!EditorApplication.isPlayingOrWillChangePlaymode) ValidateReliableRouting(report);
            // The packet/state checks operate on value copies only and are safe
            // even in Play Mode. Object/scene fixture checks require Play stopped.
            if (hasScene && !EditorApplication.isPlayingOrWillChangePlaymode) HostWorldBridgeSyncValidation.Validate();
            report.AppendLine("PASS: Native Unity preparation-order checks complete; live two-peer introductions still need a play test.");
            File.WriteAllText("Temp/ChallengeGuestPreparationValidation.txt", report.ToString());
            Debug.Log("[Challenge preparation validation] PASS. Report: Temp/ChallengeGuestPreparationValidation.txt");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            File.WriteAllText("Temp/ChallengeGuestPreparationValidation.txt", report.ToString());
            Debug.LogException(error);
        }
    }
    public static void Run(Transform fixture, StringBuilder report)
    { ValidatePolicies(report); ValidateGuestPreparationOrdering(report); ValidateChunkTransfer(report); ValidateReliableRouting(report); ValidateIsolatedRunAndView(fixture, report); ValidateResultsTextFit(fixture, report); }

    public static void ValidateChunkTransfer(StringBuilder report)
    {
        // Invoke the production assembler, not a duplicate implementation.
        Type type = typeof(FusionChallengeTestSync).Assembly.GetType("ChallengeTestPacketAssembly", true);
        MethodInfo add = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo clear = type.GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic);
        int chunkBytes = (int)type.GetField("ChunkBytes", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        int maxBytes = (int)type.GetField("MaximumBytes", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        object assembly = Activator.CreateInstance(type, true);
        Func<int, int, int, int, byte[], byte[]> push = (rev, test, total, offset, chunk) =>
        {
            var args = new object[] { rev, test, total, offset, chunk, null };
            Check((bool)add.Invoke(assembly, args), "Valid RPC transfer chunk was rejected.");
            return (byte[])args[5];
        };
        foreach (int size in new[] { 1, 319, 320, 321, 497, 4097, maxBytes })
        {
            clear.Invoke(assembly, null);
            var original = new byte[size]; for (int i = 0; i < size; i++) original[i] = (byte)(i * 31);
            byte[] complete = null;
            int chunks = (size + chunkBytes - 1) / chunkBytes;
            // Reverse arrival order, with identical duplicates and a missing
            // first chunk: no descriptor may be exposed before all bytes exist.
            for (int slot = chunks - 1; slot >= 0; slot--)
            {
                int offset = slot * chunkBytes;
                var chunk = new byte[Math.Min(chunkBytes, size - offset)]; Array.Copy(original, offset, chunk, 0, chunk.Length);
                complete = push(27, 1, size, offset, chunk);
                if (slot > 0) Check(complete == null, "Partial RPC packet was published.");
                Check(push(27, 1, size, offset, chunk) == null, "Duplicate chunk published twice.");
            }
            Check(complete != null && complete.Length == original.Length, "Complete RPC packet missing.");
            for (int i = 0; i < size; i++) Check(complete[i] == original[i], "Chunk reassembly changed the bridge bytes.");
        }
        clear.Invoke(assembly, null);
        Func<int, int, int, int, byte[], bool> rejects = (rev, test, total, offset, chunk) =>
            !(bool)add.Invoke(assembly, new object[] { rev, test, total, offset, chunk, null });
        Check(rejects(0, 1, 1, 0, new byte[1]) && rejects(1, 3, 1, 0, new byte[1]) &&
            rejects(1, 1, maxBytes + 1, 0, new byte[320]) && rejects(1, 1, 100, -1, new byte[100]) &&
            rejects(1, 1, 100, 1, new byte[99]) && rejects(1, 1, 100, 0, new byte[101]) &&
            rejects(1, 1, 100, 0, null), "Malformed RPC chunk accepted.");
        push(27, 1, 640, 0, new byte[320]);
        var changed = new byte[320]; changed[0] = 1;
        Check(rejects(27, 1, 640, 0, changed) && rejects(27, 1, 641, 320, new byte[320]), "Conflicting retry changed immutable bytes/size.");
        Check(push(27, 2, 1, 0, new byte[] { 9 })[0] == 9 && push(28, 1, 1, 0, new byte[] { 8 })[0] == 8,
            "Next test/revision reused old assembly.");
        report.AppendLine("PASS: Production reliable RPC assembler round-trips 1..maximum-sized bridge/final packets, including the reported 497-byte descriptor; reverse arrival, duplicates, missing chunks, conflicts, malformed bounds and test/revision isolation pass. Live Photon delivery still requires two-peer testing.");
    }

    public static void ValidateReliableRouting(StringBuilder report)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Reliable Callback Routing Check"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        var other = new GameObject("Other Runner Routing Check"); other.SetActive(false);
        SceneManager.MoveGameObjectToScene(other, scene);
        try
        {
            NetworkRunner runner = root.AddComponent<NetworkRunner>();
            FusionSessionCallbacks callbacks = root.AddComponent<FusionSessionCallbacks>();
            typeof(FusionSessionCallbacks).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(callbacks, null);
            var cache = typeof(FusionSessionCallbacks).GetField("challengeTests", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(cache.GetValue(callbacks) == null, "Fixture did not reproduce the missing Awake-cached test receiver.");
            FusionChallengeTestSync receiver = root.AddComponent<FusionChallengeTestSync>();
            typeof(FusionChallengeTestSync).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(receiver, null);
            callbacks.OnReliableDataReceived(runner, PlayerRef.FromIndex(1), ReliableKey.FromInts(FusionChallengeTestSync.MessageTag, 1, 27, 1), new byte[] { 1, 2, 3 });
            var receipt = typeof(FusionChallengeTestSync).GetField("presentationReceipt", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(ReferenceEquals(cache.GetValue(callbacks), receiver) && ((string)receipt.GetValue(receiver)).StartsWith("callback:27/1/3B"),
                "A descriptor is silently lost when the receiver was added after callback Awake.");
            var foreign = other.AddComponent<FusionChallengeTestSync>();
            cache.SetValue(callbacks, foreign);
            callbacks.OnReliableDataReceived(runner, PlayerRef.FromIndex(1), ReliableKey.FromInts(FusionChallengeTestSync.MessageTag, 3, 27, 1), new byte[] { 1 });
            Check(ReferenceEquals(cache.GetValue(callbacks), receiver) && (string)receipt.GetValue(foreign) == "none",
                "A recovery request routes through a stale or different-runner receiver.");
            var presentation = typeof(FusionChallengeTestSync).GetField("pendingPresentation", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(presentation.GetValue(receiver) == null, "Routing a packet on a stopped runner bypasses lifecycle validation.");
            report.AppendLine("PASS: Actual reliable callback dispatch reaches the correct runner's test handler with a missing/stale Awake cache; both descriptor and recovery-request tags route independently; stopped-runner lifecycle checks still reject view construction. No Photon room was created.");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(other); EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void ValidateGuestPreparationOrdering(StringBuilder report)
    {
        var policy = typeof(FusionChallengeTestSync).GetMethod("CanBufferTestPacket", BindingFlags.Static | BindingFlags.NonPublic);
        Func<MultiplayerChallengeState, int, int, bool, bool> accepts = (state, revision, index, final) =>
            (bool)policy.Invoke(null, new object[] { state, revision, index, final });
        var guestState = new MultiplayerChallengeState { Revision = 27, Phase = MultiplayerChallengePhase.Building };
        // Reproduce the transport ordering: descriptor FIRST, state snapshot later.
        // Previously the receive phase gate discarded this reliable transfer forever.
        Check(accepts(guestState, 27, 1, false), "Guest discarded test 1 while its snapshot still showed Building.");
        Check(!accepts(guestState, 26, 1, false) && !accepts(guestState, 28, 1, false) &&
            !accepts(guestState, 27, 2, false) && !accepts(guestState, 27, 1, true), "Early buffering admits stale/future challenges or premature final frames.");
        guestState.Phase = MultiplayerChallengePhase.SubmissionsReady;
        Check(accepts(guestState, 27, 1, false), "SubmissionsReady lost its pending descriptor.");
        guestState.Phase = MultiplayerChallengePhase.PreparingTest; guestState.TestIndex = 1;
        Check(accepts(guestState, 27, 1, false) && accepts(guestState, 27, 1, true) && accepts(guestState, 27, 2, false) &&
            !accepts(guestState, 27, 2, true), "First preparation loses a buffered next descriptor or admits its premature final frame.");
        guestState.Phase = MultiplayerChallengePhase.Testing;
        Check(accepts(guestState, 27, 2, false) && !accepts(guestState, 27, 2, true), "Next-test transfer ordering is broken.");
        guestState.Phase = MultiplayerChallengePhase.PreparingTest; guestState.TestIndex = 2;
        Check(accepts(guestState, 27, 2, false) && !accepts(guestState, 27, 1, false) && !accepts(guestState, 27, 1, true),
            "Old test packets can replace the second view.");
        foreach (MultiplayerChallengePhase phase in new[] { MultiplayerChallengePhase.None, MultiplayerChallengePhase.Invited,
            MultiplayerChallengePhase.Teleporting, MultiplayerChallengePhase.Lobby, MultiplayerChallengePhase.Countdown,
            MultiplayerChallengePhase.ReadyToBuild, MultiplayerChallengePhase.TestResults })
        {
            guestState.Phase = phase;
            Check(!accepts(guestState, 27, 1, false) && !accepts(guestState, 27, 2, true), "Packets escape the active test lifecycle: " + phase);
        }
        report.AppendLine("PASS: Guest descriptor-before-state race is reproduced and accepted while Building/SubmissionsReady; preparing/testing and next-test ordering pass. Wrong revisions, old tests, premature final frames and out-of-lifecycle packets remain rejected. Physics still requires guest view readiness.");
        var servePolicy = typeof(FusionChallengeTestSync).GetMethod("CanServePresentation", BindingFlags.Static | BindingFlags.NonPublic);
        PlayerRef guest = PlayerRef.FromIndex(1), stranger = PlayerRef.FromIndex(2);
        Func<MultiplayerChallengeState, PlayerRef, int, int, int, int, bool> serves = (state, sender, rev, test, cachedRev, cachedTest) =>
            (bool)servePolicy.Invoke(null, new object[] { state, sender, rev, test, cachedRev, cachedTest });
        guestState = new MultiplayerChallengeState { Revision = 27, Guest = guest, Phase = MultiplayerChallengePhase.PreparingTest, TestIndex = 1 };
        Check(serves(guestState, guest, 27, 1, 27, 1), "Guest cannot recover the missed first descriptor after entering preparation.");
        Check(!serves(guestState, stranger, 27, 1, 27, 1) && !serves(guestState, PlayerRef.None, 27, 1, 27, 1) &&
            !serves(guestState, guest, 26, 1, 27, 1) && !serves(guestState, guest, 27, 2, 27, 1) &&
            !serves(guestState, guest, 27, 1, 26, 1) && !serves(guestState, guest, 27, 1, 27, 2),
            "Descriptor recovery leaks another participant, revision, or test's cache.");
        var before = guestState;
        Check(!MultiplayerChallengeRules.TryStartTest(ref before, 27, 1), "Requesting a descriptor bypasses view readiness.");
        guestState.GuestTestViewReady = true;
        Check(!serves(guestState, guest, 27, 1, 27, 1), "Descriptor retransmission continues after acknowledgement.");
        guestState.GuestTestViewReady = false; guestState.TestIndex = 2;
        Check(serves(guestState, guest, 27, 2, 27, 2) && !serves(guestState, guest, 27, 1, 27, 1), "Second test recovery reuses the first design.");
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        {
            if (phase == MultiplayerChallengePhase.PreparingTest) continue;
            guestState.Phase = phase;
            Check(!serves(guestState, guest, 27, 2, 27, 2), "View recovery is allowed outside preparation: " + phase);
        }
        report.AppendLine("PASS: Missed descriptor recovery serves only the authenticated guest's current prepared test; rejects stale caches, revisions, indices, strangers and all other phases; stops after readiness and never releases physics on a request alone.");
    }

    public static void ValidatePolicies(StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1);
        foreach (bool guestFirst in new[] { false, true })
        {
            var state = new MultiplayerChallengeState { Revision = 12, Phase = MultiplayerChallengePhase.Building, Guest = guest,
                ChallengeBudget = 1000,
                HostArrived = true, GuestArrived = true, HostReady = true, GuestReady = true, HostBuilding = true, GuestBuilding = true };
            var first = guestFirst ? guest : host; var second = guestFirst ? host : guest;
            Check(MultiplayerChallengeRules.TryAcceptSubmission(ref state, first, host, 12, 100, 1), "First submission rejected.");
            var copy = state;
            Check(!MultiplayerChallengeRules.TryPrepareTest(ref copy, 12, 1), "Testing began before both submitted.");
            Check(MultiplayerChallengeRules.TryAcceptSubmission(ref state, second, host, 12, 120, 2) &&
                state.HostSubmissionOrder == (guestFirst ? 2 : 1) && state.GuestSubmissionOrder == (guestFirst ? 1 : 2), "Submission ordering was not authoritative.");
            copy = state; Check(!MultiplayerChallengeRules.TryPrepareTest(ref copy, 11, 1) &&
                !MultiplayerChallengeRules.TryPrepareTest(ref copy, 12, 2), "Stale or out-of-order test began.");
            Check(MultiplayerChallengeRules.TryPrepareTest(ref state, 12, 1) &&
                MultiplayerChallengeRules.TestsHostBridge(state) == !guestFirst, "First accepted player was not tested first.");
            copy = state; Check(!MultiplayerChallengeRules.TryStartTest(ref copy, 12, 1), "Host started before the guest view was ready.");
            state.GuestTestViewReady = true;
            Check(MultiplayerChallengeRules.TryStartTest(ref state, 12, 1), "Prepared test failed to start.");
            copy = state; Check(!MultiplayerChallengeRules.TryPrepareTest(ref copy, 12, 2), "Second bridge tested before first result.");
            foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                copy = state; Check(!MultiplayerChallengeRules.TryRecordTest(ref copy, 12, 1, ChallengeTestOutcome.Crossed, bad, 0.5f), "Invalid duration accepted.");
                copy = state; Check(!MultiplayerChallengeRules.TryRecordTest(ref copy, 12, 1, ChallengeTestOutcome.Crossed, 2f, bad), "Invalid peak accepted.");
            }
            Check(MultiplayerChallengeRules.TryRecordTest(ref state, 12, 1, ChallengeTestOutcome.Crossed, 3f, 0.4f) &&
                (guestFirst ? state.GuestTestOutcome : state.HostTestOutcome) == ChallengeTestOutcome.Crossed &&
                (guestFirst ? state.HostTestOutcome : state.GuestTestOutcome) == ChallengeTestOutcome.None, "First test overwrote the opponent result.");
            copy = state; Check(!MultiplayerChallengeRules.TryRecordTest(ref copy, 12, 1, ChallengeTestOutcome.Fell, 1, 1) &&
                !MultiplayerChallengeRules.TryFinishTests(ref copy, 12), "Duplicate result replaced the first, or results appeared early.");
            Check(MultiplayerChallengeRules.TryPrepareTest(ref state, 12, 2) &&
                !state.GuestTestViewReady && MultiplayerChallengeRules.TestsHostBridge(state) == guestFirst,
                "Second test did not reset readiness or use the remaining bridge.");
            copy = state; Check(!MultiplayerChallengeRules.TryStartTest(ref copy, 12, 2), "Second test skipped spectator readiness.");
            state.GuestTestViewReady = true;
            Check(MultiplayerChallengeRules.TryStartTest(ref state, 12, 2) &&
                MultiplayerChallengeRules.TryRecordTest(ref state, 12, 2, ChallengeTestOutcome.Collapsed, 2f, 1f) &&
                state.Phase == MultiplayerChallengePhase.Testing && MultiplayerChallengeRules.TryFinishTests(ref state, 12) &&
                state.Phase == MultiplayerChallengePhase.TestResults, "Both outcomes failed to finish after the final-view gate.");
            Check(state.HostTestOutcome == (guestFirst ? ChallengeTestOutcome.Collapsed : ChallengeTestOutcome.Crossed) &&
                state.GuestTestOutcome == (guestFirst ? ChallengeTestOutcome.Crossed : ChallengeTestOutcome.Collapsed), "Result ownership swapped.");
            Check(state.Winner == (guestFirst ? ChallengeWinner.Guest : ChallengeWinner.Host) &&
                (guestFirst ? state.GuestScoreHundredths : state.HostScoreHundredths) > 0 &&
                (guestFirst ? state.HostScoreHundredths : state.GuestScoreHundredths) == 0,
                "The successful owner did not receive the authoritative winner/score.");
            copy = state;
            Check(!MultiplayerChallengeRules.TryFinishTests(ref copy, 12) && copy.Winner == state.Winner &&
                copy.HostScoreHundredths == state.HostScoreHundredths && copy.GuestScoreHundredths == state.GuestScoreHundredths,
                "A duplicate finish replaced published results.");
        }
        report.AppendLine("PASS: Host-first AND guest-first submissions test in authoritative order only after both submit; each test waits for guest readiness; stale/duplicate results cannot overwrite either design; results follow both tests.");
        ValidateCompetitionScoring(report);
    }

    private static void ValidateCompetitionScoring(StringBuilder report)
    {
        var host = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, 100000, 200000, 0.30f);
        var guest = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, 140000, 200000, 0.15f);
        Check(host.Qualified && guest.Qualified && host.Hundredths == 6200 && guest.Hundredths == 6300 &&
            ChallengeCompetitionScoring.Compare(host, guest) == ChallengeWinner.Guest &&
            ChallengeCompetitionScoring.Compare(guest, host) == ChallengeWinner.Host,
            "40% cost / 60% strength example did not score 62 versus 63 symmetrically.");
        var low = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, 200000, 200000, 1f);
        Check(low.Qualified && low.Hundredths == 0 && ChallengeCompetitionScoring.Compare(low, low) == ChallengeWinner.Draw,
            "Two eligible zero scores should draw, not become no-winner.");
        foreach (ChallengeTestOutcome failure in new[] { ChallengeTestOutcome.Collapsed, ChallengeTestOutcome.Fell,
            ChallengeTestOutcome.Stalled, ChallengeTestOutcome.TimeLimit })
        {
            var failed = ChallengeCompetitionScoring.Grade(failure, 0, 200000, 0);
            Check(!failed.Qualified && failed.Hundredths == 0 &&
                ChallengeCompetitionScoring.Compare(failed, low) == ChallengeWinner.Guest &&
                ChallengeCompetitionScoring.Compare(low, failed) == ChallengeWinner.Host &&
                ChallengeCompetitionScoring.Compare(failed, failed) == ChallengeWinner.NoWinner,
                "A cheap failed bridge defeated a successful bridge, or both failures awarded a winner.");
        }
        var over = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, 200001, 200000, 0);
        Check(!over.Qualified && over.Hundredths == 0 && ChallengeCompetitionScoring.Compare(over, low) == ChallengeWinner.Guest,
            "An over-budget bridge qualified for a win.");
        Check(ChallengeCompetitionScoring.Compare(host, host) == ChallengeWinner.Draw &&
            ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, 100000, 200000, 0.300001f).Hundredths == host.Hundredths,
            "Displayed equal scores used hidden precision as a tie-breaker.");
        int checks = 0;
        foreach (float budget in new[] { 1000f, 200000f })
            foreach (float costFraction in new[] { 0f, 0.25f, 0.5f, 0.7f, 0.85f, 1f })
                foreach (float stress in new[] { 0f, 0.15f, 0.3f, 0.6f, 1f, 1.5f })
                {
                    var score = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, budget * costFraction, budget, stress);
                    var dearer = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, budget * costFraction + 1f, budget, stress);
                    var weaker = ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, budget * costFraction, budget, stress + 0.01f);
                    Check(score.Qualified && score.Hundredths >= 0 && score.Hundredths <= 10000 &&
                        dearer.Hundredths <= score.Hundredths && weaker.Hundredths <= score.Hundredths,
                        "Cost/strength scoring was out of bounds or rewarded increased cost/stress.");
                    checks++;
                }
        var state = new MultiplayerChallengeState { Revision = 10, Phase = MultiplayerChallengePhase.Testing, TestIndex = 2,
            HostSubmitted = true, GuestSubmitted = true, ChallengeBudget = 200000,
            HostSubmittedCost = 100000, GuestSubmittedCost = 140000,
            HostTestOutcome = ChallengeTestOutcome.Crossed, GuestTestOutcome = ChallengeTestOutcome.Crossed,
            HostPeakStress = 0.30f, GuestPeakStress = 0.15f };
        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var invalid = state; invalid.ChallengeBudget = bad;
            Check(!MultiplayerChallengeRules.TryFinishTests(ref invalid, 10), "Invalid budget published results.");
            invalid = state; invalid.HostSubmittedCost = bad;
            Check(!MultiplayerChallengeRules.TryFinishTests(ref invalid, 10), "Invalid cost published results.");
            invalid = state; invalid.GuestPeakStress = bad;
            Check(!MultiplayerChallengeRules.TryFinishTests(ref invalid, 10), "Invalid stress published results.");
        }
        var zeroBudget = state; zeroBudget.ChallengeBudget = 0;
        Check(!MultiplayerChallengeRules.TryFinishTests(ref zeroBudget, 10), "Zero budget produced a free score.");
        var stale = state;
        Check(!MultiplayerChallengeRules.TryFinishTests(ref stale, 9) && stale.Winner == ChallengeWinner.Pending,
            "A stale finish assigned a winner.");
        Check(MultiplayerChallengeRules.TryFinishTests(ref state, 10) && state.Winner == ChallengeWinner.Guest &&
            state.HostScoreHundredths == 6200 && state.GuestScoreHundredths == 6300,
            "Host finish did not publish the frozen-budget winner and scores together.");
        var formatter = typeof(MultiplayerChallengeLobbyUI).GetMethod("CompetitionResultsMessage", BindingFlags.Static | BindingFlags.NonPublic);
        string message = (string)formatter.Invoke(null, new object[] { state, "Host IGN", "Guest IGN" });
        Check(message.Contains("WINNER: Guest IGN") && message.Contains("Host IGN") && message.Contains("40% COST / 60% STRENGTH") &&
            message.Contains("SCORE 62") && message.Contains("SCORE 63"), "Authored results omit winner, IGN, weights or published scores.");
        state.Winner = ChallengeWinner.Draw;
        Check(((string)formatter.Invoke(null, new object[] { state, "Host IGN", "Guest IGN" })).StartsWith("DRAW"), "Draw label is missing.");
        state.Winner = ChallengeWinner.NoWinner; state.HostTestOutcome = state.GuestTestOutcome = ChallengeTestOutcome.Fell;
        Check(((string)formatter.Invoke(null, new object[] { state, "Host IGN", "Guest IGN" })).Contains("NO WINNER"), "No-winner label is missing.");
        report.AppendLine($"PASS: 40% cost / 60% strength scores 62 vs 63 from the agreed example; {checks} bounded/monotonic score cases pass. Successful crossings always beat failures; over-budget designs cannot win; both failures give no winner; visible-score ties draw.");
        report.AppendLine("PASS: Host publishes the frozen budget, winner and two scores in the final state; invalid/stale/duplicate results are rejected. Authored popup formatter includes both IGNs, outcome, prices, stress, weight breakdown and winner/draw/no-winner labels.");
    }

    private static void ValidateResultsTextFit(Transform fixture, StringBuilder report)
    {
        var obj = new GameObject("Competition Results Typography (Temporary)", typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(fixture, false);
        try
        {
            var text = obj.GetComponent<TextMeshProUGUI>();
            // Match the separate authored result-card details font/minimum auto-size.
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("aaadb7eb00eeee74799e9edce7312ddb"));
            Check(text.font != null, "The authored challenge popup font is missing.");
            text.fontSize = 18; text.enableAutoSizing = false; text.fontStyle = FontStyles.Normal;
            var state = new MultiplayerChallengeState { Winner = ChallengeWinner.Guest, ChallengeBudget = 200000,
                HostTestOutcome = ChallengeTestOutcome.Crossed, GuestTestOutcome = ChallengeTestOutcome.Crossed,
                HostSubmittedCost = 100000, GuestSubmittedCost = 140000, HostPeakStress = 0.3f, GuestPeakStress = 0.15f,
                HostScoreHundredths = 6200, GuestScoreHundredths = 6300 };
            var formatter = typeof(MultiplayerChallengeLobbyUI).GetMethod("ResultsDetails", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (float canvasScale in new[] { 1f, 1280f / 1920f })
                foreach (bool failed in new[] { false, true })
                {
                    state.Winner = failed ? ChallengeWinner.NoWinner : ChallengeWinner.Guest;
                    state.HostTestOutcome = state.GuestTestOutcome = failed ? ChallengeTestOutcome.TimeLimit : ChallengeTestOutcome.Crossed;
                    string message = (string)formatter.Invoke(null, new object[] { state.HostTestOutcome, state.HostSubmittedCost,
                        state.ChallengeBudget, state.HostPeakStress });
                    // Canvas reference is 1920x1080; scale preserves the authored card hierarchy.
                    Vector2 measured = text.GetPreferredValues(message, (1920 * 0.84f - 20) * 0.45f * 0.89f, float.PositiveInfinity) * canvasScale;
                    float available = (1080 * 0.85f - 20) * 0.435f * 0.535f * canvasScale;
                    Check(measured.y <= available, $"Result metrics overflow at scale {canvasScale}: {measured.y:0.0}px > {available:0.0}px.");
                }
            report.AppendLine("PASS: Successful and failed result metrics fit the new scene-authored comparison cards at 1920x1080 and 1280x720 using their actual font/minimum auto-size; scores and names have separate authored fields.");
        }
        finally { Object.DestroyImmediate(obj); }
    }

    private static void ValidateIsolatedRunAndView(Transform fixture, StringBuilder report)
    {
        var definition = ScriptableObject.CreateInstance<ContractSO>();
        definition.contractID = "VALIDATION_TEST_ONLY"; definition.budget = 1000; definition.liveLoadWeight = 50;
        var material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        material.name = "Isolated Test Road"; material.costPerMeter = 10; material.maxLength = 6; material.isRoad = true;
        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube); segment.transform.SetParent(fixture, false); material.segmentPrefab = segment;
        BuildLocation site = Child("Sequential Test Fixture Site", fixture).AddComponent<BuildLocation>();
        site.activeContract = definition;
        Point start = Child("Sequential Test Start", fixture).AddComponent<Point>();
        Point end = Child("Sequential Test End", fixture).AddComponent<Point>();
        start.transform.position = new Vector3(40, 3, -8); end.transform.position = new Vector3(43, 3, -8);
        site.startingAnchors.Add(start); site.endingAnchors.Add(end);
        BarCreator creator = Child("Sequential Test Creator", fixture).AddComponent<BarCreator>();
        creator.pointToInstantiate = Child("Sequential Test Point Prefab", fixture); creator.pointToInstantiate.AddComponent<Point>();
        creator.barToInstantiate = Child("Sequential Test Bar Prefab", fixture); creator.barToInstantiate.AddComponent<Bar>();
        creator.pointToInstantiate.AddComponent<Rigidbody>().isKinematic = false;
        creator.barToInstantiate.AddComponent<Rigidbody>().isKinematic = false;
        BridgePhysicsManager settings = Child("Sequential Test Original Physics", fixture).AddComponent<BridgePhysicsManager>();
        settings.physicsSolverIterations = 52; settings.physicsSolverVelocityIterations = 24; settings.settleFramesAmount = 71;
        LiveLoadVehicle vehicle = Child("Sequential Test Original Vehicle", fixture).AddComponent<LiveLoadVehicle>();
        vehicle.assignedContract = definition; vehicle.startPoint = start.transform; vehicle.endPoint = end.transform;
        vehicle.transform.position = start.transform.position;
        vehicle.physicsManager = settings; vehicle.maxSpeed = 3.5f; vehicle.engineTorque = 987;
        GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cube); wheel.transform.SetParent(vehicle.transform, false);
        vehicle.wheelObjects = new[] { wheel };
        var graph = new ChallengeBridgeSubmission();
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = start.transform.position, Anchor = 0 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = end.transform.position, Anchor = 1 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = new Vector3(41, 5, -8), Anchor = -1 });
        graph.Bars.Add(new ChallengeSubmittedBar { Start = 0, End = 1, Material = material.Id });
        var catalog = new Dictionary<string, BridgeMaterialSO> { { material.Id, material } };
        ChallengeBridgeTestRun run = null; ChallengeBridgeTestView view = null;
        bool autoSync = UnityEngine.Physics.autoSyncTransforms; int iterations = UnityEngine.Physics.defaultSolverIterations;
        float maximumDelta = Time.maximumDeltaTime;
        try
        {
            definition.budget = 1f; // A poor/over-budget draft still reaches isolated physics.
            Check(ChallengeTestDescriptor.SelectVehicle(site, definition) == vehicle &&
                ChallengeTestDescriptor.ResolveVehicle(ChallengeTestDescriptor.VehiclePath(vehicle), site.gameObject.scene) == vehicle,
                "Build preview and physics disagreed on the authored contract vehicle.");
            bool originalVehicleActive = vehicle.gameObject.activeSelf;
            // FindObjectsOfType deliberately excludes Editor preview scenes.
            // Match the visibility scan's scope without moving this fixture into
            // the user's loaded world or enabling its gameplay components.
            bool originalVehicleDiscoverable = Array.IndexOf(Object.FindObjectsOfType<LiveLoadVehicle>(true), vehicle) >= 0;
            Vector3 originalVehiclePosition = vehicle.transform.position;
            var previewPose = new object[] { Vector3.zero, Quaternion.identity };
            typeof(LiveLoadVehicle).GetMethod("GetSessionTestStartPose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vehicle, previewPose);
            Vector3 previewStartPosition = (Vector3)previewPose[0];
            using (var previewWorkspace = ChallengeBuildWorkspace.Create(site, definition, creator, 12, 1))
            {
                previewWorkspace.ShowLoadPreview(vehicle);
                GameObject preview = previewWorkspace.LoadPreview;
                Check(preview != null && preview.activeInHierarchy && preview.GetComponentsInChildren<MeshRenderer>(true).Length > 0 &&
                    preview.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && preview.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                    preview.GetComponentsInChildren<Collider>(true).Length == 0, "Live load preview is invisible or contains gameplay/physics.");
                Check(preview.transform.position == previewStartPosition && vehicle.transform.position == originalVehiclePosition &&
                    vehicle.gameObject.activeSelf == (originalVehicleActive && !originalVehicleDiscoverable) &&
                    !vehicle.IsDriving && !settings.IsSimulationActive,
                    $"Preview changed the world: preview={preview.transform.position}, expectedStart={previewStartPosition}, " +
                    $"original={vehicle.transform.position}, expectedOriginal={originalVehiclePosition}, " +
                    $"originalActive={vehicle.gameObject.activeSelf}, driving={vehicle.IsDriving}, physics={settings.IsSimulationActive}.");
                previewWorkspace.Root.SetActive(false);
                Check(!preview.activeInHierarchy, "Build load preview remained visible alongside the test vehicle.");
            }
            Check(vehicle.gameObject.activeSelf == originalVehicleActive && vehicle.transform.position == originalVehiclePosition,
                "Disposing the build preview failed to restore original vehicle visibility/pose.");
            report.AppendLine("PASS: Contract truck resolves identically for building/testing; disposable visible mesh preview has no scripts, Rigidbody or colliders, hides for testing and restores original visibility without moving/driving the truck. Visibility scan respects Unity's exclusion of isolated Editor preview-scene objects.");
            Check(ChallengeBridgeSubmissionRules.Validate(graph, site, definition, catalog, -10, out float submittedCost) == ChallengeSubmissionError.None &&
                submittedCost == 30f, "An over-budget non-empty draft with a loose node was rejected before reconstruction.");
            run = ChallengeBridgeTestRun.Create(graph, site, definition, creator, settings, vehicle, 77, 12, 1);
            foreach (Bar preparedBar in run.Bars)
                Check(preparedBar.GetComponent<Rigidbody>().isKinematic, "Copied dynamic bars can fall during the name introduction.");
            foreach (Point preparedPoint in run.Root.GetComponentsInChildren<Point>(true))
            {
                Rigidbody body = preparedPoint.GetComponent<Rigidbody>();
                Check(body == null || body.isKinematic, "Copied dynamic points can fall during the name introduction.");
            }
            Check(!run.Physics.IsSimulationActive, "Preparing a view started its physics before both peers were ready.");
            report.AppendLine("PASS: Dynamic point/bar prefab bodies are held kinematic before the introduction; no simulation starts during view preparation.");
            Check(run.Physics != settings && run.Vehicle != vehicle && run.Location != site && run.Physics.IsSessionChallengeTest &&
                run.Physics.SessionTestVehicle == run.Vehicle && run.Vehicle.physicsManager == run.Physics &&
                run.Physics.physicsSolverIterations == 52 && run.Physics.physicsSolverVelocityIterations == 24 && run.Physics.settleFramesAmount == 71 &&
                run.Vehicle.maxSpeed == vehicle.maxSpeed && run.Vehicle.engineTorque == vehicle.engineTorque && run.Vehicle.TotalTestWeight == 77 &&
                !run.Physics.BakeBridge(definition), "Test reused world objects, lost settings/load or allowed baking.");
            var descriptor = new ChallengeTestDescriptor { Bridge = run.CapturePresentation("Validation/Bridge"),
                VehicleKey = ChallengeTestDescriptor.VehiclePath(vehicle), Weight = 77,
                VehiclePoses = new[] { HostWorldBridgePose.World(run.Vehicle.transform), HostWorldBridgePose.World(run.Vehicle.wheelObjects[0].transform) } };
            var decoded = ChallengeTestDescriptor.Decode(descriptor.Encode());
            Check(decoded.Weight == 77 && decoded.Bridge.Bars.Count == 1 && decoded.VehicleKey == descriptor.VehicleKey,
                "Test baseline did not round trip.");
            view = new ChallengeBridgeTestView(decoded, site, creator, catalog, settings);
            Check(view.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                view.Root.GetComponentsInChildren<Rigidbody>(true).Length == 0 && view.Root.GetComponentsInChildren<Collider>(true).Length == 0,
                "Guest simulation view contains gameplay scripts, physics or collision.");
            byte[] motion = ChallengeTestMotionCodec.Encode(1, 0, 1f, run.MotionTargets, run.Bars);
            Check(motion.Length <= RpcAttribute.MaxPayloadSize - 64 && motion.Length == 16 + run.MotionTargets.Count * 30,
                "Motion exceeds Fusion RPC payload or unexpected bandwidth.");
            var chunk = ChallengeTestMotionCodec.Decode(motion); view.Receive(chunk); view.Render(0f);
            material.isRope = true;
            var ropeChunk = ChallengeTestMotionCodec.Decode(ChallengeTestMotionCodec.Encode(2, 0, 2, run.MotionTargets, run.Bars));
            Check((ropeChunk.Stress[0] & 0x4000) != 0 && ropeChunk.Positions[0] == run.Bars[0].StartPosition &&
                ropeChunk.RopeEnds[0] == run.Bars[0].EndPosition && ropeChunk.RopeLengths[0] == run.Bars[0].currentLength,
                "Deforming rope endpoints/rest length were lost or required extra bandwidth.");
            using (var ropeView = new ChallengeBridgeTestView(decoded, site, creator, catalog, settings))
            {
                ropeView.Receive(ropeChunk); ropeView.Render(0f);
                var line = ropeView.Root.GetComponentInChildren<LineRenderer>();
                Check(line != null && line.enabled && line.positionCount == 17 &&
                    line.GetPosition(0) == ropeChunk.Positions[0] && line.GetPosition(16) == ropeChunk.RopeEnds[0],
                    "Guest rope did not render the authoritative deforming endpoints.");
            }
            material.isRope = false;
            var newer = ChallengeTestMotionCodec.Decode(motion); newer.Sequence = 2; newer.Time = 2; newer.Positions[0] += Vector3.right;
            view.Receive(newer); view.Render(0f);
            Vector3 newestPose = view.Root.transform.GetChild(0).position;
            var third = ChallengeTestMotionCodec.Decode(ChallengeTestMotionCodec.Encode(3, 0, 2.05f, run.MotionTargets, run.Bars));
            third.Positions[0] += Vector3.right * 1.05f;
            view.Receive(third); view.Render(0.1f);
            float interpolatedX = view.Root.transform.GetChild(0).position.x;
            Check(interpolatedX < newestPose.x && interpolatedX > newestPose.x - 0.15f,
                "A multi-snapshot interpolation delay clamped to the previous pose instead of using history.");
            view.Render(0f); newestPose = view.Root.transform.GetChild(0).position;
            view.Receive(chunk); view.Render(0f);
            Check(view.Root.transform.GetChild(0).position == newestPose, "Late motion rewound the guest's bridge.");
            view.CompleteFinalFrame(); view.Render(0.25f);
            Check(view.Root.transform.GetChild(0).position == newestPose, "Interpolation rewound an acknowledged final frame.");
            Array.Resize(ref motion, motion.Length - 1); RejectMotion(motion);
            byte[] invalid = ChallengeTestMotionCodec.Encode(3, 0, 3, run.MotionTargets, run.Bars); invalid[7] = 9; RejectMotion(invalid);
            Check(!settings.IsSimulationActive && !vehicle.IsDriving && site.bakedBars.Count == 0 &&
                start.transform.position == new Vector3(40,3,-8) && end.transform.position == new Vector3(43,3,-8), "A test mutated the original world.");
            run.Physics.ActivatePhysics();
            Check(run.Physics.IsSimulationActive && !settings.IsSimulationActive && !vehicle.IsDriving,
                "Physics startup reached the story manager or world vehicle.");
            Check(run.Physics.TryGetConstructionReceipt(run.Location, out var receipt) && receipt.TotalCost == 30 &&
                !run.Physics.TryGetConstructionReceipt(site, out _),
                "Physics startup did not freeze the scoped construction cost before simulation.");
            run.Bars[0].currentLength = 1000;
            Check(receipt.TotalCost == 30, "Simulation deformation changed the frozen construction receipt.");
            Check(!ChallengeCompetitionScoring.Grade(ChallengeTestOutcome.Crossed, submittedCost, definition.budget, 0).Qualified,
                "Accepting a draft for simulation also allowed an over-budget win.");
            report.AppendLine("PASS: A non-empty over-budget draft with a loose node reaches actual isolated physics startup with its full host-computed cost; accepting simulation does not qualify it for a win or modify story progress.");
            report.AppendLine("PASS: Physics startup freezes the 3m/30-cost construction receipt for its own site, rejects receipts for other sites, and deformation cannot change that cost.");
            report.AppendLine("PASS: Disposable host graph copies original solver/settling/vehicle settings and frozen load; original physics, vehicle, anchors and baked lists remain unchanged; bake is blocked.");
            report.AppendLine("PASS: Guest baseline is visual only, with no gameplay scripts, Rigidbody or collider; motion and deforming-rope endpoints round-trip within Fusion's RPC limit, reject malformed chunks and ignore late snapshots.");
        }
        finally
        {
            view?.Dispose(); run?.Dispose(); Object.DestroyImmediate(definition); Object.DestroyImmediate(material);
        }
        Check(UnityEngine.Physics.autoSyncTransforms == autoSync && UnityEngine.Physics.defaultSolverIterations == iterations && Time.maximumDeltaTime == maximumDelta,
            "Disposing a test failed to restore global physics settings.");
        report.AppendLine("PASS: Cancelling/disposing an isolated test restores global physics settings and destroys transient geometry; no PlayerDataManager save/reward route is used.");
    }
    private static void RejectMotion(byte[] packet)
    {
        try { ChallengeTestMotionCodec.Decode(packet); }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException) { return; }
        throw new InvalidOperationException("Invalid motion was accepted.");
    }
    private static GameObject Child(string name, Transform parent) { var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj; }
    private static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
