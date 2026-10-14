using UnityEngine;
using TMPro;

public class FPSViewer : MonoBehaviour
{
    public TextMeshProUGUI fpsText;
    [Min(.1f)] public float updateInterval = .5f;
    [HideInInspector] public bool developmentOverlay;
    [HideInInspector] public RectTransform safeAreaRoot;
    readonly FrameRateSample sample = new FrameRateSample();
    private void OnEnable() => sample.Reset();
    private void Start()
    {
        if (fpsText == null) fpsText = GetComponent<TextMeshProUGUI>();
        // Replace legacy scene counters with one opt-in development overlay.
        if (fpsText != null) fpsText.enabled = false;
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (fpsText == null) return;
        fpsText.enabled = developmentOverlay && DevelopmentPerformanceSettings.ShowFps;
        if (!fpsText.enabled) return;
        if (safeAreaRoot != null && Screen.width > 0 && Screen.height > 0)
        {
            Rect safe = Screen.safeArea;
            safeAreaRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeAreaRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        }
        if (sample.Add(Time.unscaledDeltaTime, Mathf.Max(.1f, updateInterval)))
            fpsText.text = $"{sample.Fps:0} FPS  |  {sample.Milliseconds:0.0} ms\n{DevelopmentPerformanceSettings.OcclusionStatus}";
#else
        if (fpsText != null) fpsText.enabled = false;
        enabled = false;
#endif
    }
}

/// <summary>Wall-clock frame cadence, independent of pause/timeScale; not GPU timing.</summary>
public sealed class FrameRateSample
{
    double elapsed;
    int frames;
    public double Fps { get; private set; }
    public double Milliseconds { get; private set; }
    public void Reset() { elapsed = 0; frames = 0; Fps = 0; Milliseconds = 0; }
    public bool Add(float seconds, float interval)
    {
        if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
        elapsed += seconds;
        frames++;
        if (elapsed < interval) return false;
        Fps = frames / elapsed;
        Milliseconds = elapsed * 1000d / frames;
        elapsed = 0; frames = 0;
        return true;
    }
}
