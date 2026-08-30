using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Interaction
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ProjectedCellSelectionRenderSystem : SystemBase
    {
        private const float CellWidth = 1f;
        private const float CellHeight = 0.5f;
        private const float DepthStep = 0.01f;

        protected override void OnCreate()
        {
            RequireForUpdate<SelectedProjectedCell>();
            RequireForUpdate<ViewDirectionComponent>();
        }

        protected override void OnUpdate()
        {
            ChunkProceduralRenderer renderer = ChunkProceduralRenderer.Instance;
            if (renderer == null)
            {
                return;
            }

            SelectedProjectedCell selection = SystemAPI.GetSingleton<SelectedProjectedCell>();
            if (!selection.IsValid)
            {
                renderer.SetSelection(false, float3.zero, 0, default);
                return;
            }

            ViewDirection direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            float3 chunkPosition = ChunkRenderPositionUtility.GetPosition(
                selection.ChunkCoordinate,
                direction,
                CellWidth,
                CellHeight,
                DepthStep);

            renderer.SetSelection(
                true,
                chunkPosition,
                selection.ProjectionPosition,
                selection.FaceType);
        }
    }
}   