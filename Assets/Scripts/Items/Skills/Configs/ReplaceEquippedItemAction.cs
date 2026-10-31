using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    public enum EquippedItemHand { SourceHand, OtherHand }

    [CreateAssetMenu(
        fileName = "ReplaceEquippedItem",
        menuName = "Game/Items/Skills/Actions/Replace Equipped Item")]
    public sealed class ReplaceEquippedItemAction : ItemSkillAction
    {
        [SerializeField]
        private ItemDefinition replacementItem;
        public EquippedItemHand TargetHand;
        public ItemDefinition RequiredTargetItem;
        [Tooltip("Optional crafting ingredient consumed from the other hand after a successful source-hand replacement.")]
        public ItemDefinition ConsumeOtherHandItem;

        private bool TryGetTargetSlot(EquipmentSlot source, out EquipmentSlot target)
        {
            target = source;
            if (TargetHand == EquippedItemHand.SourceHand) return true;
            if (TargetHand != EquippedItemHand.OtherHand ||
                (source != EquipmentSlot.LeftHand && source != EquipmentSlot.RightHand)) return false;
            target = source == EquipmentSlot.LeftHand ? EquipmentSlot.RightHand : EquipmentSlot.LeftHand;
            return true;
        }

        public string GetConfigurationError(EntityManager manager, EquipmentSlot slot)
        {
            if (!TryGetTargetSlot(slot, out var targetSlot)) return "Invalid target hand for the source slot.";
            if (ConsumeOtherHandItem != null && (TargetHand != EquippedItemHand.SourceHand ||
                (slot != EquipmentSlot.LeftHand && slot != EquipmentSlot.RightHand)))
                return "An offhand ingredient requires a source-hand replacement.";
            if (replacementItem == null) return "Replacement Item is not assigned.";
            if (replacementItem.Id <= 0 || (replacementItem.AllowedSlots & targetSlot) == 0)
                return "Replacement Item has an invalid ID or does not support the target slot.";
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>());
            if (query.CalculateEntityCount() != 1 || !query.GetSingleton<ItemLibrary>().Value.IsCreated)
                return "Expected one initialized item library.";
            using var definitions = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (definitions.CalculateEntityCount() != 1 || !ItemDefinitionReference.TryGet(
                manager.GetBuffer<ItemDefinitionReference>(definitions.GetSingletonEntity(), true), replacementItem.Id, out var registered) ||
                registered != replacementItem) return "Replacement Item is missing from the item library.";
            if (RequiredTargetItem != null && (!ItemDefinitionReference.TryGet(
                manager.GetBuffer<ItemDefinitionReference>(definitions.GetSingletonEntity(), true), RequiredTargetItem.Id, out var required) ||
                required != RequiredTargetItem))
                return "Required Target Item is missing from the item library.";
            return null;
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (!TryGetTargetSlot(slot, out var targetSlot) || replacementItem == null || (replacementItem.AllowedSlots & targetSlot) == 0 ||
                !manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player)) return false;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player, true);
            return PlayerEquipmentUtility.GetItem(slots, slot) == item &&
                (ConsumeOtherHandItem == null || (TargetHand == EquippedItemHand.SourceHand &&
                    (slot == EquipmentSlot.LeftHand || slot == EquipmentSlot.RightHand) &&
                    PlayerEquipmentUtility.GetItem(slots, slot == EquipmentSlot.LeftHand ? EquipmentSlot.RightHand : EquipmentSlot.LeftHand) == ConsumeOtherHandItem.Id)) &&
                (RequiredTargetItem == null || PlayerEquipmentUtility.GetItem(slots, targetSlot) == RequiredTargetItem.Id);
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
            if (!TryGetTargetSlot(sourceSlot, out var targetSlot) || PlayerEquipmentUtility.GetItem(slots, sourceSlot) != sourceItem)
                return false;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>());
            if (query.CalculateEntityCount() != 1)
                return false;
            var library = query.GetSingleton<ItemLibrary>();
            if (!library.Value.IsCreated)
                return false;
            // Apply before the next action executes; retain the item's resource.
            var original = PlayerEquipmentUtility.GetInstance(slots, targetSlot);
            if (!PlayerEquipmentUtility.TryEquip(slots, ref library.Value.Value,
                targetSlot, replacementItem.Id, preserveDurability: true)) return false;
            if (ConsumeOtherHandItem == null) return true;
            var otherSlot = sourceSlot == EquipmentSlot.LeftHand ? EquipmentSlot.RightHand : EquipmentSlot.LeftHand;
            if (PlayerEquipmentUtility.TryEquip(slots, ref library.Value.Value, otherSlot, 0)) return true;
            // A failed ingredient removal must not leave a free crafted item.
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Slot == targetSlot)
                {
                    var entry = slots[i];
                    entry.Instance = original;
                    slots[i] = entry;
                    break;
                }
            return false;
        }
    }
}
