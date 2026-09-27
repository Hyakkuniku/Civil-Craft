using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class CompletionButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    private Button button;
    private bool hovered;
    private bool pressed;

    private void Awake() { button = GetComponent<Button>(); }
    private void OnDisable() { hovered = pressed = false; transform.localScale = Vector3.one; }
    private void Update()
    {
        float target = button != null && button.IsInteractable()
            ? (pressed ? .91f : hovered ? 1.045f : 1f) : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target,
            1f - Mathf.Exp(-22f * Time.unscaledDeltaTime));
    }
    public void OnPointerEnter(PointerEventData e) { hovered = true; }
    public void OnPointerExit(PointerEventData e) { hovered = pressed = false; }
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) pressed = true;
    }
    public void OnPointerUp(PointerEventData e) { pressed = false; }
}
