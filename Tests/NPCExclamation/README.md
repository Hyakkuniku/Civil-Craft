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

It also compiles the actual shared HUD reservation intersection/visibility methods.
Current physical screen rectangles are an engine boundary double; full-badge edge
and corner intersection, two-pixel padding, moving maps, registration/teardown,
hidden maps, ancestor Canvas opacity, and disabled Canvases are tested directly.
The overhead call-order guards require suppressing an intersecting world badge
without moving it, changing map render order, or hiding the map's own markers.

Presence tests compile the actual model-presence and spawn-condition-readiness
methods. They cover startup waiting for rules/manual spawn, despawn/respawn,
unrelated and nested controlled objects, disabled debug gates, renderer-hidden
models, labels/particles that are not NPC bodies, and off-camera NPCs that must
remain visible on the map. Component discovery and `activeInHierarchy` are engine
boundary doubles; no real spawning, camera rendering, or player progress runs.

Rendering size/contrast, occlusion, map clipping, and real navigation should still
be checked in Unity; the standalone checks do not claim native rendering coverage.

The opt-in interaction-needed policy is compiled unchanged as well. Checks cover
offer/accepted-build/turn-in state, completed conversations, cancelled and waiting
quizzes, review ownership, dialogue, pause, build/simulation/transition states,
unavailable givers, and preserving the previous behavior when the option is off.
Giver availability, saved dialogue completion, and lesson-review ownership are
boundary doubles; these tests never initiate dialogue, quests, or real saves.

Completed repeatable camera introductions are checked separately: an interrupted
camera tour or Reed introduction restores talk attention while the phase remains
unchanged. Running, disabled, foreign-scene, or unrelated callback owners cannot
request a retry. Ordinary completed repeat reminders remain hidden. Persistent
callback metadata and continuation activity are boundary inputs; the real query
is compiled from production and never invokes a camera, dialogue, or lesson.
