# Main-menu design checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/MainMenuRevamp/MainMenuRevamp.csproj --verbosity quiet
```

The harness links the actual production, System-only main-menu layout policy.
It verifies finite, positive dimensions, safe-area bounds, deterministic placement,
and reserved space for the existing Updates entry and its NEW UPDATE badge across
portrait, landscape, tablet and unusually wide layouts. Randomized samples are
deterministic and include reduced safe-area sizes.

Source assertions check that the new layout stays presentation-only, retains the
existing Bekind font, and that the opt-in editor authoring includes callback and
dirty-scene preservation checks. Such assertions are shipping guards, not proof of
runtime behavior or appearance.

The default shipping gate also indexes the saved Main Menu YAML without
hard-coded Unity file IDs. It checks that the actual hero and original dropdown
share the preserved MainPanel → Container → MainMenuCanvas branch, above the
fullscreen input backdrop. It resolves the original controller's dropdown and
the presentation component's references, rather than guessing from names alone.
Run it after native authoring is complete and the Main Menu is saved.

The first screen must use centered Title/Edition/Play siblings directly under
MainPanel without the old paper container. The recoverable old hero board must
remain inactive, and Quit's primary text—not merely its button—must be opaque red.

Each saved main-menu action must also retain exactly one enabled, active primary
label with its expected caption. Secondary tile captions/kickers are excluded.
This catches repeated authoring accidentally selecting a decorative secondary
label and hiding the real Play, Credits or Back text.

No Unity session, input, account, authentication, network request, preferences or
player save is used. These checks do not replace the native Unity render and
raycast validation, nor device testing of the original menu actions.

Related regression gates:

```powershell
dotnet run --project Tests/MainMenuUpdates/MainMenuUpdates.csproj --verbosity quiet
dotnet run --project Tests/BhanNameLogin/BhanNameLogin.csproj --verbosity quiet
dotnet run --project Tests/PlayFabUsernameLoginPolicy/PlayFabUsernameLoginPolicy.csproj --verbosity quiet
```
