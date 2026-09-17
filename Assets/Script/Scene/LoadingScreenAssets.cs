using UnityEngine;

[CreateAssetMenu(menuName = "Civil Craft/Loading Screen Assets")]
public sealed class LoadingScreenAssets : ScriptableObject
{
    [Tooltip("Visual-only player used when the current scene has no gameplay player to clone.")]
    public GameObject fallbackPlayerPrefab;

    [Tooltip("The normal player Animator Controller. The loading preview forces its walk state.")]
    public RuntimeAnimatorController playerAnimatorController;
}
