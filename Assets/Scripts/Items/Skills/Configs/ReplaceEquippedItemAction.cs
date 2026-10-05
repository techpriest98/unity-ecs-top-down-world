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
            ItemID sourceItem)
        {
            if (replacementItem == null)
                return;

            if (!entityManager.Exists(player) ||
                !entityManager.HasBuffer<PlayerEquipmentChangeRequest>(player))
            {
                return;
            }

            DynamicBuffer<PlayerEquipmentChangeRequest> requests =
                entityManager.GetBuffer<PlayerEquipmentChangeRequest>(player);

            requests.Add(new PlayerEquipmentChangeRequest
            {
                Slot = sourceSlot,
                ExpectedItem = sourceItem,
                ReplacementItem = replacementItem.Id
            });
        }
    }
}