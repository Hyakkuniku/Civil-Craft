using UnityEngine;

/// <summary>
/// One comfortable interaction area for a cargo truck. The authored cargo
/// slots remain responsible for placement and persistence.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class VehicleCargoLoadingZone : Interactable
{
    private LiveLoadVehicle vehicle;
    private BoxCollider zone;
    private BridgeSelectionOutline vehicleOutline;

    public void Initialize(LiveLoadVehicle owner)
    {
        vehicle = owner;
        zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;
        promptMessage = "LOAD CARGO";
    }

    public override bool IsInteractionAvailable
    {
        get
        {
            CargoItem cargo = CargoItem.HeldCargo;
            if (!base.IsInteractionAvailable || vehicle == null || cargo == null ||
                !vehicle.TryGetCompatibleCargoSlot(cargo, out _)) return false;

            int required = vehicle.assignedContract != null
                ? vehicle.assignedContract.minimumLoadedCargo
                : 0;
            promptMessage = required > 0
                ? $"LOAD CARGO  {vehicle.LoadedCargoCount}/{required}"
                : "LOAD CARGO";
            return true;
        }
    }

    protected override void Intract()
    {
        if (!IsInteractionAvailable) return;
        vehicle.TryLoadCargoFromSmartZone(CargoItem.HeldCargo);
    }

    private void LateUpdate()
    {
        // Guide the player to the destination immediately after pickup. The
        // interaction button itself still appears only when PlayerInteract is
        // within range of this zone's collider.
        bool show = IsInteractionAvailable;
        if (show && vehicleOutline == null)
            vehicleOutline = new BridgeSelectionOutline(vehicle.transform);
        vehicleOutline?.SetVisualOnlyVisible(show);
    }

    private void OnDisable() => DisposeOutline();
    private void OnDestroy() => DisposeOutline();

    private void DisposeOutline()
    {
        if (vehicleOutline == null) return;
        vehicleOutline.Dispose();
        vehicleOutline = null;
    }
}
