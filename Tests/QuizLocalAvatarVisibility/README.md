# Quiz local-avatar visibility regressions

Run `dotnet run --project Tests/QuizLocalAvatarVisibility/QuizLocalAvatarVisibility.csproj` from the Unity project.

This compiles the complete production `QuizLocalAvatarVisibility` helper against isolated Unity object/render-event boundary doubles. Its 75 assertions cover local body/outfit discovery, inactive wardrobe variants, current/prior force-rendering flags, untouched cargo (including an inactive cargo subtree activated later)/NPC/remote/nonmesh renderers, owned versus map/portrait cameras, nested renders, pipeline callback gating, missed render completion and end-frame recovery, teardown, destroyed renderers/models, invalid replacement/empty setup, and render-hook cleanup.

No scene, save, account, or network is accessed. Real Unity/URP rendering, foreground-avatar visual comparisons, and phone layouts still require native checks.
