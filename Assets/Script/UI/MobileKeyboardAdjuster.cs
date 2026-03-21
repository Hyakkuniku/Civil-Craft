using UnityEngine;

public class MobileKeyboardAdjuster : MonoBehaviour
{
    [Header("UI To Move")]
    [Tooltip("Drag the Panel you want to slide up here (e.g., LoginPanel or AuthCanvas)")]
    public RectTransform panelToMove;

    [Header("Settings")]
    [Tooltip("How high should the panel slide up? (Usually 300 to 500)")]
    public float slideUpDistance = 400f;
    [Tooltip("How fast should it slide?")]
    public float slideSpeed = 10f;

    private Vector2 originalPosition;
    private Vector2 targetPosition;

    private void Start()
    {
        if (panelToMove != null)
        {
            // Remember exactly where the panel started
            originalPosition = panelToMove.anchoredPosition;
            targetPosition = originalPosition;
        }
    }

    private void Update()
    {
        if (panelToMove == null) return;

        // Check if the mobile keyboard is open
        if (TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible)
        {
            // Set the target position higher up
            targetPosition = new Vector2(originalPosition.x, originalPosition.y + slideUpDistance);
        }
        else
        {
            // Set the target position back to normal
            targetPosition = originalPosition;
        }

        // Smoothly slide the panel to the target position
        panelToMove.anchoredPosition = Vector2.Lerp(panelToMove.anchoredPosition, targetPosition, Time.deltaTime * slideSpeed);
    }
}