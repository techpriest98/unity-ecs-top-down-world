using Game.Items;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentChangeSystem))]
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
            float delta = (float)SystemAPI.Time.DeltaTime;
            foreach (var slotBuffer in SystemAPI.Query<DynamicBuffer<PlayerEquipmentSlot>>().WithAll<PlayerTag>())
            {
                var slots = slotBuffer;
                for (int i = 0; i < slots.Length; i++)
                {
                    var entry = slots[i];
                    if (entry.Item == 0)
                        continue;
                    for (int j = 0; j < items.Length; j++)
                    {
                        if (items[j].Id != entry.Item)
                            continue;
                        if (items[j].DurabilityLossAmount > 0f)
                        {
                            float interval = items[j].DurabilityLossInterval;
                            entry.DurabilityElapsed += delta;
                            float ticks = math.floor(entry.DurabilityElapsed / interval);
                            if (ticks > 0f)
                            {
                                entry.DurabilityElapsed = math.max(0f,
                                    entry.DurabilityElapsed - ticks * interval);
                                entry.Durability = math.max(0f, entry.Durability -
                                    ticks * items[j].DurabilityLossAmount);
                            }
                        }
                        break;
                    }
                    if (entry.Durability <= 0f)
                    {
                        entry.Item = 0;
                        entry.Durability = entry.MaxDurability = 0f;
                        entry.DurabilityElapsed = 0f;
                    }
                    slots[i] = entry;
                }
            }
        }
    }
}
