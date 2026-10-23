# Bhan qualified inspection → Almanac reminder regression checks

From the Unity project root:

```powershell
dotnet run --project Tests/BhanLiveLoadReview/BhanLiveLoadReview.csproj
```

The complete production `LessonTrigger` and `NPCOrderedLearningFlow` sources are linked directly. `Boundary.cs` explicitly doubles Unity lifecycle/coroutine stepping, lesson/book UI events, the NPC phase move boundary and in-memory save snapshots. It does not implement either production progression decision. No real account, scene or player save is opened or modified.

Cases distinguish archive unlock, armed instruction, open lesson, owned matching closed inspection, pending review, genuine book open/completed close and final advance. They exercise repeated inspections/resumes, mismatched lesson events, profile switches, protected visitors, proof-save rollback, null old lists, in-memory save reload, disable/re-enable, phase/travel changes, missing book, defensive/custom narrative reminders and non-review legacy behavior.

Persistence uses the existing `completedLessons` list with `StoryInspection/Bhan_LiveLoad_Inspection`, leaving the old `StoryReview/MainContractNPC/LiveLoad/inspection-review` ID and lesson-close completion key intact. The harness snapshots are serialized only in memory; production disk/cloud encryption and visual pointer/dialogue rendering require separate Unity validation.

Legacy review opt-ins with no completion save key retain runtime-only qualified review behavior. They create no durable `StoryInspection/` marker and make no new reload-resumption promise; the owned session can finish normally, while disable/profile changes require a fresh inspection. Keyed marker save failures still roll back strictly and do not start the review.
