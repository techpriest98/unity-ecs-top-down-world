using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Mathematics;

namespace Game.World.Interaction
{
    public static class ProjectedCellInteractionUtility
    {
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