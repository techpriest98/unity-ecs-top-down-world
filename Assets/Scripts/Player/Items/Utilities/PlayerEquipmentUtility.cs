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
            ItemID itemId,
            bool preserveDurability = false)
        {
            int slotIndex = FindSlot(slots, slot);

            if (slotIndex < 0)
                return false;

            float maximum = 0f;
            if (itemId != ItemID.None)
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
            bool preserve = preserveDurability && entry.Item != ItemID.None && itemId != ItemID.None;
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

        public static ItemID GetItem(
            DynamicBuffer<PlayerEquipmentSlot> slots,
            EquipmentSlot slot)
        {
            int index = FindSlot(slots, slot);

            return index >= 0
                ? slots[index].Item
                : ItemID.None;
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
            ItemID itemId)
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
