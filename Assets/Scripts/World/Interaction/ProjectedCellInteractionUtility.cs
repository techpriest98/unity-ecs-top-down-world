using Game.World.Chunks;
using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Mathematics;

namespace Game.World.Interaction
{
    public static class ProjectedCellInteractionUtility
    {
        public static bool TryRemoveSelectedPart(in SelectedProjectedCell selection, CellContents contents,
            out CellContents result)
        {
            result = contents;
            if (!selection.IsValid) return false;
            if (contents.IsUniform)
            {
                if (contents.Lower.Id == BlockId.Air) return false;
                result = default;
                return true;
            }
            bool lower = ProjectedCellPacking.IsLowerHalf(selection.BlockData) ||
                selection.FaceType == ProjectedFaceType.SideLower;
            BlockPart part = lower ? contents.Lower : contents.Upper;
            if (part.Id == BlockId.Air || part.Id != selection.BlockId) return false;
            if (lower) result.Lower = new BlockPart(BlockId.Air, 0);
            else result.Upper = new BlockPart(BlockId.Air, 0);
            return true;
        }

        public static void GetBlockPlacementStart(in SelectedProjectedCell selection, int3 input,
            out int3 origin, out int startHalf)
        {
            origin = input;
            startHalf = 0;
            if (!TryGetPlacementPosition(selection, out int3 before) || !math.all(before == input)) return;
            if (selection.FaceType == ProjectedFaceType.Top && ProjectedCellPacking.IsLowerHalf(selection.BlockData))
            { origin.y--; startHalf = 1; }
            else if (selection.FaceType == ProjectedFaceType.SideUpper &&
                (selection.BlockData & ProjectedCellPacking.UpperHalfFlag) != 0) startHalf = 1;
        }

        public static bool TryGetPlacementPosition(
            in SelectedProjectedCell selection,
            out int3 worldPosition)
        {
            if (!selection.IsValid)
            {
                worldPosition = int3.zero;
                return false;
            }

            worldPosition = GetSourceAirWorldPosition(selection);
            return true;
        }

        public static bool TryGetRemovalPosition(
            in SelectedProjectedCell selection,
            ViewDirection direction,
            out int3 worldPosition)
        {
            if (!selection.IsValid)
            {
                worldPosition = int3.zero;
                return false;
            }

            int3 sourceAirPosition = GetSourceAirWorldPosition(selection);

            switch (selection.FaceType)
            {
                case ProjectedFaceType.Top:
                    worldPosition = sourceAirPosition + new int3(0, -1, 0);
                    return true;

                case ProjectedFaceType.SideUpper:
                case ProjectedFaceType.SideLower:
                    worldPosition =
                        sourceAirPosition +
                        ViewDirectionUtility
                            .GetAwayFromCameraOffset(
                                direction);

                    return true;

                default:
                    worldPosition = int3.zero;
                    return false;
            }
        }

        private static int3 GetSourceAirWorldPosition(in SelectedProjectedCell selection)
        {
            int3 localPosition = ChunkUtility.ToLocalPosition(selection.SourceAirIndex);

            return new int3(
                selection.ChunkCoordinate.x * ChunkSettings.SizeX + localPosition.x,
                localPosition.y,
                selection.ChunkCoordinate.y * ChunkSettings.SizeZ + localPosition.z);
        }
    }
}
