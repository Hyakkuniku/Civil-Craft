# Stage-aware NPC Almanac reminder checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/NPCAlmanacReviewReminder/NPCAlmanacReviewReminder.csproj
```

Roslyn extracts and executes the actual production NPC interaction handler, lesson-review reminder, speaker selection, late ownership check, cancellation and local-progression gate. The production ownership-safe `DialogueManager.CancelDialogueIfOwned` also runs. No copied reminder algorithm substitutes for those methods.

Coverage includes the pre-inspection original dialogue, pending multi-sentence reminder, animation/facing references, suppression before phase events, repeated talks and double taps, modal/Pause/book/quiz/transition gates, missing services, synchronous resume invalidation, profile/visitor/phase/trigger changes, teardown, stale callbacks and protection of unrelated conversations. Source guards verify that disable routes to the tested cancellation and the reminder itself cannot open the book, save, advance or reward.

`LessonTrigger` pending ownership, content and resume are **explicit boundary doubles**; their persistence/registration/qualification logic belongs to the separate trigger suite. Likewise normal dialogue/input, quiz and book services are isolated boundaries. This is not an end-to-end Unity story, animation, device-input or disk-save test. No Unity session, scene, player save, account or network is touched.
