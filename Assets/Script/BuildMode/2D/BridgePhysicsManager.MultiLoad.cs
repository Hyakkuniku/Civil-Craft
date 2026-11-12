using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BridgePhysicsManager
{
    public LiveLoadTestSession MultiLoadSession { get; private set; }
    public string MultiLoadSetupError { get; private set; }
    private LiveLoadTestRoster sessionTestRoster;
    public LiveLoadTestRoster SessionTestRoster => sessionTestRoster;
    private DeterministicBridgeStressSolver.PreparedAnalysis multiLoadPreparedStress;
    private readonly List<DeterministicBridgeStressSolver.PointLoad> multiLoadStressInputs =
        new List<DeterministicBridgeStressSolver.PointLoad>(4);
    private readonly List<DeterministicBridgeStressSolver.PointLoad> previousMultiLoadStressInputs =
        new List<DeterministicBridgeStressSolver.PointLoad>(4);
    private bool multiLoadStressInputsValid;
    private DeterministicBridgeStressSolver.EnvironmentalLoad previousEnvironmentalLoad;
    private float singleVehicleTestMass;

    /// <summary>Preflights the immutable roster before releasing any bridge or vehicle physics.</summary>
    public bool TryPrepareMultiLoadSession(BuildLocation location, out string reason)
    {
        reason = null;
        MultiLoadSetupError = null;
        if (IsSimulationActive)
        { reason = "Stop the current simulation before preparing another live-load roster."; return false; }
        ContractSO contract = SimulationContract;
        if (contract == null || !contract.HasMultipleLiveLoads)
        {
            CancelMultiLoadSession();
            return true;
        }
        LiveLoadTestRoster roster = sessionTestRoster;
        if (roster == null && !LiveLoadTestRoster.TryCreate(location, contract, out roster, out reason, true))
        {
            MultiLoadSetupError = reason;
            return false;
        }
        CancelMultiLoadSession();
        try
        {
            MultiLoadSession = new LiveLoadTestSession(this, roster);
            MultiLoadSession.Prepare();
            return true;
        }
        catch (Exception error)
        {
            CancelMultiLoadSession();
            reason = MultiLoadSetupError = "Could not prepare the live-load roster: " + error.Message;
            return false;
        }
    }

    internal void ConfigureSessionMultiLoadTest(BuildLocation location, LiveLoadTestRoster roster)
    {
        if (location == null || !location.IsSessionChallengeLocation || roster == null || IsSimulationActive ||
            roster.Location != location || roster.Contract != location.activeContract || !roster.Contract.HasMultipleLiveLoads)
            throw new InvalidOperationException("Invalid isolated multiple-live-load challenge context.");
        sessionTestLocation = location;
        sessionTestRoster = roster;
        sessionTestVehicle = roster.Participants.Count > 0 ? roster.Participants[0].Vehicle : null;
    }

    public bool TryGetRoadSpan(out float minX, out float maxX)
    {
        bool valid = TryResolveRoadSpan();
        minX = deterministicRoadMinX; maxX = deterministicRoadMaxX;
        return valid;
    }

    private void CancelMultiLoadSession()
    {
        LiveLoadTestSession session = MultiLoadSession;
        MultiLoadSession = null;
        session?.Cancel();
        multiLoadPreparedStress = null;
        multiLoadStressInputsValid = false;
        multiLoadStressInputs.Clear();
        previousMultiLoadStressInputs.Clear();
    }

    private void OnDisable()
    {
        DisposeSimulationPlayback();
        if (IsSimulationActive) StopPhysicsAndReset();
        ClearEnvironmentalTest();
        CancelMultiLoadSession();
        RestoreGlobalPhysicsSettings();
    }

    // One ordered vector of simultaneous physical load contributions. No per-frame scene lookups.
    private bool UpdateCombinedStress(float singleProgress, float singleContact)
    {
        multiLoadStressInputs.Clear();
        if (MultiLoadSession != null)
        {
            for (int index = 0; index < MultiLoadSession.Participants.Count; index++)
            {
                LiveLoadTestParticipant participant = MultiLoadSession.Participants[index];
                // Passed vehicles can still brake/roll, and stopped failed vehicles
                // can remain on the deck. Their physical weight cannot disappear.
                if (participant.State == LiveLoadTestParticipantState.Waiting) continue;
                LiveLoadVehicle vehicle = participant.Definition.Vehicle;
                if (vehicle == null || !TryGetVehicleRoadState(vehicle, out float progress, out float fraction) || fraction <= 0f)
                    continue;
                if (fraction > .01f) HasLiveLoadEngagedThisRun = true;
                AddCombinedLiveLoad(progress, participant.Definition.WeightKg * fraction);
                if (environmentalTest != null) ApplyEnvironmentalVehicleAcceleration(vehicle, fraction);
            }
        }
        else if (singleContact > 0f)
        {
            AddCombinedLiveLoad(singleProgress, singleVehicleTestMass * singleContact);
            ApplyEnvironmentalVehicleAcceleration(deterministicLiveLoadVehicle, singleContact);
        }

        bool changed = !multiLoadStressInputsValid || multiLoadStressInputs.Count != previousMultiLoadStressInputs.Count ||
            environmentalLoad.WindPressure != previousEnvironmentalLoad.WindPressure ||
            environmentalLoad.AccelerationX != previousEnvironmentalLoad.AccelerationX ||
            environmentalLoad.AccelerationY != previousEnvironmentalLoad.AccelerationY;
        if (!changed)
            for (int index = 0; index < multiLoadStressInputs.Count; index++)
                if (multiLoadStressInputs[index].WorldX != previousMultiLoadStressInputs[index].WorldX ||
                    multiLoadStressInputs[index].WeightKg != previousMultiLoadStressInputs[index].WeightKg)
                { changed = true; break; }
        if (!changed) return false;
        previousMultiLoadStressInputs.Clear(); previousMultiLoadStressInputs.AddRange(multiLoadStressInputs);
        previousEnvironmentalLoad = environmentalLoad;
        multiLoadStressInputsValid = true;
        if (multiLoadPreparedStress != null)
        {
            deterministicStressResult = multiLoadPreparedStress.Evaluate(multiLoadStressInputs, environmentalLoad);
            deterministicStructureStable = deterministicStressResult != null && deterministicStressResult.IsStructurallyStable;
        }
        return true;
    }

    private void AddCombinedLiveLoad(float progress, float supportedMass)
    {
        // Match the solver's millimetre/gram precision when checking for changes.
        float x = Mathf.Round(Mathf.Lerp(deterministicRoadMinX, deterministicRoadMaxX, progress) * 1000f) / 1000f;
        float kg = Mathf.Round(supportedMass * 1000f) / 1000f;
        if (kg > 0f) multiLoadStressInputs.Add(new DeterministicBridgeStressSolver.PointLoad(x, kg));
    }
}
