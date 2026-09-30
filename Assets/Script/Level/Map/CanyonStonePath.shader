Shader "Civil Craft/Canyon Stone Path"
{
    Properties
    {
        _StoneTint ("Stone color", Color) = (0.86, 0.75, 0.62, 1)
        _GroutTint ("Joint color", Color) = (0.69, 0.59, 0.48, 1)
        _StoneVariation ("Block color variation", Range(0, 0.12)) = 0.05
        _StonesAcross ("Blocks across path", Range(3, 5)) = 4
        _StoneAspect ("Block length / width", Range(0.6, 2.5)) = 1.2
        _JointWidth ("Joint width", Range(0.01, 0.08)) = 0.03
        _Strength ("Opacity", Range(0, 1)) = 0.85
        _Width ("Path width", Float) = 0.075
        _Softness ("Path edge softness", Range(0.02, 0.8)) = 0.16
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-48" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _StoneTint, _GroutTint;
                float4 _PathBounds;
                float4 _Segments[16];
                float4 _SegmentWidths[16];
                float _StoneVariation, _StonesAcross, _StoneAspect, _JointWidth;
                float _Strength, _Width, _Softness;
                int _SegmentCount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(
                    output.positionWS + normalize(output.normalWS) * 0.017);
                return output;
            }

            float HashCell(float2 cell)
            {
                return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The overlay must not paint the canyon's vertical cliff faces.
                float3 normalWS = normalize(input.normalWS);
                clip(normalWS.y - 0.85);

                float2 p = (input.positionWS.xz - _PathBounds.xy) * _PathBounds.z;
                float closestSq = 1e12;
                float along = 0;
                float across = 0;
                float widthMultiplier = 1;

                // Use the nearest authored route segment to orient the stones.
                // Short connected segments can follow a curved canyon path.
                for (int n = 0; n < _SegmentCount; n++)
                {
                    float2 start = _Segments[n].xy;
                    float2 span = _Segments[n].zw - start;
                    float lengthSq = max(dot(span, span), 1e-6);
                    float t = saturate(dot(p - start, span) / lengthSq);
                    float2 offset = p - start - span * t;
                    float multiplier = max(0.1, _SegmentWidths[n].x);
                    float candidateSq = dot(offset, offset) / (multiplier * multiplier);
                    if (candidateSq < closestSq)
                    {
                        float segmentLength = sqrt(lengthSq);
                        float2 tangent = span / segmentLength;
                        closestSq = candidateSq;
                        along = t * segmentLength;
                        across = dot(offset, float2(-tangent.y, tangent.x));
                        widthMultiplier = multiplier;
                    }
                }

                float halfWidth = max(_Width * 0.5, 1e-4);
                float edgeSoftness = clamp(_Softness, 0.02, 0.8);
                float coverage = 1 - smoothstep(halfWidth * (1 - edgeSoftness),
                    halfWidth, sqrt(closestSq));
                clip(coverage - 0.002);

                // Large, flat blocks echo the game's low-poly shapes. Keep a
                // little antialiasing so their clean joints survive on mobile.
                float stoneWidth = 2 * halfWidth * widthMultiplier /
                    max(_StonesAcross, 1);
                float2 grid = float2(along / max(stoneWidth * _StoneAspect, 1e-4),
                    across / max(stoneWidth, 1e-4));
                float row = floor(grid.y);
                grid.x += frac(row * 0.5);
                float2 cell = floor(grid);
                float2 positionInCell = frac(grid);
                float2 edgeDistance = min(positionInCell, 1 - positionInCell);
                float jointDistance = min(edgeDistance.x, edgeDistance.y);
                float antialias = max(fwidth(jointDistance), 0.003);
                float stoneMask = smoothstep(_JointWidth - antialias,
                    _JointWidth + antialias, jointDistance);

                // Three deliberate flat swatches, without grain or bevels.
                float toneBand = floor(HashCell(cell) * 3);
                float tone = 1 + (toneBand - 1) * _StoneVariation;
                float3 stone = _StoneTint.rgb * tone;
                float3 joint = lerp(_StoneTint.rgb, _GroutTint.rgb, 0.6);
                float3 surface = lerp(joint, stone, stoneMask);

                // This is an unlit overlay, so sample the main-light shadow
                // explicitly to keep the paving grounded in the scene.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half direct = saturate(dot(normalWS, mainLight.direction)) *
                    mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                surface *= lerp(0.58, 1, direct);
                // Default path strength must hide the canyon's green albedo.
                // Keep coverage unmodified so the outer edge stays soft.
                return half4(surface, coverage * saturate(_Strength * 1.6));
            }
            ENDHLSL
        }
    }
}
