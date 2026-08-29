using Game.World.Time;
using Unity.Burst;
using Unity.Entities;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldTimeSystem))]
    public partial struct DirectionalLightSystem : ISystem
    {
        private int lastHour;
        private bool initialized;

        public void OnCreate(ref SystemState state)
        {
            EntityQuery lightQuery = state.GetEntityQuery(ComponentType.ReadOnly<DirectionalLightData>());

            if (lightQuery.IsEmptyIgnoreFilter)
            {
                Entity entity = state.EntityManager.CreateEntity(typeof(DirectionalLightData));
                state.EntityManager.SetName(entity, "Directional Light");
            }

            state.RequireForUpdate<WorldTime>();
            state.RequireForUpdate<DirectionalLightData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int hour = SystemAPI.GetSingleton<WorldTime>().Hour;

            if (initialized && hour == lastHour)
            {
                return;
            }

            DirectionalLightData light = DirectionalLightUtility.Sample(hour);
            SystemAPI.SetSingleton(light);

            lastHour = hour;
            initialized = true;
        }
    }
}