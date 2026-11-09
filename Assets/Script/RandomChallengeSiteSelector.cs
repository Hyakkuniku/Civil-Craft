using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Read-only, invitation-time validation and uniform selection across the loaded host world.</summary>
public static class RandomChallengeSiteSelector
{
    /// <summary>Read authored NPC assignments without applying a phase or accepting a contract.</summary>
    public static ContractSO ResolveAuthoredDefinition(BuildLocation site)
    {
        if (site == null || !site.gameObject.scene.IsValid()) return null;
        ContractSO match = null;
        foreach (NPCProgressionManager npc in Resources.FindObjectsOfTypeAll<NPCProgressionManager>())
        {
            if (npc == null || npc.gameObject.scene != site.gameObject.scene || npc.AuthoredPhases == null) continue;
            foreach (NPCProgressionPhase phase in npc.AuthoredPhases)
                if (phase != null && phase.targetBuildLocation == site && phase.contract != null)
                {
                    if (match != null && match != phase.contract) return null;
                    match = phase.contract;
                }
        }
        foreach (NPCContractGiver giver in Resources.FindObjectsOfTypeAll<NPCContractGiver>())
        {
            if (giver == null || giver.gameObject.scene != site.gameObject.scene ||
                giver.targetBuildLocation != site || giver.contractToGive == null) continue;
            if (match != null && match != giver.contractToGive) return null;
            match = giver.contractToGive;
        }
        return match;
    }

    private sealed class Context
    {
        public readonly Dictionary<string, int> Paths = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly HashSet<int> PhysicsScenes = new HashSet<int>();
        public readonly Dictionary<string, BridgeMaterialSO> Materials = ChallengeBridgeSubmissionRules.CreateMaterialCatalog(null);
        public Context()
        {
            foreach (BuildLocation site in Resources.FindObjectsOfTypeAll<BuildLocation>())
            {
                if (!site.gameObject.scene.IsValid() || !site.gameObject.scene.isLoaded ||
                    site.gameObject.scene.name != FusionConnectionManager.HostWorldSceneName) continue;
                string path = MultiplayerChallengeRules.SiteKey(site);
                Paths.TryGetValue(path, out int count); Paths[path] = count + 1;
            }
            foreach (BridgePhysicsManager manager in Resources.FindObjectsOfTypeAll<BridgePhysicsManager>())
                if (manager != null && !manager.IsSessionChallengeTest && manager.gameObject.scene.IsValid() &&
                    manager.gameObject.scene.isLoaded) PhysicsScenes.Add(manager.gameObject.scene.handle);
        }
    }

    /// <summary>Call only for an invitation/preflight, not from a per-frame UI availability check.</summary>
    public static bool IsValidSite(BuildLocation site, out string reason)
    {
        try { return IsValidSite(site, new Context(), out reason); }
        catch (Exception error) { return Reject("Challenge setup could not be checked: " + error.Message, out reason); }
    }

    public static bool TrySelect(BuildLocation[] sites, CharacterController player, out BuildLocation chosen, out string reason,
        Predicate<BuildLocation> admission = null)
    {
        try { return TrySelectChecked(sites, player, out chosen, out reason, admission); }
        catch (Exception error)
        {
            chosen = null;
            return Reject("Challenge destinations could not be checked: " + error.Message, out reason);
        }
    }

    private static bool TrySelectChecked(BuildLocation[] sites, CharacterController player, out BuildLocation chosen, out string reason,
        Predicate<BuildLocation> admission)
    {
        chosen = null;
        reason = string.Empty;
        if (player == null || !Finite(player.radius) || player.radius <= 0f || !Finite(player.height) || player.height <= 0f)
            return Reject("The host's player is not ready for challenge travel.", out reason);
        if (sites == null || sites.Length == 0) return Reject("No build locations are loaded in this map.", out reason);
        var context = new Context();
        var seen = new HashSet<BuildLocation>();
        var eligible = new List<BuildLocation>();
        int validSetup = 0;
        int admittedSetup = 0;
        foreach (BuildLocation site in sites)
        {
            if (site == null || !seen.Add(site) || !IsValidSite(site, context, out _)) continue;
            validSetup++;
            if (admission != null && !admission(site)) continue;
            admittedSetup++;
            if (!TryGetLandings(site, player, out _, out _)) continue;
            eligible.Add(site);
        }
        if (eligible.Count == 0)
            return Reject(validSetup == 0
                ? "No challenge-ready vehicle crossings are available. Check the build locations, contracts and routes."
                : admittedSetup == 0 ? "No ready crossing has been completed by both players yet."
                : "No valid crossing has clear landing space for both players right now.", out reason);
        chosen = ChooseUniformly(eligible, upper => UnityEngine.Random.Range(0, upper));
        return true;
    }

