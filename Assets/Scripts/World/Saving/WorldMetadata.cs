using System;

namespace Game.World.Saving
{
    [Serializable]
    public sealed class WorldMetadata
    {
        public int SaveVersion;
        public string Id;
        public string Name;
        public uint Seed;
        public string CreatedAtUtc;
        public string LastPlayedAtUtc;
    }
}
