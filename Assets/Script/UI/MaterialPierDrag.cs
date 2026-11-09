using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(MaterialButtonTrigger))]
internal sealed class MaterialPierDrag : MonoBehaviour, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private MaterialButtonTrigger materialButton;
    private ScrollRect scrollRect;
    private PointerEventData activeDrag;
    private BarCreator pierCreator;
    private bool scrolling;
    private bool attemptedPlacement;

    private void Awake()
    {
        materialButton = GetComponent<MaterialButtonTrigger>();
        scrollRect = GetComponentInParent<ScrollRect>();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (activeDrag == null && scrollRect != null)
            scrollRect.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        eventData.eligibleForClick = false;
        if (activeDrag != null) return;

        activeDrag = eventData;
        Vector2 movement = eventData.position - eventData.pressPosition;
        // The toolbar sits below the workspace. Keep sideways swipes on its scroll view.
        scrolling = !materialButton.IsAvailableForSelection || movement.y <= Mathf.Abs(movement.x);
        if (scrolling && scrollRect != null) scrollRect.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData != activeDrag) return;
        if (scrolling)
        {
            if (scrollRect != null) scrollRect.OnDrag(eventData);
            return;
        }

        if (pierCreator != null)
        {
            pierCreator.UpdateToolbarPierDrag(eventData, finish: false);
            return;
        }

        BarCreator creator = BuildUIController.Instance?.barCreator;
        if (attemptedPlacement || creator == null || !creator.CanAimToolbarPierAt(eventData)) return;
        attemptedPlacement = true;
        if (!creator.TryBeginToolbarPierDrag(materialButton.buttonMaterial, eventData)) return;
        if (activeDrag == eventData) pierCreator = creator;
        else creator.CancelToolbarPierDrag(eventData.pointerId);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData != activeDrag) return;
        BarCreator creator = pierCreator;
        EndGesture();
        if (creator != null) creator.UpdateToolbarPierDrag(eventData, finish: true);
    }

    private void OnDisable() => CancelGesture();

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelGesture();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) CancelGesture();
    }

    private void CancelGesture()
    {
        if (activeDrag == null) return;
        int pointerId = activeDrag.pointerId;
        BarCreator creator = pierCreator;
        EndGesture();
        if (creator != null) creator.CancelToolbarPierDrag(pointerId);
    }

    private void EndGesture()
    {
        if (scrolling && scrollRect != null) scrollRect.OnEndDrag(activeDrag);
        activeDrag = null;
        pierCreator = null;
        scrolling = false;
        attemptedPlacement = false;
    }
}
