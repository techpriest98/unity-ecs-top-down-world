using Game.World.Chunks;
using Game.World.Rendering;
using Game.World.Time;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DirectionalLightSystem))]
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    [UpdateBefore(typeof(ChunkProceduralRenderSystem))]
    public partial struct ChunkLightingSystem : ISystem
    {
        private const int MaxChunksLitPerFrame = 1;

        private int lastHour;
        private bool initialized;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldTime>();
            state.RequireForUpdate<DirectionalLightData>();
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int hour = SystemAPI.GetSingleton<WorldTime>().Hour;

            if (!initialized || hour != lastHour)
            {
                MarkAllChunksForLighting(ref state);
                lastHour = hour;
                initialized = true;
            }

            DirectionalLightData light = SystemAPI.GetSingleton<DirectionalLightData>();
            ViewDirection direction = GetProjectionDirection(ref state);

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            int litChunkCount = 0;

            foreach (var (projectedCells, projectedWaterCells, entity) in
                     SystemAPI.Query<
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>>()
                         .WithAll<ChunkGenerated, ChunkNeedsLighting>()
                         .WithEntityAccess())
            {
                if (litChunkCount >= MaxChunksLitPerFrame)
                {
                    break;
                }

                UpdateOpaqueCells(projectedCells, light, direction);
                UpdateWaterCells(projectedWaterCells, light, direction);

                ecb.SetComponentEnabled<ChunkNeedsLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsRender>(entity, true);

                litChunkCount++;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private void MarkAllChunksForLighting(
            ref SystemState state)
        {
            foreach (EnabledRefRW<ChunkNeedsLighting> needsLighting in
                    SystemAPI.Query<EnabledRefRW<ChunkNeedsLighting>>()
                        .WithAll<ChunkGenerated>()
                        .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                needsLighting.ValueRW = true;
            }
        }

        private ViewDirection GetProjectionDirection(
            ref SystemState state)
        {
            ViewDirection direction =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(
                    out ViewDirectionTransitionComponent transition) &&
                transition.IsActive)
            {
                direction = transition.TargetDirection;
            }

            return direction;
        }

        private static void UpdateOpaqueCells(
            DynamicBuffer<ProjectedCellData> cells,
            DirectionalLightData light,
            ViewDirection direction)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedCellData cell = cells[i];
                cell.LightData = CalculateLightData(cell.BlockData, light, direction);
                cells[i] = cell;
            }
        }

        private static void UpdateWaterCells(
            DynamicBuffer<ProjectedWaterCellData> cells,
            DirectionalLightData light,
            ViewDirection direction)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedWaterCellData waterCell = cells[i];
                ProjectedCellData cell = waterCell.Value;

                cell.LightData = CalculateLightData(cell.BlockData, light, direction);
                waterCell.Value = cell;
                cells[i] = waterCell;
            }
        }

        private static uint CalculateLightData(
            uint blockData,
            DirectionalLightData light,
            ViewDirection direction)
        {
            ProjectedFaceType faceType =
                (ProjectedFaceType)((blockData >> 8) & 0xFFu);

            float3 normal = GetFaceNormal(faceType, direction);
            float directIntensity = math.max(math.dot(normal, light.DirectionToLight), 0f);

            float3 finalLight =
                light.AmbientColor +
                light.Color * light.Intensity * directIntensity;

            return LightDataUtility.Pack(finalLight);
        }

        private static float3 GetFaceNormal(
            ProjectedFaceType faceType,
            ViewDirection direction)
        {
            if (faceType == ProjectedFaceType.Top)
            {
                return new float3(0f, 1f, 0f);
            }

            int2 towardCamera = ViewDirectionUtility.Forward(direction);

            return new float3(
                towardCamera.x,
                0f,
                towardCamera.y);
        }
    }
}