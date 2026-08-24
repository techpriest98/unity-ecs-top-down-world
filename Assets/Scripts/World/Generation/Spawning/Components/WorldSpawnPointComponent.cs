using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Generation.Spawning
{
    public struct WorldSpawnPointComponent :
        IComponentData,
        IEnableableComponent
    {
        public int3 Position;
    }
}