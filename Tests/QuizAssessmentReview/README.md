# Submitted assessment answer review

Run `dotnet run --project Tests/QuizAssessmentReview/QuizAssessmentReview.csproj` from the Unity project root.

The harness directly compiles production `QuizView`, `QuizResultCard`, `QuizChoiceView`, `QuizData`, and `QuizProgressUtility`. It checks actual submitted-choice rendering, authored ordering and variable counts, green/red states, correction visibility, image/caption preservation, null/legacy selections, plain text, explanation copy, card sizing, cleanup, result-only controls, and unchanged practice flow. Measurement includes TMP margins and verifies font-relative gutters contain the authored Bekind capital-glyph overshoot. Source guards check the manager's post-save submission/resume handoff and the real layout's scrollable one-column path.

Unity hierarchy/input/instantiation and TMP measurement are small boundary doubles; actual font rendering, camera composition, touch scrolling and scene input remain native Unity/device checks. The suite never accesses live accounts, saves, scenes, rewards, or network.
