using Game.Items;
using Game.Items.Skills;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerAvailableSkillsSystem))]
    public partial class PlayerSkillInputSystem : SystemBase
    {
        private EntityQuery libraryQuery;

        protected override void OnCreate()
        {
            libraryQuery = GetEntityQuery(
                ComponentType.ReadOnly<ItemDefinitionLibrary>());

            RequireForUpdate(libraryQuery);
            RequireForUpdate<PlayerTag>();
        }

        protected override void OnUpdate()
        {
            int pressedIndex = GetPressedSkillIndex();

            if (pressedIndex < 0)
                return;
            
            Entity libraryEntity = libraryQuery.GetSingletonEntity();

            ItemDefinitionLibrary library =
                EntityManager.GetComponentObject<ItemDefinitionLibrary>(
                    libraryEntity);

            ItemSkillAction action = null;
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

                ItemID equippedItem = PlayerEquipmentUtility.GetItem(
                    slots,
                    entry.SourceSlot);

                if (equippedItem != entry.SourceItem)
                    continue;

                if (!library.TryGet(
                        entry.SourceItem,
                        out ItemDefinition item))
                {
                    continue;
                }

                if (item.Skills == null ||
                    entry.SkillIndex < 0 ||
                    entry.SkillIndex >= item.Skills.Length)
                {
                    continue;
                }

                ItemSkillDefinition skill =
                    item.Skills[entry.SkillIndex];

                if (skill == null || skill.Action == null)
                    continue;

                action = skill.Action;
                player = entity;
                selectedSkill = entry;
                break;
            }

            // Викликаємо після виходу з обходу ECS-буферів.
            if (action != null)
            {
                action.Execute(
                    EntityManager,
                    player,
                    selectedSkill.SourceSlot,
                    selectedSkill.SourceItem);
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