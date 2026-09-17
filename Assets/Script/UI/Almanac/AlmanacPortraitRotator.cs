using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Turns the 3D photobooth character when the Almanac portrait is dragged,
/// while leaving the portrait UI and its render camera in place.
/// </summary>
[DisallowMultipleComponent]
public sealed class AlmanacPortraitRotator : MonoBehaviour,
    IInitializePotentialDragHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IPointerUpHandler
{
    [Header("Photobooth References")]
    [SerializeField] private Transform rotationTarget;
    [SerializeField] private Transform cameraToKeepFixed;

    [Header("Interaction")]
    [SerializeField, Min(1f)] private float degreesPerScreenWidth = 180f;
    [SerializeField, Range(1f, 180f)] private float maximumYaw = 70f;
    [SerializeField, Min(0.01f)] private float returnDuration = 0.25f;

    private Quaternion restLocalRotation;
    private Transform stableSpace;
    private Vector3 cameraPositionInStableSpace;
    private Quaternion cameraRotationInStableSpace;
    private float currentYaw;
    private float returnStartYaw;
    private float returnElapsed;
    private int activePointerId = int.MinValue;
    private bool returning;
    private bool poseCached;

    private void Awake()
    {
        // The portrait RawImage was presentation-only before this component was
        // added. It must participate in UI raycasts to receive touch/mouse drag.
        Graphic portraitGraphic = GetComponent<Graphic>();
        if (portraitGraphic != null)
            portraitGraphic.raycastTarget = true;

        CacheRestPose();
    }

    private void OnEnable()
    {
        if (!poseCached)
            CacheRestPose();
    }

    private void OnDisable()
    {
        ResetImmediately();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        // Rotation should begin without requiring a large finger movement.
        eventData.useDragThreshold = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!poseCached || rotationTarget == null)
            return;

        activePointerId = eventData.pointerId;
        returning = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!poseCached || rotationTarget == null ||
            eventData.pointerId != activePointerId)
        {
            return;
        }

        float screenWidth = Mathf.Max(1f, Screen.width);
        currentYaw += eventData.delta.x / screenWidth * degreesPerScreenWidth;
        currentYaw = Mathf.Clamp(currentYaw, -maximumYaw, maximumYaw);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        ReleasePointer(eventData.pointerId);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ReleasePointer(eventData.pointerId);
    }

    private void LateUpdate()
    {
        if (!poseCached || rotationTarget == null)
            return;

        if (returning)
        {
            returnElapsed += Time.unscaledDeltaTime;
            float time = Mathf.Clamp01(returnElapsed / returnDuration);
            float easedTime = 1f - Mathf.Pow(1f - time, 3f);
            currentYaw = Mathf.LerpUnclamped(returnStartYaw, 0f, easedTime);

            if (time >= 1f)
            {
                currentYaw = 0f;
                returning = false;
            }
        }

        rotationTarget.localRotation =
            restLocalRotation * Quaternion.AngleAxis(currentYaw, Vector3.up);
        RestoreCameraPose();
    }

    private void CacheRestPose()
    {
        if (rotationTarget == null)
            return;

        restLocalRotation = rotationTarget.localRotation;
        stableSpace = rotationTarget.parent;

        if (cameraToKeepFixed != null && stableSpace != null)
        {
            cameraPositionInStableSpace =
                stableSpace.InverseTransformPoint(cameraToKeepFixed.position);
            cameraRotationInStableSpace =
                Quaternion.Inverse(stableSpace.rotation) * cameraToKeepFixed.rotation;
        }

        currentYaw = 0f;
        poseCached = true;
    }

    private void ReleasePointer(int pointerId)
    {
        if (pointerId != activePointerId)
            return;

        activePointerId = int.MinValue;
        returnStartYaw = currentYaw;
        returnElapsed = 0f;
        returning = !Mathf.Approximately(currentYaw, 0f);
    }

    private void RestoreCameraPose()
    {
        if (cameraToKeepFixed == null || stableSpace == null)
            return;

        cameraToKeepFixed.SetPositionAndRotation(
            stableSpace.TransformPoint(cameraPositionInStableSpace),
            stableSpace.rotation * cameraRotationInStableSpace);
    }

    private void ResetImmediately()
    {
        activePointerId = int.MinValue;
        returning = false;
        returnElapsed = 0f;
        currentYaw = 0f;

        if (!poseCached || rotationTarget == null)
            return;

        rotationTarget.localRotation = restLocalRotation;
        RestoreCameraPose();
    }

    private void OnValidate()
    {
        degreesPerScreenWidth = Mathf.Max(1f, degreesPerScreenWidth);
        maximumYaw = Mathf.Clamp(maximumYaw, 1f, 180f);
        returnDuration = Mathf.Max(0.01f, returnDuration);
    }
}
