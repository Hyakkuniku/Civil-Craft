using System.Collections;
using System.Reflection;
using PlayFab.Json;

internal static partial class Program
{
    private static int checks;
    private static readonly string Root = FindRoot();
    private static int Main(string[] args)
    {
        try
        {
            TestJsonSyntax();
            TestResponseParser();
            TestVersionEligibility();
            TestRefreshPolicy();
            TestService();
            TestPrivateMessages();
            if (!args.Contains("--runtime-only", StringComparer.Ordinal)) TestSourceShipping();
            else Console.WriteLine("Saved-scene shipping assertions skipped (--runtime-only); run the default command after Unity authoring is saved.");
            Console.WriteLine($"MainMenuUpdates: {checks} checks passed (actual production parser/policy/service/controller; engine/network/UI boundaries doubled, no Unity session, accounts or live requests).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string FindRoot()
    {
        DirectoryInfo directory = new(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Assets", "Script", "Client", "GameReleaseService.cs"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Run this harness from the Unity project root or a child directory.");
    }
    private static IDictionary<string, object> ParseMap(string json) =>
        PlayFabSimpleJson.DeserializeObject(json) as IDictionary<string, object>;
    private static string FixtureJson(string code = "11", string fileUrl = "/api/releases/build-a/download", bool latest = true)
    {
        string current = "{\"id\":\"build-a\",\"version\":\"1.2.4\",\"build\":\"Alpha 11\",\"status\":\"current\",\"platform\":\"Android\",\"fileUrl\":" +
            (fileUrl == null ? "null" : PlayFabSimpleJson.SerializeObject(fileUrl)) +
            (code == "<missing>" ? "" : ",\"androidVersionCode\":" + code) + "}";
        string notes = latest ? "{\"id\":\"announcement-b\",\"title\":\"October Improvements\",\"version\":\"1.2.5\",\"build\":\"Alpha 12\",\"releaseDate\":\"2026-10-14\",\"notes\":\"Joystick improvements\\nBug fixes\"}" : "null";
        return "{\"initialized\":true,\"current\":" + current + ",\"latest\":" + notes + "}";
    }
    private static GameReleaseSnapshot ParseValid(string json, string message = "Valid public release feed parses")
    {
        Check(GameReleasePolicy.TryParse(ParseMap(json), out GameReleaseSnapshot snapshot), message);
        return snapshot;
    }
    private static void Reject(string json, string message)
    {
        Check(!GameReleasePolicy.TryParse(ParseMap(json), out _), message);
    }
    private static string ReplaceField(string json, string path, object value)
    {
        IDictionary<string, object> root = ParseMap(json);
        string[] keys = path.Split('.');
        IDictionary<string, object> container = root;
        foreach (string key in keys.Take(keys.Length - 1)) container = (IDictionary<string, object>)container[key];
        container[keys[^1]] = value;
        if (path == "current.id" && value is string id) container["fileUrl"] = "/api/releases/" + id + "/download";
        return PlayFabSimpleJson.SerializeObject(root);
    }
    private static void Drain(IEnumerator coroutine)
    {
        int steps = 0;
        while (coroutine.MoveNext())
        {
            if (++steps > 1000) throw new InvalidOperationException("A test coroutine never completed.");
            if (coroutine.Current is IEnumerator nested) Drain(nested);
        }
    }
    private static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(info != null, "Expected actual production method exists: " + method);
        info.Invoke(target, null);
    }
}
