using Unity.Mathematics;

namespace Game.Items
{
    public static class ItemRuntimeUtility
    {
        public static int Find(ref ItemLibraryBlob library, int id)
        {
            for (int i = 0; i < library.Items.Length; i++)
                if (library.Items[i].Id == id) return i;
            return -1;
        }

        public static void TickDurability(ref ItemInstance instance, in ItemData data, float delta)
        {
            if (data.DurabilityLossAmount <= 0f) return;
            float interval = math.max(0.001f, data.DurabilityLossInterval);
            instance.DurabilityElapsed += delta;
            float ticks = math.floor(instance.DurabilityElapsed / interval);
            instance.DurabilityElapsed = math.max(0f, instance.DurabilityElapsed - ticks * interval);
            instance.Durability = math.max(0f, instance.Durability - ticks * data.DurabilityLossAmount);
        }
    }
}
