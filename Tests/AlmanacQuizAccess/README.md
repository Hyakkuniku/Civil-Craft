# Almanac quiz handoff checks

Run `dotnet run --project Tests/AlmanacQuizAccess/AlmanacQuizAccess.csproj` from the Unity project folder.

Compiles the production offer/start/cancellation methods against isolated UI/save/Unity doubles. Checks delayed book-close handoff, double taps, original callback ownership, copied context, stale cancellation, retry after unavailable quiz UI, blocked close, account replacement, visitor protection, and disable/destroy behavior. No native globals, live player save, scene, account, or network is touched. Native tab geometry/rendering checks remain in `QuizUIAuthoring`.
