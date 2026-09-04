using Game.World.Chunks;
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
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (voxelLight, projectedCells, projectedWaterCells, needsUpdate, needsRender) in
                     SystemAPI.Query<
                             DynamicBuffer<VoxelLightData>,
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>,
                             EnabledRefRW<ChunkNeedsLocalLightUpdate>,
                             EnabledRefRW<ChunkNeedsRender>>()
                         .WithAll<ChunkGenerated>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (!needsUpdate.ValueRO)
                {
                    continue;
                }

                UpdateOpaqueCells(projectedCells, voxelLight);
                UpdateWaterCells(projectedWaterCells, voxelLight);

                needsUpdate.ValueRW = false;
                needsRender.ValueRW = true;
            }
        }

        private static void UpdateOpaqueCells(
            DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                ProjectedCellData cell = cells[index];
                cell.LightData = LightDataUtility.ReplaceLocalLight(
                    cell.LightData,
                    GetLocalLight(cell.SourceAirIndex, voxelLight));

                cells[index] = cell;
            }
        }

        private static void UpdateWaterCells(
            DynamicBuffer<ProjectedWaterCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                ProjectedWaterCellData waterCell = cells[index];
                ProjectedCellData cell = waterCell.Value;

                cell.LightData = LightDataUtility.ReplaceLocalLight(
                    cell.LightData,
                    GetLocalLight(cell.SourceAirIndex, voxelLight));

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
