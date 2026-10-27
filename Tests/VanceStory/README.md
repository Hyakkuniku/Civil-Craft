Run from the Unity project root:

```powershell
dotnet run --project Tests/VanceStory/VanceStory.csproj -- --source-only
dotnet run --project Tests/VanceStory/VanceStory.csproj
```

`--source-only` checks assets and authoring while allowing the original placeholder span before Unity applies the update. The default additionally requires the saved 80 m contract and the authored learning flow in CanyonCrossing. Both commands are read-only and use ignored `bin`/`obj` outputs. They do not touch a live scene, player save, account, reward, cargo object, purchase, or network.

The tests compile the real production `QuizData`, validate the five serialized questions and answer key **B/D/A/C/B**, and verify permanent lesson/quiz IDs. They protect all non-narrative contract settings except the explicit 3 m placeholder to measured 80 m correction. The 5,000 kg empty-vehicle weight, 150,000 budget, four-cargo gate, rewards and tool/material permissions must remain unchanged.

The saved-scene checks preserve all seven phase IDs, the complete realignment side phase, the original loaded-contract phase, cargo-phase callbacks and loading tutorial, four cargo items, four vehicle slots, truck, camera tour and all other NPC progression components. They require the lesson to finish before cargo is introduced, keep cargo unlocking at completed phase3, and defer the existing Silas/side-contract callbacks until after the lesson review, submitted quiz and final handoff dialogue.

`Baseline.json` was captured from the saved project before Vance authoring. It protects the full side-contract asset and eight existing shared runtime sources. Never regenerate the baseline from an updated scene to conceal a regression. These checks supplement Unity import validation and an end-to-end playthrough; they do not claim to run the real vehicle physics or UI raycasts.