    // The random source is injected only for isolated distribution checks. Every valid
    // site gets the same probability; scene enumeration order does not weight a region.
    private static BuildLocation ChooseUniformly(IReadOnlyList<BuildLocation> eligible, Func<int, int> next)
    {
        BuildLocation chosen = null;
        for (int index = 0; index < eligible.Count; index++)
            if (next(index + 1) == 0) chosen = eligible[index];
        return chosen;
    }

    public static bool TryGetLandings(BuildLocation site, CharacterController player, out Vector3 host, out Vector3 guest)
    {
        host = guest = default;
        if (site == null || player == null ||
            !MultiplayerChallengeLobbyUI.TryResolveLanding(site, player, false, out host) ||
            !MultiplayerChallengeLobbyUI.TryResolveLanding(site, player, true, out guest)) return false;
        Vector3 scale = player.transform.lossyScale;
        float radius = player.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float separation = radius * 2f + .1f;
        return Finite(host) && Finite(guest) &&
            new Vector2(host.x - guest.x, host.z - guest.z).sqrMagnitude >= separation * separation;
    }

    private static bool IsValidSite(BuildLocation site, Context context, out string reason)
    {
        reason = string.Empty;
        if (site == null || !site.gameObject.scene.IsValid() || !site.gameObject.scene.isLoaded ||
            site.gameObject.scene.name != FusionConnectionManager.HostWorldSceneName)
            return Reject("This is not a loaded host-world crossing.", out reason);
        if (site.IsSessionChallengeLocation || (site.gameObject.hideFlags & HideFlags.DontSave) != 0)
            return Reject("Temporary challenge objects cannot become random destinations.", out reason);
        if (site.IsRedesigningBridge || site.IsRedesignBlockedByNPCTravel)
            return Reject("This crossing is being edited or used by a travelling NPC.", out reason);
        ContractSO contract = site.ChallengeContractDefinition;
        if (contract == null || string.IsNullOrWhiteSpace(contract.ContractID) || contract.ContractID.Length > 128 ||
            contract.ContractID.StartsWith(ChallengeBuildWorkspace.ContractPrefix, StringComparison.Ordinal))
            return Reject("The crossing has no stable authored contract.", out reason);
        if (contract.isTutorialContract || contract.isTimeAttack || contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle ||
            contract.winCondition != ContractSO.WinCondition.FinishLine)
            return Reject("Random challenges require a non-tutorial vehicle test with Finish Line completion.", out reason);
        if (!Finite(contract.budget) || contract.budget <= 0f ||
            !ChallengeCompetitionScoring.ValidMeasurements(contract.budget, 0f, 0f))
            return Reject("The contract's challenge budget is invalid.", out reason);
        if (contract.twoWayRoad && contract.GetWideRoadWidth() <= 0f)
            return Reject("The configured two-way road width is invalid.", out reason);
        string key = MultiplayerChallengeRules.SiteKey(site);
        if (key.Length == 0 || key.Length > 256 || !context.Paths.TryGetValue(key, out int matches) || matches != 1)
            return Reject("The crossing's scene hierarchy path is missing, duplicated or too long.", out reason);
        if (site.locationCamera == null || site.gridImage == null ||
            site.locationCamera.gameObject.scene != site.gameObject.scene || site.gridImage.gameObject.scene != site.gameObject.scene ||
            !Finite(site.locationCamera.transform.position) || !Finite(site.GetDesiredCameraPosition()))
            return Reject("The crossing needs its authored build camera and construction grid.", out reason);
        if (!site.locationCamera.gameObject.activeInHierarchy || !site.gridImage.gameObject.activeInHierarchy)
            return Reject("The crossing's camera or grid is inside an inactive scene container.", out reason);
        if (!ValidateAnchors(site, out float left, out float right, out reason)) return false;
        if (!HasMaterials(contract, context.Materials))
            return Reject("The contract needs usable allowed road and structural materials.", out reason);
        MultiplayerChallengeState frozen = default;
        if (!ChallengeLiveLoadRoster.TryFreeze(ref frozen, site, contract, out string loadReason))
            return Reject("Live-load setup: " + loadReason, out reason);
        if (frozen.LiveLoadMode == ContractSO.VehicleTestMode.Single)
        {
            LiveLoadVehicle vehicle = ChallengeTestDescriptor.ResolveVehicle(frozen.ChallengeVehicleKey.ToString(), site.gameObject.scene);
            if (!ValidateVehicleRoute(vehicle, site, context, left, right, out reason)) return false;
        }
        return true;
    }

