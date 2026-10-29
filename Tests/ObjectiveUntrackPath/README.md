Run from the Unity project root:

```powershell
dotnet run --project Tests/ObjectiveUntrackPath/ObjectiveUntrackPath.csproj
```

The suite extracts the production PathGuider update, untrack/resume/recall/route methods and saved-preference properties. It checks that untracking preserves the exact waypoint list and reached index, arrival can still advance a real tutorial, automatic replacement and navigation refresh do not restore hidden markers, valid explicit tracking resumes them, and disabled/stale/guest services cannot write the preference. It also executes the production contract-hint hide method and checks the restore/binding gates.

Rendering, scene identity, player saves and navigation are boundary doubles. The test never opens Unity, reads live player profiles, modifies scenes or grants rewards.
