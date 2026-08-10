using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Rendering
{
    [UpdateInGroup(
        typeof(SimulationSystemGroup))]
    [UpdateAfter(
        typeof(ChunkProjectionSystem))]
    public partial class ChunkProceduralRenderSystem :
        SystemBase
    {
        private const float ProjectedCellWidth =
            1f;

        private const float ProjectedCellHeight =
            0.5f;

        private const float DepthStep =
            0.01f;

        private EntityQuery dirtyQuery;

        private bool hasLastCenter;

        private int2 lastCenter;

        protected override void OnCreate()
        {
            RequireForUpdate<
                ChunkComponent>();

            RequireForUpdate<
                ViewDirectionComponent>();

            RequireForUpdate<
                ChunkStreamingCenter>();

            dirtyQuery =
                GetEntityQuery(
                    ComponentType.ReadOnly<
                        ChunkNeedsRender>());
        }

        protected override void OnUpdate()
        {
            ChunkProceduralRenderer renderer =
                ChunkProceduralRenderer.Instance;

            if (renderer == null)
            {
                return;
            }

            int2 currentCenter =
                SystemAPI.GetSingleton<
                    ChunkStreamingCenter>()
                    .Coordinate;

            bool centerChanged =
                !hasLastCenter ||
                math.any(
                    currentCenter !=
                    lastCenter);

            bool projectionChanged =
                dirtyQuery
                    .CalculateEntityCount() >
                0;

            if (!centerChanged &&
                !projectionChanged)
            {
                return;
            }

            ViewDirection direction =
                SystemAPI.GetSingleton<
                    ViewDirectionComponent>()
                    .Value;

            int totalCellCount =
                0;

            foreach (var projectedCells
                     in SystemAPI.Query<
                             DynamicBuffer<
                                 ProjectedCellData>>()
                         .WithAll<
                             ChunkGenerated>())
            {
                totalCellCount +=
                    projectedCells.Length;
            }

            using var renderData =
                new NativeList<
                    ProjectedCellRenderData>(
                    totalCellCount,
                    Allocator.Temp);

            bool hasBounds =
                false;

            float3 minBounds =
                float3.zero;

            float3 maxBounds =
                float3.zero;

            float chunkWidth =
                ChunkSettings.SizeX *
                ProjectedCellWidth;

            float projectionHeight =
                (
                    ChunkSettings.SizeY *
                    2 +
                    ChunkSettings.SizeZ
                ) *
                ProjectedCellHeight;

            foreach (var (
                        chunk,
                        projectedCells)
                    in SystemAPI.Query<
                            RefRO<ChunkComponent>,
                            DynamicBuffer<
                                ProjectedCellData>>()
                        .WithAll<
                            ChunkGenerated>())
            {
                Vector3 chunkPosition =
                    ChunkRenderPositionUtility
                        .GetPosition(
                            chunk.ValueRO.Coordinate,
                            direction,
                            ProjectedCellWidth,
                            ProjectedCellHeight,
                            DepthStep);

                float3 chunkPositionFloat =
                    new float3(
                        chunkPosition.x,
                        chunkPosition.y,
                        chunkPosition.z);

                for (int index = 0;
                     index <
                     projectedCells.Length;
                     index++)
                {
                    ProjectedCellData cell =
                        projectedCells[index];

                    renderData.Add(
                        new ProjectedCellRenderData
                        {
                            BlockData =
                                cell.BlockData,

                            Position =
                                cell.Position,

                            LightData =
                                cell.LightData,

                            Reserved =
                                cell.Reserved,

                            ChunkPosition =
                                chunkPositionFloat,

                            Padding =
                                0f
                        });
                }

                float3 chunkMin =
                    chunkPositionFloat +
                    new float3(
                        -chunkWidth * 0.5f,
                        0f,
                        -0.1f);

                float3 chunkMax =
                    chunkPositionFloat +
                    new float3(
                        chunkWidth * 0.5f,
                        projectionHeight,
                        0.1f);

                if (!hasBounds)
                {
                    minBounds =
                        chunkMin;

                    maxBounds =
                        chunkMax;

                    hasBounds =
                        true;
                }
                else
                {
                    minBounds =
                        math.min(
                            minBounds,
                            chunkMin);

                    maxBounds =
                        math.max(
                            maxBounds,
                            chunkMax);
                }
            }

            Bounds bounds;

            if (hasBounds)
            {
                float3 center =
                    (
                        minBounds +
                        maxBounds
                    ) *
                    0.5f;

                float3 size =
                    maxBounds -
                    minBounds;

                bounds =
                    new Bounds(
                        new Vector3(
                            center.x,
                            center.y,
                            center.z),

                        new Vector3(
                            size.x,
                            size.y,
                            math.max(
                                size.z,
                                1f)));
            }
            else
            {
                bounds =
                    new Bounds(
                        Vector3.zero,
                        Vector3.one);
            }

            renderer.Upload(
                renderData.AsArray(),
                bounds);

            using NativeArray<Entity> dirtyEntities =
                dirtyQuery.ToEntityArray(
                    Allocator.Temp);

            for (int index = 0;
                 index <
                 dirtyEntities.Length;
                 index++)
            {
                EntityManager
                    .SetComponentEnabled<
                        ChunkNeedsRender>(
                        dirtyEntities[index],
                        false);
            }

            lastCenter =
                currentCenter;

            hasLastCenter =
                true;
        }
    }
}