# Vehicle simulation audio checks

From the Unity project root:

```powershell
dotnet run --project Tests/VehicleSimulationAudio/VehicleSimulationAudio.csproj
```

This package-free .NET harness compiles the actual `VehicleSimulationLoop.cs` and
`LiveLoadVehicle.Audio.cs` sources. Small Unity/audio doubles expose source
creation, playback calls, routing, settings, and lifecycle state. It verifies
independent vehicle loops, channel reuse for host clones, pause/resume, sound
changes, and the simulation-start latch across stop and participation changes.

These checks do not open Unity, play audio, or access a network. Audible clip
quality, real mixer/listener behavior, Unity object-destruction semantics, Android
focus events, and scene callback wiring still require Editor/device inspection.
