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
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity spawnPointEntity = Entity.Null;

            int3 spawnPosition = default;

            foreach (var (
                spawnPoint,
                entity)
                in SystemAPI
                    .Query<
                        RefRO<
                            WorldSpawnPointComponent>>()
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

            foreach (RefRW<PlayerWorldPosition>
                playerPosition
                in SystemAPI
                    .Query<
                        RefRW<
                            PlayerWorldPosition>>()
                    .WithAll<PlayerTag>())
            {
                playerPosition.ValueRW.Value = (float3)spawnPosition;
                playerFound = true;

                break;
            }

            if (!playerFound)
                return;

            state.EntityManager.SetComponentEnabled<
                WorldSpawnPointComponent>(
                spawnPointEntity,
                false);
        }
    }
}