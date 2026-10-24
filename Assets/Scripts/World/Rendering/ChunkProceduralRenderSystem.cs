using Game.Player;
using Game.World.Chunks;
using Game.World.Effects;
using Game.World.Lighting;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
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
            if (!WorldRenderDebug.ProjectionRequired) return;
            ChunkProceduralRenderer renderer = ChunkProceduralRenderer.Instance;

            if (renderer == null || !renderer.enabled)
            {
                return;
            }
            if (SystemAPI.TryGetSingleton<Game.World.Time.WorldWind>(out var wind)) renderer.SetWind(wind);
            renderer.SetWorldDelta(SystemAPI.TryGetSingleton<Game.World.Time.WorldTime>(out var clock) ? clock.DeltaSeconds : 0f);
            bool hasFogPlayer = SystemAPI.TryGetSingleton<PlayerWorldPosition>(out var fogPlayer);
            renderer.SetTerrainReferenceHeight(hasFogPlayer ? fogPlayer.Value.y : 0f, hasFogPlayer);
            renderer.SetFogPlayerPosition(hasFogPlayer
                ? WorldPositionProjectionUtility.Project(fogPlayer.Value, SystemAPI.GetSingleton<ViewDirectionComponent>().Value)
                : float2.zero, hasFogPlayer);

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
            renderer.SetFogSeaLevel(hasSeaPlane ? waterSettings.WaterSurfaceHeight : 0f, hasSeaPlane);

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

            // Existing GPU ranges stay valid until the next full snapshot. Refresh
            // ready chunks even while unrelated chunks are being built or streamed.
            bool rangesUpToDate = !transitionCompleted && uploadedRenderer == renderer &&
                uploadedDirection == direction && TryUpdateLightingRanges(renderer);
            if (!centerChanged && rangesUpToDate) return;

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

            // Reuse per-chunk CPU arrays on geometry rebuilds; do not create
            // a managed copy of the entire world on each clipping change.
            uploadedRenderer = null;
            snapshotSeen.Clear();
            using var fullSnapshotMarker = FullSnapshotMarker.Auto();

            int totalCellCount = 0;
            int totalClippedCellCount = 0;
            int totalWaterCellCount = 0;

            renderer.BeginFogChunks();
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
                renderer.AddFogChunk(chunkPosition);

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
                }

                for (int index = 0;
                     index < projectedCells.Length;
                     index++)
                {
                    ProjectedCellData cell = projectedCells[index];

                    ProjectedCellRenderData cellRenderData =
                        new ProjectedCellRenderData
                        {
                            BlockData = ProjectedCellRenderData.PackBlockData(cell.BlockData, cell.TopFrontMaterial),
                            Position = cell.Position,
                            LightData = cell.LightData,
                            FaceData = ProjectedCellRenderData.PackFaceData(cell.FaceData, cell.SourceAirIndex),
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
                            BlockData = ProjectedCellRenderData.PackBlockData(cell.BlockData, cell.TopFrontMaterial),
                            Position = cell.Position,
                            LightData = cell.LightData,
                            FaceData = ProjectedCellRenderData.PackFaceData(cell.FaceData, cell.SourceAirIndex),
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
            renderer.UploadFogChunks(bounds);
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
            // Validate each chunk against its OWN published range. A changed or
            // unfinished neighbour must not prevent safe lighting-only uploads.
            using var ready = new NativeParallelHashSet<Entity>(math.max(uploadedChunks.Count, 1), Allocator.Temp);
            bool layoutReady = true;
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
                    !renderer.CanUpdateRange(snapshot.WaterStart, water.Length, snapshot.Clipped, true))
                { layoutReady = false; continue; }
                if (EntityManager.IsComponentEnabled<ChunkNeedsProjection>(entity) ||
                    EntityManager.IsComponentEnabled<ChunkNeedsLighting>(entity))
                { layoutReady = false; continue; }
                if (!EntityManager.IsComponentEnabled<ChunkNeedsRender>(entity)) continue;
                bool sameGeometry = true;
                for (int i = 0; i < cells.Length; i++)
                    if (!snapshot.Opaque[i].HasSameGeometry(cells[i].BlockData, cells[i].Position, cells[i].FaceData, cells[i].SourceAirIndex, cells[i].TopFrontMaterial)) { sameGeometry = false; break; }
                for (int i = 0; i < water.Length; i++)
                    if (!snapshot.Water[i].HasSameGeometry(water[i].Value.BlockData, water[i].Value.Position, water[i].Value.FaceData, water[i].Value.SourceAirIndex, water[i].Value.TopFrontMaterial)) { sameGeometry = false; break; }
                for (int i = 0; i < caps.Length; i++)
                    if (snapshot.ClipCells[i].X != caps[i].X || snapshot.ClipCells[i].Y != caps[i].Y) { sameGeometry = false; break; }
                if (!sameGeometry) { layoutReady = false; continue; }
                ready.Add(entity);
            }
            if (!lightUploadScratch.IsCreated) lightUploadScratch = new NativeList<ProjectedCellRenderData>(1024, Allocator.Persistent);

            using var dirty = dirtyQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in dirty)
            {
                if (!ready.Contains(entity) || !uploadedChunks.TryGetValue(entity, out var snapshot)) continue;
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
            return layoutReady && count == uploadedChunks.Count;
        }

        private static bool UpdateLightRecord(ref ProjectedCellRenderData record, in ProjectedCellData cell)
        {
            return record.UpdateLighting(cell.LightData, cell.LocalLightDirection, cell.LocalLightBottomLeft,
                cell.LocalLightBottomRight, cell.LocalLightTopLeft, cell.LocalLightTopRight);
        }

    }
}

