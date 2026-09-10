using System;

namespace Game.World.Saving
{
    [Serializable]
    public sealed class WorldStateData
    {
        public int Version;
        public string WorldId;
        public uint Seed;
        public float X, Y, Z;
        public int Facing;
        public int View;
        public int Hour;
        public float UpdateTimer;
        public float VerticalVelocity;
    }
}
