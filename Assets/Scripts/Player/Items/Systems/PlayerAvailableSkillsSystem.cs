using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentChangeSystem))]
    [UpdateAfter(typeof(PlayerActiveHandInputSystem))]
    [UpdateAfter(typeof(Game.World.Interaction.ProjectedCellSelectionSystem))]
    public partial class PlayerAvailableSkillsSystem : SystemBase
    {
        private EntityQuery libraryQuery;

        protected override void OnCreate()
        {
            libraryQuery = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate(libraryQuery);
            RequireForUpdate<PlayerTag>();
        }

        protected override void OnUpdate()
        {
            Entity libraryEntity = libraryQuery.GetSingletonEntity();
            var library = EntityManager.GetBuffer<ItemDefinitionReference>(libraryEntity, true);
            foreach (var (slots, availableSkills, activeHand, player) in SystemAPI.Query<
                         DynamicBuffer<PlayerEquipmentSlot>, DynamicBuffer<PlayerAvailableSkill>, RefRO<PlayerActiveHand>>().WithAll<PlayerTag>().WithEntityAccess())
            {
                var destination = availableSkills;
                if (destination.Length != PlayerAvailableSkill.SlotCount)
                {
                    destination.ResizeUninitialized(PlayerAvailableSkill.SlotCount);
                    for (int i = 0; i < destination.Length; i++)
                        destination[i] = new PlayerAvailableSkill { SkillIndex = -1 };
                }
                FillHand(EntityManager, player, libraryEntity, library, slots, destination, activeHand.ValueRO.Slot, 0);
            }
        }

        private static void FillHand(EntityManager manager, Entity player, Entity libraryEntity,
            DynamicBuffer<ItemDefinitionReference> library, DynamicBuffer<PlayerEquipmentSlot> slots,
            DynamicBuffer<PlayerAvailableSkill> destination, EquipmentSlot sourceSlot, int offset)
        {
            int itemId = PlayerEquipmentUtility.GetItem(slots, sourceSlot);
            var skills = SkillProducerUtility.GetSkills(manager, libraryEntity, library, itemId);
            int next = 0;
            if (skills != null)
                for (int i = 0; i < skills.Length && next < PlayerAvailableSkill.SkillsPerHand; i++)
                {
                    if (skills[i] == null || !skills[i].HasActions) continue;
                    bool visible = true;
                    if (skills[i].UseChain)
                        visible = Game.Items.Skills.SkillChainRunner.IsVisible(skills[i].FirstNode, manager, player, sourceSlot, itemId);
                    foreach (var condition in skills[i].Conditions ?? System.Array.Empty<Game.Items.Skills.ItemSkillCondition>())
                        if (condition != null && condition.ControlsVisibility &&
                            !condition.Evaluate(manager, player, sourceSlot, itemId)) { visible = false; break; }
                    if (!visible) continue;
                    SetEntry(destination, offset + next++, sourceSlot, itemId, i);
                }
            // Empty slots stay at their own key; they never borrow the other hand's skills.
            while (next < PlayerAvailableSkill.SkillsPerHand)
                SetEntry(destination, offset + next++, sourceSlot, itemId, -1);
        }

        private static void SetEntry(DynamicBuffer<PlayerAvailableSkill> destination, int index,
            EquipmentSlot slot, int item, int skillIndex)
        {
            var current = destination[index];
            if (current.SourceSlot == slot && current.SourceItem == item && current.SkillIndex == skillIndex) return;
            destination[index] = new PlayerAvailableSkill
            {
                SourceSlot = slot, SourceItem = item, SkillIndex = skillIndex
            };
        }
    }
}
