using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

int checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}

WalletReadOnlyCheckPolicy.Evidence Fresh() => new()
{
    capturedAtUtc = "2026-10-11T00:00:00.0000000Z",
    editorPlaying = true,
    reflectionFieldsAvailable = true,
    coherentCapture = true,
    componentsBound = true,
    loggedIn = true,
    loginChoice = 2,
    hasSignedInAccount = true,
    sdkTitle = WalletReadOnlyCheckPolicy.ExpectedTitle,
    sdkAccount = WalletReadOnlyCheckPolicy.ExpectedClassicPlayer,
    dataAccount = WalletReadOnlyCheckPolicy.ExpectedClassicPlayer,
    cloudAccount = WalletReadOnlyCheckPolicy.ExpectedClassicPlayer,
    walletAccount = WalletReadOnlyCheckPolicy.ExpectedClassicPlayer,
    walletTitle = WalletReadOnlyCheckPolicy.ExpectedTitle,
    entityId = WalletReadOnlyCheckPolicy.ExpectedEntity,
    entityType = WalletReadOnlyCheckPolicy.ExpectedEntityType,
    cloudGeneration = 4,
    walletGeneration = 4,
    sessionReady = true,
    legacySourceReady = true,
    canImportLegacy = true,
    canPublishDashboard = true,
    journalAvailable = true,
    journalSchemaVersion = 1,
    journalTitle = WalletReadOnlyCheckPolicy.ExpectedTitle,
    journalAccount = WalletReadOnlyCheckPolicy.ExpectedClassicPlayer,
    journalRewards = 0,
    journalPurchases = 0,
    journalDeliveredRewards = 0,
    completedContracts = 0,
    unlockedAchievements = 0,
    purchasedShopItems = 0,
    unlockedCosmetics = 0,
    unlockedContractMaterials = 0,
    rewardOutbox = 0,
    contractEvidence = 0,
    saveHash = string.Concat(Enumerable.Repeat("abcdef0123456789", 4)),
    matchesLastSyncedHash = true
};

void Reject(string label, Action<WalletReadOnlyCheckPolicy.Evidence> change)
{
    var evidence = Fresh();
    change(evidence);
    Check(WalletReadOnlyCheckPolicy.Evaluate(evidence).Count > 0, label);
}

void SetField(WalletReadOnlyCheckPolicy.Evidence evidence, string name, object value) =>
    typeof(WalletReadOnlyCheckPolicy.Evidence).GetField(name)!.SetValue(evidence, value);

Check(WalletReadOnlyCheckPolicy.Evaluate(Fresh()).Count == 0,
    "exact reviewed fresh zero opening is eligible for read-only review");
Check(WalletReadOnlyCheckPolicy.Evaluate(null).Count > 0, "missing evidence is not approval");
Check(WalletReadOnlyCheckPolicy.Evaluate(new()).Count > 0,
    "unpopulated evidence never manufactures zero or readiness");

foreach (int version in new[] { -1, 0, 2, int.MaxValue })
    Reject("unsupported diagnostic schema " + version, e => e.diagnosticSchemaVersion = version);

foreach (string field in new[] {
    "editorPlaying", "reflectionFieldsAvailable", "coherentCapture", "componentsBound",
    "loggedIn", "hasSignedInAccount", "sessionReady", "legacySourceReady",
    "canImportLegacy", "canPublishDashboard", "journalAvailable"
})
    Reject("required guard " + field, e => SetField(e, field, false));

foreach (string field in new[] {
    "protectedMultiplayerGuest", "requiresSignIn", "hasConflict", "cloudInFlight",
    "suspendedForConflict", "startupSelectionPending", "auxiliaryFileOperation",
    "legacyImportInProgress", "cloudRetryRequired", "walletInFlight", "journalDirty"
})
    Reject("unsafe state " + field, e => SetField(e, field, true));

foreach (int choice in new[] { -1, 0, 1, 3, int.MaxValue })
    Reject("only loginChoice 2 is signed in; reject " + choice, e => e.loginChoice = choice);

foreach (string field in new[] { "sdkTitle", "walletTitle", "journalTitle" })
    foreach (string wrong in new[] { null, "", "17FA04", " 17FA03", "17FA03 ", "17ＦA03" })
        Reject("title binding rejects malformed or other value in " + field,
            e => SetField(e, field, wrong));

foreach (string field in new[] {
    "sdkAccount", "dataAccount", "cloudAccount", "walletAccount", "journalAccount"
})
    foreach (string wrong in new[] { null, "", "30DEB3061B745828", "5D2A34877435C96 ", "5D2A34877435C96/" })
        Reject("each account binding rejects malformed or other account in " + field,
            e => SetField(e, field, wrong));

foreach (string wrong in new[] { null, "", "3AD40507F3D1AD98", "B6C2522543CEB1DA " })
    Reject("cloud entity must be the reviewed title player", e => e.entityId = wrong);
