using Game.World.Interaction;
using Game.World.Rendering;
using Game.World.Trees;
using Game.World.Chunks;
using Game.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    // Shared by execution and cursor presentation; Rubedo selection is irrelevant.
    public static class NigredoTargetUtility
    {
        public static int Resolve(EntityManager manager, Entity player, out Entity owner, out int id, out float3 surface)
        {
            owner = Entity.Null; id = 0; surface = default;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)) return 0;
            if (!CursorWorldUtility.TryGetPosition(manager, player, CursorWorldPosition.Before, out var before)) return 0;
            if (WorldObjectTargetUtility.TryFind(manager, player, before, false, out owner, out id, out surface)) return 1;
            if (WorldObjectTargetUtility.TryFind(manager, player, before, true, out owner, out id, out surface)) return 2;
            return 0;
        }

        public static bool IsActive(EntityManager manager, Entity player)
        {
            if (!manager.HasBuffer<PlayerEquipmentSlot>(player)) return false;
            var hand = manager.HasComponent<Game.Player.PlayerActiveHand>(player)
                ? manager.GetComponentData<Game.Player.PlayerActiveHand>(player).Slot : EquipmentSlot.RightHand;
            int item = PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), hand);
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            if (query.CalculateEntityCount() != 1 || !ItemDefinitionReference.TryGet(
                manager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true), item, out var definition)) return false;
            foreach (var skill in definition.Skills ?? System.Array.Empty<ItemSkillDefinition>())
                if (skill != null && skill.UseChain && skill.FirstNode != null &&
                    skill.FirstNode.Kind == SkillChainNodeKind.DestructionTargetBranch) return true;
            return false;
        }
    }

    public enum WorldObjectCategory { Item, Structure }

    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Destroy World Object")]
    public sealed class DestroyWorldObjectAction : ItemSkillAction
    {
        public WorldObjectCategory Category;

        public bool CanDestroy(EntityManager manager, Entity owner, int structureId, float3 surface)
        {
            if (!manager.Exists(owner)) return false;
            if (structureId == 0 && manager.HasComponent<WorldItem>(owner))
            {
                var worldItem = manager.GetComponentData<WorldItem>(owner);
                return WorldObjectTargetUtility.IsStructureItem(manager, worldItem.Instance.DefinitionId) == (Category == WorldObjectCategory.Structure) &&
                    worldItem.Grounded && math.distancesq(worldItem.Position - new float3(0, worldItem.CollisionRadius, 0), surface) < .0001f;
            }
            if (Category == WorldObjectCategory.Item)
                return false;
            if (structureId == 0 || !manager.HasBuffer<ChunkTree>(owner)) return false;
            foreach (var tree in manager.GetBuffer<ChunkTree>(owner, true))
                if (tree.Id == structureId && math.distancesq(tree.Position, surface) < .0001f) return true;
            return false;
        }

        public bool ExecuteTarget(EntityManager manager, Entity owner, int structureId, float3 surface)
        {
            if (!CanDestroy(manager, owner, structureId, surface)) return false;
            if (structureId == 0) manager.DestroyEntity(owner);
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
        public static bool IsStructureItem(EntityManager manager, int id)
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            return query.CalculateEntityCount() == 1 && ItemDefinitionReference.TryGet(
                manager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true), id, out var definition) && definition.IsStructure;
        }
        // A lower-half Top is below its Before cell (e.g. surface Y=63.5,
        // Before Y=64). Recover the source cell from the selection, not floor(Y).
        public static bool TryFindFromPosition(EntityManager manager, Entity player, float3 position, bool structure,
            out Entity owner, out int id, out float3 surface)
        {
            owner = Entity.Null; id = 0; surface = default;
            if (!math.all(math.isfinite(position)) || !manager.Exists(player) ||
                !manager.HasComponent<SelectedProjectedCell>(player)) return false;
            var selected = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!ProjectedCellInteractionUtility.TryGetPlacementPosition(selected, out var before)) return false;
            float3 top = ProjectedCellInteractionUtility.GetTopSurfacePosition(selected, before);
            // Top Surface Position supplies the physical height. Valid Cursor
            // Target supplies the Before cell centre. Accept these two forms.
            if (math.distancesq(position, top) > .0001f &&
                math.distancesq(position, (float3)before + .5f) > .0001f) return false;
            return TryFind(manager, player, before, structure, out owner, out id, out surface);
        }

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
                    if (!item.Grounded || item.Instance.DefinitionId == 0 || item.Instance.Durability <= 0 ||
                        !Matches(position, before, expected.y) || IsStructureItem(manager, item.Instance.DefinitionId)) continue;
                    float distance = math.distancesq(position.xz, expected.xz);
                    if (distance < best || (distance == best && (owner == Entity.Null || entity.Index < owner.Index)))
                    { best = distance; owner = entity; surface = position; }
                }
            }
            if (structure)
            {
                using var itemQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
                using var items = itemQuery.ToEntityArray(Allocator.Temp);
                foreach (var entity in items)
                {
                    var item = manager.GetComponentData<WorldItem>(entity);
                    var position = item.Position - new float3(0, item.CollisionRadius, 0);
                    if (!item.Grounded || !Matches(position, before, expected.y) || !IsStructureItem(manager, item.Instance.DefinitionId)) continue;
                    float distance = math.distancesq(position.xz, expected.xz);
                    if (distance < best || (distance == best && (owner == Entity.Null || entity.Index < owner.Index)))
                    { best = distance; owner = entity; id = 0; surface = position; }
                }
            }
            return owner != Entity.Null;
        }

        private static bool Matches(float3 position, int3 cell, float height) =>
            (int)math.floor(position.x) == cell.x && (int)math.floor(position.z) == cell.z && math.abs(position.y - height) <= .01f;
    }
}
