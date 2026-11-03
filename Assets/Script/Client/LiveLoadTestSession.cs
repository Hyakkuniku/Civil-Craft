using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum LiveLoadTestParticipantState { Waiting, Driving, Passed, Failed }

public sealed class LiveLoadTestParticipant
{
    public LiveLoadTestParticipantDefinition Definition { get; }
    public LiveLoadTestParticipantState State { get; internal set; }
    public string FailureReason { get; internal set; }
    internal bool FinishCandidate, EngagedBridge;
    internal float BelowTime, StallTime, BestProgress;
    internal LiveLoadTestParticipant(LiveLoadTestParticipantDefinition definition) { Definition = definition; }
}

/// <summary>One continuous, host-owned attempt. Only final completion reaches story/reward systems.</summary>
public sealed class LiveLoadTestSession
{
    private readonly BridgePhysicsManager physics;
    private readonly List<LiveLoadTestParticipant> participants = new List<LiveLoadTestParticipant>();
    private readonly Dictionary<LiveLoadVehicle, LiveLoadTestParticipant> byVehicle = new Dictionary<LiveLoadVehicle, LiveLoadTestParticipant>();
    private bool prepared, driving, cancelled, outcomePublished;
    private float elapsed;
    private Action finalFinishEvent;
    public LiveLoadTestRoster Roster { get; }
    public BridgePhysicsManager Physics => physics;
    public IReadOnlyList<LiveLoadTestParticipant> Participants { get; }
    public bool AllPassed { get; private set; }
    public bool Failed { get; private set; }
    public string FailureReason { get; private set; } = string.Empty;
    public float Elapsed => elapsed;
    public event Action Changed;
    public LiveLoadVehicle FocusVehicle
    {
        get
        {
            foreach (var item in participants) if (item.State == LiveLoadTestParticipantState.Driving) return item.Definition.Vehicle;
            foreach (var item in participants) if (item.State == LiveLoadTestParticipantState.Waiting) return item.Definition.Vehicle;
            return participants.Count > 0 ? participants[participants.Count - 1].Definition.Vehicle : null;
        }
    }
    public LiveLoadTestSession(BridgePhysicsManager physics, LiveLoadTestRoster roster)
    {
        this.physics = physics != null ? physics : throw new ArgumentNullException(nameof(physics));
        Roster = roster ?? throw new ArgumentNullException(nameof(roster));
        foreach (var definition in roster.Participants)
        {
            var item = new LiveLoadTestParticipant(definition); participants.Add(item); byVehicle.Add(definition.Vehicle, item);
        }
        Participants = new ReadOnlyCollection<LiveLoadTestParticipant>(participants);
    }
    public bool Contains(LiveLoadVehicle vehicle) => vehicle != null && byVehicle.ContainsKey(vehicle);
    public void Prepare()
    {
        if (prepared || cancelled) return;
        prepared = true;
        foreach (var item in participants) item.Definition.Vehicle.PrepareMultiLoadTest(this, item.Definition.WeightKg);
    }
    public void BeginDriving()
    {
        if (cancelled || driving || Failed) return;
        Prepare(); driving = true; elapsed = 0f;
        ReleaseDueVehicles();
    }
    public void ReportFinishCandidate(LiveLoadVehicle vehicle, Action contractCompleted = null)
    {
        if (cancelled || Failed || AllPassed || vehicle == null || !byVehicle.TryGetValue(vehicle, out var item) ||
            item.State != LiveLoadTestParticipantState.Driving) return;
        item.FinishCandidate = true;
        if (contractCompleted != null) finalFinishEvent = contractCompleted;
    }
    public void Tick(float simulationDelta)
    {
        if (!driving || cancelled || !physics.isSimulating) return;
        if (!Finite(simulationDelta) || simulationDelta <= 0f) return;
        if (Failed) { PublishFailure(); return; }
        if (AllPassed) { PublishCompletion(); return; }
        elapsed += simulationDelta;
        ReleaseDueVehicles();
        foreach (var item in participants)
        {
            if (item.State == LiveLoadTestParticipantState.Passed) continue;
            LiveLoadVehicle vehicle = item.Definition.Vehicle;
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || !vehicle.enabled)
            { item.State = LiveLoadTestParticipantState.Failed; item.FailureReason = item.Definition.DisplayName + " is no longer available."; Fail(item.FailureReason, vehicle); break; }
            if (item.State != LiveLoadTestParticipantState.Driving) continue;
            if (physics.TryGetVehicleRoadState(vehicle, out _, out float factor) && factor > 0.01f) item.EngagedBridge = true;
            LevelFailedManager failure = !physics.IsSessionChallengeTest ? LevelFailedManager.Instance : null;
            float fallDistance = failure != null ? failure.maximumVehicleFallDistance : 6f;
            float deathY = failure != null ? failure.deathThreshold : -15f;
            bool below = vehicle.TryGetVerticalRouteDeviation(out float deviation, out float height)
                ? deviation < -Mathf.Max(1f, fallDistance) || height < deathY : height < deathY;
            item.BelowTime = below ? item.BelowTime + simulationDelta : 0f;
            if (item.BelowTime >= (failure != null ? failure.vehicleFallConfirmationDuration : .35f))
            { Fail(item.Definition.DisplayName + " fell before completing the crossing.", vehicle); break; }
            // End markers are also authoritative route candidates in isolated
            // challenge scenes, which intentionally have no story finish zones.
            if (physics.IsSessionChallengeTest && vehicle.HasReachedEnd) item.FinishCandidate = true;
            // Keep driving to the authored parking marker, not merely until the
            // rear wheel clears the bridge. Otherwise the first car parks at
            // the bank and blocks later vehicles from clearing the same lane.
            if (!below && item.EngagedBridge && item.FinishCandidate && vehicle.HasReachedEnd && FullyClearedBridge(vehicle))
            {
                item.State = LiveLoadTestParticipantState.Passed; vehicle.ParkForMultiLoadTest(this); Changed?.Invoke();
                continue;
            }
            float progress = vehicle.NormalizedRouteProgress;
            if (progress >= item.BestProgress + .005f) { item.BestProgress = progress; item.StallTime = 0f; }
            else item.StallTime += simulationDelta;
            if (item.StallTime >= (failure != null ? Mathf.Max(1f, failure.vehicleStallTimeout) : 8f))
            { Fail(item.Definition.DisplayName + (vehicle.HasReachedEnd && !item.FinishCandidate
                ? " reached its route end without entering the contract's finish zone." : " stalled before completing the crossing."), vehicle); break; }
        }
        if (Failed) { PublishFailure(); return; }
        bool passed = true;
        foreach (var item in participants) if (item.State != LiveLoadTestParticipantState.Passed) { passed = false; break; }
        if (passed) { AllPassed = true; Changed?.Invoke(); }
        else ReleaseDueVehicles();
    }
    private void ReleaseDueVehicles()
    {
        bool sequential = Roster.Mode == ContractSO.VehicleTestMode.Sequential;
        foreach (var item in participants)
        {
            if (sequential && item.State == LiveLoadTestParticipantState.Driving) return;
            if (item.State != LiveLoadTestParticipantState.Waiting) continue;
            if (!sequential && item.Definition.DepartureDelay > elapsed) continue;
            if (item.Definition.Vehicle == null)
            { item.State = LiveLoadTestParticipantState.Failed; item.FailureReason = item.Definition.DisplayName + " is missing."; Fail(item.FailureReason); return; }
            item.State = LiveLoadTestParticipantState.Driving;
            item.BestProgress = item.Definition.Vehicle.NormalizedRouteProgress;
            item.Definition.Vehicle.ReleaseForMultiLoadTest(this); Changed?.Invoke();
            if (sequential) return;
        }
    }
    private bool FullyClearedBridge(LiveLoadVehicle vehicle)
    {
        if (!vehicle.TryGetMultiLoadEnvelopeBounds(out Bounds bounds) || !physics.TryGetRoadSpan(out float minX, out float maxX)) return false;
        vehicle.GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end);
        return end.x > start.x ? bounds.min.x >= maxX - .01f : bounds.max.x <= minX + .01f;
    }
    public void Fail(string reason, LiveLoadVehicle vehicle = null)
    {
        if (Failed || cancelled || outcomePublished) return;
        Failed = true; AllPassed = false; FailureReason = string.IsNullOrWhiteSpace(reason) ? "Live load test failed." : reason;
        foreach (var item in participants)
        {
            if (vehicle != null && item.Definition.Vehicle == vehicle) { item.State = LiveLoadTestParticipantState.Failed; item.FailureReason = FailureReason; }
            if (item.Definition.Vehicle != null) item.Definition.Vehicle.EmergencyStop();
        }
        Changed?.Invoke();
    }
    private void PublishFailure()
    {
        if (!physics.IsSessionChallengeTest && LevelFailedManager.Instance != null && !LevelFailedManager.Instance.isFailed)
            LevelFailedManager.Instance.TriggerLevelFailed(FailureReason);
    }
    private void PublishCompletion()
    {
        if (physics.IsSessionChallengeTest || outcomePublished) return;
        if (LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed) { Fail("Bridge test failed."); return; }
        ContractSO contract = Roster.Contract;
        float limit = contract.enforceMaxStress ? contract.maxAllowedStress / 100f : 1f;
        if (!BridgePhysicsManager.DebugInvincibleBridge && (physics.HadBrokenPartsThisRun ||
            physics.IsContractStressLimitArmed && physics.peakStressThisRun >= limit))
        { Fail("Bridge capacity exceeded during the live load test."); PublishFailure(); return; }
        if (LevelCompleteManager.Instance == null) { Fail("The level completion manager is missing."); PublishFailure(); return; }
        outcomePublished = true; physics.lockStressTracking = true;
        finalFinishEvent?.Invoke();
        LevelCompleteManager.Instance.CompleteLevelForVehicle(contract, participants[participants.Count - 1].Definition.Vehicle);
    }
    public void Cancel()
    {
        if (cancelled) return;
        cancelled = true;
        foreach (var item in participants) if (item.Definition.Vehicle != null) item.Definition.Vehicle.ResetMultiLoadTest(this);
        Changed?.Invoke();
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
