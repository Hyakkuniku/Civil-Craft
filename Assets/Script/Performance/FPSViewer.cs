using UnityEngine;
using TMPro;

public class FPSViewer : MonoBehaviour
{
    [Header("UI Reference")]
    public TextMeshProUGUI fpsText;

    [Header("Settings")]
    [Tooltip("How often the text updates (in seconds). Updating every frame causes lag!")]
    public float updateInterval = 0.2f;

    private float accumulator = 0f;
    private int frames = 0;
    private float timeLeft;

    private void Start()
    {
        if (fpsText == null)
            fpsText = GetComponent<TextMeshProUGUI>();

        timeLeft = updateInterval;
    }

    private void Update()
    {
        timeLeft -= Time.unscaledDeltaTime;
        accumulator += Time.timeScale / Time.unscaledDeltaTime;
        frames++;

        // Only update the text when the interval has passed
        if (timeLeft <= 0.0f)
        {
            float currentFps = accumulator / frames;
            
            if (fpsText != null)
            {
                fpsText.text = $"FPS: {Mathf.RoundToInt(currentFps)}";

                // Optional: Change color based on performance
                if (currentFps >= 50f)
                    fpsText.color = Color.green;
                else if (currentFps >= 30f)
                    fpsText.color = Color.yellow;
                else
                    fpsText.color = Color.red;
            }

            // Reset variables for the next interval
            timeLeft = updateInterval;
            accumulator = 0f;
            frames = 0;
        }
    }
}