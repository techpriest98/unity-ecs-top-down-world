using Game.World.Chunks;
using Game.World.Blocks;
using Unity.Collections;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DynamicLightSystem))]
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    [UpdateBefore(typeof(ChunkLightingSystem))]
    public partial struct ChunkLocalLightUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var affected = new NativeParallelHashSet<int2>(16, Allocator.Temp);
            foreach (var chunk in SystemAPI.Query<RefRO<ChunkComponent>>()
                .WithAll<ChunkGenerated, ChunkNeedsLocalLightUpdate>())
            {
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (affected.Count() == affected.Capacity)
                        affected.Capacity = affected.Capacity * 2;
                    affected.Add(chunk.ValueRO.Coordinate + new int2(x, z));
                }
            }

            if (affected.Count() == 0)
            {
                affected.Dispose();
                return;
            }

            var entities = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>()
                .WithAll<ChunkGenerated, BlockData, VoxelLightData>().WithEntityAccess())
            {
                if (entities.Count() == entities.Capacity)
                    entities.Capacity = entities.Capacity * 2;
                entities.TryAdd(chunk.ValueRO.Coordinate, entity);
            }

            var blocks = new ChunkBlockAccessor(entities, SystemAPI.GetBufferLookup<BlockData>(true));
            var lights = new ChunkLightAccessor(entities, SystemAPI.GetBufferLookup<VoxelLightData>(true));
            ViewDirection direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(out var transition) &&
                transition.IsActive)
                direction = transition.TargetDirection;

            foreach (var (chunk, voxelLight, projectedCells, projectedWaterCells, needsUpdate, needsRender) in
                SystemAPI.Query<
                    RefRO<ChunkComponent>,
                    DynamicBuffer<VoxelLightData>,
                    DynamicBuffer<ProjectedCellData>,
                    DynamicBuffer<ProjectedWaterCellData>,
                    EnabledRefRW<ChunkNeedsLocalLightUpdate>,
                    EnabledRefRW<ChunkNeedsRender>>()
                .WithAll<ChunkGenerated>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                int2 coordinate = chunk.ValueRO.Coordinate;
                if (!affected.Contains(coordinate)) continue;

                UpdateOpaqueCells(projectedCells, voxelLight, coordinate, blocks, lights, direction);
                UpdateWaterCells(projectedWaterCells, voxelLight, coordinate, blocks, lights, direction);
                needsUpdate.ValueRW = false;
                needsRender.ValueRW = true;
            }

            entities.Dispose();
            affected.Dispose();
        }

        private static void UpdateOpaqueCells(
            DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 coordinate,
            ChunkBlockAccessor blocks,
            ChunkLightAccessor lights,
            ViewDirection direction)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                ProjectedCellData cell = cells[index];
                cell.LightData = LightDataUtility.ReplaceLocalLight(
                    cell.LightData,
                    GetLocalLight(cell.SourceAirIndex, voxelLight));

                ChunkLightingSystem.UpdateLocalLightCorners(
                    ref cell, coordinate, blocks, lights, direction);
                cells[index] = cell;
            }
        }

        private static void UpdateWaterCells(
            DynamicBuffer<ProjectedWaterCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 coordinate,
            ChunkBlockAccessor blocks,
            ChunkLightAccessor lights,
            ViewDirection direction)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                ProjectedWaterCellData waterCell = cells[index];
                ProjectedCellData cell = waterCell.Value;

                cell.LightData = LightDataUtility.ReplaceLocalLight(
                    cell.LightData,
                    GetLocalLight(cell.SourceAirIndex, voxelLight));

                ChunkLightingSystem.UpdateLocalLightCorners(
                    ref cell, coordinate, blocks, lights, direction);
                waterCell.Value = cell;
                cells[index] = waterCell;
            }
        }

        private static float3 GetLocalLight(
            ushort sourceAirIndex,
            DynamicBuffer<VoxelLightData> voxelLight)
        {
            if (sourceAirIndex >= voxelLight.Length)
            {
                return float3.zero;
            }

            VoxelLightData light = voxelLight[sourceAirIndex];
            return new float3(light.R, light.G, light.B) / byte.MaxValue;
        }
    }
}
