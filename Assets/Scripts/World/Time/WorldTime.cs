using Unity.Entities;

namespace Game.World.Time
{
    public struct WorldTime : IComponentData
    {
        public int Hour;
        public float UpdateTimer;
    }
}