Shader "Game/World/ChunkWaterProcedural"
{
    Properties
    {
        _BlockAtlas("Block Atlas", 2D) = "white" {}
        _WaterTint("Water Tint", Color) = (0.55, 0.9, 0.9, 1.0)
        _WaterDesaturation("Water Desaturation", Range(0, 1)) = 0
        _WaterOpacity("Water Opacity", Range(0.0, 1.0)) = 0.20
        _WaterAbsorption("Water Absorption", Range(0.0, 1.0)) = 0.12
        _WaterTopInsetPixels("Water Top Inset Pixels", Range(0.0, 4.0)) = 2.0
        _OceanWaveStrength("Ocean Wave Strength", Range(0, 1)) = 0.75
        _OceanWaveLength("Ocean Wave Length (Blocks)", Range(0.5, 12)) = 3.4
        _OceanWaveSpeed("Ocean Wave Speed", Range(0, 3)) = 1
        _OceanNormalStrength("Ocean Wave Normal Strength", Range(0, 1)) = 0.22
        _OceanFoamStrength("Ocean Crest Foam Strength", Range(0, 1)) = 0.6
        _OceanFoamCoverage("Ocean Crest Foam Coverage", Range(0, 1)) = 0.65
        _OceanFoamPatchLength("Ocean Foam Patch Length (Blocks)", Range(1, 6)) = 2.8
        _OceanFoamPatchDepth("Ocean Foam Patch Depth (Blocks)", Range(0.25, 2)) = 1
        _ShoreFoamStrength("Shore Foam Strength", Range(0, 1)) = 0.85
        _ShoreFoamWidth("Shore Foam Width", Range(0.05, 0.5)) = 0.28
        _ShoreFoamScale("Shore Foam Scale", Range(0.5, 4)) = 2
        _ShoreFoamFrame("Shore Foam Preview Frame", Range(0, 7)) = 2
        [Toggle] _ShoreFoamAnimate("Animate Shore Foam", Float) = 1
        _ShoreFoamPeriod("Shore Foam Cycle Seconds", Range(0.5, 10)) = 3.2
        _ShoreWaveTailOpacity("Shore Wave Gradient Opacity", Range(0, 0.5)) = 0.18
        _ShoreWaveTailLength("Shore Wave Gradient Length", Range(0.2, 2)) = 1.2
        _ShoreWaveTailWaterBlend("Shore Wave Gradient Water Color", Range(0, 1)) = 0.8
        _ShoreWaveTailNoise("Shore Wave Gradient Noise", Range(0, 1)) = 0.35
        _ShoreWaveSecondStrength("Second Shore Wave Strength", Range(0, 1)) = 0.8
        _ShoreWaveSpacing("Shore Wave Spacing (Blocks)", Range(0, 0.5)) = 0
        _ShoreWavePhaseOffset("Second Shore Wave Phase Offset", Range(0, 1)) = 0.5
        _ShoreWaveRunUp("Shore Wave Run Up (Blocks)", Range(0, 1)) = 0.35
        _ShoreWindInfluence("Shore Wind Influence", Range(0, 1)) = 1
        [Toggle] _ShoreFoamDebug("Shore Foam Debug Mask", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ChunkWaterProcedural"

            Cull Off

            Blend SrcAlpha OneMinusSrcAlpha

            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ShoreFoam.hlsl"
            #include "OceanWaves.hlsl"

            // ============================================================
            // Constants
            // ============================================================

            static const uint TILE_WIDTH = 32;
            static const uint TILE_HEIGHT = 16;
            static const uint ATLAS_GROUP_WIDTH = 64;
            static const uint ATLAS_GROUP_HEIGHT = 48;
            static const float MINIMUM_AMBIENT_VISIBILITY = 0.08;
            static const uint OPTICAL_DEPTH_MASK = 0xFF;
            static const uint TOP_INSET_FLAG = 1u << 8;

            // ============================================================
            // GPU data
            // ============================================================

            #include "ProjectedCellPacking.hlsl"
            #include "ChunkBorders.hlsl"

            StructuredBuffer<ProjectedCellRenderData> _ProjectedCells;
            StructuredBuffer<BlockGpuData> _BlockDatabase;
            TEXTURE2D(_LocalTileCoverage); SAMPLER(sampler_LocalTileCoverage);
            float4 _LocalTileCoverageBounds;
            float _LocalTileCoverageEnabled;

            int _BlockDatabaseCount;

            // ============================================================
            // Atlas
            // ============================================================

            TEXTURE2D(_BlockAtlas);
            SAMPLER(sampler_BlockAtlas);

            float4 _BlockAtlas_TexelSize;

            // ============================================================
            // Water
            // ============================================================

            float4 _WaterTint;
            float _WaterDesaturation;

            float _WaterOpacity;
            float _WaterAbsorption;
            float _WaterTopInsetPixels;

            // ============================================================
            // Projection
            // ============================================================

            float _CellWidth;
            float _CellHeight;

            float _ChunkWidth;
            float _ProjectionHeight;

            float3 _DirectionToLight;
            float3 _DirectionalLightColor;
            float _DirectionalLightIntensity;
            float3 _AmbientColor;
            float3 _SideFaceNormal;
            int _SunShadowMaskIndex;
            float _CpuSunShadowsDisabled;
            // Match the local response strength to the existing ocean-wave normals.
            static const float _LocalNormalStrength = 1;
            #include "DetailedLocalLighting.hlsl"
            #include "TreeCoarseSunLighting.hlsl"


            // ============================================================
            // Packed data
            // ============================================================







            float GetSunVisibility(uint packedLightData)
            {
                if (_CpuSunShadowsDisabled > .5) return 1;
                return GetPackedSunVisibility(packedLightData, (uint)_SunShadowMaskIndex);
            }


            // ============================================================
            // Faces
            // ============================================================

            uint GetFaceIndex(uint faceType)
            {
                switch (faceType)
                {
                    case 1:
                        return 0;

                    case 2:
                        return 1;

                    case 3:
                        return 2;

                    default:
                        return 0;
                }
            }

            float3 GetFaceNormal(uint faceIndex)
            {
                return faceIndex == 0
                    ? float3(0.0, 1.0, 0.0)
                    : normalize(_SideFaceNormal);
            }

            // ============================================================
            // Quad
            // ============================================================

            float2 GetQuadCorner(uint vertexId)
            {
                switch (vertexId)
                {
                    case 0:
                        return float2(0.0, 0.0);

                    case 1:
                        return float2(0.0, 1.0);

                    case 2:
                        return float2(1.0, 1.0);

                    case 3:
                        return float2(0.0, 0.0);

                    case 4:
                        return float2(1.0, 1.0);

                    default:
                        return float2(1.0, 0.0);
                }
            }

            // ============================================================
            // Vertex output
            // ============================================================

            struct Varyings
            {
                float4 PositionCS : SV_POSITION;
                float2 LocalUv : TEXCOORD0;
                float2 ProjectionPosition : TEXCOORD6;
                float2 ChunkBorderUv : TEXCOORD10;
                float ShadowReceiverHeight : TEXCOORD11;
                nointerpolation uint ShoreMask : TEXCOORD7;
                nointerpolation float ShoreQuadHeight : TEXCOORD8;
                nointerpolation uint SkyVisible : TEXCOORD9;

                nointerpolation uint BlockId : TEXCOORD1;
                nointerpolation uint FaceIndex : TEXCOORD2;
                nointerpolation uint OpticalDepth : TEXCOORD3;
                nointerpolation uint LightData : TEXCOORD4;
                nointerpolation uint4 LocalLightCorners : TEXCOORD5;
            };

            // ============================================================
            // Vertex
            // ============================================================

            Varyings Vert(
                uint vertexId : SV_VertexID,
                uint instanceId : SV_InstanceID)
            {
                ProjectedCellRenderData cell = _ProjectedCells[instanceId];

                uint2 cellPosition = UnpackPosition(cell.Position);
                uint faceIndex = GetFaceIndex(GetFaceType(cell.BlockData));
                uint opticalDepth = GetOpticalDepth(cell.FaceData);
                bool hasTopInset = HasTopInset(cell.FaceData);

                float2 corner = GetQuadCorner(vertexId);
                float insetHeight = 0.0;

                if (faceIndex == 0 && hasTopInset)
                {
                    float insetRatio = _WaterTopInsetPixels / (float)TILE_HEIGHT;

                    insetHeight = _CellHeight * insetRatio;
                }

                float chunkLeft = cell.ChunkPosition.x - _ChunkWidth * 0.5;
                float cellLeft = chunkLeft + cellPosition.x * _CellWidth;

                float chunkTop = cell.ChunkPosition.y + _ProjectionHeight;
                float cellBottom = chunkTop - (cellPosition.y + 1) * _CellHeight;

                float worldY =
                    cellBottom +
                    corner.y *
                    _CellHeight -
                    corner.y *
                    insetHeight;

                float3 worldPosition =
                    float3(
                        cellLeft +
                        corner.x *
                        _CellWidth,

                        worldY,

                        cell.ChunkPosition.z);

                Varyings output;

                output.ProjectionPosition = worldPosition.xy;
                float sourceHeight = (cell.FaceData >> 24) & 255u;
                uint faceType = GetFaceType(cell.BlockData);
                bool lowerHalf = (cell.BlockData & (1u << 12)) != 0u;
                float faceHeight = faceType == 1u ? sourceHeight - (lowerHalf ? .5 : 0) :
                    sourceHeight + (faceType == 3u || lowerHalf ? .25 : .75);
                output.ShadowReceiverHeight = faceHeight + (faceType == 1u ? 0 : (corner.y - .5) * .5);
                // The inset is a physical lowering of the top surface, not just its UV.
                if (faceType == 1u) output.ShadowReceiverHeight -= insetHeight;
                worldPosition.z = TerrainSurfaceDepth(cell, worldPosition.y, output.ShadowReceiverHeight);
                output.PositionCS = TransformWorldToHClip(worldPosition);
                output.LocalUv = corner;
                output.BlockId = GetBlockId(cell.BlockData);
                output.FaceIndex = faceIndex;
                output.ChunkBorderUv = ChunkBorderCoordinates(cell.FaceData,corner,faceIndex);
                output.ShoreMask = faceIndex == 0 ? ((cell.FaceData >> 9) & 127u) |
                    (((cell.BlockData >> 15) & 1u) << 7) : 0u;
                output.ShoreQuadHeight = _CellHeight - insetHeight;
                // Water-only bit in the unused durability channel; buffer stride unchanged.
                output.SkyVisible = (cell.BlockData >> 16) & 1u;
                output.OpticalDepth = opticalDepth;
                output.LightData = cell.LightData;

                output.LocalLightCorners = uint4(
                    cell.LocalLightBottomLeft,
                    cell.LocalLightBottomRight,
                    cell.LocalLightTopLeft,
                    cell.LocalLightTopRight);

                return output;
            }

            // ============================================================
            // Fragment
            // ============================================================

            float4 Frag(
                Varyings input) :
                SV_Target
            {
                if (_LocalTileCoverageEnabled > .5)
                {
                    float2 uv = (input.ProjectionPosition - _LocalTileCoverageBounds.xy) / _LocalTileCoverageBounds.zw;
                    if (all(uv >= 0) && all(uv < 1))
                        clip(.5 - SAMPLE_TEXTURE2D_LOD(_LocalTileCoverage, sampler_LocalTileCoverage, uv, 0).r);
                }

                if (input.BlockId == 0 ||
                    input.BlockId >=
                    (uint)_BlockDatabaseCount)
                {
                    return float4(
                        1.0,
                        0.0,
                        1.0,
                        1.0);
                }

                BlockGpuData block =
                    _BlockDatabase[
                        input.BlockId];

                uint2 atlasPosition =
                    UnpackAtlasPosition(
                        block.AtlasPosition);

                float2 localUv =
                    saturate(
                        input.LocalUv);

                uint localX =
                    min(
                        (uint)(
                            localUv.x *
                            TILE_WIDTH),

                        TILE_WIDTH - 1);

                uint localY =
                    min(
                        (uint)(
                            localUv.y *
                            TILE_HEIGHT),

                        TILE_HEIGHT - 1);

                // Database rows and group pixels use a top-left origin.
                uint2 groupPixel = uint2(
                    localX,
                    input.FaceIndex * TILE_HEIGHT + TILE_HEIGHT - 1u - localY);

                uint2 atlasPixel = atlasPosition *
                    uint2(ATLAS_GROUP_WIDTH, ATLAS_GROUP_HEIGHT) + groupPixel;

                atlasPixel.y = (uint)_BlockAtlas_TexelSize.w - 1u - atlasPixel.y;

                float4 color = _BlockAtlas.Load(int3(atlasPixel, 0));
                float waterGray = dot(color.rgb, float3(.2126, .7152, .0722));
                color.rgb = lerp(color.rgb, waterGray.xxx, saturate(_WaterDesaturation));
                float4 foam = GetShoreFoam(localUv, input.ShoreMask, float2(_CellWidth, input.ShoreQuadHeight),
                    input.ProjectionPosition, false, color.rgb * _WaterTint.rgb);
                float3 oceanNormal = float3(0,1,0);
                float oceanCrest = 0;
                // BlockId 4 = OceanWater. No ocean animation on river water,
                // or vertical faces.
                if (input.FaceIndex == 0u && input.BlockId == 4u && input.SkyVisible != 0u)
                    GetOceanWaves(input.ProjectionPosition, float2(_CellWidth, _CellHeight),
                        localUv, input.ShoreMask, oceanNormal, oceanCrest);
                oceanCrest *= 1.0 - foam.a;
                float3 waterColor = color.rgb * _WaterTint.rgb;
                float3 crestColor = lerp(waterColor, float3(.65,.74,.72), .42);
                color.rgb = lerp(waterColor, crestColor, oceanCrest);
                color.rgb = lerp(color.rgb, foam.rgb, foam.a);

                // ========================================================
                // Lighting
                // ========================================================

                float3 worldNormal =
                    input.FaceIndex == 0u ? oceanNormal : GetFaceNormal(input.FaceIndex);

                float directIntensity = saturate(dot(
                    worldNormal,
                    normalize(_DirectionToLight)));

                float4 bottomLight = lerp(
                    GetCornerLight(input.LocalLightCorners.x),
                    GetCornerLight(input.LocalLightCorners.y),
                    localUv.x);

                float4 topLight = lerp(
                    GetCornerLight(input.LocalLightCorners.z),
                    GetCornerLight(input.LocalLightCorners.w),
                    localUv.x);

                float4 smoothLight = lerp(
                    bottomLight,
                    topLight,
                    localUv.y);

                float3 localLight = smoothLight.rgb;
                float3 referenceNormal = GetFaceNormal(input.FaceIndex);
                localLight = SingleLocalLightDiagnostic(input.ProjectionPosition, input.ShadowReceiverHeight,
                    referenceNormal, worldNormal, referenceNormal, localLight);
                float skyVisibility = smoothLight.a;
                float sunVisibility = min(GetSunVisibility(input.LightData),
                    CoarseTreeSunVisibility(input.ProjectionPosition, input.ShadowReceiverHeight));

                float ambientVisibility = lerp(
                    MINIMUM_AMBIENT_VISIBILITY,
                    1.0,
                    skyVisibility);

                float3 ambientLight =
                    _AmbientColor *
                    ambientVisibility;

                float3 directLight =
                    _DirectionalLightColor *
                    _DirectionalLightIntensity *
                    directIntensity *
                    sunVisibility;

                color.rgb *= saturate(
                    ambientLight +
                    localLight +
                    directLight);


                // ========================================================
                // Optical depth
                // ========================================================

                float opticalDepth =
                    max(
                        (float)
                        input.OpticalDepth,
                        1.0);

                float transmission =
                    exp(
                        -_WaterAbsorption *
                        (
                            opticalDepth -
                            1.0
                        ));

                float opacity =
                    1.0 -
                    (
                        1.0 -
                        _WaterOpacity
                    ) *
                    transmission;

                color.a =
                    saturate(
                        opacity *
                        _WaterTint.a);
                color.a = lerp(color.a, 1.0, foam.a);
                color.a = lerp(color.a, 1.0, oceanCrest * .65);

                color.rgb = ApplyChunkBorders(color.rgb,input.ChunkBorderUv,input.FaceIndex);
                return color;
            }

            ENDHLSL
        }
    }
}
