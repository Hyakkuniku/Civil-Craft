using UnityEngine;

public class UIPulseAnimation : MonoBehaviour
{
    [Header("Pulse Settings")]
    [Tooltip("How fast the icon breathes in and out.")]
    public float pulseSpeed = 6f;
    
    [Tooltip("The smallest size the icon shrinks to.")]
    public float minScale = 0.85f;
    
    [Tooltip("The largest size the icon grows to.")]
    public float maxScale = 1.15f;

    [Min(0f)] public float bobHeight;

    private Vector3 originalScale;
    private RectTransform rect;
    private Vector2 restingPosition;
    private float enabledAt;

    private void Awake()
    {
        // Remember the native size of your icon so it scales proportionally
        originalScale = transform.localScale;
        rect = transform as RectTransform;
        if (rect != null) restingPosition = rect.anchoredPosition;
    }

    private void OnEnable()
    {
        // Whenever the ObjectiveTrackerUI turns this icon back on, snap it back to normal first
        transform.localScale = originalScale;
        enabledAt = Time.unscaledTime;
    }

    private void OnDisable()
    {
        if (rect != null && bobHeight > 0f) rect.anchoredPosition = restingPosition;
    }

    private void Update()
    {
        // Use a Sine wave to create a perfectly smooth, endless breathing loop
        // Time.unscaledTime ensures it keeps animating even if the game is paused!
        float sineWave = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) / 2f;
        float currentScale = Mathf.Lerp(minScale, maxScale, sineWave);
        
        transform.localScale = originalScale * currentScale;
        if (rect != null && bobHeight > 0f)
        {
            float bounce = (1f - Mathf.Cos((Time.unscaledTime - enabledAt) * pulseSpeed)) * .5f;
            rect.anchoredPosition = restingPosition + Vector2.up * (bounce * bobHeight);
        }
    }
}
