Run from the Unity project root:

```powershell
dotnet run --project Tests/ReedStory/ReedStory.csproj -- --source-only
dotnet run --project Tests/ReedStory/ReedStory.csproj
```

`--source-only` checks authored assets and source without claiming the scene has been applied or saved. The default additionally checks the saved CanyonCrossing scene. Neither command opens Unity scenes, writes player saves, grants rewards, or contacts a network service. Build outputs stay in this folder's ignored `bin` and `obj` directories.

The checks compile the real production `QuizData`, populate it from BendingQuiz, validate all five questions and their answer indices, and verify its save fingerprint. They verify the confirmed 60 m main crossing in both the contract and briefing while preserving the original balance (100,000 budget, 5,000 kg, 500 coins and 100 XP), compare the complete side-contract asset and shared Reed prefab against pre-update SHA256 hashes, and guard the original phase order, ownership, cancellation, cinematic/lesson ordering, and Vance/side-contract continuation.

The saved-scene checks target Reed's original prefab instance `1110249782773445749`, preserve its meaningful side-phase overrides and four other Reed-prefab instances, require one correctly bound introduction helper, and verify actual lesson/quiz references and Almanac registration. Unity may discard pre-existing out-of-range sentence overrides on save; those unused values are not treated as changes to the zero-length side-phase dialogue.

`Baseline.json` was captured before Reed story authoring. Do not regenerate it from an updated scene to conceal a regression. These tests do not substitute for Unity import/compilation or an end-to-end playthrough.
