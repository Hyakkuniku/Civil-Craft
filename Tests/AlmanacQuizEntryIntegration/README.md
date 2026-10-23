# NPC quiz Almanac entry API checks

Run `dotnet run --project Tests/AlmanacQuizEntryIntegration` from the Unity project.

This suite compiles the production `TrySelectQuizOfferCategory`, `IsOpenAndReady`, `IsOpeningOrClosing`, `IsAnyTabUnlockTutorialPlaying`, and `SelectCategory` members into an isolated test assembly. Unity graphics and the engine-heavy page-application method are boundary doubles; the entry gate itself is not copied or rewritten.

Checks cover arbitrary actual quiz category indices, normal selection routing, missing/hidden/disabled tabs, opening and page-turn waits, all mandatory book walkthroughs, retry after guards clear, and failure to apply a selection. Source guards forbid direct quiz starting, progression, save writes, or bypassing normal category selection.

For pending-offer lifecycle, owner/account invalidation, and Start Quiz handoff, use `Tests/AlmanacQuizAccess`. For NPC reminder callbacks and phase identity, use `Tests/NPCQuizReminder`.
