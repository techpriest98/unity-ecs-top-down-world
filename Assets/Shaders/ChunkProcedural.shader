Shader "Game/World/ChunkProcedural"
{
    Properties
    {
        [Toggle(_PLAYER_CLIPPING)]
        _PlayerClipping("Player Clipping", Float) = 0

        [Toggle(_DEBUG_NORMALS)]
        _DebugNormals("Debug Normals", Float) = 0

        _BlockAtlas("Block Atlas", 2D) = "white" {}
        _BlockNormalAtlas("Block Normal Atlas", 2D) = "bump" {}
        _PlayerClipRadius("Player Clip Radius", Range(0.0, 1024.0)) = 64.0
        _PlayerClipFadeWidth("Player Clip Fade Width", Range(0.0, 256.0)) = 32.0
        _PlayerClipGrainSize("Player Clip Grain Size", Range(1.0, 8.0)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ChunkProcedural"

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma shader_feature_local_fragment _PLAYER_CLIPPING
            #pragma shader_feature_local_fragment _DEBUG_NORMALS
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ============================================================
            // Constants
            // ============================================================

            static const uint TILE_WIDTH = 32;
            static const uint TILE_HEIGHT = 16;
            static const uint ATLAS_GROUP_WIDTH = 64;
            static const uint ATLAS_GROUP_HEIGHT = 48;
            static const float MINIMUM_AMBIENT_VISIBILITY = 0.08;

            // Autotiling
            static const uint NORTH_BIT = 1 << 0;
            static const uint EAST_BIT = 1 << 1;
            static const uint SOUTH_BIT = 1 << 2;
            static const uint WEST_BIT = 1 << 3;
            static const uint NORTH_EAST_BIT = 1 << 4;
            static const uint SOUTH_EAST_BIT = 1 << 5;
            static const uint SOUTH_WEST_BIT = 1 << 6;
            static const uint NORTH_WEST_BIT = 1 << 7;

            // ============================================================
            // GPU data
            // ============================================================

            struct ProjectedCellRenderData
            {
                uint BlockData;
                uint Position;
                uint LightData;
                uint FaceData;

                float3 ChunkPosition;
                float Padding;

                uint LocalLightBottomLeft;
                uint LocalLightBottomRight;
                uint LocalLightTopLeft;
                uint LocalLightTopRight;
            };

            struct BlockGpuData
            {
                uint AtlasPosition;
                uint Flags;
            };

            StructuredBuffer<ProjectedCellRenderData> _ProjectedCells;
            StructuredBuffer<BlockGpuData> _BlockDatabase;

            int _BlockDatabaseCount;

            // ============================================================
            // Atlas
            // ============================================================

            TEXTURE2D(_BlockAtlas);
            SAMPLER(sampler_BlockAtlas);

            float4 _BlockAtlas_TexelSize;

            TEXTURE2D(_BlockNormalAtlas);



            // ============================================================
            // Projection
            // ============================================================

            float _CellWidth;
            float _CellHeight;

            float _ChunkWidth;
            float _ProjectionHeight;

            // ============================================================
            // Lighting
            // ============================================================

            float3 _DirectionToLight;
            float3 _DirectionalLightColor;
            float _DirectionalLightIntensity;
            float3 _AmbientColor;
            float3 _SideFaceNormal;

            // ============================================================
            // Clipping
            // ============================================================

            float _PlayerClipRadius;
            float _PlayerClipFadeWidth;
            float _PlayerClipGrainSize;

            // ============================================================
            // Selection
            // ============================================================

            float _SelectionEnabled;
            float3 _SelectedChunkPosition;
            uint _SelectedProjectionPosition;
            float4 _SelectionColor;
            uint _SelectedFaceType;

            // ============================================================
            // Packed data
            // ============================================================

            uint2 UnpackPosition(uint packedPosition)
            {
                return uint2(
                    packedPosition & 0xFFFF,
                    (packedPosition >> 16) & 0xFFFF);
            }

            uint GetBlockId(uint packedBlockData)
            {
                return packedBlockData & 0xFF;
            }

            uint GetFaceType(uint packedBlockData)
            {
                return (packedBlockData >> 8) & 0xFF;
            }

            uint2 UnpackAtlasPosition(uint atlasPosition)
            {
                uint column = atlasPosition & 0xFF;
                uint row = (atlasPosition >> 8) & 0xFF;

                return uint2(column, row);
            }

            float3 GetLocalLight(uint packedLightData)
            {
                return float3(
                    packedLightData & 0xFFu,
                    (packedLightData >> 8) & 0xFFu,
                    (packedLightData >> 16) & 0xFFu) / 255.0;
            }

            float GetSkyVisibility(uint packedLightData)
            {
                return ((packedLightData >> 24) & 0x0Fu) / 15.0;
            }

            float GetSunVisibility(uint packedLightData)
            {
                return (packedLightData >> 28) & 0x01u;
            }

            // ============================================================
            // Faces
            // ============================================================

            uint GetFaceIndex(uint faceType)
            {
                switch (faceType)
                {
                    case 1: return 0; // Top
                    case 2: return 1; // SideUpper
                    case 3: return 2; // SideLower
                    default: return 0;
                }
            }

            float3 DecodeObjectNormal(float4 packedNormal)
            {
                return normalize(packedNormal.rgb * 2.0 - 1.0);
            }

            float3 GetWorldNormal(float3 normalOS)
            {
                float3 up = float3(0.0, 1.0, 0.0);
                float3 sideNormal = normalize(_SideFaceNormal);
                float3 tangent = normalize(cross(sideNormal, up));

                return normalize(
                    tangent * normalOS.x +
                    up * normalOS.y +
                    sideNormal * normalOS.z);
            }

            // ============================================================
            // Quad
            // ============================================================

            float2 GetQuadCorner(uint vertexId)
            {
                switch (vertexId)
                {
                    case 0: return float2(0.0, 0.0);
                    case 1: return float2(0.0, 1.0);
                    case 2: return float2(1.0, 1.0);
                    case 3: return float2(0.0, 0.0);
                    case 4: return float2(1.0, 1.0);
                    default: return float2(1.0, 0.0);
                }
            }

            // ============================================================
            // Vertex output
            // ============================================================

            struct Varyings
            {
                float4 PositionCS : SV_POSITION;
                float2 LocalUv : TEXCOORD0;

                nointerpolation uint BlockId : TEXCOORD1;
                nointerpolation uint FaceIndex : TEXCOORD2;
                nointerpolation uint NeighborMask : TEXCOORD3;
                nointerpolation uint LightData : TEXCOORD4;
                nointerpolation float IsSelected : TEXCOORD5;
                nointerpolation uint FootBlockId : TEXCOORD6;
                nointerpolation uint4 LocalLightCorners : TEXCOORD7;
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
                float2 corner = GetQuadCorner(vertexId);

                float chunkLeft = cell.ChunkPosition.x - _ChunkWidth * 0.5;
                float cellLeft = chunkLeft + cellPosition.x * _CellWidth;
                float chunkTop = cell.ChunkPosition.y + _ProjectionHeight;
                float cellBottom = chunkTop - (cellPosition.y + 1) * _CellHeight;

                float3 worldPosition = float3(
                    cellLeft + corner.x * _CellWidth,
                    cellBottom + corner.y * _CellHeight,
                    cell.ChunkPosition.z);

                Varyings output;

                output.PositionCS = TransformWorldToHClip(worldPosition);
                output.LocalUv = corner;
                output.BlockId = GetBlockId(cell.BlockData);
                output.FaceIndex = GetFaceIndex(GetFaceType(cell.BlockData));
                output.NeighborMask = cell.FaceData & 0xFF;
                output.FootBlockId = (cell.FaceData >> 8) & 0xFF;
                output.LightData = cell.LightData;

                output.LocalLightCorners = uint4(
                    cell.LocalLightBottomLeft,
                    cell.LocalLightBottomRight,
                    cell.LocalLightTopLeft,
                    cell.LocalLightTopRight);

                uint2 selectedPosition = UnpackPosition(_SelectedProjectionPosition);
                uint cellFaceType = GetFaceType(cell.BlockData);

                bool exactPosition = all(cellPosition == selectedPosition);

                bool selectedUpperMatchesLower =
                    _SelectedFaceType == 2u &&
                    cellFaceType == 3u &&
                    cellPosition.x == selectedPosition.x &&
                    cellPosition.y == selectedPosition.y + 1u;

                bool selectedLowerMatchesUpper =
                    _SelectedFaceType == 3u &&
                    cellFaceType == 2u &&
                    cellPosition.x == selectedPosition.x &&
                    cellPosition.y + 1u == selectedPosition.y;

                bool samePosition =
                    exactPosition ||
                    selectedUpperMatchesLower ||
                    selectedLowerMatchesUpper;

                bool sameChunk = all(abs(cell.ChunkPosition - _SelectedChunkPosition) < 0.0001);

                output.IsSelected =
                    _SelectionEnabled > 0.5 &&
                    samePosition &&
                    sameChunk
                        ? 1.0
                        : 0.0;

                return output;
            }

            // ============================================================
            // Autotiling
            // ============================================================

            bool HasNeighbor(uint neighborMask, uint bit)
            {
                return (neighborMask & bit) != 0;
            }

            float4 BlendOverlay(float4 baseColor, float4 overlayColor)
            {
                baseColor.rgb = lerp(
                    baseColor.rgb,
                    overlayColor.rgb,
                    overlayColor.a);

                return baseColor;
            }

            float3 BlendOverlayNormal(float3 baseNormal, float4 overlayNormal, float blend)
            {
                return normalize(lerp(baseNormal, DecodeObjectNormal(overlayNormal), blend));
            }

            // Atlas coordinates and database rows use a top-left origin.
            int3 GetAtlasPixel(uint2 atlasPosition, uint2 pixel)
            {
                uint2 position = atlasPosition *
                    uint2(ATLAS_GROUP_WIDTH, ATLAS_GROUP_HEIGHT) + pixel;
                return int3(position.x, (uint)_BlockAtlas_TexelSize.w - 1u - position.y, 0);
            }

            void ApplyOverlay(
                inout float4 color,
                inout float3 normalOS,
                uint2 atlasPosition,
                uint2 pixel,
                uint2 source,
                uint2 destination,
                uint2 size)
            {
                if (any(pixel < destination) || any(pixel >= destination + size))
                    return;

                int3 atlasPixel = GetAtlasPixel(atlasPosition, source + pixel - destination);
                float4 overlay = _BlockAtlas.Load(atlasPixel);
                if (overlay.a <= 0.0)
                    return;

                normalOS = BlendOverlayNormal(
                    normalOS, _BlockNormalAtlas.Load(atlasPixel), overlay.a);
                color = BlendOverlay(color, overlay);
            }

            void ApplyTopOverlays(
                inout float4 color,
                inout float3 normalOS,
                uint neighborMask,
                uint2 atlasPosition,
                uint2 pixel)
            {
                bool north = HasNeighbor(neighborMask, NORTH_BIT);
                bool east = HasNeighbor(neighborMask, EAST_BIT);
                bool south = HasNeighbor(neighborMask, SOUTH_BIT);
                bool west = HasNeighbor(neighborMask, WEST_BIT);

                if (!north)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 0), uint2(0, 0), uint2(32, 4));

                if (!south)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 4), uint2(0, 12), uint2(32, 4));

                if (!west)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 8), uint2(0, 0), uint2(4, 16));

                if (!east)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(36, 8), uint2(28, 0), uint2(4, 16));

                if (!north && !west)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(40, 8), uint2(0, 0), uint2(4, 4));

                if (!north && !east)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(44, 8), uint2(28, 0), uint2(4, 4));

                if (!south && !west)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(48, 8), uint2(0, 12), uint2(4, 4));

                if (!south && !east)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(52, 8), uint2(28, 12), uint2(4, 4));

                if (north && west && !HasNeighbor(neighborMask, NORTH_WEST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(40, 12), uint2(0, 0), uint2(4, 4));

                if (north && east && !HasNeighbor(neighborMask, NORTH_EAST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(44, 12), uint2(28, 0), uint2(4, 4));

                if (south && west && !HasNeighbor(neighborMask, SOUTH_WEST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(48, 12), uint2(0, 12), uint2(4, 4));

                if (south && east && !HasNeighbor(neighborMask, SOUTH_EAST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(52, 12), uint2(28, 12), uint2(4, 4));
            }

            void ApplySideOverlays(
                inout float4 color,
                inout float3 normalOS,
                uint neighborMask,
                uint footBlockId,
                uint2 atlasPosition,
                uint2 pixel)
            {
                bool aboveOpen = !HasNeighbor(neighborMask, NORTH_BIT);
                bool rightOpen = !HasNeighbor(neighborMask, EAST_BIT);
                bool leftOpen = !HasNeighbor(neighborMask, WEST_BIT);
                bool hasFoot = footBlockId != 0u && footBlockId < (uint)_BlockDatabaseCount;
                uint2 footAtlasPosition = uint2(0, 0);
                if (hasFoot)
                    footAtlasPosition = UnpackAtlasPosition(_BlockDatabase[footBlockId].AtlasPosition);

                if (leftOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(56, 8), uint2(0, 16), uint2(4, 32));

                if (rightOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(60, 8), uint2(28, 16), uint2(4, 32));

                if (aboveOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 40), uint2(0, 16), uint2(32, 4));

                if (hasFoot)
                    ApplyOverlay(color, normalOS, footAtlasPosition, pixel,
                        uint2(32, 44), uint2(0, 44), uint2(32, 4));

                if (aboveOpen && leftOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(40, 16), uint2(0, 16), uint2(4, 4));

                if (aboveOpen && rightOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(44, 16), uint2(28, 16), uint2(4, 4));

                if (hasFoot && leftOpen)
                    ApplyOverlay(color, normalOS, footAtlasPosition, pixel,
                        uint2(40, 20), uint2(0, 44), uint2(4, 4));

                if (hasFoot && rightOpen)
                    ApplyOverlay(color, normalOS, footAtlasPosition, pixel,
                        uint2(44, 20), uint2(28, 44), uint2(4, 4));
            }

            // ============================================================
            // Clipping
            // ============================================================

            float GetClipGrain(float2 screenPosition)
            {
                float grainSize = max(_PlayerClipGrainSize, 1.0);
                float2 grainPosition = floor(screenPosition / grainSize);

                return frac(52.9829189 * frac(dot(grainPosition, float2(0.06711056, 0.00583715))));
            }

            // ============================================================
            // Fragment
            // ============================================================

            float4 Frag(Varyings input) : SV_Target
            {
                #if defined(_PLAYER_CLIPPING)
                    float2 screenCenter = _ScreenParams.xy * 0.5;
                    float2 clipOffset = input.PositionCS.xy - screenCenter;
                    float distanceFromCenter = length(clipOffset);

                    float fadeWidth = max(_PlayerClipFadeWidth, 0.0001);
                    float innerRadius = max(_PlayerClipRadius - fadeWidth, 0.0);
                    float visibility = saturate((distanceFromCenter - innerRadius) / fadeWidth);

                    visibility = smoothstep(0.0, 1.0, visibility);

                    float grain = GetClipGrain(input.PositionCS.xy);

                    clip(visibility - grain);
                #endif

                if (input.BlockId == 0 ||
                    input.BlockId >= (uint)_BlockDatabaseCount)
                {
                    return float4(1.0, 0.0, 1.0, 1.0);
                }

                BlockGpuData block = _BlockDatabase[input.BlockId];
                uint2 atlasPosition = UnpackAtlasPosition(block.AtlasPosition);

                float2 localUv = saturate(input.LocalUv);

                uint localX = min(
                    (uint)(localUv.x * TILE_WIDTH),
                    TILE_WIDTH - 1);

                uint localY = min(
                    (uint)(localUv.y * TILE_HEIGHT),
                    TILE_HEIGHT - 1);

                uint2 pixel = uint2(
                    localX, input.FaceIndex * TILE_HEIGHT + TILE_HEIGHT - 1u - localY);
                int3 atlasPixel = GetAtlasPixel(atlasPosition, pixel);

                float4 color = _BlockAtlas.Load(atlasPixel);
                float3 normalOS = DecodeObjectNormal(_BlockNormalAtlas.Load(atlasPixel));

                if (input.FaceIndex == 0u)
                {
                    ApplyTopOverlays(
                        color, normalOS, input.NeighborMask, atlasPosition, pixel);
                }
                else
                {
                    ApplySideOverlays(
                        color, normalOS, input.NeighborMask,
                        input.FootBlockId, atlasPosition, pixel);
                }

                #if defined(_DEBUG_NORMALS)
                    return float4(normalOS * 0.5 + 0.5, 1.0);
                #endif

                // ========================================================
                // Lighting
                // ========================================================

                float3 worldNormal = GetWorldNormal(normalOS);
                float directIntensity = saturate(dot(
                    worldNormal,
                    normalize(_DirectionToLight)));

                float3 bottomLight = lerp(
                    GetLocalLight(input.LocalLightCorners.x),
                    GetLocalLight(input.LocalLightCorners.y), localUv.x);

                float3 topLight = lerp(
                    GetLocalLight(input.LocalLightCorners.z),
                    GetLocalLight(input.LocalLightCorners.w), localUv.x);

                float3 localLight = lerp(bottomLight, topLight, localUv.y);
                float skyVisibility = GetSkyVisibility(input.LightData);
                float sunVisibility = GetSunVisibility(input.LightData);

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
                // Selection
                // ========================================================

                if (input.IsSelected > 0.5)
                {
                    float2 selectionUv = input.LocalUv;


                    if (input.FaceIndex == 1u)
                    {
                        selectionUv.y = 0.5 + input.LocalUv.y * 0.5;
                    }
                    else if (input.FaceIndex == 2u)
                    {
                        selectionUv.y = input.LocalUv.y * 0.5;
                    }

                    float edgeDistance = min(
                        min(selectionUv.x, 1.0 - selectionUv.x),
                        min(selectionUv.y, 1.0 - selectionUv.y));

                    float border = 1.0 - step(0.06, edgeDistance);
                    float fill = 1.0 - smoothstep(0.0, 0.25, edgeDistance);

                    float selectionStrength = max(
                        border * _SelectionColor.a,
                        fill * _SelectionColor.a * 0.45);

                    color.rgb = lerp(
                        color.rgb,
                        _SelectionColor.rgb,
                        selectionStrength);
                }
                
                color.a = 1.0;

                return color;
            }

            ENDHLSL
        }
    }
}
