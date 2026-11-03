using System.Collections.Generic;
using UnityEngine;

public partial class LiveLoadVehicle
{
    private BuildLocation multiLoadBindingOwner;
    private string multiLoadEntryId;
    private LiveLoadTestSession multiLoadSession;
    private float multiLoadFrozenWeight;
    private Vector3 multiLoadOriginalPosition;
    private Quaternion multiLoadOriginalRotation;
    private bool multiLoadOriginalParked;
    private float remoteMultiplayerWeight;
    private readonly List<Collider> multiLoadParkedColliders = new List<Collider>();
    private readonly List<bool> multiLoadParkedColliderStates = new List<bool>();
    private bool multiLoadParkingCaptured;
    private Collider[] multiLoadEnvelopeColliders;

    public void SetRemoteMultiplayerWeight(float weight)
    {
        remoteMultiplayerWeight = !float.IsNaN(weight) && !float.IsInfinity(weight) && weight > 0f ? weight : 0f;
        RefreshCargoMass();
    }

    public void BindMultiLoadEntry(BuildLocation owner, string entryId)
    {
        multiLoadBindingOwner = owner; multiLoadEntryId = entryId;
    }
    private ContractLiveLoadEntry ConfiguredMultiLoadEntry
    {
        get
        {
            if (assignedContract == null || !assignedContract.HasMultipleLiveLoads || multiLoadBindingOwner == null ||
                multiLoadBindingOwner.activeContract == null || multiLoadBindingOwner.activeContract.ContractID != assignedContract.ContractID ||
                assignedContract.liveLoadTests == null) return null;
            foreach (var entry in assignedContract.liveLoadTests) if (entry != null && entry.entryId == multiLoadEntryId) return entry;
            return null;
        }
    }
    public float GetConfiguredEmptyWeight() => assignedContract != null && assignedContract.HasMultipleLiveLoads
        ? (ConfiguredMultiLoadEntry != null ? ConfiguredMultiLoadEntry.emptyWeightKg : 0f)
        : assignedContract != null ? assignedContract.liveLoadWeight : vehicleMass;
    public int RequiredLoadedCargoCount => assignedContract != null && assignedContract.HasMultipleLiveLoads
        ? (ConfiguredMultiLoadEntry != null ? ConfiguredMultiLoadEntry.minimumLoadedCargo : 0)
        : assignedContract != null ? assignedContract.minimumLoadedCargo : 0;
    private bool ConfiguredAllowsCargo => assignedContract != null && assignedContract.HasMultipleLiveLoads
        ? ConfiguredMultiLoadEntry != null && ConfiguredMultiLoadEntry.allowCargo : assignedContract != null && assignedContract.allowVehicleCargo;

