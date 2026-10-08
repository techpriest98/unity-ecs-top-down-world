using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    public static class PlayerEquipmentUtility
    {
        public static bool TryEquip(
            DynamicBuffer<PlayerEquipmentSlot> slots,
            ref ItemLibraryBlob library,
            EquipmentSlot slot,
            int itemId,
            bool preserveDurability = false)
        {
            int slotIndex = FindSlot(slots, slot);

            if (slotIndex < 0)
                return false;

            float maximum = 0f;
            if (itemId != 0)
            {
                int itemIndex = FindItem(ref library, itemId);

                if (itemIndex < 0)
                    return false;
                maximum = library.Items[itemIndex].MaxDurability;

                EquipmentSlot allowed =
                    library.Items[itemIndex].AllowedSlots;

                if ((allowed & slot) != slot)
                    return false;
            }

            PlayerEquipmentSlot entry = slots[slotIndex];
            bool preserve = preserveDurability && entry.Item != 0 && itemId != 0;
            if (!preserve)
            {
                entry.MaxDurability = maximum;
                entry.Durability = maximum;
                entry.DurabilityElapsed = 0f;
            }
            entry.Item = itemId;
            slots[slotIndex] = entry;

            return true;
        }

        public static int GetItem(
            DynamicBuffer<PlayerEquipmentSlot> slots,
            EquipmentSlot slot)
        {
            int index = FindSlot(slots, slot);

            return index >= 0
                ? slots[index].Item
                : 0;
        }

        private static int FindSlot(
            DynamicBuffer<PlayerEquipmentSlot> slots,
            EquipmentSlot slot)
        {
            int value = (int)slot;

            if (value == 0 || (value & (value - 1)) != 0)
                return -1;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Slot == slot)
                    return i;
            }

            return -1;
        }

        private static int FindItem(
            ref ItemLibraryBlob library,
            int itemId)
        {
            for (int i = 0; i < library.Items.Length; i++)
            {
                if (library.Items[i].Id == itemId)
                    return i;
            }

            return -1;
        }
    }
}
