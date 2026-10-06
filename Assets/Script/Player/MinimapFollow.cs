using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MinimapFollow : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Drag your Player GameObject here!")]
    public Transform player;

    [Header("Settings")]
    [Tooltip("How high in the sky should the camera hover?")]
    public float mapHeight = 50f;
    
    [Tooltip("Should the map spin when the player turns?")]
    public bool rotateWithPlayer = false;

    [Header("Existing Shoreline Foam (Map Views Only)")]
    [Tooltip("Enhance the water shader's existing object-contact foam only while this map camera renders.")]
    public bool clearerShorelineFoam = true;
    [Tooltip("Search this many render-texture pixels for above-water shore/object contact. Open water is excluded.")]
    [Range(1f, 6f)] public float shorelineContactPixels = 3f;
    [Tooltip("Widen the existing foam fade near confirmed contacts. Does not change the water material.")]
    [Range(1f, 3f)] public float shorelineFoamWidthMultiplier = 3f;
    [Tooltip("Make the existing noise patches easier to see on the map; normal camera tiling is unchanged.")]
    [Range(1f, 6f)] public float shorelineFoamPatternMultiplier = 3.6f;

    private bool manualView;
    private float nextPlayerSearchTime;
    private UniversalAdditionalCameraData waterCameraData;
    private CameraOverrideOption previousDepthOption;
    private Camera mapCamera;
    private static readonly int MapFoamProfileId = Shader.PropertyToID("_CCMinimapContactFoam");
    private static readonly List<MinimapFollow> mapCameras = new List<MinimapFollow>(2);
    private static readonly List<FoamCameraState> foamCameraStates = new List<FoamCameraState>(4);

    private struct FoamCameraState
    {
        public Camera camera;
        public Vector4 previousProfile;
    }

    public bool IsManualView => manualView;

    private void OnEnable()
    {
        if (!TryGetComponent(out Camera minimapCamera)) return;
        mapCamera = minimapCamera;
        waterCameraData = minimapCamera.GetUniversalAdditionalCameraData();
        previousDepthOption = waterCameraData.requiresDepthOption;
        // Shoreline foam needs depth, not a color copy of the minimap.
        // Water2 no longer samples camera color, avoiding cross-camera feedback.
        waterCameraData.requiresDepthOption = CameraOverrideOption.On;
        if (!mapCameras.Contains(this)) mapCameras.Add(this);
        BindFoamCallbacks();
    }

    private void OnDisable()
    {
        mapCameras.Remove(this);
        if (mapCameras.Count == 0)
        {
            RestoreFoamProfiles();
            RenderPipelineManager.beginCameraRendering -= BeginFoamCamera;
            RenderPipelineManager.endCameraRendering -= EndFoamCamera;
        }
        if (waterCameraData == null) return;
        if (waterCameraData.requiresDepthOption == CameraOverrideOption.On)
            waterCameraData.requiresDepthOption = previousDepthOption;
        waterCameraData = null;
        mapCamera = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFoamProfiles()
    {
        RestoreFoamProfiles();
        Shader.SetGlobalVector(MapFoamProfileId, Vector4.zero);
        RenderPipelineManager.beginCameraRendering -= BeginFoamCamera;
        RenderPipelineManager.endCameraRendering -= EndFoamCamera;
        // Keep surviving components when scene/domain reload is disabled.
        for (int i = mapCameras.Count - 1; i >= 0; i--)
            if (mapCameras[i] == null || !mapCameras[i].isActiveAndEnabled) mapCameras.RemoveAt(i);
        if (mapCameras.Count > 0) BindFoamCallbacks();
    }

    private static void BindFoamCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= BeginFoamCamera;
        RenderPipelineManager.endCameraRendering -= EndFoamCamera;
        RenderPipelineManager.beginCameraRendering += BeginFoamCamera;
        RenderPipelineManager.endCameraRendering += EndFoamCamera;
    }

    private static void BeginFoamCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null) return;
        Vector4 profile = Vector4.zero;
        for (int i = 0; i < mapCameras.Count; i++)
        {
            MinimapFollow follow = mapCameras[i];
            if (follow == null || !follow.isActiveAndEnabled || follow.mapCamera != camera || !follow.clearerShorelineFoam) continue;
            profile = new Vector4(1f, Mathf.Clamp(follow.shorelineContactPixels, 1f, 6f),
                Mathf.Clamp(follow.shorelineFoamWidthMultiplier, 1f, 3f),
                Mathf.Clamp(follow.shorelineFoamPatternMultiplier, 1f, 6f));
            break;
        }
        foamCameraStates.Add(new FoamCameraState { camera = camera, previousProfile = Shader.GetGlobalVector(MapFoamProfileId) });
        // Other cameras always see the unchanged original foam, even if rendered inside a map capture.
        Shader.SetGlobalVector(MapFoamProfileId, profile);
    }

    private static void EndFoamCamera(ScriptableRenderContext context, Camera camera)
    {
        for (int i = foamCameraStates.Count - 1; i >= 0; i--)
        {
            FoamCameraState state = foamCameraStates[i];
            if (state.camera != camera) continue;
            Shader.SetGlobalVector(MapFoamProfileId, state.previousProfile);
            foamCameraStates.RemoveAt(i);
            break;
        }
    }

    private static void RestoreFoamProfiles()
    {
        for (int i = foamCameraStates.Count - 1; i >= 0; i--)
            Shader.SetGlobalVector(MapFoamProfileId, foamCameraStates[i].previousProfile);
        foamCameraStates.Clear();
    }

    private void LateUpdate()
    {
        if (manualView) return;
        if (player == null)
        {
            // Runtime-repaired minimap cameras can be created before a scene's
            // player prefab finishes spawning. Keep trying at a low frequency.
            if (Time.unscaledTime < nextPlayerSearchTime) return;
            nextPlayerSearchTime = Time.unscaledTime + 0.5f;
            PlayerMotor playerMotor = null;
            foreach (PlayerMotor candidate in FindObjectsOfType<PlayerMotor>())
            {
                if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                {
                    playerMotor = candidate;
                    break;
                }
            }

            if (playerMotor == null) return;
            player = playerMotor.transform;
        }

        // 1. Follow the player's X and Z, but lock the Y height in the sky
        Vector3 newPosition = player.position;
        newPosition.y = mapHeight;
        transform.position = newPosition;

        // 2. Optional: Spin the map if the player turns
        if (rotateWithPlayer)
        {
            // Lock the X to 90 (looking down), match the Player's Y turn, lock Z to 0
            transform.rotation = Quaternion.Euler(90f, player.eulerAngles.y, 0f);
        }
        else
        {
            // Keep the map permanently facing North
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }

    /// <summary>
    /// Releases the minimap from player-follow while the expanded map is being
    /// explored. The expanded view is north-up so dragging remains predictable.
    /// </summary>
    public void SetManualView(bool enabled, bool faceNorth = true)
    {
        manualView = enabled;
        if (enabled && faceNorth)
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void SetManualCenter(Vector3 worldCenter)
    {
        worldCenter.y = mapHeight;
        transform.position = worldCenter;
    }

    public void SnapToPlayer()
    {
        if (player == null) return;

        Vector3 playerPosition = player.position;
        playerPosition.y = mapHeight;
        transform.position = playerPosition;
        transform.rotation = rotateWithPlayer
            ? Quaternion.Euler(90f, player.eulerAngles.y, 0f)
            : Quaternion.Euler(90f, 0f, 0f);
    }
}
