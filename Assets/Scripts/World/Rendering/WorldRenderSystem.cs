using Game.World.Blocks;
using Unity.Collections;
using Unity.Entities;

namespace Game.World.Rendering
{
    public partial class WorldRenderSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            WorldRenderer renderer = WorldRenderer.Instance;

            if (renderer == null)
            {
                return;
            }

            var ecb = new EntityCommandBuffer(
                Unity.Collections.Allocator.Temp);

            foreach (var (
                        projectedCells,
                        blocks,
                        entity)
                    in SystemAPI
                        .Query<
                            DynamicBuffer<ProjectedCellData>,
                            DynamicBuffer<BlockData>>()
                        .WithAll<ChunkNeedsRender>()
                        .WithEntityAccess())
            {
                using NativeArray<ProjectedCellGpuData> gpuData =
                    ProjectedCellGpuBuilder.Build(
                        projectedCells,
                        blocks,
                        Allocator.Temp);

                renderer.Render(
                    gpuData,
                    projectionWidth: 4,
                    projectionHeight: 24);

                ecb.SetComponentEnabled<ChunkNeedsRender>(entity, false);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}