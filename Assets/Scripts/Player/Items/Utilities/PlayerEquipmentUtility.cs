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
            bool preserve = preserveDurability && entry.Instance.DefinitionId != 0 && itemId != 0;
            if (!preserve)
            {
                entry.Instance = default;
                entry.Instance.MaxDurability = maximum;
                entry.Instance.Durability = maximum;
                entry.Instance.DurabilityElapsed = 0f;
            }
            entry.Instance.DefinitionId = itemId;
            slots[slotIndex] = entry;

            return true;
        }

        public static int GetItem(
            DynamicBuffer<PlayerEquipmentSlot> slots,
            EquipmentSlot slot)
        {
            int index = FindSlot(slots, slot);

            return index >= 0
                ? slots[index].Instance.DefinitionId
                : 0;
        }

        public static ItemInstance GetInstance(DynamicBuffer<PlayerEquipmentSlot> slots, EquipmentSlot slot)
        {
            int index = FindSlot(slots, slot);
            return index >= 0 ? slots[index].Instance : default;
        }

        public static bool TrySwap(DynamicBuffer<PlayerEquipmentSlot> slots,
            ref ItemLibraryBlob library, EquipmentSlot first, EquipmentSlot second)
        {
            int a = FindSlot(slots, first), b = FindSlot(slots, second);
            if (a < 0 || b < 0 || a == b)
                return false;
            var left = slots[a];
            var right = slots[b];
            if (!CanEquip(ref library, right.Instance.DefinitionId, first) ||
                !CanEquip(ref library, left.Instance.DefinitionId, second))
                return false;
            var instance = left.Instance;
            left.Instance = right.Instance;
            right.Instance = instance;
            slots[a] = left;
            slots[b] = right;
            return true;
        }

        private static bool CanEquip(ref ItemLibraryBlob library, int id, EquipmentSlot slot)
        {
            if (id == 0) return true;
            int index = FindItem(ref library, id);
            return index >= 0 && (library.Items[index].AllowedSlots & slot) == slot;
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
