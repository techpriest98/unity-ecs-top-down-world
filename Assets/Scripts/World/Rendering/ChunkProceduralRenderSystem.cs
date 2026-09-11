using Game.World.Chunks;
using Game.World.Lighting;
using Game.World.Effects;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Rendering
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    [UpdateAfter(typeof(ChunkLightingSystem))]
    public partial class ChunkProceduralRenderSystem :
        SystemBase
    {
        private const float ProjectedCellWidth = 1f;
        private const float ProjectedCellHeight = 0.5f;
        private const float DepthStep = 0.01f;

        private EntityQuery dirtyQuery;
        private EntityQuery pendingProjectionQuery;
        private EntityQuery pendingLightingQuery;

        private bool hasLastCenter;
        private int2 lastCenter;

        public bool HasUploadedView { get; private set; }
        public int2 UploadedCenter { get; private set; }


        protected override void OnCreate()
        {
            RequireForUpdate<ChunkComponent>();
            RequireForUpdate<ViewDirectionComponent>();
            RequireForUpdate<ChunkStreamingCenter>();
            RequireForUpdate<DirectionalLightData>();
            RequireForUpdate<SunShadowState>();

            // ------------------------------------------------------------
            // Chunks whose CPU projection changed
            // and therefore GPU data is outdated.
            // ------------------------------------------------------------

            dirtyQuery = GetEntityQuery(ComponentType.ReadOnly<ChunkNeedsRender>());

            // ------------------------------------------------------------
            // Generated chunks which are still waiting
            // for their projection.
            //
            // ChunkNeedsProjection is enableable,
            // so disabled components are not counted.
            // ------------------------------------------------------------

            pendingProjectionQuery =
                new EntityQueryBuilder(
                        Allocator.Temp)
                    .WithAll<
                        ChunkGenerated,
                        ChunkNeedsProjection>()
                    .Build(
                        ref CheckedStateRef);

            pendingLightingQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<
                        ChunkGenerated,
                        ChunkNeedsLighting>()
                    .Build(ref CheckedStateRef);
        }


        protected override void OnUpdate()
        {
            ChunkProceduralRenderer renderer = ChunkProceduralRenderer.Instance;

            if (renderer == null)
            {
                return;
            }

            int2 currentCenter = SystemAPI.GetSingleton<ChunkStreamingCenter>().Coordinate;
            int pendingLightingCount = pendingLightingQuery.CalculateEntityCount();

            bool centerChanged = !hasLastCenter || math.any(currentCenter != lastCenter);
            bool projectionChanged = dirtyQuery.CalculateEntityCount() > 0;

            // ============================================================
            // View transition
            // ============================================================

            bool transitionCompleted = false;


            if (SystemAPI.TryGetSingleton<
                    ViewDirectionTransitionComponent>(
                    out ViewDirectionTransitionComponent
                        transition) &&
                transition.IsActive)
            {
                int pendingProjectionCount = pendingProjectionQuery.CalculateEntityCount();

                // --------------------------------------------------------
                // CPU is still preparing TargetDirection.
                //
                // IMPORTANT:
                //
                // Do NOT upload partially converted chunks.
                //
                // The previous complete view remains in the
                // existing GPU GraphicsBuffer.
                // --------------------------------------------------------

                ViewBlinkEffect blink = ViewBlinkEffect.Instance;

                bool waitingForBlack = blink != null && !blink.CanSwitchView;

                if (pendingProjectionCount > 0 || pendingLightingCount > 0 || waitingForBlack)
                {
                    return;
                }

                // --------------------------------------------------------
                // Every currently generated chunk now contains
                // projection data for TargetDirection.
                //
                // We can atomically change the active direction.
                // --------------------------------------------------------

                RefRW<ViewDirectionComponent> viewDirection =
                    SystemAPI.GetSingletonRW<ViewDirectionComponent>();
                viewDirection.ValueRW.Value = transition.TargetDirection;

                RefRW<ViewDirectionTransitionComponent> transitionState =
                    SystemAPI.GetSingletonRW<ViewDirectionTransitionComponent>();

                transitionState.ValueRW.IsActive = false;
                transitionCompleted = true;
            }

            ViewDirection direction =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            SunShadowState shadowState =
                SystemAPI.GetSingleton<SunShadowState>();

            DirectionalLightData light = shadowState.Initialized
                ? shadowState.ActiveLight
                : SystemAPI.GetSingleton<DirectionalLightData>();

            int2 towardCamera =
                ViewDirectionUtility.Forward(direction);

            renderer.SetDirectionalLight(
                light.DirectionToLight,
                light.Color,
                light.Intensity,
                light.AmbientColor,
                new float3(
                    towardCamera.x,
                    0f,
                    towardCamera.y),
                shadowState.ActiveMaskIndex);

            // ============================================================
            // Do we need a GPU rebuild?
            // ============================================================

            if (!centerChanged && !projectionChanged && !transitionCompleted)
            {
                return;
            }

            // ============================================================
            // Count projected cells
            // ============================================================

            int totalCellCount = 0;
            int totalClippedCellCount = 0;
            int totalWaterCellCount = 0;

            foreach (var (
                    clippingEnabled,
                    projectedCells,
                    projectedWaterCells)
                in SystemAPI.Query<
                    EnabledRefRO<ChunkShaderClippingEnabled>,
                    DynamicBuffer<ProjectedCellData>,
                    DynamicBuffer<ProjectedWaterCellData>>()
                .WithAll<ChunkGenerated>()
                .WithOptions(
                    EntityQueryOptions
                        .IgnoreComponentEnabledState))
            {
                if (clippingEnabled.ValueRO)
                {
                    totalClippedCellCount += projectedCells.Length;
                }
                else
                {
                    totalCellCount += projectedCells.Length;
                }

                totalWaterCellCount += projectedWaterCells.Length;
            }


            // ============================================================
            // Build GPU render data
            // ============================================================

            using var renderData = new NativeList<ProjectedCellRenderData>(
                totalCellCount,
                Allocator.Temp);

            using var waterRenderData = new NativeList<ProjectedCellRenderData>(
                totalWaterCellCount,
                Allocator.Temp);

            using var clippedRenderData = new NativeList<ProjectedCellRenderData>(
                totalClippedCellCount,
                Allocator.Temp);

            bool hasBounds = false;

            float3 minBounds = float3.zero;
            float3 maxBounds = float3.zero;

            float chunkWidth = ChunkSettings.SizeX * ProjectedCellWidth;
            float projectionHeight = (
                ChunkSettings.SizeY *
                2 +
                ChunkSettings.SizeZ
            ) * ProjectedCellHeight;


            foreach (var (
                        chunk,
                        clippingEnabled,
                        projectedCells,
                        projectedWaterCells)
                    in SystemAPI.Query<
                            RefRO<ChunkComponent>,
                            EnabledRefRO<
                                ChunkShaderClippingEnabled>,
                            DynamicBuffer<ProjectedCellData>,
                            DynamicBuffer<ProjectedWaterCellData>>()
                        .WithAll<ChunkGenerated>()
                        .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                Vector3 chunkPosition = ChunkRenderPositionUtility.GetPosition(
                    chunk.ValueRO.Coordinate,
                    direction,
                    ProjectedCellWidth,
                    ProjectedCellHeight,
                    DepthStep);

                float3 chunkPositionFloat = new float3(
                    chunkPosition.x,
                    chunkPosition.y,
                    chunkPosition.z);

                // --------------------------------------------------------
                // Cells
                // --------------------------------------------------------

                for (int index = 0; index < projectedCells.Length; index++)
                {
                    ProjectedCellData cell = projectedCells[index];

                    ProjectedCellRenderData cellRenderData = new ProjectedCellRenderData
                    {
                        BlockData = cell.BlockData,
                        Position = cell.Position,
                        LightData = cell.LightData,
                        FaceData = cell.FaceData,
                        ChunkPosition = chunkPosition,
                        Padding = 0f,
                        LocalLightBottomLeft = cell.LocalLightBottomLeft,
                        LocalLightBottomRight = cell.LocalLightBottomRight,
                        LocalLightTopLeft = cell.LocalLightTopLeft,
                        LocalLightTopRight = cell.LocalLightTopRight
                    };

                    if (clippingEnabled.ValueRO)
                    {
                        clippedRenderData.Add(cellRenderData);
                    }
                    else
                    {
                        renderData.Add(cellRenderData);
                    }
                }

                for (int index = 0; index < projectedWaterCells.Length; index++)
                {
                    ProjectedCellData cell = projectedWaterCells[index].Value;

                    waterRenderData.Add(new ProjectedCellRenderData
                    {
                        BlockData = cell.BlockData,
                        Position = cell.Position,
                        LightData = cell.LightData,
                        FaceData = cell.FaceData,
                        ChunkPosition = chunkPosition,
                        Padding = 0f,
                        LocalLightBottomLeft = cell.LocalLightBottomLeft,
                        LocalLightBottomRight = cell.LocalLightBottomRight,
                        LocalLightTopLeft = cell.LocalLightTopLeft,
                        LocalLightTopRight = cell.LocalLightTopRight
                    });
                }

                // --------------------------------------------------------
                // Bounds
                // --------------------------------------------------------

                float3 chunkMin = chunkPositionFloat + new float3(
                    -chunkWidth * 0.5f,
                    0f,
                    -0.1f);

                float3 chunkMax = chunkPositionFloat + new float3(
                    chunkWidth * 0.5f,
                    projectionHeight,
                    0.1f);


                if (!hasBounds)
                {
                    minBounds = chunkMin;
                    maxBounds = chunkMax;
                    hasBounds = true;
                }
                else
                {
                    minBounds = math.min(minBounds, chunkMin);
                    maxBounds = math.max(maxBounds, chunkMax);
                }
            }


            // ============================================================
            // Global bounds
            // ============================================================

            Bounds bounds;


            if (hasBounds)
            {
                float3 center = (minBounds + maxBounds) * 0.5f;
                float3 size = maxBounds - minBounds;

                bounds = new Bounds(
                    new Vector3(center.x, center.y, center.z),
                    new Vector3(size.x, size.y, math.max(size.z, 1f)));
            }
            else
            {
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }


            // ============================================================
            // One complete GPU upload
            // ============================================================

            renderer.Upload(renderData.AsArray(), bounds);
            renderer.UploadClipped(clippedRenderData.AsArray(), bounds);
            renderer.UploadWater(waterRenderData.AsArray(), bounds);

            if (transitionCompleted)
            {
                ViewBlinkEffect blink = ViewBlinkEffect.Instance;

                if (blink != null)
                {
                    blink.Reveal();
                }
            }

            // ============================================================
            // GPU is now synchronized with CPU projection.
            // Clear dirty flags.
            // ============================================================

            using NativeArray<Entity> dirtyEntities = dirtyQuery.ToEntityArray(Allocator.Temp);

            for (int index = 0; index < dirtyEntities.Length; index++)
            {
                EntityManager.SetComponentEnabled<ChunkNeedsRender>(dirtyEntities[index], false);
            }

            lastCenter = currentCenter;
            hasLastCenter = true;

            UploadedCenter = currentCenter;
            HasUploadedView = true;
        }
    }
}
