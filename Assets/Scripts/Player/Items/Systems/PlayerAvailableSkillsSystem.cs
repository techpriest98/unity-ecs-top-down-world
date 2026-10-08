using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentChangeSystem))]
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

            foreach (var (slots, availableSkills) in
                     SystemAPI.Query<
                             DynamicBuffer<PlayerEquipmentSlot>,
                             DynamicBuffer<PlayerAvailableSkill>>()
                         .WithAll<PlayerTag>())
            {
                int count = 0;

                AppendSkills(
                    library,
                    slots,
                    availableSkills,
                    EquipmentSlot.LeftHand,
                    ref count);

                AppendSkills(
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
            DynamicBuffer<ItemDefinitionReference> library,
            DynamicBuffer<PlayerEquipmentSlot> slots,
            DynamicBuffer<PlayerAvailableSkill> destination,
            EquipmentSlot sourceSlot,
            ref int count)
        {
            int itemId =
                PlayerEquipmentUtility.GetItem(slots, sourceSlot);

            if (!ItemDefinitionReference.TryGet(library, itemId, out ItemDefinition item) ||
                item.Skills == null)
            {
                return;
            }

            for (int i = 0;
                 i < item.Skills.Length && count < MaxSkills;
                 i++)
            {
                var skill = item.Skills[i];

                if (skill == null || !skill.HasActions)
                    continue;

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
