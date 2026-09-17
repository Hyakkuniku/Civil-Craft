Shader "CivilCraft/NPC Teleport Dissolve"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _DissolveAmount("Dissolve Amount", Range(0,1)) = 0
        [HDR] _EdgeColor("Edge Color", Color) = (1,0.55,0.12,1)
        _EdgeWidth("Edge Width", Range(0.01,0.25)) = 0.08
        _NoiseScale("Noise Scale", Range(2,40)) = 14
        _AlphaCutoff("Alpha Cutoff", Range(0,1)) = 0.03
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull [_Cull]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EdgeColor;
                float _DissolveAmount;
                float _EdgeWidth;
                float _NoiseScale;
                float _AlphaCutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float ValueNoise(float3 position)
            {
                float3 cell = floor(position);
                float3 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);

                float n000 = Hash31(cell + float3(0,0,0));
                float n100 = Hash31(cell + float3(1,0,0));
                float n010 = Hash31(cell + float3(0,1,0));
                float n110 = Hash31(cell + float3(1,1,0));
                float n001 = Hash31(cell + float3(0,0,1));
                float n101 = Hash31(cell + float3(1,0,1));
                float n011 = Hash31(cell + float3(0,1,1));
                float n111 = Hash31(cell + float3(1,1,1));

                float n00 = lerp(n000, n100, blend.x);
                float n10 = lerp(n010, n110, blend.x);
                float n01 = lerp(n001, n101, blend.x);
                float n11 = lerp(n011, n111, blend.x);
                return lerp(lerp(n00, n10, blend.y), lerp(n01, n11, blend.y), blend.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _AlphaCutoff);

                float noise = ValueNoise(input.positionWS * _NoiseScale);
                clip(noise - _DissolveAmount);

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half direct = saturate(dot(normalWS, mainLight.direction)) *
                              mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 lighting = SampleSH(normalWS) + mainLight.color * direct;
                half edge = 1.0h - smoothstep(
                    _DissolveAmount,
                    _DissolveAmount + max(_EdgeWidth, 0.001),
                    noise);

                half3 color = albedo.rgb * max(lighting, 0.18h) + _EdgeColor.rgb * edge * 1.6h;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
