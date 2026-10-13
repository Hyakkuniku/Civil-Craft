using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using PlayFab.Json;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;

internal static partial class Program
{
    private static readonly string TestCacheRoot = Path.Combine(Root, "Temp", "MainMenuUpdatesChecks", Guid.NewGuid().ToString("N"));
    private static int fixtureNumber;

    private static GameReleaseService Fixture(string cacheDirectory = null)
    {
        UnityWebRequest.Reset();
        Application.OpenedUrls.Clear();
        Keyboard.current = null;
        UIPanelCoordinator.Instance = null;
        UnityEditor.PlayerSettings.Android.bundleVersionCode = 10;
        Application.persistentDataPath = cacheDirectory ?? Path.Combine(TestCacheRoot, (++fixtureNumber).ToString(CultureInfo.InvariantCulture));
        Application.version = "1.2.3";
        var gameObject = new GameObject();
        return gameObject.AddComponent<GameReleaseService>();
    }
    private static void Reply(long status = 200, string body = null, UnityWebRequest.Result outcome = UnityWebRequest.Result.Success) =>
        UnityWebRequest.Script.Enqueue(new(status, body ?? FixtureJson(), outcome));
    private static void DrainService(GameReleaseService service)
    {
        foreach (IEnumerator coroutine in service.StartedCoroutines.ToArray()) Drain(coroutine);
        service.StartedCoroutines.Clear();
    }
    private static void SetLastAttempt(GameReleaseService service, DateTime stamp) =>
        typeof(GameReleaseService).GetField("lastAttemptUtc", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, stamp);
    private static string CachePath() => Path.Combine(Application.persistentDataPath, GameReleaseService.CacheFileName);
    private static void WriteFixtureCache(string text)
    {
        Directory.CreateDirectory(Application.persistentDataPath);
        File.WriteAllText(CachePath(), text, Encoding.UTF8);
    }
    private static string CacheJson(string feed = null, string timestamp = null, object schema = null) => PlayFabSimpleJson.SerializeObject(new Dictionary<string, object>
    {
        ["schema"] = schema ?? 1L,
        ["fetchedAtUtc"] = timestamp ?? DateTime.UtcNow.AddDays(-1).ToString("O", CultureInfo.InvariantCulture),
        ["feed"] = ParseMap(feed ?? FixtureJson())
    });

    private static void TestService()
    {
        TestPublicFetchAndDeduplication();
        TestCachedOfflineAndMalformedReplies();
        TestCacheValidationAndWriteFailure();
        TestBoundedResponseAndCancellation();
        TestActualController();
    }

    private static void TestPublicFetchAndDeduplication()
    {
        GameReleaseService service = Fixture();
        int notifications = 0;
        service.Changed += () => throw new InvalidOperationException("Scripted UI subscriber failure");
        service.Changed += () => notifications++;
        Reply();
        service.BeginMenuSession();
        Check(service.State == GameReleaseFetchState.Checking && service.IsRequestInFlight, "Menu entry starts one non-blocking update check.");
        Check(!service.HasVerifiedUpdate, "No update badge before response proof.");
        Check(service.InstalledVersionCode == 10, "Actual production Editor version reader gets Bundle Version Code.");
        service.BeginMenuSession(); service.RefreshIfStale(); service.Retry();
        Check(service.StartedCoroutines.Count == 1, "Entry/open/retry during an in-flight check share one request.");
        DrainService(service);
        Check(service.State == GameReleaseFetchState.Ready && !service.IsRequestInFlight && service.HasVerifiedUpdate, "Verified newer downloadable build enables badge after fetch.");
        Check(service.LastVerifiedUtc.HasValue && service.CacheFetchedAtUtc.HasValue, "Successful proof timestamps are recorded.");
        Check(notifications >= 2, "A bad UI subscriber cannot suppress other listeners or block the update response.");
        Check(UnityWebRequest.Requests.Count == 1, "One and only one public fetch ran.");
        var request = UnityWebRequest.Requests.Single();
        Check(request.Method == "GET" && request.Url == "https://civil-craft.vercel.app/api/releases", "Release lookup uses only unauthenticated public GET.");
        Check(request.Timeout == 10 && request.RedirectLimit == 0, "Fetch has ten-second timeout and cannot follow unexpected redirects.");
        Check(request.Headers.Count == 1 && request.Headers.TryGetValue("Accept", out string accept) && accept == "application/json", "No session ticket, cookie, player ID or secret reaches release fetch.");
        Check(Application.OpenedUrls.Count == 0, "Menu entry and update discovery never open browser.");
        Check(File.Exists(CachePath()), "Successful public response is cached independently for offline reading.");
        string cache = File.ReadAllText(CachePath());
        Check(!cache.Contains("ClientSessionTicket") && !cache.Contains("PlayFabId") && !cache.Contains("gold"), "Public-data cache contains no account or wallet state.");
        service.BeginMenuSession(); service.RefreshIfStale();
        Check(service.StartedCoroutines.Count == 0 && UnityWebRequest.Requests.Count == 1, "Opening within five minutes and repeated menu initialization do not refetch.");
        Reply();
        service.Retry();
        Check(service.State == GameReleaseFetchState.Checking && !service.HasVerifiedUpdate, "Starting explicit verification clears badge until fresh proof completes.");
        DrainService(service);
        Check(UnityWebRequest.Requests.Count == 2 && service.HasVerifiedUpdate, "Explicit Retry bypasses freshness but still uses one request.");
        SetLastAttempt(service, DateTime.UtcNow.AddMinutes(-6));
        Reply(); service.RefreshIfStale(); DrainService(service);
        Check(UnityWebRequest.Requests.Count == 3, "Opening after five-minute freshness interval refreshes.");

        foreach (int code in new[] { 0, -1 })
        {
            UnityEditor.PlayerSettings.Android.bundleVersionCode = code;
            Check(GameInstalledVersionReader.ReadAndroidCode() == null, "Invalid installed Editor code remains unknown.");
        }
        UnityEditor.PlayerSettings.Android.bundleVersionCode = 10;
    }

