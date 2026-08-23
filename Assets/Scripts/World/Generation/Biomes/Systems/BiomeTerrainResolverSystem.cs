using Game.World.Generation.Biomes.Ocean;
using Unity.Entities;

namespace Game.World.Generation.Biomes
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct BiomeTerrainResolverSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OceanSettingsComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            OceanSettingsComponent oceanSettings =
                SystemAPI.GetSingleton<OceanSettingsComponent>();

            Entity entity = state.EntityManager.CreateEntity();

            state.EntityManager.AddComponentData(
                entity,
                new BiomeTerrainResolver(oceanSettings));

            state.Enabled = false;
        }
    }
}