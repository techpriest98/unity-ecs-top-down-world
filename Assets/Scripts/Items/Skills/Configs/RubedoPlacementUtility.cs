using Game.Player;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Interaction;
using Game.World.Rendering;
using Game.World.Trees;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items.Skills
{
    // Rubedo clones definitions; it never consumes the equipped tool or paints terrain.
    public static class RubedoPlacementUtility
    {
        public static bool IsSpawnSelection(ItemPlacementSelection selection) =>
            selection.Kind == PlacementSelectionKind.Item || selection.Kind == PlacementSelectionKind.Structure;

        public static bool CanSpawn(EntityManager manager, Entity player, ItemPlacementSelection selection, int3 before)
            => TryPlan(manager, player, selection, before, out _, out _, out _, out _);

        public static bool Spawn(EntityManager manager, Entity player, ItemPlacementSelection selection, int3 before)
        {
            if (!TryPlan(manager, player, selection, before, out float3 surface, out Entity owner,
                    out ItemDefinition definition, out ChunkTree tree)) return false;
            if (selection.Kind == PlacementSelectionKind.Item)
            {
                const float radius = .08f;
                manager.GetBuffer<WorldItemThrowRequest>(player).Add(new WorldItemThrowRequest
                {
                    Item = new WorldItem
                    {
                        Instance = new ItemInstance { DefinitionId = definition.Id,
                            Durability = definition.MaxDurability, MaxDurability = definition.MaxDurability },
                        Position = surface + new float3(0, radius, 0), CollisionRadius = radius,
                        Gravity = 9.81f, Grounded = true
                    }
                });
                return true;
            }
            // Called after skill input has left its ECS query; adding a chunk buffer is safe.
            if (!manager.HasBuffer<ChunkTree>(owner)) manager.AddBuffer<ChunkTree>(owner);
            var trees = manager.GetBuffer<ChunkTree>(owner);
            int id = 1023; // Procedural cells reserve IDs 1..4; manual instances start at 1024.
            foreach (var existing in trees) id = math.max(id, existing.Id);
            if (id == int.MaxValue) return false;
            tree.Id = id + 1;
            trees.Add(tree);
            return true;
        }

        private static bool TryPlan(EntityManager manager, Entity player, ItemPlacementSelection selection, int3 before,
            out float3 surface, out Entity owner, out ItemDefinition definition, out ChunkTree tree)
        {
            surface = default; owner = Entity.Null; definition = null; tree = default;
            if (!IsSpawnSelection(selection) || !manager.Exists(player) ||
                !manager.HasComponent<SelectedProjectedCell>(player)) return false;
            var target = manager.GetComponentData<SelectedProjectedCell>(player);
            if (!target.IsValid || target.IsCutSurface || target.FaceType != ProjectedFaceType.Top ||
                !ProjectedCellInteractionUtility.TryGetPlacementPosition(target, out var actual) ||
                math.any(actual != before)) return false;
            surface = ProjectedCellInteractionUtility.GetTopSurfacePosition(target, before);
            if (!math.all(math.isfinite(surface))) return false;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ChunkComponent>(),
                ComponentType.ReadOnly<ChunkGenerated>(), ComponentType.ReadOnly<BlockData>());
            using var chunks = query.ToEntityArray(Allocator.Temp);
            int2 coordinate = (int2)math.floor(surface.xz / 16f);
            foreach (Entity chunk in chunks)
                if (math.all(manager.GetComponentData<ChunkComponent>(chunk).Coordinate == coordinate)) { owner = chunk; break; }
            if (owner == Entity.Null) return false;
            if (selection.Kind == PlacementSelectionKind.Item)
            {
                if (!manager.HasBuffer<WorldItemThrowRequest>(player)) return false;
                using var library = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
                if (library.CalculateEntityCount() != 1 ||
                    !ItemDefinitionReference.TryGet(manager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true),
                        selection.Id, out definition) || definition.WorldSprite == null) return false;
                return HasClearColumn(manager, owner, coordinate, surface, .16f);
            }
            if (selection.Id != (int)StructureDefinitionId.Pine) return false;
            var config = PineTreeTest.Active;
            var resources = manager.World.GetExistingSystemManaged<TreeResourcePreparationSystem>();
            if (config == null || resources == null || !resources.Ready) return false;
            float scale = math.clamp(config.Scale, .5f, 1.5f);
            tree = new ChunkTree { DefinitionId = StructureDefinitionId.Pine, PlacedByPlayer = true,
                Seed = resources.BaseSeed, GeneratorVersion = 1, Position = surface,
                QuarterTurn = ((config.QuarterTurn % 4) + 4) % 4, Scale = scale,
                TrunkRadius = math.clamp(config.Shape.TrunkRadius, .08f, .6f) * scale,
                TrunkHeight = math.clamp(config.Shape.Height, 4, 24) * scale };
            if (!HasClearColumn(manager, owner, coordinate, surface, tree.TrunkHeight)) return false;
            if (manager.HasComponent<PlayerWorldPosition>(player) && manager.HasComponent<PlayerCollisionShape>(player))
            {
                var shape = manager.GetComponentData<PlayerCollisionShape>(player);
                if (TreeTrunkCollision.Intersects(manager.GetComponentData<PlayerWorldPosition>(player).Value,
                        shape.HalfExtentsXZ, shape.Height, tree)) return false;
            }
            foreach (var chunk in chunks)
                if (manager.HasBuffer<ChunkTree>(chunk))
                    foreach (var existing in manager.GetBuffer<ChunkTree>(chunk, true))
                        if (surface.y < existing.Position.y + existing.TrunkHeight &&
                            surface.y + tree.TrunkHeight > existing.Position.y &&
                            math.distance(surface.xz, existing.Position.xz) < tree.TrunkRadius + existing.TrunkRadius) return false;
            return true;
        }

        private static bool HasClearColumn(EntityManager manager, Entity owner, int2 coordinate, float3 surface, float height)
        {
            if (surface.y < 0 || surface.y + height >= ChunkSettings.SizeY || !manager.HasBuffer<SplitCellData>(owner)) return false;
            var blocks = manager.GetBuffer<BlockData>(owner, true);
            var split = manager.GetBuffer<SplitCellData>(owner, true);
            int x = (int)math.floor(surface.x) - coordinate.x * 16, z = (int)math.floor(surface.z) - coordinate.y * 16;
            int first = (int)math.floor(surface.y * 2f + .001f), last = (int)math.ceil((surface.y + height) * 2f);
            for (int half = first; half < last; half++)
            {
                int index = ChunkUtility.ToIndex(x, half / 2, z);
                if (index < 0 || index >= blocks.Length) return false;
                var contents = SplitCellUtility.Get(blocks, split, index);
                if (!BlockUtility.IsAir((half & 1) == 0 ? contents.Lower.Id : contents.Upper.Id)) return false;
            }
            return true;
        }
    }
}
