Shader "Game/World/LocalProjectionBackground"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-1" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            StructuredBuffer<float4> _LocalTiles;
            struct Varyings { float4 position : SV_POSITION; };
            Varyings Vert(uint id : SV_VertexID, uint instance : SV_InstanceID)
            {
                float2 corner = id == 0 || id == 5 ? float2(0,0) : id == 1 ? float2(0,1) :
                    id == 2 || id == 3 ? float2(1,1) : float2(1,0);
                float4 rect = _LocalTiles[instance];
                Varyings output;
                output.position = TransformWorldToHClip(float3(rect.xy + corner * rect.zw, 0));
                return output;
            }
            float4 Frag(Varyings input) : SV_Target { return float4(0,0,0,1); }
            ENDHLSL
        }
    }
}
