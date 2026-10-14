# NPC exclamation map checks

Run `dotnet run --project Tests/NPCExclamationMap/NPCExclamationMap.csproj` from the Unity project.

The suite extracts and compiles the real projection and physical-pixel sizing methods from `ExpandedMinimapController`; the Unity vector/rectangle arithmetic boundary is doubled. It covers compact/expanded marker sizing, RawImage crop and flip, current-camera viewport coordinates, offscreen/behind-camera rejection, invalid values, parent/canvas scales, and source guards for phase/arrival gating, throttled discovery, animation refresh, clipping, non-blocking graphics, scene filtering, and owned teardown.

HUD-reservation guards verify the current minimap panel is registered/released and
the overhead badge's exclusion check is never applied to markers inside either map.
Shared-policy guards require model/spawn readiness while keeping gameplay-camera
culling independent from NPC presence on the map.

It does not run Unity rendering, camera transforms, actual map gestures, player progress, accounts, or network calls. Native rendering/input and NPC arrival integration require the separate Unity check.
