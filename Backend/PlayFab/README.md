# Civil Craft PlayFab backend — development setup

This folder contains the legacy PlayFab CloudScript handlers
`submitBridgeRunV1` and `syncDashboardV1`, plus the deployed development
multiplayer handlers `submitMultiplayerResultV1` and
`getMultiplayerLeaderboardV1`. Unity continues to read the single-player top 15
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
metadata already published by a newer build. On October 5, 2026, revision 9
was uploaded and deployed with `submitMultiplayerResultV1` and
`getMultiplayerLeaderboardV1`. Later on October 5, revision 10 was deployed
to fix dashboard publishing; revision 10 is the current live revision.
The Unity submit flag is enabled in Canyon Crossing and Bhan House, and
explicitly disabled in Multiplayer. A signed-in SilasContract completion
reached `submitBridgeRunV1`; both of its live boards show two player entries,
and each entry decodes to the same cost/stress pair in both tabs. The remaining
controlled checks are worse/better redesigns, guest/offline submissions, and
mobile-build gameplay.

1. Run `node Backend/PlayFab/leaderboardCloudScript.test.js`,
   `node Backend/PlayFab/dashboardCloudScript.test.js`, and
   `node Backend/PlayFab/multiplayerCloudScript.test.js` locally.
2. Before uploading, compare the currently live revision with the preserved
   handlers in `mergedCloudScript.dev.js` in case another developer changed it.
   Upload **that merged file**, never a feature-only handler alone.
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

## Development multiplayer leaderboard — revision 9 deployed

The multiplayer handlers are appended to `mergedCloudScript.dev.js` as the
exact contents of `multiplayerCloudScript.js`. The existing sample/Photon,
single-player leaderboard, and dashboard handlers are preserved. On October 5,
2026, the Development title (17FA03) was verified to have revision 8 live. Its
source matched the preserved prefix of the merged file. Revision 9 was uploaded
without deployment and its full source was checked against the local merged
file, including all 16 existing handlers and both new multiplayer handlers.

The three legacy statistic definitions `CC_MP_Wins`, `CC_MP_Losses`, and
`CC_MP_Draws` were created and individually verified with Maximum aggregation
and manual reset. **Allow client to post player statistics** remained disabled.
An authenticated, non-scoring `getMultiplayerLeaderboardV1({})` probe against
staged revision 9 succeeded with an empty board, zero personal totals, two
PlayFab API reads, no HTTP requests, and no CloudScript error. Revision 9 was
then deployed and the UI confirmed **Revision 9 (live)**. The same read-only
probe also succeeded after deployment with no error. No synthetic match
results were submitted and no existing player scores were reset or modified.
The remaining gameplay check is a completed match between two signed-in
accounts, followed by a leaderboard refresh in Unity.

For another title, create **all three** legacy
statistic definitions `CC_MP_Wins`, `CC_MP_Losses`, and `CC_MP_Draws` with
**Maximum** aggregation and **Manual** reset (no automatic reset). Maximum is
required: Sum would double-count every receipt replay, and Last could replace a
newer total with an older concurrent snapshot. Keep **Allow client to post
player statistics** disabled. Use the authenticated CloudScript handlers; no
developer secret or direct client statistic write belongs in Unity.

`submitMultiplayerResultV1` accepts:

```json
{
  "matchId": "f17da9a3c1534dc99f92029e2893d738",
  "opponentId": "OTHER_PLAYFAB_ID",
  "outcome": "win"
}
```

Generate one unique GUID per match, formatted with 32 hexadecimal characters
and no dashes. The two signed-in participants each submit their own result with
the same match ID; the caller's authenticated `currentPlayerId` is the only
account whose data or statistics may be updated. `outcome` is exactly `win`,
`loss`, or `draw`; the opponent must be a nonempty different PlayFab ID. Submitted
totals or a submitted caller ID are not used. An accepted response contains
`accepted`, `duplicate`, the canonical lowercase `matchId`, and
`personal: { wins, losses, draws }`.

Each account retains a server-only `UserInternalData` receipt under
`CC_MP_MATCH_<matchId>`. Sequential duplicate submissions preserve the receipt;
conflicting reuse of the ID is rejected. The handler persists the receipt first,
reads **every** prefixed receipt, and publishes the resulting cumulative totals
with `ForceUpdate: false`. It never increments a stored statistic counter. If
the statistic update fails after receipt persistence, retry the **same** match
ID, opponent, and outcome: the duplicate is accepted and the statistics are
published again without adding another match. Invalid or corrupt receipts and
storage/API failures propagate as errors; receipts are not evicted or silently
ignored to make a request succeed.

`getMultiplayerLeaderboardV1` requires a signed-in session and accepts `{}`.
It reads the Top 15 from `CC_MP_Wins`, obtains each player's wins/losses/draws,
and returns:

