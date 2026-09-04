using Game.World.Lighting;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateBefore(typeof(DynamicLightSystem))]
    public partial struct PlayerDynamicLightPositionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (playerPosition, lightPosition) in
                     SystemAPI.Query<
                             RefRO<PlayerWorldPosition>,
                             RefRW<DynamicLightWorldPosition>>()
                         .WithAll<PlayerTag, DynamicLightSource>())
            {
                lightPosition.ValueRW.Value =
                    playerPosition.ValueRO.Value +
                    new float3(0f, 1f, 0f);
            }
        }
    }
}