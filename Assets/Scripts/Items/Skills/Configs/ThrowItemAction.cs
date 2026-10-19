using Game.Player;
using Game.World.Blocks;
using Game.World.Interaction;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(fileName = "ThrowItem", menuName = "Game/Items/Skills/Actions/Throw Item")]
    public sealed class ThrowItemAction : ItemSkillAction
    {
        public CursorWorldPosition Position = CursorWorldPosition.Before;
        [Min(0.1f)] public float FlightTime = 0.65f;
        [Min(0.1f)] public float Gravity = 30f;
        [Range(0.01f, 0.2f)] public float CollisionRadius = 0.04f;

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (Position != CursorWorldPosition.Before || !manager.Exists(player) ||
                !manager.HasComponent<SelectedProjectedCell>(player) ||
                !manager.HasComponent<PlayerWorldPosition>(player) ||
                !manager.HasBuffer<WorldItemThrowRequest>(player)) return false;
            var selection = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!selection.IsValid ||
                !TryGetTarget(manager, player, slot, item, out var target) || !WithinDistance(manager, player, target)) return false;
            return CanThrowAt(manager, player, slot, item, (int3)math.floor(target));
        }

        public bool CanThrowAt(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 cell)
        {
            if (!manager.Exists(player) || !manager.HasComponent<PlayerWorldPosition>(player) ||
                !manager.HasBuffer<WorldItemThrowRequest>(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != item || item == 0)
                return false;
            // Before may be beside a wall, with no floor directly beneath it.
            // The existing world-item physics resolves collision and the fall.
            if (!CursorWorldUtility.TryGetBlock(manager, cell, out var block) || !BlockUtility.IsAir(block)) return false;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (query.CalculateEntityCount() != 1 ||
                !ItemDefinitionReference.TryGet(manager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true),
                    item, out var definition) || definition.WorldSprite == null) return false;
            if (!math.isfinite(FlightTime) || !math.isfinite(Gravity) || !math.isfinite(CollisionRadius)) return false;
            float radius = math.clamp(CollisionRadius, 0.01f, 0.2f);
            float3 start = manager.GetComponentData<PlayerWorldPosition>(player).Value;
            start.y += manager.HasComponent<PlayerCollisionShape>(player)
                ? manager.GetComponentData<PlayerCollisionShape>(player).Height * 0.6f : 0.7f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                if (!CursorWorldUtility.TryGetBlock(manager, (int3)math.floor(start + new float3(x, y, z) * radius), out var atStart) ||
                    BlockUtility.IsSolid(atStart)) return false;
            return true;
        }

        // Configuration failures are not dialogue: expose a diagnostic to the chain runner.
        public string GetConfigurationError(EntityManager manager, Entity player, int item)
        {
            if (!manager.Exists(player) || !manager.HasComponent<PlayerWorldPosition>(player) ||
                !manager.HasBuffer<WorldItemThrowRequest>(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player))
                return "Player is missing components required for throwing items.";
            if (!math.isfinite(FlightTime) || !math.isfinite(Gravity) || !math.isfinite(CollisionRadius))
                return "Flight Time, Gravity and Collision Radius must be finite numbers.";
            if (item == 0) return null; // Empty hand is a gameplay refusal.
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (query.CalculateEntityCount() != 1) return "Expected exactly one item library.";
            if (!ItemDefinitionReference.TryGet(manager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true),
                item, out var definition)) return $"Item ID {item} is missing from the item library.";
            return definition.WorldSprite == null ? $"Item '{definition.name}' has no World Sprite." : null;
        }

        public override bool TryGetTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, out float3 position)
        {
            position = default;
            if (!CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell)) return false;
            position = GetThrowTarget(manager.GetComponentData<SelectedProjectedCell>(player), cell,
                math.clamp(CollisionRadius, 0.01f, 0.2f));
            return true;
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (!IsAvailable(manager, player, slot, item) ||
                !CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell)) return;
            ExecuteAtPosition(manager, player, slot, item, cell);
        }

        public bool ExecuteAtPosition(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 cell)
        {
            if (!CanThrowAt(manager, player, slot, item, cell)) return false;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.Slot != slot || entry.Instance.DefinitionId != item) continue;
                float radius = math.clamp(CollisionRadius, 0.01f, 0.2f);
                float3 start = manager.GetComponentData<PlayerWorldPosition>(player).Value;
                start.y += manager.HasComponent<PlayerCollisionShape>(player)
                    ? manager.GetComponentData<PlayerCollisionShape>(player).Height * 0.6f : 0.7f;
                float3 target = GetThrowTarget(manager.HasComponent<SelectedProjectedCell>(player)
                    ? manager.GetComponentData<SelectedProjectedCell>(player) : default, cell, radius);
                float time = math.clamp(FlightTime, 0.1f, 5f), gravity = math.clamp(Gravity, 0.1f, 100f);
                manager.GetBuffer<WorldItemThrowRequest>(player).Add(new WorldItemThrowRequest
                {
                    Item = new WorldItem
                    {
                        Instance = entry.Instance, Position = start,
                        Velocity = (target - start) / time + new float3(0, gravity * time * 0.5f, 0),
                        Gravity = gravity, CollisionRadius = radius
                    }
                });
                entry.Instance = default;
                slots[i] = entry;
                return true;
            }
            return false;
        }

        public static float3 GetThrowTarget(in SelectedProjectedCell selection, int3 before, float radius)
        {
            // Keep top/half-block landing heights; side targets use the centre
            // of the exact Before cell rather than pretending it is a floor.
            return selection.IsValid && selection.FaceType != ProjectedFaceType.Top
                ? (float3)before + .5f
                : ProjectedCellInteractionUtility.GetTopSurfacePosition(selection, before) + new float3(0, radius, 0);
        }
    }
}