    private static bool ValidateAnchors(BuildLocation site, out float left, out float right, out string reason)
    {
        left = float.PositiveInfinity; right = float.NegativeInfinity; reason = string.Empty;
        if (site.startingAnchors == null || site.endingAnchors == null || site.startingAnchors.Count == 0 || site.endingAnchors.Count == 0)
            return Reject("Both banks need authored anchors.", out reason);
        var seen = new HashSet<Point>();
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        foreach (List<Point> bank in new[] { site.startingAnchors, site.endingAnchors })
            foreach (Point point in bank)
            {
                if (point == null || point.gameObject.scene != site.gameObject.scene || !seen.Add(point) || !Finite(point.transform.position))
                    return Reject("An anchor is missing, duplicated, foreign or invalid.", out reason);
                Vector3 position = point.transform.position;
                left = Mathf.Min(left, position.x); right = Mathf.Max(right, position.x);
                minZ = Mathf.Min(minZ, position.z); maxZ = Mathf.Max(maxZ, position.z);
            }
        if (right - left < .01f || maxZ - minZ > 1f)
            return Reject("The anchors need a nonzero span on a shared construction plane.", out reason);
        return true;
    }

    private static bool HasMaterials(ContractSO contract, Dictionary<string, BridgeMaterialSO> catalog)
    {
        bool road = false, structure = false;
        foreach (BridgeMaterialSO material in catalog.Values)
        {
            if (material == null || contract.IsMaterialHidden(material) || material.segmentPrefab == null ||
                !Finite(material.costPerMeter) || material.costPerMeter < 0f || !Finite(material.massPerMeter) || material.massPerMeter < 0f ||
                !Finite(material.maxLength) || material.maxLength <= 0f) continue;
            bool allowed = contract.allowedMaterials == null || contract.allowedMaterials.Count == 0;
            if (!allowed) foreach (MaterialAllowance allowance in contract.allowedMaterials)
                if (allowance != null && allowance.material == material && allowance.maxPieces >= 0) { allowed = true; break; }
            if (!allowed) continue;
            if (material.isRoad) road = true;
            else structure = true;
        }
        return road && structure;
    }

    private static bool ValidateVehicleRoute(LiveLoadVehicle vehicle, BuildLocation site, Context context, float left, float right, out string reason)
    {
        reason = string.Empty;
        // The disposable challenge vehicle is enabled and bound to a new physics
        // manager. World vehicles may be disabled or bind their scene manager in Awake.
        if (vehicle == null || !context.PhysicsScenes.Contains(site.gameObject.scene.handle) ||
            vehicle.startPoint == null || vehicle.endPoint == null)
            return Reject("The test vehicle needs its authored route and scene physics manager.", out reason);
        vehicle.GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end);
        if (!Finite(start) || !Finite(end) || Mathf.Abs(end.x - start.x) < .01f ||
            !vehicle.TryGetMultiLoadStartBounds(out Bounds envelope))
            return Reject("The test vehicle needs distinct route markers and physical body colliders.", out reason);
        if (!Finite(envelope.center) || !Finite(envelope.size) || envelope.size.x <= .001f)
            return Reject("The test vehicle's physical bounds are invalid.", out reason);
        Bounds finish = envelope; finish.center += end - start;
        bool forwards = end.x > start.x;
        if (forwards ? envelope.max.x > left + .01f || finish.min.x < right - .01f :
            envelope.min.x < right - .01f || finish.max.x > left + .01f)
            return Reject("The whole test vehicle must start before and finish beyond the crossing.", out reason);
        return true;
    }

    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) &&
        Mathf.Abs(value.x) < 1000000f && Mathf.Abs(value.y) < 1000000f && Mathf.Abs(value.z) < 1000000f;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Reject(string message, out string reason) { reason = message; return false; }
}
