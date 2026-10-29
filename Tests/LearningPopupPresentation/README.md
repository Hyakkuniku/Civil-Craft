Run from the Unity project root:

```powershell
dotnet run --project Tests/LearningPopupPresentation/LearningPopupPresentation.csproj
```

The frozen baseline protects the existing BridgeBuilder data assets and the saved NPC progression, contract-giver, build-location and tutorial components in CanyonCrossing and Bhan House. It deliberately does not rebase after UI authoring. The presentation code may change authored UI while preserving these stories, assignments and balancing values.

The opt-in native command **Tools → Civil Craft → Learning Popups → Validate Authored Phone and Tablet Layouts** checks the actual authored lesson panels and shared material-reader prefab. It copies only the popup into temporary rendering scenes, sets content directly without discovery or story callbacks, checks five device sizes (including 800×360 and portrait), image proportions, 44-pixel dismiss targets, complete scrollable descriptions and the reachable end of a long reference. PNG previews are written under `Temp/LearningPopupPresentation`; the report is `Logs/learning-popup-presentation.txt`.

No game button is invoked and no source scene, prefab or player save is written by validation.
