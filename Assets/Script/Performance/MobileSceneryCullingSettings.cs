using UnityEngine;

[CreateAssetMenu(fileName = "MobileSceneryCullingSettings", menuName = "Civil Craft/Performance/Mobile Scenery Culling")]
public sealed class MobileSceneryCullingSettings : ScriptableObject
{
    [Tooltip("Applies only in mobile players, unless Editor Preview is enabled.")]
    public bool enableCulling = true;

    [Tooltip("Allows testing this mobile-only culling in the Unity Editor.")]
    public bool previewInEditor;

    [Tooltip("Only visual-only meshes below this world-space size are distance culled. Larger scenery stays visible.")]
    [Min(0.1f)] public float maximumPropSize = 3f;

    [Tooltip("Small generated scenery remains visible this far from the overworld camera.")]
    [Min(0f)] public float overworldDistance = 200f;

    [Tooltip("Small generated scenery remains visible this far from a build camera. Zero disables build-camera distance culling.")]
    [Min(0f)] public float buildDistance = 300f;

    [Tooltip("Number of decorative renderers inspected each frame while a map loads. Lower values keep loading frames shorter.")]
    [Min(1)] public int renderersPerFrame = 512;

    [Tooltip("Only this map Scene is scanned. Leave empty to use any Scene containing the generated props root.")]
    public string targetSceneName = "CanyonCrossing";

    [Tooltip("Scene root containing generated decorative props. No other scene hierarchy is changed.")]
    public string generatedPropsRootName = "---- GENERATED PROPS ------";

    [Tooltip("Name of the overworld camera in Canyon Crossing.")]
    public string overworldCameraName = "PlayerCamera";

    [Tooltip("Name shared by the build-location cameras in Canyon Crossing.")]
    public string buildCameraName = "build_camera";
}
