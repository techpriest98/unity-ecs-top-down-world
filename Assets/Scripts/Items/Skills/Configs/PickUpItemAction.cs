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
        private Entity Find(EntityManager manager, Entity player, EquipmentSlot slot)
        {
            if (!manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != 0 ||
                !manager.HasComponent<SelectedProjectedCell>(player)) return Entity.Null;
            var cursor = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!cursor.IsValid || cursor.FaceType != ProjectedFaceType.Top ||
                !CursorWorldUtility.TryGetPosition(manager, player, CursorWorldPosition.Before, out var cell)) return Entity.Null;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
            using var library = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (library.CalculateEntityCount() != 1) return Entity.Null;
            var definitions = manager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true);
            using var entities = query.ToEntityArray(Allocator.Temp);
            Entity target = Entity.Null;
            float best = float.MaxValue;
            foreach (var entity in entities)
            {
                var item = manager.GetComponentData<WorldItem>(entity);
                if (!item.Grounded || item.Instance.DefinitionId == 0 || item.Instance.Durability <= 0) continue;
                if (!ItemDefinitionReference.TryGet(definitions, item.Instance.DefinitionId, out var definition) ||
                    (definition.AllowedSlots & slot) == 0) continue;
                var surface = item.Position;
                surface.y -= item.CollisionRadius;
                if (!WithinDistance(manager, player, surface)) continue;
                if ((int)math.floor(surface.x) != cell.x || (int)math.floor(surface.z) != cell.z ||
                    math.abs(surface.y - cell.y) > 0.01f) continue;
                float distance = math.lengthsq(surface.xz - new float2(cell.x + 0.5f, cell.z + 0.5f));
                if (distance < best || (distance == best && entity.Index < target.Index))
                { best = distance; target = entity; }
            }
            return target;
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => item == 0 && Find(manager, player, slot) != Entity.Null;

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (item != 0) return;
            Entity target = Find(manager, player, slot);
            if (target == Entity.Null) return;
            var instance = manager.GetComponentData<WorldItem>(target).Instance;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.Slot != slot || entry.Instance.DefinitionId != 0) continue;
                entry.Instance = instance;
                slots[i] = entry;
                manager.DestroyEntity(target);
                return;
            }
        }
    }
}
