using Game.World.Generation.Spawning;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(Game.Player.PlayerStreamingCenterSystem))]
    [UpdateBefore(typeof(ChunkStreamingSystem))]
    public partial struct ChunkStreamingRadiusSystem : ISystem
    {
        private const int PreloadMargin = 1;

        private int searchRadius;
        private bool searchStarted;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkStreamingSettings>();
            state.RequireForUpdate<ChunkStreamingCenter>();
            state.RequireForUpdate<WorldStartupState>();

            state.EntityManager.CreateEntity(
                typeof(ChunkStreamingRuntime));
        }

        public void OnUpdate(ref SystemState state)
        {
            int configuredRadius = math.max(
                SystemAPI.GetSingleton<ChunkStreamingSettings>().LoadRadius,
                0);

            WorldStartupState startup = SystemAPI.GetSingleton<WorldStartupState>();

            if (startup.Phase != WorldStartupPhase.SearchingSpawn)
            {
                searchStarted = false;

                SystemAPI.GetSingletonRW<ChunkStreamingRuntime>()
                    .ValueRW.LoadRadius = configuredRadius;

                return;
            }

            if (!searchStarted)
            {
                searchRadius = configuredRadius;
                searchStarted = true;
            }

            searchRadius = math.max(searchRadius, configuredRadius);

            int2 center = SystemAPI.GetSingleton<ChunkStreamingCenter>().Coordinate;

            int generatedCount = SystemAPI.QueryBuilder()
                .WithAll<ChunkComponent, ChunkGenerated>()
                .Build()
                .CalculateEntityCount();

            using var generatedCoordinates =
                new NativeParallelHashSet<int2>(
                    math.max(generatedCount, 1),
                    Allocator.Temp);

            foreach (RefRO<ChunkComponent> chunk in SystemAPI
                .Query<RefRO<ChunkComponent>>()
                .WithAll<ChunkGenerated>())
            {
                generatedCoordinates.Add(chunk.ValueRO.Coordinate);
            }

            int checkRadius = searchRadius + PreloadMargin;
            bool areaGenerated = true;

            for (int z = -checkRadius; z <= checkRadius; z++)
            {
                for (int x = -checkRadius; x <= checkRadius; x++)
                {
                    if (!generatedCoordinates.Contains(
                            center + new int2(x, z)))
                    {
                        areaGenerated = false;
                        break;
                    }
                }

                if (!areaGenerated)
                {
                    break;
                }
            }

            if (areaGenerated)
            {
                searchRadius++;
            }

            SystemAPI.GetSingletonRW<ChunkStreamingRuntime>().ValueRW.LoadRadius = searchRadius;
        }
    }
}