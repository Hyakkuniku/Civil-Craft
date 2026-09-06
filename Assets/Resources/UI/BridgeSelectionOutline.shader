Shader "CivilCraft/Bridge Selection Outline"
{
    Properties { _OutlineColor ("Outline Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct v2f_img
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };
        sampler2D _BridgeMaskSource;
        float4 _BridgeMaskTexelSize;
        float4x4 _BridgeMaskMVP;
        sampler2D _BridgeOriginalMask;
        float4 _BridgeOutlineColor;
        float _BridgeOutlinePixels;
        float _BridgeClosingStep;

        float4 MaskVertex(float4 vertex : POSITION) : SV_POSITION { return mul(_BridgeMaskMVP, vertex); }
        v2f_img OutlineFullscreenVertex(uint id : SV_VertexID)
        {
            v2f_img output;
            // URP handles Vulkan's landscape display pre-rotation here. A raw
            // clip-space triangle can rotate the mask relative to the scene.
            output.pos = GetFullScreenTriangleVertexPosition(id);
            output.uv = GetFullScreenTriangleTexCoord(id);
            return output;
        }
        half4 MaskFragment() : SV_Target { return 1; }

        float Expand(float2 uv, float2 direction)
        {
            float result = 0;
            [unroll] for (int i = -4; i <= 4; i++)
                result = max(result, tex2D(_BridgeMaskSource, uv + direction * i * _BridgeClosingStep).r);
            return result;
        }
        float Contract(float2 uv, float2 direction)
        {
            float result = 1;
            [unroll] for (int i = -4; i <= 4; i++)
                result = min(result, tex2D(_BridgeMaskSource, uv + direction * i * _BridgeClosingStep).r);
            return result;
        }
        half4 ExpandX(v2f_img input) : SV_Target { return Expand(input.uv, float2(_BridgeMaskTexelSize.x, 0)); }
        half4 ExpandY(v2f_img input) : SV_Target { return Expand(input.uv, float2(0, _BridgeMaskTexelSize.y)); }
        half4 ContractX(v2f_img input) : SV_Target { return Contract(input.uv, float2(_BridgeMaskTexelSize.x, 0)); }
        half4 ContractY(v2f_img input) : SV_Target { return Contract(input.uv, float2(0, _BridgeMaskTexelSize.y)); }
        float Silhouette(float2 uv)
        {
            return max(tex2D(_BridgeMaskSource, uv).r, tex2D(_BridgeOriginalMask, uv).r);
        }
        half4 Composite(v2f_img input) : SV_Target
        {
            float center = Silhouette(input.uv);
            float expanded = center;
            [unroll] for (int i = 0; i < 16; i++)
            {
                float angle = i * 6.2831853 / 16;
                float2 offset = float2(cos(angle), sin(angle)) * _BridgeMaskTexelSize.xy * _BridgeOutlinePixels;
                expanded = max(expanded, Silhouette(input.uv + offset));
            }
            return half4(_BridgeOutlineColor.rgb, saturate(expanded - center));
        }
        ENDHLSL
        Pass
        {
            Name "MeshMask"
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "CloseExpandX"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVertex
            #pragma fragment ExpandX
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "CloseExpandY"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVertex
            #pragma fragment ExpandY
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "CloseContractX"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVertex
            #pragma fragment ContractX
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "CloseContractY"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVertex
            #pragma fragment ContractY
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "WhiteOuterBorder"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVertex
            #pragma fragment Composite
            #pragma target 3.5
            ENDHLSL
        }
    }
}
