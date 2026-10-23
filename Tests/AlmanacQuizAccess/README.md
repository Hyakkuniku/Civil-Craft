# Almanac quiz handoff checks

Run `dotnet run --project Tests/AlmanacQuizAccess/AlmanacQuizAccess.csproj` from the Unity project folder.

Compiles the production offer, queued Quiz-tab entry, waiting-offer query, start and cancellation methods against isolated UI/save/Unity doubles. Both assessment and retry-until-correct practice quizzes use the same entry without automatically starting the quiz. Checks delayed dialogue/book-animation handoff, an already-open book, mandatory walkthroughs, nested/owned coordinator panels, Pause, transitions, profile/visitor changes, generation identity, one-shot opening, explicit reminder reopening, and respecting the player's close before selection. Unavailable quiz UI returns to the Quiz tab without advancing the NPC or repeatedly capturing input.

Existing close-before-start, double-tap, callback ownership, copied context, cancellation, failed-open retry, blocked close, account replacement and disable/destroy checks remain. The Almanac manager's animation/category guard is a boundary here and has separate linked/native checks; this suite does not render a book or claim device input verification. No native globals, live player save, scene, account, or network is touched. Native tab geometry/rendering checks remain in `QuizUIAuthoring`.