namespace Game.World.Rendering
{
    [Unity.Burst.BurstCompile]
    internal struct LocalProjectionBuildJob : IJob
    {
        [ReadOnly] public DynamicBuffer<Game.World.Blocks.BlockData> Blocks;
        [ReadOnly] public ChunkBlockAccessor Accessor;
        public ProjectionOccupancy Occupancy, WaterOccupancy;
        public DynamicBuffer<ProjectedCellData> Solids;
        public DynamicBuffer<ProjectedWaterCellData> Water;
        public DynamicBuffer<ProjectedClipCell> Clips;
        public int2 Coordinate;
        public ViewDirection View;
        public float3 Feet;
        public int FirstColumn, ColumnCount, FirstRow, LastRow;
        public void Execute()
        {
            // Construct shared occupancy writers here so job validation sees each container only once.
            var writer = new ProjectionWriter(Occupancy, Solids, Clips);
            var waterWriter = new WaterProjectionWriter(Occupancy, WaterOccupancy, Water);
            ChunkProjectionBuilder.BuildColumns(Blocks, Coordinate, Accessor, writer,
                waterWriter, View, true, Feet, FirstColumn, ColumnCount, FirstRow, LastRow);
        }
    }

    // Owns scratch projection buffers, never the projection buffers of terrain chunks.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalProjectionRenderSystem : SystemBase
    {
        private Entity scratch;
        private EntityQuery sources;
        private bool published;
        private int3 lastCell;
        private int2 lastRegionStep;
        private uint lastGeometry, lastSun;
        private ViewDirection lastView;
        private LocalProjectionRenderer lastRenderer;
        private static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("LocalProjection.BuildFromVoxels");
        private static readonly Unity.Profiling.ProfilerMarker GeometryMarker = new("LocalProjection.Geometry");
        private static readonly Unity.Profiling.ProfilerMarker LightingMarker = new("LocalProjection.Lighting");
        private static readonly Unity.Profiling.ProfilerMarker UploadMarker = new("LocalProjection.Upload");

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerWorldPosition>();
            RequireForUpdate<ViewDirectionComponent>();
            RequireForUpdate<SunShadowState>();
            scratch = EntityManager.CreateEntity();
            EntityManager.AddBuffer<ProjectedCellData>(scratch);
            EntityManager.AddBuffer<ProjectedWaterCellData>(scratch);
            EntityManager.AddBuffer<ProjectedClipCell>(scratch);
            sources = GetEntityQuery(ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                ComponentType.ReadOnly<Game.World.Blocks.BlockData>(), ComponentType.ReadOnly<VoxelLightData>());
        }

