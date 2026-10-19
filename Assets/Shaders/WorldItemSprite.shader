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
            SamplerState item_point_clamp_sampler;
            struct WorldItemDrawData
            {
                float4 Rect, Uv, MaskPixels, LightRect;
                float4 BottomLeft, BottomRight, TopLeft, TopRight;
                float4 Ambient, Sunlight, Flags;
            };
            StructuredBuffer<WorldItemDrawData> _WorldItems;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 maskUv : TEXCOORD1; float2 lightUv : TEXCOORD2; nointerpolation uint instanceId : TEXCOORD3; };
            Varyings Vert(uint id : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                WorldItemDrawData data = _WorldItems[instanceId];
                const float2 corners[6] = { float2(0,0), float2(0,1), float2(1,1), float2(0,0), float2(1,1), float2(1,0) };
                Varyings output;
                float2 corner = corners[id];
                output.positionCS = TransformWorldToHClip(float3(data.Rect.xy + corner * data.Rect.zw, data.Flags.x));
                output.uv = data.Uv.xy + corner * data.Uv.zw;
                output.instanceId = instanceId;
                output.maskUv = corner;
                output.lightUv = (data.Rect.xy + corner * data.Rect.zw - data.LightRect.xy) / max(data.LightRect.zw, 0.0001);
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                WorldItemDrawData data = _WorldItems[input.instanceId];
                float4 color = SAMPLE_TEXTURE2D(_MainTex, item_point_clamp_sampler, input.uv);
                clip(color.a - 0.1);
                if (data.Flags.z > 0.5)
                {
                    // Integer atlas addressing exactly preserves point/clamp
                    // sampling, including uv=1 at the edge of adjacent slots.
                    uint2 size = max(uint2(1,1), (uint2)data.MaskPixels.zw);
                    uint2 pixel = min((uint2)(saturate(input.maskUv) * size), size - 1);
                    clip(0.5 - _OcclusionMask.Load(int3((uint2)data.MaskPixels.xy + pixel, 0)).r);
                }
                if (data.Flags.y > 0.5)
                {
                    float2 tileUv = saturate(input.lightUv);
                    float4 lighting = lerp(lerp(data.BottomLeft, data.BottomRight, tileUv.x),
                        lerp(data.TopLeft, data.TopRight, tileUv.x), tileUv.y);
                    // Same ambient floor and corner interpolation as ChunkProcedural.
                    color.rgb *= saturate(data.Ambient.rgb * lerp(0.08, 1.0, lighting.a) + lighting.rgb + data.Sunlight.rgb);
                }
                return color;
            }
            ENDHLSL
        }
    }
}
