using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

internal static class Program
{
    private static int checks;
    private const string ChannelName = "Vehicle Simulation Audio (Runtime)";

    private static void Main()
    {
        TestSilentAndIdleVehicles();
        TestIndependentVehiclesAndChannelReuse();
        TestSettingsAndRouting();
        TestPauseResumeAndStop();
        TestClipReplacementAndDispose();
        TestVehicleLifecycleGuards();
        TestVehiclePauseLifecycle();
        Console.WriteLine($"All {checks} vehicle simulation audio checks passed.");
    }

    private static VehicleSimulationSound Sound(string name = "Truck") => new VehicleSimulationSound { clip = new AudioClip(name) };
    private static AudioSource Source(GameObject vehicle) => vehicle.AllComponents<AudioSource>().Single();
    private static int Sources(GameObject vehicle) => vehicle.AllComponents<AudioSource>().Count();
    private static void Check(bool condition, string scenario)
    {
        if (!condition) throw new Exception(scenario);
        checks++;
    }
    private static void Near(float expected, float actual, string scenario) => Check(Math.Abs(expected - actual) < 0.0001f, $"{scenario}: expected {expected}, received {actual}");
    private static void ResetEnvironment()
    {
        AudioManager.Instance = null;
        AudioListener.pause = false;
        Application.isFocused = true;
        Time.timeScale = 1f;
    }

    private static void TestSilentAndIdleVehicles()
    {
        ResetEnvironment();
        var vehicle = new GameObject();
        var loop = new VehicleSimulationLoop(vehicle.transform);
        loop.Tick(null, true, false);
        loop.Tick(new VehicleSimulationSound(), true, false);
        loop.Tick(Sound(), false, false);
        loop.Tick(Sound(), true, true);
        Check(Sources(vehicle) == 0, "Empty, idle, and initially paused vehicles allocate no source");
        vehicle.activeSelf = false;
        loop.Tick(Sound(), true, false);
        Check(Sources(vehicle) == 0, "Inactive vehicle allocates no source");
        new VehicleSimulationLoop(null).Tick(Sound(), true, false);
        Check(true, "Absent owner is safely silent");

        vehicle.activeSelf = true;
        loop.Tick(Sound(), true, false);
        var source = Source(vehicle);
        loop.Tick(new VehicleSimulationSound(), true, false);
        Check(!source.isPlaying && !source.isPaused, "Clearing a clip stops previously playing audio");
    }

    private static void TestIndependentVehiclesAndChannelReuse()
    {
        ResetEnvironment();
        var taxi = new GameObject("Taxi");
        var truck = new GameObject("Truck");
        var taxiSound = Sound("Taxi engine");
        var truckSound = Sound("Truck engine");
        var taxiLoop = new VehicleSimulationLoop(taxi.transform);
        var truckLoop = new VehicleSimulationLoop(truck.transform);
        taxiLoop.Tick(taxiSound, true, false);
        truckLoop.Tick(truckSound, true, false);
        Check(!ReferenceEquals(Source(taxi), Source(truck)), "Distinct vehicles own distinct audio sources");
        Check(ReferenceEquals(Source(taxi).clip, taxiSound.clip), "Taxi keeps its own authored clip");
        Check(ReferenceEquals(Source(truck).clip, truckSound.clip), "Truck keeps its own authored clip");
        for (int i = 0; i < 20; i++) taxiLoop.Tick(taxiSound, true, false);
        Check(Sources(taxi) == 1 && taxi.transform.childCount == 1, "Repeated frames create only one owned channel");
        Check(Source(taxi).playCalls == 1, "Repeated driving frames do not restart audio");
        taxiLoop.Stop();
        Check(!Source(taxi).isPlaying && Source(truck).isPlaying, "Stopping one vehicle leaves the other playing");

        var clone = new GameObject("Host test clone");
        var inheritedChild = new GameObject(ChannelName);
        inheritedChild.transform.SetParent(clone.transform, false);
        var inheritedSource = inheritedChild.AddComponent<AudioSource>();
        inheritedSource.clip = taxiSound.clip;
        inheritedSource.Play();
        new VehicleSimulationLoop(clone.transform).Tick(truckSound, true, false);
        Check(Sources(clone) == 1 && clone.transform.childCount == 1, "Host clone reuses its inherited runtime channel");
        Check(ReferenceEquals(Source(clone), inheritedSource), "Host clone uses the exact inherited source");
        Check(inheritedSource.stopCalls >= 1 && inheritedSource.playCalls == 2, "Inherited playback is stopped before the clone starts its own loop");
        Check(ReferenceEquals(inheritedSource.clip, truckSound.clip), "Clone replaces inherited clip with its own vehicle sound");
        Check(!inheritedSource.playOnAwake, "Inherited autoplay is disabled");

        var childOnlyVehicle = new GameObject();
        new GameObject(ChannelName).transform.SetParent(childOnlyVehicle.transform, false);
        new VehicleSimulationLoop(childOnlyVehicle.transform).Tick(Sound(), true, false);
        Check(Sources(childOnlyVehicle) == 1 && childOnlyVehicle.transform.childCount == 1, "Existing named child without a source is completed in place");
    }

