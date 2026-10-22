Shader "Game/Player/AnimatedNormals"
{
    Properties
    {
        _SpriteArray("Animation Atlas", 2DArray) = "" {}
        _NormalArray("Normal Atlas (linear RGB)", 2DArray) = "" {}
        _PaintedArray("Painted Shade Atlas", 2DArray) = "" {}
        [HideInInspector] _PaintedDetailsEnabled("Painted Atlas Available", Float) = 0
        _NormalStrength("Normal Strength", Range(0,1)) = 1
        [Toggle] _NormalDebug("Show Encoded Normals", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _UvScaleOffset("Atlas UV", Vector) = (1,1,0,0)
        [HideInInspector] _SpriteIndex("Atlas Page", Float) = 0
        [HideInInspector] _PlayerLightColor("Legacy Lighting", Color) = (1,1,1,1)
        [HideInInspector] _SpriteGeometry("Frame Size / Offset", Vector) = (0,0,0,0)
        [HideInInspector] _SpriteLightingDepth("Physical Lighting Depth", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Name "PlayerForward"
            // The project uses Renderer2D, whose renderer list accepts
            // SRPDefaultUnlit / Universal2D, not UniversalForward.
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Layer Z offsets must decide overlap, not transparent draw order.
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_ARRAY(_SpriteArray);
            TEXTURE2D_ARRAY(_NormalArray);
            TEXTURE2D_ARRAY(_PaintedArray);
            SamplerState player_point_clamp_sampler;
            CBUFFER_START(UnityPerMaterial)
                float4 _UvScaleOffset, _PlayerLightColor, _SpriteGeometry;
                float _SpriteIndex, _NormalStrength, _NormalDebug;
                float _Cutoff;
                float _PaintedDetailsEnabled;
                float _SpriteLightingDepth;
            CBUFFER_END
            float4 _BodyNormalAmbient, _BodyNormalSun, _BodyNormalLocal;
            float4 _BodyNormalSunDirection;
            float4 _BodyNormalPointPositions[8], _BodyNormalPointColors[8];
            int _BodyNormalPointCount;
            float _BodyNormalHeight;
            float3 ShadeAlbedo(float3 clean, float3 painted, float3 direct)
            {
                float brightness = dot(direct, float3(.2126, .7152, .0722));
                float weight = 1 - smoothstep(0, .35, brightness);
                return lerp(clean, painted, weight);
            }
            float3 EvaluatePointLight(float3 n, float3 surface, float4 positionRadius, float3 color)
            {
                float3 ray = positionRadius.xyz - surface;
                float distance = length(ray);
                // Coincident positions have no defined direction, not full brightness.
                float3 direction = ray / max(distance, .0001);
                float attenuation = saturate(1 - distance / max(.001, positionRadius.w));
                float response = saturate(dot(n, direction));
                return color * attenuation * response;
            }
            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float4, _UvScaleOffset)
                    UNITY_DOTS_INSTANCED_PROP(float4, _PlayerLightColor)
                    UNITY_DOTS_INSTANCED_PROP(float4, _SpriteGeometry)
                    UNITY_DOTS_INSTANCED_PROP(float, _SpriteIndex)
                    UNITY_DOTS_INSTANCED_PROP(float, _SpriteLightingDepth)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
                #define PLAYER_PROP(type, name) UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(type, name)
            #else
                #define PLAYER_PROP(type, name) name
            #endif
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 rect = PLAYER_PROP(float4, _UvScaleOffset);
                float page = PLAYER_PROP(float, _SpriteIndex);
                float2 uv = input.uv * rect.xy + rect.zw;
                float4 color = SAMPLE_TEXTURE2D_ARRAY(_SpriteArray, player_point_clamp_sampler, uv, page);
                clip(color.a - max(_Cutoff, .0001));
                color.a = 1;
                float3 painted = color.rgb;
                if (_PaintedDetailsEnabled > .5)
                {
                    float4 samplePainted = SAMPLE_TEXTURE2D_ARRAY(_PaintedArray, player_point_clamp_sampler, uv, page);
                    if (samplePainted.a >= max(_Cutoff, .0001)) painted = samplePainted.rgb;
                }
                float4 encoded = SAMPLE_TEXTURE2D_ARRAY(_NormalArray, player_point_clamp_sampler, uv, page);
                if (encoded.a > .01)
                {
                    // Data is already in the block editor's upright sprite
                    // basis: right, world-up, towards camera. No tangent unpack.
                    float3 n = encoded.a > .01 ? normalize(encoded.rgb * 2 - 1) : float3(0,0,1);
                    n = normalize(lerp(float3(0,0,1), n, _NormalStrength));
                    if (_NormalDebug > .5) return float4(n * .5 + .5, color.a);
                    float sun = saturate(dot(n, _BodyNormalSunDirection.xyz));
                    // Approximate upright sprite surface in real-world units,
                    // using the shared 64x128 source cells (feet at pixel 25).
                    // Evaluate EACH pixel, not a single direction at the torso.
                    float4 geometry = PLAYER_PROP(float4, _SpriteGeometry);
                    float2 cellUv = (input.uv - .5) * geometry.xy + geometry.zw + .5;
                    float pixelScale = _BodyNormalHeight / 98.0;
                    float3 surface = float3((cellUv.x * 64 - 32) * pixelScale,
                        (cellUv.y * 128 - 25) * pixelScale, PLAYER_PROP(float, _SpriteLightingDepth));
                    float3 local = 0;
                    for (int i = 0; i < min(_BodyNormalPointCount, 8); i++)
                    {
                        local = max(local, EvaluatePointLight(n, surface,
                            _BodyNormalPointPositions[i], _BodyNormalPointColors[i].rgb));
                    }
                    float3 direct = _BodyNormalSun.rgb * sun + local;
                    color.rgb = ShadeAlbedo(color.rgb, painted, direct) *
                        saturate(_BodyNormalAmbient.rgb + _BodyNormalLocal.rgb + direct);
                    return color;
                }
                // Includes arms, held items, walking and disabled-normal fallback.
                // Never let transparent quad pixels occlude another part.
                float3 legacyLight = PLAYER_PROP(float4, _PlayerLightColor).rgb;
                color.rgb = ShadeAlbedo(color.rgb, painted, legacyLight) * legacyLight;
                return color;
            }
            ENDHLSL
        }
    }
}
