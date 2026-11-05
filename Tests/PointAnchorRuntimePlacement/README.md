# Permanent anchor placement

Run `dotnet run --project Tests/PointAnchorRuntimePlacement/PointAnchorRuntimePlacement.csproj`.

This harness extracts the actual `Point.Update`, `IsScenePlacedAnchor` and `IsSessionAnchor` members from production source and executes them with small Unity boundary doubles. It checks fractional permanent anchor heights and X/Z, repeated transform-change notifications, both multiplayer session endpoint lists, width refreshes, Editor authoring helpers and unchanged ordinary-node grid placement.

The boundary does not simulate Unity's object lifecycle, render a road, or drive a vehicle. Native spawned-road and Play Mode checks remain separate. No scenes, player saves or production scripts are modified.
