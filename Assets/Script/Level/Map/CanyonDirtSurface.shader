Shader "Civil Craft/Canyon Dirt Surface"
{
    Properties
    {
        _DirtTint ("Dirt color", Color) = (.78, .70, .60, 1)
        _Strength ("Strength", Range(0,1)) = .65
        _Width ("Width", Float) = .075
        _Softness ("Softness", Float) = .35
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Cover the green canyon surface; only the path's feathered edge blends.
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
                float4 _DirtTint;
                float4 _PathBounds;
                float4 _Segments[16];
                float4 _SegmentWidths[16];
                float _Strength, _Width, _Softness;
                int _SegmentCount;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                // Separate only the visual overlay from the canyon depth surface.
                // World-space units keep this consistent on scaled canyon meshes.
                o.positionCS = TransformWorldToHClip(o.positionWS + normalize(o.normalWS) * .015);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(Hash(i), Hash(i+float2(1,0)), f.x),
                    lerp(Hash(i+float2(0,1)), Hash(i+1), f.x), f.y);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // Cliff faces receive no dirt treatment, even where the route crosses their silhouette.
                clip(normalize(i.normalWS).y - .85);
                float2 p = (i.positionWS.xz - _PathBounds.xy) * _PathBounds.z;
                float d = 100;
                for (int n = 0; n < _SegmentCount; n++)
                {
                    float2 a = _Segments[n].xy, ab = _Segments[n].zw-a;
                    float t = saturate(dot(p-a,ab)/max(dot(ab,ab), .000001));
                    d = min(d, length(p-a-ab*t) / max(.1, _SegmentWidths[n].x));
                }
                float radius = _Width*.5;
                float roughness = (Noise(p*90)-.5)*radius*.18;
                float mask = 1-smoothstep(radius*(1-_Softness), radius, d+roughness);
                clip(mask-.002);
                float grain = lerp(.90, 1.0, Noise(p * 180));
                float3 normalWS = normalize(i.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half direct = saturate(dot(normalWS, mainLight.direction)) *
                    mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float3 surface = _DirtTint.rgb * grain * lerp(.58, 1, direct);
                // The normal .65 strength fully covers the green terrain at the
                // center, while the mask still feathers the route's edges.
                return half4(surface, mask * saturate(_Strength * 1.6));
            }
            ENDHLSL
        }
    }
}
