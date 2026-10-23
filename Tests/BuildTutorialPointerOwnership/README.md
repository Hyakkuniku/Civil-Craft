# Build tutorial pointer ownership

```powershell
dotnet run --project Tests/BuildTutorialPointerOwnership/BuildTutorialPointerOwnership.csproj
```

Runs Roslyn-extracted production director pointer/ownership/material/tool/observed-tool methods, full `TutorialManager.Update` and immediate `RestorePresentationAfterInspection` against explicit engine/UI/pointer boundaries. Covers same-target offset/rotation replacement, explicit-vs-authored arbitration, Undo priority during tracing, per-step/sequence scope, suppression, inactive targets, disable, normal non-build placement, exact-control fallbacks and unchanged tool/material gates. Restoration is checked before any subsequent Update, including Undo, an explicit prompt whose authored pointer flag is false, nested inspection suppression, stale snapshots and unchanged non-build placement.

The suite also executes the actual required-Undo warning lifecycle (`PromptUndoInvalidBar`, `NotifyUndoCompleted`, cleanup and late refresh) and owner-bound instruction visual gate. It checks exclusive mismatch priority, hidden Continue/Skip, unchanged objectives/Undo restrictions, restoration only after invalid action objects disappear, layout-signature caching, modal/inspection suppression, deferred same-step restoration, stale-owner safety, fade completion, disabled controls, and disable/re-enable/destroy cleanup. Continue/Skip progression guards and clearing the visual gate before a new presentation are source-checked; the boundary completion callback records calls rather than advancing a live tutorial.

The pointer and warning-layout boundaries record calls; they do not duplicate or validate edge placement, safe-area math, native TMP measurements, UGUI modal rendering or actual device clicks. Those need the separate production geometry/native checks. Source guards retain cleanup call paths and prevent the Undo coroutine bypassing manager suppression. No scene, player data or live Unity state is accessed or changed.
