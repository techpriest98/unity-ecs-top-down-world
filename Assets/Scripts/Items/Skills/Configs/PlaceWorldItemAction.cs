using Game.Player;
using Game.World.Interaction;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Place World Item")]
    public sealed class PlaceWorldItemAction : ItemSkillAction
    {
        public override bool TryGetTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, out float3 position)
        {
            position = default;
            if (!manager.HasComponent<SelectedProjectedCell>(player)) return false;
            var selected = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!ProjectedCellInteractionUtility.TryGetPlacementPosition(selected, out var before)) return false;
            position = ProjectedCellInteractionUtility.GetTopSurfacePosition(selected, before);
            return CanPlace(manager, player, slot, item, position);
        }

        public bool CanPlace(EntityManager manager, Entity player, EquipmentSlot slot, int item, float3 position)
        {
            if (item <= 0 || !manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                !manager.HasComponent<SelectedProjectedCell>(player) || !manager.HasBuffer<WorldItemThrowRequest>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != item) return false;
            var selected = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!ProjectedCellInteractionUtility.TryGetPlacementPosition(selected, out var before) ||
                math.distancesq(ProjectedCellInteractionUtility.GetTopSurfacePosition(selected, before), position) > .000001f ||
                !RubedoPlacementUtility.CanSpawn(manager, player, new ItemPlacementSelection { Kind = PlacementSelectionKind.Item, Id = item }, before)) return false;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var entity in entities)
            {
                var other = manager.GetComponentData<WorldItem>(entity);
                if (other.Grounded && math.distancesq(other.Position - new float3(0, other.CollisionRadius, 0), position) < .04f) return false;
            }
            return true;
        }

        public bool ExecuteAt(EntityManager manager, Entity player, EquipmentSlot slot, int item, float3 position)
        {
            if (!CanPlace(manager, player, slot, item, position)) return false;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            var instance = PlayerEquipmentUtility.GetInstance(slots, slot);
            const float radius = .08f;
            manager.GetBuffer<WorldItemThrowRequest>(player).Add(new WorldItemThrowRequest { Item = new WorldItem
            {
                Instance = instance, Grounded = true, CollisionRadius = radius,
                Position = position + new float3(0, radius, 0), Gravity = 9.81f
            }});
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Slot == slot) { var entry = slots[i]; entry.Instance = default; slots[i] = entry; break; }
            return true;
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (TryGetTarget(manager, player, slot, item, out var position)) ExecuteAt(manager, player, slot, item, position);
        }
    }
}
