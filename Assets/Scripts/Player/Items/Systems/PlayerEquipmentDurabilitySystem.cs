using Game.Items;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentChangeSystem))]
    [UpdateAfter(typeof(Game.World.Time.WorldTimeSystem))]
    [UpdateBefore(typeof(PlayerAvailableSkillsSystem))]
    [UpdateBefore(typeof(PlayerArmAnimationSelectionSystem))]
    [UpdateBefore(typeof(PlayerEquipmentLightSystem))]
    public partial struct PlayerEquipmentDurabilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<ItemLibrary>();

        public void OnUpdate(ref SystemState state)
        {
            var library = SystemAPI.GetSingleton<ItemLibrary>();
            if (!library.Value.IsCreated)
                return;
            ref var items = ref library.Value.Value.Items;
            float delta = SystemAPI.TryGetSingleton<Game.World.Time.WorldTime>(out var clock)
                ? clock.DeltaSeconds : SystemAPI.Time.DeltaTime;
            foreach (var slotBuffer in SystemAPI.Query<DynamicBuffer<PlayerEquipmentSlot>>().WithAll<PlayerTag>())
            {
                var slots = slotBuffer;
                for (int i = 0; i < slots.Length; i++)
                {
                    var entry = slots[i];
                    if (entry.Instance.DefinitionId == 0)
                        continue;
                    for (int j = 0; j < items.Length; j++)
                    {
                        if (items[j].Id != entry.Instance.DefinitionId)
                            continue;
                        ItemRuntimeUtility.TickDurability(ref entry.Instance, items[j], delta);
                        break;
                    }
                    if (entry.Instance.Durability <= 0f)
                    {
                        entry.Instance = default;
                    }
                    slots[i] = entry;
                }
            }
        }
    }
}
