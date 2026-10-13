internal static partial class Program
{
    private static void TestJsonSyntax()
    {
        foreach (string valid in new[] { "{}", " { \"x\": [true,false,null,{},[],1,-1,0,-0,1.0,1e3,1E+2,-2e-3] } \r\n",
            "{\"quotes\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0041\"}", FixtureJson(),
            string.Concat(Enumerable.Repeat("{\"x\":", 64)) + "0" + new string('}', 64) })
            Check(GameReleasePolicy.IsJsonObjectDocument(valid), "Strict JSON guard accepts a complete valid object document.");
        foreach (string invalid in new[] {
            null, "", " ", "null", "[]", "1", "{} trailing", "{} {}", "{}[]", "<html></html>",
            "{", "{\"a\":}", "{\"a\":true,}", "{,\"a\":true}", "{\"a\" true}", "{\"a\":true \"b\":false}",
            "{\"a\":true,\"a\":false}", "{\"a\":true,\"\\u0061\":false}",
            "{\"x\":01}", "{\"x\":+1}", "{\"x\":.5}", "{\"x\":1.}", "{\"x\":1e}", "{\"x\":1e+}", "{\"x\":NaN}", "{\"x\":Infinity}",
            "{\"x\":\"\\x01\"}", "{\"x\":\"\\u000\"}", "{\"x\":\"\\uZZZZ\"}", "{\"x\":\"literal\nline\"}", "{\"x\":[1,]}", "{\"x\":[,1]}",
            string.Concat(Enumerable.Repeat("{\"x\":", 65)) + "0" + new string('}', 65),
            "{\"x\":\"" + new string('a', GameReleasePolicy.MaxResponseBytes * 2) + "\"}" })
            Check(!GameReleasePolicy.IsJsonObjectDocument(invalid), "Malformed/trailing/duplicate/excess-depth JSON is rejected before permissive SDK decoding.");
    }

    private static void TestResponseParser()
    {
        GameReleaseSnapshot snapshot = ParseValid(FixtureJson());
        Check(snapshot.Initialized && snapshot.Current.AndroidVersionCode == 11, "Numeric Android code survives actual SimpleJson parsing.");
        Check(snapshot.Latest.Id == "announcement-b" && snapshot.Current.Id == "build-a", "Latest announcement remains independent from downloadable current build.");
        Check(snapshot.Latest.Notes == "Joystick improvements\nBug fixes", "Multiline notes stay plain text.");

        foreach (string code in new[] { "null", "<missing>" })
        {
            GameReleaseSnapshot legacy = ParseValid(FixtureJson(code), "Legacy/missing code preserves release data.");
            Check(legacy.Current.AndroidVersionCode == null, "Missing code stays unknown, not zero.");
            Check(!GameReleasePolicy.IsVerifiedUpdate(legacy, 10, true), "Unknown available code cannot create a verified update badge.");
        }
        foreach (string code in new[] { "1", "2100000000" })
            Check(ParseValid(FixtureJson(code)).Current.AndroidVersionCode == long.Parse(code), "Both supported numeric code limits parse.");
        foreach (string code in new[] { "0", "-1", "2100000001", "2147483648", "9223372036854775807", "18446744073709551615", "11.5", "\"11\"", "true", "false", "{}", "[]" })
            Reject(FixtureJson(code), "Invalid Android code is rejected: " + code);
        foreach (object invalid in new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 11.5m })
        {
            var body = ParseMap(FixtureJson());
            ((IDictionary<string, object>)body["current"])["androidVersionCode"] = invalid;
            Check(!GameReleasePolicy.TryParse(body, out _), "Nonfinite/fractional numeric parser input remains rejected.");
        }

        GameReleaseSnapshot noPublishedNotes = ParseValid(FixtureJson(latest: false));
        Check(noPublishedNotes.Latest == null && GameReleasePolicy.IsVerifiedUpdate(noPublishedNotes, 10, true), "A current downloadable build need not have published notes; draft latest remains absent.");
        GameReleaseSnapshot announcementOnly = ParseValid("{\"initialized\":true,\"current\":null,\"latest\":" +
            PlayFab.Json.PlayFabSimpleJson.SerializeObject(ParseMap(FixtureJson())["latest"]) + "}");
        Check(announcementOnly.Current == null && announcementOnly.Latest != null, "Announcement-only feed preserves published text.");
        Check(!GameReleasePolicy.IsVerifiedUpdate(announcementOnly, 10, true) && !GameReleasePolicy.HasDownload(announcementOnly), "A newer announcement without a current build cannot announce an installable update.");
        GameReleaseSnapshot empty = ParseValid("{\"initialized\":true,\"current\":null,\"latest\":null}");
        Check(!GameReleasePolicy.HasDownload(empty), "Configured empty release list has no download.");
        GameReleaseSnapshot unconfigured = ParseValid("{\"initialized\":false,\"current\":null,\"latest\":null}");
        Check(!unconfigured.Initialized && !GameReleasePolicy.IsVerifiedUpdate(unconfigured, 10, true), "Unconfigured website does not fabricate an update.");
        Reject(FixtureJson().Replace("\"initialized\":true", "\"initialized\":false"), "Unconfigured feed cannot smuggle a current/published build.");

        foreach (string malformed in new[] {
            "{}", "{\"initialized\":true}", "{\"initialized\":null,\"current\":null,\"latest\":null}",
            "{\"initialized\":\"true\",\"current\":null,\"latest\":null}",
            "{\"initialized\":true,\"current\":\"build\",\"latest\":null}",
            "{\"initialized\":true,\"current\":null,\"latest\":[]}" })
            Reject(malformed, "Malformed public feed shape is rejected: " + malformed);
        Check(!GameReleasePolicy.TryParse(null, out _), "Null parser input is rejected.");

        foreach (var field in new[] { ("current.id", 64), ("current.version", 40), ("current.build", 40),
            ("latest.id", 64), ("latest.title", 160), ("latest.version", 40), ("latest.build", 40), ("latest.notes", 5000) })
        {
            ParseValid(ReplaceField(FixtureJson(), field.Item1, new string('x', field.Item2)), "Maximum accepted public text length: " + field.Item1);
            Reject(ReplaceField(FixtureJson(), field.Item1, new string('x', field.Item2 + 1)), "Oversized public text rejected: " + field.Item1);
            Reject(ReplaceField(FixtureJson(), field.Item1, 42L), "Non-string public text rejected: " + field.Item1);
        }
        foreach (string date in new[] { "2026-02-29", "2026-13-01", "2026-10-14T10:00:00Z", "14/10/2026", "not a date", "" })
            Reject(ReplaceField(FixtureJson(), "latest.releaseDate", date), "Malformed/non-calendar release date rejected: " + date);
        ParseValid(ReplaceField(FixtureJson(), "latest.releaseDate", "2024-02-29"), "Valid leap date is accepted.");
        foreach (string file in new[] { "http://civil-craft.vercel.app/download", "javascript:alert(1)", "//bad.invalid/a.apk", "/api/releases/wrong-id/download", "../download" })
            Reject(FixtureJson(fileUrl: file), "Unsupported/unsafe download metadata path rejected: " + file);
        foreach (string field in new[] { "latest.notes", "latest.title", "latest.version", "current.version" })
            Reject(ReplaceField(FixtureJson(), field, "Hidden\0Text"), "Embedded NUL cannot evade text validation: " + field);

        string malicious = "<size=200%><link=\"https://bad.invalid\">Injected</link></size> & <sprite=0>\n<color=red>Notes</color>";
        Check(ParseValid(ReplaceField(FixtureJson(), "latest.notes", malicious)).Latest.Notes == malicious,
            "Parser preserves remote notes as literal text instead of converting markup into UI behavior.");
    }

    private static void TestVersionEligibility()
    {
        foreach (long installed in new[] { 1L, 10L, 100L, GameReleasePolicy.MaxAndroidVersionCode })
        foreach (long available in new[] { 1L, 10L, 11L, 100L, GameReleasePolicy.MaxAndroidVersionCode })
        {
            GameReleaseSnapshot feed = ParseValid(FixtureJson(available.ToString()));
            Check(GameReleasePolicy.IsVerifiedUpdate(feed, installed, true) == (available > installed),
                "Only strictly greater downloadable numeric code is an update.");
            Check(!GameReleasePolicy.IsVerifiedUpdate(feed, installed, false), "Cached/offline/failed evidence cannot show verified update badge.");
        }
        GameReleaseSnapshot availableFeed = ParseValid(FixtureJson());
        Check(!GameReleasePolicy.IsVerifiedUpdate(availableFeed, null, true), "Unknown installed code is not treated as older than every build.");
        foreach (string file in new[] { null, "", "   " })
        {
            GameReleaseSnapshot noInstaller = ParseValid(FixtureJson(fileUrl: file));
            Check(!GameReleasePolicy.HasDownload(noInstaller) && !GameReleasePolicy.IsVerifiedUpdate(noInstaller, 10, true), "Build without actual download cannot trigger badge.");
        }
        GameReleaseSnapshot legacyLink = ParseValid(FixtureJson(fileUrl: "https://drive.google.com/file/d/legacy"));
        Check(GameReleasePolicy.HasDownload(legacyLink) && GameReleasePolicy.IsVerifiedUpdate(legacyLink, 10, true), "Legacy downloadable links remain eligible without apkHosted field.");
        Check(!GameReleasePolicy.IsVerifiedUpdate(null, 10, true) && !GameReleasePolicy.HasDownload(null), "No feed never fabricates current/download state.");
    }

    private static void TestRefreshPolicy()
    {
        DateTime time = new(2026, 10, 14, 4, 0, 0, DateTimeKind.Utc);
        Check(GameReleasePolicy.FetchTimeoutSeconds == 10, "Request timeout remains ten seconds.");
        Check(GameReleasePolicy.RefreshIntervalSeconds == 300, "Panel-open stale interval remains five minutes.");
        Check(GameReleasePolicy.MaxAndroidVersionCode == 2100000000, "Android code upper limit matches server and Play compatibility requirement.");
        Check(GameReleasePolicy.WebsiteOrigin == "https://civil-craft.vercel.app" && GameReleasePolicy.FeedPath == "/api/releases", "Only Jemma's main website provides public updates.");
        Check(GameReleasePolicy.DownloadUrl == "https://civil-craft.vercel.app/download", "Download action uses fixed public landing page.");
        Check(GameReleasePolicy.NeedsRefresh(null, time, false), "First check is eligible.");
        Check(!GameReleasePolicy.NeedsRefresh(time, time, false), "Repeated immediate panel opens do not refetch.");
        Check(!GameReleasePolicy.NeedsRefresh(time, time.AddSeconds(299), false), "Checks younger than five minutes are reused.");
        Check(GameReleasePolicy.NeedsRefresh(time, time.AddSeconds(300), false), "Exactly five minutes becomes stale.");
        Check(GameReleasePolicy.NeedsRefresh(time, time.AddDays(2), false), "Old successful/failed attempts become retryable.");
        Check(GameReleasePolicy.NeedsRefresh(time, time, true), "Explicit Retry bypasses the freshness interval.");
    }
}
