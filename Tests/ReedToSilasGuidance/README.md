Run from the Unity project root:

```powershell
dotnet run --project Tests/ReedToSilasGuidance/ReedToSilasGuidance.csproj -- --source-only
dotnet run --project Tests/ReedToSilasGuidance/ReedToSilasGuidance.csproj
```

The source-only check does not claim that the scene has been applied or saved. The default also checks the saved CanyonCrossing definitions, NPC attention flags, four existing tutorial checkboxes and catalog merge.

The baseline was captured before this authoring task. It protects all six main/side contract assets, existing story content and callbacks outside attention settings, existing tutorial steps and lesson IDs, other scene components, and the prior objective catalog. The sixteen new checked TutorialSequences are passive objective definitions: they neither play automatically nor advance tutorials. Quiz entries have no world route; the Multiplayer entry requires both completed dialogue and the real feature unlock.

The tests are read-only and do not open live scenes, write player data, grant rewards or contact the network. They complement the separate NPCStoryObjectives runtime tests and Unity import/authoring validation.
