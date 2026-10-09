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
        public EquipmentSlotData[] Equipment;
        public WorldItemData[] WorldItems;
    }

    [Serializable]
    public sealed class WorldItemData
    {
        public Game.Items.ItemInstance Instance;
        public float X, Y, Z, Vx, Vy, Vz;
        public float Gravity, CollisionRadius;
        public bool Grounded;
    }

    [Serializable]
    public sealed class EquipmentSlotData
    {
        public int Slot;
        public int ItemId;
        public bool HasDurability;
        public float Durability;
        public float MaxDurability;
        public float DurabilityElapsed;
        public int PlacementSelectionKind;
        public int PlacementSelectionId;
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
