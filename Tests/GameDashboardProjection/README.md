# Dashboard projection integration regression checks

Run from the Unity repository root:

```powershell
dotnet build Tests/GameDashboardProjection/GameDashboardProjection.csproj --artifacts-path Temp/GameDashboardProjection --verbosity quiet
dotnet Temp/GameDashboardProjection/bin/GameDashboardProjection/debug/GameDashboardProjection.dll
```

For a cross-runtime check, append `--quiz-review-catalog` and the absolute path to
the website checkout's `src/lib/playfab/quiz-review-catalog.server.json`. This
reconstructs each authored assessment bank as an actual C# `QuizData`, compares
its fingerprint to the website export, and uses real canonical answer snapshots
in the full-catalog payload budget test. Without that optional artifact, the
budget still includes a bounded review for every current quiz asset.

Links actual projection, save records and quiz validation/grading production sources.
The quiz transaction test extracts the current actual save method with Roslyn and
executes it against a failing/succeeding disk boundary. Engine/SDK boundaries are
doubles; no live accounts, images, saves, rewards or network are accessed.

Checks explicit lesson/material discoveries, real quiz aggregate validation,
legacy unknown dates, true turn-in versus bridge success, latest-result stars and
photo association, all accessories/empty slots, schema acknowledgment, size limits,
same-request retries and obsolete account callbacks.

Quiz review checks additionally cover canonical indices despite shuffled answer
insertion/display order, same-save rollback, latest-attempt binding, compact
versioned publication, old-save reconstruction only from an exact unique retained
context/definition, invalid/ambiguous review fallback, separate size limits and
the additive backend acknowledgment. Unsupported review choice counts do not
invalidate otherwise valid assessment submissions or their aggregate scores.

Native Unity compilation and an installed-device/website round trip are separate
acceptance steps, not established by this managed harness.
