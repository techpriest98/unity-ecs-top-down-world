using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentChangeSystem))]
    [UpdateAfter(typeof(Game.World.Interaction.ProjectedCellSelectionSystem))]
    public partial class PlayerAvailableSkillsSystem : SystemBase
    {
        private const int MaxSkills = 10;

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

            DynamicBuffer<ItemDefinitionReference> library =
                EntityManager.GetBuffer<ItemDefinitionReference>(libraryEntity, true);

            foreach (var (slots, availableSkills, player) in
                     SystemAPI.Query<
                             DynamicBuffer<PlayerEquipmentSlot>,
                             DynamicBuffer<PlayerAvailableSkill>>()
                         .WithAll<PlayerTag>().WithEntityAccess())
            {
                int count = 0;

                AppendSkills(
                    EntityManager, player, libraryEntity,
                    library,
                    slots,
                    availableSkills,
                    EquipmentSlot.LeftHand,
                    ref count);

                AppendSkills(
                    EntityManager, player, libraryEntity,
                    library,
                    slots,
                    availableSkills,
                    EquipmentSlot.RightHand,
                    ref count);

                if (availableSkills.Length > count)
                {
                    availableSkills.RemoveRange(
                        count,
                        availableSkills.Length - count);
                }
            }
        }

        private static void AppendSkills(
            EntityManager manager, Entity player, Entity libraryEntity,
            DynamicBuffer<ItemDefinitionReference> library,
            DynamicBuffer<PlayerEquipmentSlot> slots,
            DynamicBuffer<PlayerAvailableSkill> destination,
            EquipmentSlot sourceSlot,
            ref int count)
        {
            int itemId =
                PlayerEquipmentUtility.GetItem(slots, sourceSlot);

            var skills = SkillProducerUtility.GetSkills(manager, libraryEntity, library, itemId);
            if (skills == null)
            {
                return;
            }

            for (int i = 0;
                 i < skills.Length && count < MaxSkills;
                 i++)
            {
                var skill = skills[i];

                if (skill == null || !skill.HasActions)
                    continue;
                bool available = true;
                foreach (var action in skill.Actions)
                    if (action != null && ((!action.IsAvailable(manager, player, sourceSlot, itemId)) ||
                        (action is Game.Items.Skills.ThrowItemAction &&
                         (!ItemDefinitionReference.TryGet(library, itemId, out var item) || item.WorldSprite == null))))
                    {
                        available = false;
                        break;
                    }
                if (!available) continue;

                var entry = new PlayerAvailableSkill
                {
                    SourceSlot = sourceSlot,
                    SourceItem = itemId,
                    SkillIndex = i
                };

                if (count < destination.Length)
                {
                    PlayerAvailableSkill current = destination[count];

                    if (current.SourceSlot != entry.SourceSlot ||
                        current.SourceItem != entry.SourceItem ||
                        current.SkillIndex != entry.SkillIndex)
                    {
                        destination[count] = entry;
                    }
                }
                else
                {
                    destination.Add(entry);
                }

                count++;
            }
        }

    }
}
