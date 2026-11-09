using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>An immutable attempt/invitation snapshot, never written into contract assets or player saves.</summary>
public sealed class LiveLoadTestParticipantDefinition
{
    public string EntryId { get; }
    public string DisplayName { get; }
    public LiveLoadVehicle Vehicle { get; }
    public float WeightKg { get; }
    public float DepartureDelay { get; }
    public LiveLoadTestParticipantDefinition(string entryId, string displayName, LiveLoadVehicle vehicle, float weightKg, float departureDelay)
    {
        EntryId = entryId; DisplayName = displayName; Vehicle = vehicle; WeightKg = weightKg; DepartureDelay = departureDelay;
    }
}

public sealed class LiveLoadTestRoster
{
    public const int MaxVehicles = 4;
    public const float MaxWeightKg = 1000000000f;
    public const float MaxDepartureDelay = 86400f;
    public ContractSO Contract { get; }
    public BuildLocation Location { get; }
    public ContractSO.VehicleTestMode Mode { get; }
    public IReadOnlyList<LiveLoadTestParticipantDefinition> Participants { get; }
    public LiveLoadTestRoster(ContractSO contract, BuildLocation location, ContractSO.VehicleTestMode mode,
        IEnumerable<LiveLoadTestParticipantDefinition> participants)
    {
        if (contract == null || location == null || participants == null) throw new ArgumentException("A live load roster needs its exact contract and location.");
        if (mode != ContractSO.VehicleTestMode.Single && mode != ContractSO.VehicleTestMode.Sequential && mode != ContractSO.VehicleTestMode.Together)
            throw new ArgumentException("Unknown live load test mode.");
        var entries = new List<LiveLoadTestParticipantDefinition>(participants);
        if ((mode == ContractSO.VehicleTestMode.Single ? entries.Count != 1 : entries.Count < 2) || entries.Count > MaxVehicles)
            throw new ArgumentException("A multiple-load test requires 2–4 vehicles.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var vehicles = new HashSet<LiveLoadVehicle>();
        foreach (var entry in entries)
            if (entry == null || string.IsNullOrWhiteSpace(entry.EntryId) || !ids.Add(entry.EntryId) || entry.Vehicle == null ||
                !vehicles.Add(entry.Vehicle) || !Positive(entry.WeightKg) || !Finite(entry.DepartureDelay) || entry.DepartureDelay < 0f || entry.DepartureDelay > MaxDepartureDelay)
                throw new ArgumentException("Invalid or duplicate live load entry, vehicle, weight or departure delay.");
        Contract = contract; Location = location; Mode = mode;
        Participants = new ReadOnlyCollection<LiveLoadTestParticipantDefinition>(entries);
    }

    public LiveLoadTestRoster WithVehicles(BuildLocation location, ContractSO contract, IReadOnlyList<LiveLoadVehicle> replacements)
    {
        if (replacements == null || replacements.Count != Participants.Count) throw new ArgumentException("Frozen roster vehicle count changed.");
        var result = new List<LiveLoadTestParticipantDefinition>(Participants.Count);
        for (int index = 0; index < Participants.Count; index++)
        {
            var item = Participants[index];
            result.Add(new LiveLoadTestParticipantDefinition(item.EntryId, item.DisplayName, replacements[index], item.WeightKg, item.DepartureDelay));
        }
        return new LiveLoadTestRoster(contract, location, Mode, result);
    }

    /// <summary>Allow every frozen participant its full crossing time, including scheduled departures.</summary>
    public float GetCompetitionTimeLimit(float perVehicleSeconds)
    {
        if (!Finite(perVehicleSeconds) || perVehicleSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(perVehicleSeconds));
        if (Mode == ContractSO.VehicleTestMode.Single) return perVehicleSeconds;
        if (Mode == ContractSO.VehicleTestMode.Sequential)
            return (float)Math.Min(float.MaxValue, (double)perVehicleSeconds * Participants.Count);
        float lastDeparture = 0f;
        foreach (LiveLoadTestParticipantDefinition participant in Participants)
            lastDeparture = Math.Max(lastDeparture, participant.DepartureDelay);
        return (float)Math.Min(float.MaxValue, (double)perVehicleSeconds + lastDeparture);
    }

    public static bool TryCreate(BuildLocation location, ContractSO contract, out LiveLoadTestRoster roster, out string reason, bool requireCargo = true)
    {
        roster = null; reason = string.Empty;
        if (location == null || contract == null || (location.activeContract != contract &&
            (requireCargo || location.ChallengeContractDefinition != contract)))
            return Error("Select the contract's exact build location.", out reason);
        if (!contract.HasMultipleLiveLoads) return Error("This contract uses the existing Single live load mode.", out reason);
        if (contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle || contract.winCondition != ContractSO.WinCondition.FinishLine)
            return Error("Multiple live loads require Vehicle mode and Finish Line completion.", out reason);
        if (contract.vehicleTestMode != ContractSO.VehicleTestMode.Sequential && contract.vehicleTestMode != ContractSO.VehicleTestMode.Together)
            return Error("Unknown live load test mode.", out reason);
        if (contract.liveLoadTests == null || contract.liveLoadTests.Count < 2 || contract.liveLoadTests.Count > MaxVehicles)
            return Error("Configure between 2 and 4 live load entries.", out reason);
        if (!TryGetSiteSpan(location, out float minX, out float maxX)) return Error("The build location needs valid start and end anchors.", out reason);
        var entries = new List<LiveLoadTestParticipantDefinition>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var vehicles = new HashSet<LiveLoadVehicle>(); var starts = new List<Bounds>();
        BridgePhysicsManager boundPhysics = null;
        foreach (ContractLiveLoadEntry entry in contract.liveLoadTests)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.entryId) || !ids.Add(entry.entryId)) return Error("Every live load needs a unique permanent entry ID.", out reason);
            string label = string.IsNullOrWhiteSpace(entry.displayName) ? "Vehicle " + (entries.Count + 1) : entry.displayName.Trim();
            if (!Positive(entry.emptyWeightKg) || !Finite(entry.departureDelay) || entry.departureDelay < 0f || entry.departureDelay > MaxDepartureDelay || entry.minimumLoadedCargo < 0 ||
                (!entry.allowCargo && entry.minimumLoadedCargo != 0)) return Error(label + ": enter a positive empty weight, nonnegative delay and valid cargo requirement.", out reason);
            ContractLiveLoadBinding match = null; int count = 0;
            if (location.liveLoadVehicles != null) foreach (var binding in location.liveLoadVehicles)
                if (binding != null && binding.entryId == entry.entryId) { match = binding; count++; }
            if (count != 1 || match.vehicle == null || !vehicles.Add(match.vehicle)) return Error(label + ": bind exactly one distinct scene vehicle.", out reason);
            LiveLoadVehicle vehicle = match.vehicle;
            if (vehicle.gameObject.scene != location.gameObject.scene || vehicle.assignedContract == null ||
                vehicle.assignedContract.ContractID != contract.ContractID || !vehicle.enabled ||
                (requireCargo && !vehicle.gameObject.activeInHierarchy)) return Error(label + ": its vehicle must belong to this contract and scene and be available.", out reason);
            if (vehicle.physicsManager == null || vehicle.physicsManager.gameObject.scene != location.gameObject.scene ||
                (boundPhysics != null && vehicle.physicsManager != boundPhysics))
                return Error(label + ": assign the same scene Bridge Physics Manager to every test vehicle.", out reason);
            if (requireCargo && (!vehicle.physicsManager.enabled || !vehicle.physicsManager.gameObject.activeInHierarchy))
                return Error(label + ": enable its Bridge Physics Manager before starting the test.", out reason);
            boundPhysics = vehicle.physicsManager;
            vehicle.GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end);
            if (vehicle.startPoint == null || vehicle.endPoint == null || Mathf.Abs(end.x - start.x) < 0.01f)
                return Error(label + ": assign distinct Start and End route markers.", out reason);
            if (!vehicle.TryGetMultiLoadStartBounds(out Bounds startBounds)) return Error(label + ": the vehicle needs physical non-trigger colliders.", out reason);
            bool forwards = end.x > start.x;
            Bounds endBounds = startBounds; endBounds.center += end - start;
            if (forwards ? startBounds.max.x > minX + 0.01f || endBounds.min.x < maxX - 0.01f :
                startBounds.min.x < maxX - 0.01f || endBounds.max.x > minX + 0.01f)
                return Error(label + ": move route markers so the whole vehicle starts before and finishes beyond the bridge.", out reason);
            foreach (Bounds other in starts) if (startBounds.Intersects(other)) return Error(label + ": vehicle starting positions overlap. Space the vehicles apart.", out reason);
            starts.Add(startBounds);
            if (requireCargo && entry.allowCargo && vehicle.LoadedCargoCount < entry.minimumLoadedCargo)
                return Error(label + ": load cargo before testing (" + vehicle.LoadedCargoCount + "/" + entry.minimumLoadedCargo + ").", out reason);
            float payload = vehicle.GetPayloadWeightForTest();
            if (!Finite(payload) || payload < 0f || !Positive(entry.emptyWeightKg + payload)) return Error(label + ": invalid loaded cargo weight.", out reason);
            entries.Add(new LiveLoadTestParticipantDefinition(entry.entryId, label, vehicle, entry.emptyWeightKg + payload, entry.departureDelay));
        }
        if (location.liveLoadVehicles != null) foreach (var binding in location.liveLoadVehicles)
            if (binding != null && !ids.Contains(binding.entryId ?? string.Empty)) return Error("Remove stale scene vehicle bindings that are not in this contract.", out reason);
        try { roster = new LiveLoadTestRoster(contract, location, contract.vehicleTestMode, entries); return true; }
        catch (ArgumentException error) { reason = error.Message; return false; }
    }

    public static bool TryGetSiteSpan(BuildLocation location, out float minX, out float maxX)
    {
        minX = float.PositiveInfinity; maxX = float.NegativeInfinity;
        if (location == null || location.startingAnchors == null || location.endingAnchors == null || location.startingAnchors.Count == 0 || location.endingAnchors.Count == 0) return false;
        foreach (Point point in location.startingAnchors) if (point != null) { minX = Mathf.Min(minX, point.transform.position.x); maxX = Mathf.Max(maxX, point.transform.position.x); }
        foreach (Point point in location.endingAnchors) if (point != null) { minX = Mathf.Min(minX, point.transform.position.x); maxX = Mathf.Max(maxX, point.transform.position.x); }
        return Finite(minX) && Finite(maxX) && maxX - minX > 0.01f;
    }
    private static bool Positive(float value) => Finite(value) && value > 0f && value <= MaxWeightKg;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Error(string message, out string reason) { reason = message; return false; }
}
