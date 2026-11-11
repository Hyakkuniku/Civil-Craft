using System;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

const string email = "builder+tag@example.com";
const string password = "Bridge123";
string Registration(string username = "Civil_Builder", string address = email, string secret = password,
    string confirmation = password, bool consent = true) =>
    CivilCraftAccountPolicy.RegistrationError(CivilCraftAccountPolicy.TrimIdentifier(username),
        CivilCraftAccountPolicy.TrimIdentifier(address), secret, confirmation, consent);

foreach (string username in new[] { "ABC", "_b7", "Civil_Builder", new string('Z', 24), " \tMiXeD_CaSe \n" })
    Check(Registration(username) == null, "Website accepts valid usernames.");
foreach (string username in new[] { "ab", "bridge-builder", "bridge.builder", "Bridge Builder", "Enginéér", new string('a', 25) })
    Check(Registration(username) != null, "Website rejects invalid sign-up usernames.");
foreach (string address in new[] { "user@example.com", " USER+builder@example.com ", "o'brien@sub.example.com" })
    Check(Registration(address: address) == null, "Website accepts supported email addresses.");
foreach (string address in new[] { "a@localhost", ".x@example.com", "x..y@example.com", "x@-example.com",
    "x@site.1", new string('a', 245) + "@example.com" })
    Check(Registration(address: address) != null, "Website rejects malformed or oversized email addresses.");

foreach (string secret in new[] { "Bridge1!", " Bridge1 ", "Bridge1\n", "Bridge1" + new string('_', 121) })
    Check(Registration(secret: secret, confirmation: secret) == null, "Password policy permits characters allowed by the website.");
foreach (string secret in new[] { "Abcde1", "bridges1", "BRIDGES1", "Bridge١١", "Bridge1" + new string('_', 122) })
    Check(Registration(secret: secret, confirmation: secret) != null, "Password requires website length and ASCII letter/number checks.");
Check(Registration(confirmation: "Bridge124") != null, "Confirmation is an exact match.");
Check(Registration(consent: false) != null, "Both website policies are mandatory at sign-up.");

Check(CivilCraftAccountPolicy.TrimIdentifier(" \tMiXeD_CaSe ") == "MiXeD_CaSe", "Preserve submitted username casing.");
Check(!CivilCraftAccountPolicy.IsEmailLogin("Mixed_Case") && CivilCraftAccountPolicy.IsEmailLogin(email), "Route login by identifier type.");
Check(CivilCraftAccountPolicy.LoginError("Legacy builder-name", "abcd") == null, "Existing usernames use website sign-in rules.");
Check(CivilCraftAccountPolicy.LoginError("abc", "123") != null, "Short login passwords fail.");
Check(CivilCraftAccountPolicy.LoginError("abc", new string('a', 129)) != null, "Oversized login passwords fail.");
Check(CivilCraftAccountPolicy.LoginError("a@bad", "1234") != null, "Malformed sign-in email fails.");
Check(PlayerNamePolicy.TryValidate("Civil Builder", out _), "The in-game engineer name stays independent from the website username.");

{
    const string player = "C123";
    Check(CivilCraftAccountPolicy.CheckVerification(200, player, player, true, "Confirmed") ==
        CivilCraftAccountPolicy.Verification.Confirmed, "Only a matching confirmed website response admits sign-in.");
    foreach (var item in new[] { (200L, player, false, "Unconfirmed"), (200L, player, false, "Unknown"), (403L, "", false, "") })
        Check(CivilCraftAccountPolicy.CheckVerification(item.Item1, player, item.Item2, item.Item3, item.Item4) ==
            CivilCraftAccountPolicy.Verification.Unconfirmed, "An unconfirmed account requires verification.");
    foreach (var item in new[] { (200L, "C456", true, "Confirmed"), (200L, player, true, ""),
        (200L, player, false, "Confirmed"), (200L, (string)null, false, (string)null),
        (401L, player, true, "Confirmed"), (503L, player, true, "Confirmed") })
        Check(CivilCraftAccountPolicy.CheckVerification(item.Item1, player, item.Item2, item.Item3, item.Item4) ==
            CivilCraftAccountPolicy.Verification.Unavailable, "Malformed or unavailable verification cannot admit sign-in.");
}

string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string manager = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/Script/PlayFabAuthManager.cs"));
string website = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/Script/Client/CivilCraftAccountWebsite.cs"));
string Part(string start, string end) => manager.Split(start, StringSplitOptions.None)[1].Split(end, StringSplitOptions.None)[0];
string manual = Part("private void OnLoginSuccess(", "private void ShowVerificationResult(");
string auto = Part("private void OnAutomaticLoginSuccess(", "private void CompleteAutomaticLogin(");
foreach (string gate in new[] { manual, auto })
    Check(gate.Contains("CivilCraftAccountWebsite.CheckEmail") &&
        gate.Contains("CivilCraftAccountPolicy.Verification.Confirmed") &&
        gate.Contains("generation != authenticationGeneration"), "Manual and device sign-in require a fresh, matching website decision.");
string register = Part("public void OnRegisterButtonClicked()", "public void OnForgotPasswordButtonClicked()");
Check(register.Contains("termsAgreementToggle.isOn") && register.Contains("emailUpdatesToggle.isOn") &&
    register.Contains("new PlayFabClientInstanceAPI()") && register.Contains("client.RegisterPlayFabUser"),
    "Actual registration reads both consent choices and keeps the new account provisional.");
Check(!manager.Contains("PlayFabClientAPI.LoginWith") &&
    new[] { "LoginWithEmailAddress", "LoginWithPlayFab", "LoginWithAndroidDeviceID", "LoginWithIOSDeviceID" }
        .All(method => manager.Contains("client." + method)),
    "All login methods use an isolated SDK client until verification.");
Check(!manager.Contains("SendAccountRecoveryEmail") && website.Contains("/api/email/recovery") &&
    website.Contains("/api/player/email-preference") && website.Contains("/api/player/auth/resend-verification"),
    "The game uses the website's account email services.");
Console.WriteLine($"PASS: {checks} website account policy and wiring checks.");
