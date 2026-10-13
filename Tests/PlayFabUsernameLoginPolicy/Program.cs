using System;
using System.Globalization;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

void Verify(string submitted, string accountUsername, PlayFabUsernameVerification expected, string message)
{
    var attempt = new PlayFabUsernameLoginPolicy(submitted);
    Check(attempt.Verify(accountUsername) == expected, message);
}

foreach (string exact in new[] { "Hyakk", "HYAKK", "hyakk", "CivilBuilder42", "CIVILCRAFT", "MiXeD_CaSe" })
{
    var attempt = new PlayFabUsernameLoginPolicy(exact);
    Check(attempt.Identifier == exact, "Submitted usernames retain every original capital: " + exact);
    Check(!attempt.IsEmailLogin, "A plain username uses exact-case username verification.");
    Check(attempt.Verify(exact) == PlayFabUsernameVerification.Matches,
        "Exact uppercase/mixed-case usernames must be accepted: " + exact);
}

foreach (var pair in new[] {
    (Submitted: "Hyakk", Registered: "hyakk"),
    (Submitted: "HYAKK", Registered: "Hyakk"),
    (Submitted: "CivilBuilder42", Registered: "Civilbuilder42"),
    (Submitted: "MiXeD_CaSe", Registered: "mixed_case") })
    Verify(pair.Submitted, pair.Registered, PlayFabUsernameVerification.Mismatch,
        "A case-only difference cannot authenticate a username.");

var trimmed = new PlayFabUsernameLoginPolicy(" \tHyakk\r\n ");
Check(trimmed.Identifier == "Hyakk", "Only surrounding submitted whitespace is trimmed.");
Check(trimmed.Verify("Hyakk") == PlayFabUsernameVerification.Matches,
    "Whitespace around the submitted username does not prevent an otherwise exact match.");
Check(trimmed.Verify(" Hyakk ") == PlayFabUsernameVerification.Mismatch,
    "The server's registered username must not be trimmed into a match.");
Verify("Civil Builder", "Civil Builder", PlayFabUsernameVerification.Matches,
    "Interior submitted whitespace is preserved.");
Verify("Civil  Builder", "Civil Builder", PlayFabUsernameVerification.Mismatch,
    "Interior submitted whitespace is not collapsed.");
Verify("Builder-1", "Builder_1", PlayFabUsernameVerification.Mismatch,
    "Punctuation is not normalized into a matching username.");

foreach (string missingAccountUsername in new string[] { null, "", " ", "\t\r\n" })
    Verify("Hyakk", missingAccountUsername, PlayFabUsernameVerification.RequiresAccountInfo,
        "Missing/blank account details require a server account-info lookup, never success.");

var nullInput = new PlayFabUsernameLoginPolicy(null);
Check(nullInput.Identifier == string.Empty && !nullInput.IsEmailLogin,
    "Null submitted input is handled without an exception or an email bypass.");
var blankInput = new PlayFabUsernameLoginPolicy(" \t ");
Check(blankInput.Identifier == string.Empty && !blankInput.IsEmailLogin,
    "Whitespace-only submitted input remains empty for the caller's input validation.");
Check(nullInput.Verify("Hyakk") == PlayFabUsernameVerification.Mismatch,
    "A missing submitted identifier cannot accidentally match a registered account.");

string liveInputText = "  Hyakk  ";
var capturedAttempt = new PlayFabUsernameLoginPolicy(liveInputText);
liveInputText = "hyakk";
Check(capturedAttempt.Identifier == "Hyakk", "An asynchronous attempt captures the submitted identifier.");
Check(capturedAttempt.Verify("Hyakk") == PlayFabUsernameVerification.Matches,
    "Editing the visible field cannot invalidate the captured exact-case submission.");
Check(capturedAttempt.Verify(liveInputText) == PlayFabUsernameVerification.Mismatch,
    "A later field edit cannot replace the captured identifier or bypass verification.");
liveInputText = "player@example.com";
Check(!capturedAttempt.IsEmailLogin,
    "Editing the input to an email while awaiting a callback cannot skip username verification.");
Check(capturedAttempt.Verify(null) == PlayFabUsernameVerification.RequiresAccountInfo,
    "A captured username still fails closed when the callback omits account details.");

