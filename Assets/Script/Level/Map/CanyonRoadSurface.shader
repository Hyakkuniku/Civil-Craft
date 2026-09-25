Shader "Civil Craft/Canyon Road Surface"
{
    Properties
    {
        _RoadTint ("Road surface color", Color) = (.30, .32, .34, 1)
        _ShoulderTint ("Shoulder color", Color) = (.38, .38, .37, 1)
        _EdgeLineTint ("Edge line color", Color) = (.82, .80, .72, 1)
        _CenterLineTint ("Center line color", Color) = (1, .76, .20, 1)
        _SidewalkTint ("Sidewalk color", Color) = (.66, .65, .62, 1)
        _CurbTint ("Curb color", Color) = (.82, .80, .74, 1)
        _SidewalkWidth ("Sidewalk width (road half-widths)", Range(0,.6)) = .22
        _CurbWidth ("Curb width (road half-widths)", Range(.005,.12)) = .045
        _Strength ("Strength", Range(0,1)) = .85
        _Width ("Width", Float) = .075
        _Softness ("Softness", Float) = .16
        _MarkingStrength ("Marking strength", Range(0,1)) = .85
        _EdgeLineWidth ("Edge line width", Range(.005,.12)) = .035
        _CenterLineWidth ("Center line width", Range(.005,.12)) = .045
        _DashLength ("Center dash length (road widths)", Range(.2,4)) = 1.2
        _DashGap ("Center dash gap (road widths)", Range(.2,4)) = .8
    }
    SubShader
    {
        // Draw after the dirt overlay if both are present on one canyon.
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-49" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Use actual gray asphalt color; multiplying the underlying grass
            // would turn the road olive green.
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
                float4 _RoadTint, _ShoulderTint, _EdgeLineTint, _CenterLineTint;
                float4 _SidewalkTint, _CurbTint;
                float4 _PathBounds;
                float4 _Segments[16];
                float4 _SegmentWidths[16];
                float _Strength, _Width, _Softness, _MarkingStrength;
                float _EdgeLineWidth, _CenterLineWidth, _DashLength, _DashGap;
                float _SidewalkWidth, _CurbWidth;
                int _SegmentCount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS + normalize(o.normalWS) * .016);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), f.x), f.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Like the dirt overlay, the road belongs on the upper canyon
                // surface and never paints vertical cliff faces.
                clip(normalize(i.normalWS).y - .85);
                float2 p = (i.positionWS.xz - _PathBounds.xy) * _PathBounds.z;
                float nearest = 100;
                float distanceAlong = 0;
                for (int n = 0; n < _SegmentCount; n++)
                {
                    float2 a = _Segments[n].xy;
                    float2 ab = _Segments[n].zw - a;
                    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), .000001));
                    float candidate = length(p - a - ab * t) / max(.1, _SegmentWidths[n].x);
                    if (candidate < nearest)
                    {
                        nearest = candidate;
                        distanceAlong = t * _SegmentWidths[n].y;
                    }
                }

                float radius = max(_Width * .5, .0001);
                float grain = Noise(p * 180);
                // Never perturb the footprint or markings with noise. Straight
                // authored route segments must render with straight edges.
                float normalizedDistance = nearest / radius;
                // A crisp road/curb boundary separates asphalt from concrete.
                // The sidewalk extends outside the road's authored width.
                float edgeFeather = min(_Softness, .08);
                float roadMask = 1 - smoothstep(1 - edgeFeather, 1, normalizedDistance);
                float hasSidewalk = step(.001, _SidewalkWidth);
                float sidewalkMask = hasSidewalk *
                    (1 - smoothstep(1 + _SidewalkWidth - edgeFeather,
                        1 + _SidewalkWidth, normalizedDistance));
                float coverage = max(roadMask, sidewalkMask);
                clip(coverage - .002);

                float3 paved = lerp(_RoadTint.rgb, _ShoulderTint.rgb,
                    smoothstep(.78, .98, normalizedDistance));
                paved *= lerp(.98, 1.02, grain);
                float3 roadColor = paved;

                float aa = max(fwidth(normalizedDistance), .003);
                float edgeLine = 1 - smoothstep(_EdgeLineWidth * .5,
                    _EdgeLineWidth * .5 + aa, abs(normalizedDistance - .82));
                float centerLine = 1 - smoothstep(_CenterLineWidth * .5,
                    _CenterLineWidth * .5 + aa, normalizedDistance);
                float period = max(_DashLength + _DashGap, .001);
                float phase = frac(distanceAlong / max(_Width * period, .0001));
                float dash = 1 - smoothstep(_DashLength / period - .015,
                    _DashLength / period + .015, phase);
                roadColor = lerp(roadColor, _EdgeLineTint.rgb, edgeLine * _MarkingStrength);
                roadColor = lerp(roadColor, _CenterLineTint.rgb, centerLine * dash * _MarkingStrength);

                // Subtle cross-joints distinguish concrete sidewalk from a
                // painted shoulder, without changing the road or terrain mesh.
                float jointPhase = frac(distanceAlong / max(_Width * 1.5, .0001));
                float jointDistance = min(jointPhase, 1 - jointPhase);
                float joint = 1 - smoothstep(.008, .024, jointDistance);
                float3 sidewalkColor = _SidewalkTint.rgb * (1 - joint * .16);
                float3 surfaceColor = lerp(sidewalkColor, roadColor, roadMask);
                float curb = hasSidewalk * (1 - smoothstep(_CurbWidth * .5,
                    _CurbWidth * .5 + aa, abs(normalizedDistance - 1)));
                surfaceColor = lerp(surfaceColor, _CurbTint.rgb, curb);

                // The overlay is not a multiply decal anymore, so receive the
                // main-light shadow explicitly (including the player's shadow).
                half3 normalWS = normalize(i.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half direct = saturate(dot(normalWS, mainLight.direction)) *
                    mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                surfaceColor *= lerp(.5, 1, direct);
                return half4(surfaceColor, coverage * saturate(_Strength * 1.5));
            }
            ENDHLSL
        }
    }
}
