using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class CargoDropLocation : Interactable
{
    public ContractSO assignedContract;
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
        if (!isActiveAndEnabled || cargo == null || game == null || !game.IsCargoTestActive ||
            game.CurrentState != GameManager.GameState.CargoTesting || game.CurrentContract != assignedContract ||
            assignedContract == null || assignedContract.liveLoadMode != ContractSO.LiveLoadMode.PlayerCarriedCargo ||
            game.ActiveBuildLocation == null || game.ActiveBuildLocation.testCargo != cargo ||
            cargo != CargoItem.HeldCargo || cargo.Holder == null || dropSocket == null) return false;
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
}