foreach (string wrong in new[] { null, "", "master_player_account", "title", "Title_Player_Account", "title_player_account " })
    Reject("entity type must be exactly title_player_account", e => e.entityType = wrong);

var caseInsensitive = Fresh();
foreach (string field in new[] {
    "sdkTitle", "walletTitle", "journalTitle", "sdkAccount", "dataAccount", "cloudAccount",
    "walletAccount", "journalAccount", "entityId"
})
{
    var info = typeof(WalletReadOnlyCheckPolicy.Evidence).GetField(field)!;
    info.SetValue(caseInsensitive, ((string)info.GetValue(caseInsensitive)!).ToLowerInvariant());
}
Check(WalletReadOnlyCheckPolicy.Evaluate(caseInsensitive).Count == 0,
    "hex identity casing is accepted without trimming or changing entity type");

Reject("negative cloud generation", e => e.cloudGeneration = -1);
Reject("negative matching generations", e => e.cloudGeneration = e.walletGeneration = -1);
Reject("negative wallet generation", e => e.walletGeneration = -1);
Reject("a relogin generation mismatch invalidates evidence", e => e.walletGeneration++);
var generationZero = Fresh();
generationZero.cloudGeneration = generationZero.walletGeneration = 0;
Check(WalletReadOnlyCheckPolicy.Evaluate(generationZero).Count == 0,
    "matching nonnegative initial generations are permitted");
foreach (int schema in new[] { -1, 0, 2, int.MaxValue })
    Reject("journal schema must be version 1; reject " + schema, e => e.journalSchemaVersion = schema);

foreach (string field in new[] { "gold", "lifetimeGoldEarned", "lifetimeGoldSpent", "walletAuthorityVersion" })
    foreach (int value in new[] { -1, 1, int.MinValue, int.MaxValue })
        Reject("raw financial field is zero only: " + field + " at " + value,
            e => SetField(e, field, value));
foreach (long value in new[] { -1L, 1L, long.MinValue, long.MaxValue })
    Reject("wallet coin version is raw zero only: " + value, e => e.walletCoinsVersion = value);
Reject("already migrated zero balance cannot receive legacy opening approval", e => {
    e.walletAuthorityVersion = 1;
    e.walletCoinsVersion = 1;
});

foreach (string field in new[] {
    "completedContracts", "unlockedAchievements", "purchasedShopItems", "unlockedCosmetics",
    "unlockedContractMaterials", "rewardOutbox", "contractEvidence", "journalRewards",
    "journalPurchases", "journalDeliveredRewards"
})
    foreach (int count in new[] { -1, 1, int.MinValue, int.MaxValue })
        Reject("list count is available and empty: " + field + " at " + count,
            e => SetField(e, field, count));

foreach (string invalid in new[] {
    null, "", new string('a', 63), new string('a', 65), new string('A', 64),
    new string('g', 64), new string('0', 63) + " ", new string('0', 63) + "\n",
    new string('0', 63) + "０"
})
    Reject("SHA-256 must have exactly 64 lowercase ASCII hex characters", e => e.saveHash = invalid);

var informationalHash = Fresh();
informationalHash.matchesLastSyncedHash = false;
Check(WalletReadOnlyCheckPolicy.Evaluate(informationalHash).Count == 0,
    "a position-only last-upload hash difference is informational, not a new financial guard");
Reject("a matching upload hash cannot substitute for coherent live capture", e => {
    e.matchesLastSyncedHash = true;
    e.coherentCapture = false;
});
Reject("a matching upload hash cannot authorize offline Resume import", e => {
    e.matchesLastSyncedHash = true;
    e.legacySourceReady = false;
    e.canImportLegacy = false;
});

var immutableEvidence = Fresh();
immutableEvidence.rewardOutbox = -1;
var before = typeof(WalletReadOnlyCheckPolicy.Evidence).GetFields()
    .Select(f => (f, value: f.GetValue(immutableEvidence))).ToArray();
var firstErrors = WalletReadOnlyCheckPolicy.Evaluate(immutableEvidence);
var secondErrors = WalletReadOnlyCheckPolicy.Evaluate(immutableEvidence);
Check(before.All(pair => Equals(pair.value, pair.f.GetValue(immutableEvidence))),
    "evaluation cannot alter evidence, clamp negatives or repair readiness");
Check(firstErrors.SequenceEqual(secondErrors), "pure evaluation is deterministic");

var privateMarker = Fresh();
privateMarker.sdkAccount = "DO_NOT_ECHO_ACCOUNT_PAYLOAD";
privateMarker.saveHash = "DO_NOT_ECHO_SAVE_PAYLOAD";
string fixedErrors = string.Join("\n", WalletReadOnlyCheckPolicy.Evaluate(privateMarker));
Check(!fixedErrors.Contains("DO_NOT_ECHO", StringComparison.Ordinal),
    "failure diagnostics contain fixed messages, not arbitrary input payloads");

