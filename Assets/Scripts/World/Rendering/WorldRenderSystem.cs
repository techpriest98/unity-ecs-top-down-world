using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public partial class WorldRenderSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ChunkNeedsRender>();
        }
        
        protected override void OnUpdate()
        {
            WorldRenderer worldRenderer =
                WorldRenderer.Instance;

            ChunkRenderManager chunkRenderManager =
                ChunkRenderManager.Instance;

            if (worldRenderer == null ||
                chunkRenderManager == null)
            {
                return;
            }

            int projectionWidth =
                ChunkSettings.SizeX;

            int projectionHeight =
                ChunkSettings.SizeY * 2 +
                ChunkSettings.SizeZ;

            int textureWidth =
                projectionWidth *
                BlockAtlasSettings.TileWidth;

            int textureHeight =
                projectionHeight *
                BlockAtlasSettings.TileHeight;

            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);

            foreach (var (
                        chunk,
                        projectedCells,
                        entity)
                    in SystemAPI
                        .Query<
                            RefRO<ChunkComponent>,
                            DynamicBuffer<ProjectedCellData>>()
                        .WithAll<ChunkNeedsRender>()
                        .WithEntityAccess())
            {
                int2 chunkCoordinate =
                    chunk.ValueRO.Coordinate;

                ChunkRenderObject renderObject =
                    chunkRenderManager.GetOrCreate(
                        chunkCoordinate,
                        textureWidth,
                        textureHeight);

                NativeArray<ProjectedCellData> cells =
                    projectedCells.AsNativeArray();

                worldRenderer.Render(
                    cells,
                    renderObject.RenderTexture);

                ecb.SetComponentEnabled<ChunkNeedsRender>(
                    entity,
                    false);
            }

            ecb.Playback(
                EntityManager);

            ecb.Dispose();
        }
    }
}