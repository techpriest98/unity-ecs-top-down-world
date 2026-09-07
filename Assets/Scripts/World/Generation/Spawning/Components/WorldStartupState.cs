using Unity.Entities;

namespace Game.World.Generation.Spawning
{
    public enum WorldStartupPhase : byte
    {
        SearchingSpawn,
        PreparingView,
        Ready
    }

    public struct WorldStartupState : IComponentData
    {
        public WorldStartupPhase Phase;
    }
}