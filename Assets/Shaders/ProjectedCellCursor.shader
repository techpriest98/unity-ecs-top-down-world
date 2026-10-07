Shader "Game/World/ProjectedCellCursor"
{
    Properties
    {
        _CursorColor("Cursor Color", Color) = (1, 0.902, 0.608, 0.45)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _CursorRect;
            float _CursorDepth;
            float4 _CursorColor;
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(uint id : SV_VertexID)
            {
                const float2 corners[6] = {
                    float2(0,0), float2(0,1), float2(1,1),
                    float2(0,0), float2(1,1), float2(1,0)
                };
                Varyings output;
                output.uv = corners[id];
                output.positionCS = TransformWorldToHClip(float3(
                    _CursorRect.xy + output.uv * _CursorRect.zw, _CursorDepth));
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float edge = min(min(input.uv.x, 1-input.uv.x),
                    min(input.uv.y, 1-input.uv.y));
                float border = 1-step(0.06, edge);
                float fill = 1-smoothstep(0, 0.25, edge);
                return float4(_CursorColor.rgb,
                    max(border * _CursorColor.a, fill * _CursorColor.a * 0.45));
            }
            ENDHLSL
        }
    }
}
