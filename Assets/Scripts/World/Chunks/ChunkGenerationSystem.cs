using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    [BurstCompile]
    public partial struct ChunkGenerationSystem : ISystem
    {
        private const byte MaxDurability =
            byte.MaxValue;

        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<ChunkComponent>();
        }

        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);

            foreach (var (
                        chunk,
                        blocks,
                        entity)
                    in SystemAPI.Query<
                            RefRO<ChunkComponent>,
                            DynamicBuffer<BlockData>>()
                        .WithNone<ChunkGenerated>()
                        .WithEntityAccess())
            {
                GenerateChunk(
                    blocks,
                    chunk.ValueRO.Coordinate);

                ecb.AddComponent<ChunkGenerated>(
                    entity);

                ecb.SetComponentEnabled<
                    ChunkNeedsProjection>(
                    entity,
                    true);
            }

            ecb.Playback(
                state.EntityManager);

            ecb.Dispose();
        }

        private static void GenerateChunk(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate)
        {
            BlockData air =
                new(
                    BlockId.Air,
                    0);

            BlockData grass =
                new(
                    BlockId.Grass,
                    MaxDurability);

            BlockData dirt =
                new(
                    BlockId.Dirt,
                    MaxDurability);

            BlockData stone =
                new(
                    BlockId.Stone,
                    MaxDurability);

            for (int z = 0;
                 z < ChunkSettings.SizeZ;
                 z++)
            {
                for (int x = 0;
                     x < ChunkSettings.SizeX;
                     x++)
                {
                    int globalX =
                        chunkCoordinate.x *
                        ChunkSettings.SizeX +
                        x;

                    int globalZ =
                        chunkCoordinate.y *
                        ChunkSettings.SizeZ +
                        z;

                    int terrainHeight =
                        GetTerrainHeight(
                            globalX,
                            globalZ);

                    for (int y = 0;
                         y < ChunkSettings.SizeY;
                         y++)
                    {
                        BlockData block;

                        if (y >= terrainHeight)
                        {
                            block = air;
                        }
                        else if (y == terrainHeight - 1)
                        {
                            block = grass;
                        }
                        else if (y >= terrainHeight - 4)
                        {
                            block = dirt;
                        }
                        else
                        {
                            block = stone;
                        }

                        ChunkUtility.SetBlock(
                            blocks,
                            x,
                            y,
                            z,
                            block);
                    }
                }
            }
        }

        private static int GetTerrainHeight(
            int globalX,
            int globalZ)
        {
            const float baseHeight = 36f;

            const float xFrequency = 0.08f;
            const float zFrequency = 0.06f;

            const float xAmplitude = 14f;
            const float zAmplitude = 10f;

            float xWave =
                math.sin(
                    globalX *
                    xFrequency) *
                xAmplitude;

            float zWave =
                math.cos(
                    globalZ *
                    zFrequency) *
                zAmplitude;

            float diagonalWave =
                math.sin(
                    (globalX + globalZ) *
                    0.035f) *
                6f;

            float height =
                baseHeight +
                xWave +
                zWave +
                diagonalWave;

            return math.clamp(
                (int)math.round(height),
                1,
                ChunkSettings.SizeY - 1);
        }
    }
}