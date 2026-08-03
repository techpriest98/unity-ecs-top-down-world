using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Chunks
{
    public sealed class ChunkAuthoring : MonoBehaviour
    {
        public int2 Coordinate;

        public BlockId DefaultBlockId = BlockId.Air;
        public byte DefaultDurability = byte.MaxValue;

        private sealed class Baker : Baker<ChunkAuthoring>
        {
            public override void Bake(ChunkAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new ChunkComponent
                {
                    Coordinate = authoring.Coordinate
                });
                AddComponent<ChunkNeedsProjection>(entity);
                AddComponent<ChunkNeedsRender>(entity);


                DynamicBuffer<BlockData> blocks = AddBuffer<BlockData>(entity);
                AddBuffer<ProjectedCellData>(entity);

                blocks.ResizeUninitialized(ChunkSettings.BlockCount);

                BlockData defaultBlock = new BlockData(
                    authoring.DefaultBlockId,
                    authoring.DefaultDurability
                );

                for (int i = 0; i < ChunkSettings.BlockCount; i++)
                {
                    blocks[i] = defaultBlock;
                }
            }
        }
    }
}