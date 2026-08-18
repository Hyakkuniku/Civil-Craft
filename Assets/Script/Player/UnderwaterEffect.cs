using UnityEngine;
using UnityEngine.Rendering.Universal; // NEW: We need this to talk to URP!

public class UnderwaterEffect : MonoBehaviour
{
    [Header("Water Reference")]
    [Tooltip("Drag your water plane here.")]
    public Transform waterPlane;

    [Header("Underwater Visuals")]
    public Color underwaterColor = new Color(0.1f, 0.4f, 0.5f, 1f); 
    public float underwaterFogDensity = 0.15f;

    private bool defaultFogState;
    private Color defaultFogColor;
    private float defaultFogDensity;
    private CameraClearFlags defaultClearFlags;
    private Color defaultCameraColor;

    private Camera cam;
    private UniversalAdditionalCameraData urpCameraData; // NEW: URP Camera Data
    private CameraRenderType defaultRenderType; // NEW: URP Render Type

    private bool isUnderwater = false;

    private void Start()
    {
        cam = GetComponent<Camera>();
        
        // Grab the URP specific camera data
        if (cam != null)
        {
            urpCameraData = cam.GetComponent<UniversalAdditionalCameraData>();
        }

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
            RenderSettings.fog = true;
            RenderSettings.fogColor = underwaterColor;
            RenderSettings.fogDensity = underwaterFogDensity;
            RenderSettings.fogMode = FogMode.Exponential; 

            if (cam != null)
            {
                cam.backgroundColor = underwaterColor;
                cam.clearFlags = CameraClearFlags.SolidColor; 
                
                // NEW: Force URP to respect the solid color background
                if (urpCameraData != null)
                {
                    urpCameraData.renderType = CameraRenderType.Base;
                }
            }
        }
        else
        {
            RenderSettings.fog = defaultFogState;
            RenderSettings.fogColor = defaultFogColor;
            RenderSettings.fogDensity = defaultFogDensity;

            if (cam != null)
            {
                cam.backgroundColor = defaultCameraColor;
                cam.clearFlags = defaultClearFlags; 
            }
        }
    }
}