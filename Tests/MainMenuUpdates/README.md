# Main-menu Updates checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/MainMenuUpdates/MainMenuUpdates.csproj --verbosity quiet
```

The harness links the actual production release policy, response parser, and
service and panel controller. Unity lifecycle, UI coordinator, TextMeshPro and
HTTP boundaries are doubles; no Unity session,
physical/synthetic input, real network requests, account operations, or player
saves are used. Cache fixtures are confined to a test-owned temporary directory.

These deterministic checks do not replace a real Unity rendering/input check
or Android package-version check. Source/scene shipping assertions verify saved
wiring and safety constraints, not runtime appearance.

Before the new main-menu objects have been authored/saved, pass
`-- --runtime-only` to run the actual policy/service/controller checks without the
saved-scene assertions. The default command remains the shipping gate.

Coverage includes strict JSON framing and field types, Android numeric code
bounds/comparison, announcements versus installable builds, independent public
cache validation/read/write failures, first launch and cached offline states,
unconfigured/failed/malformed responses, bounded downloads, one in-flight fetch,
five-minute stale checks and explicit retry, scene exit interruption, manual-only
opening/download, literal remote notes, badge persistence when reading notes,
Back handling, callback cleanup and authenticated-data isolation.
