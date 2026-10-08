using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(
        fileName = "ReplaceEquippedItem",
        menuName = "Game/Items/Skills/Actions/Replace Equipped Item")]
    public sealed class ReplaceEquippedItemAction : ItemSkillAction
    {
        [SerializeField]
        private ItemDefinition replacementItem;

        public override void Execute(
            EntityManager entityManager,
            Entity player,
            EquipmentSlot sourceSlot,
            int sourceItem)
        {
            if (replacementItem == null)
                return;

            if (!entityManager.Exists(player) ||
                !entityManager.HasBuffer<PlayerEquipmentSlot>(player))
            {
                return;
            }

            var slots = entityManager.GetBuffer<PlayerEquipmentSlot>(player);
            if (PlayerEquipmentUtility.GetItem(slots, sourceSlot) != sourceItem)
                return;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>());
            if (query.CalculateEntityCount() != 1)
                return;
            var library = query.GetSingleton<ItemLibrary>();
            if (!library.Value.IsCreated)
                return;
            // Apply before the next action executes; retain the item's resource.
            PlayerEquipmentUtility.TryEquip(slots, ref library.Value.Value,
                sourceSlot, replacementItem.Id, preserveDurability: true);
        }
    }
}
