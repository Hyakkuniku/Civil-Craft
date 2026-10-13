# Mobile joystick regression checks

From the Unity repository root:

```powershell
dotnet run --project Tests/MobileJoystick/MobileJoystick.csproj --verbosity quiet
```

No Unity session, physical inputs, accounts, saves, or network requests are used.
The project links the actual `MobileJoystickMotion` source. It also extracts and
compiles the current `MobileJoystickSettings` class and joystick runtime methods
directly from their production files; the tested implementations are not copies.

Checks cover:

- Fixed base placement, clamped handle motion, and neutral off-center touchdown.
- Floating touchdown, finger-following handle/base, reversal, stationary hold,
  cancellation, and multiple canvas coordinate scales.
- Floating origin limits at each safe-area edge/corner, neutral clamped initial
  touchdown, continuous walking while a finger passes the UI boundary, bounded
  reversal, no stationary edge drift, and cleared limits after cancellation.
- One-finger ownership and rejection of unrelated/released pointers or invalid
  coordinates/ranges.
- Left-half touch eligibility, fixed-only hit testing, existing UI priority,
  canvas conversion, and unchanged virtual left-stick output.
- Full ring/knob safe-area geometry, including authored knob offset and full
  handle travel; larger artwork, translated parents, narrow Canvas layouts,
  midpoint protection even when oversized artwork must clip on the left, and
  releasing input when the safe area changes without a screen-size change.
- Front-to-back graphical raycast priority: the joystick accepts clicks when it
  is in front of an unrelated UI rectangle, while foreground buttons/blockers
  still win; non-UI physics hits and empty records do not change that ordering.
- Actual touch polling, release/cancel/device loss, multi-touch, fresh-touch-only
  acquisition after UI closes, and a mouse host matrix covering both joystick
  modes, desktop builds, actual mobile builds, and the Editor with an Android
  build target / Device Simulator's mobile platform flag.
- Editor presses in empty left-side space with no joystick hit, both within and
  at the movement-region edges: floating mode claims and drags, while fixed mode
  still requires touching the authored joystick.
- Editor focus polling after disabled scene/domain reload: stale cached focus
  recovery, missed focus-loss callbacks, release of synthetic input, and requiring
  a new mouse press rather than resuming an already-held pointer after focus returns.
- Dialogue, pause, panels, loading, building, disabled remote player input,
  app focus/pause, mode changes, and resized-layout cancellation.
- Device-local setting defaults, persistence, change notifications, and listener
  reset for disabled domain reload.

Engine/UI/device boundaries are test doubles. These checks do not replace a
visual/input check in Unity or on a phone, particularly for actual scene raycasts,
safe-area layouts, touch events, and scene migration.

Native Unity regression (Edit Mode, no physical/synthetic input or Play Mode):
`Tools > Civil Craft > Validate Movement Grid Raycasts`. The detached fixture
uses real camera-space `GraphicRaycaster` hits with a disabled build camera,
then queries the actual joystick's UI-start predicate to verify decorative
backgrounds no longer block empty-space starts. Grid raycast targets stay intact
for construction; interactive controls and nested HUD canvases remain protected.
Results are written under `Temp` without saving a scene.
