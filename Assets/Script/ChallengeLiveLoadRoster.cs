using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Fusion;

/// <summary>Invitation-time load values. Both submitted designs use this exact immutable configuration.</summary>
public struct ChallengeLiveLoadRecord : INetworkStruct
{
    public NetworkString<_64> EntryId;
    public NetworkString<_64> DisplayName;
    // Full paths remain in reliable presentation descriptors. The invitation
    // needs only an immutable identity check against its explicit site binding.
    // Four 256-character path buffers made the parent state exceed Mono's
    // by-value argument limit, including existing Single-mode UI calls.
    public ulong VehicleIdentity0, VehicleIdentity1, VehicleIdentity2, VehicleIdentity3;
    public float WeightKg;
    public float DepartureDelay;
}

public static class ChallengeLiveLoadRoster
{
    public const int MaximumVehicles = 4;
    public static string Describe(MultiplayerChallengeState state)
    {
        var text = new StringBuilder("LIVE LOADS · " + (state.LiveLoadMode == ContractSO.VehicleTestMode.Sequential ? "SEQUENTIAL" : "TOGETHER"));
        for (int index = 0; index < state.LiveLoadCount && index < MaximumVehicles; index++)
        {
            var entry = Entry(state, index);
            string label = entry.DisplayName.ToString().Replace('<', '‹').Replace('>', '›');
            text.Append('\n').Append(index + 1).Append(". ").Append(label).Append(" — ").Append(entry.WeightKg.ToString("N0")).Append(" kg");
            if (state.LiveLoadMode == ContractSO.VehicleTestMode.Together) text.Append(" · ").Append(entry.DepartureDelay.ToString("0.#")).Append('s');
            if (state.Phase == MultiplayerChallengePhase.Testing)
                text.Append((state.LiveLoadPassedMask & (1 << index)) != 0 ? " · Passed" :
                    (state.LiveLoadFailedMask & (1 << index)) != 0 ? " · Failed" :
                    (state.LiveLoadDrivingMask & (1 << index)) != 0 ? " · Crossing" : " · Waiting");
        }
        return text.ToString();
    }
    public static ChallengeLiveLoadRecord Entry(MultiplayerChallengeState state, int index)
    {
        switch (index)
        {
            case 0: return state.LiveLoad0;
            case 1: return state.LiveLoad1;
            case 2: return state.LiveLoad2;
            case 3: return state.LiveLoad3;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static bool TryFreeze(ref MultiplayerChallengeState state, BuildLocation site, ContractSO contract, out string reason)
    {
        reason = null;
        try
        {
            var entries = new List<LiveLoadTestParticipantDefinition>();
            if (contract.HasMultipleLiveLoads)
            {
                if (!LiveLoadTestRoster.TryCreate(site, contract, out LiveLoadTestRoster roster, out reason, false)) return false;
                foreach (var participant in roster.Participants) entries.Add(participant);
            }
            else
            {
                LiveLoadVehicle vehicle = ChallengeTestDescriptor.SelectVehicle(site, contract);
                if (vehicle == null) throw new InvalidDataException("This crossing has no authored test vehicle.");
                entries.Add(new LiveLoadTestParticipantDefinition("single", vehicle.vehicleName, vehicle, vehicle.GetSessionTestWeight(), 0f));
            }
            if (entries.Count < 1 || entries.Count > MaximumVehicles) throw new InvalidDataException("A challenge requires one to four vehicles.");
            state.LiveLoadCount = entries.Count; state.LiveLoadMode = contract.vehicleTestMode;
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index]; string key = ChallengeTestDescriptor.VehiclePath(entry.Vehicle);
                if (key.Length > 256 || entry.EntryId.Length > 64 || entry.DisplayName.Length > 64 ||
                    ChallengeTestDescriptor.ResolveVehicle(key, site.gameObject.scene) != entry.Vehicle ||
                    !ChallengeTestMotionCodec.Finite(entry.WeightKg) || entry.WeightKg <= 0f || entry.WeightKg > LiveLoadTestRoster.MaxWeightKg ||
                    !ChallengeTestMotionCodec.Finite(entry.DepartureDelay) || entry.DepartureDelay < 0f || entry.DepartureDelay > LiveLoadTestRoster.MaxDepartureDelay)
                    throw new InvalidDataException("A challenge live load has an invalid identity, weight or delay.");
                var record = new ChallengeLiveLoadRecord { EntryId = entry.EntryId, DisplayName = entry.DisplayName,
                    WeightKg = entry.WeightKg, DepartureDelay = entry.DepartureDelay };
                SetVehicleIdentity(ref record, key);
                switch (index) { case 0: state.LiveLoad0 = record; break; case 1: state.LiveLoad1 = record; break;
                    case 2: state.LiveLoad2 = record; break; case 3: state.LiveLoad3 = record; break; }
            }
            state.ChallengeVehicleKey = ChallengeTestDescriptor.VehiclePath(entries[0].Vehicle);
            state.ChallengeVehicleWeight = state.LiveLoad0.WeightKg;
            return true;
        }
        catch (Exception error) { reason = error.Message; return false; }
    }

