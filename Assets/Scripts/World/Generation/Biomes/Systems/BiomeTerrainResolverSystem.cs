using Game.World.Generation.Biomes.Ocean;
using Game.World.Generation.Biomes.RockyShore;
using Unity.Entities;

namespace Game.World.Generation.Biomes
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct BiomeTerrainResolverSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OceanSettingsComponent>();
            state.RequireForUpdate<RockyShoreSettingsComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            OceanSettingsComponent oceanSettings =
                SystemAPI.GetSingleton<OceanSettingsComponent>();

            RockyShoreSettingsComponent rockyShoreSettings =
                SystemAPI.GetSingleton<
                    RockyShoreSettingsComponent>();

            Entity entity =
                state.EntityManager.CreateEntity();

            state.EntityManager.AddComponentData(
                entity,
                new BiomeTerrainResolver(
                    oceanSettings,
                    rockyShoreSettings));

            state.Enabled = false;
        }
    }
}