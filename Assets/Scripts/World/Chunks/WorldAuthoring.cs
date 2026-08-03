using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Chunks
{
    public sealed class WorldAuthoring : MonoBehaviour
    {
        [SerializeField]
        [Min(0)]
        private int chunkRadius = 1;

        private sealed class Baker :
            Baker<WorldAuthoring>
        {
            public override void Bake(
                WorldAuthoring authoring)
            {
                for (int chunkZ = -authoring.chunkRadius;
                     chunkZ <= authoring.chunkRadius;
                     chunkZ++)
                {
                    for (int chunkX = -authoring.chunkRadius;
                         chunkX <= authoring.chunkRadius;
                         chunkX++)
                    {
                        CreateChunk(
                            chunkX,
                            chunkZ);
                    }
                }
            }

            private void CreateChunk(
                int chunkX,
                int chunkZ)
            {
                Entity chunkEntity =
                    CreateAdditionalEntity(
                        TransformUsageFlags.None);

                AddComponent(
                    chunkEntity,
                    new ChunkComponent
                    {
                        Coordinate =
                            new int2(
                                chunkX,
                                chunkZ)
                    });

                DynamicBuffer<BlockData> blocks =
                    AddBuffer<BlockData>(
                        chunkEntity);

                blocks.ResizeUninitialized(
                    ChunkSettings.BlockCount);

                AddBuffer<ProjectedCellData>(
                    chunkEntity);

                AddComponent<ChunkNeedsProjection>(
                    chunkEntity);

                SetComponentEnabled<ChunkNeedsProjection>(
                    chunkEntity,
                    false);

                AddComponent<ChunkNeedsRender>(
                    chunkEntity);

                SetComponentEnabled<ChunkNeedsRender>(
                    chunkEntity,
                    false);
            }
        }
    }
}