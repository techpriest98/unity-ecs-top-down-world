Shader "Game/World/WorldFog"
{
    Properties { _FogNoise("Periodic Noise", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            // Bit 3 is reserved by this experimental layer; the camera clears stencil.
            Stencil { Ref 8 ReadMask 8 WriteMask 0 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            StructuredBuffer<float4> _FogChunks;
            TEXTURE2D(_FogNoise); SAMPLER(sampler_FogNoise);
            float4 _FogPlane, _FogSeaOrigin, _FogRight, _FogAway, _FogDrift, _FogColor;
            float _FogCoverage;
            float4 _FogPlayerClear; // strength, reserved, inner radius, feather
            float4 _FogPlayerCentre; // actual projected player position + upward offset
            struct Varyings { float4 position : SV_POSITION; float2 world : TEXCOORD0; float2 view : TEXCOORD1; };
            float2 Corner(uint id)
            {
                if(id==0) return float2(0,0); if(id==1) return float2(0,1);
                if(id==2 || id==3) return float2(1,1); if(id==4) return float2(1,0);
                return float2(0,0);
            }
            Varyings Vert(uint id:SV_VertexID, uint instance:SV_InstanceID)
            {
                float4 chunk = _FogChunks[instance];
                float2 corner = Corner(id);
                float width = _FogPlane.w;
                // Same projection as a horizontal Top plane, not camera-space scrolling.
                float2 projected = chunk.xy + float2(-width*.5 + corner.x*width,
                    1.0 + _FogPlane.x + corner.y*width*.5);
                float2 plane = projected - _FogSeaOrigin.xy - float2(0,_FogPlane.x-_FogSeaOrigin.z);
                Varyings o;
                o.position = TransformWorldToHClip(float3(projected, chunk.z));
                o.view = projected;
                o.world = _FogRight.xy * plane.x + _FogAway.xy * plane.y * 2.0;
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float noise = SAMPLE_TEXTURE2D(_FogNoise, sampler_FogNoise,
                    (i.world-_FogDrift.xy*.5)/max(1.0,_FogPlane.z)).r;
                float opacity = lerp(saturate(_FogCoverage), 1.0, smoothstep(.22,.78,noise)) * _FogPlane.y;
                // Projected world units keep the radius stable with zoom and
                // follow the player even when the camera has a screen offset.
                float distanceFromPlayer = length(i.view - _FogPlayerCentre.xy);
                float clear = 1.0-smoothstep(_FogPlayerClear.z,
                    _FogPlayerClear.z+_FogPlayerClear.w, distanceFromPlayer);
                opacity *= 1.0-clear*_FogPlayerClear.x;
                return float4(_FogColor.rgb, opacity);
            }
            ENDHLSL
        }
    }
}
