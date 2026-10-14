# NPC quiz integration checks

Run `dotnet run --project Tests/NPCQuizIntegration/NPCQuizIntegration.csproj` from the Unity project.

This compiles extracted production ordered-sequence detection and quiz validation against boundary doubles, plus actual `NPCOptionalDialogueStep` and `QuizData`. It checks enum serialization, quiz-only and invalid quiz ownership, stable context IDs, duplicates, and legacy detection. Structural Roslyn/source assertions cover NPC callback guards, cancellation/retry, ordered-only prompts, and accepted-contract state restored after restart.

No live Unity scene, player save, account, or network is accessed. These tests do not substitute for native panel/input/rendering and Android touch/Back checks.
