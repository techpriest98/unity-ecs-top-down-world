using System.Collections.Generic;
using Game.Player;
using Game.World.Chunks;
using Game.World.Effects;
using Game.World.Lighting;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Rendering
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    [UpdateAfter(typeof(ChunkLightingSystem))]
    public partial class ChunkProceduralRenderSystem : SystemBase
    {
        private const float ProjectedCellWidth = 1f;
        private const float ProjectedCellHeight = 0.5f;
        private const float DepthStep = 0.01f;
        private const float PlayerClipCenterOffset = 1f;

        private struct OcclusionFace
        {
            public int3 BlockPosition;
            public Rect ProjectedRect;
        }

        private EntityQuery dirtyQuery;
        private EntityQuery pendingProjectionQuery;
        private EntityQuery pendingLightingQuery;

        private readonly List<OcclusionFace> uploadedFaces = new();

        private PlayerOcclusionMask playerOcclusionMask;
        private ViewDirection uploadedDirection;

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

            dirtyQuery = GetEntityQuery(
                ComponentType.ReadOnly<ChunkNeedsRender>());

            pendingProjectionQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<
                        ChunkGenerated,
                        ChunkNeedsProjection>()
                    .Build(ref CheckedStateRef);

            pendingLightingQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<
                        ChunkGenerated,
                        ChunkNeedsLighting>()
                    .Build(ref CheckedStateRef);

            playerOcclusionMask = new PlayerOcclusionMask();
        }

        protected override void OnStopRunning()
        {
            playerOcclusionMask?.Disable();
        }

        protected override void OnDestroy()
        {
            playerOcclusionMask?.Dispose();
        }

        protected override void OnUpdate()
        {
            ChunkProceduralRenderer renderer =
                ChunkProceduralRenderer.Instance;

            if (renderer == null)
            {
                playerOcclusionMask.Disable();
                return;
            }

            int2 currentCenter =
                SystemAPI.GetSingleton<ChunkStreamingCenter>().Coordinate;

            int pendingLightingCount =
                pendingLightingQuery.CalculateEntityCount();

            bool centerChanged =
                !hasLastCenter ||
                math.any(currentCenter != lastCenter);

            bool projectionChanged =
                dirtyQuery.CalculateEntityCount() > 0;

            bool transitionCompleted = false;

            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(
                    out var transition) &&
                transition.IsActive)
            {
                int pendingProjectionCount =
                    pendingProjectionQuery.CalculateEntityCount();

                ViewBlinkEffect blink = ViewBlinkEffect.Instance;

                bool waitingForBlack =
                    blink != null && !blink.CanSwitchView;

                if (pendingProjectionCount > 0 ||
                    pendingLightingCount > 0 ||
                    waitingForBlack)
                {
                    UpdatePlayerOcclusion();
                    return;
                }

                RefRW<ViewDirectionComponent> viewDirection =
                    SystemAPI.GetSingletonRW<ViewDirectionComponent>();

                viewDirection.ValueRW.Value =
                    transition.TargetDirection;

                RefRW<ViewDirectionTransitionComponent> transitionState =
                    SystemAPI
                        .GetSingletonRW<ViewDirectionTransitionComponent>();

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

            if (!centerChanged &&
                !projectionChanged &&
                !transitionCompleted)
            {
                UpdatePlayerOcclusion();
                return;
            }

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
                             EntityQueryOptions.IgnoreComponentEnabledState))
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

            using var renderData =
                new NativeList<ProjectedCellRenderData>(
                    totalCellCount,
                    Allocator.Temp);

            using var clippedRenderData =
                new NativeList<ProjectedCellRenderData>(
                    totalClippedCellCount,
                    Allocator.Temp);

            using var waterRenderData =
                new NativeList<ProjectedCellRenderData>(
                    totalWaterCellCount,
                    Allocator.Temp);

            bool hasBounds = false;
            float3 minBounds = float3.zero;
            float3 maxBounds = float3.zero;

            float chunkWidth =
                ChunkSettings.SizeX * ProjectedCellWidth;

            float projectionHeight =
                (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) *
                ProjectedCellHeight;

            uploadedFaces.Clear();

            foreach (var (
                         chunk,
                         clippingEnabled,
                         projectedCells,
                         projectedWaterCells)
                     in SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             EnabledRefRO<ChunkShaderClippingEnabled>,
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>>()
                         .WithAll<ChunkGenerated>()
                         .WithOptions(
                             EntityQueryOptions.IgnoreComponentEnabledState))
            {
                Vector3 chunkPosition =
                    ChunkRenderPositionUtility.GetPosition(
                        chunk.ValueRO.Coordinate,
                        direction,
                        ProjectedCellWidth,
                        ProjectedCellHeight,
                        DepthStep);

                float3 chunkPositionFloat = new float3(
                    chunkPosition.x,
                    chunkPosition.y,
                    chunkPosition.z);

                for (int index = 0;
                     index < projectedCells.Length;
                     index++)
                {
                    ProjectedCellData cell = projectedCells[index];

                    CacheOcclusionFace(
                        cell,
                        chunk.ValueRO.Coordinate,
                        chunkPosition,
                        direction,
                        chunkWidth,
                        projectionHeight);

                    ProjectedCellRenderData cellRenderData =
                        new ProjectedCellRenderData
                        {
                            BlockData = cell.BlockData,
                            Position = cell.Position,
                            LightData = cell.LightData,
                            FaceData = cell.FaceData,
                            ChunkPosition = chunkPosition,
                            Padding = 0f,

                            LocalLightBottomLeft =
                                cell.LocalLightBottomLeft,

                            LocalLightBottomRight =
                                cell.LocalLightBottomRight,

                            LocalLightTopLeft =
                                cell.LocalLightTopLeft,

                            LocalLightTopRight =
                                cell.LocalLightTopRight
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

                for (int index = 0;
                     index < projectedWaterCells.Length;
                     index++)
                {
                    ProjectedCellData cell =
                        projectedWaterCells[index].Value;

                    waterRenderData.Add(
                        new ProjectedCellRenderData
                        {
                            BlockData = cell.BlockData,
                            Position = cell.Position,
                            LightData = cell.LightData,
                            FaceData = cell.FaceData,
                            ChunkPosition = chunkPosition,
                            Padding = 0f,

                            LocalLightBottomLeft =
                                cell.LocalLightBottomLeft,

                            LocalLightBottomRight =
                                cell.LocalLightBottomRight,

                            LocalLightTopLeft =
                                cell.LocalLightTopLeft,

                            LocalLightTopRight =
                                cell.LocalLightTopRight
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

            Bounds bounds;

            if (hasBounds)
            {
                float3 center = (minBounds + maxBounds) * 0.5f;
                float3 size = maxBounds - minBounds;

                bounds = new Bounds(
                    new Vector3(center.x, center.y, center.z),
                    new Vector3(
                        size.x,
                        size.y,
                        math.max(size.z, 1f)));
            }
            else
            {
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }

            renderer.Upload(renderData.AsArray(), bounds);
            renderer.UploadClipped(clippedRenderData.AsArray(), bounds);
            renderer.UploadWater(waterRenderData.AsArray(), bounds);

            using NativeArray<Entity> dirtyEntities =
                dirtyQuery.ToEntityArray(Allocator.Temp);

            for (int index = 0;
                 index < dirtyEntities.Length;
                 index++)
            {
                EntityManager.SetComponentEnabled<ChunkNeedsRender>(
                    dirtyEntities[index],
                    false);
            }

            lastCenter = currentCenter;
            hasLastCenter = true;

            UploadedCenter = currentCenter;
            HasUploadedView = true;
            uploadedDirection = direction;

            UpdatePlayerOcclusion();

            if (transitionCompleted)
            {
                ViewBlinkEffect blink = ViewBlinkEffect.Instance;

                if (blink != null)
                {
                    blink.Reveal();
                }
            }
        }

        private void CacheOcclusionFace(
            ProjectedCellData cell,
            int2 chunkCoordinate,
            Vector3 chunkPosition,
            ViewDirection direction,
            float chunkWidth,
            float projectionHeight)
        {
            int faceType =
                (int)((cell.BlockData >> 8) & 0xFFu);

            int3 blockPosition =
                ChunkUtility.ToLocalPosition(cell.SourceAirIndex);

            switch (faceType)
            {
                case 1:
                    blockPosition.y--;
                    break;
                case 2:
                case 3:
                {
                    int2 towardCamera =
                        ViewDirectionUtility.Forward(direction);

                    blockPosition.x -= towardCamera.x;
                    blockPosition.z -= towardCamera.y;
                    break;
                }

                default:
                    return;
            }

            blockPosition.x +=
                chunkCoordinate.x * ChunkSettings.SizeX;

            blockPosition.z +=
                chunkCoordinate.y * ChunkSettings.SizeZ;

            int projectionX =
                (int)(cell.Position & 0xFFFFu);

            int projectionY =
                (int)(cell.Position >> 16);

            float left =
                chunkPosition.x -
                chunkWidth * 0.5f +
                projectionX * ProjectedCellWidth;

            float bottom =
                chunkPosition.y +
                projectionHeight -
                (projectionY + 1) * ProjectedCellHeight;

            uploadedFaces.Add(new OcclusionFace
            {
                BlockPosition = blockPosition,

                ProjectedRect = new Rect(
                    left,
                    bottom,
                    ProjectedCellWidth,
                    ProjectedCellHeight)
            });
        }

        private void UpdatePlayerOcclusion()
        {
            if (!HasUploadedView ||
                !SystemAPI.TryGetSingleton<PlayerWorldPosition>(
                    out var playerWorldPosition))
            {
                playerOcclusionMask.Disable();
                return;
            }

            float3 playerPosition = playerWorldPosition.Value;

            float2 projectedPosition =
                WorldPositionProjectionUtility.Project(
                    playerPosition,
                    uploadedDirection);

            playerOcclusionMask.Begin(
                new Vector2(
                    projectedPosition.x,
                    projectedPosition.y));

            int clipFromBlockY = (int)math.floor(
                playerPosition.y +
                PlayerClipCenterOffset +
                0.01f);

            int2 playerCell =(int2)math.floor(playerPosition.xz);

            int2 towardCamera = ViewDirectionUtility.Forward(uploadedDirection);

            for (int i = 0; i < uploadedFaces.Count; i++)
            {
                OcclusionFace face = uploadedFaces[i];

                if (face.BlockPosition.y >= clipFromBlockY)
                {
                    continue;
                }

                if (face.BlockPosition.y + 1f <= playerPosition.y + 0.01f)
                {
                    continue;
                }

                int2 blockCell = new int2(
                    face.BlockPosition.x,
                    face.BlockPosition.z);

                int2 delta = blockCell - playerCell;

                int depth =
                    delta.x * towardCamera.x +
                    delta.y * towardCamera.y;

                if (depth <= 0)
                {
                    continue;
                }

                playerOcclusionMask.AddOccluder(
                    face.ProjectedRect);
            }

            playerOcclusionMask.Publish();
        }
    }
}