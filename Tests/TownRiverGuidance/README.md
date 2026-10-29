Run from the Unity project root:

```powershell
dotnet run --project Tests/TownRiverGuidance/TownRiverGuidance.csproj -- --source-only
dotnet run --project Tests/TownRiverGuidance/TownRiverGuidance.csproj
```

The first command checks source and frozen assets without claiming the scene was applied. The default also checks the saved Marsh, Ford, Brooks and Ronan objectives, attention policy, three existing follow tutorials and catalog merge.

The baseline was captured before this task. It preserves all existing MonoBehaviour/camera documents outside the explicitly changed NPC attention and tutorial objective fields, all prior Ronan prefab overrides except attention settings, the two prior added components, NPC component membership, and 31 contract/lesson/quiz/simulation/prefab assets. No dialogue, phase ID, callback, material, budget, vehicle load, payout or finale flag may be changed. The three real follow guides still use their original arrival-completion route; the passive site conversations are separate objectives. Resolved phases never gain another objective or attention mark.

Checks read project files only. They never start Unity, invoke story callbacks, open player profiles, save scenes, grant rewards or contact the network. Runtime history/profile/navigation behavior is covered separately by `Tests/NPCStoryObjectives`, `Tests/NPCExclamation` and `Tests/ObjectiveUntrackPath`.

Unity may omit Ronan's false Resolved-marker override: his frozen source prefab has an empty phase array, and `NPCProgressionPhase.showExclamationMark` has the ordinary false C# default. The checker accepts that exact omission only after verifying both facts; all five true flags still require explicit overrides.
