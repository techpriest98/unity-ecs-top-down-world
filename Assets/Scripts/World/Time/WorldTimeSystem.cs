using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Time
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WorldTimeSystem : ISystem
    {
        private const float SecondsPerHour = 60f;

        public void OnCreate(ref SystemState state)
        {
            EntityQuery worldTimeQuery = state.GetEntityQuery(ComponentType.ReadOnly<WorldTime>());

            if (worldTimeQuery.IsEmptyIgnoreFilter)
            {
                Entity entity = state.EntityManager.CreateEntity(typeof(WorldTime));

                state.EntityManager.SetComponentData(entity,
                    new WorldTime
                    {
                        Hour = 12,
                        UpdateTimer = 0f
                    });

                state.EntityManager.SetName(entity, "World Time");
            }

            state.RequireForUpdate<WorldTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RefRW<WorldTime> worldTime = SystemAPI.GetSingletonRW<WorldTime>();

            worldTime.ValueRW.UpdateTimer += SystemAPI.Time.DeltaTime;

            if (worldTime.ValueRO.UpdateTimer < SecondsPerHour)
            {
                return;
            }

            int elapsedHours = (int)math.floor(worldTime.ValueRO.UpdateTimer / SecondsPerHour);

            worldTime.ValueRW.UpdateTimer -= elapsedHours * SecondsPerHour;
            worldTime.ValueRW.Hour = (worldTime.ValueRO.Hour + elapsedHours) % 24;
        }
    }
}