Shader "Game/World/TreeSprite"
{
    Properties {
        _TreeDiffuse("Diffuse bank", 2DArray)="" {}
        [HideInInspector] _TreeZTest("Depth Test", Float)=4
        [HideInInspector] _TreeZWrite("Depth Write", Float)=1
        [HideInInspector] _TreeColorMask("Color Mask", Float)=15
        [HideInInspector] _TreeStencilRef("Stencil Reference", Float)=0
        [HideInInspector] _TreeFogMaskPass("Fog Mask Pass", Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest+1" "RenderType"="TransparentCutout" }
        Pass
        {
            Cull Off ZWrite [_TreeZWrite] ZTest [_TreeZTest] ColorMask [_TreeColorMask]
            // Visible tree pixels replace terrain's fog classification. The
            // later depth-equal mask pass marks only exposed high tree pixels.
            Stencil { Ref [_TreeStencilRef] ReadMask 8 WriteMask 8 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_ARRAY(_TreeDiffuse); TEXTURE2D_ARRAY(_TreeNormals);
            TEXTURE2D_ARRAY(_TreeSurfaces);
            SamplerState tree_point_clamp_sampler;
            // C# TreeDrawData: four float4 values, 64-byte structured-buffer stride.
            struct TreeDrawData { float4 Rect, Base, Pixels, Layers; };
            StructuredBuffer<TreeDrawData> _TreeInstances;
            float4 _TreeRight, _TreeToward, _TreePlayer;
            float4 _TreeAmbient, _TreeSun, _TreeSunDirection;
            float _TerrainDepthOrigin, _TreeLocalOnly;
            float _TreeFogMaskPass, _TreeFogEnabled, _TreeFogHeight;
            float4 _TreeFogEdge, _TreeFogSeaOrigin, _TreeFogRight, _TreeFogAway, _TreeFogDrift;
            TEXTURE2D(_TreeFogNoise);
            SamplerState tree_fog_linear_repeat_sampler;
            float TreeCardDepth(float anchorDepth)
            {
                return anchorDepth - _TerrainDepthOrigin - .001;
            }
            TEXTURE2D(_TreeLocalCoverage);
            SamplerState tree_local_point_clamp_sampler;
            float4 _TreeLocalCoverageBounds, _TreeLocalClipPlane;
            bool TreeReceiverInCutVolume(float3 receiver, float4 plane, float2 toward)
            {
                return receiver.y > plane.z + .001 && dot(floor(receiver.xz) - plane.xy, toward) > 0;
            }
            static const float _LocalNormalStrength=1;
            #include "DetailedLocalLighting.hlsl"
            struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 projected:TEXCOORD1; nointerpolation uint instance:TEXCOORD2; };
            Varyings Vert(uint id:SV_VertexID, uint instance:SV_InstanceID)
            {
                const float2 corners[6]={float2(0,0),float2(0,1),float2(1,1),float2(0,0),float2(1,1),float2(1,0)};
                TreeDrawData data=_TreeInstances[instance];
                Varyings o; o.instance=instance; o.uv=corners[id]; o.projected=data.Rect.xy+o.uv*data.Rect.zw;
                o.position=TransformWorldToHClip(float3(o.projected,TreeCardDepth(data.Layers.z))); return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                TreeDrawData data=_TreeInstances[i.instance];
                float layer=data.Layers.x;
                float4 color=SAMPLE_TEXTURE2D_ARRAY(_TreeDiffuse,tree_point_clamp_sampler,i.uv,layer);
                clip(color.a-.1);
                float2 surface=SAMPLE_TEXTURE2D_ARRAY(_TreeSurfaces,tree_point_clamp_sampler,i.uv,layer).rg;
                float x=(i.uv.x*data.Pixels.z-data.Pixels.z*.5)/data.Pixels.x;
                float3 receiver=data.Base.xyz+(_TreeRight.xyz*x+float3(0,surface.x,0)+_TreeToward.xyz*surface.y)*data.Base.w;
                if (_TreeLocalOnly > .5 || (_TreeLocalClipPlane.w > .5 && TreeReceiverInCutVolume(receiver, _TreeLocalClipPlane, _TreeToward.xz)))
                {
                    float2 localUv = (i.projected - _TreeLocalCoverageBounds.xy) / max(_TreeLocalCoverageBounds.zw, .0001);
                    float coverage = 0;
                    if (all(localUv >= 0) && all(localUv < 1))
                        coverage = SAMPLE_TEXTURE2D_LOD(_TreeLocalCoverage, tree_local_point_clamp_sampler, localUv, 0).r;
                    if (_TreeLocalOnly > .5) clip(coverage - .5);
                    if (_TreeLocalClipPlane.w > .5 && TreeReceiverInCutVolume(receiver, _TreeLocalClipPlane, _TreeToward.xz))
                        clip(.5 - coverage);
                }
                if (_TreeFogMaskPass > .5)
                {
                    clip(_TreeFogEnabled - .5);
                    float clearance = receiver.y - _TreeFogHeight - .01;
                    clip(clearance);
                    if (_TreeFogEdge.x > 0 && clearance < _TreeFogEdge.x)
                    {
                        float2 plane = i.projected - _TreeFogSeaOrigin.xy - float2(0, _TreeFogHeight - _TreeFogSeaOrigin.z);
                        float2 world = _TreeFogRight.xy * plane.x + _TreeFogAway.xy * plane.y * 2;
                        float noise = SAMPLE_TEXTURE2D_LOD(_TreeFogNoise, tree_fog_linear_repeat_sampler,
                            (world - _TreeFogDrift.xy * .5) / max(1.0, _TreeFogEdge.y), 0).r;
                        clip(clearance - noise * _TreeFogEdge.x);
                    }
                    return 0;
                }
                float3 encoded=SAMPLE_TEXTURE2D_ARRAY(_TreeNormals,tree_point_clamp_sampler,i.uv,layer).xyz*2-1;
                float3 normal=normalize(_TreeRight.xyz*encoded.x+float3(0,encoded.y,0)+_TreeToward.xyz*encoded.z);
                float3 local=SingleLocalLightDiagnostic(i.projected,receiver.y,normal,normal,float3(0,1,0),float3(0,0,0));
                float sun=saturate(dot(normal,normalize(_TreeSunDirection.xyz+float3(0,.000001,0))));
                color.rgb*=saturate(_TreeAmbient.rgb+_TreeSun.rgb*_TreeSun.w*sun+local);
                return color;
            }
            ENDHLSL
        }
    }
}
