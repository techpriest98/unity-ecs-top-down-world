using Unity.Entities;

namespace Game.World.Time
{
    public struct WorldTime : IComponentData
    {
        public int Hour;
        public float UpdateTimer;
        public double SimulationSeconds;
        // Scaled world time advanced this frame; movement/physics use ECS delta instead.
        public float DeltaSeconds;
    }
}
