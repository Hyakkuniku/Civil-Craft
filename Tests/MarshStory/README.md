# Marsh story regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/MarshStory/MarshStory.csproj -- --source-only
```

Source-only checks validate the Arch lesson and five-question assessment (answers **B, C, A, D, B**), preserved Marsh identity/budget/load/span/rewards, unchanged shared materials, safe Editor migration boundaries, and the existing contract/ordered-quiz continuation guards. The complete production legacy Marsh continuation helper is compiled with Roslyn against isolated boundary doubles and its coroutine is executed for old completed phases, incomplete contracts, missing save data, visitors, invalid/duplicate review identity, pause/dialogue/panel/build/transition/travel waiting, normal progression winning the race, and exactly-once continuation. They do not claim that a loaded or saved scene has been updated.

Optional real-Unity-reference compilation (without starting/importing Unity):

```powershell
dotnet build Tests/MarshStory/EditorCompile.csproj --nologo
```

This compiles only the two new Marsh scripts against the installed Unity 2022.3 managed DLLs and the existing `Library/ScriptAssemblies` references. Its output and intermediate files stay under this test directory; it never overwrites production assemblies. The SDK/runtime boundary tests are not substitutes for Unity's subsequent native script import.

After applying the explicit Marsh scene migration and manually saving CanyonCrossing, run:

```powershell
dotnet run --project Tests/MarshStory/MarshStory.csproj
```

This additionally checks the saved Marsh component and four released phase IDs/indexes, appended review/resolved phases, exact existing contract/build-site/cinematic references, dialogue and material/lesson order, post-collection assessment, the existing Almanac lesson archive, and Ford's existing visibility condition.

The later user-authorized material replacement changes only Marsh's third allowance and material popup from Wood Pier to Concrete Pier, plus that popup's preceding highlighted material name. Road/Beam allowances, unlimited piece counts, hidden-material list, budgets, rewards, Arch lesson/assessment and phase identities remain unchanged. The shared Wood Pier asset keeps its original name and properties; no other contract opts in Concrete Pier.

This is a read-only static/Roslyn and serialized-asset runner. It does not import/open/save Unity scenes, execute live NPC/reward callbacks, touch player data/accounts, access the website, or use the network. It complements the existing production-flow suites in `Tests/NPCOrderedLearningFlow`, `Tests/NPCQuizIntegration`, `Tests/NPCQuizSession`, `Tests/NPCQuizReminder`, and `Tests/AlmanacQuizAccess`. It does not substitute for native Play Mode, visual camera/layout checks, phone/tablet/Android testing, or durable end-to-end save verification.
