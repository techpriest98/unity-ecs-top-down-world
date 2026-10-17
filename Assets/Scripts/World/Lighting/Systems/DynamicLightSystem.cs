using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkLightingSystem))]
    [UpdateAfter(typeof(BlockModificationSystem))]
    public partial struct DynamicLightSystem : ISystem
    {
        private const float MaximumRadius = 8f;

        private EntityQuery generatedChunksQuery;
        private EntityQuery lightSourcesQuery;

        private NativeParallelHashMap<Entity, DynamicLightSourceState> previousSources;
        private int lastChunkCount;
        private uint lastChunkSignature;
        private bool initialized;
        private uint lastLightingRevision;
        private NativeParallelHashSet<int2> previouslyLitChunks;

        public void OnCreate(ref SystemState state)
        {
            generatedChunksQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated, BlockData, VoxelLightData>()
                .Build(ref state);

            lightSourcesQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<DynamicLightSource, DynamicLightWorldPosition, DynamicLightSourceState>()
                .Build(ref state);

            EntityQuery revisionQuery =
                state.GetEntityQuery(
                    ComponentType.ReadOnly<DynamicLightingRevision>());

            if (revisionQuery.IsEmptyIgnoreFilter)
            {
                Entity revisionEntity =
                    state.EntityManager.CreateEntity(
                        typeof(DynamicLightingRevision));

                state.EntityManager.SetName(
                    revisionEntity,
                    "Dynamic Lighting Revision");
            }

            state.RequireForUpdate<DynamicLightingRevision>();

            previouslyLitChunks = new NativeParallelHashSet<int2>(16, Allocator.Persistent);
            previousSources = new NativeParallelHashMap<Entity, DynamicLightSourceState>(16, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (previousSources.IsCreated) previousSources.Dispose();
            if (previouslyLitChunks.IsCreated)
            {
                previouslyLitChunks.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int chunkCount = generatedChunksQuery.CalculateEntityCount();
            int sourceCount = lightSourcesQuery.CalculateEntityCount();
            var currentSources = new NativeParallelHashMap<Entity, DynamicLightSourceState>(math.max(sourceCount, 1), Allocator.Temp);
            var changedBounds = new NativeList<DynamicLightBounds>(Allocator.Temp);
            var chunkEntities = new NativeParallelHashMap<int2, Entity>(math.max(chunkCount, 1), Allocator.Temp);
            uint chunkSignature = 0;
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>()
                .WithAll<ChunkGenerated, BlockData, VoxelLightData>().WithEntityAccess())
            {
                int2 coordinate = chunk.ValueRO.Coordinate;
                chunkEntities.TryAdd(coordinate, entity);
                chunkSignature ^= math.hash(new int4(coordinate.x, coordinate.y, entity.Index, entity.Version));
            }

            uint lightingRevision = SystemAPI.GetSingleton<DynamicLightingRevision>().Value;
            bool fullRebuild = !initialized || lightingRevision != lastLightingRevision ||
                chunkCount != lastChunkCount || chunkSignature != lastChunkSignature;
            foreach (var (source, position, sourceState, entity) in SystemAPI.Query<
                RefRO<DynamicLightSource>, RefRO<DynamicLightWorldPosition>, RefRW<DynamicLightSourceState>>().WithEntityAccess())
            {
                var next = new DynamicLightSourceState
                {
                    Cell = (int3)math.floor(position.ValueRO.Value), Color = source.ValueRO.Color,
                    Radius = source.ValueRO.Radius, Intensity = source.ValueRO.Intensity, Initialized = true
                };
                currentSources.TryAdd(entity, next);
                bool hadOld = previousSources.TryGetValue(entity, out var old);
                bool changed = !hadOld || !sourceState.ValueRO.Initialized || math.any(old.Cell != next.Cell) ||
                    math.any(old.Color != next.Color) || old.Radius != next.Radius || old.Intensity != next.Intensity;
                if (changed)
                {
                    if (hadOld) changedBounds.Add(DynamicLightBounds.Around(old.Cell, old.Radius));
                    changedBounds.Add(DynamicLightBounds.Around(next.Cell, next.Radius));
                }
                sourceState.ValueRW = next;
            }
            // Entity identity retains old bounds even after removal/disable.
            foreach (var old in previousSources)
                if (!currentSources.ContainsKey(old.Key))
                    changedBounds.Add(DynamicLightBounds.Around(old.Value.Cell, old.Value.Radius));

            if (!fullRebuild && changedBounds.Length == 0)
            {
                currentSources.Dispose(); changedBounds.Dispose(); chunkEntities.Dispose();
                return;
            }
            lastChunkCount = chunkCount; lastChunkSignature = chunkSignature;
            lastLightingRevision = lightingRevision; initialized = true;

            var dirtyChunks = new NativeParallelHashSet<Entity>(
                math.max(chunkCount, 1),
                Allocator.Temp);

            BufferLookup<BlockData> blockLookup =
                SystemAPI.GetBufferLookup<BlockData>(true);

            BufferLookup<VoxelLightData> lightLookup =
                SystemAPI.GetBufferLookup<VoxelLightData>();

            var currentlyLitChunks = new NativeParallelHashSet<int2>(
                math.max(chunkCount, 1),
                Allocator.Temp);

            ChunkBlockAccessor blockAccessor = new(chunkEntities, blockLookup, SystemAPI.GetBufferLookup<SplitCellData>(true));
            ChunkVoxelLightAccessor lightAccessor = new(chunkEntities, lightLookup);
            var originalLocalLight = new NativeParallelHashMap<int3, uint>(fullRebuild ? 1 : 1024, Allocator.Temp);
            if (fullRebuild) ClearLocalLight(chunkEntities, lightLookup, dirtyChunks);
            else ClearRegions(changedBounds, lightAccessor, ref originalLocalLight);

            foreach (var (source, position) in
                     SystemAPI.Query<
                         RefRO<DynamicLightSource>,
                         RefRO<DynamicLightWorldPosition>>().WithAll<DynamicLightSourceState>())
            {
                SpreadSource(
                    source.ValueRO,
                    position.ValueRO.Value,
                    blockAccessor,
                    lightAccessor,
                    dirtyChunks,
                    currentlyLitChunks, !fullRebuild, changedBounds, originalLocalLight);
            }

            if (fullRebuild) StoreLitChunks(currentlyLitChunks);
            else
            {
                foreach (int2 coordinate in currentlyLitChunks)
                {
                    if (previouslyLitChunks.Count() == previouslyLitChunks.Capacity)
                        previouslyLitChunks.Capacity *= 2;
                    previouslyLitChunks.Add(coordinate);
                }
                // Clearing/reapplying an unchanged neighbour must not dirty
                // its projected lighting or trigger a renderer upload.
                dirtyChunks.Clear();
                foreach (var original in originalLocalLight)
                {
                    int3 p = original.Key;
                    if (!lightAccessor.TryGet(int2.zero, p.x, p.y, p.z, out var light) || PackRgb(light) == original.Value) continue;
                    int2 coordinate = new int2(FloorDiv(p.x, ChunkSettings.SizeX), FloorDiv(p.z, ChunkSettings.SizeZ));
                    if (chunkEntities.TryGetValue(coordinate, out var entity)) dirtyChunks.Add(entity);
                }
            }
            previousSources.Clear();
            if (previousSources.Capacity < sourceCount) previousSources.Capacity = sourceCount;
            foreach (var source in currentSources) previousSources.TryAdd(source.Key, source.Value);

            foreach (Entity entity in dirtyChunks)
            {
                state.EntityManager.SetComponentEnabled<ChunkNeedsLocalLightUpdate>(entity, true);
            }

            currentlyLitChunks.Dispose();
            originalLocalLight.Dispose();
            currentSources.Dispose();
            changedBounds.Dispose();
            dirtyChunks.Dispose();
            chunkEntities.Dispose();
        }

        private static uint PackRgb(VoxelLightData light) =>
            (uint)light.R | ((uint)light.G << 8) | ((uint)light.B << 16);

        private static void ClearRegions(NativeList<DynamicLightBounds> bounds, ChunkVoxelLightAccessor lights,
            ref NativeParallelHashMap<int3, uint> original)
        {
            // The hash map is the exact union of the boxes: disjoint changes
            // never force a large empty bounding rectangle to be recalculated.
            for (int i = 0; i < bounds.Length; i++)
            {
                var box = bounds[i];
                for (int y = math.max(0, box.Min.y); y <= math.min(ChunkSettings.SizeY - 1, box.Max.y); y++)
                for (int z = box.Min.z; z <= box.Max.z; z++)
                for (int x = box.Min.x; x <= box.Max.x; x++)
                {
                    int3 p = new int3(x, y, z);
                    if (original.ContainsKey(p) || !lights.TryGet(int2.zero, x, y, z, out var light)) continue;
                    if (original.Count() == original.Capacity) original.Capacity *= 2;
                    uint rgb = PackRgb(light);
                    original.TryAdd(p, rgb);
                    if (rgb == 0) continue;
                    light.R = light.G = light.B = 0;
                    lights.TrySet(int2.zero, x, y, z, light, out _, out _);
                }
            }
        }

        private void ClearLocalLight(
            NativeParallelHashMap<int2, Entity> chunkEntities,
            BufferLookup<VoxelLightData> lightLookup,
            NativeParallelHashSet<Entity> dirtyChunks)
        {
            foreach (int2 coordinate in previouslyLitChunks)
            {
                if (!chunkEntities.TryGetValue(coordinate, out Entity entity) ||
                    !lightLookup.HasBuffer(entity))
                {
                    continue;
                }

                DynamicBuffer<VoxelLightData> buffer = lightLookup[entity];

                bool changed = false;

                for (int index = 0; index < buffer.Length; index++)
                {
                    VoxelLightData light =
                        buffer[index];

                    if (light.R == 0 &&
                        light.G == 0 &&
                        light.B == 0)
                    {
                        continue;
                    }

                    light.R = 0;
                    light.G = 0;
                    light.B = 0;

                    buffer[index] =
                        light;

                    changed = true;
                }

                if (changed)
                {
                    dirtyChunks.Add(entity);
                }
            }
        }

        private void StoreLitChunks(NativeParallelHashSet<int2> currentlyLitChunks)
        {
            if (previouslyLitChunks.Capacity < currentlyLitChunks.Count())
            {
                previouslyLitChunks.Capacity = currentlyLitChunks.Count();
            }

            previouslyLitChunks.Clear();

            foreach (int2 coordinate in currentlyLitChunks)
            {
                previouslyLitChunks.Add(coordinate);
            }
        }

        private static void SpreadSource(
            DynamicLightSource source,
            float3 worldPosition,
            ChunkBlockAccessor blockAccessor,
            ChunkVoxelLightAccessor lightAccessor,
            NativeParallelHashSet<Entity> dirtyChunks,
            NativeParallelHashSet<int2> litChunks, bool restricted,
            NativeList<DynamicLightBounds> changedBounds, NativeParallelHashMap<int3, uint> region)
        {
            float radius = math.clamp(source.Radius, 0f, MaximumRadius);
            float3 sourceLight = math.saturate(source.Color * source.Intensity);

            if (radius <= 0f || math.cmax(sourceLight) <= 0f)
            {
                return;
            }

            int3 sourceCell = (int3)math.floor(worldPosition);
            if (restricted)
            {
                var bounds = DynamicLightBounds.Around(sourceCell, radius);
                bool intersects = false;
                for (int i = 0; i < changedBounds.Length; i++)
                    if (bounds.Intersects(changedBounds[i])) { intersects = true; break; }
                if (!intersects) return;
            }
            int2 sourceChunk = new int2(
                FloorDiv(sourceCell.x, ChunkSettings.SizeX),
                FloorDiv(sourceCell.z, ChunkSettings.SizeZ));

            int3 sourceLocal = new int3(
                sourceCell.x - sourceChunk.x * ChunkSettings.SizeX,
                sourceCell.y,
                sourceCell.z - sourceChunk.y * ChunkSettings.SizeZ);

            int cellRadius = (int)math.ceil(radius);

            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                for (int z = -cellRadius; z <= cellRadius; z++)
                {
                    for (int x = -cellRadius; x <= cellRadius; x++)
                    {
                        int3 offset = new int3(x, y, z);
                        if (restricted && !region.ContainsKey(sourceCell + offset)) continue;
                        float distance = math.length(offset);

                        if (distance > radius)
                        {
                            continue;
                        }

                        IlluminateCell(
                            sourceLocal + offset,
                            sourceLocal,
                            sourceChunk,
                            distance,
                            radius,
                            sourceLight,
                            blockAccessor,
                            lightAccessor,
                            dirtyChunks,
                            litChunks);
                    }
                }
            }
        }

        private static void IlluminateCell(
            int3 position,
            int3 sourcePosition,
            int2 sourceChunk,
            float distance,
            float radius,
            float3 sourceLight,
            ChunkBlockAccessor blockAccessor,
            ChunkVoxelLightAccessor lightAccessor,
            NativeParallelHashSet<Entity> dirtyChunks,
            NativeParallelHashSet<int2> litChunks)
        {
            bool isSource = math.all(position == sourcePosition);

            if (!blockAccessor.TryGetBlock(
                    sourceChunk,
                    position.x,
                    position.y,
                    position.z,
                    out BlockData block) ||
                (!isSource && BlockLightGeometry.IsFull(block)) ||
                !DynamicLightVisibilityUtility.IsVisible(
                    sourceChunk,
                    sourcePosition,
                    position,
                    blockAccessor))
            {
                return;
            }

            if (!lightAccessor.TryGet(
                    sourceChunk,
                    position.x,
                    position.y,
                    position.z,
                    out VoxelLightData light))
            {
                return;
            }

            float attenuation = 1f - distance / radius;
            byte r = ToByte(sourceLight.x * attenuation);
            byte g = ToByte(sourceLight.y * attenuation);
            byte b = ToByte(sourceLight.z * attenuation);

            byte nextR = (byte)math.max((int)light.R, (int)r);
            byte nextG = (byte)math.max((int)light.G, (int)g);
            byte nextB = (byte)math.max((int)light.B, (int)b);

            if (nextR != light.R || nextG != light.G || nextB != light.B)
            {
                light.R = nextR;
                light.G = nextG;
                light.B = nextB;

                if (lightAccessor.TrySet(
                        sourceChunk,
                        position.x,
                        position.y,
                        position.z,
                        light,
                        out Entity changedChunk,
                        out int2 changedChunkCoordinate))
                {
                    dirtyChunks.Add(changedChunk);
                    litChunks.Add(changedChunkCoordinate);
                }
            }
        }

        private static bool IsOpaque(BlockId blockId)
        {
            return BlockUtility.IsSolid(blockId) && blockId != BlockId.OceanWater;
        }

        private static byte ToByte(float value)
        {
            return (byte)math.round(math.saturate(value) * byte.MaxValue);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;

            if (value % divisor != 0 && value < 0)
            {
                quotient--;
            }

            return quotient;
        }
    }
}
