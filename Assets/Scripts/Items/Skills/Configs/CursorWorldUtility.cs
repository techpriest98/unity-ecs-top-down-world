using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Interaction;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items.Skills
{
    public enum CursorWorldPosition { Disconnected = -1, Before = 0, Behind = 1 }

    public static class CursorWorldUtility
    {
        public static bool TryGetPosition(EntityManager manager, Entity player,
            CursorWorldPosition source, out int3 position)
        {
            position = default;
            if (source == CursorWorldPosition.Disconnected || !manager.Exists(player) ||
                !manager.HasComponent<SelectedProjectedCell>(player))
                return false;
            var target = manager.GetComponentData<SelectedProjectedCell>(player);
            if (source == CursorWorldPosition.Before)
                return ProjectedCellInteractionUtility.TryGetPlacementPosition(target, out position);
            if (source != CursorWorldPosition.Behind)
                return false;
            using var view = manager.CreateEntityQuery(ComponentType.ReadOnly<ViewDirectionComponent>());
            return view.CalculateEntityCount() == 1 &&
                ProjectedCellInteractionUtility.TryGetRemovalPosition(target,
                    view.GetSingleton<ViewDirectionComponent>().Value, out position);
        }

        public static bool TryGetBlock(EntityManager manager, int3 position, out BlockId block)
        {
            block = BlockId.Air;
            if (!TryGetContents(manager, position, out CellContents contents)) return false;
            block = contents.GeometryView().BlockId;
            return true;
        }

        public static bool TryGetContents(EntityManager manager, int3 position, out CellContents contents)
        {
            contents = default;
            if (position.y < 0 || position.y >= ChunkSettings.SizeY)
                return false;
            int2 coordinate = new int2(FloorDiv(position.x, ChunkSettings.SizeX),
                FloorDiv(position.z, ChunkSettings.SizeZ));
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ChunkComponent>(),
                ComponentType.ReadOnly<ChunkGenerated>(), ComponentType.ReadOnly<BlockData>());
            using var chunks = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in chunks)
            {
                if (math.any(manager.GetComponentData<ChunkComponent>(entity).Coordinate != coordinate))
                    continue;
                int index = ChunkUtility.ToIndex(position.x - coordinate.x * ChunkSettings.SizeX,
                    position.y, position.z - coordinate.y * ChunkSettings.SizeZ);
                var blocks = manager.GetBuffer<BlockData>(entity, true);
                if (index < 0 || index >= blocks.Length)
                    return false;
                contents = SplitCellUtility.Get(blocks, manager.GetBuffer<SplitCellData>(entity, true), index);
                return true;
            }
            return false;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
    }
}
