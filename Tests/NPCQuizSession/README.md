# NPC quiz session integration regressions

Run `dotnet run --project Tests/NPCQuizSession/NPCQuizSession.csproj` from the Unity project.

The harness extracts and compiles the current production quiz controller session lifecycle, interaction, progress commit, control locking/restoration, owner cancellation, and actual Pause/Resume/quiz-teardown methods. It also runs real quiz data/helpers and production quiz save methods. Unity object lookup, input flags, modal stack, view rendering, and durable writer are isolated boundary doubles.

Coverage includes wrong/correct answers, dynamic question counts, Submit/Next/Finish sequencing, repeated taps, resume, exactly-once results, failed answers/completion and retries, safe unsaved exit, pause/settings nesting, stale owners/profiles/content, protected visits, unload/disable, saved control snapshots, and follow-up dialogue ownership.

No live Unity scene, persistent save, account, or network is accessed. Native layout/raycast/touch checks remain separate.
