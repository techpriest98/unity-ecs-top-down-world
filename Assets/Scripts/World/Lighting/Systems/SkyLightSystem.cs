using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkProjectionSystem))]
    public partial struct SkyLightSystem : ISystem
    {
        private const byte MaximumLight = 15;
        private const int MaxChunksPerFrame = 1;
        private const byte SpreadAttenuation = 3;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int processedCount = 0;

            foreach (var (blocks, skyLight, needsSkyLight, needsLighting) in
                     SystemAPI.Query<
                             DynamicBuffer<BlockData>,
                             DynamicBuffer<SkyLightData>,
                             EnabledRefRW<ChunkNeedsSkyLight>,
                             EnabledRefRW<ChunkNeedsLighting>>()
                         .WithAll<ChunkGenerated>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (!needsSkyLight.ValueRO)
                {
                    continue;
                }

                if (processedCount >= MaxChunksPerFrame)
                {
                    break;
                }

                Calculate(blocks, skyLight);

                needsSkyLight.ValueRW = false;
                needsLighting.ValueRW = true;
                processedCount++;
            }
        }

        private static void Calculate(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SkyLightData> skyLight)
        {
            for (int index = 0; index < skyLight.Length; index++)
            {
                skyLight[index] = new SkyLightData(0);
            }

            NativeQueue<int3> queue = new(Allocator.Temp);

            SeedVerticalLight(blocks, skyLight, queue);
            SpreadLight(blocks, skyLight, queue);

            queue.Dispose();
        }

        private static void SeedVerticalLight(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SkyLightData> skyLight,
            NativeQueue<int3> queue)
        {
            for (int z = 0; z < ChunkSettings.SizeZ; z++)
            {
                for (int x = 0; x < ChunkSettings.SizeX; x++)
                {
                    bool openToSky = true;

                    for (int y = ChunkSettings.SizeY - 1; y >= 0; y--)
                    {
                        int index = ChunkUtility.ToIndex(x, y, z);

                        if (IsOpaque(blocks[index].BlockId))
                        {
                            openToSky = false;
                            continue;
                        }

                        if (!openToSky)
                        {
                            continue;
                        }

                        skyLight[index] = new SkyLightData(MaximumLight);
                        queue.Enqueue(new int3(x, y, z));
                    }
                }
            }
        }

        private static void SpreadLight(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SkyLightData> skyLight,
            NativeQueue<int3> queue)
        {
            while (queue.TryDequeue(out int3 position))
            {
                int index = ChunkUtility.ToIndex(position.x, position.y, position.z);
                byte currentLight = skyLight[index].Value;

                if (currentLight <= SpreadAttenuation)
                {
                    continue;
                }

                byte nextLight = (byte)(currentLight - SpreadAttenuation);

                TrySpread(position + new int3(1, 0, 0), nextLight, blocks, skyLight, queue);
                TrySpread(position + new int3(-1, 0, 0), nextLight, blocks, skyLight, queue);
                TrySpread(position + new int3(0, 1, 0), nextLight, blocks, skyLight, queue);
                TrySpread(position + new int3(0, -1, 0), nextLight, blocks, skyLight, queue);
                TrySpread(position + new int3(0, 0, 1), nextLight, blocks, skyLight, queue);
                TrySpread(position + new int3(0, 0, -1), nextLight, blocks, skyLight, queue);
            }
        }

        private static void TrySpread(
            int3 position,
            byte light,
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SkyLightData> skyLight,
            NativeQueue<int3> queue)
        {
            if (!ChunkUtility.IsInside(position.x, position.y, position.z))
            {
                return;
            }

            int index = ChunkUtility.ToIndex(position.x, position.y, position.z);

            if (IsOpaque(blocks[index].BlockId) || skyLight[index].Value >= light)
            {
                return;
            }

            skyLight[index] = new SkyLightData(light);
            queue.Enqueue(position);
        }

        private static bool IsOpaque(BlockId blockId)
        {
            return BlockUtility.IsSolid(blockId) &&
                   blockId != BlockId.OceanWater;
        }
    }
}