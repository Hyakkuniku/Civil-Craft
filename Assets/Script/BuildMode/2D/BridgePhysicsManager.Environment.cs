using System.Collections.Generic;
using UnityEngine;

public partial class BridgePhysicsManager
{
    private BridgeEnvironmentalTest environmentalTest;
    private DeterministicBridgeStressSolver.EnvironmentalLoad environmentalLoad;
    private BridgeEnvironmentalEffects environmentalEffects;
    private float environmentalElapsed;
    private float environmentalEventTime;
    private Bounds environmentalBounds;
    private readonly List<Vector3> environmentalAnchors = new List<Vector3>();
    private readonly List<EnvironmentalBody> environmentalBodies = new List<EnvironmentalBody>();
    private readonly Dictionary<LiveLoadVehicle, Rigidbody> environmentalVehicles = new Dictionary<LiveLoadVehicle, Rigidbody>();

    private struct EnvironmentalBody
    {
        public Rigidbody Body;
        public decimal Mass;
        public decimal ProfileArea;
        public decimal DeckArea;
    }

    private bool EnvironmentalEventVisible => environmentalTest != null && isSimulating && !lockStressTracking &&
        environmentalEventTime >= 0f && environmentalEventTime < environmentalTest.duration;

    public string EnvironmentalStressLabel
    {
        get
        {
            if (!EnvironmentalEventVisible) return null;
            if (environmentalTest.HasWind && environmentalTest.HasEarthquake) return "WIND + QUAKE STRESS";
            return environmentalTest.HasWind ? "WIND GUST STRESS" : "EARTHQUAKE STRESS";
        }
    }

    private void PrepareEnvironmentalTest()
    {
        ClearEnvironmentalTest();
        ContractSO contract = SimulationContract;
        if (!IsSoloSimulationPlayback || contract == null || contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle ||
            contract.environmentalTest == null || !contract.environmentalTest.IsEnabled) return;

        environmentalTest = contract.environmentalTest.Freeze();
        environmentalEventTime = -environmentalTest.startDelay;
        PrepareEnvironmentalAudio();
        bool hasBounds = false;
        foreach (Point point in deterministicPoints)
        {
            if (point == null) continue;
            if (!hasBounds) environmentalBounds = new Bounds(point.preSimPos, Vector3.one);
            else environmentalBounds.Encapsulate(point.preSimPos);
            if (point.IsScenePlacedAnchor) environmentalAnchors.Add(point.preSimPos);
            hasBounds = true;
        }
        foreach (Bar bar in deterministicBars)
        {
            if (bar == null || bar.materialData == null || bar.startPoint == null || bar.endPoint == null ||
                !bar.TryGetComponent(out Rigidbody body)) continue;
            BridgeMaterialSO material = bar.materialData;
            Vector3 delta = bar.endPoint.preSimPos - bar.startPoint.preSimPos;
            float length = new Vector2(delta.x, delta.y).magnitude;
            float mass = length * bar.PlacedMassPerMeter;
            if (!float.IsFinite(length) || !float.IsFinite(mass) || mass < 0f) continue;
            environmentalBodies.Add(new EnvironmentalBody
            {
                Body = body,
                Mass = (decimal)mass,
                ProfileArea = (decimal)DeterministicBridgeStressSolver.EnvironmentalLoad.ProfileArea(
                    length, material.isRoad, material.isRope, material.isDualBeam),
                DeckArea = material.isRoad ? DeterministicBridgeStressSolver.EnvironmentalLoad.DeckArea(length, bar.RoadColliderWidth) : 0m
            });
        }
        if (MultiLoadSession != null)
        {
            foreach (LiveLoadTestParticipant participant in MultiLoadSession.Participants)
                CacheEnvironmentalVehicle(participant.Definition.Vehicle);
        }
        else CacheEnvironmentalVehicle(LiveLoadVehicle.FindActiveForContract(contract));
    }

    private void CacheEnvironmentalVehicle(LiveLoadVehicle vehicle)
    {
        if (vehicle != null && vehicle.TryGetComponent(out Rigidbody body)) environmentalVehicles[vehicle] = body;
    }

    private void TickEnvironmentalTest(float deltaTime)
    {
        if (environmentalTest == null) return;
        environmentalEventTime = environmentalElapsed - environmentalTest.startDelay;
        environmentalElapsed += deltaTime;
        environmentalLoad = environmentalTest.Sample(environmentalEventTime);
        if (!environmentalLoad.IsActive) return;

        foreach (EnvironmentalBody member in environmentalBodies)
        {
            if (member.Body == null || member.Body.isKinematic) continue;
            environmentalLoad.MemberForce(member.Mass, member.ProfileArea, member.DeckArea, out decimal x, out decimal y);
            member.Body.AddForce(new Vector3((float)x, (float)y, 0f), ForceMode.Force);
        }
    }

    private void ApplyEnvironmentalVehicleAcceleration(LiveLoadVehicle vehicle, float supportedFraction)
    {
        if (vehicle == null || (environmentalLoad.AccelerationX == 0m && environmentalLoad.AccelerationY == 0m) ||
            !environmentalVehicles.TryGetValue(vehicle, out Rigidbody body) || body == null || body.isKinematic) return;
        body.AddForce(new Vector3((float)environmentalLoad.AccelerationX, (float)environmentalLoad.AccelerationY, 0f) *
            supportedFraction, ForceMode.Acceleration);
    }

    private void RenderEnvironmentalTest()
    {
        if (!EnvironmentalEventVisible)
        {
            environmentalEffects?.Hide();
            return;
        }

        BuildLocation location = SimulationLocation;
        if (location == null || !Application.isPlaying) return;
        Camera camera = location.cinematicCamera != null && location.cinematicCamera.isActiveAndEnabled
            ? location.cinematicCamera : location.locationCamera;
        if (camera == null || !camera.isActiveAndEnabled)
        {
            environmentalEffects?.Hide();
            return;
        }
        if (environmentalEffects == null)
            environmentalEffects = new BridgeEnvironmentalEffects(environmentalBounds, environmentalAnchors);
        environmentalEffects.Draw(camera, environmentalEventTime, environmentalLoad);
    }

    private void ClearEnvironmentalTest()
    {
        environmentalAudio?.Dispose();
        environmentalAudio = null;
        environmentalEffects?.Dispose();
        environmentalEffects = null;
        environmentalTest = null;
        environmentalLoad = default;
        environmentalElapsed = 0f;
        environmentalEventTime = 0f;
        environmentalBodies.Clear();
        environmentalVehicles.Clear();
        environmentalAnchors.Clear();
    }
}
