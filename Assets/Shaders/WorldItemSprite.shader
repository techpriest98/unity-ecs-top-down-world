Shader "Game/World/ItemSprite"
{
    Properties { _MainTex("Sprite", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            TEXTURE2D(_OcclusionMask);
            float _OcclusionEnabled;
            SamplerState item_point_clamp_sampler;
            float4 _SpriteRect;
            float4 _SpriteUv;
            float _SpriteDepth;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 maskUv : TEXCOORD1; };
            Varyings Vert(uint id : SV_VertexID)
            {
                const float2 corners[6] = { float2(0,0), float2(0,1), float2(1,1), float2(0,0), float2(1,1), float2(1,0) };
                Varyings output;
                float2 corner = corners[id];
                output.positionCS = TransformWorldToHClip(float3(_SpriteRect.xy + corner * _SpriteRect.zw, _SpriteDepth));
                output.uv = _SpriteUv.xy + corner * _SpriteUv.zw;
                output.maskUv = corner;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_MainTex, item_point_clamp_sampler, input.uv);
                clip(color.a - 0.1);
                if (_OcclusionEnabled > 0.5)
                    clip(0.5 - SAMPLE_TEXTURE2D(_OcclusionMask, item_point_clamp_sampler, input.maskUv).r);
                return color;
            }
            ENDHLSL
        }
    }
}
