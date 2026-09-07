using Game.World.Chunks;
using Game.World.Generation.Spawning;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkGenerationSystem))]
    [UpdateBefore(typeof(PlayerMovementSystem))]
    public partial struct PlayerSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<PlayerWorldPosition>();
            state.RequireForUpdate<WorldSpawnPointComponent>();

            Entity startupEntity = state.EntityManager.CreateEntity(typeof(WorldStartupState));

            state.EntityManager.SetComponentData(
                startupEntity,
                new WorldStartupState
                {
                    Phase = WorldStartupPhase.SearchingSpawn
                });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity spawnPointEntity = Entity.Null;

            int3 spawnPosition = default;

            foreach (var (spawnPoint, entity) in SystemAPI
                .Query<RefRO<WorldSpawnPointComponent>>()
                .WithEntityAccess())
            {
                spawnPointEntity = entity;
                spawnPosition = spawnPoint.ValueRO.Position;

                break;
            }

            if (spawnPointEntity == Entity.Null)
            {
                return;
            }

            bool playerFound = false;

            foreach (var (playerPosition, verticalVelocity) in SystemAPI
                .Query<RefRW<PlayerWorldPosition>, RefRW<PlayerVerticalVelocity>>()
                .WithAll<PlayerTag>())
            {
                playerPosition.ValueRW.Value = new float3(
                    spawnPosition.x + 0.5f,
                    spawnPosition.y,
                    spawnPosition.z + 0.5f);

                verticalVelocity.ValueRW.Value = 0f;

                playerFound = true;
                break;
            }

            if (!playerFound)
                return;

            state.EntityManager.SetComponentEnabled<WorldSpawnPointComponent>(spawnPointEntity, false);

            RefRW<WorldStartupState> startup = SystemAPI.GetSingletonRW<WorldStartupState>();
            startup.ValueRW.Phase = WorldStartupPhase.PreparingView;
        }
    }
}