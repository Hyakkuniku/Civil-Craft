# Jump tutorial lock boundary regressions

Run from the Unity project root:

```powershell
dotnet run --project Tests/JumpTutorialButtonLock/JumpTutorialButtonLock.csproj
```

The linked production `JumpTutorialButtonLock.cs` is executed directly. `UnityBoundary.cs` explicitly doubles only the engine-facing object lifecycle, Unity destroyed-object null semantics, persistent event inspection, scenes and CanvasGroup/Selectable authority needed by these scenarios. It does not replace or duplicate the production tutorial gate logic.

Coverage includes unlocked/locked/unlocked transitions, authored disabled colors, preserved Button/local/ancestor restrictions, component and GameObject disable/re-enable, null/destroyed manager, owned-group teardown/recreation, Jump vs Run independence, inactive button discovery, normal/additive scenes, repeated retained-domain bootstraps, listener and method/target matching, legacy null targets, unchanged event wiring and unchanged authored sprites/colors/aspect/size. Source checks exclude per-frame hierarchy lookup, click rewiring, player persistence, scene transitions and forced Button authority. Runtime helper, PlayerMotor and relevant authored scene hashes are checked before and after the run.

This is **not** native Unity UGUI rendering, real touch/click, color transition or lifecycle scheduling validation. The simplified boundary's derived state only confirms the expected authority path; actual disabled tint and blocked clicks must be verified separately in Unity, including the existing Run button comparison. No source, scene, prefab, player save or network is modified by this runner. The usual test build writes only this project's `bin/obj` output.
