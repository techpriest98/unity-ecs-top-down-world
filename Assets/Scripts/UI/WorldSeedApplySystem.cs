using Game.World.Chunks;
using Unity.Entities;

namespace Game.World.Generation
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(ChunkGenerationSystem))]
    public partial struct WorldSeedApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldSeedComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!WorldLaunchRequest.Pending)
                return;

            SystemAPI.SetSingleton(new WorldSeedComponent
            {
                Value = WorldLaunchRequest.Seed
            });
            WorldLaunchRequest.MarkApplied();
        }
    }
}
