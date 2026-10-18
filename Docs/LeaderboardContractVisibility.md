# Administrator-selected leaderboard contracts

The website exposes `GET https://civil-craft.vercel.app/api/leaderboard/contracts` with:

```json
{"schemaVersion":1,"contractIds":["ShopKeeper","TUT_CONTRACT1"]}
```

`LeaderboardPanelUI` requests this public display policy on every panel open and manual refresh. Story options and ranks remain cleared while it loads. A valid empty list hides all story choices. Provider, HTTP or validation failures show a retry message instead of revealing every local asset. Multiplayer can still be selected independently. Requests have a ten-second timeout, reject redirects, and receive at most 4 KiB. No session ticket or player identity is sent.

Only IDs selected by the administrator and locally eligible are displayed. `ContractSO.hideFromLeaderboard` and challenge-contract exclusion still apply. Closing/reopening or disabling the panel invalidates the earlier request. Completing a hidden contract shows its display-disabled message rather than opening another contract's ranks. Selecting a different contract intentionally is still available. The Editor-only authoring validation can continue to use `TrySelectContract` with synthetic local contracts without network calls.

The Level Complete **Leaderboard** action independently requests the same policy for each new report, including multiplayer-guest reports. It starts hidden/noninteractive and is revealed only for that exact completed contract after a successful public allowlist response. Hidden, empty, malformed, offline and failed responses leave the action hidden; an earlier report's approval is never reused. Close, retry/reset, controller disable and teardown cancel approval and invalidate late responses. The public click method also rejects unapproved contracts. Opening the nested board still performs its own fresh policy check, so it cannot silently fall back to another level. This is a runtime-only change; the authored completion panel does not need rebuilding.

Supported permanent IDs match the existing website/server catalog: `ShopKeeper`, `TUT_CONTRACT1`, `ReedsContract`, `VancesContract`, `TUT_CONTRACT2`, `ReedSideInspection`, `VanceSideRealignment`, `SilasMainContract`, and `MainContractSilas`. The local `MarshID` asset is not part of that server catalog and is therefore not a selectable leaderboard. Adding its scoring protocol/catalog entry is a separate content change.

This controls display only. Score encoding, statistic names, submissions, rewards and progression are unchanged. Publish the website endpoint before shipping the new APK; older APKs cannot consume this setting. No scene reauthoring is required.

Verification: run `dotnet run --project Tests/LeaderboardVisibility/LeaderboardVisibility.csproj`, compile Unity, then test both the main-menu and nested completion leaderboard in the new APK. The isolated .NET harness uses doubled engine/UI/HTTP/PlayFab boundaries and does not prove Android rendering or connectivity.

19 October 2026: the expanded harness passed 193 checks, including completion-action visibility and source lifecycle hooks; the runtime sources also compiled against Unity 2022.3.62f3. The public production policy endpoint returned HTTP 200. No website settings, scenes, player saves, rewards or statistic submissions were modified by this fix. Live Play Mode and Android smoke tests remain manual; refresh scripts and start a fresh Play session, or build a new APK.
