using Game.Items;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlayerLightDebugSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
            state.RequireForUpdate<PlayerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            bool toggleLeft = keyboard.kKey.wasPressedThisFrame;
            bool toggleRight = keyboard.lKey.wasPressedThisFrame;

            if (!toggleLeft && !toggleRight)
                return;

            ItemLibrary library = SystemAPI.GetSingleton<ItemLibrary>();

            if (!library.Value.IsCreated)
                return;

            ref ItemLibraryBlob items = ref library.Value.Value;

            foreach (var slots in
                     SystemAPI.Query<DynamicBuffer<PlayerEquipmentSlot>>()
                         .WithAll<PlayerTag>())
            {
                if (toggleLeft) {
                    int currentItem = PlayerEquipmentUtility.GetItem(slots, EquipmentSlot.LeftHand);
                    int nextItem = 0;

                    if (currentItem == 0)
                        nextItem = items.DebugLeftItemId;

                    PlayerEquipmentUtility.TryEquip(
                        slots,
                        ref items,
                        EquipmentSlot.LeftHand,
                        nextItem);
                }

                if (toggleRight)
                {
                    int currentItem = PlayerEquipmentUtility.GetItem(slots, EquipmentSlot.RightHand);
                    int nextItem = 0;

                    if (currentItem == 0)
                        nextItem = items.DebugRightItemId;

                    PlayerEquipmentUtility.TryEquip(
                        slots,
                        ref items,
                        EquipmentSlot.RightHand,
                        nextItem);
                }
            }
        }
    }
}
