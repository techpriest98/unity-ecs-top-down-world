using Game.Player;
using Game.World.Interaction;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Pick Up Item")]
    public sealed class PickUpItemAction : ItemSkillAction
    {
        private Entity Find(EntityManager manager, Entity player, EquipmentSlot slot, bool checkDistance = true)
        {
            if (!manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != 0 ||
                !manager.HasComponent<SelectedProjectedCell>(player)) return Entity.Null;
            var cursor = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!cursor.IsValid || cursor.FaceType != ProjectedFaceType.Top ||
                !CursorWorldUtility.TryGetPosition(manager, player, CursorWorldPosition.Before, out var cell)) return Entity.Null;
            var target = FindAt(manager, player, slot, cell);
            if (target != Entity.Null && checkDistance && !WithinDistance(manager, player, Surface(manager, target))) return Entity.Null;
            return target;
        }

        public static float3 Surface(EntityManager manager, Entity target)
        {
            var value = manager.GetComponentData<WorldItem>(target);
            return value.Position - new float3(0, value.CollisionRadius, 0);
        }

        public static Entity FindAt(EntityManager manager, Entity player, EquipmentSlot slot, int3 cell)
        {
            if (!manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != 0) return Entity.Null;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
            using var library = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (library.CalculateEntityCount() != 1) return Entity.Null;
            var definitions = manager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true);
            using var entities = query.ToEntityArray(Allocator.Temp);
            Entity target = Entity.Null;
            float best = float.MaxValue;
            float expectedHeight = manager.HasComponent<SelectedProjectedCell>(player)
                ? ProjectedCellInteractionUtility.GetTopSurfacePosition(manager.GetComponentData<SelectedProjectedCell>(player), cell).y
                : cell.y;
            foreach (var entity in entities)
            {
                var item = manager.GetComponentData<WorldItem>(entity);
                if (!item.Grounded || item.Instance.DefinitionId == 0 || item.Instance.Durability <= 0) continue;
                if (!ItemDefinitionReference.TryGet(definitions, item.Instance.DefinitionId, out var definition) ||
                    (definition.AllowedSlots & slot) == 0) continue;
                var surface = item.Position;
                surface.y -= item.CollisionRadius;
                if ((int)math.floor(surface.x) != cell.x || (int)math.floor(surface.z) != cell.z ||
                    math.abs(surface.y - expectedHeight) > 0.01f) continue;
                float distance = math.lengthsq(surface.xz - new float2(cell.x + 0.5f, cell.z + 0.5f));
                if (distance < best || (distance == best && entity.Index < target.Index))
                { best = distance; target = entity; }
            }
            return target;
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => item == 0 && Find(manager, player, slot) != Entity.Null;

        public override bool TryGetTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, out float3 position)
        {
            position = default;
            var target = Find(manager, player, slot, false);
            if (target == Entity.Null) return false;
            var value = manager.GetComponentData<WorldItem>(target);
            position = value.Position - new float3(0, value.CollisionRadius, 0);
            return true;
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (item != 0) return;
            Entity target = Find(manager, player, slot);
            if (target == Entity.Null) return;
            ExecuteTarget(manager, player, slot, target, Surface(manager, target));
        }

        public bool CanPickUp(EntityManager manager, Entity player, EquipmentSlot slot, Entity target, float3 position, bool checkDistance = true)
        {
            if (!manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                !manager.Exists(target) || !manager.HasComponent<WorldItem>(target)) return false;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player, true);
            bool empty = false;
            foreach (var entry in slots)
                if (entry.Slot == slot && entry.Instance.DefinitionId == 0) empty = true;
            var value = manager.GetComponentData<WorldItem>(target);
            if (!empty || !value.Grounded || value.Instance.DefinitionId == 0 || value.Instance.Durability <= 0 ||
                math.distancesq(Surface(manager, target), position) > 0.000001f || (checkDistance && !WithinDistance(manager, player, position))) return false;
            using var library = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            return library.CalculateEntityCount() == 1 && ItemDefinitionReference.TryGet(
                manager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true), value.Instance.DefinitionId,
                out var definition) && (definition.AllowedSlots & slot) != 0;
        }

        public bool ExecuteTarget(EntityManager manager, Entity player, EquipmentSlot slot, Entity target, float3 position, bool checkDistance = true)
        {
            if (!CanPickUp(manager, player, slot, target, position, checkDistance)) return false;
            var instance = manager.GetComponentData<WorldItem>(target).Instance;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.Slot != slot || entry.Instance.DefinitionId != 0) continue;
                entry.Instance = instance;
                slots[i] = entry;
                manager.DestroyEntity(target);
                return true;
            }
            return false;
        }
    }
}
