using UnityEngine;

public class UnderwaterEffect : MonoBehaviour
{
    [Header("Water Reference")]
    [Tooltip("Drag your water plane here.")]
    public Transform waterPlane;

    [Header("Underwater Visuals")]
    public Color underwaterColor = new Color(0.1f, 0.4f, 0.5f, 1f); // A deep murky blue
    public float underwaterFogDensity = 0.15f;

    // We save the original settings so we can revert when they pop back out!
    private bool defaultFogState;
    private Color defaultFogColor;
    private float defaultFogDensity;
    private CameraClearFlags defaultClearFlags;
    private Color defaultCameraColor;

    private Camera cam;
    private bool isUnderwater = false;

    private void Start()
    {
        cam = GetComponent<Camera>();

        // Remember what the air looked like before we fell in
        defaultFogState = RenderSettings.fog;
        defaultFogColor = RenderSettings.fogColor;
        defaultFogDensity = RenderSettings.fogDensity;
        
        if (cam != null)
        {
            defaultClearFlags = cam.clearFlags;
            defaultCameraColor = cam.backgroundColor;
        }
    }

    private void Update()
    {
        if (waterPlane == null || cam == null) return;

        // Check if the camera has dipped below the water plane
        if (transform.position.y < waterPlane.position.y && !isUnderwater)
        {
            SetUnderwater(true);
        }
        else if (transform.position.y >= waterPlane.position.y && isUnderwater)
        {
            SetUnderwater(false);
        }
    }

    private void SetUnderwater(bool state)
    {
        isUnderwater = state;

        if (state)
        {
            // Turn on the thick water fog
            RenderSettings.fog = true;
            RenderSettings.fogColor = underwaterColor;
            RenderSettings.fogDensity = underwaterFogDensity;
            
            // Exponential fog looks the most realistic for underwater depth
            RenderSettings.fogMode = FogMode.Exponential; 

            if (cam != null)
            {
                cam.backgroundColor = underwaterColor;
                cam.clearFlags = CameraClearFlags.SolidColor; // Hides the skybox
            }
        }
        else
        {
            // Revert back to normal air
            RenderSettings.fog = defaultFogState;
            RenderSettings.fogColor = defaultFogColor;
            RenderSettings.fogDensity = defaultFogDensity;

            if (cam != null)
            {
                cam.backgroundColor = defaultCameraColor;
                cam.clearFlags = defaultClearFlags; // Brings the skybox back
            }
        }
    }
}