    private static void TestCachedOfflineAndMalformedReplies()
    {
        foreach (var failure in new[] { (0L, "", UnityWebRequest.Result.ConnectionError), (408L, "", UnityWebRequest.Result.ConnectionError), (500L, "{}", UnityWebRequest.Result.ProtocolError),
            (200L, "<html>Service unavailable</html>", UnityWebRequest.Result.Success), (200L, "{\"initialized\":true", UnityWebRequest.Result.Success),
            (200L, "[]", UnityWebRequest.Result.Success), (200L, FixtureJson("\"11\""), UnityWebRequest.Result.Success),
            (200L, FixtureJson() + " garbage", UnityWebRequest.Result.Success), (200L, FixtureJson() + " {}", UnityWebRequest.Result.Success),
            (200L, FixtureJson().Replace("\"initialized\":true", "\"initialized\":true,\"initialized\":true"), UnityWebRequest.Result.Success) })
        {
            GameReleaseService service = Fixture();
            WriteFixtureCache(CacheJson());
            Reply(failure.Item1, failure.Item2, failure.Item3);
            service.BeginMenuSession();
            Check(service.Snapshot?.Latest?.Title == "October Improvements" && service.CacheFetchedAtUtc.HasValue, "Cached public notes remain readable while reconnecting.");
            Check(!service.HasVerifiedUpdate && !service.LastVerifiedUtc.HasValue, "Cache alone never supplies verified online badge/current evidence.");
            DrainService(service);
            Check(service.State == GameReleaseFetchState.Failed && !service.HasVerifiedUpdate && !service.IsRequestInFlight, "Failure/malformed response cannot verify an update or leave stuck loading.");
            Check(service.Snapshot?.Latest?.Notes == "Joystick improvements\nBug fixes", "Connection and payload failures preserve last successfully validated notes.");
            Check(service.StatusMessage.Contains("saved", StringComparison.OrdinalIgnoreCase) && !service.StatusMessage.Contains("No newer update"), "Offline status describes cached information rather than claiming the game is current.");
        }
        GameReleaseService emptyOffline = Fixture();
        Reply(0, "", UnityWebRequest.Result.ConnectionError); emptyOffline.BeginMenuSession(); DrainService(emptyOffline);
        Check(emptyOffline.Snapshot == null && emptyOffline.State == GameReleaseFetchState.Failed && !emptyOffline.HasVerifiedUpdate, "Offline first launch has no fabricated notes/build/balance state.");
        Check(emptyOffline.StatusMessage.Contains("No saved release notes"), "Offline first launch explains no cached notes.");

        GameReleaseService unconfigured = Fixture();
        WriteFixtureCache(CacheJson());
        string originalCache = File.ReadAllText(CachePath());
        Reply(body: "{\"initialized\":false,\"current\":null,\"latest\":null}");
        unconfigured.BeginMenuSession(); DrainService(unconfigured);
        Check(unconfigured.State == GameReleaseFetchState.Unconfigured && !unconfigured.HasVerifiedUpdate, "Unconfigured website disables verified update badge.");
        Check(unconfigured.Snapshot?.Latest != null && File.ReadAllText(CachePath()) == originalCache, "Unconfigured server never overwrites good cached notes with an empty setup state.");

        GameReleaseService unknownBuild = Fixture(); Reply(body: FixtureJson("null")); unknownBuild.BeginMenuSession(); DrainService(unknownBuild);
        Check(unknownBuild.State == GameReleaseFetchState.Ready && !unknownBuild.HasVerifiedUpdate && unknownBuild.StatusMessage.Contains("unavailable"), "Healthy feed with unknown code says status unknown, not falsely current.");
    }

