# NPC quiz session integration regressions

Run `dotnet run --project Tests/NPCQuizSession/NPCQuizSession.csproj` from the Unity project.

The harness extracts and compiles the current production quiz controller session lifecycle, interaction, progress commit, control locking/restoration, owner cancellation, and actual Pause/Resume/quiz-teardown methods. It also runs real quiz data/helpers and production quiz save methods. Unity object lookup, input flags, modal stack, view rendering, and durable writer are isolated boundary doubles.

Coverage includes wrong/correct answers, dynamic question counts, Submit/Next/Finish sequencing, repeated taps, resume, exactly-once results, failed answers/completion and retries, safe unsaved exit, pause/settings nesting, stale owners/profiles/content, protected visits, unload/disable, saved control snapshots, and follow-up dialogue ownership.

NPC-camera presentation is a boundary double. Controller checks verify the original NPC owner reaches the presenter, the right-side quiz is enabled only after successful framing, question animation requests and layout viewport updates are forwarded, unavailable framing retains full-panel operation, nested Pause/Settings do not end the shot, and camera/animation teardown occurs before control restoration and follow-up dialogue. Recording-camera handoff must precede the quiz input lock; the camera controller's enabled state is modeled independently from its captured look-input flag. Owner/account/visitor changes, scene unload, disable/destroy, transitions, and repeated close paths release ownership once. Real framing, camera render state, animator parameters, and responsive visuals require the separate Unity checks.

No live Unity scene, persistent save, account, or network is accessed. Native layout/raycast/touch checks remain separate.
