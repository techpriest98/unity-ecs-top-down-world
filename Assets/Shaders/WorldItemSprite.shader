Shader "Game/World/ItemSprite"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
        [HideInInspector] _FogStencilRef("Fog Occlusion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            // Write only actual visible sprite pixels (alpha/occlusion clips
            // in Frag still apply). Below-fog sprites clear this same bit.
            Stencil { Ref [_FogStencilRef] ReadMask 8 WriteMask 8 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            TEXTURE2D(_LocalTileCoverage);
            float4 _LocalTileCoverageBounds;
            float _ItemLocalOnly;
            SamplerState item_point_clamp_sampler;
            float _TerrainDepthOrigin;
            float ItemSurfaceDepth(float anchorDepth, float projectedY, float centreY, float grounded)
            {
                float physicalDepth = anchorDepth + 2.0 * (projectedY - centreY) * grounded - _TerrainDepthOrigin - .002;
                return physicalDepth;
            }
            struct WorldItemDrawData
            {
                float4 Rect, Uv, Reserved, LightRect;
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
                float2 projected = data.Rect.xy + corner * data.Rect.zw;
                float depth = ItemSurfaceDepth(data.Flags.w, projected.y,
                    data.Rect.y + data.Rect.w * .5, data.Flags.z);
                output.positionCS = TransformWorldToHClip(float3(projected, depth));
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
                if (_ItemLocalOnly > .5)
                {
                    float2 projected = data.Rect.xy + input.maskUv * data.Rect.zw;
                    float2 uv = (projected - _LocalTileCoverageBounds.xy) / max(_LocalTileCoverageBounds.zw, .0001);
                    clip(all(uv >= 0) && all(uv < 1) ? 1.0 : -1.0);
                    clip(SAMPLE_TEXTURE2D_LOD(_LocalTileCoverage, item_point_clamp_sampler, uv, 0).r - .5);
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
