using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Chunks
{
    public sealed class ChunkAuthoring :
        MonoBehaviour
    {
        [SerializeField]
        [Min(0)]
        private int chunkRadius = 1;

        [SerializeField]
        private Vector2Int initialCenterChunk =
            Vector2Int.zero;

        private sealed class Baker :
            Baker<ChunkAuthoring>
        {
            public override void Bake(
                ChunkAuthoring authoring)
            {
                Entity entity =
                    GetEntity(
                        TransformUsageFlags.None);

                AddComponent(
                    entity,
                    new ChunkStreamingSettings
                    {
                        LoadRadius =
                            authoring.chunkRadius
                    });

                AddComponent(
                    entity,
                    new ChunkStreamingCenter
                    {
                        Coordinate =
                            new int2(
                                authoring
                                    .initialCenterChunk
                                    .x,
                                authoring
                                    .initialCenterChunk
                                    .y)
                    });
            }
        }
    }
}