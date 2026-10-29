Run from the Unity project root:

```powershell
dotnet run --project Tests/SilasStory/SilasStory.csproj -- --source-only
dotnet run --project Tests/SilasStory/SilasStory.csproj
```

The first command validates source and learning assets without claiming the scene update has been applied or saved. The second additionally validates the saved scene's two main-contract lesson/assessment/simulation flows and original Multiplayer handoff.

The captured baseline protects both contracts' main classification, current live loads/budgets/rewards and all settings except the intentional 30-to-100-metre span correction and dialogue. It also protects all eleven stable Silas phase IDs, callback/target settings, the Multiplayer unlock/Collect/icon settings, Rope balance, existing cinematics and phase actions, other NPCs, Vance cargo/truck/slot state, and existing runtime implementations.

Both quizzes are loaded into the real production QuizData validator, checking the reference answer keys and usable save fingerprints. Tests never open a live scene, write player data, grant rewards, or create/modify a multiplayer session. Build outputs stay in ignored bin/obj folders.

Baseline.json was captured before this Silas authoring update. Do not regenerate it from an updated scene to conceal an unrelated regression. These checks do not replace Unity import/compilation or a complete playthrough.

Authorized follow-up on 2026-10-28: the user requested Objectives and NPC exclamation guidance from Reed through Silas. Only the NPCProgressionManager source expectation advanced from 4C570318F1504FD9E1FCF7AB3C36D2BEC7A10A4FD32958835F5F4F1294150615 to 021F78EC3C39B168D7836EA1FB810E712448D801DD219AB79C31EEA433C0380A. The original hash is retained here; all other source and behavior baselines remain unchanged. Tests/NPCExclamation verifies the scoped interrupted-cinematic attention retry policy (5,640 assertions and 45 guards). These older story checks normalize only showExclamationMark/onlyShowExclamationWhenInteractionNeeded on the explicit Vance and Silas components, and showInObjectives/objectiveTitle/objectiveId on the four named existing guide sequences; every callback, step, identity, target, contract setting and other component stays protected by its original hash. Tests/ReedToSilasGuidance checks the actual new presentation settings.
