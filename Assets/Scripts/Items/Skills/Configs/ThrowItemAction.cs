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
            return selection.IsValid && selection.FaceType == ProjectedFaceType.Top &&
                (Distance == null || (CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell) &&
                    WithinDistance(manager, player, new float3(cell.x + 0.5f, cell.y, cell.z + 0.5f))));
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (!IsAvailable(manager, player, slot, item) ||
                !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                !CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell) ||
                !CursorWorldUtility.TryGetBlock(manager, cell, out var block) || !BlockUtility.IsAir(block) ||
                !CursorWorldUtility.TryGetBlock(manager, cell + new int3(0, -1, 0), out var support) ||
                !BlockUtility.IsSolid(support)) return;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (query.CalculateEntityCount() != 1 ||
                !ItemDefinitionReference.TryGet(manager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true),
                    item, out var definition) || definition.WorldSprite == null) return;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.Slot != slot || entry.Instance.DefinitionId != item) continue;
                float radius = math.clamp(CollisionRadius, 0.01f, 0.2f);
                float3 start = manager.GetComponentData<PlayerWorldPosition>(player).Value;
                start.y += manager.HasComponent<PlayerCollisionShape>(player)
                    ? manager.GetComponentData<PlayerCollisionShape>(player).Height * 0.6f : 0.7f;
                float3 target = new float3(cell.x + 0.5f, cell.y + radius, cell.z + 0.5f);
                if (!math.isfinite(FlightTime) || !math.isfinite(Gravity) || !math.isfinite(CollisionRadius)) return;
                // Do not spawn overlapping terrain (for example, next to a wall).
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    if (!CursorWorldUtility.TryGetBlock(manager, (int3)math.floor(start + new float3(x, y, z) * radius), out var atStart) ||
                        BlockUtility.IsSolid(atStart)) return;
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
                return;
            }
        }
    }
}
