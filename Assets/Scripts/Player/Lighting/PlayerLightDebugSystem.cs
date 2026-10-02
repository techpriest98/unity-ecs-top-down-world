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
                if (toggleLeft)
                    ToggleTorch(slots, ref items, EquipmentSlot.LeftHand);

                if (toggleRight)
                    ToggleTorch(slots, ref items, EquipmentSlot.RightHand);
            }
        }

        private static void ToggleTorch(
            DynamicBuffer<PlayerEquipmentSlot> slots,
            ref ItemLibraryBlob items,
            EquipmentSlot slot)
        {
            ItemID current = PlayerEquipmentUtility.GetItem(slots, slot);

            if (current != ItemID.None && current != ItemID.Torch)
                return;

            ItemID next = current == ItemID.Torch
                ? ItemID.None
                : ItemID.Torch;

            PlayerEquipmentUtility.TryEquip(
                slots,
                ref items,
                slot,
                next);
        }
    }
}