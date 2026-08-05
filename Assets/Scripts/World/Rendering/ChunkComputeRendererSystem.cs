using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    public partial class ChunkComputeRendererSystem :
        SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ChunkNeedsRender>();
            RequireForUpdate<ChunkTextureSlice>();
        }

        protected override void OnUpdate()
        {
            ChunkComputeRenderer renderer =
                ChunkComputeRenderer.Instance;

            if (renderer == null)
            {
                return;
            }

            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);

            foreach (var (
                        projectedCells,
                        textureSlice,
                        entity)
                    in SystemAPI.Query<
                            DynamicBuffer<ProjectedCellData>,
                            RefRO<ChunkTextureSlice>>()
                        .WithAll<ChunkNeedsRender>()
                        .WithEntityAccess())
            {
                int slice =
                    (int)math.round(
                        textureSlice.ValueRO.Value);

                renderer.Render(
                    projectedCells.AsNativeArray(),
                    slice);

                ecb.SetComponentEnabled<
                    ChunkNeedsRender>(
                    entity,
                    false);
            }

            ecb.Playback(
                EntityManager);

            ecb.Dispose();
        }
    }
}