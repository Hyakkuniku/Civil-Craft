Run from the Unity project root:

```powershell
dotnet run --project Tests/SilasStory/SilasStory.csproj -- --source-only
dotnet run --project Tests/SilasStory/SilasStory.csproj
```

The first command validates source and learning assets without claiming the scene update has been applied or saved. The second additionally validates the saved scene's two main-contract lesson/assessment/simulation flows and original Multiplayer handoff.

The captured baseline protects both contracts' main classification, current live loads/budgets/rewards and all settings except the intentional 30-to-100-metre span correction and dialogue. It also protects all eleven stable Silas phase IDs, callback/target settings, the Multiplayer unlock/Collect/icon settings, Rope balance, existing cinematics and phase actions, other NPCs, Vance cargo/truck/slot state, and existing runtime implementations.

Both quizzes are loaded into the real production QuizData validator, checking the reference answer keys and usable save fingerprints. Tests never open a live scene, write player data, grant rewards, or create/modify a multiplayer session. Build outputs stay in ignored bin/obj folders.

Baseline.json was captured before this Silas authoring update. Do not regenerate it from an updated scene to conceal an unrelated regression. These checks do not replace Unity import/compilation or a complete playthrough.
