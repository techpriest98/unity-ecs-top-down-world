using Game.Player;
using Unity.Collections;
using Unity.Entities;

namespace Game.Items
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PlayerEquipmentDurabilitySystem))]
    [UpdateAfter(typeof(Game.World.Time.WorldTimeSystem))]
    public partial struct WorldItemDurabilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
            state.RequireForUpdate<WorldItem>();
        }
        public void OnUpdate(ref SystemState state)
        {
            var library = SystemAPI.GetSingleton<ItemLibrary>();
            if (!library.Value.IsCreated) return;
            using var commands = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (item, entity) in SystemAPI.Query<RefRW<WorldItem>>().WithEntityAccess())
            {
                int index = ItemRuntimeUtility.Find(ref library.Value.Value, item.ValueRO.Instance.DefinitionId);
                if (index < 0) continue;
                var value = item.ValueRO;
                ItemRuntimeUtility.TickDurability(ref value.Instance, library.Value.Value.Items[index], (float)SystemAPI.Time.DeltaTime);
                item.ValueRW = value;
                if (value.Instance.Durability <= 0f) commands.DestroyEntity(entity);
            }
            commands.Playback(state.EntityManager);
        }
    }
}
