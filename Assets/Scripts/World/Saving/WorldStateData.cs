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
        public int PlacementMode;
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
        // Absent in versions 1–4: JSON's default zero means Full.
        public int Shape;
        // Version 6: optional independent halves. Legacy fields are ignored when present.
        public CellHalvesData Halves;
        public bool HasHalves;

        // Unity can instantiate inline serializable classes even when their
        // field is absent/null in JSON. Do not infer presence from null alone.
        // Nonzero payload supports v6-v7 saves written before the discriminator.
        public bool HasSplitContents => HasHalves || (Halves != null &&
            (Halves.LowerId != 0 || Halves.LowerDurability != 0 || Halves.UpperId != 0 || Halves.UpperDurability != 0));
    }

    [Serializable]
    public sealed class CellHalvesData
    {
        public int LowerId, LowerDurability;
        public int UpperId, UpperDurability;
    }
}
