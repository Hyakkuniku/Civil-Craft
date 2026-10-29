# Game and website integration: private bridge album

This implementation extends the existing dashboard publisher. It does not change
wallet authority, payment configuration, reward amounts, leaderboard formulas,
player account records, or unrelated scene/story edits. No operator migration is
required for its optional save fields. An installed APK must be rebuilt to use it.

## Actual saved records

- `completedContracts` establishes contract turn-in. A successful saved bridge
  alone is not a completed contract. New completion dates are committed with the
  existing turn-in save; a failed save rolls them back with existing progression.
- A successful saved design gets an immutable `resultId`. Its latest star result,
  geometry and optional photo refer to that same design. Historical unknown dates
  and scores remain unavailable; no historical photos are manufactured.
- Lesson archive IDs publish as `discovered`, never as tutorial/story completion.
  Material discoveries remain cumulative; ConcreteBeam maps to concrete_member.
- Genuine quiz submissions save a stable latest aggregate with their answers in
  the same transaction. New results have actual submission dates. A legacy result
  is eligible only when one retained context and its exact current fingerprint
  validate. Ambiguous results are omitted rather than arbitrarily called latest.
- Submitted answer reviews now additionally capture canonical selected indices
  with that aggregate and publish a separate private `QuizReviewProgress` value.
  The website resolves only the matching retained quiz fingerprint after owner
  authentication. See the website's `docs/quiz-answer-review.md`. This adds no
  Entity File slot. Older results without exact retained evidence stay unavailable.
- Equipment publishes every accessory and explicitly empty wardrobe slots.

The v1 outer dashboard request is unchanged. Its nested Almanac now includes
`publisherVersion:2`, the established optional lesson/quiz fields and a versioned
snapshot. The backend preserves omitted additions from older publishers; modern
turn-in dates (including unknown dates) must not become old APK sync timestamps.
All four dashboard statistics are absolute snapshots written with ForceUpdate;
CC_E_, CC_S_ and multiplayer submission handlers are unchanged.

## One extra PlayFab file

`bridge-photos-v1.zip` is a private account album, not one remote file per bridge.
It contains a UTF-8 manifest and canonical STORE-only PNG entries. It uses one
additional title-player file slot. The verified title limit is ten files; existing
save/portrait/other files are never deleted to make room.

Application limits: 8 MiB archive, 64 photos, 64 KiB index, 512 KiB per PNG and
960x540 maximum website-image dimensions. Original local photos remain unchanged.
The website protocol and exact reader behavior are documented in the website
repository's `docs/bridge-photo-album.md`. These are application limits, not a
promise of unlimited storage or a claim about PlayFab's per-file maximum.

Only the original successful-result capture queues an optimized website copy.
Local regenerated bridge previews do not silently become historical photos.
Pending optimized PNGs and their account-scoped queue survive restarts. A new
candidate preserves the remote album's unrelated contracts and replaces only the
selected contract. Oversize/invalid albums remain pending with a clear diagnostic;
no unrelated photo is silently discarded.

Media waits for the selected game save to be synchronized. It reserves the file
slot for one account/login generation, observes the entity profile version and
reconciles gameplay again if another device advances it. Initiation uses that
expected version. Successful finalization precedes local snapshot metadata and
dashboard publication. A late result cannot attach to a redesigned bridge.

Known album and portrait initiation receipts/candidates are durably recorded.
Cloud-save startup reconciles them before attempting another file transaction,
avoiding a ready-state deadlock after interruption. Automatic recovery is limited
to the exact acknowledged account/entity/file/profile. Unknown pending operations
or a corrupt receipt remain blocked for review; never delete receipts or abort an
unidentified upload based on time/current balance. A network loss before an
initiation acknowledgment is recorded can therefore require operator review.

Each website image request authenticates its owner and binds completion, result,
manifest entry, hash and PNG. A bounded owner/checksum cache deduplicates downloads
but never replaces authentication. Signed storage URLs and credentials stay out
of browser image responses and logs. Missing/stale references show placeholders;
the completion and rewards are not removed or replayed.

## Local verification

Build test harnesses with `--artifacts-path Temp/<name>` and execute the generated
DLL to avoid modifying existing tracked test artifacts:

```powershell
node Backend/PlayFab/dashboardCloudScript.test.js
node Backend/PlayFab/dashboardCloudScript.test.js mergedCloudScript.dev.js
node Backend/PlayFab/multiplayerCloudScript.test.js
dotnet build Tests/GameDashboardProjection/GameDashboardProjection.csproj --artifacts-path Temp/GameDashboardProjection --verbosity quiet
dotnet Temp/GameDashboardProjection/bin/GameDashboardProjection/debug/GameDashboardProjection.dll
dotnet build Tests/BridgePhotoAlbum/BridgePhotoAlbum.csproj --artifacts-path Temp/BridgePhotoAlbum --verbosity quiet
dotnet Temp/BridgePhotoAlbum/bin/BridgePhotoAlbum/debug/BridgePhotoAlbum.dll
dotnet build Tests/CloudSaveWalletReadiness/CloudSaveWalletReadiness.csproj --artifacts-path Temp/CloudSaveWalletReadiness --verbosity quiet
dotnet Temp/CloudSaveWalletReadiness/bin/CloudSaveWalletReadiness/debug/CloudSaveWalletReadiness.dll
```

