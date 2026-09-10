using Game.World.Chunks;
using Game.World.Lighting;
using Game.World.Rendering;
using Game.World.Saving;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Blocks
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkProjectionSystem))]
    public partial struct BlockModificationSystem : ISystem
    {
        private EntityQuery generatedChunksQuery;

        public void OnCreate(ref SystemState state)
        {
            EntityQuery queueQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<BlockModificationQueue>(),
                ComponentType.ReadWrite<BlockModificationRequest>());

            if (queueQuery.IsEmptyIgnoreFilter)
            {
                Entity entity = state.EntityManager.CreateEntity(
                    typeof(BlockModificationQueue));

                state.EntityManager.AddBuffer<BlockModificationRequest>(
                    entity);

                state.EntityManager.SetName(
                    entity,
                    "Block Modification Queue");
            }

            generatedChunksQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated, BlockData>()
                .Build(ref state);

            state.RequireForUpdate<BlockModificationQueue>();
            state.RequireForUpdate<DynamicLightingRevision>();
            state.RequireForUpdate<WorldBlockChanges>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RefRW<WorldBlockChanges> changes = SystemAPI.GetSingletonRW<WorldBlockChanges>();
            if (!changes.ValueRO.IsReady)
                return;

            Entity queueEntity =
                SystemAPI.GetSingletonEntity<BlockModificationQueue>();

            DynamicBuffer<BlockModificationRequest> requests =
                state.EntityManager.GetBuffer<BlockModificationRequest>(
                    queueEntity);

            if (requests.Length == 0)
            {
                return;
            }

            int generatedChunkCount =
                generatedChunksQuery.CalculateEntityCount();

            var chunkEntities = new NativeParallelHashMap<int2, Entity>(
                math.max(generatedChunkCount, 1),
                Allocator.Temp);

            foreach (var (chunk, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>>()
                         .WithAll<ChunkGenerated>()
                         .WithEntityAccess())
            {
                chunkEntities.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }

            var dirtyProjectionChunks =
                new NativeParallelHashSet<Entity>(
                    math.max(generatedChunkCount, 1),
                    Allocator.Temp);

            BufferLookup<BlockData> blockLookup =
                SystemAPI.GetBufferLookup<BlockData>();

            bool worldChanged = false;

            for (int i = 0; i < requests.Length; i++)
            {
                BlockModificationRequest request = requests[i];

                if (request.WorldPosition.y < 0 ||
                    request.WorldPosition.y >= ChunkSettings.SizeY)
                {
                    continue;
                }

                int2 chunkCoordinate = new int2(
                    FloorDiv(
                        request.WorldPosition.x,
                        ChunkSettings.SizeX),
                    FloorDiv(
                        request.WorldPosition.z,
                        ChunkSettings.SizeZ));

                if (!chunkEntities.TryGetValue(
                        chunkCoordinate,
                        out Entity chunkEntity) ||
                    !blockLookup.HasBuffer(chunkEntity))
                {
                    continue;
                }

                int localX =
                    request.WorldPosition.x -
                    chunkCoordinate.x *
                    ChunkSettings.SizeX;

                int localZ =
                    request.WorldPosition.z -
                    chunkCoordinate.y *
                    ChunkSettings.SizeZ;

                int blockIndex = ChunkUtility.ToIndex(
                    localX,
                    request.WorldPosition.y,
                    localZ);

                DynamicBuffer<BlockData> blocks =
                    blockLookup[chunkEntity];

                BlockData previousBlock = blocks[blockIndex];

                if (previousBlock.BlockId == request.Value.BlockId &&
                    previousBlock.Durability == request.Value.Durability)
                {
                    continue;
                }

                changes.ValueRW.Record(chunkCoordinate, blockIndex, request.Value);
                blocks[blockIndex] = request.Value;
                worldChanged = true;

                AddDirtyChunk(
                    chunkCoordinate,
                    chunkEntities,
                    dirtyProjectionChunks);

                if (localX == 0)
                {
                    AddDirtyChunk(
                        chunkCoordinate + new int2(-1, 0),
                        chunkEntities,
                        dirtyProjectionChunks);
                }
                else if (localX == ChunkSettings.SizeX - 1)
                {
                    AddDirtyChunk(
                        chunkCoordinate + new int2(1, 0),
                        chunkEntities,
                        dirtyProjectionChunks);
                }

                if (localZ == 0)
                {
                    AddDirtyChunk(
                        chunkCoordinate + new int2(0, -1),
                        chunkEntities,
                        dirtyProjectionChunks);
                }
                else if (localZ == ChunkSettings.SizeZ - 1)
                {
                    AddDirtyChunk(
                        chunkCoordinate + new int2(0, 1),
                        chunkEntities,
                        dirtyProjectionChunks);
                }
            }

            requests.Clear();

            if (worldChanged)
            {
                RefRW<DynamicLightingRevision> lightingRevision =
                    SystemAPI.GetSingletonRW<DynamicLightingRevision>();

                lightingRevision.ValueRW.Value++;

                EntityCommandBuffer ecb =
                    new EntityCommandBuffer(Allocator.Temp);

                foreach (Entity entity in dirtyProjectionChunks)
                {
                    ecb.SetComponentEnabled<ChunkNeedsSkyLight>(entity, true);
                    ecb.SetComponentEnabled<ChunkNeedsProjection>(entity, true);
                }

                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }

            dirtyProjectionChunks.Dispose();
            chunkEntities.Dispose();
        }

        private static void AddDirtyChunk(
            int2 chunkCoordinate,
            NativeParallelHashMap<int2, Entity> chunkEntities,
            NativeParallelHashSet<Entity> dirtyChunks)
        {
            if (chunkEntities.TryGetValue(
                    chunkCoordinate,
                    out Entity entity))
            {
                dirtyChunks.Add(entity);
            }
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;

            if (remainder != 0 &&
                value < 0)
            {
                quotient--;
            }

            return quotient;
        }
    }
}
