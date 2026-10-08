using Game.Items;
using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.UI
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class EquipmentHudSystem : SystemBase
    {
        private EntityQuery playerQuery;
        private EntityQuery libraryQuery;
        private EquipmentHud hud;
        private bool ownsHud;

        protected override void OnCreate()
        {
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerEquipmentSlot>(),
                ComponentType.ReadOnly<PlayerAvailableSkill>());
            libraryQuery = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate(playerQuery);
            RequireForUpdate(libraryQuery);
        }

        protected override void OnUpdate()
        {
            if (playerQuery.CalculateEntityCount() != 1 || libraryQuery.CalculateEntityCount() != 1)
                return;
            if (hud == null)
            {
                hud = Object.FindFirstObjectByType<EquipmentHud>();
                ownsHud = false;
            }
            if (hud == null)
            {
                var prefab = Resources.Load<GameObject>("EquipmentHud");
                if (prefab == null)
                    return;
                hud = Object.Instantiate(prefab).GetComponent<EquipmentHud>();
                ownsHud = true;
            }
            Entity player = playerQuery.GetSingletonEntity();
            var slots = EntityManager.GetBuffer<PlayerEquipmentSlot>(player, true);
            var skills = EntityManager.GetBuffer<PlayerAvailableSkill>(player, true);
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(
                libraryQuery.GetSingletonEntity(), true);
            for (int i = 0; i < 2; i++)
            {
                var slot = i == 0 ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand;
                int id = PlayerEquipmentUtility.GetItem(slots, slot);
                for (int j = 0; j < slots.Length; j++)
                    if (slots[j].Slot == slot)
                        hud.SetDurability(i, slots[j].Instance.Durability, slots[j].Instance.MaxDurability);
                for (int j = 0; j < slots.Length; j++)
                    if (slots[j].Slot == slot)
                        hud.SetDurability(i, slots[j].Instance.Durability, slots[j].Instance.MaxDurability);
                if (ItemDefinitionReference.TryGet(references, id, out var item))
                {
                    hud.SetSlot(i, item.Icon, item.DisplayName);
                    hud.SetHandItem(i, item);
                }
                else
                {
                    hud.SetSlot(i, null, "—");
                    hud.SetHandItem(i, null);
                }
            }
            for (int i = 0; i < 10; i++)
            {
                if (i < skills.Length)
                {
                    var entry = skills[i];
                    if (ItemDefinitionReference.TryGet(references, entry.SourceItem, out var item) &&
                        item.Skills != null && entry.SkillIndex >= 0 && entry.SkillIndex < item.Skills.Length)
                    {
                        var skill = item.Skills[entry.SkillIndex];
                        if (skill != null)
                        {
                            hud.SetSlot(i + 2, skill.Icon, skill.DisplayName);
                            hud.SetSkill(i, skill, entry.SourceSlot);
                            continue;
                        }
                    }
                }
                hud.SetSlot(i + 2, null, "");
                hud.SetSkill(i, null, default);
            }
        }

        protected override void OnStopRunning()
        {
            if (hud != null && ownsHud)
                Object.Destroy(hud.gameObject);
            hud = null;
            ownsHud = false;
        }

        protected override void OnDestroy() => OnStopRunning();
    }
}
