Shader "Game/World/WorldFogMask"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+99" }
        Pass
        {
            Cull Off ZWrite Off ZTest Equal ColorMask 0
            Stencil { Ref 8 ReadMask 8 WriteMask 8 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ProjectedCellPacking.hlsl"
            StructuredBuffer<ProjectedCellRenderData> _ProjectedCells;
            TEXTURE2D(_ProjectionClipMask); SAMPLER(sampler_ProjectionClipMask);
            TEXTURE2D(_FogNoise); SAMPLER(sampler_FogNoise);
            float4 _FogEdge, _FogSeaOrigin, _FogRight, _FogAway, _FogDrift;
            float4 _ProjectionClipMaskBounds;
            float _FogHeight, _FogClipEnabled;
            struct Varyings {
                float4 position:SV_POSITION; float2 projected:TEXCOORD0;
                float height:TEXCOORD1; nointerpolation uint cut:TEXCOORD2;
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
                bool lower = (cell.BlockData & (1u<<12)) != 0;
                bool cutTop = face==1u && HasCutTop(cell.FaceData);
                bool cutSide = face!=1u && HasCutSide(cell.FaceData);
                float source = (cell.FaceData>>24)&255u;
                float height = face==1u ? source + (cutTop?1.0:0.0) - (lower?.5:0.0) :
                    source + (face==3u || lower?.25:.75) + (corner.y-.5)*.5;
                Varyings o; o.position=TransformWorldToHClip(float3(projected,cell.ChunkPosition.z));
                o.projected=projected; o.height=height; o.cut=(cutTop||cutSide)?1u:0u;
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float clearance = i.height-_FogHeight-.01;
                clip(clearance);
                if(_FogClipEnabled>.5 && i.cut==0u)
                {
                    float2 uv=(i.projected-_ProjectionClipMaskBounds.xy)/_ProjectionClipMaskBounds.zw;
                    if(all(uv>=0)&&all(uv<1))
                        clip(.5-SAMPLE_TEXTURE2D_LOD(_ProjectionClipMask,sampler_ProjectionClipMask,uv,0).r);
                }
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
