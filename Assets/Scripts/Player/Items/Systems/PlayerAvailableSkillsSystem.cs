using Game.Items;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentChangeSystem))]
    [UpdateAfter(typeof(PlayerActiveHandInputSystem))]
    [UpdateAfter(typeof(IgnitableCellSystem))]
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
                    bool visible = Game.Items.Skills.SkillChainRunner.EvaluateVisibilityTarget(skills[i].VisibilityNode, manager, player, sourceSlot, itemId,
                        out var target, out var position);
                    if (!visible) continue;
                    SetEntry(destination, offset + next++, sourceSlot, itemId, i, target, position);
                }
            // Empty slots stay at their own key; they never borrow the other hand's skills.
            while (next < PlayerAvailableSkill.SkillsPerHand)
                SetEntry(destination, offset + next++, sourceSlot, itemId, -1);
        }

        private static void SetEntry(DynamicBuffer<PlayerAvailableSkill> destination, int index,
            EquipmentSlot slot, int item, int skillIndex, Entity target = default, float3 position = default)
        {
            var current = destination[index];
            if (current.SourceSlot == slot && current.SourceItem == item && current.SkillIndex == skillIndex &&
                current.Target == target && math.all(current.TargetPosition == position)) return;
            destination[index] = new PlayerAvailableSkill
            {
                SourceSlot = slot, SourceItem = item, SkillIndex = skillIndex, Target = target, TargetPosition = position
            };
        }
    }
}
