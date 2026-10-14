# NPC phase exclamation checks

Run from this Unity project root:

```powershell
dotnet run --project Tests/NPCExclamation/NPCExclamation.csproj
```

The suite compiles the actual marker eligibility and destination-resolution methods
from `NPCProgressionManager.cs` with minimal engine boundary doubles. It verifies
per-phase opt-in, actual arrival, active/loaded scene state, guest restrictions,
horizontal/vertical arrival tolerance, authored/self/targetless destinations,
and marker restoration after travel/disable/re-enable. Syntax guards verify travel
and cargo transitions invalidate prior arrival, and map/overhead indicators remain
noninteractive. It never opens Unity, changes scenes, or accesses saves/accounts.

Rendering size/contrast, occlusion, map clipping, and real navigation should still
be checked in Unity; the standalone checks do not claim native rendering coverage.
