using UnityEngine;

public sealed class TestPhysicsManager { public bool IsSimulationActive = true; }

public partial class LiveLoadVehicle : MonoBehaviour
{
    // These are the state fields supplied by the main vehicle controller in Unity.
    public bool isRemoteMultiplayerRepresentation;
    public bool participatingInCurrentSimulation = true;
    public bool isDriving = true;
    public bool canParticipate = true;
    public TestPhysicsManager physicsManager = new TestPhysicsManager();
    private bool CanParticipateInCurrentSimulation() => canParticipate;

    public void SetSound(VehicleSimulationSound settings) => simulationSound = settings;
    public void BeginAudio() => BeginSimulationAudio();
    public void RefreshAudio() => RefreshSimulationAudio();
    public void StopAudio() => StopSimulationAudio();
    public void ApplicationPause(bool paused) => OnApplicationPause(paused);
    public void ApplicationFocus(bool focused) => OnApplicationFocus(focused);
}
