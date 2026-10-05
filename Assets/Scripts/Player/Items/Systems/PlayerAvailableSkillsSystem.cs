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
            libraryQuery = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionLibrary>());

            RequireForUpdate(libraryQuery);
            RequireForUpdate<PlayerTag>();
        }

        protected override void OnUpdate()
        {
            Entity libraryEntity = libraryQuery.GetSingletonEntity();

            ItemDefinitionLibrary library =
                EntityManager.GetComponentObject<ItemDefinitionLibrary>(
                    libraryEntity);

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
            ItemDefinitionLibrary library,
            DynamicBuffer<PlayerEquipmentSlot> slots,
            DynamicBuffer<PlayerAvailableSkill> destination,
            EquipmentSlot sourceSlot,
            ref int count)
        {
            ItemID itemId =
                PlayerEquipmentUtility.GetItem(slots, sourceSlot);

            if (!library.TryGet(itemId, out ItemDefinition item) ||
                item.Skills == null)
            {
                return;
            }

            for (int i = 0;
                 i < item.Skills.Length && count < MaxSkills;
                 i++)
            {
                var skill = item.Skills[i];

                if (skill == null || skill.Action == null)
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