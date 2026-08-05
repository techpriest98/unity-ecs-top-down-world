using Unity.Entities;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class ChunkRenderResourcesAuthoring :
        MonoBehaviour
    {
        [SerializeField]
        private Mesh chunkMesh;

        [SerializeField]
        private Material chunkMaterial;

        private sealed class Baker :
            Baker<ChunkRenderResourcesAuthoring>
        {
            public override void Bake(
                ChunkRenderResourcesAuthoring authoring)
            {
                Entity entity =
                    GetEntity(
                        TransformUsageFlags.None);

                AddComponentObject(
                    entity,
                    new ChunkRenderResources
                    {
                        Mesh =
                            authoring.chunkMesh,

                        Material =
                            authoring.chunkMaterial
                    });
            }
        }
    }
}