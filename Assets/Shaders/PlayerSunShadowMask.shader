Shader "Hidden/Game/PlayerSunShadowMask"
{
    Properties { _SpriteArray("Sprite atlas", 2DArray) = "" {} }
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_ARRAY(_SpriteArray);
            SamplerState silhouette_point_clamp_sampler;
            float4 _PartRects[8], _PartUvs[8];
            float _PartPages[8];
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float page : TEXCOORD1; };
            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                float2 corners[6] = {float2(0,0),float2(0,1),float2(1,1),float2(0,0),float2(1,1),float2(1,0)};
                float2 corner = corners[vertex];
                float2 cell = corner * _PartRects[instance].xy + _PartRects[instance].zw;
                Varyings output;
                output.position = float4(cell * 2 - 1, 0, 1);
                // Match RenderTexture UV convention on D3D and GL.
                #if UNITY_UV_STARTS_AT_TOP
                    output.position.y = -output.position.y;
                #endif
                output.uv = corner * _PartUvs[instance].xy + _PartUvs[instance].zw;
                output.page = _PartPages[instance];
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D_ARRAY(_SpriteArray, silhouette_point_clamp_sampler, input.uv, input.page).a;
                return step(.5, alpha).xxxx;
            }
            ENDHLSL
        }
    }
}
