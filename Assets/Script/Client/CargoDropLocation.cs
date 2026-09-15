using UnityEngine;
using TMPro;
using UnityEngine.UI;

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
        if (!IsDestinationForHeldCargo(cargo)) return false;
        if (zone == null) zone = GetComponent<BoxCollider>();
        // The player must actually reach the zone, not merely see its mobile interaction button.
        return zone.enabled && zone.isTrigger &&
            (zone.ClosestPoint(cargo.Holder.position) - cargo.Holder.position).sqrMagnitude <= 0.25f;
    }

    public bool IsDestinationForHeldCargo(CargoItem cargo)
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
        return validContractDelivery || validStoryDelivery;
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

    private GameObject highlightVisual;
    private RectTransform footprint, caption;
    private CanvasGroup highlightPulse;
    private TMP_Text highlightLabel;
    private Camera highlightCamera;

    private void LateUpdate()
    {
        bool show = zone != null && zone.enabled && zone.isTrigger && Time.timeScale > 0f &&
            IsDestinationForHeldCargo(CargoItem.HeldCargo);
        if (!show) { if (highlightVisual != null) highlightVisual.SetActive(false); return; }
        if (highlightCamera == null || !highlightCamera.isActiveAndEnabled) highlightCamera = Camera.main;
        if (highlightCamera == null) { if (highlightVisual != null) highlightVisual.SetActive(false); return; }
        if (highlightVisual == null) CreateHighlight();
        highlightVisual.SetActive(true);
        Vector3 center = zone.transform.TransformPoint(zone.center);
        center.y = dropSocket.position.y + 0.06f;
        footprint.position = center;
        footprint.rotation = Quaternion.Euler(90f, zone.transform.eulerAngles.y, 0f);
        Vector3 scale = zone.transform.lossyScale;
        footprint.sizeDelta = new Vector2(Mathf.Max(0.5f, Mathf.Abs(zone.size.x * scale.x)),
            Mathf.Max(0.5f, Mathf.Abs(zone.size.z * scale.z))) * 100f;
        caption.position = center + Vector3.up * 1.8f;
        caption.rotation = highlightCamera.transform.rotation;
        bool inReach = CanReceive(CargoItem.HeldCargo);
        highlightLabel.text = inReach ? "PLACE CARGO HERE" : "DROP CARGO HERE";
        highlightPulse.alpha = inReach ? 1f : 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f);
    }

    private void CreateHighlight()
    {
        // Independent, runtime-only root keeps dimensions stable under scaled drop-zone parents.
        highlightVisual = new GameObject("Cargo Drop Cue (Runtime)");
        highlightVisual.hideFlags = HideFlags.DontSave;
        highlightPulse = highlightVisual.AddComponent<CanvasGroup>();
        highlightPulse.interactable = false;
        highlightPulse.blocksRaycasts = false;
        footprint = CreateHighlightCanvas("Delivery Outline", new Vector2(300, 300));
        Color gold = new Color(1f, 0.78f, 0.15f, 0.95f);
        AddHighlightStrip(Vector2.zero, Vector2.right, new Vector2(0, 7), gold);
        AddHighlightStrip(Vector2.up, Vector2.one, new Vector2(0, 7), gold);
        AddHighlightStrip(Vector2.zero, Vector2.up, new Vector2(7, 0), gold);
        AddHighlightStrip(Vector2.right, Vector2.one, new Vector2(7, 0), gold);
        caption = CreateHighlightCanvas("Delivery Label", new Vector2(360, 65));
        Image background = caption.gameObject.AddComponent<Image>();
        background.color = new Color(0.04f, 0.14f, 0.18f, 0.92f);
        background.raycastTarget = false;
        GameObject text = new GameObject("Label", typeof(RectTransform));
        text.transform.SetParent(caption, false);
        RectTransform rect = (RectTransform)text.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = new Vector2(-20, -10);
        highlightLabel = text.AddComponent<TextMeshProUGUI>();
        highlightLabel.fontSize = 30;
        highlightLabel.color = gold;
        highlightLabel.alignment = TextAlignmentOptions.Center;
        highlightLabel.enableWordWrapping = false;
        highlightLabel.raycastTarget = false;
    }

    private RectTransform CreateHighlightCanvas(string title, Vector2 size)
    {
        GameObject go = new GameObject(title, typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(highlightVisual.transform, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one * 0.01f;
        return rect;
    }

    private void AddHighlightStrip(Vector2 min, Vector2 max, Vector2 size, Color color)
    {
        GameObject go = new GameObject("Outline", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(footprint, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private void OnDisable() { if (highlightVisual != null) highlightVisual.SetActive(false); }
    private void OnDestroy() { if (highlightVisual != null) Destroy(highlightVisual); }
}
