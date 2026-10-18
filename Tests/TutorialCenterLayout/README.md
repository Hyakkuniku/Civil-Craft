# Center tutorial presentation checks

Run `dotnet run --project Tests/TutorialCenterLayout/TutorialCenterLayout.csproj` from the Unity project root.

This harness links the production centered-message layout helper directly. Small Unity and TMP boundaries exercise geometry, measured-text growth, safe-area clamping, Center/LowCenter offsets, repeat application, short/long messages, and preservation of authored content and style. The TMP boundary supplies deterministic preferred-size measurements, including TMP's unwrapped preferred-width/wrapped preferred-height distinction. A regression verifies that a long ordinary sentence still fits despite its unwrapped preferred width exceeding the paper. Real typeface wrapping and native phone/tablet rendering are verified separately in Unity.

Source guards check that TutorialManager applies sizing after assigning each message and before its entrance animation, refreshes changed screen bounds before tracing early returns, and leaves progression, story callbacks, the left banner, and the existing Continue control outside the layout helper's authority. These tests do not operate the Editor or modify scenes, saves, accounts, or network state.
