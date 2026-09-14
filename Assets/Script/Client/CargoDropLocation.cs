using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class CargoDropLocation : Interactable
{
    [Header("Delivery Type")]
    public ContractSO assignedContract;
    [Tooltip("Accept the assigned story cargo during normal gameplay without requiring an active contract or bridge simulation.")]
    public bool allowStoryDeliveryWithoutContract;
    [Tooltip("Exact cargo accepted by this story location. Required when contract-free delivery is enabled.")]
    public CargoItem acceptedStoryCargo;
    [Tooltip("Stable save key for this story handoff. Assign Cargo Save IDs in Edit Mode after adding the location.")]
    public string StorySaveKey => "StoryCargo:" + persistentDropLocationId;
    [SerializeField, HideInInspector] private string persistentDropLocationId;
    public string PersistentDropLocationId => persistentDropLocationId;
    [Tooltip("Position/rotation for the delivered cargo pivot. Defaults to this object.")]
    public Transform dropSocket;
    public UnityEngine.Events.UnityEvent onCargoPlaced;
    private BoxCollider zone;

    private void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (dropSocket == null) dropSocket = transform;
        zone.isTrigger = true;
        promptMessage = "Place Cargo";
    }

    public bool CanReceive(CargoItem cargo)
    {
        GameManager game = GameManager.Instance;
        if (!isActiveAndEnabled || cargo == null || cargo != CargoItem.HeldCargo ||
            cargo.Holder == null || dropSocket == null) return false;

        bool validContractDelivery = game != null && game.IsCargoTestActive &&
            game.CurrentState == GameManager.GameState.CargoTesting &&
            game.CurrentContract == assignedContract && assignedContract != null &&
            assignedContract.liveLoadMode == ContractSO.LiveLoadMode.PlayerCarriedCargo &&
            game.ActiveBuildLocation != null && game.ActiveBuildLocation.testCargo == cargo;
        bool validStoryDelivery = allowStoryDeliveryWithoutContract && assignedContract == null &&
            acceptedStoryCargo == cargo && cargo.IsStoryCargo &&
            (game == null || (!game.IsCargoTestActive && game.CurrentState == GameManager.GameState.Normal));
        if (!validContractDelivery && !validStoryDelivery) return false;

        if (zone == null) zone = GetComponent<BoxCollider>();
        // The player must actually reach the zone, not merely see its mobile interaction button.
        return zone.enabled && zone.isTrigger &&
            (zone.ClosestPoint(cargo.Holder.position) - cargo.Holder.position).sqrMagnitude <= 0.25f;
    }

    public override bool IsInteractionAvailable => base.IsInteractionAvailable && CanReceive(CargoItem.HeldCargo);

    protected override void Intract()
    {
        CargoItem cargo = CargoItem.HeldCargo;
        if (!CanReceive(cargo)) return;
        if (allowStoryDeliveryWithoutContract && assignedContract == null)
        {
            if (cargo.PlaceAtStoryDropLocation(this)) onCargoPlaced?.Invoke();
            return;
        }

        Collider playerCollider = cargo.Holder.GetComponent<CharacterController>();
        if (playerCollider == null) playerCollider = cargo.Holder.GetComponent<Collider>();
        if (GameManager.Instance.TryCompleteCargoTest(assignedContract, playerCollider, this))
            onCargoPlaced?.Invoke();
    }

    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        GetComponent<BoxCollider>().size = new Vector3(3f, 3f, 3f);
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0) gameObject.layer = layer;
        dropSocket = transform;
        promptMessage = "Place Cargo";
    }

    public bool IsAvailableStoryDestinationFor(CargoItem cargo)
    {
        return isActiveAndEnabled && allowStoryDeliveryWithoutContract && assignedContract == null &&
            acceptedStoryCargo == cargo && cargo != null && cargo.IsStoryCargo &&
            cargo.DeliveredLocation != this && dropSocket != null;
    }
}
