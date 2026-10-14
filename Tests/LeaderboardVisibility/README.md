# Shared leaderboard contract visibility

Run from the Unity project root:

```
dotnet run --project Tests/LeaderboardVisibility/LeaderboardVisibility.csproj
```

The harness links the production strict parser and all three `LeaderboardPanelUI` partials. Engine lifecycle, authored UI controls, HTTP and PlayFab calls are doubled. It checks loading/error/empty behavior; server allowlist plus local exclusions; exact completion selection; no unrelated fallback; refresh and reopen generations; no credentials in the public request; timeout, redirect and body limits; and multiplayer independence.

These checks do not compile Unity itself, render authored scenes, contact the website, or validate a built Android player. Complete a Unity compile and exercise the main-menu and completion leaderboards on the new APK after publishing the website endpoint. No player saves or statistic submissions are changed by this feature.
