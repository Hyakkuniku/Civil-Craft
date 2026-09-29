Shader "CivilCraft/Bridge Break Smoke"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Smoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 p = input.uv * 2.0h - 1.0h;
                half center = length(p);
                half lobeA = length((p - half2(-0.17h, 0.12h)) * half2(1.15h, 0.90h));
                half lobeB = length((p - half2(0.19h, -0.09h)) * half2(0.95h, 1.20h));
                half density = max(1.0h - smoothstep(0.12h, 0.85h, center),
                    max(0.72h * (1.0h - smoothstep(0.10h, 0.75h, lobeA)),
                        0.65h * (1.0h - smoothstep(0.10h, 0.72h, lobeB))));
                half softEdge = 1.0h - smoothstep(0.65h, 1.0h, center);
                half alpha = saturate(density * softEdge * input.color.a * _Tint.a);
                return half4(input.color.rgb * _Tint.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
