# Per-location guided tutorial retry policy

Run `dotnet run --project Tests/TrussTutorialRetry/TrussTutorialRetry.csproj` from the Unity project root.

The harness compiles the actual BuildLocation retry-policy field/property and `RestartBuildTutorialAfterFailure`, plus LevelFailedManager's `RetryLevel`, delayed retry coroutine and shared eligibility guard. It verifies the legacy default (`true`), deliberately disabled direct calls/replays, no scheduling for disabled policy, policy changes while waiting, null/destroyed/inactive/unloaded targets, location changes, missing GameManager, exit from building and transitions. Disabled/stale requests preserve bridge, undo, construction lesson completion, saves and ghost state without warnings. Ordinary physics reset, failure-panel close and UI restoration still execute. Valid default retries continue their existing guided-reset behavior; malformed enabled configuration still warns.

The bridge graph, lesson/save writer, physics, tutorial controller and coroutine scheduler are deterministic boundary doubles. This does not run a native Unity scene or touch player saves, accounts or network. The Tutorial 2 authorer must set `restartGuidedTutorialOnFailure` to false only on its existing second BuildLocation; old/missing serialized fields continue to use true.
