using UnityEngine;

public class TutorialPointer : MonoBehaviour
{
    [Header("Bounce Settings")]
    public float bounceSpeed = 8f;
    public float bounceAmount = 15f;

    private RectTransform target;
    private Vector2 customOffset;
    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    // --- THE FIX: The script now accepts our 2-part command from the Director! ---
    public void PointAt(RectTransform newTarget, Vector2 offset)
    {
        target = newTarget;
        customOffset = offset;
        gameObject.SetActive(true);
    }

    // (Fallback just in case any old scripts still use the 1-argument version)
    public void PointAt(RectTransform newTarget)
    {
        target = newTarget;
        customOffset = Vector2.zero;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        target = null;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (target != null && rectTransform != null)
        {
            // 1. Snap directly to the center of the UI button
            rectTransform.position = target.position;

            // 2. Apply your custom X/Y offset from the Director Inspector
            rectTransform.anchoredPosition += customOffset;

            // 3. Make it bounce smoothly!
            // We use transform.up so it always bounces in the direction it is pointing.
            // If you rotate it 180 degrees to point down, it will naturally bounce down!
            // We use unscaledTime so it still bounces even if the game is paused!
            float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceAmount;
            rectTransform.position += transform.up * bounce;
        }
    }
}