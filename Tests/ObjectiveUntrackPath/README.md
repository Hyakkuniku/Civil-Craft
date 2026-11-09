Run from the Unity project root:

```powershell
dotnet run --project Tests/ObjectiveUntrackPath/ObjectiveUntrackPath.csproj
```

The suite extracts the production PathGuider update, untrack/resume/recall/route methods and saved-preference properties. It checks that untracking preserves the exact waypoint list and reached index, arrival can still advance a real tutorial, automatic replacement and navigation refresh do not restore hidden markers, valid explicit tracking resumes them, and disabled/stale/guest services cannot write the preference. It also verifies that the Active Contract can be untracked while the Objectives panel suspends its route. The contract reminder itself is covered by `Tests/ContractGuideRestore`.

Rendering, scene identity, player saves and navigation are boundary doubles. The test never opens Unity, reads live player profiles, modifies scenes or grants rewards.
