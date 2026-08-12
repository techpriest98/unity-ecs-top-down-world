Shader "Game/World/ChunkProcedural"
{
    Properties
    {
        _BlockAtlas("Block Atlas", 2D) = "white" {}
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

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ============================================================
            // Constants
            // ============================================================

            static const uint TILE_WIDTH = 16;
            static const uint TILE_HEIGHT = 8;
            static const uint FACE_COUNT = 3;

            // Temporary simple lighting
            static const float TOP_LIGHT = 1.0;
            static const float SIDE_LIGHT = 0.72;

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

            // ============================================================
            // Projection
            // ============================================================

            float _CellWidth;
            float _CellHeight;

            float _ChunkWidth;
            float _ProjectionHeight;

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

                float chunkLeft =
                    cell.ChunkPosition.x - _ChunkWidth * 0.5;

                float cellLeft =
                    chunkLeft + cellPosition.x * _CellWidth;

                float chunkTop =
                    cell.ChunkPosition.y + _ProjectionHeight;

                float cellBottom =
                    chunkTop - (cellPosition.y + 1) * _CellHeight;

                float3 worldPosition = float3(
                    cellLeft + corner.x * _CellWidth,
                    cellBottom + corner.y * _CellHeight,
                    cell.ChunkPosition.z);

                Varyings output;

                output.PositionCS = TransformWorldToHClip(worldPosition);
                output.LocalUv = corner;
                output.BlockId = GetBlockId(cell.BlockData);
                output.FaceIndex = GetFaceIndex(GetFaceType(cell.BlockData));

                return output;
            }

            // ============================================================
            // Fragment
            // ============================================================

            float4 Frag(Varyings input) : SV_Target
            {
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