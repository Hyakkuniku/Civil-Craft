# Administrator-selected leaderboard contracts

The website exposes `GET https://civil-craft.vercel.app/api/leaderboard/contracts` with:

```json
{"schemaVersion":1,"contractIds":["ShopKeeper","TUT_CONTRACT1"]}
```

`LeaderboardPanelUI` requests this public display policy on every panel open and manual refresh. Story options and ranks remain cleared while it loads. A valid empty list hides all story choices. Provider, HTTP or validation failures show a retry message instead of revealing every local asset. Multiplayer can still be selected independently. Requests have a ten-second timeout, reject redirects, and receive at most 4 KiB. No session ticket or player identity is sent.

Only IDs selected by the administrator and locally eligible are displayed. `ContractSO.hideFromLeaderboard` and challenge-contract exclusion still apply. Closing/reopening or disabling the panel invalidates the earlier request. Completing a hidden contract shows its display-disabled message rather than opening another contract's ranks. Selecting a different contract intentionally is still available. The Editor-only authoring validation can continue to use `TrySelectContract` with synthetic local contracts without network calls.

Supported permanent IDs match the existing website/server catalog: `ShopKeeper`, `TUT_CONTRACT1`, `ReedsContract`, `VancesContract`, `TUT_CONTRACT2`, `ReedSideInspection`, `VanceSideRealignment`, `SilasMainContract`, and `MainContractSilas`. The local `MarshID` asset is not part of that server catalog and is therefore not a selectable leaderboard. Adding its scoring protocol/catalog entry is a separate content change.

This controls display only. Score encoding, statistic names, submissions, rewards and progression are unchanged. Publish the website endpoint before shipping the new APK; older APKs cannot consume this setting. No scene reauthoring is required.

Verification: run `dotnet run --project Tests/LeaderboardVisibility/LeaderboardVisibility.csproj`, compile Unity, then test both the main-menu and nested completion leaderboard in the new APK. The isolated .NET harness uses doubled engine/UI/HTTP/PlayFab boundaries and does not prove Android rendering or connectivity.
