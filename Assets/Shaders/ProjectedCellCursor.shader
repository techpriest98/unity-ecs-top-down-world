Shader "Game/World/ProjectedCellCursor"
{
    Properties
    {
        _CursorColor("Cursor Color", Color) = (1, 0.902, 0.608, 0.45)
        _CursorBorderWidth("Border width (world units)", Float) = 0.03125
        _CursorEdgeFade("Inner edge fade (world units)", Float) = 0.09375
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ProjectedCellPacking.hlsl"
            float4 _CursorRect;
            float4 _CursorSurface;
            float4 _CursorColor;
            float _CursorBorderWidth, _CursorEdgeFade;
            float _CursorObjectMode, _CursorObjectDepth, _CursorObjectSlope, _CursorObjectAnchorY;
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(uint id : SV_VertexID)
            {
                const float2 corners[6] = {
                    float2(0,0), float2(0,1), float2(1,1),
                    float2(0,0), float2(1,1), float2(1,0)
                };
                Varyings output;
                output.uv = corners[id];
                float2 projected = _CursorRect.xy + output.uv * _CursorRect.zw;
                ProjectedCellRenderData cell = (ProjectedCellRenderData)0;
                cell.ChunkPosition = float3(0, _CursorSurface.x, _CursorSurface.y);
                float height = lerp(_CursorSurface.z, _CursorSurface.w, output.uv.y);
                float surfaceDepth = TerrainSurfaceDepth(cell, projected.y, height) - .001;
                if (_CursorObjectMode > .5) surfaceDepth = _CursorObjectDepth +
                    2.0 * (projected.y - _CursorObjectAnchorY) * _CursorObjectSlope;
                output.positionCS = TransformWorldToHClip(float3(projected, surfaceDepth));
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                // Nine-slice equivalent: measure from each edge in projected
                // world units, not a percentage of this object's width/height.
                float2 size = max(abs(_CursorRect.zw), float2(.0001, .0001));
                float2 distanceToEdge = min(input.uv, 1-input.uv) * size;
                float edge = min(distanceToEdge.x, distanceToEdge.y);
                float halfShortSide = min(size.x, size.y) * .5;
                float thickness = min(max(_CursorBorderWidth, 0), halfShortSide * .5);
                float fade = min(max(_CursorEdgeFade, .0001), max(halfShortSide - thickness, .0001));
                float border = 1-step(thickness, edge);
                float fill = 1-smoothstep(thickness, thickness + fade, edge);
                return float4(_CursorColor.rgb,
                    max(border * _CursorColor.a, fill * _CursorColor.a * 0.45));
            }
            ENDHLSL
        }
    }
}
