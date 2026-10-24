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
            if (UnityEngine.Time.timeScale <= 0f) return;
            
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
                for (int index = 0; index < skills.Length; index++)
                {
                    PlayerAvailableSkill entry = skills[index];
                    if (entry.SkillIndex < 0) continue;

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
                    if (!skill.InputBinding.WasPressed(index)) continue;
                    // UI clicks must not also throw/use a world item.
                    if (skill.InputBinding.IsMouse && UnityEngine.EventSystems.EventSystem.current != null &&
                        UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) continue;

                    selectedDefinition = skill;
                    player = entity;
                    selectedSkill = entry;
                    break;
                }
                if (selectedDefinition != null) break;
            }

            // Викликаємо після виходу з обходу ECS-буферів.
            if (selectedDefinition != null)
            {
                if (selectedDefinition.UseChain)
                {
                    if (!SkillChainRunner.Execute(selectedDefinition.FirstNode, EntityManager, player,
                        selectedSkill.SourceSlot, selectedSkill.SourceItem, out var failure))
                        ShowFailure(player, failure);
                    return;
                }
                foreach (var condition in selectedDefinition.Conditions ?? System.Array.Empty<ItemSkillCondition>())
                {
                    if (condition == null)
                    {
                        ShowFailure(player, "I can't do that.");
                        return;
                    }
                    if (!condition.Evaluate(EntityManager, player, selectedSkill.SourceSlot, selectedSkill.SourceItem))
                    {
                        ShowFailure(player, condition.FailureMessage);
                        return;
                    }
                }
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
                    if (!action.IsAvailable(EntityManager, player, selectedSkill.SourceSlot, currentItem))
                    {
                        ShowFailure(player, "I can't do that.");
                        break;
                    }
                    action.Execute(EntityManager, player, selectedSkill.SourceSlot, currentItem);
                }
            }
        }

        private void ShowFailure(Entity player, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            // One component per player: repeated attempts refresh, never stack bubbles.
            var value = new SkillFailureMessage
            {
                Text = new Unity.Collections.FixedString512Bytes(message.Length > 120 ? message.Substring(0, 120) : message),
                ExpiresAt = SystemAPI.Time.ElapsedTime + 2.5
            };
            if (EntityManager.HasComponent<SkillFailureMessage>(player)) EntityManager.SetComponentData(player, value);
            else EntityManager.AddComponentData(player, value);
        }

    }
}
