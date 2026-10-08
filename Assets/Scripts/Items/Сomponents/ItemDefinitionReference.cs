using Unity.Entities;

namespace Game.Items
{
    public struct ItemDefinitionReference : IBufferElementData
    {
        public int Id;
        public UnityObjectRef<ItemDefinition> Definition;

        // Resolving UnityEngine.Objects is a main-thread operation.
        public static bool TryGet(
            DynamicBuffer<ItemDefinitionReference> references,
            int id,
            out ItemDefinition definition)
        {
            definition = null;
            if (id == 0)
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
