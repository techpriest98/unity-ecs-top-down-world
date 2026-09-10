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
        public int ChunkSizeX, ChunkSizeY, ChunkSizeZ;
        public ChunkChangesData[] Chunks;
    }

    [Serializable]
    public sealed class ChunkChangesData
    {
        public int X, Z;
        public BlockChangeData[] Blocks;
    }

    [Serializable]
    public sealed class BlockChangeData
    {
        public int Index;
        public int BlockId;
        public int Durability;
    }
}
