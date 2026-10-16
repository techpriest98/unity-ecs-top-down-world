using System;
using Game.Items;
using System.Runtime.InteropServices;
using Unity.Entities;

namespace Game.World.Blocks
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BlockPart
    {
        public BlockId Id;
        public byte Durability;

        public BlockPart(BlockId id, byte durability)
        {
            Id = id;
            Durability = id == BlockId.Air ? (byte)0 : durability;
        }
    }

    public struct CellContents
    {
        public BlockPart Lower;
        public BlockPart Upper;

        public CellContents(BlockPart lower, BlockPart upper)
        {
            Lower = new BlockPart(lower.Id, lower.Durability);
            Upper = new BlockPart(upper.Id, upper.Durability);
        }

        public bool IsUniform => Lower.Id == Upper.Id && Lower.Durability == Upper.Durability;
        // Different durability alone does not make a continuous material a half block.
        public BlockPlacementMode PlacementMode => Lower.Id == Upper.Id ? BlockPlacementMode.Full : BlockPlacementMode.Half;

        public BlockData GeometryView()
        {
            bool lowerSolid = BlockUtility.IsSolid(Lower.Id) && Lower.Id != BlockId.OceanWater && Lower.Id != BlockId.RiverWater;
            bool upperSolid = BlockUtility.IsSolid(Upper.Id) && Upper.Id != BlockId.OceanWater && Upper.Id != BlockId.RiverWater;
            if (lowerSolid && !upperSolid) return new BlockData(Lower.Id, Lower.Durability, BlockShape.LowerHalf);
            if (upperSolid && !lowerSolid) return new BlockData(Upper.Id, Upper.Durability, BlockShape.UpperHalf);
            if (upperSolid) return new BlockData(Upper.Id, Upper.Durability);
            if (Upper.Id != BlockId.Air) return new BlockData(Upper.Id, Upper.Durability,
                Lower.Id == BlockId.Air ? BlockShape.UpperHalf : BlockShape.Full);
            return new BlockData(Lower.Id, Lower.Durability, BlockShape.LowerHalf);
        }

        public static CellContents FromLegacy(BlockData value)
        {
            if (SplitCellUtility.IsSplit(value))
                throw new ArgumentException("A split reference requires its chunk buffer.");
            var lower = new BlockPart(value.BlockId, value.Durability);
            if (value.Shape == BlockShape.UpperHalf)
                return new CellContents(new BlockPart(BlockId.Air, 0), lower);
            return new CellContents(lower, value.Shape == BlockShape.LowerHalf
                ? new BlockPart(BlockId.Air, 0) : lower);
        }
    }

    [InternalBufferCapacity(0)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SplitCellData : IBufferElementData
    {
        public ushort CellIndex;
        public BlockPart Lower;
        public BlockPart Upper;
    }

    // Callers must resolve references before interpreting material/durability.
    // GeometryView is a compatibility view, not a replacement for per-part reads.
    public static class BlockPlacementUtility
    {
        // Plan the entire volume before enqueuing either cell. Untouched halves
        // retain their material and durability, including water.
        public static bool TryFill(BlockPart material, BlockPlacementMode mode, int startHalf,
            CellContents first, CellContents second, bool secondAvailable,
            out CellContents resultFirst, out CellContents resultSecond, out bool spills)
        {
            resultFirst = first;
            resultSecond = second;
            spills = false;
            if ((mode != BlockPlacementMode.Full && mode != BlockPlacementMode.Half) ||
                startHalf < 0 || startHalf > 1 || material.Id == BlockId.Air ||
                !SplitCellUtility.IsKnownMaterial(material.Id)) return false;
            int count = mode == BlockPlacementMode.Half ? 1 : 2;
            bool crossesCell = startHalf + count > 2;
            if (crossesCell && !secondAvailable) return false;
            for (int i = 0; i < count; i++)
            {
                int half = startHalf + i;
                BlockPart existing = half == 0 ? first.Lower : half == 1 ? first.Upper : second.Lower;
                if (existing.Id != BlockId.Air && existing.Id != BlockId.OceanWater && existing.Id != BlockId.RiverWater)
                    return false;
            }
            for (int i = 0; i < count; i++)
            {
                int half = startHalf + i;
                if (half == 0) resultFirst.Lower = material;
                else if (half == 1) resultFirst.Upper = material;
                else resultSecond.Lower = material;
            }
            spills = crossesCell;
            return true;
        }
    }

    public static class SplitCellUtility
    {
        public static bool IsKnownMaterial(BlockId id) => id == BlockId.Air || id == BlockId.Dirt ||
            id == BlockId.Stone || id == BlockId.Sand || id == BlockId.OceanWater || id == BlockId.RiverWater ||
            id == BlockId.Snow || id == BlockId.PlainsGrass || id == BlockId.PlainsHighGrass ||
            id == BlockId.MeadowGrass || id == BlockId.MeadowHighGrass || id == BlockId.DarkForestGrass;
        public const BlockShape ReferenceShape = (BlockShape)255;
        public static bool IsSplit(BlockData value) => value.Shape == ReferenceShape;
        public static int GetReferenceIndex(BlockData value) => (byte)value.BlockId | (value.Durability << 8);

        private static BlockData Reference(int index) => new BlockData
        {
            BlockId = (BlockId)(byte)index,
            Durability = (byte)(index >> 8),
            Shape = ReferenceShape
        };

        public static CellContents Get(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex)
        {
            BlockData value = blocks[cellIndex];
            if (!IsSplit(value)) return CellContents.FromLegacy(value);
            SplitCellData split = splits[GetReferenceIndex(value)];
            return new CellContents(split.Lower, split.Upper);
        }

        public static void Set(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex, CellContents contents)
        {
            // Validate before changing either buffer.
            if ((uint)cellIndex >= blocks.Length || cellIndex > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(cellIndex));
            contents = new CellContents(contents.Lower, contents.Upper);
            if (contents.IsUniform)
            {
                Remove(blocks, splits, cellIndex);
                blocks[cellIndex] = new BlockData(contents.Lower.Id, contents.Lower.Durability);
                return;
            }

            bool existing = IsSplit(blocks[cellIndex]);
            int index = existing ? GetReferenceIndex(blocks[cellIndex]) : splits.Length;
            var split = new SplitCellData
            {
                CellIndex = (ushort)cellIndex, Lower = contents.Lower, Upper = contents.Upper
            };
            if (existing) splits[index] = split;
            else
            {
                if (index > ushort.MaxValue) throw new InvalidOperationException("Split cell buffer is full.");
                splits.Add(split);
            }
            blocks[cellIndex] = Reference(index);
        }

        public static void SetLegacy(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex, BlockData value)
        {
            if (IsSplit(value)) throw new ArgumentException("Cannot copy a chunk-local split reference.");
            Remove(blocks, splits, cellIndex);
            blocks[cellIndex] = value;
        }

        private static void Remove(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex)
        {
            if (!IsSplit(blocks[cellIndex])) return;
            int index = GetReferenceIndex(blocks[cellIndex]);
            int last = splits.Length - 1;
            SplitCellData moved = splits[last];
            splits.RemoveAtSwapBack(index);
            if (index != last) blocks[moved.CellIndex] = Reference(index);
        }
    }
}
