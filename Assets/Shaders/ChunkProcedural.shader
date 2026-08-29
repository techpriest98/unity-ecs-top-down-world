Shader "Game/World/ChunkProcedural"
{
    Properties
    {
        [Toggle(_PLAYER_CLIPPING)]
        _PlayerClipping("Player Clipping", Float) = 0

        _BlockAtlas("Block Atlas", 2D) = "white" {}
        _TopOverlayAtlas("Top Overlay Atlas", 2D) = "black" {}
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ============================================================
            // Constants
            // ============================================================

            static const uint TILE_WIDTH = 32;
            static const uint TILE_HEIGHT = 16;
            static const uint FACE_COUNT = 3;

            // Temporary simple lighting
            static const float TOP_LIGHT = 1.0;
            static const float SIDE_LIGHT = 0.72;

            // Autotiling
            static const uint OVERLAY_GROUP_WIDTH = 32;
            static const uint OVERLAY_GROUP_HEIGHT = 32;
            static const uint EDGE_THICKNESS = 4;
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
                uint Reserved;

                float3 ChunkPosition;
                float Padding;
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

            TEXTURE2D(_TopOverlayAtlas);
            
            float4 _TopOverlayAtlas_TexelSize;

            // ============================================================
            // Projection
            // ============================================================

            float _CellWidth;
            float _CellHeight;

            float _ChunkWidth;
            float _ProjectionHeight;

            // ============================================================
            // Clipping
            // ============================================================

            float _PlayerClipRadius;
            float _PlayerClipFadeWidth;
            float _PlayerClipGrainSize;

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

            float GetFaceLighting(uint faceIndex)
            {
                return faceIndex == 0
                    ? TOP_LIGHT
                    : SIDE_LIGHT;
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
                nointerpolation uint ClipFlags : TEXCOORD4;
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
                output.NeighborMask = cell.Reserved & 0xFF;

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

            float4 LoadTopOverlay(uint2 atlasPosition, uint2 partOffset, uint2 partPixel)
            {
                uint2 groupOrigin = atlasPosition * uint2(OVERLAY_GROUP_WIDTH, OVERLAY_GROUP_HEIGHT);
                uint2 atlasPixel = groupOrigin + partOffset + partPixel;

                return _TopOverlayAtlas.Load(int3(atlasPixel, 0));
            }

            float4 ApplyTopEdges(
                float4 color,
                uint neighborMask,
                uint2 atlasPosition,
                uint localX,
                uint localY)
            {
                // North:
                if (!HasNeighbor(neighborMask, NORTH_BIT) && localY >= TILE_HEIGHT - EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(0, 28),
                        uint2(localX, localY - (TILE_HEIGHT - EDGE_THICKNESS)));

                    color = BlendOverlay(color, overlay);
                }

                // South:
                if (!HasNeighbor(neighborMask, SOUTH_BIT) && localY < EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(0, 24),
                        uint2(localX, localY));

                    color = BlendOverlay(color, overlay);
                }

                // West:
                if (!HasNeighbor(neighborMask, WEST_BIT) && localX < EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(0, 8),
                        uint2(localX, localY));

                    color = BlendOverlay(color, overlay);
                }

                // East:
                if (!HasNeighbor(neighborMask, EAST_BIT) && localX >= TILE_WIDTH - EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(4, 8),
                        uint2(localX - (TILE_WIDTH - EDGE_THICKNESS), localY));

                    color = BlendOverlay(color, overlay);
                }

                return color;
            }

            float4 ApplyTopOuterCorners(
                float4 color,
                uint neighborMask,
                uint2 atlasPosition,
                uint localX,
                uint localY)
            {
                bool northOpen = !HasNeighbor(neighborMask, NORTH_BIT);
                bool eastOpen = !HasNeighbor(neighborMask, EAST_BIT);
                bool southOpen = !HasNeighbor(neighborMask, SOUTH_BIT);
                bool westOpen = !HasNeighbor(neighborMask, WEST_BIT);

                // ========================================================
                // Outer North West
                // ========================================================

                if (northOpen && westOpen &&
                    localX < EDGE_THICKNESS &&
                    localY >= TILE_HEIGHT - EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(8, 20),
                        uint2(localX, localY - (TILE_HEIGHT - EDGE_THICKNESS)));

                    color = BlendOverlay(color, overlay);
                }

                // ========================================================
                // Outer North East
                // ========================================================

                if (northOpen && eastOpen &&
                    localX >= TILE_WIDTH - EDGE_THICKNESS &&
                    localY >= TILE_HEIGHT - EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(12, 20),
                        uint2(localX - (TILE_WIDTH - EDGE_THICKNESS),
                        localY - (TILE_HEIGHT - EDGE_THICKNESS)));

                    color = BlendOverlay(color, overlay);
                }

                // ========================================================
                // Outer South West
                // ========================================================

                if (southOpen && westOpen &&
                    localX < EDGE_THICKNESS &&
                    localY < EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(16, 20),
                        uint2(localX, localY));

                    color = BlendOverlay(color, overlay);
                }

                // ========================================================
                // Outer South East
                // ========================================================

                if (southOpen && eastOpen &&
                    localX >= TILE_WIDTH - EDGE_THICKNESS &&
                    localY < EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(20, 20),
                        uint2(localX - (TILE_WIDTH - EDGE_THICKNESS),
                        localY));

                    color = BlendOverlay(color, overlay);
                }

                return color;
            }

            float4 ApplyTopInnerCorners(
                float4 color,
                uint neighborMask,
                uint2 atlasPosition,
                uint localX,
                uint localY)
            {
                bool north = HasNeighbor(neighborMask, NORTH_BIT);
                bool east = HasNeighbor(neighborMask, EAST_BIT);
                bool south = HasNeighbor(neighborMask, SOUTH_BIT);
                bool west = HasNeighbor(neighborMask, WEST_BIT);
                bool northEast = HasNeighbor(neighborMask, NORTH_EAST_BIT);
                bool southEast = HasNeighbor(neighborMask, SOUTH_EAST_BIT);
                bool southWest = HasNeighbor(neighborMask, SOUTH_WEST_BIT);
                bool northWest = HasNeighbor(neighborMask, NORTH_WEST_BIT);

                // ========================================================
                // Inner North West
                // ========================================================

                if (north && west && !northWest &&
                    localX < EDGE_THICKNESS &&
                    localY >= TILE_HEIGHT - EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(8, 16),
                        uint2(localX, localY - (TILE_HEIGHT - EDGE_THICKNESS)));

                    color = BlendOverlay(color, overlay);
                }

                // ========================================================
                // Inner North East
                // ========================================================

                if (north && east && !northEast &&
                    localX >= TILE_WIDTH - EDGE_THICKNESS &&
                    localY >= TILE_HEIGHT - EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(12, 16),
                        uint2(localX - (TILE_WIDTH - EDGE_THICKNESS),
                        localY - (TILE_HEIGHT - EDGE_THICKNESS)));

                    color = BlendOverlay(color, overlay);
                }

                // ========================================================
                // Inner South West
                // ========================================================

                if (south && west && !southWest &&
                    localX < EDGE_THICKNESS &&
                    localY < EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(16, 16),
                        uint2(localX, localY));

                    color = BlendOverlay(color, overlay);
                }

                // ========================================================
                // Inner South East
                // ========================================================

                if (south && east && !southEast &&
                    localX >= TILE_WIDTH - EDGE_THICKNESS &&
                    localY < EDGE_THICKNESS)
                {
                    float4 overlay = LoadTopOverlay(
                        atlasPosition,
                        uint2(20, 16),
                        uint2(localX - (TILE_WIDTH - EDGE_THICKNESS),
                        localY));

                    color = BlendOverlay(color, overlay);
                }

                return color;
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

                uint atlasPixelX =
                    atlasPosition.x * TILE_WIDTH +
                    localX;

                uint blockGroupHeight =
                    TILE_HEIGHT * FACE_COUNT;

                uint atlasFaceRow =
                    (FACE_COUNT - 1) - input.FaceIndex;

                uint atlasPixelY =
                    atlasPosition.y * blockGroupHeight +
                    atlasFaceRow * TILE_HEIGHT +
                    localY;

                float4 color = _BlockAtlas.Load(
                    int3(atlasPixelX, atlasPixelY, 0));
                
                // Autotiling
                if (input.FaceIndex == 0)
                {
                    color = ApplyTopEdges(
                        color,
                        input.NeighborMask,
                        atlasPosition,
                        localX,
                        localY);

                    color = ApplyTopOuterCorners(
                        color,
                        input.NeighborMask,
                        atlasPosition,
                        localX,
                        localY);

                    color = ApplyTopInnerCorners(
                        color,
                        input.NeighborMask,
                        atlasPosition,
                        localX,
                        localY);
                }

                // ========================================================
                // Temporary face lighting
                // ========================================================

                float light = GetFaceLighting(input.FaceIndex);
                color.rgb *= light;

                color.a = 1.0;

                return color;
            }

            ENDHLSL
        }
    }
}