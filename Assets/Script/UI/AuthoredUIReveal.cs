using UnityEngine;

/// <summary>Short unscaled reveal; restores the authored pose even when interrupted.</summary>
[DisallowMultipleComponent]
public sealed class AuthoredUIReveal : MonoBehaviour
{
    [SerializeField] private RectTransform visual;
    [SerializeField] private CanvasGroup group;
    [SerializeField, Range(.05f, .5f)] private float duration = .18f;
    [SerializeField, Range(.95f, 1f)] private float startScale = .985f;
    private Vector3 restingScale;
    private float restingAlpha;
    private float started;
    private bool captured;
    private bool running;

    private void OnEnable() { if (Application.IsPlaying(gameObject)) Play(); }
    private void Update()
    {
        if (!running) return;
        float t = Mathf.Clamp01((Time.unscaledTime - started) / Mathf.Max(.05f, duration));
        ApplyProgress(t);
        if (t >= 1) { running = false; Restore(); }
    }
    public void Play()
    {
        if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled) return;
        Capture(); Restore(); started = Time.unscaledTime; running = true; ApplyProgress(0);
    }
    private void Capture()
    {
        if (captured) return;
        if (visual != null) restingScale = visual.localScale;
        if (group != null) restingAlpha = group.alpha;
        captured = true;
    }
    // Deterministic sampling also lets isolated editor checks cover pauses and interruptions.
    public void ApplyProgress(float progress)
    {
        Capture();
        float eased = 1 - Mathf.Pow(1 - Mathf.Clamp01(progress), 3);
        if (visual != null) visual.localScale = restingScale * Mathf.Lerp(startScale, 1, eased);
        if (group != null) group.alpha = restingAlpha * eased;
    }
    public void Restore()
    {
        if (!captured) return;
        if (visual != null) visual.localScale = restingScale;
        if (group != null) group.alpha = restingAlpha;
    }
    private void OnDisable() { running = false; Restore(); }
}
