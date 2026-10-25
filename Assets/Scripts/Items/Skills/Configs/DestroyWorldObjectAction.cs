using Game.World.Interaction;
using Game.World.Rendering;
using Game.World.Trees;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    public enum WorldObjectCategory { Item, Structure }

    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Destroy World Object")]
    public sealed class DestroyWorldObjectAction : ItemSkillAction
    {
        public WorldObjectCategory Category;

        public bool CanDestroy(EntityManager manager, Entity owner, int structureId, float3 surface)
        {
            if (!manager.Exists(owner)) return false;
            if (Category == WorldObjectCategory.Item)
            {
                if (structureId != 0 || !manager.HasComponent<WorldItem>(owner)) return false;
                var item = manager.GetComponentData<WorldItem>(owner);
                return item.Grounded && math.distancesq(item.Position - new float3(0, item.CollisionRadius, 0), surface) < .0001f;
            }
            if (structureId == 0 || !manager.HasBuffer<ChunkTree>(owner)) return false;
            foreach (var tree in manager.GetBuffer<ChunkTree>(owner, true))
                if (tree.Id == structureId && math.distancesq(tree.Position, surface) < .0001f) return true;
            return false;
        }

        public bool ExecuteTarget(EntityManager manager, Entity owner, int structureId, float3 surface)
        {
            if (!CanDestroy(manager, owner, structureId, surface)) return false;
            if (Category == WorldObjectCategory.Item) manager.DestroyEntity(owner);
            else
            {
                var trees = manager.GetBuffer<ChunkTree>(owner);
                for (int i = 0; i < trees.Length; i++)
                    if (trees[i].Id == structureId) { trees.RemoveAt(i); return true; }
                return false;
            }
            return true;
        }

        // Like ReplaceWorldItemAction, this requires a resolved graph target.
        public override void Execute(EntityManager manager, Entity player, EquipmentSlot sourceSlot, int sourceItem) { }
    }

    public static class WorldObjectTargetUtility
    {
        // Category, not selected definition ID, determines the target. Resolve once.
        public static bool TryFind(EntityManager manager, Entity player, int3 before, bool structure,
            out Entity owner, out int id, out float3 surface)
        {
            owner = Entity.Null; id = 0; surface = default;
            if (!manager.Exists(player) || !manager.HasComponent<SelectedProjectedCell>(player)) return false;
            var cursor = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!cursor.IsValid || cursor.IsCutSurface || cursor.FaceType != ProjectedFaceType.Top ||
                !ProjectedCellInteractionUtility.TryGetPlacementPosition(cursor, out var actual) || math.any(actual != before)) return false;
            float3 expected = ProjectedCellInteractionUtility.GetTopSurfacePosition(cursor, before);
            float best = float.MaxValue;
            using var query = structure
                ? manager.CreateEntityQuery(ComponentType.ReadOnly<ChunkTree>(), ComponentType.ReadOnly<ChunkComponent>())
                : manager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                if (structure)
                {
                    foreach (var tree in manager.GetBuffer<ChunkTree>(entity, true))
                    {
                        if (tree.Id == 0 || !Matches(tree.Position, before, expected.y)) continue;
                        float distance = math.distancesq(tree.Position.xz, expected.xz);
                        if (distance < best || (distance == best && (owner == Entity.Null || entity.Index < owner.Index ||
                            (entity.Index == owner.Index && tree.Id < id))))
                        { best = distance; owner = entity; id = tree.Id; surface = tree.Position; }
                    }
                }
                else
                {
                    var item = manager.GetComponentData<WorldItem>(entity);
                    float3 position = item.Position - new float3(0, item.CollisionRadius, 0);
                    if (!item.Grounded || item.Instance.DefinitionId == 0 || item.Instance.Durability <= 0 || !Matches(position, before, expected.y)) continue;
                    float distance = math.distancesq(position.xz, expected.xz);
                    if (distance < best || (distance == best && (owner == Entity.Null || entity.Index < owner.Index)))
                    { best = distance; owner = entity; surface = position; }
                }
            }
            return owner != Entity.Null;
        }

        private static bool Matches(float3 position, int3 cell, float height) =>
            (int)math.floor(position.x) == cell.x && (int)math.floor(position.z) == cell.z && math.abs(position.y - height) <= .01f;
    }
}
