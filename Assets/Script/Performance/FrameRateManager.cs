using UnityEngine;

public class FrameRateManager : MonoBehaviour
{
    [Header("Frame Settings")]
    [Tooltip("Used only when the player has not selected a frame-rate cap yet.")]
    [Min(30)] public int defaultFrameRate = 60;

    private void Awake()
    {
        ApplySavedFrameRate(defaultFrameRate);
    }

    public static void ApplySavedFrameRate(int fallback = 60)
    {
        int savedCap = PlayerPrefs.GetInt("FrameRateCap", fallback);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = savedCap;
    }
}
