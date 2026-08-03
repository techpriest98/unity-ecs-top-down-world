using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Game.World.Chunks
{
    [BurstCompile]
    public partial struct ChunkGenerationSystem : ISystem
    {
        private const byte MaxDurability = byte.MaxValue;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (blocks, entity) in
                     SystemAPI.Query<DynamicBuffer<BlockData>>()
                         .WithAll<ChunkComponent>()
                         .WithNone<ChunkGenerated>()
                         .WithEntityAccess())
            {
                GenerateChunk(blocks);

                ecb.AddComponent<ChunkGenerated>(entity);
                ecb.SetComponentEnabled<ChunkNeedsProjection>(entity, true);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private static void GenerateChunk(
            DynamicBuffer<BlockData> blocks)
        {
            BlockData air = new(
                BlockId.Air,
                0);

            BlockData grass = new(
                BlockId.Grass,
                MaxDurability);

            BlockData dirt = new(
                BlockId.Dirt,
                MaxDurability);

            BlockData stone = new(
                BlockId.Stone,
                MaxDurability);

            BlockData sand = new(
                BlockId.Sand,
                MaxDurability);

            BlockData highGrass = new(
                BlockId.HighGrass,
                MaxDurability);

            for (int y = 0; y < ChunkSettings.SizeY; y++)
            {
                for (int z = 0; z < ChunkSettings.SizeZ; z++)
                {
                    for (int x = 0; x < ChunkSettings.SizeX; x++)
                    {
                        int terrainHeight =
                            GetTerrainHeight(x, z);

                        BlockData block;

                        if (y >= terrainHeight)
                        {
                            block = air;
                        }
                        else if (y == terrainHeight - 1)
                        {
                            block = grass;
                        }
                        else if (y >= terrainHeight - 2)
                        {
                            block = dirt;
                        }
                        else
                        {
                            block = stone;
                        }

                        if (x == 2 &&
                            z == 2 &&
                            y == terrainHeight)
                        {
                            block = highGrass;
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
            int x,
            int z)
        {
            return (x, z) switch
            {
                (0, 0) => 4,
                (1, 0) => 5,
                (2, 0) => 5,
                (3, 0) => 4,

                (0, 1) => 3,
                (1, 1) => 3,
                (2, 1) => 1,
                (3, 1) => 3,

                (0, 2) => 2,
                (1, 2) => 1,
                (2, 2) => 1,
                (3, 2) => 3,

                (0, 3) => 1,
                (1, 3) => 1,
                (2, 3) => 1,
                (3, 3) => 1,

                _ => 0
            };
        }
    }
}