    private static void TestCacheValidationAndWriteFailure()
    {
        foreach (string invalid in new[] {
            "<html>Invalid</html>", "{}", CacheJson(schema: 2L), CacheJson(schema: "1"), CacheJson(timestamp: "2026-10-14"),
            CacheJson(timestamp: DateTime.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture)),
            CacheJson(feed: FixtureJson("11.1")), CacheJson(feed: "{\"initialized\":false,\"current\":null,\"latest\":null}"), CacheJson() + " {}",
            new string('x', GameReleasePolicy.MaxResponseBytes * 2 + 1) })
        {
            GameReleaseService service = Fixture(); WriteFixtureCache(invalid);
            Reply(0, "", UnityWebRequest.Result.ConnectionError); service.BeginMenuSession();
            Check(service.Snapshot == null && !service.HasVerifiedUpdate, "Malformed, wrong-schema, future-dated or oversized cache is ignored.");
            DrainService(service);
        }
        GameReleaseService writeFailure = Fixture();
        string blockedPath = Application.persistentDataPath;
        Directory.CreateDirectory(Path.GetDirectoryName(blockedPath));
        File.WriteAllText(blockedPath, "Test-owned file intentionally blocks cache directory creation.");
        Reply(); writeFailure.BeginMenuSession(); DrainService(writeFailure);
        Check(writeFailure.State == GameReleaseFetchState.Ready && writeFailure.HasVerifiedUpdate, "Cache write failure cannot invalidate a freshly verified network response.");
        Check(writeFailure.StatusMessage.Contains("offline reading"), "Cache write failure is reported without failing the menu.");