    public float GetPayloadWeightForTest()
    {
        float payload = 0f;
        foreach (VehicleCargoSlot slot in GetComponentsInChildren<VehicleCargoSlot>(true))
        {
            CargoItem cargo = slot.LoadedCargo;
            if (cargo == null && !AllowsCargo && slot.acceptedCargo != null && slot.acceptedCargo.transform.IsChildOf(slot.transform)) cargo = slot.acceptedCargo;
            if (cargo != null && cargo.gameObject.activeSelf && cargo.transform.IsChildOf(transform)) payload += Mathf.Max(0f, cargo.cargoWeight);
        }
        return payload;
    }
    public void GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end)
    {
        // In Edit Mode Awake has not captured the imported-root waypoint offset.
        if (!Application.isPlaying)
        {
            Vector3 offset = startPoint != null ? transform.position - startPoint.position : Vector3.zero;
            start = transform.position; end = endPoint != null ? endPoint.position + offset : transform.position;
            return;
        }
        GetWaypointPose(startPoint, out start, out _); GetWaypointPose(endPoint, out end, out _);
    }
    public bool TryGetMultiLoadStartBounds(out Bounds bounds)
    {
        if (!TryGetMultiLoadEnvelopeBounds(out bounds)) return false;
        GetMultiLoadRoutePoses(out Vector3 start, out _);
        bounds.center += start - transform.position;
        return true;
    }

    // Collider.bounds is empty for an inactive GameObject. Completed-contract
    // vehicles are hidden, so invitation preflight must measure authored shapes
    // without temporarily activating the truck or replaying its Awake/story hooks.
    public bool TryGetMultiLoadEnvelopeBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.zero);
        bool found = false;
        // Clearance/overlap uses the complete solid chassis, including overhangs.
        // The existing wheel-contact envelope remains separate for load transfer.
        Collider[] colliders = Application.isPlaying && multiLoadSession != null && multiLoadEnvelopeColliders != null
            ? multiLoadEnvelopeColliders
            : GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;
            bool available = true;
            for (Transform child = collider.transform; child != null && child != transform; child = child.parent)
                if (!child.gameObject.activeSelf) { available = false; break; }
            if (available && TryGetAuthoredColliderBounds(collider, out Bounds current)) AddAuthoredBounds(ref bounds, ref found, current);
        }
        return found;
    }

    private static void AddAuthoredBounds(ref Bounds total, ref bool found, Bounds current)
    {
        if (!found) { total = current; found = true; } else total.Encapsulate(current);
    }

    private static bool TryGetAuthoredColliderBounds(Collider collider, out Bounds bounds)
    {
        bounds = default;
        if (collider == null || !collider.enabled || collider.isTrigger) return false;
        Transform shape = collider.transform;
        Bounds local;
        if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
        else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
        else if (collider is SphereCollider sphere)
        {
            Vector3 scale = shape.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            bounds = new Bounds(shape.TransformPoint(sphere.center), Vector3.one * (radius * 2f));
            return true;
        }
        else if (collider is CapsuleCollider capsule)
        {
            Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 first = capsule.direction == 0 ? Vector3.up : Vector3.right;
            Vector3 second = capsule.direction == 2 ? Vector3.up : Vector3.forward;
            Vector3 worldAxis = shape.TransformVector(axis);
            float radius = capsule.radius * Mathf.Max(shape.TransformVector(first).magnitude, shape.TransformVector(second).magnitude);
            float halfLine = Mathf.Max(0f, capsule.height * worldAxis.magnitude * .5f - radius);
            Vector3 line = worldAxis.normalized * halfLine;
            Vector3 extents = new Vector3(Mathf.Abs(line.x) + radius, Mathf.Abs(line.y) + radius, Mathf.Abs(line.z) + radius);
            bounds = new Bounds(shape.TransformPoint(capsule.center), extents * 2f);
            return true;
        }
        else return false;
        Vector3 min = local.min, max = local.max;
        bounds = new Bounds(shape.TransformPoint(min), Vector3.zero);
        for (int corner = 1; corner < 8; corner++)
            bounds.Encapsulate(shape.TransformPoint(new Vector3((corner & 1) == 0 ? min.x : max.x,
                (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z)));
        return true;
    }
    public void PrepareMultiLoadTest(LiveLoadTestSession owner, float weight)
    {
        if (multiLoadSession == owner) return;
        if (owner == null || !owner.Contains(this) || float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f)
            throw new System.ArgumentException("Invalid live load participant.");
        if (physicsManager != owner.Physics)
            throw new System.InvalidOperationException("Every test vehicle must use the active Bridge Physics Manager.");
        if (multiLoadSession != null) throw new System.InvalidOperationException("Vehicle is already in another live load test.");
        multiLoadParkingCaptured = false; multiLoadParkedColliders.Clear(); multiLoadParkedColliderStates.Clear();
        multiLoadOriginalPosition = transform.position; multiLoadOriginalRotation = transform.rotation; multiLoadOriginalParked = isParkedAtFinish;
        multiLoadSession = owner; multiLoadFrozenWeight = weight;
        multiLoadEnvelopeColliders = GetComponentsInChildren<Collider>(true);
    }
    public void ReleaseForMultiLoadTest(LiveLoadTestSession owner)
    {
        if (multiLoadSession != owner || owner == null || !owner.Contains(this)) return;
        StartDrivingForSimulation();
    }
    public void ResetMultiLoadTest(LiveLoadTestSession owner)
    {
        if (multiLoadSession != owner) return;
        // A passed vehicle must reset too; legacy StopAndReset deliberately keeps
        // a completed single vehicle parked until its bridge is saved.
        RestoreMultiLoadParkingColliders();
        isParkedAtFinish = false; StopAndReset();
        if (rb != null) ApplyVehiclePose(multiLoadOriginalPosition, multiLoadOriginalRotation);
        else transform.SetPositionAndRotation(multiLoadOriginalPosition, multiLoadOriginalRotation);
        isParkedAtFinish = multiLoadOriginalParked; hasReachedEnd = multiLoadOriginalParked;
        participatingInCurrentSimulation = false; multiLoadSession = null; multiLoadFrozenWeight = 0f; multiLoadEnvelopeColliders = null;
        RefreshCargoMass();
    }

    public void ParkForMultiLoadTest(LiveLoadTestSession owner)
    {
        if (multiLoadSession != owner || owner == null) return;
        StopAndFreezeForWin(); StopSimulationAudio();
        isDriving = false; isBrakingAtFinish = false; isParkedAtFinish = hasReachedEnd = true;
        if (rb != null) { ClearDynamicVelocity(rb); rb.isKinematic = true; }
        foreach (var wheel in wheels)
            if (wheel.rb != null) { ClearDynamicVelocity(wheel.rb); wheel.rb.isKinematic = true; }
        // Completed cars may share a destination marker. Their parked visuals
        // remain visible, but their solid bodies must not block the next truck.
        // Keep interaction/finish triggers untouched, and restore exact enabled
        // flags on cancellation before the generated wheel rig is dismantled.
        if (!multiLoadParkingCaptured)
        {
            multiLoadParkingCaptured = true;
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger) continue;
                multiLoadParkedColliders.Add(collider); multiLoadParkedColliderStates.Add(collider.enabled);
                collider.enabled = false;
            }
        }
    }

    private void RestoreMultiLoadParkingColliders()
    {
        for (int index = 0; index < multiLoadParkedColliders.Count; index++)
            if (multiLoadParkedColliders[index] != null) multiLoadParkedColliders[index].enabled = multiLoadParkedColliderStates[index];
        multiLoadParkedColliders.Clear(); multiLoadParkedColliderStates.Clear(); multiLoadParkingCaptured = false;
    }
}
