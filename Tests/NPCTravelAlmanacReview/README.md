# Walking-time Almanac review checks

```powershell
dotnet run --project Tests/NPCTravelAlmanacReview/NPCTravelAlmanacReview.csproj
```

The full production `NPCOrderedLearningFlow` is compiled together with Roslyn-extracted production travel-stage, ownership, cancellation, `ActivatePhase` and deferred-arrival methods. Engine/coroutine scheduling, movement, neighboring normal ordered-step continuation and UI/save services are explicit boundaries. The continuation boundary requests the same review ID, so it cannot hide an erroneous second-read requirement.

Cases cover reading before arrival, arriving while the book is open, arriving before reading, one shared HUD pointer/session, true open-close gating, modal/paused/transition waits, profile/visitor/phase/step changes, cancellation/failure, already-completed IDs, opt-in guards, duplicate starts and deferred follow-up. Source guards check actual movement/disable/failure/invalidation/interaction call paths. No actual Unity scenes, player saves, accounts or network are touched. Native pointer rendering, real NPC path movement and input restoration still require Unity validation.