foreach (string email in new[] { "player@example.com", "PLAYER@EXAMPLE.COM", "  Player@Example.com  " })
{
    var attempt = new PlayFabUsernameLoginPolicy(email);
    Check(attempt.IsEmailLogin && attempt.Identifier == email.Trim(),
        "The existing email-login path stays classified as email without lowercase rewriting.");
    foreach (string accountUsername in new string[] { null, "", "Hyakk", "SOME_OTHER_USERNAME" })
        Check(attempt.Verify(accountUsername) == PlayFabUsernameVerification.NotRequired,
            "Email login does not enforce an unrelated account-username capitalization check.");
}

CultureInfo originalCulture = CultureInfo.CurrentCulture;
CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
try
{
    foreach (string cultureName in new[] { "en-US", "tr-TR" })
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        Verify("Istanbul", "Istanbul", PlayFabUsernameVerification.Matches,
            "Exact ASCII capital I remains valid in " + cultureName);
        Verify("Istanbul", "istanbul", PlayFabUsernameVerification.Mismatch,
            "ASCII I/i comparison is ordinal, not culture-aware in " + cultureName);
        Verify("Istanbul", "\u0131stanbul", PlayFabUsernameVerification.Mismatch,
            "Turkish dotless i cannot match ASCII I in " + cultureName);
        Verify("\u0130stanbul", "\u0130stanbul", PlayFabUsernameVerification.Matches,
            "An exactly submitted Turkish dotted I remains valid in " + cultureName);
        Verify("\u0130stanbul", "istanbul", PlayFabUsernameVerification.Mismatch,
            "Turkish dotted I cannot match lowercase i in " + cultureName);
        Verify("Builder\u00E9", "Buildere\u0301", PlayFabUsernameVerification.Mismatch,
            "Unicode normalization cannot silently change exact username spelling in " + cultureName);
    }
}
finally
{
    CultureInfo.CurrentCulture = originalCulture;
    CultureInfo.CurrentUICulture = originalUiCulture;
}

foreach (string validName in new[] { "HYAKK", "Hyakk", "CivilBuilder42", "UPPER CASE NAME" })
    Check(PlayerNamePolicy.TryValidate(validName, out string error) && error == null,
        "Existing registration/display-name policy accepts uppercase letters: " + validName);
Check(!PlayerNamePolicy.TryValidate("Example.DEV_Tester", out _),
    "Existing reserved developer-name rule is not weakened by username-login changes.");
Check(!PlayerNamePolicy.TryValidate("<b>HYAKK</b>", out _),
    "Existing plain-text player-name validation remains active.");

Check(!string.IsNullOrWhiteSpace(PlayFabUsernameLoginPolicy.CapitalizationError),
    "Case mismatch has a player-readable error.");
Check(!string.IsNullOrWhiteSpace(PlayFabUsernameLoginPolicy.VerificationError),
    "Unavailable account verification has a player-readable error.");
Check(typeof(PlayFabUsernameLoginPolicy).IsSealed, "Captured login attempts cannot be altered by subclass behavior.");

// Read-only integration guards for the actual Unity entry point. These do not
// instantiate the manager, contact PlayFab, or read/write player preferences.
string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string managerSource = System.IO.File.ReadAllText(System.IO.Path.Combine(projectRoot,
    "Assets/Script/PlayFabAuthManager.cs"));
string MethodRegion(string startSignature, string nextSignature)
{
    int start = managerSource.IndexOf(startSignature, StringComparison.Ordinal);
    int end = start >= 0 ? managerSource.IndexOf(nextSignature, start + startSignature.Length, StringComparison.Ordinal) : -1;
    Check(start >= 0 && end > start, "Actual login integration method is present: " + startSignature);
    return managerSource.Substring(start, end - start);
}

string click = MethodRegion("public void OnLoginButtonClicked()", "public void OnRegisterButtonClicked()");
Check(click.Contains("new PlayFabUsernameLoginPolicy(loginUsername.text)", StringComparison.Ordinal) &&
    click.Contains("string inputId = attempt.Identifier;", StringComparison.Ordinal),
    "The actual button captures one immutable identifier before sending either login request.");
Check(click.Contains("Username = inputId", StringComparison.Ordinal) &&
    click.Contains("Email = inputId", StringComparison.Ordinal),
    "Actual login requests use the captured spelling for both username and email.");
Check(click.Split("result => OnLoginSuccess(result, generation, attempt)", StringSplitOptions.None).Length == 3,
    "Both username and email callbacks carry their original captured attempt.");
Check(!click.Contains("ToLower", StringComparison.Ordinal) && !click.Contains("ToUpper", StringComparison.Ordinal),
    "The actual login entry point never rewrites username case.");

string success = MethodRegion("private void OnLoginSuccess(LoginResult result, int generation, PlayFabUsernameLoginPolicy attempt)",
    "private void VerifySubmittedUsername(");
