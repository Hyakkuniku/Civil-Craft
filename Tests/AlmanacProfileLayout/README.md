# Almanac profile layout checks

Run `dotnet run --project Tests/AlmanacProfileLayout/AlmanacProfileLayout.csproj` from the Unity project.

Compiles the actual runtime layout helper against isolated hierarchy/TMP doubles. Checks stacked currency rows, preserved `Unavailable` and large balance strings, bounded text sizing, non-overlapping rectangles, long identities, unrelated UI isolation, null/partial authored hierarchies, and repeat application.

These checks do not approximate font width or replace the native Unity phone/tablet rendering check. No live scene, player save, wallet or account is accessed.