```json
{
  "entries": [
    { "rank": 1, "playerId": "PLAYFAB_ID", "displayName": "Engineer",
      "wins": 3, "losses": 1, "draws": 2 }
  ],
  "personal": { "wins": 3, "losses": 1, "draws": 2 }
}
```

Ranks are one-based, the server leaderboard orders entries by wins, and personal
totals are always for the caller even when they are outside the Top 15. The
client calculates `winRate = wins / (wins + losses) * 100`, displaying `—` when there
are no decided matches; draws are excluded. Missing individual statistics are
zero. A PlayFab `StatisticNotFound` error returns an empty board/zero totals;
other errors remain visible. This read handler is also an authenticated,
non-scoring deployment probe, but an empty response does not verify the required
statistic definitions or aggregation configuration.

This is a **development, client-reported** leaderboard. PlayFab authenticates
who submitted each result; the bridge simulation and reported match outcome
still run on the clients. This handler does not verify a Photon room, verify an
opponent's claim, or prove an authoritative match result.

Distinct GUIDs use separate receipt keys, so overlapping matches do not overwrite
each other's records. Maximum aggregation prevents older total snapshots from
lowering published statistics. However, the legacy internal-data update API has
no create-only/compare-and-set operation. Two simultaneously conflicting claims
for the **same** account and GUID can race; post-write confirmation detects a
visible mismatch but is not a transaction. Keep one immutable result per GUID
in the client. A production service should provide transactional match receipts
and authoritative match verification before claiming that guarantee.

The ledger deliberately grows with lifetime matches and is read in full on each
submission. User-data size/key quotas, CloudScript execution limits, and the
additional per-player reads for a Top 15 may eventually require migration to a
transactional match service and a durable aggregate. Do not fix capacity by
truncating/deleting receipts: that permits old IDs to count again and loses the
source of lifetime totals. Do not reset these statistics independently of their
ledger; the next submission would restore lifetime totals. Any migration or
season reset must retain durable deduplication and establish an explicit receipt
generation/baseline with matching statistic versions.

## Dashboard sync repair — revision 10 deployed

The website could load the account name/profile but showed **Awaiting game
sync** for progress. Unity logged repeated CloudScript API failures and the
player's Title Player Data had no dashboard projection. The title's **Settings
→ Limits → Data Storage** showed a maximum of **10 player-data updates per
request**; the previous handler submitted 13 keys (16 with portrait metadata)
in one `UpdateUserData` call.

Revision 10 keeps the same private keys and schema. It sends at most ten keys
per call (10 + 5 with portrait metadata), writes the four summary statistics,
then writes `CharacterSyncedAt` separately after success. Any failure propagates
and Unity retries the full projection. These additive API writes are not an
atomic transaction; the timestamp is a completion/freshness signal, not snapshot
isolation. No website source or PlayFab permissions were changed.

`DashboardSyncService` now uses the persistent `CloudSaveManager` session rather
than the Main Menu's scene-bound authentication component. It waits for the
startup save choice, blocks unresolved or deliberately retained divergent
device saves, resets its publish cache per login/account, and ignores old-session
callbacks. A success requires `accepted: true`; failures report safe API/error
identifiers without printing credentials or raw requests.

Before upload, live revision 9 matched the local merged source. Staged revision
10 was verified against the complete local file with all 18 handlers preserved.
An invalid-schema dashboard probe was rejected with zero PlayFab API/HTTP calls,
so no synthetic player progress was written. The browser then confirmed
**Revision 10 (live)**.

Verification passed: dashboard tests against both the feature and merged
scripts (including a strict ten-key API mock, failed batch/statistic retries,
optional portrait fields, and account routing), multiplayer tests, Unity 2022.3
Roslyn compilation, managed readiness/session/diagnostic checks, and diff checks.
The separate existing single-player leaderboard test still fails because a
`MarshID` contract asset is not in the existing server allowlist; that
configuration was left unchanged by this dashboard repair.

To verify real progress end to end:

1. Stop Unity Play Mode and allow compilation to finish, then start from Main
   Menu and sign into the same account as the website. An installed build must
   be rebuilt to include the game-side repair.
2. Let account-save loading finish and wait at least 5–10 seconds after the last
   save. Login publishes existing progress; completing a bridge or saving a
   wardrobe look also requests a refresh.
3. Confirm Unity logs `[DashboardSync] Player dashboard projection published.`
4. In PlayFab, check **Players → that account → Player Data → Title** for
   `BridgesCompleted`, `MapProgress`, and `CharacterSyncedAt`, then click
   **Refresh** on the website dashboard.

This final gameplay/website check remains pending until the updated game runs.

API references: [internal-data reads](https://learn.microsoft.com/en-us/rest/api/playfab/server/player-data-management/get-user-internal-data),
[internal-data updates](https://learn.microsoft.com/en-us/rest/api/playfab/server/player-data-management/update-user-internal-data),
and [CloudScript error shapes](https://learn.microsoft.com/en-us/xbox/playfab/live-service-management/service-gateway/automation/cloudscript/handling-errors-in-cloudscript).