string lookup = MethodRegion("private void VerifySubmittedUsername(", "private void FinishUsernameVerification(");
string finish = MethodRegion("private void FinishUsernameVerification(", "private void RejectUsernameLogin(");
string reject = MethodRegion("private void RejectUsernameLogin(", "private void AcceptVerifiedManualLogin(");
string accept = MethodRegion("private void AcceptVerifiedManualLogin(", "private void CompleteManualLogin(");
Check(!(success + lookup + finish + reject + accept).Contains("loginUsername.text", StringComparison.Ordinal),
    "No asynchronous success/verification callback rereads the mutable username field.");
Check(success.Contains("if (attempt.IsEmailLogin)", StringComparison.Ordinal) &&
    success.Contains("AcceptVerifiedManualLogin(result, generation)", StringComparison.Ordinal),
    "Email login retains its explicit unchanged bypass of username-only spelling verification.");
Check(success.Contains("attempt.Verify(account?.Username)", StringComparison.Ordinal) &&
    success.Contains("PlayFabUsernameVerification.RequiresAccountInfo", StringComparison.Ordinal) &&
    success.Contains("VerifySubmittedUsername(result, generation, attempt)", StringComparison.Ordinal),
    "Missing optional combined account data triggers verification instead of accepting the login.");
Check(lookup.Contains("ClientSessionTicket = result.SessionTicket", StringComparison.Ordinal) &&
    lookup.Contains("AuthenticationContext = context", StringComparison.Ordinal) &&
    lookup.Contains("PlayFabId = result.PlayFabId", StringComparison.Ordinal),
    "The fallback lookup uses the authenticated result's ticket and account ID, not mutable global input.");
Check(lookup.Contains("string.Equals(account.PlayFabId, result.PlayFabId, StringComparison.Ordinal)", StringComparison.Ordinal),
    "Fallback details must belong to the same authenticated account.");
Check(lookup.Contains("attempt.Verify(account.Username)", StringComparison.Ordinal),
    "The fallback applies exact spelling to the original captured submission.");
Check(lookup.Split("generation != authenticationGeneration", StringSplitOptions.None).Length == 3,
    "Both fallback success and error callbacks reject stale authentication generations.");
Check(lookup.Contains("error =>", StringComparison.Ordinal) &&
    lookup.Contains("RejectUsernameLogin(PlayFabUsernameLoginPolicy.VerificationError, generation)", StringComparison.Ordinal),
    "Unavailable fallback account lookup fails closed with a readable error.");
int matchesGuard = finish.IndexOf("verification != PlayFabUsernameVerification.Matches", StringComparison.Ordinal);
int acceptCall = finish.IndexOf("AcceptVerifiedManualLogin(result, generation)", StringComparison.Ordinal);
Check(matchesGuard >= 0 && acceptCall > matchesGuard &&
    finish.Contains("RejectUsernameLogin", StringComparison.Ordinal),
    "Only verified exact-case username matches can reach the save/account-selection workflow.");
Check(reject.Contains("manualLoginInProgress = false", StringComparison.Ordinal) &&
    reject.Contains("PlayFabClientAPI.ForgetAllCredentials()", StringComparison.Ordinal),
    "Rejected verification releases the UI guard and invalidates rejected account credentials.");
Check(!reject.Contains("PlayerPrefs", StringComparison.Ordinal) &&
    !reject.Contains("CurrentData", StringComparison.Ordinal) &&
    !reject.Contains("CompleteManualLogin", StringComparison.Ordinal) &&
    !reject.Contains("BeginCloudSaveLogin", StringComparison.Ordinal),
    "Wrong capitalization cannot modify remembered-login choices or start a save/account switch.");
Check(accept.Contains("CompleteManualLogin(result, generation)", StringComparison.Ordinal),
    "A verified login continues through the existing save-choice workflow.");

string registration = MethodRegion("public void OnRegisterButtonClicked()", "public void OnForgotPasswordButtonClicked()");
Check(registration.Contains("if (manualLoginInProgress || IsAutomaticLoginInProgress)", StringComparison.Ordinal) &&
    registration.IndexOf("if (manualLoginInProgress || IsAutomaticLoginInProgress)", StringComparison.Ordinal) <
    registration.IndexOf("PlayFabClientAPI.RegisterPlayFabUser", StringComparison.Ordinal),
    "Registration cannot replace the SDK account while a username/login is being verified.");

Console.WriteLine($"PASS: {checks} exact-case username-login checks; no Unity, network, account, or save-state mutation.");
