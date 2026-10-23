# Ordered NPC learning regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/NPCOrderedLearningFlow/NPCOrderedLearningFlow.csproj
```

The runner compiles the complete production `NPCOrderedLearningFlow.cs` with Roslyn against small Unity, tutorial, lesson-reader, Almanac, profile, and visitor boundary doubles. It does not open Unity, access real accounts/saves, change scenes, or use the network.

Coverage includes genuine Almanac open/finished-close gating, animation/event ordering, panel hiding, priority/queued tutorial ownership, repeated matching inspection idempotence, wrong identity/callback/profile rejection, visitor restrictions, exactly-once callbacks, cancellation/teardown and retry, saved review completion, missing configuration, default/custom prompt wording, and existing ordered lesson introductions.

The review-arrow checks verify the exact `hudOpenButton` RectTransform, placement settings, no click-to-advance shortcut, an arrow-free second step, and missing HUD/RectTransform fallback. Pending-review queries are exercised before/after completion/cancellation, with wrong IDs, profile replacement and visitors. These are reference/control-flow checks, not rendered arrow measurements.

The first Beam Bridge entry also exercises its actual stable review identity (`MainContractNPC/DeadLoad/training-bridge-record-review`), HUD pointer, genuine open/finished-close requirement and exactly-once completion, independently of the earlier Live Load inspection review.

Coroutines are advanced explicitly. This verifies production control flow; it does not replace native scene/UI or Android testing. The tutorial-manager double only records in-memory completion and models ownership. It does not claim coverage of Unity rendering, animation, global tutorial queue implementation, or disk persistence.
