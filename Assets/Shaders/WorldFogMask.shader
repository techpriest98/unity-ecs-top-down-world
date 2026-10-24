Shader "Game/World/WorldFogMask"
{
    Properties {
        [HideInInspector] _FogZTest("Depth Test", Float) = 3
        [HideInInspector] _FogZWrite("Depth Write", Float) = 0
        [HideInInspector] _FogColorMask("Color Mask", Float) = 0
        [HideInInspector] _FogStencilWrite("Stencil Write", Float) = 8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+99" }
        Pass
        {
            Cull Off ZWrite [_FogZWrite] ZTest [_FogZTest] ColorMask [_FogColorMask]
            Stencil { Ref 8 ReadMask 8 WriteMask [_FogStencilWrite] Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _FOG_SKY_ALPHA
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ProjectedCellPacking.hlsl"
            StructuredBuffer<ProjectedCellRenderData> _ProjectedCells;
            TEXTURE2D(_FogNoise); SAMPLER(sampler_FogNoise);
            float4 _FogEdge, _FogSeaOrigin, _FogRight, _FogAway, _FogDrift;
            float _FogHeight;
            float _FogWater, _WaterTopInsetPixels;
            TEXTURE2D(_LocalTileCoverage); SAMPLER(sampler_LocalTileCoverage);
            float4 _LocalTileCoverageBounds;
            float _LocalTileCoverageEnabled;
            float4x4 _FogSkyViewProjection;
            struct Varyings {
                float4 position:SV_POSITION; float2 projected:TEXCOORD0;
                float height:TEXCOORD1; nointerpolation uint cut:TEXCOORD2;
                float sky:TEXCOORD3;
            };
            float2 Corner(uint id)
            {
                if(id==0) return float2(0,0); if(id==1) return float2(0,1);
                if(id==2 || id==3) return float2(1,1); if(id==4) return float2(1,0);
                return float2(0,0);
            }
            Varyings Vert(uint id:SV_VertexID, uint instance:SV_InstanceID)
            {
                ProjectedCellRenderData cell = _ProjectedCells[instance];
                uint2 tile = UnpackPosition(cell.Position);
                float2 corner = Corner(id);
                float2 projected = cell.ChunkPosition.xy + float2(-8.0 + tile.x + corner.x,
                    264.0 - (tile.y+1)*.5 + corner.y*.5);
                uint face = GetFaceType(cell.BlockData);
                if (_FogWater > .5 && face == 1u && HasTopInset(cell.FaceData))
                    projected.y -= corner.y * .5 * _WaterTopInsetPixels / 16.0;
                bool lower = (cell.BlockData & (1u<<12)) != 0;
                bool cutTop = _FogWater < .5 && face==1u && HasCutTop(cell.FaceData);
                bool cutSide = _FogWater < .5 && face!=1u && HasCutSide(cell.FaceData);
                float source = (cell.FaceData>>24)&255u;
                float height = face==1u ? source + (cutTop?1.0:0.0) - (lower?.5:0.0) :
                    source + (face==3u || lower?.25:.75) + (corner.y-.5)*.5;
                Varyings o; o.position=TransformWorldToHClip(float3(projected,cell.ChunkPosition.z));
                #if defined(_FOG_SKY_ALPHA)
                    o.position=mul(_FogSkyViewProjection,float4(projected,cell.ChunkPosition.z,1));
                #endif
                o.projected=projected; o.height=height; o.cut=(cutTop||cutSide)?1u:0u;
                // Reuse the same smoothed skylight as terrain/water lighting.
                float bottomSky=lerp(GetCornerLight(cell.LocalLightBottomLeft).a,
                    GetCornerLight(cell.LocalLightBottomRight).a,corner.x);
                float topSky=lerp(GetCornerLight(cell.LocalLightTopLeft).a,
                    GetCornerLight(cell.LocalLightTopRight).a,corner.x);
                o.sky=lerp(bottomSky,topSky,corner.y);
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                if (_LocalTileCoverageEnabled > .5)
                {
                    float2 uv = (i.projected - _LocalTileCoverageBounds.xy) / _LocalTileCoverageBounds.zw;
                    if (all(uv >= 0) && all(uv < 1))
                        clip(.5 - SAMPLE_TEXTURE2D_LOD(_LocalTileCoverage, sampler_LocalTileCoverage, uv, 0).r);
                }
                #if defined(_FOG_SKY_ALPHA)
                    return smoothstep(.05,.85,saturate(i.sky)).xxxx;
                #endif
                float clearance = i.height-_FogHeight-.01;
                clip(clearance);
                // Perturb only the contact-height band, not every rock tile.
                // Cut caps and high terrain remain fully occluding. Zero width
                // restores the original mask without an additional noise read.
                if(i.cut==0u && _FogEdge.x>0.0 && clearance<_FogEdge.x)
                {
                    float2 plane=i.projected-_FogSeaOrigin.xy-float2(0,_FogHeight-_FogSeaOrigin.z);
                    float2 world=_FogRight.xy*plane.x+_FogAway.xy*plane.y*2.0;
                    float noise=SAMPLE_TEXTURE2D_LOD(_FogNoise,sampler_FogNoise,
                        (world-_FogDrift.xy*.5)/max(1.0,_FogEdge.y),0).r;
                    clip(clearance-noise*_FogEdge.x);
                }
                return 0;
            }
            ENDHLSL
        }
    }
}
