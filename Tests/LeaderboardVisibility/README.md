# Shared leaderboard contract visibility

Run from the Unity project root:

```
dotnet run --project Tests/LeaderboardVisibility/LeaderboardVisibility.csproj
```

The harness links the production strict parser, all three `LeaderboardPanelUI` partials, and `CompletionLeaderboardVisibility`. Engine lifecycle, authored UI controls, HTTP and PlayFab calls are doubled. It checks loading/error/empty behavior; server allowlist plus local exclusions; exact completion selection; no unrelated fallback; refresh and reopen generations; no credentials in the public request; timeout, redirect and body limits; and multiplayer independence.

The completion-action checks verify that the authored button stays hidden until fresh public approval, remotely hidden reports have no action, earlier reports or failed checks cannot reuse approval, local exclusions still apply, request cancellation is safe, late callbacks cannot reopen the action, and temporarily covering the report with a nested panel does not lose approval. Explicit source-wiring assertions check regular/guest report entry, reset/close/disable/destroy cancellation and the guarded public open action. These source checks do not execute the real completion manager; its scene lifecycle still requires a Unity compile and Play Mode check.

These checks do not compile Unity itself, render authored scenes, contact the website, or validate a built Android player. Complete a Unity compile and exercise the main-menu and completion leaderboards on the new APK after publishing the website endpoint. No player saves or statistic submissions are changed by this feature.
