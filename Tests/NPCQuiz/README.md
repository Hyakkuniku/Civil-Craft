# NPC quiz regression checks

Run `dotnet run --project Tests/NPCQuiz/NPCQuiz.csproj` from the Unity project.

These checks compile the real `QuizData`, `QuizProgress`, and Editor ID/fingerprint authoring logic, plus the two production `PlayerDataManager` quiz methods extracted with Roslyn. Unity assets and the normal save commit are boundary doubles only. No Unity session, on-device player save, account, or network is touched.

Coverage: variable-length authored questions/choices, text/image/caption validation, stable IDs, deterministic content changes (including sprite dependency edits), first-unresolved resume, exit/restart, completion gating, profile/context isolation, unchanged rewards/lessons, old/null saves, and rollback on failed commits/protected visits.

Actual Unity rendering, panel coordination, input restoration, NPC chaining, Android touch/Back, and native `JsonUtility` import remain separate integration checks.
