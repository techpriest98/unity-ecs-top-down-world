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

        private int lastSourceCount;
        private int lastChunkCount;
        private uint lastChunkSignature;
        private bool initialized;
        private uint lastLightingRevision;

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
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int chunkCount = generatedChunksQuery.CalculateEntityCount();
            int sourceCount = lightSourcesQuery.CalculateEntityCount();

            var chunkEntities = new NativeParallelHashMap<int2, Entity>(
                math.max(chunkCount, 1),
                Allocator.Temp);

            uint chunkSignature = 0;

            foreach (var (chunk, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>>()
                         .WithAll<ChunkGenerated, BlockData, VoxelLightData>()
                         .WithEntityAccess())
            {
                int2 coordinate = chunk.ValueRO.Coordinate;
                chunkEntities.TryAdd(coordinate, entity);
                chunkSignature ^= math.hash(coordinate);
            }

            uint lightingRevision =
                SystemAPI.GetSingleton<DynamicLightingRevision>().Value;

            bool needsRebuild =
                !initialized ||
                lightingRevision != lastLightingRevision ||
                sourceCount != lastSourceCount ||
                chunkCount != lastChunkCount ||
                chunkSignature != lastChunkSignature;

            foreach (var (source, position, sourceState) in
                     SystemAPI.Query<
                         RefRO<DynamicLightSource>,
                         RefRO<DynamicLightWorldPosition>,
                         RefRW<DynamicLightSourceState>>())
            {
                int3 cell = (int3)math.floor(position.ValueRO.Value);

                bool sourceChanged =
                    !sourceState.ValueRO.Initialized ||
                    math.any(cell != sourceState.ValueRO.Cell) ||
                    math.any(source.ValueRO.Color != sourceState.ValueRO.Color) ||
                    source.ValueRO.Radius != sourceState.ValueRO.Radius ||
                    source.ValueRO.Intensity != sourceState.ValueRO.Intensity;

                if (!sourceChanged)
                {
                    continue;
                }

                sourceState.ValueRW.Cell = cell;
                sourceState.ValueRW.Color = source.ValueRO.Color;
                sourceState.ValueRW.Radius = source.ValueRO.Radius;
                sourceState.ValueRW.Intensity = source.ValueRO.Intensity;
                sourceState.ValueRW.Initialized = true;
                needsRebuild = true;
            }

            if (!needsRebuild)
            {
                chunkEntities.Dispose();
                return;
            }

            lastSourceCount = sourceCount;
            lastChunkCount = chunkCount;
            lastChunkSignature = chunkSignature;
            lastLightingRevision = lightingRevision;
            initialized = true;

            var dirtyChunks = new NativeParallelHashSet<Entity>(
                math.max(chunkCount, 1),
                Allocator.Temp);

            ClearLocalLight(ref state, dirtyChunks);

            BufferLookup<BlockData> blockLookup =
                SystemAPI.GetBufferLookup<BlockData>(true);

            BufferLookup<VoxelLightData> lightLookup =
                SystemAPI.GetBufferLookup<VoxelLightData>();

            ChunkBlockAccessor blockAccessor = new(chunkEntities, blockLookup);
            ChunkVoxelLightAccessor lightAccessor = new(chunkEntities, lightLookup);

            foreach (var (source, position) in
                     SystemAPI.Query<
                         RefRO<DynamicLightSource>,
                         RefRO<DynamicLightWorldPosition>>())
            {
                SpreadSource(
                    source.ValueRO,
                    position.ValueRO.Value,
                    blockAccessor,
                    lightAccessor,
                    dirtyChunks);
            }

            foreach (Entity entity in dirtyChunks)
            {
                state.EntityManager.SetComponentEnabled<ChunkNeedsLocalLightUpdate>(entity, true);
            }

            dirtyChunks.Dispose();
            chunkEntities.Dispose();
        }

        private void ClearLocalLight(
            ref SystemState state,
            NativeParallelHashSet<Entity> dirtyChunks)
        {
            foreach (var (voxelLight, entity) in
                    SystemAPI.Query<DynamicBuffer<VoxelLightData>>()
                        .WithAll<ChunkGenerated>()
                        .WithEntityAccess())
            {
                DynamicBuffer<VoxelLightData> buffer =
                    voxelLight;

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

        private static void SpreadSource(
            DynamicLightSource source,
            float3 worldPosition,
            ChunkBlockAccessor blockAccessor,
            ChunkVoxelLightAccessor lightAccessor,
            NativeParallelHashSet<Entity> dirtyChunks)
        {
            float radius = math.clamp(source.Radius, 0f, MaximumRadius);
            float3 sourceLight = math.saturate(source.Color * source.Intensity);

            if (radius <= 0f || math.cmax(sourceLight) <= 0f)
            {
                return;
            }

            int3 sourceCell = (int3)math.floor(worldPosition);
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
                            dirtyChunks);
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
            NativeParallelHashSet<Entity> dirtyChunks)
        {
            bool isSource = math.all(position == sourcePosition);

            if (!blockAccessor.TryGetBlock(
                    sourceChunk,
                    position.x,
                    position.y,
                    position.z,
                    out BlockData block) ||
                (!isSource && IsOpaque(block.BlockId)) ||
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
                        out Entity changedChunk))
                {
                    dirtyChunks.Add(changedChunk);
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
