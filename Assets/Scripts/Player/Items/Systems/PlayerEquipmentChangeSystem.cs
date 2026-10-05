using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlayerEquipmentChangeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
            state.RequireForUpdate<PlayerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ItemLibrary library = SystemAPI.GetSingleton<ItemLibrary>();

            if (!library.Value.IsCreated)
                return;

            ref ItemLibraryBlob items = ref library.Value.Value;

            foreach (var (slots, requests) in
                     SystemAPI.Query<
                             DynamicBuffer<PlayerEquipmentSlot>,
                             DynamicBuffer<PlayerEquipmentChangeRequest>>()
                         .WithAll<PlayerTag>())
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    PlayerEquipmentChangeRequest request = requests[i];

                    bool slotExists = false;

                    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                    {
                        if (slots[slotIndex].Slot == request.Slot)
                        {
                            slotExists = true;
                            break;
                        }
                    }

                    if (!slotExists)
                        continue;

                    ItemID currentItem = PlayerEquipmentUtility.GetItem(
                        slots,
                        request.Slot);

                    if (currentItem != request.ExpectedItem)
                        continue;

                    if (currentItem == request.ReplacementItem)
                        continue;

                    PlayerEquipmentUtility.TryEquip(
                        slots,
                        ref items,
                        request.Slot,
                        request.ReplacementItem);
                }

                requests.Clear();
            }
        }
    }
}