        protected override void OnUpdate()
        {
            var renderer = LocalProjectionRenderer.Instance;
            if (renderer == null || !renderer.IsActive)
            { if (renderer != null) renderer.Invalidate(); published = false; return; }
            renderer.Invalidate();
            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(out var transition) && transition.IsActive) return;
            float3 feet = SystemAPI.GetSingleton<PlayerWorldPosition>().Value;
            var view = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            var sun = SystemAPI.GetSingleton<SunShadowState>();
            float2 center = WorldPositionProjectionUtility.Project(feet + new float3(0, 1, 0), view);
            int2 regionStep = (int2)math.floor(center * 8f);
            int3 cell = (int3)math.floor(feet);
            cell.y = (int)(ChunkProjectionBuilder.GetClippingHeight(feet) * 2);
            using var entities = sources.ToEntityArray(Allocator.Temp);
            using var chunks = new NativeParallelHashMap<int2, Entity>(math.max(entities.Length, 1), Allocator.TempJob);
            var voxelLights = GetBufferLookup<VoxelLightData>(true);
            uint geometry = (uint)entities.Length;
            bool lightChanged = false;
            foreach (var entity in entities)
            {
                int2 coordinate = EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate;
                chunks.TryAdd(coordinate, entity);
                uint revision = EntityManager.HasComponent<Game.World.Blocks.ChunkGeometryRevision>(entity)
                    ? EntityManager.GetComponentData<Game.World.Blocks.ChunkGeometryRevision>(entity).Value : 0;
                geometry = unchecked(geometry + math.hash(new uint4((uint)entity.Index, (uint)entity.Version, revision, math.hash(coordinate))));
                lightChanged |= voxelLights.DidChange(entity, LastSystemVersion);
            }
            uint sunHash = math.hash(new float4(sun.ActiveLight.DirectionToLight, sun.ActiveLight.Intensity)) ^ (uint)sun.ActiveMaskIndex;
            if (published && lastRenderer == renderer && math.all(lastCell == cell) && math.all(lastRegionStep == regionStep) && lastView == view &&
                lastGeometry == geometry && lastSun == sunHash && !lightChanged)
            { renderer.MarkReady(); return; }

            using var marker = BuildMarker.Auto();
            renderer.BeginOcclusionSnapshot(feet, view);
            var blocks = GetBufferLookup<Game.World.Blocks.BlockData>(true);
            var accessor = new ChunkBlockAccessor(chunks, blocks, GetBufferLookup<Game.World.Blocks.SplitCellData>(true),
                GetBufferLookup<Game.World.Blocks.ChunkSurfaceSample>(true));
            var lightAccessor = new ChunkLightAccessor(chunks, voxelLights);
            var solids = EntityManager.GetBuffer<ProjectedCellData>(scratch);
            var water = EntityManager.GetBuffer<ProjectedWaterCellData>(scratch);
            var clips = EntityManager.GetBuffer<ProjectedClipCell>(scratch);
            using var opaqueOutput = new NativeList<ProjectedCellRenderData>(Allocator.Temp);
            using var waterOutput = new NativeList<ProjectedCellRenderData>(Allocator.Temp);
            using var occupancy = new ProjectionOccupancy(4096, Allocator.TempJob);
            using var waterOccupancy = new ProjectionOccupancy(4096, Allocator.TempJob);
            var directionCache = new LocalLightDirectionCache(1024, Allocator.Temp);
            using var coverage = new NativeList<float4>(Allocator.Temp);
            int2 coverageMin = new int2((int)math.floor(center.x - 5), (int)math.floor((center.y - 5) * 2));
            int2 coverageSize = new int2(12, 24);
            int caps = 0;
            try
            {
                foreach (var entity in entities)
                {
                    // Generation publishes buffers in stages; never index partially initialized data.
                    if (blocks[entity].Length != ChunkSettings.BlockCount || voxelLights[entity].Length != ChunkSettings.BlockCount)
                        continue;
                    int2 coordinate = EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate;
                    float3 position = ChunkRenderPositionUtility.GetPosition(coordinate, view, 1, .5f, .01f);
                    float left = position.x - 8;
                    float top = position.y + (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * .5f;
                    int first = math.max(0, (int)math.floor(center.x - ChunkProjectionBuilder.ProjectionClipRadius - left) - 1);
                    int end = math.min(16, (int)math.ceil(center.x + ChunkProjectionBuilder.ProjectionClipRadius - left) + 1);
                    if (end <= first) continue;
                    int firstRow = (int)math.floor((top - center.y - ChunkProjectionBuilder.ProjectionClipRadius) * 2) - 1;
                    int lastRow = (int)math.ceil((top - center.y + ChunkProjectionBuilder.ProjectionClipRadius) * 2) + 1;
                    solids.Clear(); water.Clear(); clips.Clear(); occupancy.Clear(); waterOccupancy.Clear();
                    using (GeometryMarker.Auto())
                        new LocalProjectionBuildJob
                        {
                            Blocks = blocks[entity], Accessor = accessor, Occupancy = occupancy, WaterOccupancy = waterOccupancy,
                            Solids = solids, Water = water, Clips = clips,
                            Coordinate = coordinate, View = view, Feet = feet,
                            FirstColumn = first, ColumnCount = end - first, FirstRow = firstRow, LastRow = lastRow
                        }.Run();
                    // Build has a conservative neighbour margin. Keep only whole tiles touching the region,
                    // before calculating their lighting or uploading them.
                    int kept = 0;
                    for (int i = 0; i < solids.Length; i++)
                    {
                        var face = solids[i];
                        if (TouchesRegion(face, left, top, center)) solids[kept++] = face;
                    }
                    solids.ResizeUninitialized(kept);
                    kept = 0;
                    for (int i = 0; i < water.Length; i++)
                    {
                        var face = water[i];
                        if (TouchesRegion(face.Value, left, top, center)) water[kept++] = face;
                    }
                    water.ResizeUninitialized(kept);
                    using (LightingMarker.Auto())
                        ChunkLightingSystem.UpdateChunkFully(solids, water, voxelLights[entity], coordinate, accessor, lightAccessor,
                            view, sun, ref directionCache);
                    renderer.AddOcclusionFaces(coordinate, solids, water);
                    foreach (var face in solids)
                        {
                            opaqueOutput.Add(Pack(face, position));
                            if (face.IsCutTop || face.IsCutSide) caps++;
                        }
                    foreach (var face in water)
                    {
                        waterOutput.Add(Pack(face.Value, position));
                    }
                }
            }
            finally { directionCache.Dispose(); }
            // Cover the full selected region, including empty boundary tiles.
            // Never substitute base-world geometry for missing local results.
            for (int y = 0; y < coverageSize.y; y++)
                for (int x = 0; x < coverageSize.x; x++)
                {
                    int2 key = coverageMin + new int2(x, y);
                    float2 lower = new float2(key.x, key.y * .5f);
                    if (ChunkProjectionBuilder.TileTouchesLocalProjection(lower + new float2(.5f, .25f), center))
                        coverage.Add(new float4(lower, 1, .5f));
                }
            // Include the complete build window and neighbouring emitted side/cap rows.
            var bounds = new Bounds(new Vector3(center.x, center.y, 0), new Vector3(20, 20, 1000));
            using (UploadMarker.Auto())
            {
                renderer.UploadLocalProjection(opaqueOutput.AsArray(), waterOutput.AsArray(), bounds, caps);
                renderer.UploadLocalCoverage(coverage.AsArray(), coverageMin, coverageSize);
                renderer.MarkReady();
            }
            lastRenderer = renderer; lastCell = cell; lastRegionStep = regionStep; lastView = view; lastGeometry = geometry; lastSun = sunHash; published = true;
        }

