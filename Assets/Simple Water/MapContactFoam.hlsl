#ifndef CIVIL_CRAFT_MAP_CONTACT_FOAM_INCLUDED
#define CIVIL_CRAFT_MAP_CONTACT_FOAM_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

// Set only while a minimap camera renders: enabled, pixel radius, width, scale divisor.
float4 _CCMinimapContactFoam;

#ifndef SHADERGRAPH_PREVIEW
bool CCMapFoamHasAboveWaterNeighbor(float2 uv, float waterEyeDepth)
{
    if (any(uv < 0.0) || any(uv > 1.0))
        return false;

    float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
    if (rawDepth <= 0.000001)
        return false;
    float deviceDepth = rawDepth;
#else
    if (rawDepth >= 0.999999)
        return false;
    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
#endif

    // Reconstruct in the current camera's projection, including orthographic views.
    float3 neighborWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
    float neighborEyeDepth = -TransformWorldToView(neighborWS).z;
    return neighborEyeDepth > 0.0 && neighborEyeDepth < waterEyeDepth - 0.03;
}
#endif

void CCMapContactFoam_float(float OriginalFade, float3 PositionWS, float2 ScreenUV,
    float FoamCutoff, float NoiseScale, out float Fade, out float AdjustedNoiseScale,
    out float ContactMask)
{
    Fade = OriginalFade;
    AdjustedNoiseScale = NoiseScale;
    ContactMask = 1.0;

#ifndef SHADERGRAPH_PREVIEW
    // Preserve the original main-camera shader math and avoid extra depth samples.
    if (_CCMinimapContactFoam.x <= 0.5)
        return;

    float2 radiusUV = max(_CCMinimapContactFoam.y, 1.0) / max(_ScaledScreenParams.xy, 1.0);
    float waterEyeDepth = -TransformWorldToView(PositionWS).z;
    bool contact = CCMapFoamHasAboveWaterNeighbor(ScreenUV + float2(radiusUV.x, 0.0), waterEyeDepth)
        || CCMapFoamHasAboveWaterNeighbor(ScreenUV - float2(radiusUV.x, 0.0), waterEyeDepth)
        || CCMapFoamHasAboveWaterNeighbor(ScreenUV + float2(0.0, radiusUV.y), waterEyeDepth)
        || CCMapFoamHasAboveWaterNeighbor(ScreenUV - float2(0.0, radiusUV.y), waterEyeDepth);

    ContactMask = contact ? 1.0 : 0.0;
    AdjustedNoiseScale = NoiseScale / max(_CCMinimapContactFoam.w, 1.0);
    if (!contact)
    {
        Fade = 1.0;
        return;
    }

    // Widen the existing intersection foam, while keeping fully deep water below its cutoff.
    float widthMultiplier = max(1.0, min(_CCMinimapContactFoam.z, FoamCutoff * 0.65));
    Fade = saturate(OriginalFade / widthMultiplier);
#endif
}

#endif
