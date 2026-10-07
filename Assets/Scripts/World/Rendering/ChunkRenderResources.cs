using Unity.Entities;
using UnityEngine;

namespace Game.World.Rendering
{
    public struct ChunkRenderResources :
        IComponentData
    {
        public UnityObjectRef<Mesh> Mesh;
        public UnityObjectRef<Material> Material;
    }
}
