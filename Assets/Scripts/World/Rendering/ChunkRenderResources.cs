using Unity.Entities;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class ChunkRenderResources :
        IComponentData
    {
        public Mesh Mesh;
        public Material Material;
    }
}