// Narrow source smoke checks are guardrails, not exhaustive side-effect proof.
// The policy fixtures above never start Unity or contact a real account/provider.
string sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../../Assets/Editor/WalletReadOnlyCheck.cs"));
string captureSource = File.ReadAllText(sourcePath);
string code = CodeOnly(captureSource);
Check(captureSource.StartsWith("#if UNITY_EDITOR", StringComparison.Ordinal) &&
    captureSource.TrimEnd().EndsWith("#endif", StringComparison.Ordinal),
    "capture window is Editor-only and excluded from player builds");
foreach (string pattern in new[] {
    @"\b(?:UnityWebRequest|HttpClient|WebClient|File|Directory|MethodInfo|Assembly)\b",
    @"\.\s*(?:Invoke|SetValue|GetMethod|Save|SetInt|SetString|SetFloat|DeleteKey|DeleteAll|AddComponent)\s*\(",
    @"\b(?:SaveJournal|MergeOutbox|RefreshWallet|TrySaveGame|UseAccountSave|BeginSession|EndSession|SyncNow|EnsureAttached|GetOrCreate|TryBeginLegacyWalletImport)\s*\(",
    @"\b(?:ClientSessionTicket|EntityToken|AuthenticationContext)\b",
    @"EditorApplication\s*\.\s*isPlaying\s*=(?!=)",
    @"\b(?:Task|Process|Coroutine)\b|\bawait\b|\byield\s+return\b"
})
    Check(!Regex.IsMatch(code, pattern), "capture has no forbidden side-effect/auth pattern " + checks);

string[] apiMethods = Regex.Matches(code, @"\bPlayFab\w*API\s*\.\s*(\w+)\s*\(")
    .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
Check(apiMethods.SequenceEqual(new[] { "IsClientLoggedIn" }),
    "the only SDK API method in capture is the local logged-in boolean getter");
string[] fields = Regex.Matches(captureSource, @"ReadField<[^>]+>\(\w+,\s*""([^""]+)""")
    .Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
string[] expectedFields = {
    "entity", "journal", "saveManager", "playerData", "cloud", "accountId", "titleId", "generation",
    "journalAvailable", "journalDirty", "inFlight", "suspendSyncForConflict", "startupSelectionPending",
    "auxiliaryFileOperationInProgress", "legacyWalletImportInProgress", "retryRequired", "lastSyncedHash"
};
Check(fields.SequenceEqual(expectedFields.OrderBy(x => x, StringComparer.Ordinal)),
    "reflection reads only the reviewed fixed field allowlist");
Check(code.Contains("field.FieldType != typeof(T)", StringComparison.Ordinal) &&
    code.Contains("BindingFlags.DeclaredOnly", StringComparison.Ordinal),
    "reflection fails closed on changed field types and inherited layouts");
Check(code.Contains("ReferenceEquals(firstRefs[i], secondRefs[i])", StringComparison.Ordinal) &&
    code.Contains("string.Equals(firstJson, secondJson, StringComparison.Ordinal)", StringComparison.Ordinal) &&
    code.Contains("JsonUtility.ToJson(first), JsonUtility.ToJson(second)", StringComparison.Ordinal),
    "capture compares live object references, exact compact JSON and all allowlisted readings twice");
Check(code.Contains("GetCurrentDataJson()", StringComparison.Ordinal) &&
    code.Contains("sha.ComputeHash(Encoding.UTF8.GetBytes(json))", StringComparison.Ordinal),
    "capture hashes the same exact compact JSON bytes used by import");
Check(code.Contains("JsonUtility.ToJson(first, true)", StringComparison.Ordinal) &&
    !Regex.IsMatch(code, @"JsonUtility\s*\.\s*ToJson\s*\(\s*(?:data|journal|entity|manager|wallet|cloud)\b"),
    "report serializes only the allowlisted evidence DTO, never full live objects");

Console.WriteLine($"WalletReadOnlyCheck: {checks} pure policy and narrow source smoke checks passed. No Unity invocation or account/provider action.");

static string CodeOnly(string source)
{
    var result = new StringBuilder(source.Length);
    for (int i = 0; i < source.Length; i++)
    {
        char c = source[i];
        if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
        {
            i += 2;
            while (i < source.Length && source[i] != '\n') i++;
            result.Append('\n');
        }
        else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
            i++;
            result.Append(' ');
        }
        else if (c == '"' || c == '\'')
        {
            char quote = c;
            bool verbatim = quote == '"' && i > 0 && source[i - 1] == '@';
            for (i++; i < source.Length; i++)
            {
                if (!verbatim && source[i] == '\\') { i++; continue; }
                if (source[i] != quote) continue;
                if (verbatim && i + 1 < source.Length && source[i + 1] == '"') { i++; continue; }
                break;
            }
            result.Append(' ');
        }
        else result.Append(c);
    }
    return result.ToString();
}