    private static void TestSettingsAndRouting()
    {
        ResetEnvironment();
        var fallback = new AudioMixerGroup("SFX");
        var custom = new AudioMixerGroup("Vehicle override");
        AudioManager.Instance = new AudioManager { SfxOutput = fallback };
        var vehicle = new GameObject();
        var sound = Sound();
        var loop = new VehicleSimulationLoop(vehicle.transform);
        sound.volume = 2f;
        sound.pitch = 10f;
        loop.Tick(sound, true, false);
        var source = Source(vehicle);
        Check(source.loop && !source.playOnAwake && !source.ignoreListenerPause, "Source loops, disables autoplay, and respects the listener pause");
        Near(0f, source.dopplerLevel, "Doppler is disabled for simulation movement");
        Near(1f, source.volume, "Volume upper clamp");
        Near(3f, source.pitch, "Pitch upper clamp");
        Near(0f, source.spatialBlend, "Vehicle playback is 2D without proximity falloff");
        Near(0f, source.panStereo, "Vehicle playback is centered");
        Check(!source.spatialize, "Spatializer is disabled");
        Check(ReferenceEquals(source.outputAudioMixerGroup, fallback), "Unspecified mixer uses active SFX output");
        sound.volume = -1f;
        sound.pitch = -1f;
        source.spatialBlend = 1f;
        source.panStereo = 1f;
        source.spatialize = true;
        sound.output = custom;
        loop.Tick(sound, true, false);
        Near(0f, source.volume, "Volume lower clamp");
        Near(0.1f, source.pitch, "Pitch lower clamp");
        Near(0f, source.spatialBlend, "Inherited 3D sources are converted to 2D");
        Near(0f, source.panStereo, "Inherited source panning is cleared");
        Check(!source.spatialize, "Inherited spatializer is cleared");
        Check(ReferenceEquals(source.outputAudioMixerGroup, custom), "Vehicle mixer override takes precedence");
        AudioManager.Instance = null;
        loop.Tick(sound, true, false);
        Check(ReferenceEquals(source.outputAudioMixerGroup, custom), "Explicit output works without an AudioManager");
        sound.output = null;
        loop.Tick(sound, true, false);
        Check(source.outputAudioMixerGroup == null, "Absent output and AudioManager remain safely unrouted");
        Check(source.playCalls == 1, "Changing live source settings does not restart audio");
    }

    private static void TestPauseResumeAndStop()
    {
        ResetEnvironment();
        var vehicle = new GameObject();
        var loop = new VehicleSimulationLoop(vehicle.transform);
        var sound = Sound();
        loop.Tick(sound, true, false);
        var source = Source(vehicle);
        loop.Tick(sound, true, true);
        loop.Tick(sound, true, true);
        Check(source.isPaused && !source.isPlaying && source.pauseCalls == 1, "Pause suspends once across repeated paused frames");
        loop.Tick(sound, true, false);
        Check(source.isPlaying && !source.isPaused, "Resuming unpauses the same source");
        Check(source.unpauseCalls == 1 && source.playCalls == 1, "Resume preserves playback instead of restarting");
        loop.Suspend();
        loop.Suspend();
        Check(source.isPaused && source.pauseCalls == 2, "Explicit suspend is idempotent while paused");
        loop.Stop();
        Check(!source.isPlaying && !source.isPaused, "Stop clears a paused loop");
        loop.Tick(sound, false, false);
        Check(source.playCalls == 1 && source.unpauseCalls == 1, "Idle frame cannot resurrect a stopped paused loop");
        loop.Tick(sound, true, false);
        Check(source.isPlaying && source.playCalls == 2, "A new driving start plays after stop");
        loop.Tick(sound, true, true);
        vehicle.activeSelf = false;
        loop.Tick(sound, true, true);
        Check(!source.isPlaying && !source.isPaused, "Disabling a paused owner clears its suspended audio");
    }

    private static void TestClipReplacementAndDispose()
    {
        ResetEnvironment();
        var vehicle = new GameObject();
        var loop = new VehicleSimulationLoop(vehicle.transform);
        var sound = Sound();
        loop.Tick(sound, true, false);
        var source = Source(vehicle);
        loop.Tick(sound, true, true);
        var replacement = Sound("Replacement engine");
        loop.Tick(replacement, true, false);
        Check(ReferenceEquals(source.clip, replacement.clip) && source.isPlaying, "Replacing a suspended clip starts the new sound");
        Check(source.playCalls == 2 && source.unpauseCalls == 0, "New clip is played rather than unpausing old audio");
        Check(Sources(vehicle) == 1, "Clip changes retain the same owned source");
        loop.Dispose();
        Check(!source.isPlaying && !source.isPaused, "Disposal stops the owned audio");
        loop.Dispose();
        Check(Sources(vehicle) == 1, "Repeated disposal leaves reusable runtime channel intact");
    }

