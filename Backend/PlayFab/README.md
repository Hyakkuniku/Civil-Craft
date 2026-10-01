# Civil Craft PlayFab backend — development setup

This folder contains the legacy PlayFab CloudScript handlers
`submitBridgeRunV1` and `syncDashboardV1`. Unity continues to read the top 15
through the existing `LeaderboardPanelUI`, while the dashboard handler writes
the private website projection and summary statistics. Client statistic writes
remain disabled.

Saving a wardrobe look also renders a transparent 512×512 character portrait
and atomically replaces the private Entity File `characterPortrait.png`. See
`CHARACTER_PORTRAIT_WEBSITE.md` for the authenticated website download flow.

On September 30, 2026, the Civil Craft **Development** title (17FA03) had
legacy CloudScript revision 3 live. `mergedCloudScript.dev.js` preserves its
existing sample/Photon handlers and adds the leaderboard handler. All 18
`CC_E_`/`CC_S_` statistic definitions for the nine playable contracts were
created in Development with **Maximum** aggregation and manual reset.
Revision 4 was uploaded and deployed after a non-scoring validation test:
`submitBridgeRunV1` rejected an unknown contract with zero PlayFab API calls.
On October 1, 2026, revision 5 was uploaded and deployed with
`syncDashboardV1`; the uploaded revision was checked for both the dashboard and
existing leaderboard handlers before deployment.
Later on October 1, 2026, revision 6 was uploaded and deployed with the
character-portrait metadata fields. The uploaded revision was checked for
`submitBridgeRunV1`, `syncDashboardV1`, and `CharacterPortraitFile` before it
was made live.
Revision 7 immediately superseded it with backward-compatible optional portrait
fields, so installed builds that predate portrait upload can still call
`syncDashboardV1`. Revision 8 then made missing portrait fields preserve any
metadata already published by a newer build. Revision 8 is the current live
revision.
The Unity submit flag is enabled in Canyon Crossing and Bhan House, and
explicitly disabled in Multiplayer. A signed-in SilasContract completion
reached `submitBridgeRunV1`; both of its live boards show two player entries,
and each entry decodes to the same cost/stress pair in both tabs. The remaining
controlled checks are worse/better redesigns, guest/offline submissions, and
mobile-build gameplay.

1. Run `node Backend/PlayFab/leaderboardCloudScript.test.js` and
   `node Backend/PlayFab/dashboardCloudScript.test.js` locally.
2. Before uploading, compare the currently live revision with the preserved
   handlers in `mergedCloudScript.dev.js` in case another developer changed it.
   Upload **that merged file**, never either feature-only handler alone.
3. If a new playable contract is added later, create its `CC_E_<hash>` and
   `CC_S_<hash>` legacy statistic definitions with **Max** aggregation and no
   automatic reset. The names are listed in the `ccLeaderboardContracts` table
   and must match `LeaderboardScoreCodec.StatisticName`.
4. For a future revision, upload without deploying, test with a signed-in
   development account, then deploy. Do not put a PlayFab secret
   key in Unity and do not enable **Allow client to post player statistics**.
5. Keep **Submit Saved Runs To Leaderboard** enabled only on single-player
   `LevelCompleteManager` components and disabled in Multiplayer. A submission
   happens only after a successful local `SaveBridgeData` commit.
6. Test two signed-in accounts on the same contract: confirm that both players
   appear in the Top 15, a worse redesign does not replace a best, and a run
   that improves only one metric updates only that tab. Also test guest mode,
   offline mode, and a rejected/invalid submission.

Only current playable contract IDs are accepted by the server function. When
adding a new contract, update its server allowlist and both Max statistic
definitions before enabling its online ranking.

This is a **test leaderboard**, not an anti-cheat-certified competition. The
server checks the authenticated account, known contract, integer ranges, and
best-score monotonicity, but the bridge simulation runs on the device; a
modified client could still submit plausible false results. A fully trusted
leaderboard requires server-side reconstruction and simulation of each bridge.
Network failures are logged but not yet queued for retry, so a player may need
to save a successful redesign after reconnecting.
