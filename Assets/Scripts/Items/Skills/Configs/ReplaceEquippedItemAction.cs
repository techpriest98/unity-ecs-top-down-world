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

        public string GetConfigurationError(EntityManager manager, EquipmentSlot slot)
        {
            if (replacementItem == null) return "Replacement Item is not assigned.";
            if (replacementItem.Id <= 0 || (replacementItem.AllowedSlots & slot) == 0)
                return "Replacement Item has an invalid ID or does not support the source slot.";
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>());
            if (query.CalculateEntityCount() != 1 || !query.GetSingleton<ItemLibrary>().Value.IsCreated)
                return "Expected one initialized item library.";
            using var definitions = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (definitions.CalculateEntityCount() != 1 || !ItemDefinitionReference.TryGet(
                manager.GetBuffer<ItemDefinitionReference>(definitions.GetSingletonEntity(), true), replacementItem.Id, out var registered) ||
                registered != replacementItem) return "Replacement Item is missing from the item library.";
            return null;
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (replacementItem == null || (replacementItem.AllowedSlots & slot) == 0 ||
                !manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player)) return false;
            return PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) == item;
        }

        public override void Execute(
            EntityManager entityManager,
            Entity player,
            EquipmentSlot sourceSlot,
            int sourceItem)
            => ExecuteReplacement(entityManager, player, sourceSlot, sourceItem);

        public bool ExecuteReplacement(EntityManager entityManager, Entity player, EquipmentSlot sourceSlot, int sourceItem)
        {
            if (!IsAvailable(entityManager, player, sourceSlot, sourceItem))
                return false;

            if (!entityManager.Exists(player) ||
                !entityManager.HasBuffer<PlayerEquipmentSlot>(player))
            {
                return false;
            }

            var slots = entityManager.GetBuffer<PlayerEquipmentSlot>(player);
            if (PlayerEquipmentUtility.GetItem(slots, sourceSlot) != sourceItem)
                return false;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>());
            if (query.CalculateEntityCount() != 1)
                return false;
            var library = query.GetSingleton<ItemLibrary>();
            if (!library.Value.IsCreated)
                return false;
            // Apply before the next action executes; retain the item's resource.
            return PlayerEquipmentUtility.TryEquip(slots, ref library.Value.Value,
                sourceSlot, replacementItem.Id, preserveDurability: true);
        }
    }
}