    private static LiveLoadVehicle Vehicle(string name = "Vehicle")
    {
        var vehicle = new GameObject(name).AddComponent<LiveLoadVehicle>();
        vehicle.SetSound(Sound(name));
        return vehicle;
    }

    private static void TestVehicleLifecycleGuards()
    {
        ResetEnvironment();
        var idle = Vehicle();
        idle.RefreshAudio();
        Check(Sources(idle.gameObject) == 0, "Driving state alone cannot play before the explicit simulation start");
        idle.BeginAudio();
        Check(Source(idle.gameObject).isPlaying, "Explicit simulation start plays an eligible vehicle");
        Check(ReferenceEquals(idle.SimulationSound.clip, Source(idle.gameObject).clip), "Spectator-facing settings expose the same authored clip");
        idle.StopAudio();
        idle.RefreshAudio();
        Check(!Source(idle.gameObject).isPlaying, "A stopped simulation is not restarted by a late frame");

        var cases = new (string name, Action<LiveLoadVehicle> reject, Action<LiveLoadVehicle> restore)[]
        {
            ("disabled component", v => v.enabled = false, v => v.enabled = true),
            ("inactive object", v => v.gameObject.activeSelf = false, v => v.gameObject.activeSelf = true),
            ("remote representation", v => v.isRemoteMultiplayerRepresentation = true, v => v.isRemoteMultiplayerRepresentation = false),
            ("nonparticipating vehicle", v => v.participatingInCurrentSimulation = false, v => v.participatingInCurrentSimulation = true),
            ("not driving", v => v.isDriving = false, v => v.isDriving = true),
            ("absent simulation manager", v => v.physicsManager = null, v => v.physicsManager = new TestPhysicsManager()),
            ("inactive simulation", v => v.physicsManager.IsSimulationActive = false, v => v.physicsManager.IsSimulationActive = true),
            ("contract participation rejected", v => v.canParticipate = false, v => v.canParticipate = true),
        };
        foreach (var scenario in cases)
        {
            var vehicle = Vehicle(scenario.name);
            vehicle.BeginAudio();
            var source = Source(vehicle.gameObject);
            scenario.reject(vehicle);
            vehicle.RefreshAudio();
            Check(!source.isPlaying && !source.isPaused, $"{scenario.name} stops audio");
            scenario.restore(vehicle);
            vehicle.RefreshAudio();
            Check(!source.isPlaying && source.playCalls == 1, $"{scenario.name} clears start latch until another simulation start");
            vehicle.BeginAudio();
            Check(source.isPlaying && source.playCalls == 2 && Sources(vehicle.gameObject) == 1, $"{scenario.name} permits an explicit new start without another source");
        }

        var silent = Vehicle();
        silent.SetSound(new VehicleSimulationSound());
        silent.BeginAudio();
        silent.SetSound(Sound());
        silent.RefreshAudio();
        Check(Sources(silent.gameObject) == 0, "Adding a previously absent clip requires another explicit simulation start");
        silent.BeginAudio();
        Check(Source(silent.gameObject).isPlaying, "A new explicit start plays a newly assigned clip");
    }

    private static void TestVehiclePauseLifecycle()
    {
        ResetEnvironment();
        var cases = new (string name, Action<LiveLoadVehicle> pause, Action<LiveLoadVehicle> resume)[]
        {
            ("application pause", v => v.ApplicationPause(true), v => v.ApplicationPause(false)),
            ("application focus", v => v.ApplicationFocus(false), v => v.ApplicationFocus(true)),
            ("global focus", v => Application.isFocused = false, v => Application.isFocused = true),
            ("audio listener", v => AudioListener.pause = true, v => AudioListener.pause = false),
            ("simulation timescale", v => Time.timeScale = 0f, v => Time.timeScale = 1f),
        };
        foreach (var scenario in cases)
        {
            ResetEnvironment();
            var vehicle = Vehicle(scenario.name);
            vehicle.BeginAudio();
            var source = Source(vehicle.gameObject);
            scenario.pause(vehicle);
            vehicle.RefreshAudio();
            Check(source.isPaused && !source.isPlaying, $"{scenario.name} suspends eligible vehicle audio");
            scenario.resume(vehicle);
            vehicle.RefreshAudio();
            Check(source.isPlaying && source.playCalls == 1 && source.unpauseCalls == 1, $"{scenario.name} resumes without replaying the clip");
            scenario.pause(vehicle);
            vehicle.RefreshAudio();
            vehicle.StopAudio();
            scenario.resume(vehicle);
            vehicle.RefreshAudio();
            Check(!source.isPlaying && !source.isPaused && source.playCalls == 1, $"Stopping during {scenario.name} prevents late resume");
        }
        ResetEnvironment();
        var initiallyPaused = Vehicle();
        initiallyPaused.ApplicationPause(true);
        initiallyPaused.BeginAudio();
        Check(Sources(initiallyPaused.gameObject) == 0, "Starting during application pause creates no autoplay source");
        initiallyPaused.ApplicationPause(false);
        Check(Source(initiallyPaused.gameObject).isPlaying, "A valid start requested while paused begins upon resume");
    }
}