    public static LiveLoadTestRoster FromState(MultiplayerChallengeState state, BuildLocation site, ContractSO contract)
    {
        if (site == null || contract == null) throw new InvalidDataException("The frozen challenge crossing is missing.");
        int count = state.LiveLoadCount;
        if (count < 1 || count > MaximumVehicles || !Enum.IsDefined(typeof(ContractSO.VehicleTestMode), state.LiveLoadMode) ||
            (state.LiveLoadMode == ContractSO.VehicleTestMode.Single ? count != 1 : count < 2))
            throw new InvalidDataException("The invitation's live-load roster is invalid.");
        var participants = new List<LiveLoadTestParticipantDefinition>(count);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var vehicles = new HashSet<LiveLoadVehicle>();
        for (int index = 0; index < count; index++)
        {
            var record = Entry(state, index); string id = record.EntryId.ToString();
            LiveLoadVehicle vehicle = null;
            if (state.LiveLoadMode == ContractSO.VehicleTestMode.Single)
                vehicle = ChallengeTestDescriptor.ResolveVehicle(state.ChallengeVehicleKey.ToString(), site.gameObject.scene);
            else if (site.liveLoadVehicles != null)
            {
                int bindings = 0;
                foreach (ContractLiveLoadBinding binding in site.liveLoadVehicles)
                    if (binding != null && binding.entryId == id)
                    {
                        if (++bindings > 1) throw new InvalidDataException("The invitation's scene vehicle binding is duplicated.");
                        vehicle = binding.vehicle;
                    }
            }
            if (vehicle == null || vehicle.gameObject.scene != site.gameObject.scene || !vehicles.Add(vehicle) || string.IsNullOrWhiteSpace(id) || !identities.Add(id) ||
                !MatchesVehicleIdentity(record, ChallengeTestDescriptor.VehiclePath(vehicle)) ||
                !ChallengeTestMotionCodec.Finite(record.WeightKg) || record.WeightKg <= 0f || record.WeightKg > LiveLoadTestRoster.MaxWeightKg ||
                !ChallengeTestMotionCodec.Finite(record.DepartureDelay) || record.DepartureDelay < 0f || record.DepartureDelay > LiveLoadTestRoster.MaxDepartureDelay)
                throw new InvalidDataException("The invitation's vehicles do not match this scene.");
            participants.Add(new LiveLoadTestParticipantDefinition(id, record.DisplayName.ToString(), vehicle, record.WeightKg, record.DepartureDelay));
        }
        return new LiveLoadTestRoster(contract, site, state.LiveLoadMode, participants);
    }

    private static byte[] IdentityBytes(string path)
    {
        using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(Encoding.UTF8.GetBytes(path));
    }
    private static ulong IdentityPart(byte[] bytes, int offset)
    {
        ulong value = 0; for (int index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (index * 8); return value;
    }
    private static void SetVehicleIdentity(ref ChallengeLiveLoadRecord record, string path)
    {
        byte[] bytes = IdentityBytes(path);
        record.VehicleIdentity0 = IdentityPart(bytes, 0); record.VehicleIdentity1 = IdentityPart(bytes, 8);
        record.VehicleIdentity2 = IdentityPart(bytes, 16); record.VehicleIdentity3 = IdentityPart(bytes, 24);
    }
    private static bool MatchesVehicleIdentity(ChallengeLiveLoadRecord record, string path)
    {
        byte[] bytes = IdentityBytes(path);
        return record.VehicleIdentity0 == IdentityPart(bytes, 0) && record.VehicleIdentity1 == IdentityPart(bytes, 8) &&
            record.VehicleIdentity2 == IdentityPart(bytes, 16) && record.VehicleIdentity3 == IdentityPart(bytes, 24);
    }
}
