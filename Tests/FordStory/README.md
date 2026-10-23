# Ford story regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/FordStory/FordStory.csproj -- --source-only
```

The source-only mode validates Ford's authored assets and safe Editor migration boundaries without claiming a loaded or saved scene has changed. The Material Stress assessment uses five multiple-choice questions with answers **C, B, D, A, C**, accepts any submitted score, and adds no extra quiz rewards.

While preparing the Editor authoring tool, `--helper-only` runs the assets and complete production legacy-helper scenarios independently; it explicitly skips all Editor-authoring and saved-scene checks and must not be reported as a full migration pass.

After applying the explicit Ford migration and manually saving CanyonCrossing:

```powershell
dotnet run --project Tests/FordStory/FordStory.csproj
```

Strict mode additionally checks the serialized Ford progression, existing contract/build/camera references, post-collection lesson/assessment, and quest visibility rules, including preservation of Ford's prior Marsh-completion gate.

The complete production Ford legacy continuation is compiled with Roslyn against isolated boundary doubles and its coroutine is exercised for completed/unfinished saves, missing save data, visitors, invalid/duplicate review identities, exactly-once completion, normal progression winning the race, and waiting for Pause/dialogue/panels/transitions/build/simulation/travel. No real Unity or player callbacks are invoked.

Optional compile of the new Ford authoring, detached camera validator and continuation scripts against installed Unity 2022.3/project assemblies:

```powershell
dotnet build Tests/FordStory/EditorCompile.csproj --nologo
```

Outputs stay under this test directory. Existing production assemblies are never overwritten. Recompiling the helper after Unity already imported it may produce standalone source-vs-imported duplicate-type warnings; native Unity compilation is a separate check.

This runner is read-only: it does not open/import/save Unity scenes, execute live rewards, access player accounts/saves, contact the website, or use the network. Static asset checks and isolated boundary tests do not replace native Edit/Play Mode, phone/tablet/Android rendering, animation, or durable end-to-end save verification.
