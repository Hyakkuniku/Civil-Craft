using UnityEngine;

public partial class BridgePhysicsManager
{
    private BuildLocation sessionTestLocation;
    private LiveLoadVehicle sessionTestVehicle;
    public bool IsSessionChallengeTest => sessionTestLocation != null;
    public LiveLoadVehicle SessionTestVehicle => sessionTestVehicle;
    private BuildLocation SimulationLocation => sessionTestLocation != null ? sessionTestLocation :
        (GameManager.Instance != null ? GameManager.Instance.ActiveBuildLocation : null);
    private ContractSO SimulationContract => sessionTestLocation != null ? sessionTestLocation.activeContract :
        (GameManager.Instance != null ? GameManager.Instance.CurrentContract : null);

    // Called only on a disposable, initially inactive manager before its Start.
    // The original scene manager and its completion/reward subscribers stay idle.
    internal void ConfigureSessionChallengeTest(BuildLocation location, LiveLoadVehicle vehicle)
    {
        if (location == null || !location.IsSessionChallengeLocation || vehicle == null || IsSimulationActive)
            throw new System.InvalidOperationException("Invalid isolated challenge simulation context.");
        sessionTestLocation = location; sessionTestVehicle = vehicle;
    }

    internal void FreezeSessionChallengeTest()
    {
        if (!IsSessionChallengeTest) return;
        isSimulating = pendingSimulationStart = false; lockStressTracking = true;
        sessionTestVehicle.EmergencyStop();
        foreach (Rigidbody body in sessionTestLocation.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
        }
        RestoreGlobalPhysicsSettings();
    }
}