        private static ProjectedCellRenderData Pack(ProjectedCellData cell, float3 position) => new ProjectedCellRenderData
        {
            BlockData = ProjectedCellRenderData.PackBlockData(cell.BlockData, cell.TopFrontMaterial),
            Position = cell.Position, LightData = cell.LightData,
            FaceData = ProjectedCellRenderData.PackFaceData(cell.FaceData, cell.SourceAirIndex), ChunkPosition = position,
            LocalLightDirection = cell.LocalLightDirection, LocalLightBottomLeft = cell.LocalLightBottomLeft,
            LocalLightBottomRight = cell.LocalLightBottomRight, LocalLightTopLeft = cell.LocalLightTopLeft,
            LocalLightTopRight = cell.LocalLightTopRight
        };
        private static bool TouchesRegion(ProjectedCellData cell, float left, float top, float2 center) =>
            ChunkProjectionBuilder.TileTouchesLocalProjection(
                new float2(left + cell.ProjectionX + .5f, top - (cell.ProjectionY + .5f) * .5f), center);
        protected override void OnStopRunning()
        {
            published = false;
            if (lastRenderer != null) lastRenderer.Invalidate();
        }
        protected override void OnDestroy()
        {
            if (EntityManager.Exists(scratch)) EntityManager.DestroyEntity(scratch);
        }
    }

    // LateUpdate precedes ECS presentation in Unity's player loop. Submit both
    // layers only after publication so copied draw bounds/counts cannot refer to
    // an older state than the mutable coverage texture and GPU buffers.
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(LocalProjectionRenderSystem))]
    [UpdateAfter(typeof(PlayerSunShadowRenderSystem))]
    public partial class WorldProjectionDrawSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var terrain = ChunkProceduralRenderer.Instance;
            if (terrain == null || !terrain.isActiveAndEnabled) return;
            terrain.SubmitFrame();
            var local = LocalProjectionRenderer.Instance;
            if (local != null) local.SubmitFrame();
        }
    }
}
