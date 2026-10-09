using Game.World.Lighting;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldItemMotionSystem))]
    [UpdateAfter(typeof(WorldItemDurabilitySystem))]
    [UpdateBefore(typeof(DynamicLightSystem))]
    public partial struct WorldItemLightSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
            state.RequireForUpdate<WorldItem>();
            state.RequireForUpdate<DynamicLightingRevision>();
        }
        public void OnUpdate(ref SystemState state)
        {
            var library = SystemAPI.GetSingleton<ItemLibrary>();
            if (!library.Value.IsCreated) return;
            bool enabledChanged = false;
            foreach (var (item, light, position, enabled) in SystemAPI.Query<RefRO<WorldItem>,
                RefRW<DynamicLightSource>, RefRW<DynamicLightWorldPosition>, EnabledRefRW<DynamicLightSource>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                position.ValueRW.Value = item.ValueRO.Position;
                int index = ItemRuntimeUtility.Find(ref library.Value.Value, item.ValueRO.Instance.DefinitionId);
                bool active = false;
                if (index >= 0)
                {
                    ref var data = ref library.Value.Value.Items[index];
                    var source = data.Light;
                    if (data.LightRadiusFromDurability)
                        source.Radius = math.lerp(data.LightRadiusAtZeroDurability, data.Light.Radius,
                            math.saturate(item.ValueRO.Instance.Durability / math.max(0.001f, item.ValueRO.Instance.MaxDurability)));
                    light.ValueRW = source;
                    active = data.HasLight && source.Radius > 0f && source.Intensity > 0f && math.cmax(source.Color) > 0f;
                }
                if (enabled.ValueRO != active)
                {
                    enabled.ValueRW = active;
                    enabledChanged = true;
                }
            }
            if (enabledChanged)
            {
                var revision = SystemAPI.GetSingletonRW<DynamicLightingRevision>();
                revision.ValueRW.Value = unchecked(revision.ValueRO.Value + 1u);
            }
        }
    }
}
