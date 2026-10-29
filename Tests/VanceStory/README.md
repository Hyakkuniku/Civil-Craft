Run from the Unity project root:

```powershell
dotnet run --project Tests/VanceStory/VanceStory.csproj -- --source-only
dotnet run --project Tests/VanceStory/VanceStory.csproj
```

`--source-only` checks assets and authoring while allowing the original placeholder span before Unity applies the update. The default additionally requires the saved 80 m contract and the authored learning flow in CanyonCrossing. Both commands are read-only and use ignored `bin`/`obj` outputs. They do not touch a live scene, player save, account, reward, cargo object, purchase, or network.

The tests compile the real production `QuizData`, validate the five serialized questions and answer key **B/D/A/C/B**, and verify permanent lesson/quiz IDs. They protect all non-narrative contract settings except the explicit 3 m placeholder to measured 80 m correction. The 5,000 kg empty-vehicle weight, 150,000 budget, four-cargo gate, rewards and tool/material permissions must remain unchanged.

The saved-scene checks preserve all seven phase IDs, the complete realignment side phase, the original loaded-contract phase, cargo-phase callbacks and loading tutorial, four cargo items, four vehicle slots, truck, camera tour and all other NPC progression components. They require the lesson to finish before cargo is introduced, keep cargo unlocking at completed phase3, and defer the existing Silas/side-contract callbacks until after the lesson review, submitted quiz and final handoff dialogue.

`Baseline.json` was captured from the saved project before Vance authoring. It protects the full side-contract asset and eight existing shared runtime sources. Never regenerate the baseline from an updated scene to conceal a regression. These checks supplement Unity import validation and an end-to-end playthrough; they do not claim to run the real vehicle physics or UI raycasts.

Authorized follow-up on 2026-10-28: the user requested Silas's Load Distribution and Load Transfer main-story update, including lessons, quizzes, simulation lessons and the preserved Multiplayer unlock. After the new Tests/SilasStory saved-scene checks passed, only Silas component 318150247's protected expectation was advanced from AEF24064238E67C56B3D7C73D00D4A16E6A1526CD72788E8D101E4660F74D383 to AF87F69C5C89D8693FDDD9107AD260066EC5911E6A5B8EE2DA423756B699DB59. The original hash is retained here; every other Vance baseline entry remains unchanged. Tests/SilasStory now verifies the approved Silas behavior in detail.

Authorized follow-up on 2026-10-28: the user requested Objectives and NPC exclamation guidance from Reed through Silas. Only the NPCProgressionManager source expectation advanced from 4C570318F1504FD9E1FCF7AB3C36D2BEC7A10A4FD32958835F5F4F1294150615 to 021F78EC3C39B168D7836EA1FB810E712448D801DD219AB79C31EEA433C0380A. The original hash is retained here; all other source and behavior baselines remain unchanged. Tests/NPCExclamation verifies the scoped interrupted-cinematic attention retry policy (5,640 assertions and 45 guards). These older story checks normalize only showExclamationMark/onlyShowExclamationWhenInteractionNeeded on the explicit Vance and Silas components, and showInObjectives/objectiveTitle/objectiveId on the four named existing guide sequences; every callback, step, identity, target, contract setting and other component stays protected by its original hash. Tests/ReedToSilasGuidance checks the actual new presentation settings.
