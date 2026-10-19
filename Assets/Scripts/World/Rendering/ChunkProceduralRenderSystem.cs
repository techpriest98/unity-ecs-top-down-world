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

        private EntityQuery dirtyQuery;
        private EntityQuery pendingProjectionQuery;
        private EntityQuery pendingLightingQuery;

        private bool hasLastCenter;
        private int2 lastCenter;

        private sealed class UploadSnapshot
        {
            public bool Clipped;
            public int OpaqueStart, WaterStart;
            public int2 Coordinate;
            public ProjectedCellRenderData[] Opaque, Water;
            public ProjectedClipCell[] ClipCells;
        }
        private readonly System.Collections.Generic.Dictionary<Entity, UploadSnapshot> uploadedChunks =
            new System.Collections.Generic.Dictionary<Entity, UploadSnapshot>();
        private NativeList<ProjectedCellRenderData> lightUploadScratch;
        private ChunkProceduralRenderer uploadedRenderer;
        private ViewDirection uploadedDirection;
        private readonly System.Collections.Generic.HashSet<Entity> snapshotSeen = new();
        private readonly System.Collections.Generic.List<Entity> removedSnapshots = new();
        private static readonly Unity.Profiling.ProfilerMarker LightRangeMarker = new("ChunkRender.LightRanges");
        private static readonly Unity.Profiling.ProfilerMarker FullSnapshotMarker = new("ChunkRender.FullSnapshot");

        protected override void OnDestroy()
        {
            if (lightUploadScratch.IsCreated) lightUploadScratch.Dispose();
            uploadedChunks.Clear();
        }

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
        }

        protected override void OnUpdate()
        {
            ChunkProceduralRenderer renderer = ChunkProceduralRenderer.Instance;

            if (renderer == null || !renderer.enabled)
            {
                return;
            }
            if (SystemAPI.TryGetSingleton<Game.World.Time.WorldWind>(out var wind)) renderer.SetWind(wind);

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

            bool hasSeaPlane = SystemAPI.TryGetSingleton<Game.World.Generation.WorldGenerationSettingsComponent>(out var waterSettings);
            // Project() locates an entity's center; the rendered Top quad's
            // physical sea plane is half a projected pixel-cell higher.
            float2 seaOrigin = hasSeaPlane
                ? WorldPositionProjectionUtility.Project(new float3(0f, waterSettings.WaterSurfaceHeight, 0f), direction) +
                    new float2(0f, ProjectedCellHeight * .5f)
                : float2.zero;
            renderer.SetOceanSurfaceProjection(seaOrigin, hasSeaPlane);

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
                return;
            }

            // All chunks share one mask and GPU upload. Publishing a partial
            // rebuild can remove an old mask contributor before its replacement
            // is ready, exposing unclipped geometry for a frame.
            // Preserve the last GPU snapshot, including dirty flags, until both
            // geometry and lighting are ready to be published together.
            if (pendingProjectionQuery.CalculateEntityCount() > 0 ||
                pendingLightingCount > 0)
            {
                return;
            }

            if (!centerChanged && !transitionCompleted && uploadedRenderer == renderer &&
                uploadedDirection == direction && TryUpdateLightingRanges(renderer)) return;

            // Reuse per-chunk CPU arrays on geometry rebuilds; do not create
            // a managed copy of the entire world on each clipping change.
            uploadedRenderer = null;
            snapshotSeen.Clear();
            using var fullSnapshotMarker = FullSnapshotMarker.Auto();

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
            using var clippedWaterRenderData = new NativeList<ProjectedCellRenderData>(Allocator.Temp);

            bool hasBounds = false;
            using var clipRects = new NativeList<float4>(Allocator.Temp);
            float3 minBounds = float3.zero;
            float3 maxBounds = float3.zero;

            float chunkWidth =
                ChunkSettings.SizeX * ProjectedCellWidth;

            float projectionHeight =
                (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) *
                ProjectedCellHeight;

            foreach (var (
                         chunk,
                         clippingEnabled,
                         projectedCells,
                         projectedWaterCells,
                         clipCells,
                         chunkEntity)
                     in SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             EnabledRefRO<ChunkShaderClippingEnabled>,
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>,
                             DynamicBuffer<ProjectedClipCell>>()
                         .WithAll<ChunkGenerated>()
                         .WithOptions(
                             EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
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

                if (!uploadedChunks.TryGetValue(chunkEntity, out var snapshot))
                {
                    snapshot = new UploadSnapshot();
                    uploadedChunks.Add(chunkEntity, snapshot);
                }
                snapshotSeen.Add(chunkEntity);
                snapshot.Clipped = clippingEnabled.ValueRO;
                snapshot.Coordinate = chunk.ValueRO.Coordinate;
                snapshot.OpaqueStart = clippingEnabled.ValueRO ? clippedRenderData.Length : renderData.Length;
                snapshot.WaterStart = clippingEnabled.ValueRO ? clippedWaterRenderData.Length : waterRenderData.Length;
                if (snapshot.Opaque == null || snapshot.Opaque.Length != projectedCells.Length)
                    snapshot.Opaque = new ProjectedCellRenderData[projectedCells.Length];
                if (snapshot.Water == null || snapshot.Water.Length != projectedWaterCells.Length)
                    snapshot.Water = new ProjectedCellRenderData[projectedWaterCells.Length];
                if (snapshot.ClipCells == null || snapshot.ClipCells.Length != clipCells.Length)
                    snapshot.ClipCells = new ProjectedClipCell[clipCells.Length];

                for (int index = 0; index < clipCells.Length; index++)
                {
                    ProjectedClipCell cell = clipCells[index];
                    snapshot.ClipCells[index] = cell;
                    float left = chunkPosition.x - chunkWidth * 0.5f + cell.X * ProjectedCellWidth;
                    float bottom = chunkPosition.y + projectionHeight - (cell.Y + 1) * ProjectedCellHeight;
                    clipRects.Add(new float4(left, bottom,
                        left + ProjectedCellWidth, bottom + ProjectedCellHeight));
                }

                for (int index = 0;
                     index < projectedCells.Length;
                     index++)
                {
                    ProjectedCellData cell = projectedCells[index];

                    ProjectedCellRenderData cellRenderData =
                        new ProjectedCellRenderData
                        {
                            BlockData = cell.BlockData,
                            Position = cell.Position,
                            LightData = cell.LightData,
                            FaceData = cell.FaceData,
                            ChunkPosition = chunkPosition,
                            LocalLightDirection = cell.LocalLightDirection,

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
                    snapshot.Opaque[index] = cellRenderData;
                }

                for (int index = 0;
                     index < projectedWaterCells.Length;
                     index++)
                {
                    ProjectedCellData cell =
                        projectedWaterCells[index].Value;

                    var waterTarget = clippingEnabled.ValueRO ? clippedWaterRenderData : waterRenderData;
                    waterTarget.Add(
                        new ProjectedCellRenderData
                        {
                            BlockData = cell.BlockData,
                            Position = cell.Position,
                            LightData = cell.LightData,
                            FaceData = cell.FaceData,
                            ChunkPosition = chunkPosition,
                            LocalLightDirection = cell.LocalLightDirection,

                            LocalLightBottomLeft =
                                cell.LocalLightBottomLeft,

                            LocalLightBottomRight =
                                cell.LocalLightBottomRight,

                            LocalLightTopLeft =
                                cell.LocalLightTopLeft,

                            LocalLightTopRight =
                                cell.LocalLightTopRight
                        });
                    snapshot.Water[index] = waterTarget[waterTarget.Length - 1];
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
            renderer.UploadClipMask(clipRects.AsArray());
            renderer.UploadClipped(clippedRenderData.AsArray(), bounds);
            renderer.UploadWater(waterRenderData.AsArray(), bounds);
            renderer.UploadClippedWater(clippedWaterRenderData.AsArray(), bounds);
            removedSnapshots.Clear();
            foreach (var pair in uploadedChunks)
                if (!snapshotSeen.Contains(pair.Key)) removedSnapshots.Add(pair.Key);
            foreach (var entity in removedSnapshots) uploadedChunks.Remove(entity);
            uploadedRenderer = renderer;
            uploadedDirection = direction;

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

            if (transitionCompleted)
            {
                ViewBlinkEffect blink = ViewBlinkEffect.Instance;

                if (blink != null)
                {
                    blink.Reveal();
                }
            }
        }

        private bool TryUpdateLightingRanges(ChunkProceduralRenderer renderer)
        {
            using var marker = LightRangeMarker.Auto();
            // Validate the complete layout BEFORE publishing any range. A
            // removal, clipping change or rebuilt geometry takes the old atomic
            // full-snapshot path instead (including a changed clip mask).
            int count = 0;
            foreach (var (chunk, clipped, cells, water, caps, entity) in SystemAPI.Query<
                RefRO<ChunkComponent>, EnabledRefRO<ChunkShaderClippingEnabled>, DynamicBuffer<ProjectedCellData>,
                DynamicBuffer<ProjectedWaterCellData>, DynamicBuffer<ProjectedClipCell>>()
                .WithAll<ChunkGenerated>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
            {
                count++;
                if (!uploadedChunks.TryGetValue(entity, out var snapshot) || snapshot.Clipped != clipped.ValueRO ||
                    math.any(snapshot.Coordinate != chunk.ValueRO.Coordinate) ||
                    snapshot.Opaque.Length != cells.Length || snapshot.Water.Length != water.Length ||
                    snapshot.ClipCells.Length != caps.Length ||
                    !renderer.CanUpdateRange(snapshot.OpaqueStart, cells.Length, snapshot.Clipped, false) ||
                    !renderer.CanUpdateRange(snapshot.WaterStart, water.Length, snapshot.Clipped, true)) return false;
                if (!EntityManager.IsComponentEnabled<ChunkNeedsRender>(entity)) continue;
                for (int i = 0; i < cells.Length; i++)
                    if (!snapshot.Opaque[i].HasSameGeometry(cells[i].BlockData, cells[i].Position, cells[i].FaceData)) return false;
                for (int i = 0; i < water.Length; i++)
                    if (!snapshot.Water[i].HasSameGeometry(water[i].Value.BlockData, water[i].Value.Position, water[i].Value.FaceData)) return false;
                for (int i = 0; i < caps.Length; i++)
                    if (snapshot.ClipCells[i].X != caps[i].X || snapshot.ClipCells[i].Y != caps[i].Y) return false;
            }
            if (count != uploadedChunks.Count) return false;
            if (!lightUploadScratch.IsCreated) lightUploadScratch = new NativeList<ProjectedCellRenderData>(1024, Allocator.Persistent);

            using var dirty = dirtyQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in dirty)
            {
                if (!uploadedChunks.TryGetValue(entity, out var snapshot)) continue;
                var cells = EntityManager.GetBuffer<ProjectedCellData>(entity, true);
                var water = EntityManager.GetBuffer<ProjectedWaterCellData>(entity, true);
                lightUploadScratch.Clear();
                bool changed = false;
                for (int i = 0; i < cells.Length; i++)
                {
                    var record = snapshot.Opaque[i];
                    changed |= UpdateLightRecord(ref record, cells[i]);
                    lightUploadScratch.Add(record);
                }
                if (changed)
                {
                    renderer.UpdateRange(lightUploadScratch.AsArray(), snapshot.OpaqueStart, snapshot.Clipped, false);
                    for (int i = 0; i < cells.Length; i++) snapshot.Opaque[i] = lightUploadScratch[i];
                }
                lightUploadScratch.Clear();
                changed = false;
                for (int i = 0; i < water.Length; i++)
                {
                    var record = snapshot.Water[i];
                    changed |= UpdateLightRecord(ref record, water[i].Value);
                    lightUploadScratch.Add(record);
                }
                if (changed)
                {
                    renderer.UpdateRange(lightUploadScratch.AsArray(), snapshot.WaterStart, snapshot.Clipped, true);
                    for (int i = 0; i < water.Length; i++) snapshot.Water[i] = lightUploadScratch[i];
                }
                EntityManager.SetComponentEnabled<ChunkNeedsRender>(entity, false);
            }
            return true;
        }

        private static bool UpdateLightRecord(ref ProjectedCellRenderData record, in ProjectedCellData cell)
        {
            return record.UpdateLighting(cell.LightData, cell.LocalLightDirection, cell.LocalLightBottomLeft,
                cell.LocalLightBottomRight, cell.LocalLightTopLeft, cell.LocalLightTopRight);
        }

    }
}
