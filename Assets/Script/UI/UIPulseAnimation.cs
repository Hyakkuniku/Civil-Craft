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

    private Vector3 originalScale;

    private void Awake()
    {
        // Remember the native size of your icon so it scales proportionally
        originalScale = transform.localScale;
    }

    private void OnEnable()
    {
        // Whenever the ObjectiveTrackerUI turns this icon back on, snap it back to normal first
        transform.localScale = originalScale;
    }

    private void Update()
    {
        // Use a Sine wave to create a perfectly smooth, endless breathing loop
        // Time.unscaledTime ensures it keeps animating even if the game is paused!
        float sineWave = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) / 2f;
        float currentScale = Mathf.Lerp(minScale, maxScale, sineWave);
        
        transform.localScale = originalScale * currentScale;
    }
}