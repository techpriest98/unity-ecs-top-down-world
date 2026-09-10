using Game.Player;
using Game.World.Chunks;
using Game.World.Generation.Biomes;
using Game.World.Generation.Biomes.RockyShore;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Generation.Spawning
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkGenerationSystem))]
    [UpdateBefore(typeof(PlayerSpawnSystem))]
    public partial struct WorldSpawnSearchSystem : ISystem
    {
        private NativeParallelHashSet<Entity> searchedChunks;
        private Entity spawnPointEntity;
        private bool hasResolvedSpawnPoint;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkGenerated>();
            state.RequireForUpdate<WorldStartupState>();

            searchedChunks = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);

            spawnPointEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(spawnPointEntity, new WorldSpawnPointComponent { Position = default });
            state.EntityManager.SetComponentEnabled<WorldSpawnPointComponent>(spawnPointEntity, false);

            hasResolvedSpawnPoint = false;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (searchedChunks.IsCreated)
                searchedChunks.Dispose();

            if (state.EntityManager.Exists(spawnPointEntity))
                state.EntityManager.DestroyEntity(spawnPointEntity);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<WorldStartupState>().Phase != WorldStartupPhase.SearchingSpawn)
                return;

            if (hasResolvedSpawnPoint)
                return;

            int2 searchOrigin = new int2(
                ChunkSettings.SizeX / 2,
                ChunkSettings.SizeZ / 2);

            foreach (var (chunk, columns, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<ChunkColumnData>>()
                         .WithAll<ChunkGenerated>()
                         .WithEntityAccess())
            {
                if (searchedChunks.Contains(entity))
                    continue;

                if (searchedChunks.Count() >= searchedChunks.Capacity)
                    searchedChunks.Capacity *= 2;

                searchedChunks.Add(entity);

                if (!TryFindSpawn(
                        chunk.ValueRO.Coordinate,
                        columns,
                        searchOrigin,
                        out int3 position))
                {
                    continue;
                }

                state.EntityManager.SetComponentData(spawnPointEntity, new WorldSpawnPointComponent {
                    Position = position 
                });

                state.EntityManager.SetComponentEnabled<WorldSpawnPointComponent>(spawnPointEntity, true);

                hasResolvedSpawnPoint = true;
                return;
            }
        }

        private static bool TryFindSpawn(
            int2 coordinate,
            DynamicBuffer<ChunkColumnData> columns,
            int2 searchOrigin,
            out int3 position)
        {
            position = default;
            bool found = false;
            long bestDistanceSquared = long.MaxValue;

            for (int index = 0; index < columns.Length; index++)
            {
                ChunkColumnData column = columns[index];

                if (column.Biome != WorldBiome.RockyShore ||
                    column.Zone != (byte)RockyShoreZone.Beach)
                {
                    continue;
                }

                int worldX = coordinate.x * ChunkSettings.SizeX + index % ChunkSettings.SizeX;
                int worldZ = coordinate.y * ChunkSettings.SizeZ + index / ChunkSettings.SizeX;

                long offsetX = (long)worldX - searchOrigin.x;
                long offsetZ = (long)worldZ - searchOrigin.y;
                long distanceSquared = offsetX * offsetX + offsetZ * offsetZ;

                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                position = new int3(worldX, column.SurfaceHeight, worldZ);
                found = true;
            }

            return found;
        }
    }
}
