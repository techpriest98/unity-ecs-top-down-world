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

            foreach (var (blocks, voxelLight, needsSkyLight, needsLighting) in
                     SystemAPI.Query<
                             DynamicBuffer<BlockData>,
                             DynamicBuffer<VoxelLightData>,
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

                Calculate(blocks, voxelLight);

                needsSkyLight.ValueRW = false;
                needsLighting.ValueRW = true;
                processedCount++;
            }
        }

        private static void Calculate(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<VoxelLightData> voxelLight)
        {
            for (int index = 0; index < voxelLight.Length; index++)
            {
                SetSkyLight(voxelLight, index, 0);
            }

            NativeQueue<int3> queue = new(Allocator.Temp);

            SeedVerticalLight(blocks, voxelLight, queue);
            SpreadLight(blocks, voxelLight, queue);

            queue.Dispose();
        }

        private static void SeedVerticalLight(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<VoxelLightData> voxelLight,
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

                        SetSkyLight(voxelLight, index, MaximumLight);
                        queue.Enqueue(new int3(x, y, z));
                    }
                }
            }
        }

        private static void SpreadLight(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<VoxelLightData> voxelLight,
            NativeQueue<int3> queue)
        {
            while (queue.TryDequeue(out int3 position))
            {
                int index = ChunkUtility.ToIndex(position.x, position.y, position.z);
                byte currentLight = voxelLight[index].Sky;

                if (currentLight <= SpreadAttenuation)
                {
                    continue;
                }

                byte nextLight = (byte)(currentLight - SpreadAttenuation);

                TrySpread(position + new int3(1, 0, 0), nextLight, blocks, voxelLight, queue);
                TrySpread(position + new int3(-1, 0, 0), nextLight, blocks, voxelLight, queue);
                TrySpread(position + new int3(0, 1, 0), nextLight, blocks, voxelLight, queue);
                TrySpread(position + new int3(0, -1, 0), nextLight, blocks, voxelLight, queue);
                TrySpread(position + new int3(0, 0, 1), nextLight, blocks, voxelLight, queue);
                TrySpread(position + new int3(0, 0, -1), nextLight, blocks, voxelLight, queue);
            }
        }

        private static void TrySpread(
            int3 position,
            byte light,
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<VoxelLightData> voxelLight,
            NativeQueue<int3> queue)
        {
            if (!ChunkUtility.IsInside(position.x, position.y, position.z))
            {
                return;
            }

            int index = ChunkUtility.ToIndex(position.x, position.y, position.z);

            if (IsOpaque(blocks[index].BlockId) || voxelLight[index].Sky >= light)
            {
                return;
            }

            SetSkyLight(voxelLight, index, light);
            queue.Enqueue(position);
        }

        private static void SetSkyLight(
            DynamicBuffer<VoxelLightData> voxelLight,
            int index,
            byte value)
        {
            VoxelLightData light = voxelLight[index];
            light.Sky = value;
            voxelLight[index] = light;
        }

        private static bool IsOpaque(BlockId blockId)
        {
            return BlockUtility.IsSolid(blockId) &&
                   blockId != BlockId.OceanWater;
        }
    }
}
