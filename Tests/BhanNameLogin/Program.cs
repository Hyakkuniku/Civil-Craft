using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PlayFab.ClientModels;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message)
{
    ++checks;
    if (!condition) throw new InvalidOperationException(message);
}
string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/PlayFabAuthManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run inside the Unity repository.");
string playerPath = Path.Combine(root, "Assets/Script/Player/PlayerSave/PlayerDataManager.cs");
string authPath = Path.Combine(root, "Assets/Script/PlayFabAuthManager.cs");
MethodDeclarationSyntax Method(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(path))
    .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
string load = Method(playerPath, "LoadGame").NormalizeWhitespace().ToFullString();
string finish = Method(authPath, "FinishSelectedLogin").NormalizeWhitespace().ToFullString();

// Compile the actual current method syntax against disposable boundaries;
// neither implementation is duplicated, and no real save/pref/API is accessed.
string harness = """
using System;
using System.Collections.Generic;
using UnityEngine;
using PlayFab.ClientModels;
namespace BhanNameLoginHarness {
public sealed class PlayerDataManager {
 public static PlayerDataManager Instance;
 public PlayerData CurrentData;
 public string saveFilePath = "fixture.save";
 public Dictionary<string, PlayerData> Files = new Dictionary<string, PlayerData>();
 public int SaveCount, NormalizeCount;
 public CloudSaveManager Cloud = new CloudSaveManager();
 private PlayerData TryReadSaveFile(string path) => Files.TryGetValue(path, out PlayerData data) ? data : null;
 private void NormalizeLoadedData() => NormalizeCount++;
 public void SaveGame() => SaveCount++;
 public T GetComponent<T>() where T:class => Cloud as T;
""" + load + """
}
public sealed class PlayFabAuthManager {
 private const string RememberedPlayFabIdKey = "RememberedPlayFabId";
 private const string DeviceLinkedPlayFabIdKey = "DeviceLinkedPlayFabId";
 private int authenticationGeneration = 7;
 private bool manualLoginInProgress = true;
 private bool isGuest = true;
 private string loggedInPlayerName = "WebsiteUsername";
 private bool returnToMainMenuAfterLogin = true;
 private object successColor;
 public int DisplayUpdates, AuthEvents, Invokes;
 public string Feedback;
 private void UpdatePlayerNameDisplay() => DisplayUpdates++;
 private void SetFeedbackMessage(string message, object color) => Feedback = message;
 private void CompleteMainMenuAuthenticationRequest() => AuthEvents++;
 private void Invoke(string method, float delay) => Invokes++;
 private void LoadGameScene() { }
""" + finish + """
}
}
""";
string[] framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
var references = framework.Select(path => MetadataReference.CreateFromFile(path)).ToList();
references.Add(MetadataReference.CreateFromFile(typeof(PlayerData).Assembly.Location));
var compilation = CSharpCompilation.Create("BhanNameLoginActualMethods_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(harness) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var compiled = new MemoryStream();
var emission = compilation.Emit(compiled);
if (!emission.Success) throw new InvalidOperationException(string.Join("\n", emission.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
Assembly methods = Assembly.Load(compiled.ToArray());
Type managerType = methods.GetType("BhanNameLoginHarness.PlayerDataManager", true);
Type authType = methods.GetType("BhanNameLoginHarness.PlayFabAuthManager", true);
const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
object Manager(PlayerData primary = null, PlayerData backup = null)
{
    object manager = Activator.CreateInstance(managerType);
    managerType.GetField("Instance").SetValue(null, manager);
    var files = (Dictionary<string, PlayerData>)managerType.GetField("Files").GetValue(manager);
    if (primary != null) files["fixture.save"] = primary;
    if (backup != null) files["fixture.save.bak"] = backup;
    return manager;
}
PlayerData Data(object manager) => (PlayerData)managerType.GetField("CurrentData").GetValue(manager);
int Saves(object manager) => (int)managerType.GetField("SaveCount").GetValue(manager);
void Load(object manager, bool persist) => managerType.GetMethod("LoadGame").Invoke(manager, new object[] { persist });

foreach (string greeting in new[] { "WebsiteUsername", "UPPERCASE_ACCOUNT", "ExistingAccount", "Guest", "" })
{
    PlayerPrefs.Clear(); PlayerPrefs.SetString("SavedPlayerName", greeting);
    object manager = Manager(); Load(manager, true);
    Check(Data(manager).playerName == "Guest", "New save must not inherit account greeting " + greeting);
    Check(Saves(manager) == 1 && (int)managerType.GetField("NormalizeCount").GetValue(manager) == 1,
        "Fresh load still normalizes and commits once.");
    Check(PlayerPrefs.GetString("SavedPlayerName") == greeting, "Fresh load must not rewrite greeting prefs.");
}
object uncommittedFresh = Manager(); Load(uncommittedFresh, false);
Check(Data(uncommittedFresh).playerName == "Guest" && Saves(uncommittedFresh) == 0,
    "StartFresh account LoadGame(false) remains unnamed without an extra save.");
var alreadySeeded = new PlayerData { playerName = "WebsiteUsername" };
object pollutedLegacy = Manager(alreadySeeded); Load(pollutedLegacy, false);
Check(ReferenceEquals(Data(pollutedLegacy), alreadySeeded) && Data(pollutedLegacy).playerName == "WebsiteUsername",
    "An existing account-seeded save is preserved, even without gameplay progress; no unsafe naming inference.");
PlayerData Progress(string name) => new PlayerData {
    playerName = name, hasAlmanac = true, gold = 350, exp = 420,
    completedContracts = new List<string> { "Canyon_01" },
    completedLessons = new List<string> { "Sequence_Alamnac" },
    discoveredMaterialIds = new List<string> { "Timber" },
    unlockedFeatureIds = new List<string> { "multiplayer" },
    purchasedShopItemIds = new List<string> { "HardHat" },
    npcProgressions = new List<NPCProgressionSaveData> { new NPCProgressionSaveData { progressionId = "Silas", currentPhaseIndex = 10 } }
};
foreach (string chosen in new[] { "Engineer Rem", "Mixed_Case Builder", "WebsiteUsername", "Guest" })
{
    PlayerData existing = Progress(chosen);
    object primary = Manager(existing); Load(primary, true);
    Check(ReferenceEquals(Data(primary), existing) && Data(primary).playerName == chosen,
        "Existing local/account/guest names remain intact: " + chosen);
    object backup = Manager(null, existing); Load(backup, false);
    Check(ReferenceEquals(Data(backup), existing) && Saves(backup) == 0,
        "Recovered backup keeps its name and progress without forcing persistence.");
}
var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
PlayerData roundtrip = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(Progress("Engineer Bhan Choice"), jsonOptions), jsonOptions);
Check(roundtrip.playerName == "Engineer Bhan Choice" && roundtrip.hasAlmanac && roundtrip.gold == 350 &&
    roundtrip.exp == 420 && roundtrip.completedContracts.SequenceEqual(new[] { "Canyon_01" }) &&
    roundtrip.npcProgressions[0].currentPhaseIndex == 10, "Production PlayerData preserves name and progress through account/cloud JSON.");
Check(JsonSerializer.Deserialize<PlayerData>("{}", jsonOptions).playerName == "Guest", "Empty legacy/fresh JSON defaults to Guest.");
Check(JsonSerializer.Deserialize<PlayerData>("{\"Username\":\"WebsiteUsername\",\"DisplayName\":\"WebsiteUsername\"}", jsonOptions).playerName == "Guest",
    "Website identity fields do not confirm an engineer name.");

foreach (CloudSaveManager.StartMode mode in Enum.GetValues<CloudSaveManager.StartMode>())
    foreach (bool linked in new[] { false, true })
    {
        PlayerPrefs.Clear();
        string chosen = mode == CloudSaveManager.StartMode.StartFresh ? "Guest" : "Engineer Guest Choice";
        object manager = Manager(Progress(chosen)); Load(manager, false);
        string before = JsonSerializer.Serialize(Data(manager), jsonOptions);
        object auth = Activator.CreateInstance(authType);
        authType.GetMethod("FinishSelectedLogin", fields).Invoke(auth,
            new object[] { new LoginResult { PlayFabId = "TEST_ACCOUNT" }, linked, 7, mode });
        Check(JsonSerializer.Serialize(Data(manager), jsonOptions) == before && Saves(manager) == 0,
            "Finishing " + mode + " login must not overwrite/save engineer name or progress.");
        Check(PlayerPrefs.GetString("SavedPlayerName") == "WebsiteUsername" && PlayerPrefs.GetInt("LoginChoice") == 2,
            "Account greeting/sign-in preferences stay independent of engineer name.");
        Check(PlayerPrefs.GetString("RememberedPlayFabId") == (linked ? "TEST_ACCOUNT" : ""), "Remembered-account handling unchanged.");
        Check((int)authType.GetField("DisplayUpdates").GetValue(auth) == 1 &&
            (int)authType.GetField("AuthEvents").GetValue(auth) == 1, "Main-menu account greeting/event still completes.");
    }
object staleManager = Manager(Progress("Kept Name")); Load(staleManager, false); PlayerPrefs.Clear();
object staleAuth = Activator.CreateInstance(authType);
authType.GetMethod("FinishSelectedLogin", fields).Invoke(staleAuth,
    new object[] { new LoginResult { PlayFabId = "TEST_ACCOUNT" }, true, 6, CloudSaveManager.StartMode.StartFresh });
Check(PlayerPrefs.Strings.Count == 0 && PlayerPrefs.Integers.Count == 0 && Data(staleManager).playerName == "Kept Name",
    "Stale completion cannot change preferences or engineer name.");

string Body(string path, string name) => Method(path, name).Body.ToFullString();
Check(!load.Contains("SavedPlayerName", StringComparison.Ordinal), "LoadGame must not seed character name from login prefs.");
Check(!finish.Contains("CurrentData.playerName", StringComparison.Ordinal) && !finish.Contains("SaveGame()", StringComparison.Ordinal),
    "FinishSelectedLogin must not mutate/commit gameplay identity.");
Check(Body(playerPath, "UseAccountSave").Contains("LoadGame(false)", StringComparison.Ordinal), "Account opening/import keep normal loading.");
Check(Body(playerPath, "UseGuestSave").Contains("LoadGame()", StringComparison.Ordinal), "Guest saves keep normal loading.");
Check(Body(playerPath, "TryApplyCloudData").Contains("TryRestoreDebugState", StringComparison.Ordinal) &&
    Body(playerPath, "TryRestoreDebugState").Contains("NormalizeLoadedData()", StringComparison.Ordinal), "Cloud/debug snapshots still normalize.");
Check(Body(playerPath, "DeleteSaveData").Contains("CurrentData = new PlayerData()", StringComparison.Ordinal), "Debug Clear Save keeps unnamed defaults.");
string namingPath = Path.Combine(root, "Assets/Script/Player/PlayerSave/NameRegistrationUI.cs");
Check(Body(namingPath, "ConfirmNameYes").Contains("CurrentData.playerName = pendingName", StringComparison.Ordinal),
    "Bhan's explicit confirmation remains the gameplay name setter.");
Console.WriteLine($"PASS: {checks} checks; actual LoadGame/FinishSelectedLogin bodies, production PlayerData JSON, unnamed new saves, retained named local/import/cloud saves, unchanged greeting/device preferences, stale completion and integration guards.");
Console.WriteLine("Existing saves already seeded with an account name are intentionally preserved; no name-confirmation inference or progress reset. Only production source files read; no live Unity session/prefs, accounts, API, cloud, or player saves accessed.");
