using UnityEngine;
using System.Collections;

/// <summary>An authored interaction point in the truck bed. One item per slot.</summary>
[RequireComponent(typeof(BoxCollider))]
public class VehicleCargoSlot : Interactable
{
    public LiveLoadVehicle vehicle;
    [Tooltip("Cargo pivot is placed here. Must be a child of this vehicle. Defaults to this slot.")]
    public Transform cargoSocket;
    [Tooltip("Optional: only this cargo can occupy this slot. Empty accepts any carried CargoItem.")]
    public CargoItem acceptedCargo;
    [SerializeField, HideInInspector] private string persistentSlotId;
    public string PersistentSlotId => persistentSlotId;
    public CargoItem LoadedCargo { get; private set; }
    public float LoadedWeight => LoadedCargo != null && LoadedCargo.gameObject.activeInHierarchy
        ? Mathf.Max(0f, LoadedCargo.cargoWeight) : 0f;

    public bool CanAccept(CargoItem cargo)
    {
        if (cargo == null || LoadedCargo != null || vehicle == null || !vehicle.CanChangeCargo ||
            cargoSocket == null || !cargoSocket.IsChildOf(vehicle.transform)) return false;
        if (PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.GetLoadedVehicleCargo(persistentSlotId) != null) return false;
        return cargo.IsProgressionInteractionUnlocked && cargo.CanLoadIntoVehicle &&
               cargo.playerCargoContract == vehicle.assignedContract &&
               (acceptedCargo == null || acceptedCargo == cargo);
    }

    public bool TryLoadCargo(CargoItem cargo)
    {
        if (!CanAccept(cargo) || !cargo.MountInVehicle(this, cargoSocket)) return false;
        LoadedCargo = cargo;
        vehicle.NotifyCargoLoaded();
        return true;
    }

    private void Awake()
    {
        if (vehicle == null) vehicle = GetComponentInParent<LiveLoadVehicle>();
        if (cargoSocket == null) cargoSocket = transform;
        GetComponent<BoxCollider>().isTrigger = true;
        if (vehicle != null) vehicle.RegisterCargoSlot(this);
    }

    private IEnumerator Start()
    {
        while (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null)
            yield return null;
        LoadedVehicleCargoData record = PlayerDataManager.Instance.GetLoadedVehicleCargo(persistentSlotId);
        if (record == null) yield break;
        CargoItem match = null;
        foreach (CargoItem cargo in Resources.FindObjectsOfTypeAll<CargoItem>())
        {
            if (!cargo.gameObject.scene.IsValid() || cargo.PersistentCargoId != record.cargoId) continue;
            if (match != null)
            {
                Debug.LogError("[Vehicle Cargo] Duplicate cargo save IDs. Restore cancelled; the save record was kept.", this);
                yield break;
            }
            match = cargo;
        }
        if (match == null || vehicle == null || cargoSocket == null || !cargoSocket.IsChildOf(vehicle.transform))
        {
            Debug.LogWarning("[Vehicle Cargo] Cannot restore this slot: cargo or truck/socket is missing. Its save record was kept.", this);
            yield break;
        }
        if (match.RestoreVehicleLoad(this, cargoSocket, record.weight))
        {
            LoadedCargo = match;
            vehicle.RefreshCargoMass();
        }
    }

    public override bool IsInteractionAvailable
    {
        get
        {
            if (vehicle != null && vehicle.UsesSmartCargoLoadingZone) return false;
            if (!base.IsInteractionAvailable || LoadedCargo != null || vehicle == null || !vehicle.CanChangeCargo ||
                cargoSocket == null || !cargoSocket.IsChildOf(vehicle.transform)) return false;
            CargoItem held = CargoItem.HeldCargo;
            promptMessage = "Load Cargo";
            return CanAccept(held);
        }
    }

    protected override void Intract()
    {
        if (!IsInteractionAvailable) return;
        TryLoadCargo(CargoItem.HeldCargo);
    }

    private void Reset()
    {
        vehicle = GetComponentInParent<LiveLoadVehicle>();
        cargoSocket = transform;
        GetComponent<BoxCollider>().isTrigger = true;
        GetComponent<BoxCollider>().size = Vector3.one;
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0) gameObject.layer = layer;
        else if (vehicle != null) gameObject.layer = vehicle.gameObject.layer;
        promptMessage = "Load Cargo";
    }
}
