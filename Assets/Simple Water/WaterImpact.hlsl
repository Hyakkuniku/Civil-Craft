#ifndef CIVIL_CRAFT_WATER_IMPACT_INCLUDED
#define CIVIL_CRAFT_WATER_IMPACT_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

// Runtime-only, bounded impacts. No foam, particles or camera-color sampling.
float _CCWaterImpactCount;
float4 _CCWaterImpactPosition[8]; // world X/Y/Z, age in seconds
float4 _CCWaterImpactData[8];     // severity, footprint scale, lifetime, reserved

void CCWaterImpactResponse(float3 PositionWS, float DipDepth, float Radius,
    float Duration, float RingWidth, out float Depression, out float Ripple)
{
    Depression = 0.0;
    Ripple = 0.0;
#ifndef SHADERGRAPH_PREVIEW
    [loop]
    for (int i = 0; i < 8; i++)
    {
        if (i >= (int)_CCWaterImpactCount) break;
        float4 impact = _CCWaterImpactPosition[i];
        float4 data = _CCWaterImpactData[i];
        float lifetime = max(0.1, min(Duration, max(data.z, 0.1)));
        if (impact.w < 0.0 || impact.w >= lifetime || data.x <= 0.0) continue;
        float radius = max(0.25, Radius * data.y);
        float2 delta = PositionWS.xz - impact.xz;
        if (dot(delta, delta) > radius * radius * 16.0) continue;
        float verticalTolerance = max(0.5, DipDepth * 2.0 + 0.1);
        if (abs(PositionWS.y - impact.y) > verticalTolerance) continue;

        float t = saturate(impact.w / lifetime);
        float life = (1.0 - t) * (1.0 - t) * saturate(data.x);
        float radial = length(delta) / radius;
        float bowl = 1.0 - smoothstep(0.0, 1.0, radial);
        float onset = smoothstep(0.0, 0.08, t);
        float recovery = 1.0 - smoothstep(0.25, 0.65, t);
        Depression += bowl * bowl * onset * recovery * life;

        // A short outward rebound wave fades away; it is water-colored, not foam.
        float ring = 1.0 - smoothstep(RingWidth * 0.35, RingWidth,
            abs(radial - (0.35 + t * 3.0)));
        Ripple += sin(radial * 6.2831853 - t * 12.566371) * ring * life;
    }
#endif
}

void CCWaterImpactColor_float(float4 OriginalColor, float3 PositionWS,
    float Strength, float DipDepth, float Radius, float Duration, out float4 Color)
{
    Color = OriginalColor;
#ifndef SHADERGRAPH_PREVIEW
    if (Strength <= 0.0 || _CCWaterImpactCount < 0.5) return;
    // Modest screen-space antialiasing keeps the same local ring readable from above.
    float pixelWidth = max(length(ddx(PositionWS.xz)), length(ddy(PositionWS.xz)));
    float ringWidth = clamp(pixelWidth / max(Radius, 0.25) * 0.8, 0.3, 0.65);
    float depression, ripple;
    CCWaterImpactResponse(PositionWS, DipDepth, Radius, Duration, ringWidth,
        depression, ripple);
    float shading = clamp(-depression * 0.75 + ripple * 0.65, -1.0, 1.0);
    Color.rgb = OriginalColor.rgb * (1.0 + shading * saturate(Strength));
    // Original alpha and the contact-foam blend downstream remain unchanged.
#endif
}

void CCWaterImpactVertex_float(float3 OriginalPositionOS, float3 PositionWS,
    float DipDepth, float Radius,
    float Duration, out float3 PositionOS)
{
    PositionOS = OriginalPositionOS;
#ifndef SHADERGRAPH_PREVIEW
    if (DipDepth <= 0.0 || _CCWaterImpactCount < 0.5) return;
    float3 displaced = PositionWS;
    if (DipDepth > 0.0 && _CCWaterImpactCount > 0.5)
    {
        float depression, ripple;
        CCWaterImpactResponse(PositionWS, DipDepth, Radius, Duration, 0.35,
            depression, ripple);
        float height = clamp((-depression + ripple * 0.2) * DipDepth,
            -DipDepth, DipDepth * 0.3);
        if (abs(height) <= 0.000001) return;
        displaced.y += height;
    }
    // Shader Graph's vertex Position slot expects OBJECT space, even on scaled water.
    PositionOS = TransformWorldToObject(displaced);
#endif
}

#endif
