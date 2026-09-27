using UnityEngine;

[CreateAssetMenu(menuName = "Civil Craft/Loading Screen Assets")]
public sealed class LoadingScreenAssets : ScriptableObject
{
    [Header("Transition Timing")]
    [Min(0f)]
    [Tooltip("Minimum seconds from showing the loading screen until it reaches 100%. Set to 0 to disable the minimum.")]
    public float minimumDisplaySeconds = 1.5f;

    [Header("Player Preview")]
    [Tooltip("Visual-only player used when the current scene has no gameplay player to clone.")]
    public GameObject fallbackPlayerPrefab;

    [Tooltip("The normal player Animator Controller used for the loading preview's running state.")]
    public RuntimeAnimatorController playerAnimatorController;

    [Tooltip("Near clipping distance for the loading preview camera. Lower values keep cosmetics close to the camera visible.")]
    [Range(0.01f, 0.05f)]
    public float playerPreviewNearClipPlane = 0.01f;
}
