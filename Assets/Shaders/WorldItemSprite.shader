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
            float _GroundLightingEnabled;
            float4 _LightTileRect;
            float4 _LightBottomLeft, _LightBottomRight, _LightTopLeft, _LightTopRight;
            float3 _ItemAmbient, _ItemSunlight;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 maskUv : TEXCOORD1; float2 lightUv : TEXCOORD2; };
            Varyings Vert(uint id : SV_VertexID)
            {
                const float2 corners[6] = { float2(0,0), float2(0,1), float2(1,1), float2(0,0), float2(1,1), float2(1,0) };
                Varyings output;
                float2 corner = corners[id];
                output.positionCS = TransformWorldToHClip(float3(_SpriteRect.xy + corner * _SpriteRect.zw, _SpriteDepth));
                output.uv = _SpriteUv.xy + corner * _SpriteUv.zw;
                output.maskUv = corner;
                output.lightUv = (_SpriteRect.xy + corner * _SpriteRect.zw - _LightTileRect.xy) / max(_LightTileRect.zw, 0.0001);
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_MainTex, item_point_clamp_sampler, input.uv);
                clip(color.a - 0.1);
                if (_OcclusionEnabled > 0.5)
                    clip(0.5 - SAMPLE_TEXTURE2D(_OcclusionMask, item_point_clamp_sampler, input.maskUv).r);
                if (_GroundLightingEnabled > 0.5)
                {
                    float2 tileUv = saturate(input.lightUv);
                    float4 lighting = lerp(lerp(_LightBottomLeft, _LightBottomRight, tileUv.x),
                        lerp(_LightTopLeft, _LightTopRight, tileUv.x), tileUv.y);
                    // Same ambient floor and corner interpolation as ChunkProcedural.
                    color.rgb *= saturate(_ItemAmbient * lerp(0.08, 1.0, lighting.a) + lighting.rgb + _ItemSunlight);
                }
                return color;
            }
            ENDHLSL
        }
    }
}
