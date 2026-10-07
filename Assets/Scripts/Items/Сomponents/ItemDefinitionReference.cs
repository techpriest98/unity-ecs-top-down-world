using Unity.Entities;

namespace Game.Items
{
    public struct ItemDefinitionReference : IBufferElementData
    {
        public ItemID Id;
        public UnityObjectRef<ItemDefinition> Definition;

        // Resolving UnityEngine.Objects is a main-thread operation.
        public static bool TryGet(
            DynamicBuffer<ItemDefinitionReference> references,
            ItemID id,
            out ItemDefinition definition)
        {
            definition = null;
            if (id == ItemID.None)
                return false;
            for (int i = 0; i < references.Length; i++)
            {
                if (references[i].Id != id)
                    continue;
                definition = references[i].Definition.Value;
                return definition != null;
            }
            return false;
        }
    }
}
