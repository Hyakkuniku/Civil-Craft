# Live-load vehicle sounds

Each `LiveLoadVehicle` now has **Simulation Audio → Simulation Sound** in the
Inspector. Assign a different seamless looping **Clip** to each cart, taxi or truck.
No vehicle recordings are bundled by this change; leaving Clip empty is silent.

## Assigning sounds

1. Import the engine/wheel recordings as Unity AudioClips. For short loops, use
   mono where practical, enable Loop through the vehicle system (already automatic),
   and trim the recording so the join is smooth.
2. Select the actual vehicle in the scene, or edit its vehicle prefab. The fields
   live on **Live Load Vehicle**, not on the ContractSO. Scene prefab overrides can
   give two instances of the same vehicle different sounds.
3. Expand **Simulation Audio → Simulation Sound** and set **Clip**. Optional **Output**
   overrides the mixer group; normally the active AudioManager's SFX output is used,
   so Settings → SFX Volume controls the loop. For direct scene tests without that
   manager, assign **MainAudioMixer / SFX** explicitly.
4. Adjust **Volume** and **Pitch**. Vehicle sounds use centered 2D audio, so camera
   distance and position do not change their loudness. This also applies to
   multiplayer spectators and vehicles with older saved 3D settings.
5. Start the contract's bridge simulation. The assigned sound starts when that
   vehicle begins driving after bridge settling. Stop/reset/failure/finish silences it.

No manual AudioSource or Play On Awake wiring is required. The system owns one
looping source on a named child and reuses it, including on cloned host test vehicles.
It never uses the global one-shot SFX pool. Missing clips do not affect physics.

Game pause, AudioListener pause, focus loss and application pause suspend playback.
Playback resumes only while the same vehicle is still in its active simulation.
Disable, scene exit and reset stop it; re-enabling an idle vehicle does not start a loop.

## Verification

Use different clips for two contracts and confirm only the selected live load is
audible. Test bridge settling, crossing, finish, failure, reset, repeated retries,
pause/resume, SFX mute, and leaving the scene. In multiplayer, the host clone uses
the authored clip; the guest's mesh presentation uses its matching local vehicle
configuration and follows received movement. Keep sound assignments consistent in
the shipped build. A new APK is required to hear this feature on Android.

Automated loop/lifecycle checks:
`dotnet run --project Tests/VehicleSimulationAudio/VehicleSimulationAudio.csproj`.
These use audio/engine doubles and do not replace listening in Unity or on Android.
