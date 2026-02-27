using TMPro; 
using UnityEngine;

public class FPSDisplay : MonoBehaviour
{
    [Header("UI Settings")]
    
    public TextMeshProUGUI fpsText; 
    
    [Header("Configuration")]
    public float updateInterval = 0.5f; 

    private float accum = 0; 
    private int frames = 0; 
    private float timeleft; 

    void Start()
    {
        if (fpsText == null)
        {
            
            fpsText = GetComponent<TextMeshProUGUI>();

            if (fpsText == null)
            {
                Debug.LogError("FPSDisplay: No Text object assigned! Drag a TextMeshProUGUI object here.");
                this.enabled = false;
                return;
            }
        }
        timeleft = updateInterval;
    }

    void Update()
    {
        timeleft -= Time.deltaTime;
        accum += Time.timeScale / Time.deltaTime;
        ++frames;

        if (timeleft <= 0.0)
        {
            float fps = accum / frames;
            string format = string.Format("{0:F0} FPS", fps);
            fpsText.text = format;

            if (fps < 30)
                fpsText.color = Color.red;
            else if (fps < 55)
                fpsText.color = Color.yellow;
            else
                fpsText.color = Color.green;

            timeleft = updateInterval;
            accum = 0.0f;
            frames = 0;
        }
    }
}