# Almanac CLOSE button styling

Run `dotnet run --project Tests/AlmanacCloseButton/AlmanacCloseButton.csproj` from the Unity project.

The test links the real production style helper. Its hierarchy/UI/TMP/event doubles check direct prefab controls and the small authored oval wrapper, unchanged Button/RectTransform identity, persistent and runtime callback preservation, ordinary one-tap close, full rectangular hit area, SKIP palette/type/shadow, idempotent child creation, and safe rejection of unrelated parent layouts. It never invokes a live Almanac or writes scenes/player saves.

Source guards compare the existing close/reader/tutorial-gate methods to the read-only Git baseline and require scoped integration by the original persistent CloseAlmanac target. Native font rendering, mobile safe bounds and GraphicRaycaster checks are handled separately in Unity.
