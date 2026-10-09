using Game.Items;
using Game.Items.Skills;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerAvailableSkillsSystem))]
    [UpdateAfter(typeof(Game.World.Interaction.ProjectedCellSelectionSystem))]
    [UpdateBefore(typeof(Game.World.Blocks.BlockModificationSystem))]
    public partial class PlayerSkillInputSystem : SystemBase
    {
        private EntityQuery libraryQuery;

        protected override void OnCreate()
        {
            libraryQuery = GetEntityQuery(
                ComponentType.ReadOnly<ItemDefinitionReference>());

            RequireForUpdate(libraryQuery);
            RequireForUpdate<PlayerTag>();
        }

        protected override void OnUpdate()
        {
            int pressedIndex = GetPressedSkillIndex();

            if (pressedIndex < 0)
                return;
            
            Entity libraryEntity = libraryQuery.GetSingletonEntity();

            DynamicBuffer<ItemDefinitionReference> library =
                EntityManager.GetBuffer<ItemDefinitionReference>(libraryEntity, true);

            ItemSkillDefinition selectedDefinition = null;
            Entity player = Entity.Null;
            PlayerAvailableSkill selectedSkill = default;

            foreach (var (slots, skills, entity) in
                     SystemAPI.Query<
                             DynamicBuffer<PlayerEquipmentSlot>,
                             DynamicBuffer<PlayerAvailableSkill>>()
                         .WithAll<PlayerTag>()
                         .WithEntityAccess())
            {
                if (pressedIndex >= skills.Length)
                    continue;

                PlayerAvailableSkill entry = skills[pressedIndex];

                int equippedItem = PlayerEquipmentUtility.GetItem(
                    slots,
                    entry.SourceSlot);

                if (equippedItem != entry.SourceItem)
                    continue;

                var producerSkills = SkillProducerUtility.GetSkills(EntityManager, libraryEntity, library, entry.SourceItem);
                if (producerSkills == null ||
                    entry.SkillIndex < 0 ||
                    entry.SkillIndex >= producerSkills.Length)
                {
                    continue;
                }

                ItemSkillDefinition skill =
                    producerSkills[entry.SkillIndex];

                if (skill == null || !skill.HasActions)
                    continue;

                selectedDefinition = skill;
                player = entity;
                selectedSkill = entry;
                break;
            }

            // Викликаємо після виходу з обходу ECS-буферів.
            if (selectedDefinition != null)
            {
                foreach (var action in selectedDefinition.Actions)
                {
                    if (action == null)
                        continue;
                    if (!EntityManager.Exists(player) ||
                        !EntityManager.HasBuffer<PlayerEquipmentSlot>(player))
                        break;
                    var currentSlots = EntityManager.GetBuffer<PlayerEquipmentSlot>(player, true);
                    int currentItem = PlayerEquipmentUtility.GetItem(currentSlots, selectedSkill.SourceSlot);
                    if (selectedSkill.SourceItem == 0 ? currentItem != 0 : currentItem == 0)
                        break;
                    if (!action.IsAvailable(EntityManager, player, selectedSkill.SourceSlot, currentItem)) break;
                    action.Execute(EntityManager, player, selectedSkill.SourceSlot, currentItem);
                }
            }
        }


        private static int GetPressedSkillIndex()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return -1;

            if (keyboard.digit1Key.wasPressedThisFrame) return 0;
            if (keyboard.digit2Key.wasPressedThisFrame) return 1;
            if (keyboard.digit3Key.wasPressedThisFrame) return 2;
            if (keyboard.digit4Key.wasPressedThisFrame) return 3;
            if (keyboard.digit5Key.wasPressedThisFrame) return 4;
            if (keyboard.digit6Key.wasPressedThisFrame) return 5;
            if (keyboard.digit7Key.wasPressedThisFrame) return 6;
            if (keyboard.digit8Key.wasPressedThisFrame) return 7;
            if (keyboard.digit9Key.wasPressedThisFrame) return 8;
            if (keyboard.digit0Key.wasPressedThisFrame) return 9;

            return -1;
        }
    }
}