The codec tests exchange actual C# and TypeScript writer fixtures. The full current
catalog projection fixture checks all playable contracts/photos, lessons, quizzes
and achievements against the existing 10,000-character nested and 30,000-byte
argument limits. New catalog growth must rerun this check; never truncate JSON.
Production-source tests use fake engine/provider boundaries, not live accounts.
`Tests/GameWebsiteNativeCompile` additionally checks actual Unity runtime, Editor
and saved Android compiler references without creating an APK.

Known unrelated baseline: the single-player leaderboard allowlist test currently
rejects MarshID because it exists in game assets but not the preserved server
allowlist. This integration does not invent a ranking protocol or change it.

## Coordinated acceptance and rollout

1. Review/deploy the compatible website reader first. Compare the actual live
   CloudScript with the merged source and preserve every unrelated handler before
   publishing the reviewed dashboard compatibility change. Never deploy the
   feature-only script over the complete title revision.
2. Build one integration Development APK without Deep Profiling; keep the public
   APK download unchanged until acceptance. Record its source revision, website
   deployment and actual executed CloudScript revision.
3. Use two approved accounts. With A, discover a lesson/material, submit a quiz,
   build a bridge and verify the website does NOT complete its contract until
   turn-in. Verify the resulting photo/score/date, then redesign and check exact
   latest-result association. Save multiple accessories and verify them separately
   from ownership/portrait upload.
4. Interrupt connectivity during upload, restart, reconnect and verify recovery.
   Use a second device to update a different contract and confirm both photos
   survive; exercise a conflicting selected save through the existing choice UI.
5. Switch to B on game and website and confirm A's records/images never appear.
   Test expired login, missing image, repeated synchronization and mobile/desktop
   rendering. Rewards and wallet balances must not change merely from viewing or
   retrying these records.
6. Record sanitized evidence. Only then approve the public APK release. This is
   one coordinated two-account pass, not testing every player or migrating Coins.

Implementation/local tests do not constitute live-provider or installed-device
sign-off. Do not publish a development APK or enable payments to bypass that check.

### Implementation verification, 2026-10-29

- Website commit `d9aadf5` was pushed to Astra1127/CivilCraft main. Vercel's commit
  status reported success. Main-site root and the newly exported catalog image
  returned 200; signed-out bridge-snapshot access returned 401.
- Website TypeScript, scoped lint, production build and focused integration tests
  passed. Actual C# and TypeScript album fixtures were decoded across both readers.
- Production-source dashboard/quiz/photo-metadata tests, album/upload tests,
  cloud-save/media recovery tests, quiz-review regressions and existing wallet
  recovery regressions passed. Full game, Editor and saved Android-target sources
  compiled against the installed Unity references. Existing unused-field warnings
  are separate from this integration.
- PlayFab Development title `17FA03` now has **Revision 11 (live)**. Browser
  access was restored. The complete live Revision 10 source matched the local
  baseline after line-ending normalization (SHA-256
  `b8eca9e00561b7719debde2b3387ff7b47af790fcfa04e5982c48354d6257bf8`).
  Revision 11 was uploaded without deployment, then its complete editor source
  matched the reviewed merged file (SHA-256
  `be6534654b5a2244e13ec83ffc3860ae7c57d457ccd7c77fbbabc111ef1ff0b3`).
  All 18 handlers were preserved; code outside the dashboard section matched
  the baseline exactly. After publication the UI confirmed Revision 11 live.
  No synthetic player progress or provider execution test was submitted.
- The implementation verification above did not build/install an APK or replace
  the public installer. The operator subsequently installed a new APK and
  confirmed the acceptance results below. Deployment work did not directly edit
  player records, balances or payment flags.

### Operator acceptance confirmation, 2026-10-29

The operator reported completing and passing the coordinated checklist on the
newly installed APK, website and Unity Editor:

- Account A's existing progress and balances remained correct after login/sync.
- Real lesson/material discoveries and the latest quiz score appeared online.
- Successful bridge construction did not complete the contract before turn-in.
- Turn-in published matching completion, bridge photo and score.
- A successful redesign displayed its own photo/score, and updating another
  contract preserved the earlier contract's photo.
- Multiple equipped accessories and an explicitly cleared slot synchronized.
- Offline save, restart and reconnection recovered progress/photo delivery
  without duplicate rewards.
- Account B displayed only its own progress/photos; switching back preserved A.
- Updating different contracts on the APK and Unity Editor preserved both photos.

These are operator-reported installed-device results, not a second independent
agent observation. The exercised acceptance checklist is complete and the tested
build is ready for release preparation. The exact APK filename/hash and source
revision still need to be recorded with the release artifact; no public download
was replaced by recording this confirmation. Website deployment is commit
`d9aadf5`; the published backend is PlayFab title `17FA03`, revision 11.

### Answer review extension, 2026-10-29

Website commit `d7b6db1` adds on-demand private answer reviews to Quiz Records;
the live page was verified after deployment. PlayFab revision 12 is now live and
accepts the separate compact review projection with its explicit schema ack.
The earlier photo acceptance pass does not certify this later quiz extension.
An older live submission correctly returned unavailable while preserving its
score because no answer selections had yet been synchronized.

The game source now captures canonical choices with the submitted aggregate and
can reconstruct older reviews only from exact retained evidence. The next APK
build is required. The remaining installed-device check is to synchronize one
retained or newly submitted quiz, compare Review answers with the game's result,
and switch accounts to verify that the first account's answers disappear.
No new public installer was uploaded for this extension.
