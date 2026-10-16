using Game.Items;
using Game.Player;
using Game.World.Generation;
using Game.World.Rendering;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Entities;
using UnityEngine;

namespace Game.UI
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class EquipmentHudSystem : SystemBase
    {
        private EntityQuery playerQuery;
        private EntityQuery libraryQuery;
        private EntityQuery minimapQuery;
        private EquipmentHud hud;
        private bool ownsHud;

        protected override void OnCreate()
        {
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerEquipmentSlot>(),
                ComponentType.ReadOnly<PlayerAvailableSkill>());
            libraryQuery = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            minimapQuery = GetEntityQuery(ComponentType.ReadOnly<WorldMinimapInfo>(), ComponentType.ReadOnly<WorldMinimapCell>());
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
            if (minimapQuery.CalculateEntityCount() == 1 && EntityManager.HasComponent<PlayerWorldPosition>(player))
            {
                Entity map = minimapQuery.GetSingletonEntity();
                var view = SystemAPI.TryGetSingleton<ViewDirectionComponent>(out var viewDirection)
                    ? viewDirection.Value : ViewDirection.Front;
                if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(out var transition) && transition.IsActive)
                    view = transition.TargetDirection;
                hud.SetMinimap(EntityManager.GetComponentData<WorldMinimapInfo>(map),
                    EntityManager.GetBuffer<WorldMinimapCell>(map, true),
                    EntityManager.GetComponentData<PlayerWorldPosition>(player).Value,
                    view);
                foreach (var (chunk, revision, surface, entity) in SystemAPI.Query<RefRO<ChunkComponent>,
                             RefRO<ChunkSurfaceRevision>, DynamicBuffer<ChunkSurfaceSample>>()
                             .WithAll<ChunkGenerated>().WithEntityAccess())
                    hud.SetMinimapChunk(chunk.ValueRO.Coordinate, entity, revision.ValueRO.Value, surface);
                hud.EndMinimapFrame();
            }
            hud.SetActiveHand(EntityManager.HasComponent<PlayerActiveHand>(player)
                ? EntityManager.GetComponentData<PlayerActiveHand>(player).Slot : EquipmentSlot.RightHand);
            var slots = EntityManager.GetBuffer<PlayerEquipmentSlot>(player, true);
            var skills = EntityManager.GetBuffer<PlayerAvailableSkill>(player, true);
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(
                libraryQuery.GetSingletonEntity(), true);
            for (int i = 0; i < 2; i++)
            {
                var slot = i == 0 ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand;
                int id = PlayerEquipmentUtility.GetItem(slots, slot);
                var instance = PlayerEquipmentUtility.GetInstance(slots, slot);
                hud.SetSelectedBlock(i, id != 0 ? instance.PlacementSelection : default, instance.PlacementMode);
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
            for (int i = 0; i < PlayerAvailableSkill.SlotCount; i++)
            {
                if (i < skills.Length)
                {
                    var entry = skills[i];
                    var producerSkills = SkillProducerUtility.GetSkills(EntityManager, libraryQuery.GetSingletonEntity(), references, entry.SourceItem);
                    if (producerSkills != null && entry.SkillIndex >= 0 && entry.SkillIndex < producerSkills.Length)
                    {
                        var skill = producerSkills[entry.SkillIndex];
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