        GameReleaseSnapshot roundTrip = ParseValid(FixtureJson());
        Check(GameReleasePolicy.TryParse(GameReleasePolicy.PublicFeed(roundTrip), out GameReleaseSnapshot restored) && restored.Current.AndroidVersionCode == 11 && restored.Latest.Notes == roundTrip.Latest.Notes,
            "Actual public cache projection roundtrips through the same validated parser.");
    }

    private static void TestBoundedResponseAndCancellation()
    {
        var receiver = new BoundedReleaseDownload();
        byte[] boundary = new byte[GameReleasePolicy.MaxResponseBytes];
        Check(receiver.Deliver(boundary, boundary.Length) && !receiver.Overflowed, "Bounded receive handler accepts exactly the maximum byte limit.");
        Check(!receiver.Deliver(new byte[1], 1) && receiver.Overflowed, "One extra response byte is rejected before allocating an oversized complete body.");
        foreach (var invalid in new[] { (Data: (byte[])null, Length: 0), (Data: new byte[1], Length: -1), (Data: new byte[1], Length: 2) })
        {
            var broken = new BoundedReleaseDownload();
            Check(!broken.Deliver(invalid.Data, invalid.Length) && broken.Overflowed, "Invalid receive callback cannot write beyond buffer bounds.");
        }
        GameReleaseService oversized = Fixture(); Reply(body: FixtureJson() + new string(' ', GameReleasePolicy.MaxResponseBytes)); oversized.BeginMenuSession(); DrainService(oversized);
        Check(oversized.State == GameReleaseFetchState.Failed && !oversized.HasVerifiedUpdate, "Oversized network response is rejected without trusting partial JSON.");

        GameReleaseService disabled = Fixture(); disabled.isActiveAndEnabled = false;
        disabled.BeginMenuSession(); disabled.Retry();
        Check(disabled.StartedCoroutines.Count == 0 && !disabled.IsRequestInFlight, "Disabled service cannot dispatch network operations.");

        GameReleaseService interrupted = Fixture(); Reply(); interrupted.BeginMenuSession();
        IEnumerator waiting = interrupted.StartedCoroutines.Single();
        Check(waiting.MoveNext(), "Actual fetch coroutine reaches pending request boundary.");
        Invoke(interrupted, "OnDisable");
        Check(interrupted.State == GameReleaseFetchState.Failed && !interrupted.IsRequestInFlight && !interrupted.HasVerifiedUpdate, "Leaving menu during fetch aborts check and removes proof, not player/save state.");
        Check(interrupted.StartedCoroutines.Count == 0, "Disabled service stops its active coroutine.");
        Reply(); interrupted.Retry(); DrainService(interrupted);
        Check(interrupted.State == GameReleaseFetchState.Ready && interrupted.HasVerifiedUpdate, "A fresh request can recover after interrupted check.");
    }

    private static MainMenuUpdatesController Controller(GameReleaseService service)
    {
        var controller = new MainMenuUpdatesController
        {
            updatesButton = new Button(), closeButton = new Button(), retryButton = new Button(), downloadButton = new Button(),
            panel = new GameObject(), badge = new GameObject(), notesScroll = new ScrollRect(),
            releaseTitleText = new TMP_Text(), releaseDateText = new TMP_Text(), notesText = new TMP_Text(), installedVersionText = new TMP_Text(), availableVersionText = new TMP_Text(), statusText = new TMP_Text()
        };
        service.gameObject.Attach(controller);
        service.gameObject.Attach(new MainMenuUpdatesLayout());
        Invoke(controller, "OnEnable");
        return controller;
    }

    private static void TestActualController()
    {
        GameReleaseService service = Fixture();
        MainMenuUpdatesController controller = Controller(service);
        UIPanelCoordinator.Instance = new UIPanelCoordinator();
        Reply(); Invoke(controller, "Start"); DrainService(service);
        Check(!controller.panel.activeSelf && UIPanelCoordinator.Instance.Opens == 0, "Menu startup never opens Updates automatically.");
        Check(controller.badge.activeSelf, "Badge appears when current download is verified newer.");
        controller.updatesButton.onClick.Invoke();
        Check(controller.panel.activeSelf && UIPanelCoordinator.Instance.Opens == 1, "Explicit Updates click delegates modal opening to existing coordinator.");
        Check(controller.notesScroll.verticalNormalizedPosition == 1f, "Each newly opened notes panel starts at top.");
        Check(service.StartedCoroutines.Count == 0, "Opening fresh notes does not refetch.");
        controller.closeButton.onClick.Invoke();
        Check(!controller.panel.activeSelf && UIPanelCoordinator.Instance.Closes == 1 && controller.badge.activeSelf, "Reading/closing notes does not clear available-update badge.");
        controller.downloadButton.onClick.Invoke();
        Check(Application.OpenedUrls.Count == 1 && Application.OpenedUrls[0] == GameReleasePolicy.DownloadUrl, "User Download opens website landing page only.");
        controller.Open();
        Keyboard.current = new Keyboard(); Keyboard.current.escapeKey.wasPressedThisFrame = true;
        Invoke(controller, "Update");
        Check(!controller.panel.activeSelf && UIPanelCoordinator.Instance.Closes == 2, "Escape/Android Back closes the active Updates modal.");
        controller.Open();
        UIPanelCoordinator.Instance.CurrentPanel = new GameObject();
        Invoke(controller, "Update");
        Check(controller.panel.activeSelf && UIPanelCoordinator.Instance.Closes == 2, "Updates does not consume Back while another coordinated modal is on top.");
        UIPanelCoordinator.Instance.CurrentPanel = controller.panel; controller.Close();

        string malicious = "<size=500%><color=red><sprite=0>literal notes</color></size>";
        Reply(body: ReplaceField(FixtureJson(), "latest.notes", malicious)); service.Retry(); DrainService(service);
        Check(controller.notesText.text == malicious && !controller.notesText.richText, "Actual production Render displays malicious TMP markup as literal notes.");
        foreach (TMP_Text text in new[] { controller.releaseTitleText, controller.releaseDateText, controller.notesText, controller.installedVersionText, controller.availableVersionText, controller.statusText })
            Check(!text.richText, "Every release-controlled text field disables rich markup.");
        Check(controller.installedVersionText.text.Contains("1.2.3") && controller.availableVersionText.text.Contains("1.2.4"), "Panel shows installed label independently of current downloadable label.");
        Invoke(controller, "OnEnable");
        Check(controller.updatesButton.onClick.ListenerCount == 1 && controller.downloadButton.onClick.ListenerCount == 1, "Repeated enable does not duplicate menu/download listeners.");

        Reply(0, "", UnityWebRequest.Result.ConnectionError); service.Retry(); DrainService(service);
        Check(!controller.badge.activeSelf && !controller.downloadButton.interactable && controller.retryButton.interactable, "Offline response clears verified badge/download but keeps manual Retry.");
        Check(controller.availableVersionText.text.StartsWith("Last known:"), "Cached available label explicitly indicates last-known information.");
        int browsers = Application.OpenedUrls.Count;
        controller.OpenDownload();
        Check(Application.OpenedUrls.Count == browsers, "Offline/cached response cannot launch unverified Download action.");
        controller.Open(); Invoke(controller, "OnDisable");
        Check(!controller.panel.activeSelf && controller.updatesButton.onClick.ListenerCount == 0, "Leaving menu closes its modal and detaches UI listeners.");
    }
}
