Shader "Game/World/TreeSprite"
{
    Properties { _TreeDiffuse("Diffuse bank", 2DArray)="" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest+1" "RenderType"="TransparentCutout" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_ARRAY(_TreeDiffuse); TEXTURE2D_ARRAY(_TreeNormals);
            TEXTURE2D_ARRAY(_TreeSurfaces); TEXTURE2D_ARRAY(_TreeMasks);
            SamplerState tree_point_clamp_sampler;
            // C# TreeDrawData: four float4 values, 64-byte structured-buffer stride.
            struct TreeDrawData { float4 Rect, Base, Pixels, Layers; };
            StructuredBuffer<TreeDrawData> _TreeInstances;
            float4 _TreeRight, _TreeToward, _TreePlayer;
            float4 _TreeAmbient, _TreeSun, _TreeSunDirection;
            static const float _LocalNormalStrength=1;
            #include "DetailedLocalLighting.hlsl"
            struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 projected:TEXCOORD1; nointerpolation uint instance:TEXCOORD2; };
            Varyings Vert(uint id:SV_VertexID, uint instance:SV_InstanceID)
            {
                const float2 corners[6]={float2(0,0),float2(0,1),float2(1,1),float2(0,0),float2(1,1),float2(1,0)};
                TreeDrawData data=_TreeInstances[instance];
                Varyings o; o.instance=instance; o.uv=corners[id]; o.projected=data.Rect.xy+o.uv*data.Rect.zw;
                o.position=TransformWorldToHClip(float3(o.projected,-.9)); return o;
            }
            struct Output { float4 color:SV_Target; float depth:SV_Depth; };
            Output Frag(Varyings i)
            {
                TreeDrawData data=_TreeInstances[i.instance];
                float layer=data.Layers.x;
                float4 color=SAMPLE_TEXTURE2D_ARRAY(_TreeDiffuse,tree_point_clamp_sampler,i.uv,layer);
                clip(color.a-.1);
                clip(.5-SAMPLE_TEXTURE2D_ARRAY(_TreeMasks,tree_point_clamp_sampler,i.uv,data.Layers.y).r);
                float2 surface=SAMPLE_TEXTURE2D_ARRAY(_TreeSurfaces,tree_point_clamp_sampler,i.uv,layer).rg;
                float x=(i.uv.x*data.Pixels.z-data.Pixels.z*.5)/data.Pixels.x;
                float3 receiver=data.Base.xyz+(_TreeRight.xyz*x+float3(0,surface.x,0)+_TreeToward.xyz*surface.y)*data.Base.w;
                float3 encoded=SAMPLE_TEXTURE2D_ARRAY(_TreeNormals,tree_point_clamp_sampler,i.uv,layer).xyz*2-1;
                float3 normal=normalize(_TreeRight.xyz*encoded.x+float3(0,encoded.y,0)+_TreeToward.xyz*encoded.z);
                float3 local=SingleLocalLightDiagnostic(i.projected,receiver.y,normal,normal,float3(0,1,0),float3(0,0,0));
                float sun=saturate(dot(normal,normalize(_TreeSunDirection.xyz+float3(0,.000001,0))));
                color.rgb*=saturate(_TreeAmbient.rgb+_TreeSun.rgb*_TreeSun.w*sun+local);
                // Player parts occupy depth -1. Actual per-pixel tree depth selects
                // which side of that plane to draw, independently of sprite height.
                float worldDepth=dot(receiver.xz-_TreePlayer.xz,_TreeToward.xz);
                float renderDepth=-1-sign(worldDepth)*.02-worldDepth*.001;
                float4 clipPosition=TransformWorldToHClip(float3(i.projected,renderDepth));
                Output o; o.color=color; o.depth=clipPosition.z/clipPosition.w; return o;
            }
            ENDHLSL
        }
    }
}
