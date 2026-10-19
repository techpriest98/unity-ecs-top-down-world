using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class TopNeighborMaskUtility
    {
        // Only a shared, exposed surface at the same physical half-height.
        public static BlockId BuildFrontTransition(int2 coordinate, int3 owner,
            ChunkBlockAccessor accessor, ViewDirection direction, bool lower, BlockId material)
        {
            int3 front = owner - ViewDirectionUtility.GetAwayFromCameraOffset(direction);
            CellContents contents = accessor.GetContentsOrAir(coordinate, front.x, front.y, front.z);
            BlockId candidate = (lower ? contents.Lower : contents.Upper).Id;
            if (candidate == material || !BlockUtility.IsSolid(candidate) ||
                candidate == BlockId.OceanWater || candidate == BlockId.RiverWater)
                return BlockId.Air;
            BlockId above = lower ? contents.Upper.Id :
                accessor.GetContentsOrAir(coordinate, front.x, front.y + 1, front.z).Lower.Id;
            return above == BlockId.Air ? candidate : BlockId.Air;
        }

        public const byte NorthBit =
            1 << 0;

        public const byte EastBit =
            1 << 1;

        public const byte SouthBit =
            1 << 2;

        public const byte WestBit =
            1 << 3;

        public const byte NorthEastBit =
            1 << 4;

        public const byte SouthEastBit =
            1 << 5;

        public const byte SouthWestBit =
            1 << 6;

        public const byte NorthWestBit =
            1 << 7;

        public static byte Build(
            int2 chunkCoordinate,
            int3 blockWorld,
            ChunkBlockAccessor blockAccessor,
            ViewDirection direction)
        {
            bool lower = blockAccessor.GetBlockOrAir(chunkCoordinate, blockWorld.x, blockWorld.y, blockWorld.z).Shape == BlockShape.LowerHalf;
            return Build(chunkCoordinate, blockWorld, blockAccessor, direction, lower);
        }

        public static byte Build(
            int2 chunkCoordinate,
            int3 blockWorld,
            ChunkBlockAccessor blockAccessor,
            ViewDirection direction,
            bool lowerSurface)
        {
            int2 chunkSizeXZ =
                new int2(
                    ChunkSettings.SizeX,
                    ChunkSettings.SizeZ);

            ViewCoordinate centerView =
                ViewCoordinateUtility.WorldToView(
                    blockWorld,
                    chunkSizeXZ,
                    direction);

            BlockShape centerShape = lowerSurface ? BlockShape.LowerHalf : BlockShape.Full;
            byte mask = 0;

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(0, -1),
                NorthBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(1, 0),
                EastBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(0, 1),
                SouthBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(-1, 0),
                WestBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(1, -1),
                NorthEastBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(1, 1),
                SouthEastBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(-1, 1),
                SouthWestBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            mask |= GetNeighborBit(
                chunkCoordinate,
                centerView,
                new int2(-1, -1),
                NorthWestBit,
                chunkSizeXZ,
                blockAccessor,
                direction, centerShape);

            return mask;
        }

        public static bool Has(
            byte mask,
            byte bit)
        {
            return (mask & bit) != 0;
        }

        private static byte GetNeighborBit(
            int2 chunkCoordinate,
            ViewCoordinate centerView,
            int2 viewOffset,
            byte bit,
            int2 chunkSizeXZ,
            ChunkBlockAccessor blockAccessor,
            ViewDirection direction,
            BlockShape centerShape)
        {
            ViewCoordinate neighborView =
                new ViewCoordinate(
                    centerView.U +
                    viewOffset.x,

                    centerView.V +
                    viewOffset.y,

                    centerView.H);

            int3 neighborWorld =
                ViewCoordinateUtility.ViewToWorld(
                    neighborView,
                    chunkSizeXZ,
                    direction);

            CellContents neighbor =
                blockAccessor.GetContentsOrAir(
                    chunkCoordinate,
                    neighborWorld.x,
                    neighborWorld.y,
                    neighborWorld.z);

            return (IsOpaque(neighbor.Upper.Id) ||
                    (centerShape == BlockShape.LowerHalf && IsOpaque(neighbor.Lower.Id)))
                ? bit
                : (byte)0;
        }

        private static bool IsOpaque(
            BlockId blockId)
        {
            return
                BlockUtility.IsSolid(
                    blockId) &&
                !IsWater(
                    blockId);
        }

        private static bool IsWater(
            BlockId blockId)
        {
            return
                blockId == BlockId.OceanWater || blockId == BlockId.RiverWater;
        }
    }
}
