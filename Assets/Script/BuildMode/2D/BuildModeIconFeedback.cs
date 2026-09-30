using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Tactile feedback for an authored Build Mode icon button. This only animates
/// its RectTransform; it never changes tool state, button callbacks, or sprites.
/// </summary>
[DisallowMultipleComponent]
public sealed class BuildModeIconFeedback : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, Range(0.75f, 1f)] private float pressedScale = 0.88f;
    [SerializeField, Range(1f, 1.2f)] private float hoverScale = 1.05f;
    [SerializeField, Range(1f, 1.25f)] private float releaseScale = 1.12f;
    [SerializeField, Min(0.01f)] private float releasePulseSeconds = 0.09f;
    [SerializeField, Min(1f)] private float responseSpeed = 24f;

    private RectTransform rectTransform;
    private Button button;
    private Vector3 authoredScale;
    private bool pressed;
    private bool hovered;
    private float pulseRemaining;

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        button = GetComponent<Button>();
        if (rectTransform != null) authoredScale = rectTransform.localScale;
    }

    private void OnEnable()
    {
        if (rectTransform == null) rectTransform = transform as RectTransform;
        if (button == null) button = GetComponent<Button>();
        if (rectTransform != null) authoredScale = rectTransform.localScale;
        pressed = false;
        hovered = false;
        pulseRemaining = 0f;
    }

    private void OnDisable()
    {
        pressed = false;
        hovered = false;
        pulseRemaining = 0f;
        if (rectTransform != null) rectTransform.localScale = authoredScale;
    }

    private void Update()
    {
        if (rectTransform == null) return;

        bool interactable = button == null || button.IsInteractable();
        if (!interactable)
        {
            pressed = false;
            hovered = false;
            pulseRemaining = 0f;
        }

        if (pulseRemaining > 0f)
            pulseRemaining = Mathf.Max(0f, pulseRemaining - Time.unscaledDeltaTime);

        float targetScale = pressed ? pressedScale :
            pulseRemaining > 0f ? releaseScale :
            hovered ? hoverScale : 1f;
        Vector3 target = authoredScale * targetScale;
        if ((rectTransform.localScale - target).sqrMagnitude < 0.000001f)
        {
            rectTransform.localScale = target;
            return;
        }

        float amount = 1f - Mathf.Exp(-responseSpeed * Time.unscaledDeltaTime);
        rectTransform.localScale = Vector3.LerpUnclamped(rectTransform.localScale, target, amount);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.IsInteractable()) return;
        pressed = true;
        pulseRemaining = 0f;
    }

    public void OnPointerUp(PointerEventData eventData) { pressed = false; }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (button != null && !button.IsInteractable()) return;
        pressed = false;
        pulseRemaining = releasePulseSeconds;
    }

    public void OnPointerEnter(PointerEventData eventData) { hovered = true; }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
    }
}
