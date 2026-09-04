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
        private const float MaximumSkyLight = 15f;
        private const float MinimumAmbientVisibility = 0.08f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DirectionalLightData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float3 ambientColor =
                SystemAPI.GetSingleton<DirectionalLightData>().AmbientColor;

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

                UpdateOpaqueCells(projectedCells, voxelLight, ambientColor);
                UpdateWaterCells(projectedWaterCells, voxelLight, ambientColor);

                needsUpdate.ValueRW = false;
                needsRender.ValueRW = true;
            }
        }

        private static void UpdateOpaqueCells(
            DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight,
            float3 ambientColor)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                ProjectedCellData cell = cells[index];
                cell.LightData = LightDataUtility.ReplaceIndirect(
                    cell.LightData,
                    CalculateIndirectLight(cell.SourceAirIndex, voxelLight, ambientColor));

                cells[index] = cell;
            }
        }

        private static void UpdateWaterCells(
            DynamicBuffer<ProjectedWaterCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight,
            float3 ambientColor)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                ProjectedWaterCellData waterCell = cells[index];
                ProjectedCellData cell = waterCell.Value;

                cell.LightData = LightDataUtility.ReplaceIndirect(
                    cell.LightData,
                    CalculateIndirectLight(cell.SourceAirIndex, voxelLight, ambientColor));

                waterCell.Value = cell;
                cells[index] = waterCell;
            }
        }

        private static float3 CalculateIndirectLight(
            ushort sourceAirIndex,
            DynamicBuffer<VoxelLightData> voxelLight,
            float3 ambientColor)
        {
            VoxelLightData light = sourceAirIndex < voxelLight.Length
                ? voxelLight[sourceAirIndex]
                : new VoxelLightData(15);

            float skyLevel = light.Sky / MaximumSkyLight;
            float ambientVisibility = math.lerp(MinimumAmbientVisibility, 1f, skyLevel);
            float3 localLight = new float3(light.R, light.G, light.B) / byte.MaxValue;

            return ambientColor * ambientVisibility + localLight;
        }
    }
}
