using Game.Items.Skills;
using Unity.Entities;
using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(menuName = "Game/Items/Empty Hand")]
    public sealed class EmptyHandDefinition : ScriptableObject
    {
        public ItemSkillDefinition[] Skills = System.Array.Empty<ItemSkillDefinition>();
    }

    public struct EmptyHandReference : IComponentData
    {
        public UnityObjectRef<EmptyHandDefinition> Definition;
    }

    public static class SkillProducerUtility
    {
        public static ItemSkillDefinition[] GetSkills(EntityManager manager, Entity library,
            DynamicBuffer<ItemDefinitionReference> items, int itemId)
        {
            if (itemId != 0)
                return ItemDefinitionReference.TryGet(items, itemId, out var item) ? item.Skills : null;
            if (!manager.HasComponent<EmptyHandReference>(library)) return null;
            var empty = manager.GetComponentData<EmptyHandReference>(library).Definition.Value;
            return empty != null ? empty.Skills : null;
        }
    }
}
