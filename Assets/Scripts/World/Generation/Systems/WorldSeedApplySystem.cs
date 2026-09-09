using Unity.Entities;

namespace Game.World.Generation
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class WorldSeedApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<WorldSeedComponent>();
        }

        protected override void OnUpdate()
        {
            if (!WorldLaunchRequest.Pending) return;

            RefRW<WorldSeedComponent> seed = SystemAPI.GetSingletonRW<WorldSeedComponent>();
            seed.ValueRW.Value = WorldLaunchRequest.Seed;
            WorldLaunchRequest.MarkApplied();
        }
    }
}
