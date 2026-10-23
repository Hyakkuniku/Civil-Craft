# NPC quiz regression checks

Run `dotnet run --project Tests/NPCQuiz/NPCQuiz.csproj` from the Unity project.

These checks compile the real `QuizData`, `QuizProgress`, `QuizProgressSaveData` and Editor ID/fingerprint authoring logic, plus the three production `PlayerDataManager` quiz methods extracted with Roslyn. Unity assets and the normal save commit are boundary doubles only. No Unity session, on-device player save, account, or network is touched.

Coverage: variable-length authored questions/choices, text/image/caption validation, stable IDs, deterministic content changes (including sprite dependency edits), first-unresolved resume, exit/restart, completion gating, profile/context isolation, unchanged rewards/lessons, old/null saves, and rollback on failed commits/protected visits.

Question-order coverage executes the production seeded Fisher–Yates helper and ID-to-display mapping at varied counts without mutating the source asset/fingerprint. It verifies deep-cloned order lists, valid saved-order reuse, legacy answered/submitted/completed numbering, order persistence before any answer, zero-answer restart, both assessment/practice save modes, immutable established orders, malformed-order rejection, initial/answer commit failures, score/answer ID associations, corrupt stored-order filtering, content edits and account/visitor separation. A fresh attempt may randomly produce the same permutation as another attempt; completed results are not reset into a retake by these APIs.

Choice-order coverage additionally checks per-question source-index permutations, varied/mixed choice counts and text/image/caption choices, inverse display/source lookup, correct-answer matching, unchanged asset choice lists/fingerprint, deeply cloned nested metadata, identity choices for old attempts, zero-answer restart, source-index scoring, metadata-row order independence, conflicting/malformed mapping rejection, old/null fields, corrupt-order reads without answer remapping, failed commits and content/account/visitor boundaries. Fresh questions and choices shuffle only in presentation metadata; saved answers always identify the original source choice index.

Actual Unity rendering, panel coordination, input restoration, NPC chaining, Android touch/Back, and native `JsonUtility` import remain separate integration checks.
