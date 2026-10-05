using Unity.Entities;

namespace Game.Items
{
    public sealed class ItemDefinitionLibrary : IComponentData
    {
        public ItemDefinition[] Items;

        public bool TryGet(ItemID id, out ItemDefinition definition)
        {
            definition = null;

            if (id == ItemID.None || Items == null)
                return false;

            for (int i = 0; i < Items.Length; i++)
            {
                ItemDefinition item = Items[i];

                if (item == null || item.Id != id)
                    continue;

                definition = item;
                return true;
            }

            return false;
        }
    }
}